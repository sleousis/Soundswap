using System.Buffers.Binary;
using System.Text;

namespace Soundswap.Core.Scd;

/// <summary>Codecs an SCD audio entry can declare. Music uses Vorbis almost everywhere, HCA for two town themes.</summary>
public enum ScdCodec
{
    Pcm = 0x01,
    Vorbis = 0x06,
    MsAdpcm = 0x0C,
    Hca = 0x1A,
}

/// <summary>The audio entry of a music file, as stored. Offsets are absolute within the file.</summary>
/// <param name="LoopStart">Codec units: for Vorbis, a byte offset into the audio pages.</param>
public sealed record ScdAudio(
    int Offset,
    int DataLength,
    int Channels,
    int SampleRate,
    int Format,
    int LoopStart,
    int LoopEnd,
    int Flags,
    int MarkerSize)
{
    public const int EntryHeaderSize = 0x20;

    public bool HasMarker => (Flags & 0x01) != 0;
    public bool IsVorbis => Format == (int)ScdCodec.Vorbis;
    public ScdCodec Codec => (ScdCodec)Format;

    /// <summary>Stereo pairs. Layered songs keep one layer per pair (4 channels: 2 layers, 6 channels: 3).</summary>
    public int Layers => Math.Max(1, (Channels + 1) / 2);
}

/// <summary>Loop markers some music carries next to the audio (positions in samples).</summary>
public sealed record ScdMarker(byte[] Id, int LoopStart, int LoopEnd, IReadOnlyList<int> Positions);

/// <summary>
/// A music .scd: the sound, track, layout and attribute data the game uses to play it, and one audio entry.
/// In every music file of the game the audio entry is the last thing in the file, so replacing it means
/// keeping every byte before it and writing a new entry after them (<see cref="ScdWriter"/>).
/// </summary>
public sealed class ScdFile
{
    private const int HeaderSize = 0x30;
    private const int TableHeaderSize = 0x20;

    public byte[] Bytes { get; }

    /// <summary>Null for the game's silent placeholder files, which have no audio to replace.</summary>
    public ScdAudio? Audio { get; }

    public ScdMarker? Marker { get; }

    private ScdFile(byte[] bytes, ScdAudio? audio, ScdMarker? marker)
    {
        Bytes = bytes;
        Audio = audio;
        Marker = marker;
    }

