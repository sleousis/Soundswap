using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace Soundswap.Integrations;

/// <summary>
/// Penumbra over its raw IPC labels (API 5), with plain types. The Penumbra.Api package isn't used: since 5.19
/// its subscribers run on the Luna library, whose logging dependency can't be resolved from a plugin's load
/// context, and the plugin failed to start. Dalamud converts Penumbra's enums to the ints and bytes used here.
/// Every call is wrapped: when Penumbra is missing, unloading or too old, calls return nothing and Soundswap still
/// builds and exports mods. Calls that change Penumbra's state belong on the framework thread.
/// </summary>
public sealed class PenumbraBridge(IDalamudPluginInterface pi, IPluginLog log)
{
    /// <summary>PenumbraApiEc values this bridge looks at.</summary>
    public const int Success = 0;
    public const int NothingChanged = 1;
    public const int ModMissing = 3;
    public const int UnknownError = 255;

    /// <summary>ApiCollectionType.Current: the collection the player is using now.</summary>
    private const byte CurrentCollection = 0xE2;

    private readonly ICallGateSubscriber<(int, int)> apiVersion = pi.GetIpcSubscriber<(int, int)>("Penumbra.ApiVersion.V5");
    private readonly ICallGateSubscriber<string> getModDirectory = pi.GetIpcSubscriber<string>("Penumbra.GetModDirectory");
    private readonly ICallGateSubscriber<string, int> addMod = pi.GetIpcSubscriber<string, int>("Penumbra.AddMod.V5");
    private readonly ICallGateSubscriber<string, string, int> reloadMod = pi.GetIpcSubscriber<string, string, int>("Penumbra.ReloadMod.V5");
    private readonly ICallGateSubscriber<string, int> installMod = pi.GetIpcSubscriber<string, int>("Penumbra.InstallMod.V5");
    private readonly ICallGateSubscriber<byte, (Guid Id, string Name)?> getCollection = pi.GetIpcSubscriber<byte, (Guid Id, string Name)?>("Penumbra.GetCollection");
    private readonly ICallGateSubscriber<Guid, string, bool, string, int> trySetMod = pi.GetIpcSubscriber<Guid, string, bool, string, int>("Penumbra.TrySetMod.V5");

    /// <summary>Installed, loaded and speaking API 5.</summary>
    public bool Available
    {
        get
        {
            try
            {
                if (!pi.InstalledPlugins.Any(p => p.InternalName == "Penumbra" && p.IsLoaded)) return false;
                return apiVersion.InvokeFunc().Item1 == 5;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>Penumbra's mod root folder, when it is set and exists.</summary>
    public string? ModRoot()
    {
        try
        {
            var dir = getModDirectory.InvokeFunc();
            return string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir) ? null : dir;
        }
        catch (Exception ex)
        {
            log.Debug(ex, "Penumbra.GetModDirectory unavailable");
            return null;
        }
    }

    /// <summary>Makes Penumbra read the mod in <paramref name="folderName"/>: reloads it when known, adds it otherwise.</summary>
    public int AddOrReload(string folderName)
    {
        try
        {
            var ec = reloadMod.InvokeFunc(folderName, "");
            if (ec == Success) return ec;
            ec = addMod.InvokeFunc(folderName);
            if (ec != Success) log.Warning($"Penumbra could not add {folderName} (code {ec})");
            return ec;
        }
        catch (Exception ex)
        {
            log.Warning(ex, $"Penumbra could not load {folderName}");
            return UnknownError;
        }
    }

    /// <summary>Switches the mod on in the collection the player is using now. Returns the collection's name.</summary>
    public string? EnableInCurrentCollection(string folderName)
    {
        try
        {
            if (getCollection.InvokeFunc(CurrentCollection) is not { } collection) return null;
            var ec = trySetMod.InvokeFunc(collection.Id, folderName, true, "");
            if (ec is Success or NothingChanged) return collection.Name;
            log.Warning($"Penumbra could not switch {folderName} on in {collection.Name} (code {ec})");
            return null;
        }
        catch (Exception ex)
        {
            log.Warning(ex, $"Penumbra could not switch {folderName} on");
            return null;
        }
    }

    /// <summary>Queues a mod file for import, as if the player had picked it.</summary>
    public bool Install(string path)
    {
        try
        {
            return installMod.InvokeFunc(path) == Success;
        }
        catch (Exception ex)
        {
            log.Warning(ex, $"Penumbra could not import {Path.GetFileName(path)}");
            return false;
        }
    }
}
