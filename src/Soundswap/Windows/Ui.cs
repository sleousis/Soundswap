using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Soundswap.Core.Catalog;

namespace Soundswap.Windows;

/// <summary>
/// Soundswap's small design system: colour tokens, scale, the window style, motion helpers keyed by string, and
/// the widgets every page shares. Everything that moves goes through Smooth or Appear, so "Hold still" switches
/// it all off in one place. Built on the dalamud:ui kit.
/// </summary>
public static class Ui
{
    // ---------- tokens ----------

    public static readonly Vector4 Accent = new(0.396f, 0.310f, 0.941f, 1f);      // #654FF0
    public static readonly Vector4 AccentSoft = new(0.663f, 0.612f, 1.0f, 1f);    // #A99CFF
    public static readonly Vector4 Ink = new(0.078f, 0.067f, 0.165f, 0.985f);     // window ground
    public static readonly Vector4 InkDeep = new(0.055f, 0.047f, 0.118f, 1f);     // wells: waveforms, lists
    public static readonly Vector4 InkRaised = new(0.114f, 0.098f, 0.220f, 1f);   // frames, cards
    public static readonly Vector4 InkLine = new(1f, 1f, 1f, 0.08f);
    public static readonly Vector4 InkEdge = new(1f, 1f, 1f, 0.16f);
    public static readonly Vector4 Cream = new(0.945f, 0.933f, 1.0f, 1f);         // text
    public static readonly Vector4 White = new(1f, 1f, 1f, 1f);
    public static readonly Vector4 Muted = new(0.604f, 0.576f, 0.722f, 1f);       // secondary text
    public static readonly Vector4 Danger = new(0.95f, 0.43f, 0.49f, 1f);
    public static readonly Vector4 Ok = new(0.49f, 0.83f, 0.60f, 1f);
    public static readonly Vector4 Warn = new(0.95f, 0.70f, 0.42f, 1f);
    public static readonly Vector4 Info = new(0.56f, 0.73f, 0.92f, 1f);

    public static float Scale => ImGuiHelpers.GlobalScale;
    public static float Rounding => 6f * Scale;
    public static float Space => 8f * Scale;

    public static Vector4 ExpansionColor(Expansion e) => e switch
    {
        Expansion.Heavensward => new Vector4(0.55f, 0.76f, 0.96f, 1f),
        Expansion.Stormblood => new Vector4(0.93f, 0.46f, 0.46f, 1f),
        Expansion.Shadowbringers => new Vector4(0.68f, 0.56f, 0.96f, 1f),
        Expansion.Endwalker => new Vector4(0.94f, 0.78f, 0.44f, 1f),
        Expansion.Dawntrail => new Vector4(0.47f, 0.84f, 0.66f, 1f),
        _ => new Vector4(0.78f, 0.80f, 0.90f, 1f),
    };

    public static string ExpansionShort(Expansion e) => e switch
    {
        Expansion.Heavensward => "HW",
        Expansion.Stormblood => "SB",
        Expansion.Shadowbringers => "ShB",
        Expansion.Endwalker => "EW",
        Expansion.Dawntrail => "DT",
        _ => "ARR",
    };

    public static string ExpansionName(Expansion e) => e switch
    {
        Expansion.Heavensward => "Heavensward",
        Expansion.Stormblood => "Stormblood",
        Expansion.Shadowbringers => "Shadowbringers",
        Expansion.Endwalker => "Endwalker",
        Expansion.Dawntrail => "Dawntrail",
        _ => "A Realm Reborn and later",
    };

