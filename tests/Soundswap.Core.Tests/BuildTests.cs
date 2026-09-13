using System.IO.Compression;
using System.Text.Json.Nodes;
using Soundswap.Core.Audio;
using Soundswap.Core.Build;
using Soundswap.Core.Mods;
using Soundswap.Core.Project;
using Soundswap.Core.Scd;
using static Soundswap.Core.Tests.TestAudio;

namespace Soundswap.Core.Tests;

public class BuildTests
{
    private const string StereoPath = "music/ffxiv/bgm_test.scd";
    private const string LayeredPath = "music/ex3/bgm_ex3_layers.scd";

    private static (FakeGame Game, TempFolder Temp) World()
    {
        var game = new FakeGame();
        game.Files[StereoPath] = GameScd(Encode(Tone(2, 3, 44100, 220, 0.1)));
        game.Files[LayeredPath] = GameScd(Encode(Tone(6, 2, 48000, 220, 0.1)), marker: true);
        return (game, new TempFolder());
    }

    private static SongBuilder Builder(FakeGame game) => new(game, new AudioDecoderChain([new ManagedAudioDecoder()]));

    [Fact]
    public void A_stereo_song_is_replaced_at_the_games_rate_and_volume()
    {
        var (game, temp) = World();
        using var _ = temp;
        var song = new SongReplacement { BgmId = 1, GamePath = StereoPath, SongName = "Test", SourcePath = Wav(temp.Path, "loud.wav", Tone(2, 4, 22050, 440, 0.9)) };

        var built = Builder(game).Build(song, new BuildSettings(), null, CancellationToken.None);

        var scd = ScdFile.Parse(built.Scd);
        Assert.Equal((2, 44100), (scd.Audio!.Channels, scd.Audio.SampleRate));
        var decoded = AudioReader.DecodeOgg(scd.ExtractOgg()!, CancellationToken.None);
        Assert.InRange(decoded.Duration.TotalSeconds, 3.95, 4.05);
        var original = Loudness.Integrated(AudioReader.DecodeOgg(ScdFile.Parse(game.Files[StereoPath]).ExtractOgg()!, CancellationToken.None))!.Value;
        Assert.InRange(Loudness.Integrated(decoded)!.Value, original - 1, original + 1);
        Assert.Equal(new LoopSeconds(0, 4), built.Loop);
        Assert.Equal(2, song.Channels);
    }

    [Fact]
    public void First_layer_only_leaves_the_other_layers_silent_and_keeps_the_marker()
    {
        var (game, temp) = World();
        using var _ = temp;
        var song = new SongReplacement { GamePath = LayeredPath, SongName = "Layers", LayerMode = LayerMode.FirstLayerOnly, SourcePath = Wav(temp.Path, "s.wav", Tone(2, 2, 48000, 440, 0.5)) };

        var built = Builder(game).Build(song, new BuildSettings(), null, CancellationToken.None);

        Assert.Equal(3, built.Layers);
        var scd = ScdFile.Parse(built.Scd);
        Assert.NotNull(scd.Marker);
        var rms = RmsPerChannel(scd);
        Assert.Equal(6, rms.Length);
        Assert.True(rms[0] > 0.01 && rms[1] > 0.01);
        Assert.True(rms[2..].All(r => r < 0.005), string.Join(", ", rms));
    }

    [Fact]
    public void Per_layer_choices_can_keep_the_games_layer_and_use_other_files()
    {
        var (game, temp) = World();
        using var _ = temp;
        var song = new SongReplacement { GamePath = LayeredPath, SongName = "Layers", LayerMode = LayerMode.PerLayer, SourcePath = Wav(temp.Path, "a.wav", Tone(2, 2, 48000, 440, 0.5)) };
        song.Layer(0).Source = LayerSource.Song;
        song.Layer(1).Source = LayerSource.Original;
        song.Layer(2).Source = LayerSource.Song;
        song.Layer(2).Path = Wav(temp.Path, "b.wav", Tone(2, 1, 48000, 660, 0.5));

        var built = Builder(game).Build(song, new BuildSettings(), null, CancellationToken.None);

        var rms = RmsPerChannel(ScdFile.Parse(built.Scd));
        Assert.True(rms.All(r => r > 0.005), string.Join(", ", rms));
    }

    [Fact]
    public void Missing_choices_and_silent_placeholders_are_explained()
    {
        var (game, temp) = World();
        using var _ = temp;
        var noFile = new SongReplacement { GamePath = StereoPath, SongName = "Test" };
        Assert.Contains("Choose a song file", Assert.Throws<InvalidOperationException>(() => Builder(game).Build(noFile, new BuildSettings(), null, CancellationToken.None)).Message);

        var nothing = new SongReplacement { GamePath = "music/ffxiv/none.scd", SongName = "None", SourcePath = "x.wav" };
        Assert.Throws<InvalidDataException>(() => Builder(game).Build(nothing, new BuildSettings(), null, CancellationToken.None));
    }

