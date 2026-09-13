namespace Soundswap.Core.Audio;

/// <summary>An encoded Ogg Vorbis stream split the way the game stores it: header pages, then audio pages.</summary>
public sealed record EncodedVorbis(byte[] Header, byte[] Audio, int Channels, int SampleRate, long TotalSamples);

/// <summary>
/// Encodes planar float audio to Ogg Vorbis: with the reference libvorbis when soundswap_vorbis.dll loads (any
/// channel count, fast), otherwise with a managed port that only handles stereo.
/// </summary>
public static class VorbisEncoder
{
    /// <summary>1024-frame blocks per forced page: about 0.09 s at 44.1 kHz, so loop points stay precise.</summary>
    internal const int PageEveryBlocks = 4;

    /// <summary>
    /// Tells the encoder which folder soundswap_vorbis.dll ships in. The plugin must call this at start-up:
    /// Dalamud loads plugin assemblies from a stream, so they can't find files next to themselves on their own.
    /// </summary>
    public static void UseLibraryFolder(string directory) => NativeVorbis.UseFolder(directory);

    public static bool NativeAvailable => NativeVorbis.Available;

    /// <summary>One line for logs and the settings page: which encoder is in use, and why when it's the fallback.</summary>
    public static string Status => NativeVorbis.Available
        ? "libvorbis 1.3.7 (every song, layered ones included)"
        : $"the managed fallback, which handles stereo songs only ({NativeVorbis.LoadError})";

    /// <summary>Why the native encoder isn't there, when it isn't.</summary>
    public static string? NativeProblem => NativeVorbis.LoadError;

    /// <param name="quality">Vorbis quality from 0 (small) to 1 (best). 0.5 is about 160 kbit/s for stereo.</param>
    /// <param name="progress">Called with 0..1 as the encoder moves through the audio.</param>
    public static EncodedVorbis Encode(float[][] channels, int sampleRate, float quality, IReadOnlyDictionary<string, string>? tags,
        Action<float>? progress, CancellationToken ct)
    {
        if (channels.Length == 0 || channels[0].Length == 0) throw new ArgumentException("Nothing to encode.", nameof(channels));
        quality = Math.Clamp(quality, 0f, 1f);
        if (NativeVorbis.Available) return NativeVorbis.Encode(channels, sampleRate, quality, tags, progress, ct);
        if (channels.Length == 2) return ManagedVorbis.Encode(channels, sampleRate, quality, tags, progress, ct);
        throw new NotSupportedException(
            $"Layered songs ({channels.Length} channels) need Soundswap's Vorbis encoder, which didn't load: {NativeVorbis.LoadError}. Reinstalling the plugin should bring it back.");
    }
}
