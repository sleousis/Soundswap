using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.DragDrop;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;
using Soundswap.Audio;
using Soundswap.Core.Project;
using Soundswap.Core.Util;
using Soundswap.Services;

namespace Soundswap.Windows;

/// <summary>
/// One song of the mod: which file replaces it (drop one in or browse), a waveform with the trim and loop, the
/// layers of a layered song, the loop, and the volume. Every change saves itself.
/// </summary>
public sealed class SongCard(Studio studio, FileDialogManager dialogs, IDragDropManager dragDrop, Configuration config)
{
    private static readonly string[] LayerModes = ["Every layer", "First layer", "Each layer"];
    private static readonly string[] LayerModeTips =
    [
        "Your song on every layer: it keeps playing whichever layers the game brings in during a fight.",
        "Your song on the first layer; the others go silent.",
        "Choose per layer: a song, silence, or the game's own layer. Good for a calm and an intense version.",
    ];
    private static readonly string[] LayerSources = ["Song", "Silence", "Game's"];
    private static readonly string[] Loops = ["Smart", "Whole song", "Custom", "Play once"];
    private static readonly string[] LoopTips =
    [
        "Uses the loop stored in the file (LOOPSTART tags) if it has one, otherwise loops the whole song.",
        "When the song ends it starts again from the beginning.",
        "Pick where the loop starts and ends.",
        "Plays to the end once, then stays quiet like the game's one-off pieces.",
    ];

    private readonly HashSet<Guid> open = [];
    private Guid? pendingRemove;

    public void Unfold(SongReplacement song) => open.Add(song.Id);

    public void Draw(SongReplacement song, float width)
    {
        using var id = ImRaii.PushId(song.Id.ToString());
        var selected = studio.Selected == song.BgmId;
        using var card = new Card(width, selected ? Ui.Mix(Ui.InkRaised, Ui.Accent, 0.1f) : Ui.InkRaised, selected ? Ui.Fade(Ui.Accent, 0.6f) : Ui.InkLine);
        Header(song, card.Inner);
        if (!open.Contains(song.Id)) return;

        Ui.Gap(0.4f);
        FileSlot(song, card.Inner);
        if (song.IsLayered) Layers(song, card.Inner);
        LoopAndTrim(song, card.Inner);
        Volume(song, card.Inner);
        Footer(song);
    }

    private void Header(SongReplacement song, float width)
    {
        var info = studio.Catalog.ById(song.BgmId);
        var facts = studio.Facts(song.BgmId, song.GamePath);
        var isOpen = open.Contains(song.Id);

        if (Ui.IconButton("fold", isOpen ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronRight, isOpen ? "Fold" : "Unfold"))
        {
            if (!open.Remove(song.Id)) open.Add(song.Id);
            studio.Selected = song.BgmId;
        }
        ImGui.SameLine();
        var start = ImGui.GetCursorPosX();
        var buttonsW = ImGui.GetFrameHeight() * 2 + ImGui.GetStyle().ItemSpacing.X * 2;
        var missing = song.Missing();
        var status = missing is null ? ("Ready", Ui.Ok) : ("Needs a file", Ui.Warn);
        var chipW = ImGui.CalcTextSize(status.Item1).X + 16 * Ui.Scale + (song.IsLayered ? ImGui.CalcTextSize($"{song.LayerCount} layers").X + 22 * Ui.Scale : 0);
        var titleW = width - start - buttonsW - chipW + ImGui.GetCursorStartPos().X;

        ImGui.BeginGroup();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(Ui.Ellipsize(song.SongName.Length > 0 ? song.SongName : song.GamePath, titleW));
        if (ImGui.IsItemClicked())
        {
            if (!open.Remove(song.Id)) open.Add(song.Id);
            studio.Selected = song.BgmId;
        }
        ImGui.EndGroup();
        ImGui.SameLine();
        if (song.IsLayered)
        {
            Ui.Chip($"{song.LayerCount} layers", Ui.Info, FontAwesomeIcon.LayerGroup, "A layered song: the game fades between its layers during play. Choose what each one gets below.");
            ImGui.SameLine();
        }
        Ui.Chip(status.Item1, status.Item2, tooltip: missing);

        ImGui.SameLine(ImGui.GetCursorStartPos().X + width - buttonsW + ImGui.GetStyle().ItemSpacing.X);
        var key = $"orig:{song.BgmId}";
        var playing = studio.Player.Playing == key;
        if (Ui.IconButton("orig", playing ? FontAwesomeIcon.Stop : FontAwesomeIcon.Play, playing ? "Stop" : "Hear the game's version", facts?.Problem is null, active: playing))
            studio.PreviewOriginal(key, song.GamePath);
        ImGui.SameLine();
        if (pendingRemove == song.Id)
        {
            if (Ui.IconButton("remove", FontAwesomeIcon.Check, "Click again to remove this song from the mod", tint: Ui.Danger))
            {
                pendingRemove = null;
                studio.Remove(song);
            }
        }
        else if (Ui.IconButton("remove", FontAwesomeIcon.Trash, "Remove from the mod"))
        {
            pendingRemove = song.Id;
        }

        var sub = info?.Locations is { Length: > 0 } places ? places.ReplaceLineEndings(" · ") : song.GamePath;
        if (facts?.Seconds > 0) sub = $"{Naming.Time(facts.Seconds)} · {sub}";
        ImGui.SetCursorPosX(start);
        Ui.TextColored(Ui.Muted, Ui.Ellipsize(sub, width - start + ImGui.GetCursorStartPos().X));
    }

