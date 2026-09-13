using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Soundswap.Windows;

namespace Soundswap;

/// <summary>
/// The entry point. Dalamud constructs it with any services it asks for in the constructor, and disposes it
/// when the plugin is unloaded, reloaded or updated. Everything subscribed here is unsubscribed in Dispose.
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
    private readonly WindowSystem windows = new("Soundswap");
    private readonly MainWindow main;
    private bool disposed;

    public Plugin(IDalamudPluginInterface pi, ICommandManager commands, IFramework framework, IChatGui chat, IPluginLog log)
    {
        this.pi = pi;
        this.commands = commands;
        this.framework = framework;
        this.chat = chat;
        this.log = log;

        config = LoadConfig(pi, log, chat);
        if (config.Migrate()) Save();

        main = new MainWindow(config, Save);
        windows.AddWindow(main);

        commands.AddHandler(Command, new CommandInfo(OnCommand) { HelpMessage = "Open Soundswap." });
        pi.UiBuilder.Draw += DrawUi;
        pi.UiBuilder.OpenMainUi += OpenMain;
        pi.UiBuilder.OpenConfigUi += OpenMain;
        log.Information("Soundswap loaded");
    }

    /// <summary>
    /// Every save goes through the game's own thread, where the windows also draw. Saving from a background
    /// task races the UI over the same objects.
    /// </summary>
    private void Save()
    {
        if (disposed) return;
        if (framework.IsInFrameworkUpdateThread) config.Save(pi);
        else _ = framework.RunOnFrameworkThread(() => config.Save(pi));
    }

    /// <summary>
    /// Settings that cannot be read are copied aside before anything else happens. Otherwise the first save
    /// writes fresh defaults over the damaged file, and everything in it is gone for good.
    /// </summary>
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

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "":
                main.Toggle();
                break;
            default:
                // An unknown word should never silently do something else. Say what exists.
                chat.Print($"Try {Command}.", "Soundswap");
                break;
        }
    }

    private void DrawUi() => windows.Draw();

    private void OpenMain() => main.IsOpen = true;

    public void Dispose()
    {
        disposed = true;
        pi.UiBuilder.Draw -= DrawUi;
        pi.UiBuilder.OpenMainUi -= OpenMain;
        pi.UiBuilder.OpenConfigUi -= OpenMain;
        commands.RemoveHandler(Command);
        windows.RemoveAllWindows();
    }
}
