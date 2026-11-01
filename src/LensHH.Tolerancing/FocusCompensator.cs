namespace LensHH.Tolerancing;

/// <summary>
/// A thickness adjusted after the lens is built to bring back the best performance it can - the
/// back focus by default, as a camera is focused. Its value is a change from the nominal thickness.
///
/// <para>With a <see cref="Travel"/>, the thickness may move at most that far either way from
/// <see cref="Center"/>, as a focus mechanism allows; a lens that needs more is left at the end of it.</para>
///
/// <para>The search starts where the paraxial focus has moved to, which for a focus compensator is
/// nearly always close, and finds the minimum of the criterion by Brent's method within a bracket a
/// few depths of focus wide, widening the bracket while the minimum sits on its edge.</para>
/// </summary>
public sealed record FocusCompensator(int Surface, double? Travel = null)
{
    /// <summary>The middle of the travel, as a change from the lens's thickness: where it refocuses the nominal lens.</summary>
    public double Center { get; init; }

    /// <summary>
    /// Adjustments made beside the focus and solved with it (<see cref="CompensatorSolver"/>): air
    /// spaces, element tilts and decentres. None by default, and then the focus is found alone.
    /// </summary>
    public IReadOnlyList<Adjustment> Adjustments { get; init; } = Array.Empty<Adjustment>();

    /// <summary>This compensator with no travel limit on the focus or any adjustment.</summary>
    public FocusCompensator Free() => this with { Travel = null, Adjustments = Adjustments.Select(a => a with { Travel = null }).ToList() };

    /// <summary>The thickness before the image: refocusing.</summary>
    public static FocusCompensator BackFocus(NominalLens lens, double? travel = null) => new(lens.BackFocusSurface, travel);

    /// <summary>This compensator with its travel centred on where it refocuses the nominal lens.</summary>
    public FocusCompensator Centred(NominalLens lens, ICriterion criterion) =>
        this with { Center = Free().Solve(lens.Perturb(), criterion).Change };

    /// <summary>
    /// Refocuses the lens - by the criterion itself, or for an MTF by its <see cref="IFocusedCriterion.Focus"/>,
    /// the sharpest image - and leaves it there, with the criterion where it ends up.
    /// </summary>
    public Compensated Solve(TrialLens lens, ICriterion criterion, CancellationToken cancel = default)
    {
        if (criterion is not IFocusedCriterion focusedBy) return Refocus(lens, criterion, cancel);
        var focused = Refocus(lens, focusedBy.Focus, cancel);
        cancel.ThrowIfCancellationRequested();
        return focused with { Criterion = criterion.Evaluate(lens.Build()) };
    }

    private Compensated Refocus(TrialLens lens, ICriterion criterion, CancellationToken cancel) =>
        Adjustments.Count == 0 ? RefocusAlone(lens, criterion, cancel) : CompensatorSolver.Solve(this, lens, criterion, cancel);

    /// <summary>
    /// The focus alone, by Brent's method on the criterion; on <paramref name="lens"/> itself, where it
    /// leaves the thickness. A cancel stops it before the next evaluation of the criterion.
    /// </summary>
    internal Compensated RefocusAlone(TrialLens lens, ICriterion criterion, CancellationToken cancel = default)
    {
        var surface = lens.System.Surfaces[Surface];
        double start = lens.Nominal.System.Surfaces[Surface].Thickness;
        double baseline = surface.Thickness;   // a tolerance may have changed this thickness itself

        double Score(double change)
        {
            cancel.ThrowIfCancellationRequested();
            surface.Thickness = baseline + change;
            return criterion.Evaluate(lens.Build());
        }

        // Where the paraxial image went, when this thickness is the one in front of it. From the
        // lens's own surfaces, numbered as the nominal's: the built lens's back focus is measured
        // from its last surface, which with the elements laid out between breaks is not the lens's
        // last (the guess was some 3 mm off on a Heliar trial, and the search walked it back).
        double guess = 0.0;
        var nominal = lens.Nominal.Paraxial;
        if (Surface == lens.Nominal.BackFocusSurface)
        {
            surface.Thickness = baseline;
            double moved = Built.ParaxialOf(lens.System, lens.Nominal.Glass).Bfl - nominal.Bfl;
            if (double.IsFinite(moved)) guess = start + moved - baseline;
        }

        var sys = lens.Nominal.System;
        double waveMm = sys.Wavelengths[sys.PrimaryWavelengthIndex].Value * 1e-3;
        double fNumber = nominal.EntrancePupilDiameter > 0 ? Math.Abs(nominal.Efl / nominal.EntrancePupilDiameter) : 5.0;
        if (!double.IsFinite(fNumber) || fNumber <= 0) fNumber = 5.0;
        double depth = 2.0 * waveMm * fNumber * fNumber;            // λ/4 of defocus, about
        double half = Math.Max(8.0 * depth, 0.5 * Math.Abs(guess));
        if (!double.IsFinite(half) || half <= 0.0) half = 1e-3 * Math.Max(1.0, Math.Abs(start));

        // Bracket the minimum before searching it: downhill from the guess in steps that double,
        // until the criterion rises again. Brent's search in a bracket that held no minimum crept to
        // its edge a little at a time - on a tolerancing trial whose focus moved past it, some 20
        // evaluations of the criterion before the bracket widened, two thirds of the lens's time.
        if (Bracket(Score, guess, half) is var (lo, hi))
        {
            var (inside, insideScore) = Brent.Minimize(Score, lo, hi, tolerance: depth * 1e-3);
            double margin = 1e-3 * (hi - lo);
            bool edge = Math.Abs(inside - lo) < margin || Math.Abs(inside - hi) < margin;
            if (!edge && double.IsFinite(insideScore)) return Finish(inside, insideScore);
            guess = inside;
        }

        double best = guess, score = double.PositiveInfinity;
        for (int widen = 0; widen < 6; widen++)
        {
            (best, score) = Brent.Minimize(Score, guess - half, guess + half, tolerance: depth * 1e-3);
            bool onEdge = Math.Abs(best - (guess - half)) < 1e-3 * half || Math.Abs(best - (guess + half)) < 1e-3 * half;
            if (!onEdge && double.IsFinite(score)) break;
            guess = best;
            half *= 4.0;
        }
        return Finish(best, score);

        Compensated Finish(double found, double value)
        {
            double change = baseline + found - start;
            if (Travel is double travel && Math.Abs(change - Center) > travel)
            {
                // The criterion is unimodal in focus, so the best within the travel is its end.
                double end = Center + Math.CopySign(travel, change - Center);
                value = Score(start + end - baseline);
                return new Compensated(end, value, AtLimit: true);
            }
            surface.Thickness = baseline + found;
            return new Compensated(change, value);
        }
    }

