using System.Buffers.Binary;

namespace Soundswap.Core.Scd;

/// <summary>One Ogg page: where it starts and the sample position at its end (-1 when no packet ends on it).</summary>
public readonly record struct OggPage(int Offset, long Granule);

public static class OggPages
{
    /// <summary>Walks the pages of an Ogg stream by their headers.</summary>
    public static List<OggPage> Read(ReadOnlySpan<byte> data)
    {
        var pages = new List<OggPage>();
        var pos = 0;
        while (pos + 27 <= data.Length)
        {
            if (data[pos] != 'O' || data[pos + 1] != 'g' || data[pos + 2] != 'g' || data[pos + 3] != 'S')
                throw new InvalidDataException($"Broken Ogg page at byte {pos}.");
            var granule = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(pos + 6, 8));
            var segments = data[pos + 26];
            if (pos + 27 + segments > data.Length) throw new InvalidDataException("An Ogg page runs past the end.");
            var body = 0;
            for (var i = 0; i < segments; i++) body += data[pos + 27 + i];
            pages.Add(new OggPage(pos, granule));
            pos += 27 + segments + body;
        }
        if (pos != data.Length) throw new InvalidDataException("The Ogg stream ends in the middle of a page.");
        return pages;
    }
}

/// <summary>
/// The seek table the game stores with Vorbis music, and the mapping from sample positions to the byte offsets
/// the loop points are stored in. The bucket rules follow VFXEditor (MIT) so its files and ours behave alike:
/// one entry per <see cref="Step"/> seconds, each the offset of the page that reaches that time.
/// </summary>
public sealed class OggSeekTable
{
    public float Step { get; private set; } = 0.1f;
    public List<int> Offsets { get; } = [];
    public List<long> Samples { get; } = [];
    public int DataLength { get; }

    private OggSeekTable(int dataLength) => DataLength = dataLength;

    /// <param name="audio">The audio pages only (no header pages): offsets are relative to their start.</param>
    public static OggSeekTable Build(ReadOnlySpan<byte> audio, int sampleRate)
    {
        var table = new OggSeekTable(audio.Length);
        foreach (var page in OggPages.Read(audio))
        {
            if (page.Granule < 0) continue;
            var time = (float)page.Granule / sampleRate;
            if (table.Offsets.Count == 1 && time > table.Step) table.Step = time;
            // Every bucket a long page covers gets filled, so the table never drifts from real time.
            while (table.Step * table.Offsets.Count - time < 0.02f)
            {
                table.Offsets.Add(page.Offset);
                table.Samples.Add(page.Granule);
            }
        }
        return table;
    }

    /// <summary>The byte offset decoding must start from to play sample <paramref name="sample"/>.</summary>
    public int SamplesToRaw(long sample)
    {
        if (Offsets.Count == 0) return 0;
        for (var i = 0; i < Offsets.Count; i++)
        {
            if (Samples[i] > sample) return i == 0 ? 0 : Offsets[i - 1];
        }
        return DataLength;
    }
}