    /// <summary>Push in a Window's PreDraw, dispose in PostDraw.</summary>
    public static IDisposable PushWindowStyle()
    {
        var colors = ImRaii.PushColor(ImGuiCol.WindowBg, Ink)
            .Push(ImGuiCol.ChildBg, new Vector4(0, 0, 0, 0))
            .Push(ImGuiCol.PopupBg, InkRaised)
            .Push(ImGuiCol.Border, InkLine)
            .Push(ImGuiCol.FrameBg, InkRaised)
            .Push(ImGuiCol.FrameBgHovered, Mix(InkRaised, Accent, 0.25f))
            .Push(ImGuiCol.FrameBgActive, Mix(InkRaised, Accent, 0.4f))
            .Push(ImGuiCol.Button, InkRaised)
            .Push(ImGuiCol.ButtonHovered, Mix(InkRaised, Accent, 0.35f))
            .Push(ImGuiCol.ButtonActive, Accent)
            .Push(ImGuiCol.CheckMark, AccentSoft)
            .Push(ImGuiCol.SliderGrab, AccentSoft)
            .Push(ImGuiCol.SliderGrabActive, Cream)
            .Push(ImGuiCol.Header, Mix(InkRaised, Accent, 0.2f))
            .Push(ImGuiCol.HeaderHovered, Mix(InkRaised, Accent, 0.35f))
            .Push(ImGuiCol.HeaderActive, Mix(InkRaised, Accent, 0.5f))
            .Push(ImGuiCol.ScrollbarBg, new Vector4(0, 0, 0, 0))
            .Push(ImGuiCol.ScrollbarGrab, Mix(InkRaised, Accent, 0.3f))
            .Push(ImGuiCol.PlotHistogram, Accent)
            .Push(ImGuiCol.Separator, InkLine)
            .Push(ImGuiCol.Text, Cream)
            .Push(ImGuiCol.TextDisabled, Muted);
        var styles = ImRaii.PushStyle(ImGuiStyleVar.WindowRounding, 10f * Scale)
            .Push(ImGuiStyleVar.ChildRounding, Rounding)
            .Push(ImGuiStyleVar.FrameRounding, 5f * Scale)
            .Push(ImGuiStyleVar.PopupRounding, Rounding)
            .Push(ImGuiStyleVar.GrabRounding, 4f * Scale)
            .Push(ImGuiStyleVar.ScrollbarSize, 10f * Scale)
            .Push(ImGuiStyleVar.WindowPadding, new Vector2(14f * Scale, 12f * Scale))
            .Push(ImGuiStyleVar.FramePadding, new Vector2(8f * Scale, 4f * Scale))
            .Push(ImGuiStyleVar.ItemSpacing, new Vector2(8f * Scale, 6f * Scale));
        return new Both(styles, colors);
    }

    private sealed class Both(IDisposable a, IDisposable b) : IDisposable
    {
        public void Dispose() { a.Dispose(); b.Dispose(); }
    }

    // ---------- motion ----------

    /// <summary>Everything that would ease arrives at once. Spinners and progress keep moving: there they are the information.</summary>
    public static bool Reduced { get; set; }

    private static readonly Dictionary<string, float> motion = new();
    private static readonly Dictionary<string, (double First, double Last)> appear = new();
    private static readonly Dictionary<string, bool> hoverLast = new();

    /// <summary>A value that follows its target with an exponential ease, frame-rate independent. Starts at the target.</summary>
    public static float Smooth(string id, float target, float speed = 12f)
    {
        if (Reduced) { motion[id] = target; return target; }
        var dt = Math.Clamp(ImGui.GetIO().DeltaTime, 0f, 0.1f);
        if (!motion.TryGetValue(id, out var v)) v = target;
        v += (target - v) * (1f - MathF.Exp(-speed * dt));
        if (MathF.Abs(v - target) < 0.001f) v = target;
        motion[id] = v;
        return v;
    }

    /// <summary>0 → 1 over the first moments something is on screen; starts over once it has been away for a bit.</summary>
    public static float Appear(string id, float seconds = 0.22f)
    {
        if (Reduced) return 1f;
        var now = ImGui.GetTime();
        if (!appear.TryGetValue(id, out var t) || now - t.Last > 0.3) t = (now, now);
        appear[id] = (t.First, now);
        return EaseOut((float)Math.Clamp((now - t.First) / seconds, 0, 1));
    }

    public static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

    /// <summary>Hover of the last frame, eased. Call RecordHover right after the item.</summary>
    public static float Hover(string id) => Smooth("hover:" + id, hoverLast.GetValueOrDefault(id) ? 1f : 0f, 16f);
    public static void RecordHover(string id) => hoverLast[id] = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
    public static void RecordHover(string id, bool hovered) => hoverLast[id] = hovered;

    /// <summary>0..1..0 once a second while something is busy; still under "Hold still".</summary>
    public static float Pulse(float speed = 3f) => Reduced ? 0.6f : 0.5f + 0.5f * MathF.Sin((float)ImGui.GetTime() * speed);

    public static Vector4 Mix(Vector4 a, Vector4 b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);
    public static Vector4 Fade(Vector4 c, float a) => new(c.X, c.Y, c.Z, c.W * a);
    public static uint Col(Vector4 c) => ImGui.GetColorU32(c);