    /// <summary>
    /// An interval (a, b) with a point inside that scores no worse than its ends - one that holds a
    /// minimum - from centre ± half, walking downhill in steps that double. Null when the scores are
    /// not finite or no such interval turns up in 12 steps (4096 times the first).
    /// </summary>
    internal static (double A, double B)? Bracket(Func<double, double> score, double centre, double half)
    {
        double a = centre - half, m = centre, b = centre + half;
        double fa = score(a), fm = score(m), fb = score(b);
        for (int step = 0; step < 12; step++)
        {
            if (!double.IsFinite(fm) && !double.IsFinite(fa) && !double.IsFinite(fb)) return null;
            if (fm <= fa && fm <= fb && double.IsFinite(fm)) return (a, b);
            if (fa < fb || !double.IsFinite(fb) && double.IsFinite(fa))
            {
                double width = m - a;
                (b, fb) = (m, fm);
                (m, fm) = (a, fa);
                a = m - 2.0 * width;
                fa = score(a);
            }
            else
            {
                double width = b - m;
                (a, fa) = (m, fm);
                (m, fm) = (b, fb);
                b = m + 2.0 * width;
                fb = score(b);
            }
        }
        return null;
    }
}

/// <summary>A compensator's change from its nominal value, the criterion it reaches, and whether it ran out of travel.</summary>
public readonly record struct Compensated(double Change, double Criterion, bool AtLimit = false)
{
    /// <summary>Each of the compensator's <see cref="FocusCompensator.Adjustments"/>, as it was set, in its units; null when it has none.</summary>
    public IReadOnlyList<double>? Adjustments { get; init; }
}

/// <summary>Brent's minimization on an interval (Brent 1973, ch. 5; the routine is fmin).</summary>
public static class Brent
{
    public static (double X, double F) Minimize(Func<double, double> f, double a, double b,
                                                double tolerance = 1e-8, int maxIterations = 100)
    {
        const double golden = 0.3819660112501051;
        double x = a + golden * (b - a), w = x, v = x;
        double fx = f(x), fw = fx, fv = fx;
        double d = 0.0, e = 0.0;

        for (int i = 0; i < maxIterations; i++)
        {
            double m = 0.5 * (a + b);
            double tol1 = 1e-10 * Math.Abs(x) + tolerance, tol2 = 2.0 * tol1;
            if (Math.Abs(x - m) <= tol2 - 0.5 * (b - a)) break;

            bool golden_ = true;
            if (Math.Abs(e) > tol1 && double.IsFinite(fx) && double.IsFinite(fw) && double.IsFinite(fv))
            {
                // Parabola through x, w, v.
                double r = (x - w) * (fx - fv), q = (x - v) * (fx - fw);
                double p = (x - v) * q - (x - w) * r;
                q = 2.0 * (q - r);
                if (q > 0.0) p = -p; else q = -q;
                double eOld = e;
                e = d;
                if (Math.Abs(p) < Math.Abs(0.5 * q * eOld) && p > q * (a - x) && p < q * (b - x))
                {
                    d = p / q;
                    double u0 = x + d;
                    if (u0 - a < tol2 || b - u0 < tol2) d = x < m ? tol1 : -tol1;
                    golden_ = false;
                }
            }
            if (golden_)
            {
                e = (x < m ? b : a) - x;
                d = golden * e;
            }

            double u = Math.Abs(d) >= tol1 ? x + d : x + (d > 0.0 ? tol1 : -tol1);
            double fu = f(u);
            if (fu <= fx)
            {
                if (u < x) b = x; else a = x;
                (v, fv) = (w, fw);
                (w, fw) = (x, fx);
                (x, fx) = (u, fu);
            }
            else
            {
                if (u < x) a = u; else b = u;
                if (fu <= fw || w == x)
                {
                    (v, fv) = (w, fw);
                    (w, fw) = (u, fu);
                }
                else if (fu <= fv || v == x || v == w)
                {
                    (v, fv) = (u, fu);
                }
            }
        }
        return (x, fx);
    }
}
