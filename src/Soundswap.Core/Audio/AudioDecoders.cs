using NAudio.Wave;
using NLayer.NAudioSupport;
using NVorbis;

namespace Soundswap.Core.Audio;

/// <summary>Turns an audio file into an <see cref="AudioClip"/>.</summary>
public interface IAudioDecoder
{
    /// <summary>Extensions with the dot, lower case.</summary>
    IReadOnlyCollection<string> Extensions { get; }

    AudioClip Decode(string path, CancellationToken ct);
}

/// <summary>
/// WAV, AIFF, Ogg Vorbis and MP3 in managed code, so they work on every system the game runs on. The plugin
/// adds Windows' own decoders (Media Foundation) for everything else, such as M4A, AAC, MP4, WMA and FLAC.
/// </summary>
public sealed class ManagedAudioDecoder : IAudioDecoder
{
    public IReadOnlyCollection<string> Extensions { get; } = [".wav", ".wave", ".aif", ".aiff", ".ogg", ".oga", ".mp3"];

    public AudioClip Decode(string path, CancellationToken ct)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".ogg" or ".oga":
                return AudioReader.DecodeOgg(File.ReadAllBytes(path), ct);
            case ".mp3":
                using (var mp3 = new Mp3FileReaderBase(path, format => new Mp3FrameDecompressor(format)))
                    return AudioReader.ReadAll(mp3.ToSampleProvider(), ct);
            case ".aif" or ".aiff":
                using (var aiff = new AiffFileReader(path))
                    return AudioReader.ReadAll(aiff.ToSampleProvider(), ct);
            default:
                using (var wav = new WaveFileReader(path))
                    return AudioReader.ReadAll(wav.ToSampleProvider(), ct);
        }
    }
}

/// <summary>Tries each decoder that claims the file's extension, then every other one, and explains a failure.</summary>
public sealed class AudioDecoderChain(IReadOnlyList<IAudioDecoder> decoders) : IAudioDecoder
{
    public IReadOnlyCollection<string> Extensions { get; } = decoders.SelectMany(d => d.Extensions).Distinct().ToList();

    public AudioClip Decode(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"The file {Path.GetFileName(path)} isn't there any more.", path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var ordered = decoders.Where(d => d.Extensions.Contains(ext)).Concat(decoders.Where(d => !d.Extensions.Contains(ext)));
        Exception? last = null;
        foreach (var decoder in ordered)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var clip = decoder.Decode(path, ct);
                if (clip.Frames == 0) throw new InvalidDataException("The file has no audio in it.");
                return clip;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { last = ex; }
        }
        throw new InvalidDataException($"Soundswap couldn't read {Path.GetFileName(path)}: {last?.Message ?? "no decoder for this kind of file"}", last);
    }
}

public static class AudioReader
{
    /// <summary>Songs longer than this are refused before they fill memory.</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(30);

    public static AudioClip ReadAll(ISampleProvider provider, CancellationToken ct, IReadOnlyDictionary<string, string>? tags = null)
    {
        var channels = provider.WaveFormat.Channels;
        var rate = provider.WaveFormat.SampleRate;
        var limit = (long)(MaxDuration.TotalSeconds * rate * channels);
        var chunk = new float[rate * channels];
        var all = new List<float[]>();
        long total = 0;
        int read;
        while ((read = provider.Read(chunk.AsSpan())) > 0)
        {
            ct.ThrowIfCancellationRequested();
            all.Add(chunk[..read]);
            total += read;
            if (total > limit) throw new InvalidDataException($"The song is longer than {MaxDuration.TotalMinutes:0} minutes.");
        }
        var data = new float[total];
        var at = 0;
        foreach (var part in all)
        {
            part.CopyTo(data, at);
            at += part.Length;
        }
        return AudioClip.FromInterleaved(data, channels, rate, tags);
    }

    public static AudioClip ReadVorbis(VorbisReader reader, CancellationToken ct)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var (key, values) in reader.Tags.All)
            {
                if (values.Count > 0) tags[key.ToUpperInvariant()] = values[0];
            }
        }
        catch (Exception)
        {
            // Tags are a nicety; a file with odd comments still plays.
        }

        var channels = reader.Channels;
        var rate = reader.SampleRate;
        var limit = (long)(MaxDuration.TotalSeconds * rate * channels);
        var chunk = new float[rate * channels];
        var all = new List<float[]>();
        long total = 0;
        int read;
        while ((read = reader.ReadSamples(chunk, 0, chunk.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            all.Add(chunk[..read]);
            total += read;
            if (total > limit) throw new InvalidDataException($"The song is longer than {MaxDuration.TotalMinutes:0} minutes.");
        }
        var data = new float[total];
        var at = 0;
        foreach (var part in all)
        {
            part.CopyTo(data, at);
            at += part.Length;
        }
        return AudioClip.FromInterleaved(data, channels, rate, tags);
    }

    /// <summary>
    /// Decodes an in-memory Ogg Vorbis stream, such as the game's own music after de-obscuring. Uses the reference
    /// decoder when soundswap_vorbis.dll is there (right for every channel layout) and NVorbis otherwise. Tags
    /// (loop points) are read with NVorbis either way: they sit in the header, which it reads well.
    /// </summary>
    public static AudioClip DecodeOgg(byte[] ogg, CancellationToken ct)
    {
        if (!NativeVorbis.Available)
        {
            using var reader = new VorbisReader(new MemoryStream(ogg), true);
            return ReadVorbis(reader, ct);
        }
        return NativeVorbis.Decode(ogg, ReadTags(ogg), ct);
    }

    private static IReadOnlyDictionary<string, string> ReadTags(byte[] ogg)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var reader = new VorbisReader(new MemoryStream(ogg), true);
            foreach (var (key, values) in reader.Tags.All)
            {
                if (values.Count > 0) tags[key.ToUpperInvariant()] = values[0];
            }
        }
        catch (Exception)
        {
            // Tags are a nicety; a file whose header NVorbis dislikes still plays.
        }
        return tags;
    }
}
