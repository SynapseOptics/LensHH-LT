using LensHH.Core.IO;
using Xunit;

namespace LensHH.Tolerancing.Tests;

// The Monte Carlo's random errors against the spreads they are drawn from: every error inside its
// range, centred where the range is, spread as its distribution says, and independent of the others.
public class DrawTests
{
    private const int N = 40000;

    /// <summary>E[u²] of a unit normal cut at ±k and scaled by 1/k, by the trapezoid rule on its density.</summary>
    private static double TruncatedNormalVariance(double k)
    {
        const int steps = 20000;
        double num = 0, den = 0;
        for (int i = 0; i <= steps; i++)
        {
            double x = -k + 2 * k * i / steps, w = (i == 0 || i == steps ? 0.5 : 1.0) * Math.Exp(-0.5 * x * x);
            num += w * x * x;
            den += w;
        }
        return num / den / (k * k);
    }

    private static double[][] DrawMany(IReadOnlyList<ToleranceItem> items, StatisticsModel statistics)
    {
        var groups = MonteCarlo.Groups(items, statistics);
        var random = new Random(12345);
        return Enumerable.Range(0, N).Select(_ => MonteCarlo.Draw(groups, items, statistics, random)).ToArray();
    }

    [Fact]
    public void EachErrorIsSpreadAsItsDistributionSays()
    {
        var items = ToleranceSpec.ParseSet("""
            TTHI 1 0.2                  # uniform, the default
            TTHI 2 -0.02 0.08           # uniform about a bias
            TTHI 3 0.1 normal           # normal, ±2σ
            TTHI 4 0.1 normal:3         # normal, ±3σ
            TTHI 5 0.05 endpoint
            """);
        var x = DrawMany(items, StatisticsModel.Default);
        var expected = new[] { 1.0 / 3.0, 1.0 / 3.0, TruncatedNormalVariance(2), TruncatedNormalVariance(3), 1.0 };

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var v = x.Select(r => r[i]).ToArray();
            Assert.All(v, e => Assert.InRange(e, item.Min, item.Max));
            double h = item.HalfWidth;
            double mean = v.Average();
            double variance = v.Select(e => (e - item.Center) * (e - item.Center)).Average() / (h * h);
            // Within four standard errors: of the mean, √(σ²/N); of the variance, about √(2/N) of it or less.
            Assert.True(Math.Abs(mean - item.Center) < 4 * h * Math.Sqrt(expected[i] / N), $"{item}: mean {mean}");
            Assert.True(Math.Abs(variance - expected[i]) < 4 * expected[i] * Math.Sqrt(2.0 / N), $"{item}: variance {variance} against {expected[i]}");
        }
        // An end point is at an end, never between.
        Assert.All(x.Select(r => r[4]), e => Assert.Equal(0.05, Math.Abs(e), 12));
        // A uniform error fills its range: each tenth of it gets its share.
        var counts = new int[10];
        foreach (var r in x) counts[Math.Min(9, (int)((r[0] + 0.2) / 0.04))]++;
        Assert.All(counts, c => Assert.InRange(c, N / 10 - 4 * Math.Sqrt(N * 0.09), N / 10 + 4 * Math.Sqrt(N * 0.09)));
    }

    [Fact]
    public void ErrorsAreIndependent()
    {
        var items = ToleranceSpec.ParseSet("""
            TTHI 1 0.2
            TTHI 2 0.2
            TETX 1-2 0.1
            TETY 1-2 0.1
            """);
        var x = DrawMany(items, StatisticsModel.Default);
        for (int i = 0; i < items.Count; i++)
            for (int j = i + 1; j < items.Count; j++)
            {
                double r = Correlation(x.Select(t => t[i]).ToArray(), x.Select(t => t[j]).ToArray());
                Assert.True(Math.Abs(r) < 4 / Math.Sqrt(N), $"{items[i]} and {items[j]}: correlation {r}");
            }
    }

    [Fact]
    public void AnXYPairDrawnAsOneFillsTheDisc()
    {
        var items = ToleranceSpec.ParseSet("""
            TETX 1-2 0.1
            TETY 1-2 0.1
            """);
        var x = DrawMany(items, new StatisticsModel(Distribution.Uniform, RadialDistribution.Disc));
        var rho2 = x.Select(t => (t[0] * t[0] + t[1] * t[1]) / 0.01).ToArray();
        Assert.All(rho2, r => Assert.True(r <= 1.0 + 1e-12));
        // Uniform over the disc: E[ρ²] = t²/2, and half the points inside ρ = t/√2.
        Assert.True(Math.Abs(rho2.Average() - 0.5) < 4 * Math.Sqrt(1.0 / 12.0 / N));
        Assert.True(Math.Abs(rho2.Count(r => r < 0.5) / (double)N - 0.5) < 4 * Math.Sqrt(0.25 / N));
        // Every direction alike.
        double cos = x.Select(t => t[0] / Math.Sqrt(t[0] * t[0] + t[1] * t[1] + 1e-300)).Average();
        Assert.True(Math.Abs(cos) < 4 * Math.Sqrt(0.5 / N));
    }

    [Fact]
    public void EveryMonteCarloLensDrawsItsOwnErrors()
    {
        var lens = new NominalLens(Lenses.Singlet(), Lenses.Glass(), FieldSymmetry.File);
        var items = ToleranceSpec.ParseSet("""
            TTHI 1 0.2
            TRAD 1 0.5
            """);
        var mc = MonteCarlo.Run(lens, items, new RmsWavefrontCriterion { GridSize = 8 }, null,
                                new MonteCarloOptions { Trials = 400, Seed = 9 });
        var thickness = mc.Trials.Select(t => t.Errors[0]).ToArray();
        Assert.All(mc.Trials, t => Assert.InRange(t.Errors[0], -0.2, 0.2));
        Assert.All(mc.Trials, t => Assert.InRange(t.Errors[1], -0.5, 0.5));
        Assert.Equal(thickness.Length, thickness.Distinct().Count());
        Assert.True(Math.Abs(Correlation(thickness, mc.Trials.Select(t => t.Errors[1]).ToArray())) < 4 / Math.Sqrt(400));
        // Neighbouring lenses are no more alike than any two.
        Assert.True(Math.Abs(Correlation(thickness[..^1], thickness[1..])) < 4 / Math.Sqrt(400));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(-4)]
    public void LensesOfARunAreUnrelated(int seed)
    {
        // Each lens's first draw, across a run: no lens like the next, and spread as random draws
        // are - the counts in tenths of the range scatter as a binomial does, neither more nor (as a
        // grid would) far less.
        var first = Enumerable.Range(0, N).Select(k => SplitMix.ForTrial(seed, k).NextDouble()).ToArray();
        var second = Enumerable.Range(0, N).Select(k => { var r = SplitMix.ForTrial(seed, k); r.NextDouble(); return r.NextDouble(); }).ToArray();
        Assert.True(Math.Abs(Correlation(first[..^1], first[1..])) < 4 / Math.Sqrt(N));
        Assert.True(Math.Abs(Correlation(first, second)) < 4 / Math.Sqrt(N));
        Assert.True(Math.Abs(Correlation(first[..^1], second[1..])) < 4 / Math.Sqrt(N));
        var counts = new double[10];
        foreach (var v in first) counts[(int)(v * 10)]++;
        double expected = N / 10.0, chi2 = counts.Sum(c => (c - expected) * (c - expected) / expected);
        Assert.InRange(chi2, 1.0, 27.9);     // 9 degrees of freedom: the 0.1 % and 99.9 % points
    }

    private static double Correlation(double[] a, double[] b)
    {
        double ma = a.Average(), mb = b.Average();
        double sab = 0, saa = 0, sbb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            sab += (a[i] - ma) * (b[i] - mb);
            saa += (a[i] - ma) * (a[i] - ma);
            sbb += (b[i] - mb) * (b[i] - mb);
        }
        return sab / Math.Sqrt(saa * sbb);
    }
}
