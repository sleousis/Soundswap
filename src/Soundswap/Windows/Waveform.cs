using System.Numerics;
using Dalamud.Bindings.ImGui;
using Soundswap.Core.Project;
using Soundswap.Core.Util;

namespace Soundswap.Windows;

/// <summary>
/// A song drawn as its loudness over time: the part kept after trimming bright, the cut parts dimmed, the loop as a
/// band with its two ends marked, and the playhead while a preview plays. Clicking reports where, for seeking.
/// </summary>
public static class Waveform
{
    /// <param name="duration">Length of the whole file in seconds (the envelope spans it).</param>
    /// <param name="trimStart">Seconds cut from the start.</param>
    /// <param name="trimEnd">Where the kept part ends, in file seconds.</param>
    /// <param name="loop">In seconds of the trimmed song.</param>
    /// <param name="playhead">In file seconds, while playing.</param>
    /// <returns>File seconds where the player clicked, or null.</returns>
    public static double? Draw(string id, float[] envelope, Vector2 size, double duration, double trimStart, double trimEnd, LoopSeconds? loop, double? playhead)
    {
        var pos = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton($"##wave:{id}", size);
        var hovered = ImGui.IsItemHovered();
        var clicked = ImGui.IsItemClicked();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + size, Ui.Col(Ui.InkDeep), 6f * Ui.Scale);
        if (envelope.Length == 0 || duration <= 0) return null;

        float X(double seconds) => pos.X + (float)Math.Clamp(seconds / duration, 0, 1) * size.X;
        var grow = Ui.Appear("wave:" + id, 0.45f);
        var mid = pos.Y + size.Y / 2;
        var n = envelope.Length;
        var bar = size.X / n;
        var loopFrom = loop is { } l ? trimStart + l.Start : double.NaN;
        var loopTo = loop is { } k ? trimStart + k.End : double.NaN;

        if (loop is not null)
            dl.AddRectFilled(new Vector2(X(loopFrom), pos.Y), new Vector2(X(loopTo), pos.Y + size.Y), Ui.Col(Ui.Fade(Ui.Accent, 0.13f)));

        for (var i = 0; i < n; i++)
        {
            var t = (i + 0.5) / n * duration;
            var reveal = Math.Clamp(grow * 1.6f - (float)i / n * 0.6f, 0f, 1f);
            var h = Math.Max(1f, envelope[i] * (size.Y - 6 * Ui.Scale) * reveal);
            var kept = t >= trimStart && t <= trimEnd;
            var inLoop = kept && loop is not null && t >= loopFrom && t <= loopTo;
            var color = !kept ? Ui.Fade(Ui.Muted, 0.28f) : inLoop ? Ui.AccentSoft : Ui.Mix(Ui.Accent, Ui.AccentSoft, 0.3f);
            var x = pos.X + i * bar;
            dl.AddRectFilled(new Vector2(x, mid - h / 2), new Vector2(x + Math.Max(1f, bar - 1f), mid + h / 2), Ui.Col(color));
        }

        if (loop is not null)
        {
            foreach (var (at, color) in new[] { (loopFrom, Ui.Ok), (loopTo, Ui.Warn) })
            {
                var x = X(at);
                dl.AddLine(new Vector2(x, pos.Y + 2), new Vector2(x, pos.Y + size.Y - 2), Ui.Col(color), 2f * Ui.Scale);
                dl.AddTriangleFilled(new Vector2(x - 4 * Ui.Scale, pos.Y), new Vector2(x + 4 * Ui.Scale, pos.Y), new Vector2(x, pos.Y + 5 * Ui.Scale), Ui.Col(color));
            }
        }

        if (playhead is { } p)
        {
            var x = X(p);
            dl.AddLine(new Vector2(x, pos.Y), new Vector2(x, pos.Y + size.Y), Ui.Col(Ui.White), 1.5f * Ui.Scale);
        }

        if (!hovered) return null;
        var mouse = ImGui.GetMousePos();
        var seconds = Math.Clamp((mouse.X - pos.X) / size.X, 0, 1) * duration;
        dl.AddLine(new Vector2(mouse.X, pos.Y), new Vector2(mouse.X, pos.Y + size.Y), Ui.Col(Ui.Fade(Ui.Cream, 0.4f)));
        using (Ui.RichTooltip(170))
        {
            ImGui.TextUnformatted(Naming.PreciseTime(seconds));
            Ui.TextColored(Ui.Muted, seconds < trimStart || seconds > trimEnd ? "Trimmed away" : "Click to play from here");
        }
        return clicked ? seconds : null;
    }
}
