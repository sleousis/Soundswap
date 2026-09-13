using System.Collections.Concurrent;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Soundswap.Core.Audio;
using Soundswap.Core.Project;

namespace Soundswap.Audio;

/// <summary>
/// Windows' own decoders through Media Foundation: M4A, AAC, MP4 and MOV audio, WMA, FLAC, and anything else a
/// codec pack adds. Tried after the managed decoders, so WAV, OGG and MP3 never depend on it.
/// </summary>
public sealed class MediaFoundationDecoder : IAudioDecoder
{
    public IReadOnlyCollection<string> Extensions { get; } = [".m4a", ".mp4", ".m4v", ".mov", ".aac", ".wma", ".flac", ".3gp", ".adts", ".mp3", ".wav"];

    public AudioClip Decode(string path, CancellationToken ct)
    {
        using var reader = new MediaFoundationReader(path);
        return AudioReader.ReadAll(reader.ToSampleProvider(), ct);
    }
}

public static class AudioFiles
{
    /// <summary>What the file picker offers.</summary>
    public const string DialogFilter = "Audio{.mp3,.wav,.ogg,.flac,.m4a,.aac,.mp4,.wma,.aif,.aiff,.opus,.webm},All files{.*}";

    public static readonly IAudioDecoder Decoders = new AudioDecoderChain([new ManagedAudioDecoder(), new MediaFoundationDecoder()]);

    public static bool LooksLikeAudio(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".mp3" or ".wav" or ".wave" or ".ogg" or ".oga" or ".flac" or ".m4a" or ".aac" or ".mp4" or ".m4v"
            or ".mov" or ".wma" or ".aif" or ".aiff" or ".opus" or ".webm" or ".3gp" or ".adts";
}

/// <summary>
/// Plays previews: a song file, the game's own music, or a loop seam. Only one thing plays at a time.
/// <para>
/// The game must never wait on audio. An earlier version started and stopped WASAPI under a lock the window also
/// took, while the device's "stopped" callback wanted the same lock: the game's frame thread waited forever and
/// the game froze. Now every device call runs on one "Soundswap audio" thread; the window only queues requests
/// and reads plain fields, and nothing here takes a lock the audio callbacks need.
/// </para>
/// </summary>
public sealed class PreviewPlayer : IDisposable
{
    private readonly BlockingCollection<Action> commands = new();
    private readonly Thread thread;
    private Device? output; // audio thread only
    private volatile LoopingProvider? source;
    private volatile string? playing;
    private float trackGain = 1f;

    public PreviewPlayer()
    {
        thread = new Thread(Run) { IsBackground = true, Name = "Soundswap audio" };
        thread.Start();
    }

    /// <summary>Raised on the audio thread when a device call fails, with a message for the window.</summary>
    public event Action<string>? Failed;

    /// <summary>What is playing, by the key the caller gave it; null when nothing is.</summary>
    public string? Playing => playing;

    public double Position => source?.Seconds ?? 0;
    public double Duration => source?.Clip.Duration.TotalSeconds ?? 0;
    public LoopSeconds? Loop => source?.Loop;

    public float Volume { get; private set; } = 0.8f;

    public bool IsPlaying(string key) => playing == key;

    /// <param name="clip">Folded to stereo for listening.</param>
    /// <param name="gain">On top of the preview volume: the volume change the build would make.</param>
    public void Play(string key, AudioClip clip, double start = 0, LoopSeconds? loop = null, float gain = 1f)
    {
        var next = new LoopingProvider(clip.ToStereo(), loop) { Seconds = start, Gain = Volume * gain };
        trackGain = gain;
        source = next;
        playing = key;
        Post(() =>
        {
            StopDevice();
            if (!ReferenceEquals(source, next)) return; // stopped or replaced while waiting
            // NAudio 3's WaveOut (formerly WaveOutEvent): its own playback thread, no window messages needed.
            var device = new Device(new WaveOut { BufferMilliseconds = 70, NumberOfBuffers = 3 });
            device.Out.PlaybackStopped += (_, e) =>
            {
                // No locks here: this runs on the device's own thread.
                if (ReferenceEquals(source, next)) playing = null;
                // Stopping a device mid-buffer makes waveOutWrite complain; that is not a failure anyone needs to see.
                if (e.Exception is { } ex && !device.Stopping) Failed?.Invoke(ex.Message);
                device.Stopped.Set();
            };
            device.Out.Init(new SampleToWaveProvider16(next));
            output = device;
            device.Out.Play();
        });
    }

