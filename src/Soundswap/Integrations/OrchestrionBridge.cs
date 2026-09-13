using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace Soundswap.Integrations;

/// <summary>Shape of Orchestrion's song strings; Dalamud converts its type to this one over IPC by field names.</summary>
public struct OrchestrionSongStrings
{
    public string Name;
    public string AlternateName;
    public string SpecialModeName;
    public string Locations;
    public string AdditionalInfo;
}

public struct OrchestrionSong
{
    public int Id;
    public Dictionary<string, OrchestrionSongStrings>? Strings;
    public string FilePath;
    public bool FileExists;
    public TimeSpan Duration;
}

/// <summary>
/// The Orchestrion plugin, when installed: its song list (names in every language, kept up to date by its
/// authors), what is playing, and playing a song so a fresh replacement can be heard at once. Everything
/// degrades to nothing without it.
/// </summary>
public sealed class OrchestrionBridge : IDisposable
{
    private readonly IDalamudPluginInterface pi;
    private readonly IPluginLog log;
    private readonly ICallGateSubscriber<List<OrchestrionSong>> allSongs;
    private readonly ICallGateSubscriber<int> currentSong;
    private readonly ICallGateSubscriber<int, bool> playSong;
    private readonly ICallGateSubscriber<bool> announced;

    public OrchestrionBridge(IDalamudPluginInterface pi, IPluginLog log)
    {
        this.pi = pi;
        this.log = log;
        allSongs = pi.GetIpcSubscriber<List<OrchestrionSong>>("Orch.AllSongInfo");
        currentSong = pi.GetIpcSubscriber<int>("Orch.CurrentSong");
        playSong = pi.GetIpcSubscriber<int, bool>("Orch.PlaySong");
        // A message Orchestrion sends once it has started (and loaded its song list), not a function to ask.
        announced = pi.GetIpcSubscriber<bool>("Orch.Available");
        announced.Subscribe(OnAnnounced);
    }

    /// <summary>Orchestrion (re)started: its song list may have changed. Raised on the framework thread.</summary>
    public event Action? Announced;

    /// <summary>
    /// Loaded and answering. "Orch.Available" can't tell: it is the start-up message above, so asking it as a
    /// function always fails. The CurrentSong function answers whenever Orchestrion is ready.
    /// </summary>
    public bool Available
    {
        get
        {
            try
            {
                if (!pi.InstalledPlugins.Any(p => p.InternalName.Equals("orchestrion", StringComparison.OrdinalIgnoreCase) && p.IsLoaded)) return false;
                currentSong.InvokeFunc();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public IReadOnlyList<OrchestrionSong> Songs()
    {
        try
        {
            return allSongs.InvokeFunc() ?? [];
        }
        catch (Exception ex)
        {
            log.Debug(ex, "Orch.AllSongInfo unavailable");
            return [];
        }
    }

    /// <summary>The BGM id playing now, or 0.</summary>
    public int NowPlaying()
    {
        try
        {
            return currentSong.InvokeFunc();
        }
        catch (Exception)
        {
            return 0;
        }
    }

    public bool Play(int bgmId)
    {
        try
        {
            return playSong.InvokeFunc(bgmId);
        }
        catch (Exception ex)
        {
            log.Debug(ex, "Orch.PlaySong unavailable");
            return false;
        }
    }

    private void OnAnnounced()
    {
        try
        {
            Announced?.Invoke();
        }
        catch (Exception ex)
        {
            // Never throw back into Orchestrion.
            log.Error(ex, "Refreshing the song list after Orchestrion started failed");
        }
    }

    public void Dispose() => announced.Unsubscribe(OnAnnounced);
}