    // ---------- text ----------

    public static void TextColored(Vector4 color, string text) { using var c = ImRaii.PushColor(ImGuiCol.Text, color); ImGui.TextUnformatted(text); }
    public static void Hint(string text) { using var c = ImRaii.PushColor(ImGuiCol.Text, Muted); ImGui.TextWrapped(text); }
    public static void Gap(float multiple = 1f) => ImGui.Dummy(new Vector2(0, Space * multiple));

    public static void Icon(FontAwesomeIcon icon, Vector4? color = null)
    {
        using var c = ImRaii.PushColor(ImGuiCol.Text, color ?? Cream);
        using var f = ImRaii.PushFont(UiBuilder.IconFont);
        ImGui.TextUnformatted(icon.ToIconString());
    }

    public static float IconWidth(FontAwesomeIcon icon)
    {
        using var f = ImRaii.PushFont(UiBuilder.IconFont);
        return ImGui.CalcTextSize(icon.ToIconString()).X;
    }

    /// <summary>Draws an icon glyph at a point with the draw list.</summary>
    public static void DrawIcon(ImDrawListPtr dl, FontAwesomeIcon icon, Vector2 pos, Vector4 color)
    {
        using var f = ImRaii.PushFont(UiBuilder.IconFont);
        dl.AddText(pos, Col(color), icon.ToIconString());
    }

