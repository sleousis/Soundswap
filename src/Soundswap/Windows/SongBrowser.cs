using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Soundswap.Core.Catalog;
using Soundswap.Core.Util;
using Soundswap.Services;

namespace Soundswap.Windows;

/// <summary>
/// The game's songs, searchable: every row shows what the song is and where it plays, with play and add buttons
/// on hover. Double-click (or +) puts it in the mod.
/// </summary>
public sealed class SongBrowser(Studio studio)
{
    private static readonly string[] Filters = ["All songs", "In this mod"];

    private string query = "";
    private int filter;
    private Expansion? expansion;
    private IReadOnlyList<SongInfo> results = [];
    private (string, int, Expansion?, SongCatalog?, int) resultsFor;
    private int scrollTo;

    /// <summary>Scroll the list to this song on the next frame.</summary>
    public void Reveal(int bgmId)
    {
        scrollTo = bgmId;
        if (filter == 1 && studio.Project.Find(bgmId) is null) filter = 0;
    }

    public void Draw(Vector2 size)
    {
        using var child = ImRaii.Child("##browser", size, false);
        if (!child) return;

        var width = ImGui.GetContentRegionAvail().X;
        var count = studio.Catalog.Songs.Count;
        ImGui.SetNextItemWidth(width);
        ImGui.InputTextWithHint("##search", studio.CatalogReady ? $"Search {count:N0} songs by name, place or file" : "Reading the song list…", ref query, 128);

        Ui.Segmented("filter", Filters, ref filter);
        ImGui.SameLine();
        var comboW = width - ImGui.GetItemRectSize().X - ImGui.GetStyle().ItemSpacing.X;
        ImGui.SetNextItemWidth(comboW);
        using (var combo = ImRaii.Combo("##expansion", expansion is { } e ? Ui.ExpansionName(e) : "Every expansion"))
        {
            if (combo)
            {
                if (ImGui.Selectable("Every expansion", expansion is null)) expansion = null;
                foreach (var x in Enum.GetValues<Expansion>())
                {
                    if (ImGui.Selectable(Ui.ExpansionName(x), expansion == x)) expansion = x;
                }
            }
        }

        Refresh();
        Ui.Gap(0.4f);
        var footer = ImGui.GetTextLineHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
        var listSize = new Vector2(width, ImGui.GetContentRegionAvail().Y - footer);
        DrawList(listSize);

        var named = studio.CatalogSource.Length > 0 ? $" · names from {studio.CatalogSource}" : "";
        Ui.TextColored(Ui.Muted, Ui.Ellipsize(results.Count == count ? $"{count:N0} songs{named}" : $"{results.Count:N0} of {count:N0} songs{named}", width));
    }

    private void Refresh()
    {
        var key = (query, filter, expansion, studio.Catalog, filter == 1 ? studio.Project.Songs.Count : -1);
        if (key == resultsFor) return;
        resultsFor = key;
        var inMod = studio.Project.Songs.Select(s => s.BgmId).ToHashSet();
        results = studio.Catalog.Search(query, s => (filter == 0 || inMod.Contains(s.Id)) && (expansion is null || s.Expansion == expansion));
    }

