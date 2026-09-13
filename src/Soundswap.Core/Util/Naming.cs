using System.Globalization;
using System.Text;

namespace Soundswap.Core.Util;

public static class Naming
{
    private static readonly HashSet<char> Invalid = [.. Path.GetInvalidFileNameChars(), '/', '\\', ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>A name that is safe as a file or folder name on Windows, trimmed, never empty.</summary>
    public static string SafeFileName(string name, string fallback = "untitled")
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name.Trim()) sb.Append(Invalid.Contains(c) || char.IsControl(c) ? '_' : c);
        var result = sb.ToString().Trim(' ', '.');
        if (result.Length > 80) result = result[..80].TrimEnd(' ', '.');
        return result.Length == 0 ? fallback : result;
    }

    /// <summary><paramref name="name"/>, or "name (2)", "name (3)" … when already taken. Adds the result to <paramref name="taken"/>.</summary>
    public static string Unique(HashSet<string> taken, string name)
    {
        var candidate = name;
        for (var i = 2; !taken.Add(candidate); i++) candidate = $"{name} ({i})";
        return candidate;
    }

    /// <summary>1:05, 12:30, 1:02:03.</summary>
    public static string Time(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
        var t = TimeSpan.FromSeconds(Math.Round(seconds));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : t.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>1:05.3, for loop points where tenths matter.</summary>
    public static string PreciseTime(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
        var minutes = (int)(seconds / 60);
        return string.Create(CultureInfo.InvariantCulture, $"{minutes}:{seconds - minutes * 60:00.0}");
    }

    /// <summary>Lower case, accents and punctuation removed: how search compares text.</summary>
    public static string Fold(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return sb.ToString();
    }
}
