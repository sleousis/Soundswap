using System.Globalization;

namespace Soundswap.Core.Project;

/// <summary>A loop in seconds of the trimmed song.</summary>
public readonly record struct LoopSeconds(double Start, double End);

/// <summary>Works out where a replacement loops.</summary>
public static class LoopResolver
{
    /// <summary>The loop the file's own tags describe, in seconds of the untrimmed file, if any.</summary>
    /// <remarks>
    /// Understands the common conventions: LOOPSTART with LOOPLENGTH or LOOPEND (in samples, as RPG Maker and
    /// VFXEditor write them). Tag names are matched without regard to case.
    /// </remarks>
    public static LoopSeconds? FromTags(IReadOnlyDictionary<string, string> tags, int sampleRate)
    {
        if (!TryGet(tags, "LOOPSTART", out var start)) return null;
        double end;
        if (TryGet(tags, "LOOPLENGTH", out var length)) end = start + length;
        else if (TryGet(tags, "LOOPEND", out var stop)) end = stop;
        else return null;
        if (end <= start) return null;
        return new LoopSeconds(start / sampleRate, end / sampleRate);
    }

    /// <summary>
    /// The loop to build with, in seconds of the trimmed song, or null for no loop.
    /// </summary>
    /// <param name="duration">Length of the trimmed song in seconds.</param>
    /// <param name="tagLoop">From <see cref="FromTags"/>, in seconds of the untrimmed file.</param>
    public static LoopSeconds? Resolve(SongReplacement song, double duration, LoopSeconds? tagLoop)
    {
        switch (song.Loop)
        {
            case LoopMode.None:
                return null;
            case LoopMode.Custom:
                var start = Math.Clamp(song.LoopStart, 0, duration);
                var end = song.LoopEnd <= 0 ? duration : Math.Clamp(song.LoopEnd, 0, duration);
                return end - start >= 0.5 ? new LoopSeconds(start, end) : new LoopSeconds(0, duration);
            case LoopMode.Auto when tagLoop is { } t:
                var s = Math.Clamp(t.Start - song.TrimStart, 0, duration);
                var e = Math.Clamp(t.End - song.TrimStart, 0, duration);
                return e - s >= 0.5 ? new LoopSeconds(s, e) : new LoopSeconds(0, duration);
            default:
                return new LoopSeconds(0, duration);
        }
    }

    private static bool TryGet(IReadOnlyDictionary<string, string> tags, string key, out double value)
    {
        value = 0;
        foreach (var (k, v) in tags)
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= 0)
                return true;
        }
        return false;
    }
}
