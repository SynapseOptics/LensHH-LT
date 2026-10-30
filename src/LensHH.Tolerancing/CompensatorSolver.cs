namespace LensHH.Tolerancing;

/// <summary>
/// The focus and every other adjustment of a <see cref="FocusCompensator"/>, set together to
/// minimize the criterion.
///
/// <para>The focus is first found alone, as it would be without the others - Brent's method from
/// the paraxial focus - and the zero of every other adjustment, the nominal lens's setting, is the
/// start for the rest. Then all are refined together by damped least squares (Levenberg-Marquardt)
/// on the criterion's <see cref="RmsWavefrontCriterion.Vector"/>: W² is the vector's length squared,
/// and a small adjustment changes each sample of it linearly, so a few steps converge. The
/// derivatives are forward differences, a sample vignetted at either end of one counting as
/// unchanged. A step is kept only if it lowers the criterion itself, the damping growing until one
/// does.</para>
///
/// <para>Each adjustment with a travel is held within it, the focus about the compensator's
/// <see cref="FocusCompensator.Center"/>: a step that would cross a limit stops at it, and the
/// result says so (<see cref="Compensated.AtLimit"/>).</para>
///
/// <para>A criterion with no vector is solved instead by Brent's method on each adjustment in turn,
/// sweeping until the criterion stops falling. An MTF is refocused by its RMS wavefront
/// (<see cref="IFocusedCriterion.Focus"/>), which has one.</para>
///
/// <para>Every lens tried is built, so the lens's pickups follow each adjustment.</para>
/// </summary>
internal static class CompensatorSolver
{
    private const int MaxSteps = 10;

    public static Compensated Solve(FocusCompensator compensator, TrialLens lens, ICriterion criterion)
    {
        var adjustments = compensator.Adjustments;
        int n = adjustments.Count + 1;
        var basis = lens.Clone();
        double start = lens.Nominal.System.Surfaces[compensator.Surface].Thickness;
        double baseline = basis.System.Surfaces[compensator.Surface].Thickness;

        // The focus alone first.
        var alone = basis.Clone();
        (compensator with { Travel = null }).RefocusAlone(alone, criterion);
        var p = new double[n];
        p[0] = alone.System.Surfaces[compensator.Surface].Thickness - baseline;

        // Each parameter's limits: the focus as a thickness change from the baseline, the rest from zero.
        var lo = new double[n];
        var hi = new double[n];
        lo[0] = compensator.Travel is double t ? start + compensator.Center - t - baseline : double.NegativeInfinity;
        hi[0] = compensator.Travel is double t2 ? start + compensator.Center + t2 - baseline : double.PositiveInfinity;
        for (int k = 1; k < n; k++)
        {
            lo[k] = adjustments[k - 1].Travel is double a ? -a : double.NegativeInfinity;
            hi[k] = adjustments[k - 1].Travel is double b ? b : double.PositiveInfinity;
        }
        var step = new double[n];
        step[0] = 1e-3;
        for (int k = 1; k < n; k++) step[k] = adjustments[k - 1].Step;
        Clamp(p, lo, hi);

        TrialLens At(double[] q)
        {
            var trial = basis.Clone();
            trial.System.Surfaces[compensator.Surface].Thickness += q[0];
            for (int k = 1; k < n; k++) adjustments[k - 1].Apply(trial, q[k]);
            return trial;
        }
        double Score(double[] q)
        {
            double c = criterion.Evaluate(At(q).Build());
            return double.IsFinite(c) ? c : double.PositiveInfinity;
        }

        var rms = criterion as RmsWavefrontCriterion;
        double[]? Residual(double[] q) => rms?.Vector(At(q).Build());

        var start0 = rms == null ? null : Residual(p);
        if (start0 != null) LevenbergMarquardt(p, start0, lo, hi, step, Residual);
        else Coordinate(p, Score(p), lo, hi, step, Score);
        double best = Score(p);

        // Leave the lens as solved.
        lens.System.Surfaces[compensator.Surface].Thickness += p[0];
        for (int k = 1; k < n; k++) adjustments[k - 1].Apply(lens, p[k]);
        bool atLimit = Enumerable.Range(0, n).Any(k => p[k] <= lo[k] + 1e-12 * Math.Max(1.0, Math.Abs(lo[k]))
                                                    || p[k] >= hi[k] - 1e-12 * Math.Max(1.0, Math.Abs(hi[k])));
        double change = baseline + p[0] - start;
        return new Compensated(change, best, atLimit) { Adjustments = p.Skip(1).ToArray() };
    }

    /// <summary>The criterion the residual vector stands for: its length, the samples that do not arrive left out.</summary>
    private static double Length(double[] r) => Math.Sqrt(r.Where(v => !double.IsNaN(v)).Sum(v => v * v));

