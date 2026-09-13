using Soundswap.Core.Catalog;
using Soundswap.Core.Project;

namespace Soundswap.Core.Tests;

public class CatalogTests
{
    private static readonly SongCatalog Catalog = new([
        new SongInfo(1, "music/ffxiv/bgm_a.scd", "Answers", "", "", "Primal battle", "", null),
        new SongInfo(2, "music/ex5/bgm_ex5_b.scd", "Tuliyollal", "", "", "Town", "", TimeSpan.FromMinutes(3)),
        new SongInfo(3, "music/ex3/bgm_ex3_c.scd", "Shadowbringers", "", "", "Answers remix", "", null),
        new SongInfo(4, "music/ex1/bgm_ex1_unknown.scd", "", "", "", "", "", null),
        new SongInfo(5, "music/ex2/bgm_ex2_d.scd", "Café Answers", "", "", "", "", null),
    ]);

    [Fact]
    public void A_title_that_starts_with_the_word_ranks_first_then_places()
    {
        var results = Catalog.Search("answers").Select(s => s.Id).ToList();
        Assert.Equal([1, 5, 3], results);
    }

    [Fact]
    public void Every_word_must_match_and_accents_dont_matter()
    {
        Assert.Equal([5], Catalog.Search("cafe answ").Select(s => s.Id));
        Assert.Empty(Catalog.Search("answers zzz"));
    }

    [Fact]
    public void Unnamed_songs_are_found_by_file_name_or_id_and_listed_last()
    {
        Assert.Equal(4, Catalog.Search("ex1_unknown").Single().Id);
        Assert.Equal(4, Catalog.Search("4").First().Id);
        Assert.Equal(4, Catalog.Songs[^1].Id);
        Assert.Equal("bgm_ex1_unknown", Catalog.ById(4)!.Title);
    }

    [Fact]
    public void Expansion_comes_from_the_folder()
    {
        Assert.Equal(Expansion.Dawntrail, Catalog.ById(2)!.Expansion);
        Assert.Equal(Expansion.RealmReborn, Catalog.ById(1)!.Expansion);
    }

    [Fact]
    public void Orchestrion_csv_is_read_with_quotes_commas_and_line_breaks()
    {
        const string csv = "﻿\"ID\",\"Title\",\"Alt Title\",\"Special Mode Title\",\"Locations\",\"Comments\"\n" +
                           "\"1\",\"None\",\"\",\"\",\"\",\"Empty sound\"\n" +
                           "\"2\",\"Prelude, \"\"Rebirth\"\"\",\"\",\"\",\"A Realm Reborn title\",\"line one\nline two\"\n";
        var rows = SongNames.ReadNames(csv);

        Assert.Equal("", rows[1].Name);
        Assert.Equal("Prelude, \"Rebirth\"", rows[2].Name);
        Assert.Equal("line one\nline two", rows[2].Info);
        Assert.Equal(TimeSpan.FromSeconds(38), SongNames.ReadDurations("\"ID\",\"Duration\"\n\"2\",\"38\"\n")[2]);
    }

    [Fact]
    public void A_project_says_what_is_missing()
    {
        var song = new SongReplacement { Channels = 6, LayerMode = LayerMode.PerLayer };
        Assert.Equal("Choose a file for layer 1.", song.Missing());
        song.SourcePath = "a.wav";
        Assert.Null(song.Missing());
        song.Layer(0).Source = song.Layer(1).Source = song.Layer(2).Source = LayerSource.Silence;
        Assert.Contains("silent", song.Missing());
        Assert.Equal(["a.wav"], song.Files());
    }

    [Fact]
    public void A_project_round_trips_through_the_store_and_a_damaged_one_is_set_aside()
    {
        var dir = Path.Combine(Path.GetTempPath(), "soundswap-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProjectStore(Path.Combine(dir, "project.json"));
            var project = new SoundswapProject { Name = "Mix", Songs = [new SongReplacement { BgmId = 9, LayerMode = LayerMode.PerLayer, Loop = LoopMode.Custom }] };
            store.Save(project);

            var loaded = store.Load(out var problem)!;
            Assert.Null(problem);
            Assert.Equal("Mix", loaded.Name);
            Assert.Equal(LayerMode.PerLayer, loaded.Songs.Single().LayerMode);

            File.WriteAllText(store.Path, "{ not json");
            Assert.Null(store.Load(out problem));
            Assert.NotNull(problem);
            Assert.Single(Directory.GetFiles(dir, "project.json.unreadable-*"));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
