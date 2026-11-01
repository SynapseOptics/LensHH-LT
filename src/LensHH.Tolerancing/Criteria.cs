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
/// the chief ray's reference sphere at the primary wavelength. The sum runs over every direction
/// the lens is judged in (<see cref="Built.Views"/>), with the weights shared among them.
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
        double sum = 0.0, weights = 0.0;
        try
        {
            foreach (var view in lens.Views)
            {
                var system = view.System;
                foreach (var (f, w, weight) in Samples(system))
                {
                    var map = WavefrontMapCalculator.Compute(system, view.Glass, f, w, GridSize);
                    double rms = map.RmsWavefront;
                    if (!double.IsFinite(rms) || rms <= 0.0 && !AnyValid(map)) return double.PositiveInfinity;
                    sum += weight * rms * rms;
                    weights += weight;
                }
            }
        }
        catch (Exception e) when (e is InvalidOperationException or ArithmeticException or ArgumentException)
        {
            return double.PositiveInfinity;
        }
        return weights > 0.0 ? Math.Sqrt(sum / weights) : double.PositiveInfinity;
    }

    /// <summary>
    /// The criterion as a vector whose length is W: every pupil sample of every field and wavelength,
    /// its wavefront less the map's mean, scaled by √(weight / Σ weight / samples). A sample that does
    /// not arrive is NaN, so a vector keeps its length from lens to lens. Null when the lens cannot be
    /// traced. For a compensator's least squares.
    /// </summary>
    public double[]? Vector(Built lens)
    {
        var parts = new List<(double Weight, WavefrontResult Map)>();
        double weights = 0.0;
        try
        {
            foreach (var view in lens.Views)
                foreach (var (f, w, weight) in Samples(view.System))
                {
                    var map = WavefrontMapCalculator.Compute(view.System, view.Glass, f, w, GridSize);
                    if (!AnyValid(map)) return null;
                    parts.Add((weight, map));
                    weights += weight;
                }
        }
        catch (Exception e) when (e is InvalidOperationException or ArithmeticException or ArgumentException)
        {
            return null;
        }
        if (!(weights > 0.0)) return null;

        var vector = new List<double>();
        foreach (var (weight, map) in parts)
        {
            int n = 0;
            double sum = 0.0;
            foreach (var (v, ok) in Samples(map)) if (ok) { sum += v; n++; }
            double mean = sum / n, scale = Math.Sqrt(weight / weights / n);
            foreach (var (v, ok) in Samples(map)) vector.Add(ok ? scale * (v - mean) : double.NaN);
        }
        return vector.ToArray();
    }

    /// <summary>Each field and wavelength the criterion counts, with its weight: every one weighted above zero.</summary>
    private IEnumerable<(int Field, int Wave, double Weight)> Samples(LensHH.Core.Models.OpticalSystem system)
    {
        var waves = Polychromatic
            ? Enumerable.Range(0, system.Wavelengths.Count)
            : new[] { system.PrimaryWavelengthIndex };
        for (int f = 0; f < system.Fields.Count; f++)
            foreach (int w in waves)
            {
                double weight = system.Fields[f].Weight * system.Wavelengths[w].Weight;
                if (weight > 0.0) yield return (f, w, weight);
            }
    }

    private static IEnumerable<(double Value, bool Valid)> Samples(WavefrontResult map)
    {
        for (int i = 0; i < map.Opd.GetLength(0); i++)
            for (int j = 0; j < map.Opd.GetLength(1); j++)
                yield return (map.Opd[i, j], map.Valid[i, j]);
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
/// floor, in the worst direction the lens is judged in (<see cref="Mtf.Worst"/>).
/// W = √(1 − MTF), so that smaller is better as for every criterion and W² is the MTF's loss.
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

    /// <summary>
    /// The MTF in the worst of the directions the lens is judged in (<see cref="Built.Views"/>), each
    /// frequency and orientation its own lowest: the MTF the lens can be counted on for at this field
    /// whichever way across the field it lies. The field on the axis is the same in every direction,
    /// its tangential and sagittal already the two across the image, and is taken from the first.
    /// <paramref name="pupils"/>, one per view, as <see cref="Curve"/> takes one. A cancel stops it
    /// before the next direction.
    /// </summary>
    public static (double[] T, double[] S) Worst(Built lens, int field, IReadOnlyList<double> frequencies, int gridSize = 64,
                                                 IReadOnlyList<FftMtfCalculator.PupilGeometry>? pupils = null,
                                                 CancellationToken cancel = default)
    {
        var views = lens.Views;
        double[]? t = null, s = null;
        for (int v = 0; v < views.Count; v++)
        {
            if (v > 0 && Math.Abs(views[v].System.Fields[field].Y) < 1e-12) continue;
            cancel.ThrowIfCancellationRequested();
            var (tv, sv) = Curve(views[v], field, frequencies, gridSize, pupils?[v]);
            if (t == null || s == null) { (t, s) = (tv, sv); continue; }
            for (int k = 0; k < t.Length; k++)
            {
                t[k] = Math.Min(t[k], tv[k]);
                s[k] = Math.Min(s[k], sv[k]);
            }
        }
        return (t!, s!);
    }

    /// <summary>Each view's pupil at <paramref name="field"/>, measured once on the nominal lens for <see cref="Worst"/>.</summary>
    public static IReadOnlyList<FftMtfCalculator.PupilGeometry> Pupils(Built lens, int field) =>
        lens.Views.AsParallel().AsOrdered().Select(v => FftMtfCalculator.MeasurePupil(v.System, v.Glass, field)).ToArray();

    /// <summary>The MTF at one frequency and orientation, in the worst direction.</summary>
    public static double At(Built lens, int field, double frequency, MtfOrientation orientation, int gridSize = 64)
    {
        var (t, s) = Worst(lens, field, new[] { 0.0, frequency }, gridSize);
        return orientation == MtfOrientation.Tangential ? t[1] : s[1];
    }
}