    private void FileSlot(SongReplacement song, float width)
    {
        var path = song.SourcePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            DropZone(song, -1, width, 64 * Ui.Scale);
            return;
        }

        var src = studio.Source(path);
        Ui.Icon(FontAwesomeIcon.FileAudio, Ui.AccentSoft);
        ImGui.SameLine();
        var buttons = ImGui.GetFrameHeight() * 3 + ImGui.GetStyle().ItemSpacing.X * 3;
        ImGui.BeginGroup();
        ImGui.TextUnformatted(Ui.Ellipsize(Path.GetFileName(path), width - buttons - 30 * Ui.Scale));
        if (src is null || !src.Ready)
        {
            Ui.TextColored(Ui.Muted, "Reading the song…");
        }
        else if (src.Error is not null)
        {
            Ui.TextColored(Ui.Danger, Ui.Ellipsize(src.Error, width - buttons - 30 * Ui.Scale));
        }
        else
        {
            var channels = src.Channels switch { 1 => "mono", 2 => "stereo", _ => $"{src.Channels} channels" };
            var loop = src.TagLoop is not null ? " · has loop points" : "";
            Ui.TextColored(Ui.Muted, $"{Naming.Time(src.Seconds)} · {src.SampleRate / 1000.0:0.#} kHz · {channels}{loop}");
        }
        ImGui.EndGroup();
        Ui.Tooltip(path);
        ImGui.SameLine(ImGui.GetCursorStartPos().X + width - buttons + 6 * Ui.Scale);
        var key = $"src:{song.Id}";
        var playing = studio.Player.Playing == key;
        if (Ui.IconButton("play", playing ? FontAwesomeIcon.Stop : FontAwesomeIcon.Play, playing ? "Stop" : "Hear it as the game will play it (volume matched, trimmed)", src is { Ready: true, Error: null }, active: playing))
            studio.PreviewFile(key, song, path, false);
        ImGui.SameLine();
        if (Ui.IconButton("change", FontAwesomeIcon.FolderOpen, "Choose another file")) Browse(song, -1);
        ImGui.SameLine();
        if (Ui.IconButton("clear", FontAwesomeIcon.Times, "Clear"))
        {
            song.SourcePath = null;
            studio.Changed();
            return;
        }
        if (dragDrop.CreateImGuiTarget("SoundswapAudio", out var files, out _) && files.FirstOrDefault(AudioFiles.LooksLikeAudio) is { } dropped)
            studio.SetFile(song, dropped);

