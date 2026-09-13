using Soundswap.Core.Audio;
using Soundswap.Core.Project;

namespace Soundswap.Core.Build;

/// <summary>Where a build is, for the progress bar.</summary>
public sealed record BuildProgress(int Index, int Count, string Song, BuildStage Stage, float Overall);

public sealed record FailedSong(SongReplacement Song, string Reason);

public sealed record ModBuildResult(IReadOnlyList<BuiltSong> Built, IReadOnlyList<FailedSong> Failed)
{
    public bool Any => Built.Count > 0;
}

/// <summary>Builds every song of a project, one after another. A song that fails is reported and the rest go on.</summary>
public sealed class ModBuilder(IGameFiles game, IAudioDecoder decoder)
{
    // How much of a song's time each stage takes, roughly: encoding dominates.
    private static readonly Dictionary<BuildStage, (float Start, float Share)> Stages = new()
    {
        [BuildStage.Reading] = (0f, 0.04f),
        [BuildStage.Decoding] = (0.04f, 0.2f),
        [BuildStage.Matching] = (0.24f, 0.1f),
        [BuildStage.Encoding] = (0.34f, 0.62f),
        [BuildStage.Packing] = (0.96f, 0.04f),
    };

    public ModBuildResult Build(IReadOnlyList<SongReplacement> songs, BuildSettings settings, Action<BuildProgress>? progress, CancellationToken ct)
    {
        var builder = new SongBuilder(game, decoder);
        var built = new List<BuiltSong>();
        var failed = new List<FailedSong>();
        for (var i = 0; i < songs.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var song = songs[i];
            var index = i;
            try
            {
                built.Add(builder.Build(song, settings, (stage, fraction) =>
                {
                    var (start, share) = Stages[stage];
                    var overall = (index + start + share * Math.Clamp(fraction, 0f, 1f)) / songs.Count;
                    progress?.Invoke(new BuildProgress(index, songs.Count, song.SongName, stage, overall));
                }, ct));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed.Add(new FailedSong(song, ex.Message));
            }
        }
        progress?.Invoke(new BuildProgress(songs.Count, songs.Count, "", BuildStage.Packing, 1f));
        return new ModBuildResult(built, failed);
    }
}
