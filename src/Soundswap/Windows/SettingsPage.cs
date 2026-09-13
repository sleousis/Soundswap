using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Soundswap.Services;

namespace Soundswap.Windows;

public sealed class SettingsPage(Studio studio, Configuration config, Action save, FileDialogManager dialogs)
{
    public void Draw()
    {
        var width = Math.Min(ImGui.GetContentRegionAvail().X, 720 * Ui.Scale);

        using (var card = new Card(width))
        {
            Ui.TextColored(Ui.Muted, "SOUND");
            ImGui.SetNextItemWidth(card.Inner * 0.6f);
            var quality = config.Quality;
            var kbps = QualityKbps(quality);
            if (ImGui.SliderFloat("Quality", ref quality, 0.1f, 1f, $"{quality * 10:0} · about {kbps} kbit/s"))
            {
                config.Quality = MathF.Round(quality * 10) / 10;
                save();
            }
            Ui.Hint($"About {kbps * 60 / 8 / 1000.0:0.0} MB for every minute of a stereo song. The game's own music is around 5 to 6.");

            var match = config.MatchVolumeByDefault;
            if (ImGui.Checkbox("Match the game's volume for new songs", ref match))
            {
                config.MatchVolumeByDefault = match;
                save();
            }

            ImGui.SetNextItemWidth(card.Inner * 0.6f);
            var volume = config.PreviewVolume;
            if (ImGui.SliderFloat("Preview volume", ref volume, 0f, 1f, $"{volume * 100:0}%"))
            {
                config.PreviewVolume = volume;
                studio.Player.SetVolume(volume);
                save();
            }
        }

        using (var card = new Card(width))
        {
            Ui.TextColored(Ui.Muted, "PENUMBRA");
            var enable = config.EnableAfterInstall;
            if (ImGui.Checkbox("Switch the mod on after installing it", ref enable))
            {
                config.EnableAfterInstall = enable;
                save();
            }
            Ui.Hint("In the collection you are using right now.");
            var play = config.PlayAfterInstall;
            if (ImGui.Checkbox("Then play its first song (needs Orchestrion)", ref play))
            {
                config.PlayAfterInstall = play;
                save();
            }
            Ui.Gap(0.5f);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Exported .pmp files go to");
            ImGui.SameLine();
            var folder = studio.ExportFolder;
            if (Ui.Button("folder", Ui.Ellipsize(folder, card.Inner - 260 * Ui.Scale), FontAwesomeIcon.FolderOpen, tooltip: folder))
            {
                dialogs.OpenFolderDialog("Where exported mods go", (ok, path) =>
                {
                    if (!ok) return;
                    config.ExportFolder = path;
                    save();
                }, folder, false);
            }
            if (config.ExportFolder.Length > 0)
            {
                ImGui.SameLine();
                if (Ui.IconButton("resetfolder", FontAwesomeIcon.Undo, "Back to Documents\\Soundswap"))
                {
                    config.ExportFolder = "";
                    save();
                }
            }
        }

        using (var card = new Card(width))
        {
            Ui.TextColored(Ui.Muted, "LOOK AND FEEL");
            var still = config.ReduceMotion;
            if (ImGui.Checkbox("Hold still", ref still))
            {
                config.ReduceMotion = still;
                save();
            }
            Ui.Hint("No fades or easing. Progress and spinners still move, because there they are the information.");
        }

        using (var card = new Card(width))
        {
            Ui.TextColored(Ui.Muted, "ABOUT");
            Ui.Hint($"Song names: {(studio.CatalogSource.Length > 0 ? studio.CatalogSource : "loading")}. Orchestrion: {(studio.OrchestrionReady ? "connected" : "not installed")}. Penumbra: {(studio.PenumbraReady ? "connected" : "not loaded")}.");
            var encoder = Soundswap.Core.Audio.VorbisEncoder.Status;
            if (Soundswap.Core.Audio.VorbisEncoder.NativeAvailable) Ui.Hint($"Encoder: {encoder}.");
            else Ui.TextColored(Ui.Warn, $"Encoder: {encoder}. Layered songs can't be built until this is fixed; reinstalling Soundswap should bring the encoder back.");
            Ui.Hint("Soundswap converts your songs on your PC. The mods it makes are for you: music belongs to its owners, so share them only where you may.");
            if (Ui.Button("reload", "Reload the song list", FontAwesomeIcon.Sync)) studio.RefreshCatalog();
        }
    }

    /// <summary>Rough Vorbis bitrates for stereo at 44.1 kHz by quality step.</summary>
    private static int QualityKbps(float quality) => (int)Math.Round(quality * 10) switch
    {
        <= 1 => 80,
        2 => 96,
        3 => 112,
        4 => 128,
        5 => 160,
        6 => 192,
        7 => 224,
        8 => 256,
        9 => 320,
        _ => 500,
    };
}