    private void DrawList(Vector2 size)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        dl.AddRectFilled(pos, pos + size, Ui.Col(Ui.InkDeep), 8f * Ui.Scale);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(4, 4) * Ui.Scale).Push(ImGuiStyleVar.ItemSpacing, new Vector2(0, 2) * Ui.Scale);
        using var list = ImRaii.Child("##songs", size, false, ImGuiWindowFlags.AlwaysUseWindowPadding);
        if (!list) return;

        if (!studio.CatalogReady)
        {
            Ui.Gap();
            Ui.Spinner(7 * Ui.Scale, Ui.AccentSoft);
            ImGui.SameLine();
            Ui.TextColored(Ui.Muted, "Reading the game's song list…");
            return;
        }
        if (results.Count == 0)
        {
            Ui.EmptyState(FontAwesomeIcon.Search, "No song matches", filter == 1 ? "Songs you add to the mod show up here." : "Try fewer words, a place, or a file name such as bgm_ex5.", ImGui.GetContentRegionAvail().X);
            return;
        }

        var rowH = ImGui.GetTextLineHeight() * 2 + 10 * Ui.Scale;
        var rowStep = rowH + ImGui.GetStyle().ItemSpacing.Y;
        if (scrollTo != 0)
        {
            var index = results.ToList().FindIndex(s => s.Id == scrollTo);
            if (index >= 0) ImGui.SetScrollY(Math.Max(0, index * rowStep - size.Y / 3));
            scrollTo = 0;
        }

        var clipper = ImGui.ImGuiListClipper();
        clipper.Begin(results.Count, rowStep);
        while (clipper.Step())
        {
            for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++) Row(results[i], i, rowH);
        }
        clipper.End();
        clipper.Destroy();
    }

    private void Row(SongInfo song, int index, float height)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var pos = ImGui.GetCursorScreenPos();
        var key = $"row:{song.Id}";
        ImGui.InvisibleButton($"##{key}", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        var hover = Ui.Hover(key);
        Ui.RecordHover(key);

        var inMod = studio.Project.Find(song.Id) is not null;
        var selected = studio.Selected == song.Id;
        var playingNow = studio.NowPlaying == song.Id && song.Id != 0;
        var previewKey = $"orig:{song.Id}";
        var previewing = studio.Player.Playing == previewKey;
        var dl = ImGui.GetWindowDrawList();

        var appear = index < 24 ? Ui.Appear("rowin:" + song.Id, 0.18f + index * 0.011f) : 1f;
        var bg = selected ? Ui.Mix(Ui.InkRaised, Ui.Accent, 0.38f) : Ui.Mix(Ui.Fade(Ui.InkRaised, 0f), Ui.InkRaised, hover);
        dl.AddRectFilled(pos, pos + new Vector2(width, height), Ui.Col(Ui.Fade(bg, appear)), 6f * Ui.Scale);
        dl.AddRectFilled(pos + new Vector2(0, 6 * Ui.Scale), pos + new Vector2(3 * Ui.Scale, height - 6 * Ui.Scale), Ui.Col(Ui.Fade(Ui.ExpansionColor(song.Expansion), appear)), 2f);

        // Right side: buttons on hover, otherwise facts.
        var btn = ImGui.GetFrameHeight();
        var right = pos.X + width - 6 * Ui.Scale;
        var buttons = new List<(FontAwesomeIcon Icon, string Tip, Action Act, bool On)>();
        if (hovered || previewing)
        {
            buttons.Add((previewing ? FontAwesomeIcon.Stop : FontAwesomeIcon.Play, previewing ? "Stop" : "Hear the game's version", () => studio.PreviewOriginal(previewKey, song.GamePath), previewing));
            buttons.Add((inMod ? FontAwesomeIcon.Check : FontAwesomeIcon.Plus, inMod ? "In the mod: show it" : "Add to the mod", () => studio.Add(song), inMod));
        }
        var clickedButton = false;
        var bx = right;
        foreach (var (icon, tip, act, on) in Enumerable.Reverse(buttons))
        {
            bx -= btn;
            var min = new Vector2(bx, pos.Y + (height - btn) / 2);
            var over = ImGui.IsMouseHoveringRect(min, min + new Vector2(btn, btn));
            dl.AddRectFilled(min, min + new Vector2(btn, btn), Ui.Col(on ? Ui.Accent : over ? Ui.Mix(Ui.InkRaised, Ui.Accent, 0.5f) : Ui.Mix(Ui.InkDeep, Ui.InkRaised, 0.6f)), 5f * Ui.Scale);
            var iw = Ui.IconWidth(icon);
            Ui.DrawIcon(dl, icon, min + new Vector2((btn - iw) / 2, (btn - ImGui.GetTextLineHeight()) / 2 + 1), on ? Ui.White : Ui.Cream);
            if (over)
            {
                ImGui.SetTooltip(tip);
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    act();
                    clickedButton = true;
                }
            }
            bx -= 4 * Ui.Scale;
        }

        var line = ImGui.GetTextLineHeight();
        var textX = pos.X + 12 * Ui.Scale;
        var facts = studio.Facts(song.Id, song.GamePath);
        var info = new List<(string Text, Vector4 Color)>();
        if (!hovered && !previewing)
        {
            if (song.Duration is { } d) info.Add((Naming.Time(d.TotalSeconds), Ui.Muted));
            if (facts is { Layers: > 1 } f) info.Add(($"{f.Layers} layers", Ui.Info));
            if (facts?.Problem is not null) info.Add(("silent", Ui.Fade(Ui.Muted, 0.7f)));
            if (inMod) info.Add(("✓ in mod", Ui.AccentSoft));
        }
        var infoX = bx;
        foreach (var (text, color) in info)
        {
            var w = ImGui.CalcTextSize(text).X;
            infoX -= w;
            dl.AddText(new Vector2(infoX, pos.Y + (height - line) / 2), Ui.Col(Ui.Fade(color, appear)), text);
            infoX -= 10 * Ui.Scale;
        }

        var titleW = infoX - textX - 6 * Ui.Scale;
        var title = song.Title;
        if (playingNow)
        {
            var pulse = Ui.Pulse();
            Ui.DrawIcon(dl, FontAwesomeIcon.Music, new Vector2(textX, pos.Y + 5 * Ui.Scale + 1), Ui.Mix(Ui.AccentSoft, Ui.White, pulse));
            textX += Ui.IconWidth(FontAwesomeIcon.Music) + 6 * Ui.Scale;
            titleW -= Ui.IconWidth(FontAwesomeIcon.Music) + 6 * Ui.Scale;
        }
        dl.AddText(new Vector2(textX, pos.Y + 5 * Ui.Scale), Ui.Col(Ui.Fade(song.HasName ? Ui.Cream : Ui.Muted, appear)), Ui.Ellipsize(title, titleW));
        var sub = song.Locations.Length > 0 ? song.Locations.ReplaceLineEndings(" · ") : song.HasName ? song.FileName : song.GamePath;
        if (playingNow) sub = "Playing now · " + sub;
        dl.AddText(new Vector2(pos.X + 12 * Ui.Scale, pos.Y + 5 * Ui.Scale + line), Ui.Col(Ui.Fade(Ui.Muted, appear)), Ui.Ellipsize(sub, infoX - pos.X - 18 * Ui.Scale));

        if (hovered && !clickedButton && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) studio.Add(song);
        else if (hovered && !clickedButton && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) studio.Selected = song.Id;

        if (hovered && !clickedButton && !previewing && ImGui.IsMouseClicked(ImGuiMouseButton.Right)) ImGui.OpenPopup($"##ctx:{song.Id}");
        using var popup = ImRaii.Popup($"##ctx:{song.Id}");
        if (!popup) return;
        Ui.TextColored(Ui.Muted, Ui.Ellipsize(song.Title, 260 * Ui.Scale));
        ImGui.Separator();
        if (ImGui.Selectable(inMod ? "Show in the mod" : "Add to the mod")) studio.Add(song);
        if (ImGui.Selectable("Hear the game's version")) studio.PreviewOriginal(previewKey, song.GamePath);
        if (studio.OrchestrionReady && ImGui.Selectable("Play in game (Orchestrion)")) studio.PlayInGame(song.Id);
        if (ImGui.Selectable("Copy the game path")) ImGui.SetClipboardText(song.GamePath);
    }
}
