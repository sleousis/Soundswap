using System.Reflection;
using System.Runtime.InteropServices;

namespace Soundswap.Core.Audio;

/// <summary>
/// The reference libvorbis, through soundswap_vorbis.dll (native/soundswap_vorbis.c, built by tools/build-vorbis.cmd).
/// It ships next to Soundswap.Core.dll; plugins live in their own load context, so the DLL is found by path.
/// </summary>
internal static unsafe class NativeVorbis
{
    public const string LibraryName = "soundswap_vorbis";

    private static readonly Lazy<(bool Ok, string? Error)> Loaded = new(Load);

    static NativeVorbis()
    {
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(NativeVorbis).Assembly, Resolve);
        }
        catch (InvalidOperationException)
        {
            // A resolver was already set for this assembly; the default search still applies.
        }
    }

    public static bool Available => Loaded.Value.Ok;
    public static string? LoadError => Loaded.Value.Error;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WriteFn(byte* data, int length, int isHeader, IntPtr user);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ProgressFn(float fraction, IntPtr user);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PcmFn(float** pcm, int channels, int frames, IntPtr user);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sw_version();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sw_decode(byte* data, long length, out int channels, out int rate, out long totalFrames, PcmFn pcm, IntPtr user);

    /// <summary>
    /// Decodes an Ogg Vorbis stream with the reference decoder. Managed decoders get uncoupled multichannel
    /// streams (layered songs as libvorbis writes them) wrong; this one gets every layout right.
    /// </summary>
    public static AudioClip Decode(byte[] ogg, IReadOnlyDictionary<string, string>? tags, CancellationToken ct)
    {
        float[][]? planar = null;
        var written = 0L;
        var declaredChannels = 0;
        var declaredFrames = 0L;
        Exception? failure = null;

        PcmFn receive = (pcm, channels, frames, _) =>
        {
            try
            {
                if (ct.IsCancellationRequested) return 1;
                if (planar is null)
                {
                    var capacity = declaredFrames > 0 ? declaredFrames : Math.Max(frames, 44100L * 60);
                    planar = new float[channels][];
                    for (var c = 0; c < channels; c++) planar[c] = new float[capacity];
                }
                if (written + frames > planar[0].Length)
                {
                    var grown = Math.Max(written + frames, planar[0].LongLength * 2);
                    if (grown > (long)(AudioReader.MaxDuration.TotalSeconds * 192000)) throw new InvalidDataException($"The song is longer than {AudioReader.MaxDuration.TotalMinutes:0} minutes.");
                    for (var c = 0; c < planar.Length; c++) Array.Resize(ref planar[c], (int)grown);
                }
                for (var c = 0; c < planar.Length; c++) new ReadOnlySpan<float>(pcm[c], frames).CopyTo(planar[c].AsSpan((int)written));
                written += frames;
                return 0;
            }
            catch (Exception ex)
            {
                failure = ex;
                return 1;
            }
        };

        int result;
        fixed (byte* data = ogg)
        {
            // The layout arrives through the out parameters before the first chunk; read them up front.
            result = sw_decode(data, ogg.Length, out declaredChannels, out var rate, out declaredFrames, receive, IntPtr.Zero);
            GC.KeepAlive(receive);
            ct.ThrowIfCancellationRequested();
            if (failure is not null) throw failure;
            if (result != 0) throw new InvalidDataException($"The Vorbis audio couldn't be decoded (code {result}).");
            if (planar is null || written == 0) throw new InvalidDataException("The Vorbis stream holds no audio.");
            if (written != planar[0].Length)
            {
                for (var c = 0; c < planar.Length; c++) Array.Resize(ref planar[c], (int)written);
            }
            return new AudioClip(planar, rate, tags);
        }
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sw_encode(IntPtr* channels, int channelCount, long frames, int rate, float quality, IntPtr* comments, int commentCount,
        int pageEveryBlocks, WriteFn write, ProgressFn progress, IntPtr user);

    private static string? folder;

    /// <summary>
    /// Where the DLL ships. Dalamud loads plugin assemblies from a stream, so their Location is empty in game and
    /// the plugin has to say where its folder is. Call before the first encode.
    /// </summary>
    public static void UseFolder(string directory) => folder = directory;

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        if (name != LibraryName) return IntPtr.Zero;
        foreach (var dir in new[] { folder, Path.GetDirectoryName(assembly.Location), AppContext.BaseDirectory })
        {
            if (string.IsNullOrEmpty(dir)) continue;
            foreach (var candidate in new[] { Path.Combine(dir, LibraryName + ".dll"), Path.Combine(dir, "native", "win-x64", LibraryName + ".dll") })
            {
                if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle)) return handle;
            }
        }
        return IntPtr.Zero;
    }

    private static (bool, string?) Load()
    {
        try
        {
            // 3 keeps every channel of a six-channel song at full bandwidth; an older DLL would not.
            var version = sw_version();
            return version == 3 ? (true, null) : (false, $"{LibraryName}.dll is version {version}, expected 3");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return (false, $"{LibraryName}.dll could not be loaded ({ex.GetType().Name})");
        }
    }

    public static EncodedVorbis Encode(float[][] channels, int sampleRate, float quality, IReadOnlyDictionary<string, string>? tags,
        Action<float>? progress, CancellationToken ct)
    {
        var frames = channels[0].Length;
        using var header = new MemoryStream();
        using var audio = new MemoryStream(Math.Max(4096, frames * channels.Length / 8));
        Exception? failure = null;
        var lastReport = -1f;

        WriteFn write = (data, length, isHeader, _) =>
        {
            try
            {
                (isHeader != 0 ? header : audio).Write(new ReadOnlySpan<byte>(data, length));
                return 0;
            }
            catch (Exception ex)
            {
                failure = ex;
                return 1;
            }
        };
        ProgressFn report = (fraction, _) =>
        {
            if (ct.IsCancellationRequested) return 1;
            if (fraction - lastReport >= 0.01f || fraction >= 1f)
            {
                lastReport = fraction;
                try { progress?.Invoke(fraction); } catch (Exception) { /* a progress display can't stop the encoder */ }
            }
            return 0;
        };

        var comments = new List<string> { "ENCODER=Soundswap" };
        if (tags is not null) comments.AddRange(tags.Select(t => $"{t.Key}={t.Value}"));
        var pins = channels.Select(c => GCHandle.Alloc(c, GCHandleType.Pinned)).ToArray();
        var texts = comments.Select(Marshal.StringToCoTaskMemUTF8).ToArray();
        int result;
        try
        {
            var pointers = pins.Select(p => p.AddrOfPinnedObject()).ToArray();
            fixed (IntPtr* channelPointers = pointers)
            fixed (IntPtr* commentPointers = texts)
            {
                result = sw_encode(channelPointers, channels.Length, frames, sampleRate, quality, commentPointers, texts.Length,
                    VorbisEncoder.PageEveryBlocks, write, report, IntPtr.Zero);
            }
            GC.KeepAlive(write);
            GC.KeepAlive(report);
        }
        finally
        {
            foreach (var pin in pins) pin.Free();
            foreach (var text in texts) Marshal.FreeCoTaskMem(text);
        }

        ct.ThrowIfCancellationRequested();
        if (failure is not null) throw failure;
        if (result != 0) throw new InvalidOperationException($"The Vorbis encoder refused this audio (code {result}: {channels.Length} channels at {sampleRate} Hz).");
        progress?.Invoke(1f);
        return new EncodedVorbis(header.ToArray(), audio.ToArray(), channels.Length, sampleRate, frames);
    }
}