    public static ScdFile Parse(byte[] bytes)
    {
        if (bytes.Length < HeaderSize + TableHeaderSize)
            throw new InvalidDataException("This isn't a music file: it is too short.");
        if (Encoding.ASCII.GetString(bytes, 0, 8) != "SEDBSSCF")
            throw new InvalidDataException("This isn't a music file: the SCD signature is missing.");
        if (bytes[0x0C] != 0)
            throw new NotSupportedException("Big-endian SCD files (console builds) aren't supported.");

        var soundCount = I16(bytes, 0x30);
        var trackCount = I16(bytes, 0x32);
        var audioCount = I16(bytes, 0x34);
        var trackTable = I32(bytes, 0x38);
        var audioTable = I32(bytes, 0x3C);
        var layoutTable = I32(bytes, 0x40);
        var attributeTable = I32(bytes, 0x48);

        var sounds = Offsets(bytes, HeaderSize + TableHeaderSize, soundCount);
        var tracks = Offsets(bytes, trackTable, trackCount);
        var audio = Offsets(bytes, audioTable, audioCount);
        var layouts = layoutTable != 0 ? Offsets(bytes, layoutTable, soundCount) : [];

        var live = audio.Where(o => o > 0).ToList();
        if (live.Count == 0) return new ScdFile(bytes, null, null);
        if (live.Count > 1)
            throw new NotSupportedException("This file holds more than one audio entry, which no music file in the game does.");

        var at = live[0];
        var others = sounds.Concat(tracks).Concat(layouts).Where(o => o > 0).ToList();
        if (attributeTable != 0)
        {
            var first = others.Concat(live).Min();
            others.AddRange(Offsets(bytes, attributeTable, Math.Max(0, (first - attributeTable) / 4)).Where(o => o > 0));
        }
        if (others.Any(o => o >= at))
            throw new NotSupportedException("The audio entry of this file isn't its last part, so it can't be replaced safely.");
        if (at + ScdAudio.EntryHeaderSize > bytes.Length)
            throw new InvalidDataException("The audio entry runs past the end of the file.");

        var flags = I32(bytes, at + 0x1C);
        ScdMarker? marker = null;
        var markerSize = 0;
        if ((flags & 0x01) != 0)
        {
            var m = at + ScdAudio.EntryHeaderSize;
            markerSize = I32(bytes, m + 4);
            var count = I32(bytes, m + 16);
            if (markerSize < 20 || count < 0 || m + 20 + count * 4 > bytes.Length)
                throw new InvalidDataException("The loop markers of this file are damaged.");
            var positions = Enumerable.Range(0, count).Select(i => I32(bytes, m + 20 + i * 4)).ToList();
            marker = new ScdMarker(bytes[m..(m + 4)], I32(bytes, m + 8), I32(bytes, m + 12), positions);
        }

        var entry = new ScdAudio(
            Offset: at,
            DataLength: I32(bytes, at),
            Channels: I32(bytes, at + 0x04),
            SampleRate: I32(bytes, at + 0x08),
            Format: I32(bytes, at + 0x0C),
            LoopStart: I32(bytes, at + 0x10),
            LoopEnd: I32(bytes, at + 0x14),
            Flags: flags,
            MarkerSize: markerSize);
        if (entry.DataLength == 0) return new ScdFile(bytes, null, null);
        if (entry.Channels is < 1 or > 8 || entry.SampleRate is < 8000 or > 192000)
            throw new InvalidDataException($"This file declares {entry.Channels} channels at {entry.SampleRate} Hz, which can't be right.");
        return new ScdFile(bytes, entry, marker);
    }

    /// <summary>
    /// The original music as a plain Ogg Vorbis stream (headers and audio, de-obscured), for previews and for
    /// measuring its loudness. Null when the entry isn't Vorbis.
    /// </summary>
    public byte[]? ExtractOgg()
    {
        if (Audio is not { IsVorbis: true } a) return null;
        var p = a.Offset + ScdAudio.EntryHeaderSize + a.MarkerSize;
        var mode = I16(Bytes, p);
        var key = Bytes[p + 2];
        // Sub-header: mode, key, xor offset, xor size, seek step, seek table size, header size, two unknowns.
        var seekTableSize = I32(Bytes, p + 0x10);
        var headerSize = I32(Bytes, p + 0x14);
        var start = p + 0x20 + seekTableSize;
        if (seekTableSize < 0 || headerSize < 0 || start + headerSize + a.DataLength > Bytes.Length)
            throw new InvalidDataException("The audio of this file is damaged.");

        var ogg = Bytes.AsSpan(start, headerSize + a.DataLength).ToArray();
        if (mode == ScdXor.HeaderXorMode) ScdXor.DecodeHeader(ogg.AsSpan(0, headerSize), key);
        else if (mode == ScdXor.TableXorMode) ScdXor.DecodeTable(ogg, a.DataLength);
        return ogg;
    }

    internal static short I16(byte[] b, int at) => BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(at, 2));

    internal static int I32(byte[] b, int at) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at, 4));

    private static int[] Offsets(byte[] bytes, int table, int count)
    {
        if (count <= 0 || table <= 0) return [];
        if (table + count * 4 > bytes.Length) throw new InvalidDataException("An offset table of this file runs past its end.");
        var list = new int[count];
        for (var i = 0; i < count; i++) list[i] = I32(bytes, table + i * 4);
        return list;
    }
}
