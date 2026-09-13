using Soundswap.Core.Audio;
using Soundswap.Core.Scd;
using static Soundswap.Core.Tests.TestAudio;

namespace Soundswap.Core.Tests;

public class ScdTests
{
    private static readonly EncodedVorbis Original = Encode(Tone(2, 2, 44100, 330, 0.3));

    [Theory]
    [InlineData(ScdXor.TableXorMode)]
    [InlineData(ScdXor.HeaderXorMode)]
    [InlineData((short)0)]
    public void Reads_the_audio_entry_and_undoes_either_obscuring(short mode)
    {
        var scd = ScdFile.Parse(GameScd(Original, mode));

        var audio = Assert.IsType<ScdAudio>(scd.Audio);
        Assert.Equal(2, audio.Channels);
        Assert.Equal(44100, audio.SampleRate);
        Assert.Equal(Original.Audio.Length, audio.DataLength);
        Assert.Equal(Original.Header.Concat(Original.Audio).ToArray(), scd.ExtractOgg());
    }

    [Fact]
    public void Replacing_keeps_every_byte_before_the_audio_and_writes_a_readable_entry()
    {
        var bytes = GameScd(Original);
        var original = ScdFile.Parse(bytes);
        var replacement = Encode(Tone(2, 3, 44100, 440, 0.5));

        var result = ScdWriter.ReplaceAudio(original, replacement, new LoopSamples(22050, 110250));
        var parsed = ScdFile.Parse(result);

        AssertSamePrefix(bytes, result, original.Audio!.Offset);
        Assert.Equal(result.Length, BitConverter.ToInt32(result, 0x10));
        Assert.Equal(0, result.Length % 16);
        Assert.Equal(replacement.Audio.Length, parsed.Audio!.DataLength);
        Assert.Equal(replacement.Header.Concat(replacement.Audio).ToArray(), parsed.ExtractOgg());
        Assert.True(parsed.Audio.LoopStart > 0 && parsed.Audio.LoopEnd > parsed.Audio.LoopStart && parsed.Audio.LoopEnd <= parsed.Audio.DataLength);

        var decoded = AudioReader.DecodeOgg(parsed.ExtractOgg()!, CancellationToken.None);
        Assert.Equal(3 * 44100, decoded.Frames);
    }

    [Fact]
    public void Play_once_writes_no_loop()
    {
        var original = ScdFile.Parse(GameScd(Original));
        var parsed = ScdFile.Parse(ScdWriter.ReplaceAudio(original, Encode(Tone(2, 1, 44100, 440, 0.5)), null));
        Assert.Equal(0, parsed.Audio!.LoopStart);
        Assert.Equal(0, parsed.Audio.LoopEnd);
    }

    [Fact]
    public void Markers_carry_the_new_loop_and_drop_positions_past_the_end()
    {
        var original = ScdFile.Parse(GameScd(Original, marker: true));
        Assert.NotNull(original.Marker);

        var parsed = ScdFile.Parse(ScdWriter.ReplaceAudio(original, Encode(Tone(2, 1, 44100, 440, 0.5)), new LoopSamples(1000, 40000)));

        var marker = Assert.IsType<ScdMarker>(parsed.Marker);
        Assert.Equal("MARK"u8.ToArray(), marker.Id);
        Assert.Equal(1000, marker.LoopStart);
        Assert.Equal(40000, marker.LoopEnd);
        Assert.Equal([5], marker.Positions);
        AssertSamePrefix(original.Bytes, parsed.Bytes, original.Audio!.Offset);
        Assert.NotNull(parsed.ExtractOgg());
    }

    /// <summary>Everything before the audio entry is kept, apart from the file size in the header (0x10).</summary>
    internal static void AssertSamePrefix(byte[] original, byte[] result, int audioOffset)
    {
        Assert.Equal(original.AsSpan(0, 0x10).ToArray(), result.AsSpan(0, 0x10).ToArray());
        Assert.Equal(original.AsSpan(0x14, audioOffset - 0x14).ToArray(), result.AsSpan(0x14, audioOffset - 0x14).ToArray());
    }

    [Fact]
    public void A_replacement_must_have_the_songs_channel_count()
    {
        var original = ScdFile.Parse(GameScd(Original));
        Assert.Throws<ArgumentException>(() => ScdWriter.ReplaceAudio(original, Encode(Tone(6, 1, 44100, 440, 0.2)), null));
    }

    [Fact]
    public void Not_an_scd_is_refused()
    {
        Assert.Throws<InvalidDataException>(() => ScdFile.Parse(new byte[200]));
    }

    [Fact]
    public void The_seek_table_maps_samples_to_page_offsets_in_order()
    {
        var vorbis = Encode(Tone(2, 5, 44100, 440, 0.5));
        var table = OggSeekTable.Build(vorbis.Audio, 44100);

        Assert.NotEmpty(table.Offsets);
        Assert.Equal(0, table.SamplesToRaw(0));
        Assert.Equal(vorbis.Audio.Length, table.SamplesToRaw(vorbis.TotalSamples));
        var mid = table.SamplesToRaw(44100 * 2);
        Assert.InRange(mid, 1, vorbis.Audio.Length - 1);
        Assert.True(table.SamplesToRaw(44100 * 3) >= mid);
        Assert.Equal(table.Offsets.OrderBy(o => o), table.Offsets);
    }

    [Fact]
    public void Table_obscuring_is_its_own_inverse()
    {
        var data = Enumerable.Range(0, 1000).Select(i => (byte)(i * 7)).ToArray();
        var copy = (byte[])data.Clone();
        ScdXor.DecodeTable(copy, 777);
        Assert.NotEqual(data, copy);
        ScdXor.DecodeTable(copy, 777);
        Assert.Equal(data, copy);
    }
}
