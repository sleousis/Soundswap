using Soundswap.Core.Build;

namespace Soundswap.Core.Mods;

public sealed record ModFolderResult(string Folder, int SongsWritten, int FilesRemoved, IReadOnlyList<string> Warnings);

/// <summary>
/// Writes a Soundswap mod straight into Penumbra's mod folder, and updates it in place when rebuilt:
/// new song files first (named by content, so nothing the game is playing is overwritten), then the JSON, then
/// song files nothing points at any more. A file the game still holds open is left for the next build.
/// </summary>
public static class ModFolderWriter
{
    /// <summary>Marks a folder as made by Soundswap for this project, so it is never mistaken for someone else's mod.</summary>
    public const string OwnerFile = ".soundswap";

    public static bool IsOurs(string folder, Guid projectId)
    {
        var owner = Path.Combine(folder, OwnerFile);
        try
        {
            return File.Exists(owner) && Guid.TryParse(File.ReadAllText(owner).Trim(), out var id) && id == projectId;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>A folder name under <paramref name="modRoot"/> for a new mod: the mod's name, made unique.</summary>
    public static string NewFolderName(string modRoot, string modName)
    {
        var baseName = Util.Naming.SafeFileName(modName);
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "Soundswap music";
        var name = baseName;
        for (var i = 2; Directory.Exists(Path.Combine(modRoot, name)) || File.Exists(Path.Combine(modRoot, name)); i++) name = $"{baseName} ({i})";
        return name;
    }

    public static ModFolderResult Write(string folder, Guid projectId, ModMetadata meta, IReadOnlyList<BuiltSong> songs)
    {
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any() && !IsOurs(folder, projectId))
            throw new InvalidOperationException($"{folder} already holds a mod Soundswap didn't make for this project; it is left alone.");

        Directory.CreateDirectory(Path.Combine(folder, PenumbraMod.SongFolder));
        var warnings = new List<string>();
        var songFiles = PenumbraMod.SongFiles(songs);
        var written = 0;
        foreach (var file in songFiles)
        {
            var full = Full(folder, file.RelativePath);
            if (File.Exists(full) && new FileInfo(full).Length == file.Bytes.Length) continue;
            WriteDurably(full, file.Bytes);
            written++;
        }

        // Penumbra 1.7 keeps everything in meta.json; older layouts had these files. Ours are written fresh.
        foreach (var old in Directory.EnumerateFiles(folder, "group_*.json")) TryDelete(old, warnings);
        foreach (var file in PenumbraMod.JsonFiles(meta, songs)) WriteDurably(Full(folder, file.RelativePath), file.Bytes);
        WriteDurably(Path.Combine(folder, OwnerFile), System.Text.Encoding.UTF8.GetBytes(projectId.ToString()));

        var keep = new HashSet<string>(songFiles.Select(f => Full(folder, f.RelativePath)), StringComparer.OrdinalIgnoreCase);
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(folder, PenumbraMod.SongFolder), "*.scd"))
        {
            if (keep.Contains(Path.GetFullPath(file))) continue;
            if (TryDelete(file, warnings)) removed++;
        }
        return new ModFolderResult(folder, written, removed, warnings);
    }

    private static string Full(string folder, string relative)
    {
        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('\\', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"{relative} would land outside the mod folder.");
        return full;
    }

    /// <summary>Flushed all the way to disk, then moved over the old file, so a crash leaves one or the other.</summary>
    private static void WriteDurably(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.WriteThrough))
        {
            fs.Write(bytes);
            fs.Flush(true);
        }
        File.Move(tmp, path, true);
    }

    private static bool TryDelete(string path, List<string> warnings)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"{Path.GetFileName(path)} is still in use and was left; the next build removes it.");
            return false;
        }
    }
}
