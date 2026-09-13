namespace Soundswap.Core.Audio;

/// <summary>
/// Integrated loudness after ITU-R BS.1770-4 (the LUFS every streaming service uses): K-weighting, 400 ms
/// blocks with 75% overlap, an absolute gate at -70 LUFS and a relative gate 10 LU below. All channels count
/// the same, which is right for stereo pairs and layers.
/// </summary>
public static class Loudness
{
    /// <summary>LUFS of the whole clip, or null when it is silent.</summary>
    public static double? Integrated(AudioClip clip) => Integrated(clip.Channels, clip.SampleRate);

    public static double? Integrated(IReadOnlyList<float[]> channels, int sampleRate)
    {
        if (channels.Count == 0 || channels[0].Length == 0) return null;
        var hop = sampleRate / 10;
        var hops = channels[0].Length / hop;
        if (hops < 4) return null;

        // Mean square of every 100 ms hop, summed over channels, after K-weighting.
        var hopPower = new double[hops];
        foreach (var channel in channels)
        {
            var shelf = Biquad.HighShelf(sampleRate, 1500, 4.0, 1 / Math.Sqrt(2));
            var highPass = Biquad.HighPass(sampleRate, 38, 0.5);
            for (var h = 0; h < hops; h++)
            {
                double sum = 0;
                var start = h * hop;
                for (var i = start; i < start + hop; i++)
                {
                    var y = highPass.Process(shelf.Process(channel[i]));
                    sum += y * y;
                }
                hopPower[h] += sum / hop;
            }
        }

        var blocks = new double[hops - 3];
        for (var b = 0; b < blocks.Length; b++) blocks[b] = (hopPower[b] + hopPower[b + 1] + hopPower[b + 2] + hopPower[b + 3]) / 4;

        var aboveAbsolute = blocks.Where(z => Lufs(z) > -70).ToList();
        if (aboveAbsolute.Count == 0) return null;
        var relativeGate = Lufs(aboveAbsolute.Average()) - 10;
        var gated = aboveAbsolute.Where(z => Lufs(z) > relativeGate).ToList();
        return gated.Count == 0 ? null : Lufs(gated.Average());
    }

    private static double Lufs(double power) => -0.691 + 10 * Math.Log10(Math.Max(power, 1e-12));

    private sealed class Biquad(double b0, double b1, double b2, double a1, double a2)
    {
        private double x1, x2, y1, y2;

        public double Process(double x)
        {
            var y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1;
            x1 = x;
            y2 = y1;
            y1 = y;
            return y;
        }

        public static Biquad HighShelf(int rate, double freq, double gainDb, double q)
        {
            var a = Math.Pow(10, gainDb / 40);
            var w0 = 2 * Math.PI * freq / rate;
            var alpha = Math.Sin(w0) / (2 * q);
            var cos = Math.Cos(w0);
            var sq = 2 * Math.Sqrt(a) * alpha;
            var a0 = (a + 1) - (a - 1) * cos + sq;
            return new Biquad(
                a * ((a + 1) + (a - 1) * cos + sq) / a0,
                -2 * a * ((a - 1) + (a + 1) * cos) / a0,
                a * ((a + 1) + (a - 1) * cos - sq) / a0,
                2 * ((a - 1) - (a + 1) * cos) / a0,
                ((a + 1) - (a - 1) * cos - sq) / a0);
        }

        public static Biquad HighPass(int rate, double freq, double q)
        {
            var w0 = 2 * Math.PI * freq / rate;
            var alpha = Math.Sin(w0) / (2 * q);
            var cos = Math.Cos(w0);
            var a0 = 1 + alpha;
            return new Biquad((1 + cos) / 2 / a0, -(1 + cos) / a0, (1 + cos) / 2 / a0, -2 * cos / a0, (1 - alpha) / a0);
        }
    }
}
