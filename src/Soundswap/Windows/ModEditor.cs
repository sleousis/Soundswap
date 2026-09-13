using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.DragDrop;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;
using Soundswap.Core.Build;
using Soundswap.Services;

namespace Soundswap.Windows;

/// <summary>The right side: the mod's name and its songs, and the bar at the bottom that builds it.</summary>
public sealed class ModEditor(Studio studio, FileDialogManager dialogs, IDragDropManager dragDrop, Configuration config)
{
    private readonly SongCard cards = new(studio, dialogs, dragDrop, config);
    private bool details;
    private int scrollTo;

    public void Reveal(int bgmId)
    {
        scrollTo = bgmId;
        if (studio.Project.Find(bgmId) is { } song) cards.Unfold(song);
    }

    public void Draw(Vector2 size)
    {
        var barH = ImGui.GetFrameHeight() * 2 + 30 * Ui.Scale;
        // This sits beside the song list. Without a group, the bar would start a new line under that full-height
        // list, below the bottom of the window, where nobody could see or press it.
        using var group = ImRaii.Group();
        using (var child = ImRaii.Child("##editor", size with { Y = size.Y - barH }, false))
        {
            if (child) Songs();
        }
        BuildBar(new Vector2(size.X, barH));
    }

    private void Songs()
    {
        var width = ImGui.GetContentRegionAvail().X - 4 * Ui.Scale;
        var project = studio.Project;

        using (var card = new Card(width))
        {
            Ui.TextColored(Ui.Muted, "YOUR MUSIC MOD");
            var name = project.Name;
            ImGui.SetNextItemWidth(card.Inner - ImGui.GetFrameHeight() - ImGui.GetStyle().ItemSpacing.X);
            if (ImGui.InputTextWithHint("##name", "Name it, e.g. My battle music", ref name, 96))
            {
                project.Name = name;
                studio.Changed();
            }
            ImGui.SameLine();
            if (Ui.IconButton("details", details ? FontAwesomeIcon.ChevronUp : FontAwesomeIcon.Pen, details ? "Fewer details" : "Author, version and description")) details = !details;
            if (details)
            {
                var half = (card.Inner - ImGui.GetStyle().ItemSpacing.X) / 2;
                var author = project.Author;
                ImGui.SetNextItemWidth(half);
                if (ImGui.InputTextWithHint("##author", "Author (your character's name if empty)", ref author, 64))
                {
                    project.Author = author;
                    studio.Changed();
                }
                ImGui.SameLine();
                var version = project.Version;
                ImGui.SetNextItemWidth(half);
                if (ImGui.InputTextWithHint("##version", "Version", ref version, 16))
                {
                    project.Version = version;
                    studio.Changed();
                }
                var description = project.Description;
                if (ImGui.InputTextMultiline("##description", ref description, 1000, new Vector2(card.Inner, ImGui.GetTextLineHeight() * 3.5f)))
                {
                    project.Description = description;
                    studio.Changed();
                }
            }
            if (project.ModDirectory is { } folder)
                Ui.Hint($"In Penumbra as “{folder}”. Building again updates it in place.");
        }

        if (project.Songs.Count == 0)
        {
            var hint = studio.OrchestrionReady && studio.NowPlaying != 0
                ? "Pick a song on the left, or press Replace next to what's playing at the top."
                : "Pick a song on the left: search by name or by the place it plays. Double-click or + adds it here.";
            Ui.EmptyState(FontAwesomeIcon.Music, "No songs yet", hint, width);
            return;
        }

        foreach (var song in project.Songs.ToList())
        {
            if (scrollTo == song.BgmId)
            {
                ImGui.SetScrollHereY(0.1f);
                scrollTo = 0;
            }
            var appear = Ui.Appear("card:" + song.Id, 0.25f);
            using var alpha = ImRaii.PushStyle(ImGuiStyleVar.Alpha, appear);
            cards.Draw(song, width);
        }
    }

    private void BuildBar(Vector2 size)
    {
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddLine(pos, pos + new Vector2(size.X, 0), Ui.Col(Ui.InkLine));
        ImGui.SetCursorScreenPos(pos + new Vector2(0, 8 * Ui.Scale));

        if (studio.Building && studio.Progress is { } p)
        {
            Progress(p, size.X);
            return;
        }

        Outcome(size.X);
        var project = studio.Project;
        var ready = studio.ReadyCount;
        var total = project.Songs.Count;
        var penumbra = studio.PenumbraReady;

        ImGui.AlignTextToFramePadding();
        if (total == 0) Ui.TextColored(Ui.Muted, "Add a song to start.");
        else if (ready == total) Ui.TextColored(Ui.Ok, ready == 1 ? "1 song ready" : $"{ready} songs ready");
        else Ui.TextColored(Ui.Warn, $"{ready} of {total} ready · {total - ready} {(total - ready == 1 ? "needs" : "need")} a file");

        var buildLabel = penumbra ? (project.ModDirectory is null ? "Build & install" : "Build & update") : "Build .pmp";
        var buildW = ImGui.CalcTextSize(buildLabel).X + Ui.IconWidth(FontAwesomeIcon.Magic) + 40 * Ui.Scale;
        var exportW = ImGui.CalcTextSize("Export .pmp").X + Ui.IconWidth(FontAwesomeIcon.FileExport) + 34 * Ui.Scale;
        ImGui.SameLine(ImGui.GetCursorStartPos().X + size.X - buildW - exportW - ImGui.GetStyle().ItemSpacing.X);
        if (Ui.Button("export", "Export .pmp", FontAwesomeIcon.FileExport, enabled: ready > 0, width: exportW, tooltip: "Save the mod as a .pmp file to share or import later."))
        {
            dialogs.SaveFileDialog("Save the mod as", ".pmp", Path.GetFileNameWithoutExtension(studio.DefaultExportPath(project.Name)), ".pmp",
                (ok, path) => { if (ok) studio.Build(false, path); }, studio.ExportFolder);
        }
        ImGui.SameLine();
        var tip = penumbra
            ? "Converts every song, puts the mod in Penumbra and switches it on" + (studio.OrchestrionReady && config.PlayAfterInstall ? ", then plays the first song." : ".")
            : "Penumbra isn't loaded, so the mod is saved as a .pmp in your export folder instead.";
        if (Ui.Button("build", buildLabel, FontAwesomeIcon.Magic, primary: true, enabled: ready > 0, width: buildW, tooltip: tip))
            studio.Build(true, null);
    }

