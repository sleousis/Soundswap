using System.Collections.Concurrent;
using System.Text.Json;
using Dalamud.Plugin.Services;
using Soundswap.Audio;
using Soundswap.Core.Audio;
using Soundswap.Core.Build;
using Soundswap.Core.Catalog;
using Soundswap.Core.Mods;
using Soundswap.Core.Project;
using Soundswap.Core.Scd;
using Soundswap.Core.Util;
using Soundswap.Game;
using Soundswap.Integrations;

namespace Soundswap.Services;

/// <summary>Building the mod on its own task, then installing it through Penumbra on the framework thread.</summary>
public sealed partial class Studio
{
    // ---------- building ----------

    public int ReadyCount => Project.Songs.Count(s => s.Missing() is null);

    /// <param name="install">Write the mod into Penumbra and load it.</param>
    /// <param name="exportPath">Also save a .pmp here.</param>
    public void Build(bool install, string? exportPath)
    {
        if (Building || Project.Songs.Count == 0) return;
        Player.Stop();
        var snapshot = JsonSerializer.Deserialize<SoundswapProject>(JsonSerializer.Serialize(Project))!;
        var root = install && PenumbraReady ? penumbra.ModRoot() : null;
        if (install && root is null) exportPath ??= DefaultExportPath(snapshot.Name);
        var author = string.IsNullOrWhiteSpace(snapshot.Author) ? defaultAuthor() : snapshot.Author.Trim();

        Building = true;
        LastOutcome = null;
        Progress = new BuildProgress(0, snapshot.Songs.Count, snapshot.Songs[0].SongName, BuildStage.Reading, 0);
        buildCts = new CancellationTokenSource();
        var ct = buildCts.Token;
        Task.Run(() => RunBuild(snapshot, root, exportPath, author, ct), ct);
    }

    public void CancelBuild() => buildCts?.Cancel();

    private void RunBuild(SoundswapProject snapshot, string? root, string? exportPath, string author, CancellationToken ct)
    {
        try
        {
            var result = new ModBuilder(game, AudioFiles.Decoders).Build(snapshot.Songs, new BuildSettings { Quality = config.Quality }, p => Progress = p, ct);
            var notes = result.Built.SelectMany(b => b.Notes.Select(n => (b.Song.SongName, n))).ToList();
            if (!result.Any)
            {
                Finish(new BuildOutcome(DateTimeOffset.Now, 0, result.Failed, notes, null, null, null, "No song could be built.", 0, false));
                return;
            }

            var name = string.IsNullOrWhiteSpace(snapshot.Name) ? "Soundswap music" : snapshot.Name.Trim();
            var meta = new ModMetadata(name, author, Describe(snapshot, result.Built.Count), string.IsNullOrWhiteSpace(snapshot.Version) ? "1.0.0" : snapshot.Version);
            string? exported = null;
            if (exportPath is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(exportPath)!);
                PenumbraMod.WritePmp(exportPath, meta, result.Built);
                exported = exportPath;
            }

            string? folder = null;
            if (root is not null)
            {
                folder = snapshot.ModDirectory is { Length: > 0 } d && Directory.Exists(Path.Combine(root, d)) && ModFolderWriter.IsOurs(Path.Combine(root, d), snapshot.Id)
                    ? d
                    : ModFolderWriter.NewFolderName(root, name);
                var written = ModFolderWriter.Write(Path.Combine(root, folder), snapshot.Id, meta, result.Built);
                notes.AddRange(written.Warnings.Select(w => ("", w)));
            }

            var first = result.Built[0].Song.BgmId;
            _ = framework.RunOnFrameworkThread(() =>
            {
                string? collection = null, problem = null;
                if (folder is not null)
                {
                    var ec = penumbra.AddOrReload(folder);
                    if (ec is PenumbraBridge.Success or PenumbraBridge.NothingChanged)
                    {
                        Project.ModDirectory = folder;
                        Changed();
                        if (config.EnableAfterInstall) collection = penumbra.EnableInCurrentCollection(folder);
                        if (config.PlayAfterInstall && OrchestrionReady) orchestrion.Play(first);
                    }
                    else
                    {
                        problem = $"The mod was written, but Penumbra couldn't load it (code {ec}). Try Reload mods in Penumbra.";
                    }
                }
                foreach (var built in result.Built)
                {
                    if (Project.Songs.FirstOrDefault(s => s.Id == built.Song.Id) is { } live) live.Channels = built.Song.Channels;
                }
                Finish(new BuildOutcome(DateTimeOffset.Now, result.Built.Count, result.Failed, notes, folder, collection, exported, problem, first, false));
            });
        }
        catch (OperationCanceledException)
        {
            Finish(new BuildOutcome(DateTimeOffset.Now, 0, [], [], null, null, null, null, 0, true));
        }
        catch (Exception ex)
        {
            log.Error(ex, "Build failed");
            Finish(new BuildOutcome(DateTimeOffset.Now, 0, [], [], null, null, null, ex.Message, 0, false));
        }
    }

    private void Finish(BuildOutcome outcome)
    {
        LastOutcome = outcome;
        Progress = null;
        Building = false;
    }

    public void PlayInGame(int bgmId)
    {
        if (OrchestrionReady) orchestrion.Play(bgmId);
    }

    public string ExportFolder => string.IsNullOrWhiteSpace(config.ExportFolder)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Soundswap")
        : config.ExportFolder;

    public string DefaultExportPath(string modName)
    {
        var folder = ExportFolder;
        var baseName = Naming.SafeFileName(string.IsNullOrWhiteSpace(modName) ? "Soundswap music" : modName);
        var path = Path.Combine(folder, baseName + ".pmp");
        for (var i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{baseName} ({i}).pmp");
        return path;
    }

    private static string Describe(SoundswapProject project, int songs)
    {
        var text = project.Description.Trim();
        var line = $"{songs} {(songs == 1 ? "song" : "songs")} replaced with Soundswap.";
        return text.Length == 0 ? line : $"{text}\n\n{line}";
    }
}
