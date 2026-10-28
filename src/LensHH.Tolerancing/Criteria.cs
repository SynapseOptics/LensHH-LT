using LensHH.Core.Analysis;

namespace LensHH.Tolerancing;

/// <summary>
/// What a lens is judged by: a value W ≥ 0, smaller better, +∞ for a lens that cannot be traced.
/// </summary>
public interface ICriterion
{
    string Name { get; }

    /// <summary>The units of W and of a limit on it: "waves", "mm".</summary>
    string Units { get; }

    double Evaluate(Built lens);

    /// <summary>The value as it is quoted, and a limit is given: W itself, or for an MTF the MTF, 1 − W².</summary>
    double Quoted(double w) => w;

    /// <summary>W for a quoted value: the inverse of <see cref="Quoted"/>.</summary>
    double FromQuoted(double value) => value;

    /// <summary>Whether a limit on the quoted value is a floor, larger being better - an MTF - rather than a ceiling.</summary>
    bool IsFloor => false;
}

/// <summary>A criterion measured on the lens as a compensator leaves it after refocusing by <see cref="Focus"/>.</summary>
public interface IFocusedCriterion : ICriterion
{
    RmsWavefrontCriterion Focus { get; }
}

/// <summary>
/// The RMS wavefront error, in waves, over the fields and (polychromatic) wavelengths, weighted by
/// their weights: √(Σ w·rms² / Σ w). Each field and wavelength's RMS is the wavefront map's, about
/// the chief ray's reference sphere at the primary wavelength.
/// </summary>
public sealed class RmsWavefrontCriterion : ICriterion
{
    /// <summary>The wavefront map's grid across the pupil. 32 is enough to focus by and to rank errors.</summary>
    public int GridSize { get; init; } = 32;

    /// <summary>Every wavelength by its weight, or the primary alone.</summary>
    public bool Polychromatic { get; init; } = true;

    public string Name => Polychromatic ? "RMS wavefront (waves, polychromatic)" : "RMS wavefront (waves, primary λ)";

    public string Units => "waves";

    public double Evaluate(Built lens)
    {
        var system = lens.System;
        var waves = Polychromatic
            ? Enumerable.Range(0, system.Wavelengths.Count)
            : new[] { system.PrimaryWavelengthIndex };
        double sum = 0.0, weights = 0.0;
        try
        {
            for (int f = 0; f < system.Fields.Count; f++)
                foreach (int w in waves)
                {
                    double weight = system.Fields[f].Weight * system.Wavelengths[w].Weight;
                    if (weight <= 0.0) continue;
                    var map = WavefrontMapCalculator.Compute(system, lens.Glass, f, w, GridSize);
                    double rms = map.RmsWavefront;
                    if (!double.IsFinite(rms) || rms <= 0.0 && !AnyValid(map)) return double.PositiveInfinity;
                    sum += weight * rms * rms;
                    weights += weight;
                }
        }
        catch (Exception e) when (e is InvalidOperationException or ArithmeticException or ArgumentException)
        {
            return double.PositiveInfinity;
        }
        return weights > 0.0 ? Math.Sqrt(sum / weights) : double.PositiveInfinity;
    }

    private static bool AnyValid(WavefrontResult map)
    {
        foreach (bool v in map.Valid) if (v) return true;
        return false;
    }
}

public enum MtfOrientation { Sagittal, Tangential }

/// <summary>
/// The polychromatic diffraction (FFT) MTF at one field, frequency and orientation, held above a
/// floor. W = √(1 − MTF), so that smaller is better as for every criterion and W² is the MTF's loss.
/// The lens is refocused by <see cref="Focus"/>, the sharpest image, as it would be focused in use.
/// </summary>
public sealed record MtfCriterion : IFocusedCriterion
{
    public RmsWavefrontCriterion Focus { get; init; } = new();

    /// <summary>The field, numbered from 1 as the lens's fields.</summary>
    public int Field { get; init; } = 1;

    /// <summary>Spatial frequency, cycles per mm.</summary>
    public double Frequency { get; init; }

    public MtfOrientation Orientation { get; init; } = MtfOrientation.Tangential;

    /// <summary>The FFT's pupil grid.</summary>
    public int GridSize { get; init; } = 64;

    public string Name => $"MTF {(Orientation == MtfOrientation.Tangential ? "T" : "S")} at {Frequency:G4} cy/mm, field {Field}";

    public string Units => "";

