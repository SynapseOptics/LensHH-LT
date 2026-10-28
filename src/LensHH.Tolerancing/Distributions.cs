namespace LensHH.Tolerancing;

/// <summary>
/// How a manufacturing error is spread over its tolerance ±t, for one axis. Moments are of the
/// error itself, for a half-width of one: multiply σ² by t² and ν₄ by t⁴ (Koch 1978, Table 1).
/// </summary>
public abstract record Distribution
{
    /// <summary>Equally likely anywhere in ±t: σ² = t²/3, ν₄ = t⁴/5. Hilbert's default; Smith's conservative case.</summary>
    public static Distribution Uniform { get; } = new UniformDistribution();

    /// <summary>At +t or −t, each half the time: σ² = t², ν₄ = t⁴. A sensitivity run is this.</summary>
    public static Distribution EndPoint { get; } = new EndPointDistribution();

    /// <summary>Normal, cut off at ±t = <paramref name="sigmas"/> σ - 2 by default.</summary>
    public static Distribution Normal(double sigmas = 2.0) => new TruncatedNormalDistribution(sigmas);

    /// <summary>The second moment E[x²] for a half-width of one.</summary>
    public abstract double Variance { get; }

    /// <summary>The fourth moment E[x⁴] for a half-width of one.</summary>
    public abstract double Fourth { get; }

    /// <summary>The sixth moment E[x⁶] for a half-width of one, for the third cumulant of a quadratic.</summary>
    public abstract double Sixth { get; }

    /// <summary>A draw for a half-width of one.</summary>
    public abstract double Sample(Random random);
}

public sealed record UniformDistribution : Distribution
{
    public override double Variance => 1.0 / 3.0;
    public override double Fourth => 1.0 / 5.0;
    public override double Sixth => 1.0 / 7.0;
    public override double Sample(Random random) => 2.0 * random.NextDouble() - 1.0;
    public override string ToString() => "uniform";
}

public sealed record EndPointDistribution : Distribution
{
    public override double Variance => 1.0;
    public override double Fourth => 1.0;
    public override double Sixth => 1.0;
    public override double Sample(Random random) => random.NextDouble() < 0.5 ? -1.0 : 1.0;
    public override string ToString() => "end points";
}

/// <summary>
/// A unit normal kept within ±k and scaled by 1/k. With Z = 2Φ(k) − 1, integration by parts gives
/// E[u²] = 1 − 2kφ(k)/Z and E[u⁴] = 3E[u²] − 2k³φ(k)/Z and E[u⁶] = 5E[u⁴] − 2k⁵φ(k)/Z
/// for the unit normal u.
/// </summary>
public sealed record TruncatedNormalDistribution(double Sigmas) : Distribution
{
    public double Sigmas { get; } = Sigmas > 0.0 && double.IsFinite(Sigmas)
        ? Sigmas : throw new ArgumentOutOfRangeException(nameof(Sigmas), Sigmas, "a normal is cut off at a positive number of sigmas");

    private double Z => 2.0 * Special.NormalCdf(Sigmas) - 1.0;
    private double Phi => Special.NormalPdf(Sigmas);
    private double U2 => 1.0 - 2.0 * Sigmas * Phi / Z;

    public override double Variance => U2 / (Sigmas * Sigmas);
    private double U4 => 3.0 * U2 - 2.0 * Math.Pow(Sigmas, 3) * Phi / Z;
    public override double Fourth => U4 / Math.Pow(Sigmas, 4);
    public override double Sixth => (5.0 * U4 - 2.0 * Math.Pow(Sigmas, 5) * Phi / Z) / Math.Pow(Sigmas, 6);

    public override double Sample(Random random)
    {
        while (true)
        {
            double u = Special.StandardNormal(random);
            if (Math.Abs(u) <= Sigmas) return u / Sigmas;
        }
    }

    public override string ToString() => $"normal, ±{Sigmas:G3}σ";
}

/// <summary>
/// How the size ρ of an x-y error (a decentre or a tilt) is spread, its direction θ equally
/// likely all round (Koch 1978 eqs. 23-27). Moments are of ρ for a largest ρ of one, taken over
/// p′(ρ) = ρ p(ρ), the density of the radius rather than of the point (Koch Table 2).
/// </summary>
public abstract record RadialDistribution
{
    /// <summary>Uniform over the disc ρ ≤ t: ρ has the triangular density 2ρ; ν₂ = 1/2, ν₄ = 1/3.</summary>
    public static RadialDistribution Disc { get; } = new DiscDistribution();

    /// <summary>Always ρ = t: ν₂ = ν₄ = 1.</summary>
    public static RadialDistribution Ring { get; } = new RingDistribution();

