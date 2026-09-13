using Soundswap.Core.Audio;

namespace Soundswap.Core.Scd;

/// <summary>A loop in samples. The game jumps back to <see cref="Start"/> when it reaches <see cref="End"/>.</summary>
public readonly record struct LoopSamples(long Start, long End);

/// <summary>Builds a new music file from the game's own one and new Vorbis audio.</summary>
public static class ScdWriter
{
    /// <summary>
    /// Keeps every byte of <paramref name="original"/> before its audio entry (how the game plays the song) and
    /// writes a new audio entry laid out like the game's own: header pages apart from the audio pages, a seek
    /// table, loop points as byte offsets, and the entry's flags and loop markers carried over.
    /// </summary>
    /// <param name="loop">Null: play once. The game's own loop is replaced either way.</param>
    public static byte[] ReplaceAudio(ScdFile original, EncodedVorbis vorbis, LoopSamples? loop)
    {
        var audio = original.Audio ?? throw new InvalidOperationException("This song has no audio to replace.");
        if (vorbis.Channels != audio.Channels)
            throw new ArgumentException($"The new audio has {vorbis.Channels} channels; this song needs {audio.Channels}.", nameof(vorbis));

        var seek = OggSeekTable.Build(vorbis.Audio, vorbis.SampleRate);
        var (loopStart, loopEnd) = loop is { } l ? (seek.SamplesToRaw(l.Start), seek.SamplesToRaw(l.End)) : (0, 0);
        var marker = original.Marker is { } m ? MarkerBytes(m, loop, vorbis.TotalSamples) : [];

        using var ms = new MemoryStream(audio.Offset + vorbis.Header.Length + vorbis.Audio.Length + 256);
        using var w = new BinaryWriter(ms);
        w.Write(original.Bytes, 0, audio.Offset);

        w.Write(vorbis.Audio.Length);
        w.Write(vorbis.Channels);
        w.Write(vorbis.SampleRate);
        w.Write((int)ScdCodec.Vorbis);
        w.Write(loopStart);
        w.Write(loopEnd);
        w.Write(marker.Length + 0x20 + seek.Offsets.Count * 4 + vorbis.Header.Length);
        w.Write(original.Marker is null ? audio.Flags & ~0x01 : audio.Flags | 0x01);
        w.Write(marker);

        // The game's own layout with a zero key: nothing is obscured, and no reader needs to know that.
        w.Write(ScdXor.HeaderXorMode);
        w.Write((short)0);
        w.Write(0); // xor offset
        w.Write(0); // xor size
        w.Write(seek.Step);
        w.Write(seek.Offsets.Count * 4);
        w.Write(vorbis.Header.Length);
        w.Write(0);
        w.Write(0);
        foreach (var offset in seek.Offsets) w.Write(offset);
        w.Write(vorbis.Header);
        w.Write(vorbis.Audio);
        Pad(w, 16);

        var bytes = ms.ToArray();
        BitConverter.TryWriteBytes(bytes.AsSpan(0x10, 4), bytes.Length);
        return bytes;
    }

    /// <summary>The marker block with our loop, keeping the original's markers that still fall inside the song.</summary>
    private static byte[] MarkerBytes(ScdMarker marker, LoopSamples? loop, long totalSamples)
    {
        var positions = marker.Positions.Where(p => p >= 0 && p < totalSamples).ToList();
        var size = 20 + positions.Count * 4;
        size += (16 - size % 16) % 16;
        using var ms = new MemoryStream(size);
        using var w = new BinaryWriter(ms);
        w.Write(marker.Id);
        w.Write(size);
        w.Write((int)(loop?.Start ?? 0));
        w.Write((int)(loop?.End ?? 0));
        w.Write(positions.Count);
        foreach (var p in positions) w.Write(p);
        Pad(w, 16);
        return ms.ToArray();
    }

    private static void Pad(BinaryWriter w, int to)
    {
        var rem = (int)(w.BaseStream.Position % to);
        if (rem != 0) w.Write(new byte[to - rem]);
    }
}
