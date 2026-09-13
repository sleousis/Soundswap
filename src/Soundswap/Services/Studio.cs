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

/// <summary>What the game's own file of a song is like.</summary>
public sealed record SongFacts(int Channels, int SampleRate, string Codec, double Seconds, double?[] LayerLufs, string? Problem)
{
    public int Layers => Math.Max(1, (Channels + 1) / 2);
    public bool Measured => LayerLufs.Length > 0 && LayerLufs.Any(l => l is not null);
}

/// <summary>What a chosen song file is like, filled in by the background worker.</summary>
public sealed class SourceInfo
{
    public required string Path { get; init; }
    public volatile bool Ready;
    public string? Error { get; set; }
    public double Seconds { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public float[] Envelope { get; set; } = [];
    public LoopSeconds? TagLoop { get; set; }
    public double? Lufs { get; set; }
}

public sealed record BuildOutcome(
    DateTimeOffset At,
    int Built,
    IReadOnlyList<FailedSong> Failed,
    IReadOnlyList<(string Song, string Note)> Notes,
    string? InstalledFolder,
    string? Collection,
    string? ExportedTo,
    string? Problem,
    int FirstBgmId,
    bool Cancelled);

/// <summary>
/// Everything the window works with: the mod being made, the song list, facts about the game's songs and the
/// chosen files, previews, and building. Reading, decoding and measuring run on one low-priority background
/// thread; a build runs on its own task with a copy of the project; Penumbra and Orchestrion are called on the
/// framework thread.
/// </summary>
public sealed partial class Studio : IDisposable
{
    private readonly GameFiles game;
    private readonly IDataManager data;
    private readonly PenumbraBridge penumbra;
    private readonly OrchestrionBridge orchestrion;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly Action saveConfig;
    private readonly Func<string> defaultAuthor;
    private readonly ProjectStore store;

    private readonly BlockingCollection<Action> queue = new();
    private readonly Thread worker;
    private readonly CancellationTokenSource disposing = new();
    private readonly ConcurrentDictionary<int, SongFacts> facts = new();
    private readonly ConcurrentDictionary<int, byte> probed = new();
    private readonly ConcurrentDictionary<string, SourceInfo> sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock clipsGate = new();
    private readonly List<(string Key, AudioClip Clip)> clips = [];
    private CancellationTokenSource? buildCts;
    private DateTime lastPoll = DateTime.MinValue;
    private DateTime lastSave = DateTime.MinValue;
    private bool dirty;

    public Studio(GameFiles game, IDataManager data, PenumbraBridge penumbra, OrchestrionBridge orchestrion, IFramework framework, IPluginLog log,
        Configuration config, Action saveConfig, string configDirectory, Func<string> defaultAuthor)
    {
        this.game = game;
        this.data = data;
        this.penumbra = penumbra;
        this.orchestrion = orchestrion;
        this.framework = framework;
        this.log = log;
        this.config = config;
        this.saveConfig = saveConfig;
        this.defaultAuthor = defaultAuthor;
        store = new ProjectStore(Path.Combine(configDirectory, "project.json"));
        Project = store.Load(out var problem) ?? new SoundswapProject();
        Notice = problem;
        Player.SetVolume(config.PreviewVolume);

        worker = new Thread(Loop) { IsBackground = true, Name = "Soundswap worker", Priority = ThreadPriority.BelowNormal };
        worker.Start();

        RefreshCatalog();
        foreach (var song in Project.Songs)
        {
            Measure(song);
            foreach (var file in song.Files()) Source(file);
        }
    }

    public SoundswapProject Project { get; }
    public SongCatalog Catalog { get; private set; } = SongCatalog.Empty;
    public bool CatalogReady { get; private set; }
    public string CatalogSource { get; private set; } = "";
    public PreviewPlayer Player { get; } = new();

    /// <summary>The key of a preview being decoded, for a spinner.</summary>
    public string? PreviewLoading { get; private set; }

    public string? PreviewError { get; private set; }
    public int NowPlaying { get; private set; }
    public bool OrchestrionReady { get; private set; }
    public bool PenumbraReady { get; private set; }
    public bool Building { get; private set; }
    public BuildProgress? Progress { get; private set; }
    public BuildOutcome? LastOutcome { get; private set; }

    /// <summary>A one-off message for the top of the window.</summary>
    public string? Notice { get; set; }

    /// <summary>The song selected in the list or the mod; the window scrolls and unfolds to it.</summary>
    public int Selected { get; set; }

    /// <summary>Framework thread, every frame: polls the plugins now and then, and saves the project after edits.</summary>
    public void Update()
    {
        var now = DateTime.UtcNow;
        if (now - lastPoll > TimeSpan.FromSeconds(1))
        {
            lastPoll = now;
            PenumbraReady = penumbra.Available;
            var orch = orchestrion.Available;
            if (orch && !OrchestrionReady) RefreshCatalog();
            OrchestrionReady = orch;
            NowPlaying = orch ? orchestrion.NowPlaying() : 0;
            foreach (var song in Project.Songs)
            {
                if (song.Channels == 0 && facts.TryGetValue(song.BgmId, out var f) && f.Channels > 0) song.Channels = f.Channels;
            }
        }
        if (dirty && now - lastSave > TimeSpan.FromSeconds(1.5)) SaveNow();
    }

    // ---------- the song list ----------