    /// <summary>A circular normal in x and y - Rayleigh in ρ - cut off at t = <paramref name="sigmas"/> σ.</summary>
    public static RadialDistribution Rayleigh(double sigmas = 2.0) => new TruncatedRayleighDistribution(sigmas);

    public abstract double Second { get; }
    public abstract double Fourth { get; }
    public abstract double SampleRadius(Random random);

    /// <summary>E[ρⁿ] for a largest ρ of one, any n from 0 to 6: the odd ones too, for the third cumulant of a pair.</summary>
    public abstract double Moment(int n);
}

public sealed record DiscDistribution : RadialDistribution
{
    public override double Second => 0.5;
    public override double Fourth => 1.0 / 3.0;
    public override double Moment(int n) => 2.0 / (n + 2.0);
    public override double SampleRadius(Random random) => Math.Sqrt(random.NextDouble());
    public override string ToString() => "disc";
}

public sealed record RingDistribution : RadialDistribution
{
    public override double Second => 1.0;
    public override double Fourth => 1.0;
    public override double Moment(int n) => 1.0;
    public override double SampleRadius(Random random) => 1.0;
    public override string ToString() => "ring";
}

/// <summary>
/// ρ²/2σ² is exponential, here cut at K = k²/2: E[u] = (1 − (1+K)e^−K)/(1 − e^−K) and
/// E[u²] = (2 − (2 + 2K + K²)e^−K)/(1 − e^−K), so ν₂ = 2σ²E[u], ν₄ = 4σ⁴E[u²] with σ = 1/k.
/// </summary>
public sealed record TruncatedRayleighDistribution(double Sigmas) : RadialDistribution
{
    public double Sigmas { get; } = Sigmas > 0.0 && double.IsFinite(Sigmas)
        ? Sigmas : throw new ArgumentOutOfRangeException(nameof(Sigmas), Sigmas, "a Rayleigh is cut off at a positive number of sigmas");

    private double K => Sigmas * Sigmas / 2.0;
    private double Cut => 1.0 - Math.Exp(-K);

    public override double Second => 2.0 / (Sigmas * Sigmas) * (1.0 - (1.0 + K) * Math.Exp(-K)) / Cut;
    public override double Fourth => 4.0 / Math.Pow(Sigmas, 4) * (2.0 - (2.0 + 2.0 * K + K * K) * Math.Exp(-K)) / Cut;

    /// <summary>By Simpson's rule on the density ρ e^(−k²ρ²/2) over [0, 1]: smooth, so 2000 intervals are exact to 1e-13.</summary>
    public override double Moment(int n)
    {
        double F(double r) => Math.Pow(r, n + 1) * Math.Exp(-0.5 * Sigmas * Sigmas * r * r);
        double G(double r) => r * Math.Exp(-0.5 * Sigmas * Sigmas * r * r);
        return Simpson(F) / Simpson(G);
    }

    private static double Simpson(Func<double, double> f)
    {
        const int m = 2000;
        double h = 1.0 / m, sum = f(0.0) + f(1.0);
        for (int i = 1; i < m; i++) sum += f(i * h) * (i % 2 == 0 ? 2.0 : 4.0);
        return sum * h / 3.0;
    }

    public override double SampleRadius(Random random)
    {
        double u = -Math.Log(1.0 - random.NextDouble() * Cut);      // the exponential, cut at K
        return Math.Sqrt(2.0 * u) / Sigmas;
    }

    public override string ToString() => $"Rayleigh, ±{Sigmas:G3}σ";
}

/// <summary>
/// How the errors of a tolerance set are drawn: <see cref="Axis"/> for every error on its own, and
/// when <see cref="Polar"/> is set, an x-y pair of the same kind on the same surfaces as one error
/// of size ρ ≤ t in a direction all round - one wedge, one decentre - rather than two independent ones.
/// </summary>
public sealed record StatisticsModel(Distribution Axis, RadialDistribution? Polar = null)
{
    /// <summary>Uniform, x and y independent: the usual Monte Carlo.</summary>
    public static StatisticsModel Default { get; } = new(Distribution.Uniform);
}

internal static class Special
{
    public static double NormalPdf(double x) => Math.Exp(-0.5 * x * x) / Math.Sqrt(2.0 * Math.PI);

    public static double NormalCdf(double x) => 0.5 * Erfc(-x / Math.Sqrt(2.0));

    /// <summary>The complementary error function, to about 1e-7 (Numerical Recipes' erfcc, Chebyshev).</summary>
    public static double Erfc(double x)
    {
        double z = Math.Abs(x);
        double t = 1.0 / (1.0 + 0.5 * z);
        double r = t * Math.Exp(-z * z - 1.26551223 + t * (1.00002368 + t * (0.37409196 + t * (0.09678418 +
                   t * (-0.18628806 + t * (0.27886807 + t * (-1.13520398 + t * (1.48851587 +
                   t * (-0.82215223 + t * 0.17087277)))))))));
        return x >= 0.0 ? r : 2.0 - r;
    }


