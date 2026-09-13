using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Soundswap.Core.Audio;

/// <summary>Decoded audio: one float array per channel, all the same length, at one sample rate.</summary>
public sealed class AudioClip
{
    public float[][] Channels { get; }
    public int SampleRate { get; }

    /// <summary>Tags from the file (Vorbis comments), keys upper-cased. Loop tags come from here.</summary>
    public IReadOnlyDictionary<string, string> Tags { get; }

    public AudioClip(float[][] channels, int sampleRate, IReadOnlyDictionary<string, string>? tags = null)
    {
        if (channels.Length == 0) throw new ArgumentException("A clip needs at least one channel.", nameof(channels));
        if (channels.Any(c => c.Length != channels[0].Length)) throw new ArgumentException("Channels must be the same length.", nameof(channels));
        Channels = channels;
        SampleRate = sampleRate;
        Tags = tags ?? new Dictionary<string, string>();
    }

    public int ChannelCount => Channels.Length;
    public int Frames => Channels[0].Length;
    public TimeSpan Duration => TimeSpan.FromSeconds((double)Frames / SampleRate);

    public static AudioClip Silence(int channels, int frames, int sampleRate) =>
        new(Enumerable.Range(0, channels).Select(_ => new float[frames]).ToArray(), sampleRate);

    public static AudioClip FromInterleaved(ReadOnlySpan<float> data, int channels, int sampleRate, IReadOnlyDictionary<string, string>? tags = null)
    {
        var frames = data.Length / channels;
        var planar = new float[channels][];
        for (var c = 0; c < channels; c++) planar[c] = new float[frames];
        for (var f = 0; f < frames; f++)
        {
            for (var c = 0; c < channels; c++) planar[c][f] = data[f * channels + c];
        }
        return new AudioClip(planar, sampleRate, tags);
    }

    /// <summary>
    /// Two channels: mono is copied to both sides; more than two are folded down, the extra channels mixed into
    /// both sides 3 dB lower, the way a surround file sounds on headphones.
    /// </summary>
    public AudioClip ToStereo()
    {
        if (ChannelCount == 2) return this;
        if (ChannelCount == 1) return new AudioClip([Channels[0], (float[])Channels[0].Clone()], SampleRate, Tags);

        var left = (float[])Channels[0].Clone();
        var right = (float[])Channels[1].Clone();
        const float extra = 0.7071f;
        for (var c = 2; c < ChannelCount; c++)
        {
            var src = Channels[c];
            for (var i = 0; i < Frames; i++)
            {
                left[i] += src[i] * extra;
                right[i] += src[i] * extra;
            }
        }
        // Each side gets its own channel plus every extra one at -3 dB: scale by that sum so it can't clip.
        var scale = 1f / (1f + extra * (ChannelCount - 2));
        for (var i = 0; i < Frames; i++)
        {
            left[i] *= scale;
            right[i] *= scale;
        }
        return new AudioClip([left, right], SampleRate, Tags);
    }

    /// <summary>The channels <paramref name="first"/> and <paramref name="first"/>+1 as their own stereo clip.</summary>
    public AudioClip Pair(int first) => new([Channels[first], Channels[Math.Min(first + 1, ChannelCount - 1)]], SampleRate, Tags);

    /// <summary>Converts to <paramref name="rate"/> with NAudio's WDL resampler. Returns this clip when it already matches.</summary>
    public AudioClip Resample(int rate)
    {
        if (rate == SampleRate) return this;
        var resampler = new WdlResamplingSampleProvider(new ClipSampleProvider(this), rate);
        return AudioReader.ReadAll(resampler, CancellationToken.None, Tags);
    }

    /// <summary>The part from <paramref name="start"/> to <paramref name="end"/> (null: to the end), in seconds.</summary>
    public AudioClip Slice(double start, double? end)
    {
        var from = (int)Math.Clamp(Math.Round(start * SampleRate), 0, Frames);
        var to = end is { } e ? (int)Math.Clamp(Math.Round(e * SampleRate), from, Frames) : Frames;
        if (from == 0 && to == Frames) return this;
        return new AudioClip(Channels.Select(c => c[from..to]).ToArray(), SampleRate, Tags);
    }

    public float Peak()
    {
        var peak = 0f;
        foreach (var c in Channels)
        {
            foreach (var s in c) peak = Math.Max(peak, Math.Abs(s));
        }
        return peak;
    }

    /// <summary>Loudest sample per bucket (0..1), for drawing a waveform.</summary>
    public float[] Envelope(int buckets)
    {
        var result = new float[buckets];
        if (Frames == 0) return result;
        var per = Math.Max(1, Frames / buckets);
        for (var b = 0; b < buckets; b++)
        {
            var from = (int)((long)b * Frames / buckets);
            var to = Math.Min(Frames, from + per);
            var peak = 0f;
            foreach (var c in Channels)
            {
                for (var i = from; i < to; i++) peak = Math.Max(peak, Math.Abs(c[i]));
            }
            result[b] = Math.Min(1f, peak);
        }
        return result;
    }
}

/// <summary>Plays a clip into NAudio's sample pipeline (for resampling and previews).</summary>
public sealed class ClipSampleProvider(AudioClip clip) : ISampleProvider
{
    private long position;

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(clip.SampleRate, clip.ChannelCount);

    /// <summary>Current frame.</summary>
    public long Position
    {
        get => Interlocked.Read(ref position);
        set => Interlocked.Exchange(ref position, Math.Clamp(value, 0, clip.Frames));
    }

    public int Read(Span<float> buffer)
    {
        var channels = clip.ChannelCount;
        var frames = buffer.Length / channels;
        var at = Position;
        var n = (int)Math.Min(frames, clip.Frames - at);
        for (var f = 0; f < n; f++)
        {
            for (var c = 0; c < channels; c++) buffer[f * channels + c] = clip.Channels[c][at + f];
        }
        Position = at + n;
        return n * channels;
    }

    public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
}
