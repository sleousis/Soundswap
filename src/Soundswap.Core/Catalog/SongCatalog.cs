using Soundswap.Core.Util;

namespace Soundswap.Core.Catalog;

public enum Expansion
{
    RealmReborn,
    Heavensward,
    Stormblood,
    Shadowbringers,
    Endwalker,
    Dawntrail,
}

/// <summary>One of the game's songs: its BGM row, its file, and the names Orchestrion's list gives it.</summary>
public sealed record SongInfo(int Id, string GamePath, string Name, string AlternateName, string SpecialName, string Locations, string Info, TimeSpan? Duration)
{
    public string FileName => System.IO.Path.GetFileNameWithoutExtension(GamePath);

    /// <summary>The name to show: Orchestrion's title, otherwise the file name.</summary>
    public string Title => Name.Length > 0 ? Name : FileName;

    public bool HasName => Name.Length > 0;

    /// <summary>From the folder the game keeps the file in (music/ex3/... is Shadowbringers).</summary>
    public Expansion Expansion => GamePath.Split('/') switch
    {
        [_, "ex1", ..] => Expansion.Heavensward,
        [_, "ex2", ..] => Expansion.Stormblood,
        [_, "ex3", ..] => Expansion.Shadowbringers,
        [_, "ex4", ..] => Expansion.Endwalker,
        [_, "ex5", ..] => Expansion.Dawntrail,
        _ => Expansion.RealmReborn,
    };
}

/// <summary>Every song of the game, searchable.</summary>
public sealed class SongCatalog
{
    private readonly Dictionary<int, SongInfo> byId;
    private readonly List<(SongInfo Song, string Name, string Other, string Places, string File)> index;

    public IReadOnlyList<SongInfo> Songs { get; }

    public SongCatalog(IEnumerable<SongInfo> songs)
    {
        Songs = songs.OrderBy(s => s.HasName ? 0 : 1).ThenBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(s => s.Id).ToList();
        byId = Songs.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        index = Songs.Select(s => (s, Naming.Fold(s.Name), Naming.Fold($"{s.AlternateName} {s.SpecialName}"), Naming.Fold($"{s.Locations} {s.Info}"),
            Naming.Fold($"{s.FileName} {s.Id}"))).ToList();
    }

    public static SongCatalog Empty { get; } = new([]);

    public SongInfo? ById(int id) => byId.GetValueOrDefault(id);

    /// <summary>
    /// Songs matching every word of <paramref name="query"/> in their name, other names, places or file name, best
    /// first: a name that starts with a word beats a name that contains it, which beats a place.
    /// An empty query returns everything <paramref name="filter"/> lets through, in catalog order.
    /// </summary>
    public IReadOnlyList<SongInfo> Search(string query, Func<SongInfo, bool>? filter = null)
    {
        var words = Naming.Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return filter is null ? Songs : Songs.Where(filter).ToList();

        var results = new List<(SongInfo Song, double Score)>();
        foreach (var (song, name, other, places, file) in index)
        {
            if (filter is not null && !filter(song)) continue;
            double score = 0;
            var all = true;
            foreach (var word in words)
            {
                var s = Score(name, word, 3) ?? Score(other, word, 1.6) ?? Score(places, word, 1) ?? Score(file, word, 0.6);
                if (s is null) { all = false; break; }
                score += s.Value;
            }
            if (all) results.Add((song, score + (song.HasName ? 0.1 : 0)));
        }
        return results.OrderByDescending(r => r.Score).ThenBy(r => r.Song.Title, StringComparer.CurrentCultureIgnoreCase).Select(r => r.Song).ToList();
    }

    private static double? Score(string field, string word, double weight)
    {
        var at = field.IndexOf(word, StringComparison.Ordinal);
        if (at < 0) return null;
        var wordStart = at == 0 || field[at - 1] == ' ';
        return weight * (at == 0 ? 1.5 : wordStart ? 1.2 : 1.0);
    }
}