    /// <summary>The unit normal's quantile, by bisection on <see cref="NormalCdf"/>.</summary>
    public static double NormalQuantile(double p)
    {
        if (!(p > 0.0 && p < 1.0)) throw new ArgumentOutOfRangeException(nameof(p));
        double lo = -10.0, hi = 10.0;
        for (int i = 0; i < 100; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (NormalCdf(mid) < p) lo = mid; else hi = mid;
        }
        return 0.5 * (lo + hi);
    }

    /// <summary>Beyond this shape the gamma is all but normal, and Wilson and Hilferty's cube root is used.</summary>
    private const double LargeShape = 1000.0;

    /// <summary>
    /// The regularized lower incomplete gamma function P(a, x): its series below a + 1, Lentz's
    /// continued fraction for 1 − P above (Numerical Recipes §6.2). Beyond <see cref="LargeShape"/>, where both converge slowly,
    /// Wilson and Hilferty's (1931): (x/a)^⅓ is close to normal, mean 1 − 1/9a, variance 1/9a.
    /// </summary>
    public static double GammaP(double a, double x)
    {
        if (x <= 0.0) return 0.0;
        if (a > LargeShape) return NormalCdf((Math.Cbrt(x / a) - 1.0 + 1.0 / (9.0 * a)) * 3.0 * Math.Sqrt(a));
        double lnPrefix = -x + a * Math.Log(x) - LogGamma(a);
        if (x < a + 1.0)
        {
            double term = 1.0 / a, sum = term;
            for (int n = 1; n < 1000 && Math.Abs(term) > 1e-16 * Math.Abs(sum); n++)
            {
                term *= x / (a + n);
                sum += term;
            }
            return sum * Math.Exp(lnPrefix);
        }
        const double tiny = 1e-300;
        double b = x + 1.0 - a, c = 1.0 / tiny, d = 1.0 / b, h = d;
        for (int i = 1; i < 1000; i++)
        {
            double an = -i * (i - a);
            b += 2.0;
            d = an * d + b;
            if (Math.Abs(d) < tiny) d = tiny;
            c = b + an / c;
            if (Math.Abs(c) < tiny) c = tiny;
            d = 1.0 / d;
            double delta = d * c;
            h *= delta;
            if (Math.Abs(delta - 1.0) < 1e-16) break;
        }
        return 1.0 - Math.Exp(lnPrefix) * h;
    }

    /// <summary>The x at which P(a, x) = p, by bisection.</summary>
    public static double GammaQuantile(double a, double p)
    {
        if (!(p > 0.0 && p < 1.0)) throw new ArgumentOutOfRangeException(nameof(p));
        if (a > LargeShape) return a * Math.Pow(1.0 - 1.0 / (9.0 * a) + NormalQuantile(p) / (3.0 * Math.Sqrt(a)), 3);
        double lo = 0.0, hi = Math.Max(1.0, a);
        while (GammaP(a, hi) < p) hi *= 2.0;
        for (int i = 0; i < 200 && hi - lo > 1e-14 * hi; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (GammaP(a, mid) < p) lo = mid; else hi = mid;
        }
        return 0.5 * (lo + hi);
    }

    /// <summary>ln Γ(x) for x &gt; 0, Lanczos (g = 7, n = 9), to about 1e-15.</summary>
    public static double LogGamma(double x)
    {
        if (x < 0.5) return Math.Log(Math.PI / Math.Sin(Math.PI * x)) - LogGamma(1.0 - x);
        double[] g =
        {
            0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313,
            -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6,
            1.5056327351493116e-7,
        };
        x -= 1.0;
        double a = g[0], t = x + 7.5;
        for (int i = 1; i < 9; i++) a += g[i] / (x + i);
        return 0.5 * Math.Log(2.0 * Math.PI) + (x + 0.5) * Math.Log(t) - t + Math.Log(a);
    }

    /// <summary>A unit normal draw, Box-Muller.</summary>
    public static double StandardNormal(Random random)
    {
        double u1 = 1.0 - random.NextDouble();
        double u2 = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}

/// <summary>
/// SplitMix64 (Steele, Lea and Flood 2014): a generator seeded in a few operations, for the model's
/// own Monte Carlo, which seeds one per lens so that its draws are the same however they are threaded.
/// </summary>
internal sealed class SplitMix : Random
{
    private ulong _state;

    public SplitMix(ulong seed) => _state = seed;

    public override double NextDouble()
    {
        ulong z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (z >> 11) * (1.0 / 9007199254740992.0);
    }

    protected override double Sample() => NextDouble();
}