    private static void LevenbergMarquardt(double[] p, double[] r, double[] lo, double[] hi, double[] step,
                                           Func<double[], double[]?> residual)
    {
        int n = p.Length;
        double lambda = 1e-3, best = Length(r);
        for (int iteration = 0; iteration < MaxSteps; iteration++)
        {
            // Forward differences, a sample missing at either end counting as unchanged.
            var jacobian = new double[n][];
            for (int k = 0; k < n; k++)
            {
                var q = (double[])p.Clone();
                double h = q[k] + step[k] > hi[k] ? -step[k] : step[k];
                q[k] += h;
                var rk = residual(q);
                jacobian[k] = new double[r.Length];
                if (rk == null || rk.Length != r.Length) continue;
                for (int i = 0; i < r.Length; i++)
                    jacobian[k][i] = double.IsNaN(r[i]) || double.IsNaN(rk[i]) ? 0.0 : (rk[i] - r[i]) / h;
            }
            var jtj = new double[n, n];
            var jtr = new double[n];
            for (int a = 0; a < n; a++)
            {
                for (int i = 0; i < r.Length; i++)
                    if (!double.IsNaN(r[i])) jtr[a] += jacobian[a][i] * r[i];
                for (int b = 0; b < n; b++)
                    for (int i = 0; i < r.Length; i++) jtj[a, b] += jacobian[a][i] * jacobian[b][i];
            }

            // An adjustment whose effect is negligible beside the others' - an image tilt about y when
            // every field lies on the y axis - is held where it is this step, so as not to wander along
            // a valley it does not change.
            double strongest = Enumerable.Range(0, n).Max(a => jtj[a, a]);
            var frozen = Enumerable.Range(0, n).Select(a => jtj[a, a] < 1e-4 * strongest).ToArray();
            for (int a = 0; a < n; a++)
                if (frozen[a])
                {
                    for (int b = 0; b < n; b++) jtj[a, b] = jtj[b, a] = 0.0;
                    jtj[a, a] = 1.0;
                    jtr[a] = 0.0;
                }

            bool improved = false;
            for (int attempt = 0; attempt < 12 && !improved; attempt++, lambda *= 10.0)
            {
                var m = (double[,])jtj.Clone();
                for (int a = 0; a < n; a++) m[a, a] += lambda * Math.Max(jtj[a, a], 1e-30);
                var delta = SolveLinear(m, jtr.Select(v => -v).ToArray());
                if (delta == null) continue;
                // No step past a thousand derivative steps, a millimeter or a degree: an adjustment the
                // criterion barely feels must not run away along its small second-order slope.
                for (int a = 0; a < n; a++) delta[a] = Math.Clamp(delta[a], -1000.0 * step[a], 1000.0 * step[a]);
                var q = p.Zip(delta, (x, d) => x + d).ToArray();
                Clamp(q, lo, hi);
                var rq = residual(q);
                if (rq == null || rq.Length != r.Length) continue;
                double s = Length(rq);
                if (s < best)
                {
                    double gain = best - s;
                    Array.Copy(q, p, n);
                    r = rq;
                    best = s;
                    improved = true;
                    lambda = Math.Max(lambda / 100.0, 1e-9);
                    if (gain < 1e-6 * Math.Max(best, 1e-12)) return;
                }
            }
            if (!improved) break;
        }
    }

    /// <summary>Brent's method on each parameter in turn, each within a bracket of a hundred steps about where it stands, until a sweep gains nothing.</summary>
    private static double Coordinate(double[] p, double best, double[] lo, double[] hi, double[] step, Func<double[], double> score)
    {
        for (int sweep = 0; sweep < 8; sweep++)
        {
            double before = best;
            for (int k = 0; k < p.Length; k++)
            {
                var q = (double[])p.Clone();
                double a = Math.Max(lo[k], p[k] - 100.0 * step[k]), b = Math.Min(hi[k], p[k] + 100.0 * step[k]);
                var (x, f) = Brent.Minimize(v => { q[k] = v; return score(q); }, a, b, tolerance: step[k] * 1e-2);
                if (f < best) { p[k] = x; best = f; }
            }
            if (before - best < 1e-9 * Math.Max(best, 1e-12)) break;
        }
        return best;
    }

    private static void Clamp(double[] p, double[] lo, double[] hi)
    {
        for (int k = 0; k < p.Length; k++) p[k] = Math.Min(hi[k], Math.Max(lo[k], p[k]));
    }

    /// <summary>Gaussian elimination with partial pivoting; null when the system is singular.</summary>
    private static double[]? SolveLinear(double[,] a, double[] b)
    {
        int n = b.Length;
        var m = (double[,])a.Clone();
        var x = (double[])b.Clone();
        for (int c = 0; c < n; c++)
        {
            int pivot = c;
            for (int r = c + 1; r < n; r++) if (Math.Abs(m[r, c]) > Math.Abs(m[pivot, c])) pivot = r;
            if (Math.Abs(m[pivot, c]) < 1e-300) return null;
            if (pivot != c)
            {
                for (int k = 0; k < n; k++) (m[c, k], m[pivot, k]) = (m[pivot, k], m[c, k]);
                (x[c], x[pivot]) = (x[pivot], x[c]);
            }
            for (int r = c + 1; r < n; r++)
            {
                double f = m[r, c] / m[c, c];
                for (int k = c; k < n; k++) m[r, k] -= f * m[c, k];
                x[r] -= f * x[c];
            }
        }
        for (int c = n - 1; c >= 0; c--)
        {
            for (int k = c + 1; k < n; k++) x[c] -= m[c, k] * x[k];
            x[c] /= m[c, c];
        }
        return x;
    }
}