        if (src is { Ready: true, Error: null, Envelope.Length: > 0 })
        {
            Ui.Gap(0.3f);
            var trimEnd = song.TrimEnd ?? src.Seconds;
            var loop = LoopResolver.Resolve(song, Math.Max(0, trimEnd - song.TrimStart), src.TagLoop);
            double? head = playing ? song.TrimStart + studio.Player.Position : null;
            var clicked = Waveform.Draw(song.Id.ToString(), src.Envelope, new Vector2(width, 52 * Ui.Scale), src.Seconds, song.TrimStart, trimEnd, loop, head);
            if (clicked is { } at)
            {
                if (playing) studio.Player.Seek(Math.Max(0, at - song.TrimStart));
                else
                {
                    studio.PreviewFile(key, song, path, false);
                    pendingSeek = (key, Math.Max(0, at - song.TrimStart));
                }
            }
            if (pendingSeek is { } ps && studio.Player.Playing == ps.Key)
            {
                studio.Player.Seek(ps.Seconds);
                pendingSeek = null;
            }
        }
    }

    private (string Key, double Seconds)? pendingSeek;

    /// <summary>Where a file goes: click to browse, or drop one from Explorer.</summary>
    private void DropZone(SongReplacement song, int layer, float width, float height)
    {
        var pos = ImGui.GetCursorScreenPos();
        var key = $"drop:{song.Id}:{layer}";
        if (ImGui.InvisibleButton($"##{key}", new Vector2(width, height))) Browse(song, layer);
        var hover = Ui.Hover(key);
        Ui.RecordHover(key);
        if (dragDrop.CreateImGuiTarget("SoundswapAudio", out var files, out _) && files.FirstOrDefault(AudioFiles.LooksLikeAudio) is { } dropped)
            studio.SetFile(song, dropped, layer);

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(pos, pos + new Vector2(width, height), Ui.Col(Ui.Mix(Ui.InkDeep, Ui.Accent, 0.08f + hover * 0.12f)), 8f * Ui.Scale);
        // Dashed edge, drawn as short strokes.
        var color = Ui.Col(Ui.Fade(Ui.AccentSoft, 0.35f + hover * 0.4f));
        var dash = 7f * Ui.Scale;
        for (var x = pos.X + 6; x < pos.X + width - 6; x += dash * 2)
        {
            dl.AddLine(new Vector2(x, pos.Y), new Vector2(Math.Min(x + dash, pos.X + width - 6), pos.Y), color, 1.2f);
            dl.AddLine(new Vector2(x, pos.Y + height), new Vector2(Math.Min(x + dash, pos.X + width - 6), pos.Y + height), color, 1.2f);
        }
        for (var y = pos.Y + 6; y < pos.Y + height - 6; y += dash * 2)
        {
            dl.AddLine(new Vector2(pos.X, y), new Vector2(pos.X, Math.Min(y + dash, pos.Y + height - 6)), color, 1.2f);
            dl.AddLine(new Vector2(pos.X + width, y), new Vector2(pos.X + width, Math.Min(y + dash, pos.Y + height - 6)), color, 1.2f);
        }

        var title = layer < 0 ? "Drop your song here, or click to choose one" : $"Drop a song for layer {layer + 1}, or click to choose";
        var hint = "MP3, WAV, OGG, FLAC, M4A, AAC, MP4, WMA…";
        var iw = Ui.IconWidth(FontAwesomeIcon.FileAudio);
        var tw = ImGui.CalcTextSize(title).X;
        var line = ImGui.GetTextLineHeight();
        var twoLines = height > line * 2.6f;
        var y0 = pos.Y + (height - (twoLines ? line * 2 + 2 : line)) / 2;
        var x0 = pos.X + (width - (iw + 8 * Ui.Scale + tw)) / 2;
        Ui.DrawIcon(dl, FontAwesomeIcon.FileAudio, new Vector2(x0, y0 + 1), Ui.Mix(Ui.AccentSoft, Ui.White, hover));
        dl.AddText(new Vector2(x0 + iw + 8 * Ui.Scale, y0), Ui.Col(Ui.Cream), title);
        if (twoLines)
        {
            var hw = ImGui.CalcTextSize(hint).X;
            dl.AddText(new Vector2(pos.X + (width - hw) / 2, y0 + line + 2), Ui.Col(Ui.Muted), hint);
        }
    }

    private void Browse(SongReplacement song, int layer)
    {
        var start = Directory.Exists(config.LastFolder) ? config.LastFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        dialogs.OpenFileDialog(layer < 0 ? $"A song to replace “{song.SongName}”" : $"Layer {layer + 1} of “{song.SongName}”", AudioFiles.DialogFilter, (ok, paths) =>
        {
            if (ok && paths.FirstOrDefault() is { } path) studio.SetFile(song, path, layer);
        }, 1, start);
    }

    private void Layers(SongReplacement song, float width)
    {
        Ui.Section("Layers", FontAwesomeIcon.LayerGroup);
        var mode = (int)song.LayerMode;
        if (Ui.Segmented("layers", LayerModes, ref mode, 0, LayerModeTips))
        {
            song.LayerMode = (LayerMode)mode;
            studio.Changed();
        }
        Ui.Hint(LayerModeTips[mode]);
        if (song.LayerMode != LayerMode.PerLayer) return;

        for (var layer = 0; layer < song.LayerCount; layer++)
        {
            using var lid = ImRaii.PushId($"layer{layer}");
            var choice = song.Layer(layer);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted($"Layer {layer + 1}");
            ImGui.SameLine(70 * Ui.Scale + ImGui.GetCursorStartPos().X);
            var source = (int)choice.Source;
            if (Ui.Segmented("src", LayerSources, ref source, 210 * Ui.Scale))
            {
                choice.Source = (LayerSource)source;
                studio.Changed();
            }
            ImGui.SameLine();
            var key = $"orig:{song.BgmId}:{layer}";
            var playing = studio.Player.Playing == key;
            if (Ui.IconButton("hear", playing ? FontAwesomeIcon.Stop : FontAwesomeIcon.Headphones, playing ? "Stop" : "Hear the game's version of this layer", active: playing))
                studio.PreviewOriginal(key, song.GamePath, layer);
            if (choice.Source != LayerSource.Song) continue;

            ImGui.SameLine();
            var file = choice.Path ?? song.SourcePath;
            var label = choice.Path is null ? (song.SourcePath is null ? "Choose a file…" : $"Main song ({Path.GetFileName(song.SourcePath)})") : Path.GetFileName(choice.Path);
            var rest = ImGui.GetContentRegionAvail().X - (choice.Path is null ? 0 : ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.X);
            if (Ui.Button("file", Ui.Ellipsize(label, rest - 40 * Ui.Scale), FontAwesomeIcon.FolderOpen, width: rest, tooltip: file ?? "Choose the song for this layer")) Browse(song, layer);
            if (dragDrop.CreateImGuiTarget("SoundswapAudio", out var files, out _) && files.FirstOrDefault(AudioFiles.LooksLikeAudio) is { } dropped)
                studio.SetFile(song, dropped, layer);
            if (choice.Path is not null)
            {
                ImGui.SameLine();
                if (Ui.IconButton("main", FontAwesomeIcon.Undo, "Use the main song again"))
                {
                    choice.Path = null;
                    studio.Changed();
                }
            }
        }
    }

    private void LoopAndTrim(SongReplacement song, float width)
    {
        Ui.Section("Loop", FontAwesomeIcon.Repeat);
        var src = studio.Source(song.SourcePath);
        var loop = (int)song.Loop;
        if (Ui.Segmented("loop", Loops, ref loop, 0, LoopTips))
        {
            song.Loop = (LoopMode)loop;
            if (song.Loop == LoopMode.Custom && song.LoopEnd <= 0 && src is { Ready: true })
            {
                var length = (song.TrimEnd ?? src.Seconds) - song.TrimStart;
                (song.LoopStart, song.LoopEnd) = src.TagLoop is { } t ? (t.Start - song.TrimStart, t.End - song.TrimStart) : (0, length);
            }
            studio.Changed();
        }
        ImGui.SameLine();
        var hearKey = $"loop:{song.Id}";
        var canHear = song.Loop != LoopMode.None && src is { Ready: true, Error: null } && song.SourcePath is not null;
        if (Ui.Button("hearloop", studio.Player.Playing == hearKey ? "Stop" : "Hear the loop", FontAwesomeIcon.Headphones, enabled: canHear,
                tooltip: "Plays the last seconds before the loop's end and the jump back, the way the game will."))
            studio.PreviewFile(hearKey, song, song.SourcePath!, true);

        var length2 = src is { Ready: true } ? (song.TrimEnd ?? src.Seconds) - song.TrimStart : 0;
        if (song.Loop == LoopMode.Auto && src is { Ready: true })
            Ui.Hint(src.TagLoop is { } tl ? $"From the file: loops {Naming.PreciseTime(tl.Start)} → {Naming.PreciseTime(tl.End)}." : "The file has no loop points, so the whole song loops.");
        if (song.Loop == LoopMode.Custom)
        {
            var half = (width - ImGui.GetStyle().ItemSpacing.X) / 2;
            ImGui.SetNextItemWidth(half);
            var ls = (float)song.LoopStart;
            if (ImGui.DragFloat("##loopstart", ref ls, 0.05f, 0, (float)Math.Max(0, length2), $"Loop from {Naming.PreciseTime(ls)}"))
            {
                song.LoopStart = Math.Max(0, ls);
                studio.Changed();
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(half);
            var le = (float)song.LoopEnd;
            if (ImGui.DragFloat("##loopend", ref le, 0.05f, 0, (float)Math.Max(0, length2), $"back at {Naming.PreciseTime(le)}"))
            {
                song.LoopEnd = Math.Max(0, le);
                studio.Changed();
            }
            Ui.Tooltip("Drag, or Ctrl+click to type seconds.");
        }

        Ui.Section("Trim", FontAwesomeIcon.Cut);
        var third = (width - ImGui.GetStyle().ItemSpacing.X) / 2;
        ImGui.SetNextItemWidth(third);
        var ts = (float)song.TrimStart;
        var max = (float)(src?.Seconds ?? 600);
        if (ImGui.DragFloat("##trimstart", ref ts, 0.05f, 0, max, ts <= 0 ? "Starts at the beginning" : $"Starts at {Naming.PreciseTime(ts)}"))
        {
            song.TrimStart = Math.Clamp(ts, 0, Math.Max(0, (song.TrimEnd ?? max) - 1));
            studio.Changed();
        }
        Ui.Tooltip("Skip silence or an intro at the start. Drag, or Ctrl+click to type seconds.");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(third);
        var te = (float)(song.TrimEnd ?? max);
        if (ImGui.DragFloat("##trimend", ref te, 0.05f, 0, max, song.TrimEnd is null ? "Ends at the end" : $"Ends at {Naming.PreciseTime(te)}"))
        {
            song.TrimEnd = te >= max - 0.05f ? null : Math.Max(song.TrimStart + 1, te);
            studio.Changed();
        }
        Ui.Tooltip("Cut the song short. Drag to the far right to keep it to the end.");
    }

    private void Volume(SongReplacement song, float width)
    {
        Ui.Section("Volume", FontAwesomeIcon.VolumeUp);
        var match = song.MatchVolume;
        if (ImGui.Checkbox("Match the game's volume", ref match))
        {
            song.MatchVolume = match;
            studio.Changed();
        }
        Ui.Tooltip("Measures both songs (LUFS, like streaming services) and sets yours to the loudness of the one it replaces, so it doesn't blast or vanish.");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        var gain = song.ExtraGainDb;
        if (ImGui.SliderFloat("##gain", ref gain, -12, 12, gain == 0 ? "No extra change" : $"{gain:+0.0;-0.0} dB"))
        {
            song.ExtraGainDb = MathF.Round(gain * 2) / 2;
            studio.Changed();
        }
        Ui.Tooltip("Louder or quieter on top of that. Double-click to type; 0 keeps it as matched.");
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Right))
        {
            song.ExtraGainDb = 0;
            studio.Changed();
        }
    }

    private void Footer(SongReplacement song)
    {
        var enabled = song.Enabled;
        Ui.Gap(0.3f);
        if (ImGui.Checkbox("On when the mod is switched on", ref enabled))
        {
            song.Enabled = enabled;
            studio.Changed();
        }
        Ui.Tooltip("Every song is an option in the mod's settings in Penumbra. Untick to leave this one off at first.");
        if (studio.LastOutcome is { } outcome)
        {
            foreach (var failed in outcome.Failed.Where(f => f.Song.Id == song.Id)) Ui.TextColored(Ui.Danger, "Last build: " + failed.Reason);
            foreach (var (name, note) in outcome.Notes.Where(n => n.Song == song.SongName)) Ui.Hint(note);
        }
    }
}