    private void Progress(BuildProgress p, float width)
    {
        var fraction = Ui.Smooth("build:progress", p.Overall, 8f);
        var stage = p.Stage switch
        {
            BuildStage.Reading => "Reading",
            BuildStage.Decoding => "Decoding",
            BuildStage.Matching => "Matching the volume of",
            BuildStage.Encoding => "Encoding",
            _ => "Packing",
        };
        ImGui.AlignTextToFramePadding();
        Ui.Spinner(7 * Ui.Scale, Ui.AccentSoft);
        ImGui.SameLine();
        var text = p.Index >= p.Count ? "Putting the mod together…" : $"{stage} “{p.Song}” · song {p.Index + 1} of {p.Count}";
        ImGui.TextUnformatted(Ui.Ellipsize(text, width - 120 * Ui.Scale));
        ImGui.SameLine(ImGui.GetCursorStartPos().X + width - 90 * Ui.Scale);
        if (Ui.Button("cancel", "Cancel", FontAwesomeIcon.Times, width: 90 * Ui.Scale)) studio.CancelBuild();

        var pos = ImGui.GetCursorScreenPos();
        var h = 8 * Ui.Scale;
        ImGui.Dummy(new Vector2(width, h));
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2(width, h), Ui.Col(Ui.InkDeep), h / 2);
        dl.AddRectFilled(pos, pos + new Vector2(Math.Max(h, width * fraction), h), Ui.Col(Ui.Accent), h / 2);
        var shimmer = (float)(ImGui.GetTime() % 1.6 / 1.6);
        if (!Ui.Reduced)
        {
            var sx = pos.X + width * fraction * shimmer;
            dl.AddRectFilled(new Vector2(Math.Max(pos.X, sx - 24 * Ui.Scale), pos.Y), new Vector2(sx, pos.Y + h), Ui.Col(Ui.Fade(Ui.White, 0.18f)), h / 2);
        }
    }

    private void Outcome(float width)
    {
        if (studio.LastOutcome is not { } o) return;
        var fresh = DateTimeOffset.Now - o.At < TimeSpan.FromMinutes(10);
        if (!fresh) return;

        if (o.Cancelled)
        {
            Ui.TextColored(Ui.Muted, "Build cancelled. Nothing was changed.");
            return;
        }
        if (o.Problem is not null && o.Built == 0)
        {
            Ui.TextColored(Ui.Danger, Ui.Ellipsize(o.Problem + (o.Failed.FirstOrDefault() is { } f ? $" {f.Song.SongName}: {f.Reason}" : ""), width));
            return;
        }

        var what = o.Built == 1 ? "1 song" : $"{o.Built} songs";
        string line;
        if (o.InstalledFolder is not null && o.Problem is null)
            line = o.Collection is not null ? $"Done: {what} in Penumbra and on in “{o.Collection}”." : $"Done: {what} in Penumbra.";
        else if (o.ExportedTo is not null)
            line = $"Saved {Path.GetFileName(o.ExportedTo)} ({what}).";
        else
            line = o.Problem ?? $"Built {what}.";
        Ui.Icon(o.Problem is null ? FontAwesomeIcon.CheckCircle : FontAwesomeIcon.ExclamationTriangle, o.Problem is null ? Ui.Ok : Ui.Warn);
        ImGui.SameLine();
        var buttons = (studio.OrchestrionReady && o.InstalledFolder is not null ? 1 : 0) + (o.ExportedTo is not null ? 1 : 0);
        ImGui.TextUnformatted(Ui.Ellipsize(line, width - buttons * 130 * Ui.Scale - 30 * Ui.Scale));
        if (o.Failed.Count > 0)
        {
            ImGui.SameLine();
            Ui.Chip($"{o.Failed.Count} failed", Ui.Danger, FontAwesomeIcon.ExclamationTriangle, string.Join("\n", o.Failed.Select(f => $"{f.Song.SongName}: {f.Reason}")));
        }
        if (studio.OrchestrionReady && o.InstalledFolder is not null && o.FirstBgmId != 0)
        {
            ImGui.SameLine();
            if (Ui.Button("playgame", "Play in game", FontAwesomeIcon.PlayCircle, tooltip: "Plays the first song through Orchestrion, with your mod applied.")) studio.PlayInGame(o.FirstBgmId);
        }
        if (o.ExportedTo is not null)
        {
            ImGui.SameLine();
            if (Ui.Button("showfile", "Show file", FontAwesomeIcon.FolderOpen)) ShowInExplorer(o.ExportedTo);
        }
    }

    private static void ShowInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Not being able to open Explorer changes nothing about the file.
        }
    }
}
