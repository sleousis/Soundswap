using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.DragDrop;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Soundswap.Services;

namespace Soundswap.Windows;

/// <summary>
/// The one window. The studio: songs of the game on the left, the mod on the right, the build bar under it.
/// Settings is the other page, behind the cog in the title bar.
/// </summary>
public sealed class MainWindow : Window
{
    private enum Page { Studio, Settings }

    private readonly Studio studio;
    private readonly Configuration config;
    private readonly SongBrowser browser;
    private readonly ModEditor editor;
    private readonly SettingsPage settings;
    private Page page = Page.Studio;
    private IDisposable? style;
    private int lastSelected;

    public MainWindow(Studio studio, Configuration config, Action save, FileDialogManager dialogs, IDragDropManager dragDrop)
        : base("Soundswap###SoundswapMain", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.studio = studio;
        this.config = config;
        browser = new SongBrowser(studio);
        editor = new ModEditor(studio, dialogs, dragDrop, config);
        settings = new SettingsPage(studio, config, save, dialogs);
        Size = new Vector2(1000, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(720, 420), MaximumSize = new Vector2(4000, 3000) };
        TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.Cog,
            IconOffset = new Vector2(1.5f, 1),
            Click = _ => page = page == Page.Settings ? Page.Studio : Page.Settings,
            ShowTooltip = () => ImGui.SetTooltip(page == Page.Settings ? "Back to the studio" : "Settings"),
        });
    }

    public override void PreDraw()
    {
        Ui.Reduced = config.ReduceMotion;
        style = Ui.PushWindowStyle();
    }

    public override void PostDraw()
    {
        style?.Dispose();
        style = null;
    }

    public override void OnClose() => studio.Player.Stop();

    private DateTime catalogRefreshed = DateTime.UtcNow;

    /// <summary>A song list older than a few minutes is taken again from Orchestrion when the window opens.</summary>
    public override void OnOpen()
    {
        if (DateTime.UtcNow - catalogRefreshed < TimeSpan.FromMinutes(10)) return;
        catalogRefreshed = DateTime.UtcNow;
        studio.RefreshCatalog();
    }

    public override void Draw()
    {
        using var alpha = ImRaii.PushStyle(ImGuiStyleVar.Alpha, Ui.Appear("page:" + page, 0.2f));
        Header();
        if (page == Page.Settings)
        {
            settings.Draw();
            return;
        }

        if (studio.Selected != lastSelected)
        {
            lastSelected = studio.Selected;
            browser.Reveal(studio.Selected);
            editor.Reveal(studio.Selected);
        }

        var avail = ImGui.GetContentRegionAvail();
        var left = Math.Clamp(avail.X * 0.4f, 280 * Ui.Scale, 440 * Ui.Scale);
        browser.Draw(new Vector2(left, avail.Y));
        ImGui.SameLine(0, 14 * Ui.Scale);
        editor.Draw(new Vector2(ImGui.GetContentRegionAvail().X, avail.Y));
    }

    private void Header()
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var r = ImGui.GetFrameHeight() * 0.62f;
        dl.AddCircleFilled(pos + new Vector2(r, ImGui.GetFrameHeight() / 2), r, Ui.Col(Ui.Accent), 32);
        var iw = Ui.IconWidth(FontAwesomeIcon.Music);
        Ui.DrawIcon(dl, FontAwesomeIcon.Music, pos + new Vector2(r - iw / 2, (ImGui.GetFrameHeight() - ImGui.GetTextLineHeight()) / 2 + 1), Ui.White);
        ImGui.Dummy(new Vector2(r * 2, ImGui.GetFrameHeight()));
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Soundswap");
        ImGui.SameLine();
        Ui.TextColored(Ui.Muted, page == Page.Settings ? "Settings" : "Your songs in the game's place");

        // Right side: warnings about what is missing, and what is playing now.
        var right = ImGui.GetCursorStartPos().X + ImGui.GetContentRegionMax().X;
        var items = new List<Action>();
        float widthNeeded = 0;
        if (!studio.PenumbraReady)
        {
            widthNeeded += ImGui.CalcTextSize("Penumbra isn't loaded").X + 40 * Ui.Scale;
            items.Add(() => Ui.Chip("Penumbra isn't loaded", Ui.Warn, FontAwesomeIcon.ExclamationTriangle, "Soundswap can still build mods and save them as .pmp files. Load Penumbra to install them straight away."));
        }
        var playing = studio.NowPlaying != 0 ? studio.Catalog.ById(studio.NowPlaying) : null;
        if (playing is not null && page == Page.Studio)
        {
            var label = Ui.Ellipsize(playing.Title, 220 * Ui.Scale);
            widthNeeded += ImGui.CalcTextSize(label).X + ImGui.CalcTextSize("Replace").X + 90 * Ui.Scale;
            items.Add(() =>
            {
                Ui.Chip(label, Ui.AccentSoft, FontAwesomeIcon.Music, "Playing now (from Orchestrion)");
                ImGui.SameLine();
                var inMod = studio.Project.Find(playing.Id) is not null;
                if (Ui.Button("replacenow", inMod ? "Show" : "Replace", inMod ? FontAwesomeIcon.Eye : FontAwesomeIcon.ExchangeAlt, primary: !inMod,
                        tooltip: inMod ? "It's in your mod: show it." : "Add the song that is playing right now to your mod."))
                    studio.Add(playing);
            });
        }
        if (items.Count > 0)
        {
            ImGui.SameLine(Math.Max(ImGui.GetCursorPosX() + 20 * Ui.Scale, right - widthNeeded));
            for (var i = 0; i < items.Count; i++)
            {
                if (i > 0) ImGui.SameLine();
                items[i]();
            }
        }

        if (studio.Notice is { } notice)
        {
            Ui.TextColored(Ui.Warn, notice);
            ImGui.SameLine();
            if (ImGui.SmallButton("Got it")) studio.Notice = null;
        }
        if (studio.PreviewError is { } error) Ui.TextColored(Ui.Danger, "Preview: " + error);
        Ui.Gap(0.5f);
    }
}
