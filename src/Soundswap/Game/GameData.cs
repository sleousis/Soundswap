using System.Reflection;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Soundswap.Core.Build;
using Soundswap.Core.Catalog;
using Soundswap.Integrations;

namespace Soundswap.Game;

/// <summary>The game's own files through Lumina, one read at a time (called from background work).</summary>
public sealed class GameFiles(IDataManager data, IPluginLog log) : IGameFiles
{
    private readonly Lock gate = new();

    public byte[]? Read(string gamePath)
    {
        lock (gate)
        {
            try
            {
                return data.GetFile(gamePath)?.Data;
            }
            catch (Exception ex)
            {
                log.Debug(ex, $"Could not read {gamePath}");
                return null;
            }
        }
    }
}

/// <summary>
/// The list of songs: every row of the game's BGM sheet with a music file, named from Orchestrion's list (live
/// over IPC when it is installed, otherwise the copy of its list that ships with Soundswap).
/// </summary>
public static class CatalogLoader
{
    public static SongCatalog Load(IDataManager data, IReadOnlyList<OrchestrionSong> orchestrion, IPluginLog log)
    {
        var lang = data.Language switch
        {
            ClientLanguage.Japanese => "ja",
            ClientLanguage.German => "de",
            ClientLanguage.French => "fr",
            _ => "en",
        };

        var names = SongNames.ReadNames(Embedded($"xiv_bgm_{lang}.csv") ?? Embedded("xiv_bgm_en.csv") ?? "");
        var english = lang == "en" ? names : SongNames.ReadNames(Embedded("xiv_bgm_en.csv") ?? "");
        var durations = SongNames.ReadDurations(Embedded("xiv_bgm_metadata.csv") ?? "");
        var live = orchestrion.Where(s => s.Strings is not null).ToDictionary(s => s.Id);

        var songs = new List<SongInfo>();
        foreach (var row in data.GetExcelSheet<BGM>())
        {
            var path = row.File.ExtractText().Trim().ToLowerInvariant();
            if (!path.EndsWith(".scd", StringComparison.Ordinal)) continue;
            var id = (int)row.RowId;

            if (live.TryGetValue(id, out var song) && song.Strings is { } strings &&
                (strings.GetValueOrDefault(lang).Name ?? strings.GetValueOrDefault("en").Name) is { Length: > 0 })
            {
                var s = strings.TryGetValue(lang, out var l) && !string.IsNullOrEmpty(l.Name) ? l : strings.GetValueOrDefault("en");
                songs.Add(new SongInfo(id, path, Clean(s.Name), s.AlternateName ?? "", s.SpecialModeName ?? "", s.Locations ?? "", s.AdditionalInfo ?? "",
                    song.Duration > TimeSpan.Zero ? song.Duration : durations.GetValueOrDefault(id)));
                continue;
            }

            var n = names.GetValueOrDefault(id) ?? english.GetValueOrDefault(id);
            TimeSpan? duration = durations.TryGetValue(id, out var d) ? d : null;
            songs.Add(n is null
                ? new SongInfo(id, path, "", "", "", "", "", duration)
                : new SongInfo(id, path, Clean(n.Name), n.AlternateName, n.SpecialName, n.Locations, n.Info, duration));
        }
        log.Information($"Song list: {songs.Count} songs, {songs.Count(s => s.HasName)} named ({(live.Count > 0 ? "Orchestrion" : "built-in list")})");
        return new SongCatalog(songs);
    }

    private static string Clean(string name) => name is "None" or "Null BGM" or "test" or "?" or "???" ? "" : name.Trim();

    private static string? Embedded(string file)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Soundswap.Data.{Path.GetFileNameWithoutExtension(file)}.csv");
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