    public bool IsFloor => true;

    public double Quoted(double w) => 1.0 - w * w;

    public double FromQuoted(double value) => Math.Sqrt(Math.Max(0.0, 1.0 - value));

    public double Evaluate(Built lens)
    {
        double m = Mtf.At(lens, Field - 1, Frequency, Orientation, GridSize);
        return double.IsFinite(m) ? Math.Sqrt(Math.Max(0.0, 1.0 - m)) : double.PositiveInfinity;
    }

    /// <summary>"mtf:field:frequency:s|t", as mtf:2:30:t.</summary>
    public static MtfCriterion Parse(string text, RmsWavefrontCriterion focus)
    {
        var c = System.Globalization.CultureInfo.InvariantCulture;
        var parts = text.Split(':');
        if (parts.Length != 4 || !parts[0].Equals("mtf", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, c, out int field) || field < 1
            || !double.TryParse(parts[2], System.Globalization.NumberStyles.Float, c, out double frequency)
            || !(frequency > 0.0))
            throw new ArgumentException($"{text}: write an MTF as mtf:field:frequency:s|t, as mtf:2:30:t");
        var orientation = parts[3].ToLowerInvariant() switch
        {
            "t" or "tangential" => MtfOrientation.Tangential,
            "s" or "sagittal" => MtfOrientation.Sagittal,
            _ => throw new ArgumentException($"{text}: the orientation is s (sagittal) or t (tangential)"),
        };
        return new MtfCriterion { Focus = focus, Field = field, Frequency = frequency, Orientation = orientation };
    }
}

/// <summary>The polychromatic FFT MTF of a built lens, sampled where a tolerance run needs it.</summary>
public static class Mtf
{
    /// <summary>
    /// Tangential and sagittal MTF at each of <paramref name="frequencies"/> (cycles/mm) for field
    /// <paramref name="field"/> (0-based), interpolated on the FFT's own frequency grid. NaN where the
    /// lens cannot be traced; 0 beyond the cutoff. A <paramref name="pupil"/> measured once on the
    /// nominal lens spares each trial measuring its own, which is most of an MTF's cost.
    /// </summary>
    public static (double[] T, double[] S) Curve(Built lens, int field, IReadOnlyList<double> frequencies, int gridSize = 64,
                                                 FftMtfCalculator.PupilGeometry? pupil = null)
    {
        var t = new double[frequencies.Count];
        var s = new double[frequencies.Count];
        MtfResult r;
        try
        {
            double step = frequencies.Count > 1 ? Math.Max(1e-6, Math.Min(5.0, frequencies[1] - frequencies[0])) : 5.0;
            r = FftMtfCalculator.ComputePolychromatic(lens.System, lens.Glass, field, gridSize, step, pupil: pupil);
        }
        catch (Exception e) when (e is InvalidOperationException or ArithmeticException or ArgumentException)
        {
            Array.Fill(t, double.NaN);
            Array.Fill(s, double.NaN);
            return (t, s);
        }
        var p = r.Points;
        if (p.Count == 0)
        {
            Array.Fill(t, double.NaN);
            Array.Fill(s, double.NaN);
            return (t, s);
        }
        for (int k = 0; k < frequencies.Count; k++)
        {
            double f = frequencies[k];
            if (f >= p[^1].SpatialFrequency) { t[k] = f == p[^1].SpatialFrequency ? p[^1].Tangential : 0.0; s[k] = f == p[^1].SpatialFrequency ? p[^1].Sagittal : 0.0; continue; }
            int i = 0;
            while (i + 1 < p.Count && p[i + 1].SpatialFrequency < f) i++;
            var a = p[i];
            var b = p[Math.Min(i + 1, p.Count - 1)];
            double span = b.SpatialFrequency - a.SpatialFrequency;
            double u = span > 0 ? (f - a.SpatialFrequency) / span : 0.0;
            t[k] = a.Tangential + u * (b.Tangential - a.Tangential);
            s[k] = a.Sagittal + u * (b.Sagittal - a.Sagittal);
        }
        return (t, s);
    }

    /// <summary>The MTF at one frequency and orientation.</summary>
    public static double At(Built lens, int field, double frequency, MtfOrientation orientation, int gridSize = 64)
    {
        var (t, s) = Curve(lens, field, new[] { 0.0, frequency }, gridSize);
        return orientation == MtfOrientation.Tangential ? t[1] : s[1];
    }
}