    [Fact]
    public void A_build_goes_on_past_a_failing_song_and_reports_progress()
    {
        var (game, temp) = World();
        using var _ = temp;
        var good = new SongReplacement { GamePath = StereoPath, SongName = "Good", SourcePath = Wav(temp.Path, "g.wav", Tone(2, 1, 44100, 440, 0.5)) };
        var bad = new SongReplacement { GamePath = StereoPath, SongName = "Bad", SourcePath = Path.Combine(temp.Path, "missing.wav") };
        var seen = new List<BuildProgress>();

        var result = new ModBuilder(game, new AudioDecoderChain([new ManagedAudioDecoder()])).Build([bad, good], new BuildSettings(), seen.Add, CancellationToken.None);

        Assert.Equal("Good", Assert.Single(result.Built).Song.SongName);
        Assert.Equal("Bad", Assert.Single(result.Failed).Song.SongName);
        Assert.Equal(1f, seen[^1].Overall);
        Assert.Equal(seen.Select(p => p.Overall).OrderBy(x => x), seen.Select(p => p.Overall));
    }

    [Fact]
    public void The_mod_folder_is_written_updated_and_cleaned()
    {
        var (game, temp) = World();
        using var _ = temp;
        var project = new SoundswapProject { Name = "My mix" };
        var song = new SongReplacement { BgmId = 7, GamePath = StereoPath, SongName = "Test", SourcePath = Wav(temp.Path, "a.wav", Tone(2, 1, 44100, 440, 0.5)) };
        var folder = Path.Combine(temp.Path, "mods", "My mix");
        var meta = new ModMetadata(project.Name, "Me", "", "1.0.0");

        var first = Builder(game).Build(song, new BuildSettings(), null, CancellationToken.None);
        ModFolderWriter.Write(folder, project.Id, meta, [first]);
        song.ExtraGainDb = -6;
        var second = Builder(game).Build(song, new BuildSettings(), null, CancellationToken.None);
        var result = ModFolderWriter.Write(folder, project.Id, meta, [second]);

        Assert.Equal(1, result.FilesRemoved);
        var scds = Directory.GetFiles(Path.Combine(folder, "songs"));
        Assert.Equal(Path.GetFileName(PenumbraMod.SongPath(second)), Path.GetFileName(Assert.Single(scds)));
        var group = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "group_001_songs.json")))!;
        Assert.Equal("Multi", (string?)group["Type"]);
        Assert.Equal(PenumbraMod.SongPath(second), (string?)group["Options"]![0]!["Files"]![StereoPath]);
        Assert.True(ModFolderWriter.IsOurs(folder, project.Id));
        Assert.Throws<InvalidOperationException>(() => ModFolderWriter.Write(folder, Guid.NewGuid(), meta, [second]));
    }

    [Fact]
    public void Groups_split_at_32_songs_and_untick_disabled_ones()
    {
        var songs = Enumerable.Range(0, 40).Select(i => new BuiltSong(
            new SongReplacement { BgmId = i, GamePath = $"music/ffxiv/bgm_{i}.scd", SongName = i == 3 ? "Same" : i == 4 ? "Same" : $"Song {i}", Enabled = i != 1 },
            [(byte)i], TimeSpan.FromSeconds(10), 1, null, [])).ToList();

        var json = PenumbraMod.JsonFiles(new ModMetadata("M", "A", "", "1.0"), songs);

        var groups = json.Where(f => f.RelativePath.StartsWith("group_")).Select(f => JsonNode.Parse(f.Bytes)!).ToList();
        Assert.Equal(2, groups.Count);
        Assert.Equal(32, groups[0]["Options"]!.AsArray().Count);
        Assert.Equal(8, groups[1]["Options"]!.AsArray().Count);
        Assert.Equal(0xFFFFFFFDUL, (ulong)groups[0]["DefaultSettings"]!);
        Assert.Equal("Same (2)", (string?)groups[0]["Options"]![4]!["Name"]);
    }

    [Fact]
    public void A_pmp_holds_the_json_and_songs_Penumbra_expects()
    {
        using var temp = new TempFolder();
        var song = new BuiltSong(new SongReplacement { GamePath = StereoPath, SongName = "Test" }, [1, 2, 3], TimeSpan.FromSeconds(1), 1, new LoopSeconds(0, 1), []);
        var path = Path.Combine(temp.Path, "m.pmp");

        PenumbraMod.WritePmp(path, new ModMetadata("M", "A", "D", "1.0"), [song]);

        using var zip = ZipFile.OpenRead(path);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("meta.json", names);
        Assert.Contains("default_mod.json", names);
        Assert.Contains("group_001_songs.json", names);
        Assert.Contains(PenumbraMod.SongPath(song).Replace('\\', '/'), names);
    }

    private static double[] RmsPerChannel(ScdFile scd)
    {
        var decoded = AudioReader.DecodeOgg(scd.ExtractOgg()!, CancellationToken.None);
        return decoded.Channels.Select(c => Math.Sqrt(c.Average(s => (double)s * s))).ToArray();
    }
}
