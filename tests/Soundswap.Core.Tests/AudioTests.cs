using Soundswap.Core.Audio;
using Soundswap.Core.Project;
using static Soundswap.Core.Tests.TestAudio;

namespace Soundswap.Core.Tests;

public class AudioTests
{
    [Fact]
    public void Stereo_encodes_and_decodes_at_the_same_rate_and_length()
    {
        var clip = Tone(2, 2, 48000, 440, 0.5);
        var decoded = AudioReader.DecodeOgg([.. Encode(clip).Header, .. Encode(clip).Audio], CancellationToken.None);

        Assert.Equal(2, decoded.ChannelCount);
        Assert.Equal(48000, decoded.SampleRate);
        Assert.Equal(clip.Frames, decoded.Frames);
    }

    [Fact]
    public void Six_channels_keep_each_layer_apart()
    {
        // Sound only on the second layer (channels 3 and 4).
        var frames = 44100;
        var channels = Enumerable.Range(0, 6).Select(c => c is 2 or 3 ? Sine(frames, 44100, 440, 0.5) : new float[frames]).ToArray();
        var encoded = VorbisEncoder.Encode(channels, 44100, 0.4f, null, null, CancellationToken.None);
        var decoded = AudioReader.DecodeOgg([.. encoded.Header, .. encoded.Audio], CancellationToken.None);

        Assert.Equal(6, decoded.ChannelCount);
        var rms = decoded.Channels.Select(c => Math.Sqrt(c.Average(s => (double)s * s))).ToArray();
        Assert.True(rms[2] > 0.3 && rms[3] > 0.3, $"layer 2 should play: {string.Join(", ", rms)}");
        Assert.True(rms[0] < 0.01 && rms[1] < 0.01 && rms[4] < 0.01 && rms[5] < 0.01, $"other layers should be silent: {string.Join(", ", rms)}");
    }

    [Fact]
    public void Wav_and_ogg_files_decode_through_the_chain()
    {
        using var temp = new TempFolder();
        var clip = Tone(1, 1, 22050, 440, 0.5);
        var wav = Wav(temp.Path, "tone.wav", clip);
        var ogg = Path.Combine(temp.Path, "tone.ogg");
        var encoded = Encode(Tone(2, 1, 44100, 440, 0.5));
        File.WriteAllBytes(ogg, [.. encoded.Header, .. encoded.Audio]);
        var chain = new AudioDecoderChain([new ManagedAudioDecoder()]);

        var fromWav = chain.Decode(wav, CancellationToken.None);
        var fromOgg = chain.Decode(ogg, CancellationToken.None);

        Assert.Equal((1, 22050, 22050), (fromWav.ChannelCount, fromWav.SampleRate, fromWav.Frames));
        Assert.Equal((2, 44100), (fromOgg.ChannelCount, fromOgg.SampleRate));
        Assert.Equal("Soundswap", fromOgg.Tags["ENCODER"]);
    }

    [Fact]
    public void An_unreadable_file_says_which_one()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "broken.mp3");
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        var ex = Assert.Throws<InvalidDataException>(() => new AudioDecoderChain([new ManagedAudioDecoder()]).Decode(path, CancellationToken.None));
        Assert.Contains("broken.mp3", ex.Message);
    }

    [Theory]
    [InlineData(1.0, 2, 0.0)]
    [InlineData(1.0, 1, -3.01)]
    [InlineData(0.1, 2, -20.0)]
    public void Loudness_matches_the_reference_values(double amplitude, int channels, double expected)
    {
        var clip = Tone(channels, 5, 48000, 1000, amplitude);
        Assert.InRange(Loudness.Integrated(clip)!.Value, expected - 0.3, expected + 0.3);
    }

    [Fact]
    public void Loudness_of_silence_is_unknown()
    {
        Assert.Null(Loudness.Integrated(AudioClip.Silence(2, 48000 * 2, 48000)));
    }

    [Fact]
    public void Resample_changes_rate_and_keeps_duration()
    {
        var clip = Tone(2, 1, 48000, 440, 0.5).Resample(44100);
        Assert.Equal(44100, clip.SampleRate);
        Assert.InRange(clip.Frames, 44000, 44200);
    }

    [Fact]
    public void Mono_becomes_stereo_and_surround_folds_down_without_clipping()
    {
        Assert.Equal(2, Tone(1, 0.1, 44100, 440, 0.5).ToStereo().ChannelCount);
        var folded = Tone(6, 0.1, 44100, 440, 1.0).ToStereo();
        Assert.Equal(2, folded.ChannelCount);
        Assert.True(folded.Peak() <= 1.0001f);
    }

    [Fact]
    public void Loop_tags_are_read_in_samples()
    {
        var tags = new Dictionary<string, string> { ["LoopStart"] = "44100", ["LOOPLENGTH"] = "88200" };
        Assert.Equal(new LoopSeconds(1, 3), LoopResolver.FromTags(tags, 44100));
        Assert.Null(LoopResolver.FromTags(new Dictionary<string, string> { ["LOOPSTART"] = "5" }, 44100));
    }

    [Fact]
    public void Loops_resolve_by_mode_and_follow_the_trim()
    {
        var song = new SongReplacement { TrimStart = 1 };
        Assert.Equal(new LoopSeconds(0, 60), LoopResolver.Resolve(song, 60, null));
        Assert.Equal(new LoopSeconds(4, 9), LoopResolver.Resolve(song, 60, new LoopSeconds(5, 10)));

        song.Loop = LoopMode.None;
        Assert.Null(LoopResolver.Resolve(song, 60, null));

        song.Loop = LoopMode.Custom;
        song.LoopStart = 10;
        song.LoopEnd = 70;
        Assert.Equal(new LoopSeconds(10, 60), LoopResolver.Resolve(song, 60, null));
        song.LoopEnd = 10.2;
        Assert.Equal(new LoopSeconds(0, 60), LoopResolver.Resolve(song, 60, null));
    }
}
