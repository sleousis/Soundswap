using System.Globalization;
using Soundswap.Core.Audio;
using Soundswap.Core.Project;
using Soundswap.Core.Scd;

namespace Soundswap.Core.Build;

/// <summary>The game's own files, read-only, by game path.</summary>
public interface IGameFiles
{
    byte[]? Read(string gamePath);
}

public sealed record BuildSettings
{
    /// <summary>Vorbis quality, 0..1.</summary>
    public float Quality { get; init; } = 0.5f;

    /// <summary>Loudness a replacement is matched to when the original can't be measured (HCA songs).</summary>
    public double FallbackLufs { get; init; } = -18;

    /// <summary>Highest sample value after the volume change; above it the replacement is turned down.</summary>
    public float PeakCeiling { get; init; } = 0.97f;
}

public enum BuildStage
{
    Reading,
    Decoding,
    Matching,
    Encoding,
    Packing,
}

/// <summary>A finished song: the new music file and what happened to get there.</summary>
public sealed record BuiltSong(SongReplacement Song, byte[] Scd, TimeSpan Duration, int Layers, LoopSeconds? Loop, IReadOnlyList<string> Notes);

/// <summary>
/// Builds one replacement: reads the game's file, decodes the chosen songs, matches each layer's loudness to
/// the game's own layer, lays the layers out as the file's stereo pairs, encodes Vorbis and writes the new file.
/// </summary>
public sealed class SongBuilder(IGameFiles game, IAudioDecoder decoder)
{
    public BuiltSong Build(SongReplacement song, BuildSettings settings, Action<BuildStage, float>? progress, CancellationToken ct)
    {
        progress?.Invoke(BuildStage.Reading, 0);
        var bytes = game.Read(song.GamePath) ?? throw new InvalidDataException($"The game has no file {song.GamePath}.");
        var scd = ScdFile.Parse(bytes);
        var audio = scd.Audio ?? throw new InvalidDataException($"{song.SongName} is one of the game's silent placeholders; there's nothing to replace.");
        song.Channels = audio.Channels;
        if (song.Missing() is { } missing) throw new InvalidOperationException(missing);

        var rate = audio.SampleRate;
        var layers = audio.Layers;
        var notes = new List<string>();
        var choices = Enumerable.Range(0, layers).Select(i => Choice(song, i)).ToList();

        // The songs, decoded once each, as stereo at the game file's rate and trimmed.
        progress?.Invoke(BuildStage.Decoding, 0);
        var sources = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        LoopSeconds? tagLoop = null;
        var paths = choices.Where(c => c.Source == LayerSource.Song).Select(c => c.Path!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        for (var i = 0; i < paths.Count; i++)
        {
            var raw = decoder.Decode(paths[i], ct);
            if (i == 0) tagLoop = LoopResolver.FromTags(raw.Tags, raw.SampleRate);
            sources[paths[i]] = raw.ToStereo().Slice(song.TrimStart, song.TrimEnd).Resample(rate);
            progress?.Invoke(BuildStage.Decoding, (i + 1f) / Math.Max(1, paths.Count));
        }
        if (sources.Values.Any(s => s.Frames < rate / 4))
            throw new InvalidDataException("After trimming, the song is shorter than a quarter of a second.");

        // The game's own music: measured to match volume, and used for layers kept as they are.
        AudioClip? original = null;
        if (audio.IsVorbis && (song.MatchVolume || choices.Any(c => c.Source == LayerSource.Original)))
            original = AudioReader.DecodeOgg(scd.ExtractOgg()!, ct);
        if (original is null && choices.Any(c => c.Source == LayerSource.Original))
            throw new NotSupportedException("The game's own layers of this song can't be read (it isn't Vorbis), so they can't be kept.");

        var main = sources.Count > 0 ? sources[paths[0]] : original!;
        var frames = choices.Max(c => c.Source switch
        {
            LayerSource.Song => sources[c.Path!].Frames,
            LayerSource.Original => original!.Frames,
            _ => 0,
        });
        var loop = LoopResolver.Resolve(song, (double)main.Frames / rate, tagLoop);

        progress?.Invoke(BuildStage.Matching, 0);
        var output = new float[audio.Channels][];
        for (var c = 0; c < output.Length; c++) output[c] = new float[frames];
        var sourceLoudness = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
        for (var layer = 0; layer < layers; layer++)
        {
            ct.ThrowIfCancellationRequested();
            var choice = choices[layer];
            if (choice.Source == LayerSource.Silence) continue;

            AudioClip clip;
            float gain;
            if (choice.Source == LayerSource.Original)
            {
                clip = original!.Pair(layer * 2);
                gain = 1f;
            }
            else
            {
                clip = sources[choice.Path!];
                if (!sourceLoudness.TryGetValue(choice.Path!, out var measured))
                    sourceLoudness[choice.Path!] = measured = Loudness.Integrated(clip);
                gain = Gain(song, settings, clip, measured, original, layer, layers, notes);
            }
            Mix(clip, gain, output, layer * 2);
            progress?.Invoke(BuildStage.Matching, (layer + 1f) / layers);
        }

        Fade(output, rate, fadeIn: 0.003, fadeOut: loop is null ? 0.04 : 0);
        var loopSamples = loop is { } l ? new LoopSamples((long)Math.Round(l.Start * rate), Math.Min(frames, (long)Math.Round(l.End * rate))) : (LoopSamples?)null;
        var tags = loopSamples is { } ls
            ? new Dictionary<string, string> { ["LOOPSTART"] = ls.Start.ToString(CultureInfo.InvariantCulture), ["LOOPEND"] = ls.End.ToString(CultureInfo.InvariantCulture) }
            : null;

        var encoded = VorbisEncoder.Encode(output, rate, settings.Quality, tags, p => progress?.Invoke(BuildStage.Encoding, p), ct);
        progress?.Invoke(BuildStage.Packing, 0);
        var scdBytes = ScdWriter.ReplaceAudio(scd, encoded, loopSamples);
        if (!audio.IsVorbis) notes.Add("The game's version of this song uses another codec; it is replaced with Vorbis like the rest of the game's music.");
        return new BuiltSong(song, scdBytes, TimeSpan.FromSeconds((double)frames / rate), layers, loop, notes);
    }

    /// <summary>What plays on <paramref name="layer"/>, with the song's main file filled in.</summary>
    internal static LayerChoice Choice(SongReplacement song, int layer)
    {
        var choice = song.LayerMode switch
        {
            LayerMode.EveryLayer => new LayerChoice { Source = LayerSource.Song },
            LayerMode.FirstLayerOnly => new LayerChoice { Source = layer == 0 ? LayerSource.Song : LayerSource.Silence },
            _ => new LayerChoice { Source = song.Layer(layer).Source, Path = song.Layer(layer).Path },
        };
        if (choice.Source == LayerSource.Song) choice.Path = string.IsNullOrWhiteSpace(choice.Path) ? song.SourcePath : choice.Path;
        return choice;
    }

    /// <summary>The volume change for one layer: matched to the game's layer, then kept from clipping.</summary>
    private static float Gain(SongReplacement song, BuildSettings settings, AudioClip clip, double? measured, AudioClip? original, int layer,
        int layers, List<string> notes)
    {
        var db = (double)song.ExtraGainDb;
        if (song.MatchVolume && measured is { } source)
        {
            var target = original is not null ? Loudness.Integrated(original.Pair(layer * 2)) : null;
            if (target is null) notes.Add(layers > 1 ? $"Layer {layer + 1}: matched to a typical game volume." : "Matched to a typical game volume.");
            db += (target ?? settings.FallbackLufs) - source;
        }

        var gain = (float)Math.Pow(10, db / 20);
        var peak = clip.Peak() * gain;
        if (peak > settings.PeakCeiling && peak > 0)
        {
            var reduced = settings.PeakCeiling / peak;
            notes.Add($"{(layers > 1 ? $"Layer {layer + 1}: t" : "T")}urned down {-20 * Math.Log10(reduced):0.0} dB more so it doesn't clip.");
            gain *= reduced;
        }
        return gain;
    }

    private static void Mix(AudioClip clip, float gain, float[][] output, int first)
    {
        var n = Math.Min(clip.Frames, output[0].Length);
        for (var side = 0; side < 2 && first + side < output.Length; side++)
        {
            var src = clip.Channels[Math.Min(side, clip.ChannelCount - 1)];
            var dst = output[first + side];
            for (var i = 0; i < n; i++) dst[i] = src[i] * gain;
        }
    }

    private static void Fade(float[][] output, int rate, double fadeIn, double fadeOut)
    {
        var frames = output[0].Length;
        var inFrames = Math.Min(frames, (int)(fadeIn * rate));
        var outFrames = Math.Min(frames, (int)(fadeOut * rate));
        foreach (var channel in output)
        {
            for (var i = 0; i < inFrames; i++) channel[i] *= (float)i / inFrames;
            for (var i = 0; i < outFrames; i++) channel[frames - 1 - i] *= (float)i / outFrames;
        }
    }
}
