using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Soundswap.Core.Build;
using Soundswap.Core.Util;

namespace Soundswap.Core.Mods;

public sealed record ModMetadata(string Name, string Author, string Description, string Version);

/// <summary>One file of the mod, by its path inside the mod folder (backslashes, as Penumbra writes them).</summary>
public sealed record ModFile(string RelativePath, byte[] Bytes);

/// <summary>
/// The layout of a Soundswap music mod in Penumbra's folder format (FileVersion 3; Penumbra 1.7 moves it into
/// meta.json on load). Every song is an option of a multi-select group, so each can be switched on and off, and
/// each song file carries a hash of its content in its name, so a rebuilt song never has to overwrite a file the
/// game may still be playing.
/// </summary>
public static class PenumbraMod
{
    /// <summary>Penumbra's limit for options in one multi-select group.</summary>
    public const int MaxOptionsPerGroup = 32;

    public const string SongFolder = "songs";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static IReadOnlyList<ModFile> SongFiles(IReadOnlyList<BuiltSong> songs) =>
        songs.Select(s => new ModFile(SongPath(s), s.Scd)).ToList();

    public static string SongPath(BuiltSong song)
    {
        var name = Path.GetFileNameWithoutExtension(song.Song.GamePath.Replace('\\', '/'));
        var hash = Convert.ToHexString(SHA256.HashData(song.Scd))[..10].ToLowerInvariant();
        return $"{SongFolder}\\{Naming.SafeFileName(name)}_{hash}.scd";
    }

    /// <summary>meta.json, default_mod.json and one group file per 32 songs.</summary>
    public static IReadOnlyList<ModFile> JsonFiles(ModMetadata meta, IReadOnlyList<BuiltSong> songs)
    {
        var files = new List<ModFile>
        {
            Json("meta.json", new JsonObject
            {
                ["FileVersion"] = 3,
                ["Name"] = meta.Name,
                ["Author"] = meta.Author,
                ["Description"] = meta.Description,
                ["Image"] = "",
                ["Version"] = meta.Version,
                ["Website"] = "",
                ["ModTags"] = new JsonArray("Music", "Soundswap"),
            }),
            Json("default_mod.json", new JsonObject
            {
                ["Version"] = 0,
                ["Files"] = new JsonObject(),
                ["FileSwaps"] = new JsonObject(),
                ["Manipulations"] = new JsonArray(),
            }),
        };

        var chunks = songs.Chunk(MaxOptionsPerGroup).ToList();
        for (var g = 0; g < chunks.Count; g++)
        {
            var groupName = chunks.Count == 1 ? "Songs" : $"Songs {g + 1}";
            var options = new JsonArray();
            ulong defaults = 0;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < chunks[g].Length; i++)
            {
                var song = chunks[g][i];
                var name = Naming.Unique(names, string.IsNullOrWhiteSpace(song.Song.SongName) ? Path.GetFileNameWithoutExtension(song.Song.GamePath) : song.Song.SongName);
                if (song.Song.Enabled) defaults |= 1UL << i;
                options.Add(new JsonObject
                {
                    ["Name"] = name,
                    ["Description"] = Describe(song),
                    ["Priority"] = 0,
                    ["Files"] = new JsonObject { [song.Song.GamePath.Replace('\\', '/').ToLowerInvariant()] = SongPath(song) },
                    ["FileSwaps"] = new JsonObject(),
                    ["Manipulations"] = new JsonArray(),
                });
            }
            files.Add(Json($"group_{g + 1:000}_{Naming.SafeFileName(groupName).ToLowerInvariant()}.json", new JsonObject
            {
                ["Version"] = 0,
                ["Name"] = groupName,
                ["Description"] = "Each option replaces one of the game's songs. Untick one to hear the game's own again.",
                ["Image"] = "",
                ["Page"] = 0,
                ["Priority"] = 0,
                ["Type"] = "Multi",
                ["DefaultSettings"] = defaults,
                ["Options"] = options,
            }));
        }
        return files;
    }

    /// <summary>Writes a .pmp (a zip of the mod folder) that Penumbra can import, via a temporary file.</summary>
    public static void WritePmp(string path, ModMetadata meta, IReadOnlyList<BuiltSong> songs)
    {
        var tmp = path + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
            {
                foreach (var file in JsonFiles(meta, songs).Concat(SongFiles(songs)).DistinctBy(f => f.RelativePath))
                {
                    // Song files are already compressed audio; storing them saves time for nothing lost.
                    var level = file.RelativePath.EndsWith(".json", StringComparison.Ordinal) ? CompressionLevel.Optimal : CompressionLevel.NoCompression;
                    using var s = zip.CreateEntry(file.RelativePath.Replace('\\', '/'), level).Open();
                    s.Write(file.Bytes);
                }
            }
            File.Move(tmp, path, true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    private static string Describe(BuiltSong song)
    {
        var source = song.Song.SourcePath is { Length: > 0 } p ? Path.GetFileName(p) : "your song";
        var loop = song.Loop is { } l ? $", looping {Naming.Time(l.Start)}–{Naming.Time(l.End)}" : ", playing once";
        return $"Replaces “{song.Song.SongName}” ({song.Song.GamePath}) with {source} ({Naming.Time(song.Duration.TotalSeconds)}{loop}). Made with Soundswap.";
    }

    private static ModFile Json(string name, JsonObject json) =>
        new(name, Encoding.UTF8.GetBytes(json.ToJsonString(JsonOptions)));
}
