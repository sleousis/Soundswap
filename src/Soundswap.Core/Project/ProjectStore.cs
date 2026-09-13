using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soundswap.Core.Project;

/// <summary>
/// Keeps the project being worked on in a file of its own, so it survives reloads and game restarts. A file that
/// can't be read is set aside, never overwritten, so nothing is lost for good.
/// </summary>
public sealed class ProjectStore(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Path { get; } = path;

    /// <summary>The saved project, or null when there is none. <paramref name="problem"/> says why one was set aside.</summary>
    public SoundswapProject? Load(out string? problem)
    {
        problem = null;
        if (!File.Exists(Path)) return null;
        try
        {
            return JsonSerializer.Deserialize<SoundswapProject>(File.ReadAllText(Path), Options);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or IOException)
        {
            var aside = Path + $".unreadable-{DateTime.Now:yyyyMMdd-HHmmss}";
            try { File.Copy(Path, aside, false); } catch (IOException) { }
            problem = $"The saved project couldn't be read ({ex.Message}). It was kept as {System.IO.Path.GetFileName(aside)}.";
            return null;
        }
    }

    public void Save(SoundswapProject project)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var tmp = Path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(fs, project, Options);
            fs.Flush(true);
        }
        File.Move(tmp, Path, true);
    }
}
