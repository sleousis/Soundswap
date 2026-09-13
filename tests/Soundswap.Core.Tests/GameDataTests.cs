using Soundswap.Core.Audio;
using Soundswap.Core.Scd;

namespace Soundswap.Core.Tests;

/// <summary>
/// Runs the SCD code over the game's real music, read-only through Lumina. Skipped where the game isn't installed;
/// set SOUNDSWAP_SQPACK to the game's sqpack folder when it is somewhere unusual.
/// </summary>
public class GameDataTests
{
    private static readonly string[] Candidates =
    [
        Environment.GetEnvironmentVariable("SOUNDSWAP_SQPACK") ?? "",
        @"C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY XIV Online\game\sqpack",
        @"C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\sqpack",
    ];

    private static readonly Lazy<Lumina.GameData?> Game = new(() =>
    {
        var path = Candidates.FirstOrDefault(p => p.Length > 0 && Directory.Exists(p));
        return path is null ? null : new Lumina.GameData(path, new Lumina.LuminaOptions { PanicOnSheetChecksumMismatch = false });
    });

    private static IEnumerable<string> MusicPaths(Lumina.GameData game)
    {
        var sheet = game.Excel.GetSheet<Lumina.Excel.Sheets.BGM>();
        return sheet.Select(r => r.File.ExtractText().ToLowerInvariant()).Where(p => p.EndsWith(".scd")).Distinct();
    }

    [SkippableFact]
    public void Every_music_file_of_the_game_parses_and_its_vorbis_opens()
    {
        Skip.If(Game.Value is null, "The game isn't installed here.");
        var game = Game.Value!;
        int checkedFiles = 0, vorbis = 0, layered = 0;
        foreach (var path in MusicPaths(game))
        {
            var file = game.GetFile(path);
            if (file is null) continue;
            var scd = ScdFile.Parse(file.Data);
            checkedFiles++;
            if (scd.Audio is not { IsVorbis: true } audio) continue;
            vorbis++;
            if (audio.Layers > 1) layered++;
            var ogg = scd.ExtractOgg()!;
            using var reader = new NVorbis.VorbisReader(new MemoryStream(ogg), true);
            Assert.Equal(audio.Channels, reader.Channels);
            Assert.Equal(audio.SampleRate, reader.SampleRate);
        }
        Assert.True(checkedFiles > 1000, $"only {checkedFiles} files");
        Assert.True(vorbis > 1000 && layered >= 10, $"{vorbis} vorbis, {layered} layered");
    }

    [SkippableTheory]
    [InlineData("music/ffxiv/bgm_con_bahamut.scd")] // 6 channels with markers
    [InlineData("music/ex1/bgm_ex1_pvp01.scd")]     // shows up as 4 channels in the game's table
    [InlineData("music/ffxiv/bgm_town_gri_day.scd")] // HCA
    public void Real_songs_can_be_replaced_and_read_back(string path)
    {
        Skip.If(Game.Value is null, "The game isn't installed here.");
        var file = Game.Value!.GetFile(path);
        Skip.If(file is null, $"{path} isn't in this game version.");
        var original = ScdFile.Parse(file!.Data);
        var audio = original.Audio!;
        var tone = TestAudio.Tone(audio.Channels, 2, audio.SampleRate, 440, 0.3);
        var vorbis = VorbisEncoder.Encode(tone.Channels, audio.SampleRate, 0.3f, null, null, CancellationToken.None);

        var bytes = ScdWriter.ReplaceAudio(original, vorbis, new LoopSamples(0, tone.Frames));
        var parsed = ScdFile.Parse(bytes);

        ScdTests.AssertSamePrefix(file.Data, bytes, audio.Offset);
        Assert.Equal(bytes.Length, BitConverter.ToInt32(bytes, 0x10));
        Assert.Equal(audio.Channels, parsed.Audio!.Channels);
        Assert.Equal(original.Marker is null, parsed.Marker is null);
        var decoded = AudioReader.DecodeOgg(parsed.ExtractOgg()!, CancellationToken.None);
        Assert.Equal(tone.Frames, decoded.Frames);
    }
}
