using Soundswap.Core.Audio;
using Soundswap.Core.Build;
using Soundswap.Core.Scd;

namespace Soundswap.Core.Tests;

/// <summary>Tones, WAV files and synthetic game music files, built in memory.</summary>
internal static class TestAudio
{
    public static float[] Sine(int frames, int rate, double freq, double amplitude)
    {
        var data = new float[frames];
        for (var i = 0; i < frames; i++) data[i] = (float)(amplitude * Math.Sin(2 * Math.PI * freq * i / rate));
        return data;
    }

    public static AudioClip Tone(int channels, double seconds, int rate, double freq, double amplitude) =>
        new(Enumerable.Range(0, channels).Select(_ => Sine((int)(seconds * rate), rate, freq, amplitude)).ToArray(), rate);

    /// <summary>A 16-bit PCM WAV file on disk.</summary>
    public static string Wav(string folder, string name, AudioClip clip)
    {
        var path = Path.Combine(folder, name);
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        var dataBytes = clip.Frames * clip.ChannelCount * 2;
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)clip.ChannelCount);
        w.Write(clip.SampleRate);
        w.Write(clip.SampleRate * clip.ChannelCount * 2);
        w.Write((short)(clip.ChannelCount * 2));
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(dataBytes);
        for (var f = 0; f < clip.Frames; f++)
        {
            for (var c = 0; c < clip.ChannelCount; c++) w.Write((short)Math.Clamp(clip.Channels[c][f] * 32767f, -32768f, 32767f));
        }
        return path;
    }

    /// <summary>
    /// A music file laid out like the game's: header, offset tables for one sound, one track, one layout and
    /// one audio entry, their (dummy) data, then the audio entry, obscured with <paramref name="mode"/>.
    /// </summary>
    public static byte[] GameScd(EncodedVorbis vorbis, short mode = ScdXor.TableXorMode, bool marker = false, int loopStart = 100, int loopEnd = 200)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("SEDBSSCF"u8);
        w.Write(3);
        w.Write((byte)0);
        w.Write((byte)4);
        w.Write((short)0x30);
        w.Write(0); // file size, patched below
        w.Write(new byte[28]);

        w.Write((short)1); // sounds
        w.Write((short)1); // tracks
        w.Write((short)1); // audio
        w.Write((short)0);
        var tablePatch = ms.Position;
        w.Write(0); // track table
        w.Write(0); // audio table
        w.Write(0); // layout table
        w.Write(0); // routing
        w.Write(0); // attributes
        w.Write(0); // eof padding

        var soundTable = Table(w, 1);
        var trackTable = Table(w, 1);
        var audioTable = Table(w, 1);
        var layoutTable = Table(w, 1);

        var layout = Entry(w, 0x40);
        var sound = Entry(w, 0x30);
        var track = Entry(w, 0x10);
        var audio = (int)ms.Position;

        var header = (byte[])vorbis.Header.Clone();
        var data = (byte[])vorbis.Audio.Clone();
        if (mode == ScdXor.HeaderXorMode) ScdXor.DecodeHeader(header, 0x5A);
        if (mode == ScdXor.TableXorMode)
        {
            var both = header.Concat(data).ToArray();
            ScdXor.DecodeTable(both, data.Length);
            header = both[..header.Length];
            data = both[header.Length..];
        }
        var markerBytes = marker ? MarkerBlock(vorbis) : [];
        w.Write(data.Length);
        w.Write(vorbis.Channels);
        w.Write(vorbis.SampleRate);
        w.Write((int)ScdCodec.Vorbis);
        w.Write(loopStart);
        w.Write(loopEnd);
        w.Write(markerBytes.Length + 0x20 + 4 + header.Length);
        w.Write(marker ? 0x01 : 0x00);
        w.Write(markerBytes);
        w.Write(mode);
        w.Write((short)(mode == ScdXor.HeaderXorMode ? 0x5A : 0));
        w.Write(0);
        w.Write(0);
        w.Write(0.1f);
        w.Write(4);
        w.Write(header.Length);
        w.Write(0);
        w.Write(0);
        w.Write(0); // one seek entry
        w.Write(header);
        w.Write(data);
        while (ms.Position % 16 != 0) w.Write((byte)0);

        var bytes = ms.ToArray();
        void Put(long at, int value) => BitConverter.TryWriteBytes(bytes.AsSpan((int)at, 4), value);
        Put(0x10, bytes.Length);
        Put(tablePatch, trackTable);
        Put(tablePatch + 4, audioTable);
        Put(tablePatch + 8, layoutTable);
        Put(soundTable, sound);
        Put(trackTable, track);
        Put(audioTable, audio);
        Put(layoutTable, layout);
        return bytes;
    }

    private static int Table(BinaryWriter w, int count)
    {
        var at = (int)w.BaseStream.Position;
        for (var i = 0; i < count; i++) w.Write(0);
        while (w.BaseStream.Position % 16 != 0) w.Write((byte)0);
        return at;
    }

    private static int Entry(BinaryWriter w, int size)
    {
        var at = (int)w.BaseStream.Position;
        for (var i = 0; i < size; i++) w.Write((byte)(0xA0 + i % 16));
        return at;
    }

    private static byte[] MarkerBlock(EncodedVorbis vorbis)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("MARK"u8);
        w.Write(32);
        w.Write(10);
        w.Write(20);
        w.Write(2);
        w.Write(5);
        w.Write((int)vorbis.TotalSamples + 1000); // beyond any replacement's end
        w.Write(0);
        return ms.ToArray();
    }

    public static EncodedVorbis Encode(AudioClip clip, float quality = 0.3f) =>
        VorbisEncoder.Encode(clip.Channels, clip.SampleRate, quality, null, null, CancellationToken.None);

    public sealed class FakeGame : IGameFiles
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
        public byte[]? Read(string gamePath) => Files.GetValueOrDefault(gamePath);
    }

    public sealed class TempFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "soundswap-tests", Guid.NewGuid().ToString("N"));

        public TempFolder() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch (IOException) { }
        }
    }
}
