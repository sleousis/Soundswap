using Dalamud.Configuration;

namespace Soundswap;

/// <summary>
/// Saved by Dalamud as JSON with Newtonsoft, every object stamped with its type name, under the plugin's
/// InternalName. Two things to know before adding anything here (see the dalamud:config skill):
/// - Loading fills existing objects and collections in place. A collection pre-filled with defaults keeps
///   them and gets the saved items added on top. Clear it in an [OnDeserializing] method.
/// - Every public property is written, computed ones included. Mark those [Newtonsoft.Json.JsonIgnore].
/// </summary>
public sealed class Configuration : IPluginConfiguration
{
    /// <summary>Bump when <see cref="Migrate"/> gains a step. New configs start here and skip the chain.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Off: things ease and fade. On: they arrive at their end state at once.</summary>
    public bool ReduceMotion { get; set; }

    /// <summary>Brings a config written by an older build up to date. Returns whether anything changed.</summary>
    public bool Migrate()
    {
        var changed = false;
        // if (Version < 2)
        // {
        //     ... fix what the old default got wrong ...
        //     Version = 2;
        //     changed = true;
        // }
        return changed;
    }

    public void Save(Dalamud.Plugin.IDalamudPluginInterface pi) => pi.SavePluginConfig(this);
}
