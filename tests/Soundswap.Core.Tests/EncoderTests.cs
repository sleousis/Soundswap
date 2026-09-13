using Soundswap.Core.Audio;

namespace Soundswap.Core.Tests;

/// <summary>
/// Both encoders must give back exactly as many frames as they were given, with everything where it was: loop
/// points are sample positions, so a shift of even a few milliseconds would move every loop.
/// </summary>
public class EncoderTests
{
    public static TheoryData<string> Encoders => new() { "native", "managed" };

    private static EncodedVorbis Run(string encoder, float[][] channels, int rate) => encoder switch
    {
        "native" => NativeVorbis.Encode(channels, rate, 0.5f, null, null, CancellationToken.None),
        _ => ManagedVorbis.Encode(channels, rate, 0.5f, null, null, CancellationToken.None),
    };

    [SkippableTheory]
    [MemberData(nameof(Encoders))]
    public void Length_and_timing_survive_the_round_trip(string encoder)
    {
        Skip.If(encoder == "native" && !VorbisEncoder.NativeAvailable, $"soundswap_vorbis.dll isn't built here ({VorbisEncoder.NativeProblem}); run tools/build-vorbis.cmd.");
        const int rate = 44100;
        var frames = rate * 3 + 17;
        var impulses = new[] { 3000, frames / 2, frames - 4000 };
        var channels = new[] { new float[frames], new float[frames] };
        foreach (var at in impulses)
        {
            for (var k = 0; k < 32; k++) channels[0][at + k] = channels[1][at + k] = k % 2 == 0 ? 0.9f : -0.9f;
        }

        var encoded = Run(encoder, channels, rate);
        var decoded = AudioReader.DecodeOgg([.. encoded.Header, .. encoded.Audio], CancellationToken.None);

        Assert.Equal(frames, decoded.Frames);
        foreach (var at in impulses)
        {
            var found = Enumerable.Range(at - 2000, 4000).MaxBy(i => Math.Abs(decoded.Channels[0][i]));
            Assert.InRange(found - at, -8, 40);
        }
    }

    [SkippableFact]
    public void The_native_encoder_handles_every_layout_the_game_uses()
    {
        Skip.If(!VorbisEncoder.NativeAvailable, $"soundswap_vorbis.dll isn't built here ({VorbisEncoder.NativeProblem}).");
        foreach (var count in new[] { 2, 4, 6 })
        {
            var channels = Enumerable.Range(0, count).Select(c => TestAudio.Sine(48000, 48000, 220 + 110 * c, 0.4)).ToArray();
            var encoded = VorbisEncoder.Encode(channels, 48000, 0.4f, new Dictionary<string, string> { ["LOOPSTART"] = "0" }, null, CancellationToken.None);
            var decoded = AudioReader.DecodeOgg([.. encoded.Header, .. encoded.Audio], CancellationToken.None);
            Assert.Equal((count, 48000, 48000), (decoded.ChannelCount, decoded.SampleRate, decoded.Frames));
            Assert.Equal("0", decoded.Tags["LOOPSTART"]);
        }
    }

    /// <summary>
    /// libvorbis' 5.1 template codes the sixth channel as a subwoofer and cuts its treble. Layered songs are three
    /// stereo layers, so all six channels must come back as full-band as they went in.
    /// </summary>
    [SkippableFact]
    public void Every_channel_of_a_six_channel_song_keeps_its_treble()
    {
        Skip.If(!VorbisEncoder.NativeAvailable, $"soundswap_vorbis.dll isn't built here ({VorbisEncoder.NativeProblem}).");
        const int rate = 44100;
        var random = new Random(7);
        var noise = Enumerable.Range(0, rate * 2).Select(_ => (float)(random.NextDouble() * 0.6 - 0.3)).ToArray();
        // Above 2 kHz only: what a subwoofer channel would throw away.
        var highs = new float[noise.Length];
        for (var i = 1; i < noise.Length; i++) highs[i] = 0.7f * (highs[i - 1] + noise[i] - noise[i - 1]);
        var channels = Enumerable.Range(0, 6).Select(_ => (float[])highs.Clone()).ToArray();

        var encoded = VorbisEncoder.Encode(channels, rate, 0.5f, null, null, CancellationToken.None);
        var decoded = AudioReader.DecodeOgg([.. encoded.Header, .. encoded.Audio], CancellationToken.None);

        var rms = decoded.Channels.Select(c => 20 * Math.Log10(Math.Sqrt(c.Average(s => (double)s * s)))).ToArray();
        Assert.True(rms.Max() - rms.Min() < 1.0, $"channels differ: {string.Join(", ", rms.Select(r => r.ToString("F1")))} dB");
    }

    [SkippableFact]
    public void Cancelling_stops_the_native_encoder()
    {
        Skip.If(!VorbisEncoder.NativeAvailable, "soundswap_vorbis.dll isn't built here.");
        using var cts = new CancellationTokenSource();
        var channels = new[] { TestAudio.Sine(44100 * 20, 44100, 440, 0.5), TestAudio.Sine(44100 * 20, 44100, 440, 0.5) };
        Assert.ThrowsAny<OperationCanceledException>(() => VorbisEncoder.Encode(channels, 44100, 0.5f, null, f => { if (f > 0.1f) cts.Cancel(); }, cts.Token));
    }

    [Fact]
    public void Pages_close_often_enough_for_precise_loops()
    {
        var channels = new[] { TestAudio.Sine(44100 * 5, 44100, 440, 0.5), TestAudio.Sine(44100 * 5, 44100, 440, 0.5) };
        var encoded = VorbisEncoder.Encode(channels, 44100, 0.5f, null, null, CancellationToken.None);
        var pages = Scd.OggPages.Read(encoded.Audio).Where(p => p.Granule >= 0).ToList();
        var longest = pages.Zip(pages.Skip(1), (a, b) => b.Granule - a.Granule).Max();
        Assert.True(longest <= 44100 / 5, $"a page spans {longest} frames");
    }
}
