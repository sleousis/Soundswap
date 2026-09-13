using Dalamud.Configuration;

namespace Soundswap;

/// <summary>
/// Saved by Dalamud as JSON with Newtonsoft, every object stamped with its type name, under the plugin's
/// InternalName. Only settings live here; the mod being made is kept in its own file (see ProjectStore), so a
/// settings problem can never cost anyone their work. Two rules before adding anything (dalamud:config):
/// loading fills collections in place, and every public property is written, computed ones included.
/// </summary>
public sealed class Configuration : IPluginConfiguration
{
    /// <summary>Bump when <see cref="Migrate"/> gains a step. New configs start here and skip the chain.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Off: things ease and fade. On: they arrive at their end state at once.</summary>
    public bool ReduceMotion { get; set; }

    /// <summary>Vorbis quality, 0..1. 0.5 is about 160 kbit/s for a stereo song.</summary>
    public float Quality { get; set; } = 0.5f;

    /// <summary>Where exported .pmp files go. Empty: Documents\Soundswap.</summary>
    public string ExportFolder { get; set; } = "";

    /// <summary>Switch a freshly installed mod on in the collection in use.</summary>
    public bool EnableAfterInstall { get; set; } = true;

    /// <summary>With Orchestrion installed, play the first replaced song as soon as the mod is in.</summary>
    public bool PlayAfterInstall { get; set; } = true;

    public bool MatchVolumeByDefault { get; set; } = true;

    public float PreviewVolume { get; set; } = 0.7f;

    /// <summary>Folder the file picker opens in.</summary>
    public string LastFolder { get; set; } = "";

    /// <summary>Brings a config written by an older build up to date. Returns whether anything changed.</summary>
    public bool Migrate()
    {
        var changed = false;
        if (Version > CurrentVersion)
        {
            Version = CurrentVersion;
            changed = true;
        }
        Quality = Math.Clamp(Quality, 0.1f, 1f);
        PreviewVolume = Math.Clamp(PreviewVolume, 0f, 1f);
        return changed;
    }

    public void Save(Dalamud.Plugin.IDalamudPluginInterface pi) => pi.SavePluginConfig(this);
}
