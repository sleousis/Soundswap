using Dalamud.Game.Command;
using Dalamud.Interface.DragDrop;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Soundswap.Game;
using Soundswap.Integrations;
using Soundswap.Services;
using Soundswap.Windows;

namespace Soundswap;

/// <summary>
/// The entry point. Idle, Soundswap costs nothing per frame: it draws and polls only while its window or a file
/// picker is open. Reading and converting audio run on background threads.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/soundswap";

    private readonly IDalamudPluginInterface pi;
    private readonly ICommandManager commands;
    private readonly IFramework framework;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly Studio studio;
    private readonly OrchestrionBridge orchestrion;
    private readonly WindowSystem windows = new("Soundswap");
    private readonly MainWindow main;
    private readonly FileDialogManager dialogs = new();
    private bool disposed;

    public Plugin(IDalamudPluginInterface pi, ICommandManager commands, IFramework framework, IChatGui chat, IPluginLog log, IDataManager data,
        IDragDropManager dragDrop, IPlayerState player)
    {
        this.pi = pi;
        this.commands = commands;
        this.framework = framework;
        this.chat = chat;
        this.log = log;

        config = LoadConfig(pi, log, chat);
        if (config.Migrate()) Save();

        // Plugin assemblies are loaded from a stream, so the encoder is told where soundswap_vorbis.dll is.
        if (pi.AssemblyLocation.DirectoryName is { } folder) Soundswap.Core.Audio.VorbisEncoder.UseLibraryFolder(folder);
        log.Information($"Soundswap encodes with {Soundswap.Core.Audio.VorbisEncoder.Status}");

        orchestrion = new OrchestrionBridge(pi, log);
        studio = new Studio(new GameFiles(data, log), data, new PenumbraBridge(pi, log), orchestrion, framework, log, config, Save,
            pi.GetPluginConfigDirectory(), () => player.IsLoaded ? player.CharacterName : "Soundswap");
        // Orchestrion keeps its song names up to date; whenever it starts, take its current list.
        orchestrion.Announced += studio.RefreshCatalog;

        main = new MainWindow(studio, config, Save, dialogs, dragDrop);
        windows.AddWindow(main);

        commands.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Soundswap. \"/soundswap now\" adds the song playing right now (needs Orchestrion). \"/soundswap build\" builds and installs the mod. \"/soundswap preview\" plays (or stops) your first song.",
        });
        pi.UiBuilder.Draw += DrawUi;
        pi.UiBuilder.OpenMainUi += OpenMain;
        pi.UiBuilder.OpenConfigUi += OpenMain;
    }

    private void DrawUi()
    {
        // Polling Penumbra and Orchestrion, and saving, only matter while the window is open.
        if (main.IsOpen) studio.Update();
        windows.Draw();
        dialogs.Draw();
    }

    private void OnCommand(string command, string args)
    {
        // A command must never take the game down with it.
        try
        {
            switch (args.Trim().ToLowerInvariant())
            {
                case "":
                    main.Toggle();
                    break;
                case "now":
                    studio.Update();
                    main.IsOpen = true;
                    if (studio.NowPlaying != 0 && studio.Catalog.ById(studio.NowPlaying) is { } song) studio.Add(song);
                    else chat.Print("Soundswap can see what's playing only with Orchestrion installed.", "Soundswap");
                    break;
                case "build":
                    studio.Update();
                    main.IsOpen = true;
                    if (studio.Building) chat.Print("Soundswap is already building.", "Soundswap");
                    else if (studio.ReadyCount == 0) chat.Print("Nothing to build yet: add a song and choose a file for it.", "Soundswap");
                    else
                    {
                        studio.Build(true, null);
                        chat.Print(studio.PenumbraReady ? "Building your music mod…" : "Penumbra isn't loaded, so the mod is saved as a .pmp instead.", "Soundswap");
                    }
                    break;
                case "preview":
                    // The first song that has a file, as the game will play it; the same command again stops it.
                    if (studio.Project.Songs.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.SourcePath)) is { } first)
                    {
                        studio.PreviewFile($"src:{first.Id}", first, first.SourcePath!, false);
                        chat.Print(studio.Player.Playing is null && studio.PreviewLoading is null ? "Preview stopped." : $"Previewing your song for {first.SongName}.", "Soundswap");
                    }
                    else chat.Print("Nothing to preview yet: add a song and choose a file for it.", "Soundswap");
                    break;
                default:
                    chat.Print($"{Command} opens the window. {Command} now adds the song playing right now. {Command} build builds and installs the mod. {Command} preview plays (or stops) your first song.", "Soundswap");
                    break;
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, $"{Command} {args} failed");
        }
    }

    private void OpenMain() => main.IsOpen = true;

    /// <summary>Every save goes through the framework thread, where the window also edits the settings.</summary>
    private void Save()
    {
        if (disposed) return;
        if (framework.IsInFrameworkUpdateThread) config.Save(pi);
        else _ = framework.RunOnFrameworkThread(() => config.Save(pi));
    }

    /// <summary>Settings that cannot be read are copied aside before a fresh start, so nothing is lost for good.</summary>
    private static Configuration LoadConfig(IDalamudPluginInterface pi, IPluginLog log, IChatGui chat)
    {
        Configuration? loaded = null;
        try { loaded = pi.GetPluginConfig() as Configuration; }
        catch (Exception ex) { log.Error(ex, "Settings could not be read"); }
        if (loaded is not null) return loaded;

        try
        {
            var file = pi.ConfigFile;
            if (file.Exists && file.Length > 0)
            {
                var backup = file.FullName + $".unreadable-{DateTime.Now:yyyyMMdd-HHmmss}";
                file.CopyTo(backup, overwrite: false);
                chat.PrintError($"Soundswap could not read its settings and started fresh. The old file was kept as {Path.GetFileName(backup)}.", "Soundswap");
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Could not keep a copy of the unreadable settings");
        }
        return new Configuration();
    }

    public void Dispose()
    {
        disposed = true;
        pi.UiBuilder.Draw -= DrawUi;
        pi.UiBuilder.OpenMainUi -= OpenMain;
        pi.UiBuilder.OpenConfigUi -= OpenMain;
        commands.RemoveHandler(Command);
        windows.RemoveAllWindows();
        orchestrion.Announced -= studio.RefreshCatalog;
        orchestrion.Dispose();
        studio.Dispose();
    }
}