    public void RefreshCatalog() => _ = framework.RunOnFrameworkThread(() =>
    {
        OrchestrionReady = orchestrion.Available;
        var live = OrchestrionReady ? orchestrion.Songs() : [];
        Enqueue(() =>
        {
            Catalog = CatalogLoader.Load(data, live, log);
            CatalogSource = live.Count > 0 ? "Orchestrion" : "Soundswap's copy of Orchestrion's list";
            CatalogReady = true;
        });
    });

    /// <summary>Facts about a game song, read in the background the first time they are asked for.</summary>
    public SongFacts? Facts(int bgmId, string gamePath)
    {
        if (facts.TryGetValue(bgmId, out var f)) return f;
        if (probed.TryAdd(bgmId, 0)) Enqueue(() => facts.TryAdd(bgmId, ReadFacts(gamePath, false)));
        return null;
    }

    /// <summary>Facts plus the loudness of every layer (decodes the song), for songs in the mod.</summary>
    public void Measure(SongReplacement song)
    {
        var (id, path) = (song.BgmId, song.GamePath);
        probed.TryAdd(id, 0);
        Enqueue(() =>
        {
            if (facts.TryGetValue(id, out var known) && known.Measured) return;
            facts[id] = ReadFacts(path, true);
        });
    }

    private SongFacts ReadFacts(string path, bool measure)
    {
        try
        {
            var bytes = game.Read(path);
            if (bytes is null) return new SongFacts(0, 0, "", 0, [], "The game has no such file.");
            var scd = ScdFile.Parse(bytes);
            if (scd.Audio is not { } audio) return new SongFacts(0, 0, "", 0, [], "One of the game's silent placeholders: nothing plays, so nothing can be replaced.");
            if (!audio.IsVorbis) return new SongFacts(audio.Channels, audio.SampleRate, audio.Codec.ToString(), 0, [], null);
            var ogg = scd.ExtractOgg()!;
            if (!measure)
            {
                using var reader = new NVorbis.VorbisReader(new MemoryStream(ogg), true);
                return new SongFacts(audio.Channels, audio.SampleRate, "Vorbis", reader.TotalTime.TotalSeconds, [], null);
            }
            var clip = AudioReader.DecodeOgg(ogg, disposing.Token);
            Cache("original:" + path, clip);
            var lufs = Enumerable.Range(0, audio.Layers).Select(l => Loudness.Integrated(clip.Pair(l * 2))).ToArray();
            return new SongFacts(audio.Channels, audio.SampleRate, "Vorbis", clip.Duration.TotalSeconds, lufs, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new SongFacts(0, 0, "", 0, [], ex.Message);
        }
    }

    // ---------- chosen files ----------

    /// <summary>What a file is like: decoded, measured and drawn once in the background.</summary>
    public SourceInfo? Source(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return sources.GetOrAdd(path, p =>
        {
            var info = new SourceInfo { Path = p };
            Enqueue(() => Analyze(info));
            return info;
        });
    }

    private void Analyze(SourceInfo info)
    {
        try
        {
            var clip = AudioFiles.Decoders.Decode(info.Path, disposing.Token);
            info.TagLoop = LoopResolver.FromTags(clip.Tags, clip.SampleRate);
            info.Seconds = clip.Duration.TotalSeconds;
            info.SampleRate = clip.SampleRate;
            info.Channels = clip.ChannelCount;
            var stereo = clip.ToStereo();
            info.Envelope = stereo.Envelope(600);
            info.Lufs = Loudness.Integrated(stereo);
            Cache("source:" + info.Path, stereo);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            info.Error = ex.Message;
        }
        info.Ready = true;
    }

    // ---------- editing ----------

    public SongReplacement Add(SongInfo song)
    {
        if (Project.Find(song.Id) is { } existing)
        {
            Selected = existing.BgmId;
            return existing;
        }
        var replacement = new SongReplacement
        {
            BgmId = song.Id,
            GamePath = song.GamePath,
            SongName = song.Title,
            MatchVolume = config.MatchVolumeByDefault,
            Channels = facts.TryGetValue(song.Id, out var f) ? f.Channels : 0,
        };
        Project.Songs.Add(replacement);
        Selected = song.Id;
        Measure(replacement);
        Changed();
        return replacement;
    }

    public void Remove(SongReplacement song)
    {
        if (Player.Playing?.EndsWith(song.Id.ToString(), StringComparison.Ordinal) == true) Player.Stop();
        Project.Songs.Remove(song);
        Changed();
    }

    public void SetFile(SongReplacement song, string path, int layer = -1)
    {
        if (layer < 0) song.SourcePath = path;
        else song.Layer(layer).Path = path;
        Source(path);
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder) && folder != config.LastFolder)
        {
            config.LastFolder = folder;
            saveConfig();
        }
        Changed();
    }

    /// <summary>The project changed; it is saved a moment later.</summary>
    public void Changed() => dirty = true;

    private void SaveNow()
    {
        dirty = false;
        lastSave = DateTime.UtcNow;
        try
        {
            store.Save(Project);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Could not save the project");
        }
    }

    // ---------- worker ----------

    private void Enqueue(Action action)
    {
        if (!disposing.IsCancellationRequested) queue.Add(action);
    }

    private void Loop()
    {
        try
        {
            foreach (var action in queue.GetConsumingEnumerable(disposing.Token))
            {
                try
                {
                    action();
                }
                catch (OperationCanceledException)
                {
                    // unloading
                }
                catch (Exception ex)
                {
                    log.Error(ex, "Background work failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // disposed
        }
    }

    public void Dispose()
    {
        buildCts?.Cancel();
        if (dirty) SaveNow();
        disposing.Cancel();
        Player.Dispose();
        worker.Join(TimeSpan.FromSeconds(3));
    }
}