    /// <summary>Clips with an ellipsis instead of pushing neighbours out of the way.</summary>
    public static string Ellipsize(string text, float width)
    {
        if (width <= 0) return "";
        if (ImGui.CalcTextSize(text).X <= width) return text;
        var lo = 0;
        var hi = text.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (ImGui.CalcTextSize(text[..mid] + "…").X <= width) lo = mid;
            else hi = mid - 1;
        }
        return lo == 0 ? "…" : text[..lo].TrimEnd() + "…";
    }

    public static void Tooltip(string text)
    {
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) return;
        using var t = RichTooltip();
        ImGui.TextWrapped(text);
    }

    /// <summary>A tooltip with padding and a comfortable width that fades in. Dispose to close.</summary>
    public static IDisposable RichTooltip(float width = 320f)
    {
        var min = ImGui.GetItemRectMin();
        var style = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(12f * Scale, 10f * Scale))
            .Push(ImGuiStyleVar.WindowRounding, 8f * Scale)
            .Push(ImGuiStyleVar.Alpha, Appear($"rtip:{min.X:F0},{min.Y:F0}", 0.14f));
        ImGui.SetNextWindowSize(new Vector2(width * Scale, 0));
        ImGui.BeginTooltip();
        return new TooltipScope(style);
    }

    private sealed class TooltipScope(IDisposable style) : IDisposable
    {
        public void Dispose() { ImGui.EndTooltip(); style.Dispose(); }
    }

    // ---------- widgets ----------

    /// <summary>A button with an optional icon, drawn by hand so icon and text sit together. Primary ones are filled with the accent.</summary>
    public static bool Button(string id, string text, FontAwesomeIcon? icon = null, bool primary = false, bool enabled = true, float width = 0,
        string? tooltip = null, Vector4? tint = null)
    {
        var style = ImGui.GetStyle();
        var iconText = icon?.ToIconString() ?? "";
        var iconW = icon is { } ic ? IconWidth(ic) : 0f;
        var textW = text.Length > 0 ? ImGui.CalcTextSize(text).X : 0f;
        var gap = icon is not null && text.Length > 0 ? 6f * Scale : 0f;
        var content = iconW + gap + textW;
        var size = new Vector2(Math.Max(width, content + style.FramePadding.X * 2), ImGui.GetFrameHeight());

        var key = "btn:" + id;
        var hover = enabled ? Hover(key) : 0f;
        var baseColor = primary ? Accent : Mix(InkRaised, tint ?? Accent, 0.1f);
        var bg = primary ? Mix(Accent, AccentSoft, hover * 0.35f) : Mix(baseColor, tint ?? Accent, hover * 0.3f);
        bool clicked;
        using (ImRaii.PushColor(ImGuiCol.Button, bg).Push(ImGuiCol.ButtonHovered, bg).Push(ImGuiCol.ButtonActive, Mix(bg, Ink, 0.25f)))
        using (ImRaii.Disabled(!enabled))
            clicked = ImGui.Button($"##{id}", size);
        RecordHover(key);

        var min = ImGui.GetItemRectMin();
        var dl = ImGui.GetWindowDrawList();
        var fg = Fade(primary ? White : tint ?? Cream, enabled ? 1f : 0.4f);
        var x = min.X + (size.X - content) / 2;
        var y = min.Y + (size.Y - ImGui.GetTextLineHeight()) / 2;
        if (icon is not null)
        {
            using (ImRaii.PushFont(UiBuilder.IconFont)) dl.AddText(new Vector2(x, y + 1), Col(fg), iconText);
            x += iconW + gap;
        }
        if (text.Length > 0) dl.AddText(new Vector2(x, y), Col(fg), text);
        if (tooltip is not null) Tooltip(tooltip);
        return clicked && enabled;
    }

    /// <summary>A square button holding only an icon.</summary>
    public static bool IconButton(string id, FontAwesomeIcon icon, string? tooltip = null, bool enabled = true, Vector4? tint = null, bool active = false)
    {
        var size = ImGui.GetFrameHeight();
        return Button(id, "", icon, primary: active, enabled: enabled, width: size, tooltip: tooltip, tint: tint);
    }

    /// <summary>A small rounded label. Colour and, where given, a glyph carry the meaning together.</summary>
    public static void Chip(string text, Vector4 color, FontAwesomeIcon? icon = null, string? tooltip = null)
    {
        var pad = 7f * Scale;
        var iconW = icon is { } ic ? IconWidth(ic) : 0f;
        var gap = icon is not null && text.Length > 0 ? 5f * Scale : 0f;
        var h = ImGui.GetFrameHeight() * 0.82f;
        var size = new Vector2(pad * 2 + iconW + gap + ImGui.CalcTextSize(text).X, h);
        var pos = ImGui.GetCursorScreenPos() + new Vector2(0, (ImGui.GetFrameHeight() - h) / 2);
        ImGui.Dummy(new Vector2(size.X, ImGui.GetFrameHeight()));
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + size, Col(Fade(color, 0.16f)), h / 2);
        var x = pos.X + pad;
        var y = pos.Y + (h - ImGui.GetTextLineHeight()) / 2;
        if (icon is { } i)
        {
            DrawIcon(dl, i, new Vector2(x, y + 1), color);
            x += iconW + gap;
        }
        dl.AddText(new Vector2(x, y), Col(color), text);
        if (tooltip is not null) Tooltip(tooltip);
    }

    /// <summary>A row of choices where one is on; the highlight slides between them.</summary>
    public static bool Segmented(string id, IReadOnlyList<string> labels, ref int selected, float width = 0, IReadOnlyList<string?>? tooltips = null)
    {
        var pad = 12f * Scale;
        var h = ImGui.GetFrameHeight();
        var natural = labels.Select(l => ImGui.CalcTextSize(l).X + pad * 2).ToArray();
        var total = natural.Sum();
        var avail = width > 0 ? width : ImGui.GetContentRegionAvail().X;
        var widths = total <= avail && width <= 0 ? natural : Enumerable.Repeat(avail / labels.Count, labels.Count).ToArray();
        var full = widths.Sum();

        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2(full, h), Col(InkDeep), 5f * Scale);

        var targetX = widths.Take(Math.Clamp(selected, 0, labels.Count - 1)).Sum();
        var thumbX = Smooth($"seg:{id}:x", targetX, 18f);
        var thumbW = Smooth($"seg:{id}:w", widths[Math.Clamp(selected, 0, labels.Count - 1)], 18f);
        dl.AddRectFilled(pos + new Vector2(thumbX + 2 * Scale, 2 * Scale), pos + new Vector2(thumbX + thumbW - 2 * Scale, h - 2 * Scale), Col(Accent), 4f * Scale);

        var changed = false;
        var x = 0f;
        for (var i = 0; i < labels.Count; i++)
        {
            ImGui.SetCursorScreenPos(pos + new Vector2(x, 0));
            if (ImGui.InvisibleButton($"##{id}:{i}", new Vector2(widths[i], h)) && selected != i)
            {
                selected = i;
                changed = true;
            }
            var hovered = ImGui.IsItemHovered();
            if (tooltips is not null && i < tooltips.Count && tooltips[i] is { } tip) Tooltip(tip);
            var color = i == selected ? White : hovered ? Cream : Muted;
            var tw = ImGui.CalcTextSize(labels[i]).X;
            dl.AddText(pos + new Vector2(x + (widths[i] - tw) / 2, (h - ImGui.GetTextLineHeight()) / 2), Col(color), labels[i]);
            x += widths[i];
        }
        ImGui.SetCursorScreenPos(pos);
        ImGui.Dummy(new Vector2(full, h));
        return changed;
    }

    public static void Spinner(float radius, Vector4 color)
    {
        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(radius * 2 + 2, ImGui.GetFrameHeight());
        ImGui.Dummy(size);
        var center = pos + new Vector2(radius + 1, size.Y / 2);
        var t = (float)ImGui.GetTime() * 6f;
        var dl = ImGui.GetWindowDrawList();
        dl.PathArcTo(center, radius, t, t + MathF.PI * 1.4f, 24);
        dl.PathStroke(Col(color), ImDrawFlags.None, 2f * Scale);
    }

    /// <summary>A quiet heading with a hairline, for sections inside a card.</summary>
    public static void Section(string title, FontAwesomeIcon? icon = null)
    {
        Gap(0.5f);
        if (icon is { } i)
        {
            Icon(i, Muted);
            ImGui.SameLine();
        }
        TextColored(Muted, title.ToUpperInvariant());
        var min = ImGui.GetItemRectMax();
        var right = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X;
        ImGui.GetWindowDrawList().AddLine(new Vector2(min.X + 8 * Scale, min.Y - ImGui.GetTextLineHeight() / 2), new Vector2(right, min.Y - ImGui.GetTextLineHeight() / 2), Col(InkLine));
    }

    /// <summary>Centred icon, title and a line of help, for lists with nothing in them yet.</summary>
    public static void EmptyState(FontAwesomeIcon icon, string title, string body, float width)
    {
        var appear = Appear("empty:" + title, 0.35f);
        using var alpha = ImRaii.PushStyle(ImGuiStyleVar.Alpha, appear);
        Gap(2);
        var start = ImGui.GetCursorPosX();
        var dl = ImGui.GetWindowDrawList();
        var circle = 28f * Scale;
        var pos = ImGui.GetCursorScreenPos() + new Vector2(width / 2, circle);
        dl.AddCircleFilled(pos, circle, Col(Fade(Accent, 0.18f)), 48);
        var iw = IconWidth(icon);
        DrawIcon(dl, icon, pos - new Vector2(iw / 2, ImGui.GetTextLineHeight() / 2), AccentSoft);
        ImGui.Dummy(new Vector2(width, circle * 2 + Space));
        var tw = ImGui.CalcTextSize(title).X;
        ImGui.SetCursorPosX(start + Math.Max(0, (width - tw) / 2));
        ImGui.TextUnformatted(title);
        var wrap = Math.Min(width, 360 * Scale);
        ImGui.SetCursorPosX(start + (width - wrap) / 2);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrap);
        using (ImRaii.PushColor(ImGuiCol.Text, Muted)) ImGui.TextWrapped(body);
        ImGui.PopTextWrapPos();
    }
}

