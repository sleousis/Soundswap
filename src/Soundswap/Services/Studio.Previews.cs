using System.Collections.Concurrent;
using System.Text.Json;
using Dalamud.Plugin.Services;
using Soundswap.Audio;
using Soundswap.Core.Audio;
using Soundswap.Core.Build;
using Soundswap.Core.Catalog;
using Soundswap.Core.Mods;
using Soundswap.Core.Project;
using Soundswap.Core.Scd;
using Soundswap.Core.Util;
using Soundswap.Game;
using Soundswap.Integrations;

namespace Soundswap.Services;

/// <summary>Previews: the game's version, a chosen file as the build would play it, and the loop seam.</summary>
public sealed partial class Studio
{
    // ---------- previews ----------

    public void PreviewOriginal(string key, string gamePath, int layer = -1)
    {
        if (Toggle(key)) return;
        Preview(key, () =>
        {
            var clip = Cached("original:" + gamePath) ?? DecodeOriginal(gamePath);
            Player.Play(key, layer >= 0 && clip.ChannelCount > 2 ? clip.Pair(layer * 2) : clip);
        });
    }

    /// <param name="hearLoop">Start a few seconds before the loop's end, to hear the seam.</param>
    public void PreviewFile(string key, SongReplacement song, string path, bool hearLoop)
    {
        if (Toggle(key)) return;
        Preview(key, () =>
        {
            var clip = Cached("source:" + path) ?? Decode(path);
            var trimmed = clip.Slice(song.TrimStart, song.TrimEnd);
            var loop = LoopResolver.Resolve(song, trimmed.Duration.TotalSeconds, Source(path)?.TagLoop);
            var start = hearLoop && loop is { } l ? Math.Max(l.Start, l.End - 4) : 0;
            Player.Play(key, trimmed, start, loop, PreviewGain(song, Source(path)));
        });
    }

    /// <summary>The volume change a build would make, so a preview sounds as the game will play it.</summary>
    public float PreviewGain(SongReplacement song, SourceInfo? info)
    {
        double db = song.ExtraGainDb;
        if (song.MatchVolume && info?.Lufs is { } source)
        {
            var target = facts.TryGetValue(song.BgmId, out var f) ? f.LayerLufs.FirstOrDefault(l => l is not null) : null;
            db += (target ?? -18) - source;
        }
        return (float)Math.Clamp(Math.Pow(10, db / 20), 0, 8);
    }

    /// <summary>The same preview again stops it. Returns whether it did.</summary>
    private bool Toggle(string key)
    {
        if (Player.Playing != key && PreviewLoading != key) return false;
        Player.Stop();
        PreviewLoading = null;
        return true;
    }

    private void Preview(string key, Action play)
    {
        Player.Stop();
        PreviewError = null;
        PreviewLoading = key;
        Enqueue(() =>
        {
            try
            {
                if (PreviewLoading == key) play();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                PreviewError = ex.Message;
            }
            finally
            {
                if (PreviewLoading == key) PreviewLoading = null;
            }
        });
    }

    private AudioClip DecodeOriginal(string gamePath)
    {
        var scd = ScdFile.Parse(game.Read(gamePath) ?? throw new InvalidDataException("The game has no such file."));
        var ogg = scd.ExtractOgg() ?? throw new NotSupportedException("The game's version of this song can't be previewed here (it isn't Vorbis).");
        return Cache("original:" + gamePath, AudioReader.DecodeOgg(ogg, disposing.Token));
    }

    private AudioClip Decode(string path) => Cache("source:" + path, AudioFiles.Decoders.Decode(path, disposing.Token).ToStereo());

    private AudioClip Cache(string key, AudioClip clip)
    {
        lock (clipsGate)
        {
            clips.RemoveAll(c => c.Key == key);
            clips.Add((key, clip));
            // Decoded songs are large; keep only the last few.
            while (clips.Count > 4) clips.RemoveAt(0);
        }
        return clip;
    }

    private AudioClip? Cached(string key)
    {
        lock (clipsGate) return clips.FirstOrDefault(c => c.Key == key).Clip;
    }
}