    public void Seek(double seconds)
    {
        if (source is { } s) s.Seconds = seconds;
    }

    public void SetVolume(float volume)
    {
        Volume = volume;
        if (source is { } s) s.Gain = volume * trackGain;
    }

    /// <summary>Returns at once; the device is stopped on the audio thread.</summary>
    public void Stop()
    {
        playing = null;
        source = null;
        Post(StopDevice);
    }

    private void StopDevice()
    {
        var device = output;
        output = null;
        if (device is null) return;
        device.Stopping = true;
        try { device.Out.Stop(); } catch (Exception) { /* already stopped */ }
        // WaveOut's own thread may still be handing buffers to Windows; closing under it fails those writes. Wait
        // for it to finish (here, on the audio thread, never the game's) before letting go of the device.
        device.Stopped.Wait(TimeSpan.FromSeconds(1));
        try { device.Out.Dispose(); } catch (Exception) { /* nothing left to release */ }
    }

    private sealed class Device(WaveOut output)
    {
        public WaveOut Out { get; } = output;
        public ManualResetEventSlim Stopped { get; } = new();
        public volatile bool Stopping;
    }

    private void Post(Action action)
    {
        try
        {
            if (!commands.IsAddingCompleted) commands.Add(action);
        }
        catch (InvalidOperationException)
        {
            // disposed
        }
    }

    private void Run()
    {
        foreach (var command in commands.GetConsumingEnumerable())
        {
            try
            {
                command();
            }
            catch (Exception ex)
            {
                playing = null;
                source = null;
                try { Failed?.Invoke(ex.Message); } catch (Exception) { }
            }
        }
        StopDevice();
    }

    public void Dispose()
    {
        Stop();
        commands.CompleteAdding();
        thread.Join(TimeSpan.FromSeconds(2));
    }

    /// <summary>A clip as a sample stream that jumps from the loop's end to its start, like the game does.</summary>
    private sealed class LoopingProvider(AudioClip clip, LoopSeconds? loop) : ISampleProvider
    {
        private long frame;

        public AudioClip Clip { get; } = clip;
        public LoopSeconds? Loop { get; } = loop;
        public float Gain { get; set; } = 1f;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(clip.SampleRate, 2);

        public double Seconds
        {
            get => (double)Interlocked.Read(ref frame) / Clip.SampleRate;
            set => Interlocked.Exchange(ref frame, Math.Clamp((long)(value * Clip.SampleRate), 0, Clip.Frames));
        }

        public int Read(Span<float> buffer)
        {
            var frames = buffer.Length / 2;
            var at = Interlocked.Read(ref frame);
            long loopStart = -1, loopEnd = Clip.Frames;
            if (Loop is { } l)
            {
                loopStart = (long)(l.Start * Clip.SampleRate);
                loopEnd = Math.Min(Clip.Frames, (long)(l.End * Clip.SampleRate));
            }
            var written = 0;
            var gain = Gain;
            while (written < frames)
            {
                if (at >= loopEnd)
                {
                    if (loopStart < 0 || loopEnd - loopStart < Clip.SampleRate / 10) break;
                    at = loopStart;
                }
                var n = (int)Math.Min(frames - written, loopEnd - at);
                var left = Clip.Channels[0];
                var right = Clip.Channels[1];
                for (var i = 0; i < n; i++)
                {
                    buffer[(written + i) * 2] = Math.Clamp(left[at + i] * gain, -1f, 1f);
                    buffer[(written + i) * 2 + 1] = Math.Clamp(right[at + i] * gain, -1f, 1f);
                }
                written += n;
                at += n;
            }
            Interlocked.Exchange(ref frame, at);
            return written * 2;
        }

        public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    }
}
