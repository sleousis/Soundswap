namespace Soundswap.Core.Project;

/// <summary>What plays on a layered song's layers.</summary>
public enum LayerMode
{
    /// <summary>Your song on every layer: it keeps playing whichever layers the game brings in.</summary>
    EveryLayer,

    /// <summary>Your song on the first layer, the others silent.</summary>
    FirstLayerOnly,

    /// <summary>A choice per layer: a song, silence, or the game's own layer.</summary>
    PerLayer,
}

public enum LayerSource
{
    Song,
    Silence,
    Original,
}

public enum LoopMode
{
    /// <summary>The loop the file itself carries (LOOPSTART tags), otherwise the whole song.</summary>
    Auto,
    WholeSong,
    Custom,
    None,
}

public sealed class LayerChoice
{
    public LayerSource Source { get; set; } = LayerSource.Song;

    /// <summary>For <see cref="LayerSource.Song"/>: the file for this layer. Null uses the song's main file.</summary>
    public string? Path { get; set; }
}

/// <summary>One of the game's songs and what replaces it.</summary>
public sealed class SongReplacement
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Row of the game's BGM sheet (the id Orchestrion shows).</summary>
    public int BgmId { get; set; }

    public string GamePath { get; set; } = "";
    public string SongName { get; set; } = "";

    /// <summary>The game's file: 2 channels for a normal song, 4 or 6 for a layered one. 0 until it has been read.</summary>
    public int Channels { get; set; }

    /// <summary>The file that replaces the song.</summary>
    public string? SourcePath { get; set; }

    public LayerMode LayerMode { get; set; } = LayerMode.EveryLayer;
    public List<LayerChoice> Layers { get; set; } = [];

    public LoopMode Loop { get; set; } = LoopMode.Auto;

    /// <summary>Seconds into the trimmed song.</summary>
    public double LoopStart { get; set; }

    public double LoopEnd { get; set; }

    /// <summary>Seconds cut from the start of the file.</summary>
    public double TrimStart { get; set; }

    /// <summary>Where the file stops, in seconds of the original file. Null: its end.</summary>
    public double? TrimEnd { get; set; }

    public bool MatchVolume { get; set; } = true;
    public float ExtraGainDb { get; set; }

    /// <summary>On when the mod is first switched on in Penumbra.</summary>
    public bool Enabled { get; set; } = true;

    public int LayerCount => Math.Max(1, (Channels + 1) / 2);
    public bool IsLayered => LayerCount > 1;

    /// <summary>The choice for layer <paramref name="index"/>, created on first use.</summary>
    public LayerChoice Layer(int index)
    {
        while (Layers.Count <= index) Layers.Add(new LayerChoice());
        return Layers[index];
    }

    /// <summary>Every file this replacement needs, distinct.</summary>
    public IEnumerable<string> Files()
    {
        if (!string.IsNullOrWhiteSpace(SourcePath)) yield return SourcePath;
        if (LayerMode != LayerMode.PerLayer) yield break;
        foreach (var layer in Layers.Take(LayerCount))
        {
            if (layer.Source == LayerSource.Song && !string.IsNullOrWhiteSpace(layer.Path) &&
                !string.Equals(layer.Path, SourcePath, StringComparison.OrdinalIgnoreCase))
                yield return layer.Path;
        }
    }

    /// <summary>What still has to be chosen before this song can be built, or null when it is ready.</summary>
    public string? Missing()
    {
        if (LayerMode != LayerMode.PerLayer) return string.IsNullOrWhiteSpace(SourcePath) ? "Choose a song file." : null;
        for (var i = 0; i < LayerCount; i++)
        {
            var layer = Layer(i);
            if (layer.Source == LayerSource.Song && string.IsNullOrWhiteSpace(layer.Path ?? SourcePath))
                return $"Choose a file for layer {i + 1}.";
        }
        return Enumerable.Range(0, LayerCount).All(i => Layer(i).Source == LayerSource.Silence)
            ? "Every layer is silent. Give at least one layer a song."
            : null;
    }
}

/// <summary>A music mod being made: its name and the songs it replaces.</summary>
public sealed class SoundswapProject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "My music";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public List<SongReplacement> Songs { get; set; } = [];

    /// <summary>The folder under Penumbra's mod root once installed, so rebuilding updates that mod.</summary>
    public string? ModDirectory { get; set; }

    public SongReplacement? Find(int bgmId) => Songs.FirstOrDefault(s => s.BgmId == bgmId);
}