/// <summary>
/// A card: content drawn first into the front channel, then the background sized to it behind (dalamud:ui).
/// Use <see cref="Inner"/> for widths inside it.
/// </summary>
public sealed class Card : IDisposable
{
    private readonly ImDrawListPtr dl;
    private readonly Vector2 start;
    private readonly float width;
    private readonly Vector4 background;
    private readonly Vector4 edge;
    private readonly float pad = 12f * Ui.Scale;

    public Card(float width, Vector4? background = null, Vector4? edge = null)
    {
        this.width = width;
        this.background = background ?? Ui.InkRaised;
        this.edge = edge ?? Ui.InkLine;
        dl = ImGui.GetWindowDrawList();
        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);
        start = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(start + new Vector2(pad, pad));
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Inner);
    }

    public float Inner => width - pad * 2;

    public void Dispose()
    {
        ImGui.PopTextWrapPos();
        ImGui.EndGroup();
        var bottom = ImGui.GetItemRectMax().Y + pad;
        dl.ChannelsSetCurrent(0);
        var max = new Vector2(start.X + width, bottom);
        dl.AddRectFilled(start, max, Ui.Col(background), 8f * Ui.Scale);
        dl.AddRect(start, max, Ui.Col(edge), 8f * Ui.Scale, ImDrawFlags.None, 1f);
        dl.ChannelsMerge();
        ImGui.SetCursorScreenPos(new Vector2(start.X, bottom));
        ImGui.Dummy(new Vector2(width, Ui.Space));
    }
}
