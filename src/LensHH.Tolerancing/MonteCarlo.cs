using System.Globalization;
using System.Text;
using LensHH.Core.Enums;
using LensHH.Core.IO;

namespace LensHH.Tolerancing;

/// <summary>How a Monte Carlo is run.</summary>
public sealed record MonteCarloOptions
{
    public int Trials { get; init; } = 200;
    public int Seed { get; init; } = 1;

    /// <summary>How errors spread over their ranges when a tolerance names no distribution.</summary>
    public StatisticsModel Statistics { get; init; } = StatisticsModel.Default;

    /// <summary>
    /// Frequencies, cycles/mm, at which each trial's MTF is kept for the Monte Carlo MTF plots; empty
    /// for none. Every file field is kept, tangential and sagittal.
    /// </summary>
    public IReadOnlyList<double> MtfFrequencies { get; init; } = Array.Empty<double>();

    /// <summary>The FFT MTF's pupil grid.</summary>
    public int MtfGridSize { get; init; } = 64;

    /// <summary>A folder to save trials to as .lhlt lens files, with a CSV of every trial; null for none.</summary>
    public string? SaveDirectory { get; init; }

    /// <summary>How many trials to save, the first so many; the CSV lists them all.</summary>
    public int SaveTrials { get; init; } = 20;

    /// <summary>Trials run at once; 0 for <see cref="MonteCarlo.DefaultParallelism"/>.</summary>
    public int Parallelism { get; init; }
}

/// <summary>One Monte Carlo lens: its errors, the compensator's move, the criterion, and its MTF.</summary>
public sealed record TrialResult(int Index, IReadOnlyList<double> Errors, double Criterion, double Compensator, bool AtLimit)
{
    /// <summary>Tangential MTF at each kept field (outer) and frequency (inner); empty when none was asked for.</summary>
    public IReadOnlyList<double[]> MtfT { get; init; } = Array.Empty<double[]>();
    public IReadOnlyList<double[]> MtfS { get; init; } = Array.Empty<double[]>();

    /// <summary>Each of the compensator's adjustments beside the focus, as it was set, in its units; empty when it has none.</summary>
    public IReadOnlyList<double> Adjustments { get; init; } = Array.Empty<double>();

    /// <summary>The trial's lens file, when it was saved.</summary>
    public string? File { get; init; }
}

public sealed record MonteCarloResult(string Criterion, IReadOnlyList<ToleranceItem> Items,
                                      IReadOnlyList<TrialResult> Trials, double NominalCriterion,
                                      IReadOnlyList<double> Frequencies, IReadOnlyList<int> Fields)
{
    /// <summary>The criterion the lenses were judged by, for quoting its values.</summary>
    public ICriterion? CriterionInfo { get; init; }

    /// <summary>The lenses asked for. More than <see cref="Trials"/> holds when the run was stopped.</summary>
    public int Requested { get; init; }

    /// <summary>The run was stopped before every lens asked for was finished; <see cref="Trials"/> are the finished ones.</summary>
    public bool Stopped => Requested > Trials.Count;

    /// <summary>The compensator the lenses were refocused by; null for none.</summary>
    public FocusCompensator? Compensator { get; init; }

    /// <summary>
    /// How far the focus thickness moved from the design to refocus the nominal lens, mm. A trial's
    /// <see cref="TrialResult.Compensator"/> is its move from there; this plus that is its move from
    /// the design, the change its saved lens shows.
    /// </summary>
    public double NominalFocus { get; init; }

    /// <summary>The nominal lens's MTF, refocused, at each kept field and frequency.</summary>
    public IReadOnlyList<double[]> NominalMtfT { get; init; } = Array.Empty<double[]>();
    public IReadOnlyList<double[]> NominalMtfS { get; init; } = Array.Empty<double[]>();

    private double[] Criteria => Trials.Select(t => t.Criterion).ToArray();

    public int Untraceable => Trials.Count(t => !double.IsFinite(t.Criterion));

    public double Mean => Criteria.Where(double.IsFinite).DefaultIfEmpty(double.NaN).Average();

    /// <summary>The criterion that a fraction <paramref name="p"/> of the lenses do better than.</summary>
    public double Percentile(double p) => Stats.Percentile(Criteria, p);

    /// <summary>The fraction of lenses with the criterion at or below <paramref name="limit"/>.</summary>
    public double Yield(double limit) => Trials.Count(t => t.Criterion <= limit) / (double)Trials.Count;

    public int CompensatorAtLimit => Trials.Count(t => t.AtLimit);

    public (double Min, double Max) CompensatorRange
    {
        get
        {
            var moves = Trials.Select(t => t.Compensator).Where(double.IsFinite).ToArray();
            return moves.Length == 0 ? (0.0, 0.0) : (moves.Min(), moves.Max());
        }
    }

    /// <summary>
    /// The MTF at each frequency that a fraction <paramref name="p"/> of the lenses reach or exceed,
    /// for field <paramref name="fieldSlot"/> of <see cref="Fields"/>: p = 0.9 is the curve 90 % of
    /// the lenses built are above. An untraceable lens counts as an MTF of 0.
    /// </summary>
    public double[] MtfAtLeast(int fieldSlot, MtfOrientation orientation, double p)
    {
        var curve = new double[Frequencies.Count];
        for (int k = 0; k < curve.Length; k++)
        {
            var values = Trials.Select(t =>
            {
                var m = orientation == MtfOrientation.Tangential ? t.MtfT : t.MtfS;
                double v = m.Count > fieldSlot ? m[fieldSlot][k] : double.NaN;
                return double.IsFinite(v) ? v : 0.0;
            }).ToArray();
            curve[k] = Stats.Percentile(values, 1.0 - p);
        }
        return curve;
    }

    /// <summary>
    /// The fraction of lenses whose MTF at frequency index <paramref name="k"/> is at least each of
    /// <paramref name="levels"/>, for one field and orientation: the yield curve.
    /// </summary>
    public double[] MtfYield(int fieldSlot, MtfOrientation orientation, int k, IReadOnlyList<double> levels)
    {
        var values = Trials.Select(t =>
        {
            var m = orientation == MtfOrientation.Tangential ? t.MtfT : t.MtfS;
            double v = m.Count > fieldSlot ? m[fieldSlot][k] : double.NaN;
            return double.IsFinite(v) ? v : 0.0;
        }).ToArray();
        return levels.Select(l => values.Count(v => v >= l) / (double)values.Length).ToArray();
    }
}

public static class MonteCarlo
{
    /// <summary>
    /// Trials run at once unless asked otherwise: half the logical processors, about one per core.
    /// Tracing gains little from a core's second thread - 16 threads on a 10-core processor ran
    /// no more lenses a minute than 8 - and every lens in flight takes that much longer to finish,
    /// so with no limit the thread pool, adding threads to a loop of long items, put the first
    /// finished lens of 200 two minutes in.
    /// </summary>
    public static int DefaultParallelism => Math.Max(1, Environment.ProcessorCount / 2);

    public static MonteCarloResult Run(NominalLens lens, IReadOnlyList<ToleranceItem> items, ICriterion criterion,
                                       FocusCompensator? compensator, MonteCarloOptions options,
                                       IProgress<int>? progress = null, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(lens);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Trials < 1) throw new ArgumentException("a Monte Carlo needs at least one trial");
        ToleranceCheck.Validate(lens, items);

        var groups = Groups(items, options.Statistics);
        var fields = Enumerable.Range(0, lens.FileFieldCount).ToList();
        var freqs = options.MtfFrequencies;

        // The nominal, refocused: the reference the compensator moves from, and the MTF the bands sit under.
        var nominal = Sensitivity.Evaluate(lens, Array.Empty<(Tolerance, double)>(), criterion, compensator, out var nominalLens);
        double reference = double.IsFinite(nominal.Change) ? nominal.Change : 0.0;
        var nominalBuilt = nominalLens.Build();
        // Each field's pupil in each direction, measured once on the nominal lens: a trial's errors barely move it.
        var pupils = freqs.Count == 0 ? Array.Empty<IReadOnlyList<LensHH.Core.Analysis.FftMtfCalculator.PupilGeometry>>()
            : fields.AsParallel().AsOrdered().Select(f => Mtf.Pupils(nominalBuilt, f)).ToArray();
        var (nominalT, nominalS) = MtfOf(nominalBuilt, fields, freqs, options.MtfGridSize, pupils);

        string? dir = options.SaveDirectory;
        if (dir != null) Directory.CreateDirectory(dir);

        // A stop (cancel) ends the run within one evaluation of the criterion and keeps the lenses
        // finished by then: the result is those, a smaller Monte Carlo. Each lens's errors depend
        // on its number alone, so they are the lenses a full run would have had.
        var trials = new TrialResult?[options.Trials];
        int done = 0;
        try
        {
            Parallel.For(0, options.Trials, new ParallelOptions
            {
                CancellationToken = cancel,
                MaxDegreeOfParallelism = options.Parallelism > 0 ? options.Parallelism : DefaultParallelism,
            }, k =>
            {
                try { trials[k] = Trial(k); }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested) { return; }
                progress?.Report(Interlocked.Increment(ref done));
            });
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        var finished = trials.OfType<TrialResult>().ToList();
        if (finished.Count == 0) throw new OperationCanceledException(cancel);

        TrialResult Trial(int k)
        {
            var x = Draw(groups, items, options.Statistics, TrialRandom(options.Seed, k));
            var r = Sensitivity.Evaluate(lens, items.Select((item, i) => (item.Tolerance, x[i])), criterion, compensator, out var trial, cancel);
            var trialResult = new TrialResult(k + 1, x, r.Criterion,
                                              double.IsFinite(r.Criterion) ? r.Change - reference : double.NaN, r.AtLimit)
            {
                Adjustments = r.Adjustments ?? Array.Empty<double>(),
            };
            if (double.IsFinite(r.Criterion))
            {
                cancel.ThrowIfCancellationRequested();
                var built = trial.Build();
                var (t, s) = MtfOf(built, fields, freqs, options.MtfGridSize, pupils, cancel);
                trialResult = trialResult with { MtfT = t, MtfS = s };
                if (dir != null && k < options.SaveTrials)
                    trialResult = trialResult with { File = Save(built.System, dir, k + 1, items, x, r, compensator, lens.Views) };
            }
            return trialResult;
        }

        var result = new MonteCarloResult(criterion.Name, items, finished, nominal.Criterion, freqs, fields)
        {
            Requested = options.Trials,
            NominalMtfT = nominalT,
            NominalMtfS = nominalS,
            CriterionInfo = criterion,
            Compensator = compensator,
            NominalFocus = compensator != null && double.IsFinite(nominal.Change) ? nominal.Change : 0.0,
        };
        if (dir != null) File.WriteAllText(Path.Combine(dir, "trials.csv"), Csv(result, criterion));
        return result;
    }

    /// <summary>Each field's MTF in the worst direction the lens is judged in (<see cref="Mtf.Worst"/>).</summary>
    private static (double[][] T, double[][] S) MtfOf(Built lens, IReadOnlyList<int> fields, IReadOnlyList<double> freqs, int grid,
                                                      IReadOnlyList<IReadOnlyList<LensHH.Core.Analysis.FftMtfCalculator.PupilGeometry>> pupils,
                                                      CancellationToken cancel = default)
    {
        if (freqs.Count == 0) return (Array.Empty<double[]>(), Array.Empty<double[]>());
        var t = new double[fields.Count][];
        var s = new double[fields.Count][];
        for (int f = 0; f < fields.Count; f++) (t[f], s[f]) = Mtf.Worst(lens, fields[f], freqs, grid, pupils[f], cancel);
        return (t, s);
    }

    private static string Save(LensHH.Core.Models.OpticalSystem system, string dir, int trial, IReadOnlyList<ToleranceItem> items,
                               IReadOnlyList<double> x, Compensated r, FocusCompensator? compensator,
                               IReadOnlyList<FieldView> views)
    {
        system = system.DeepClone();
        system.Title = $"Monte Carlo trial {trial}" + (string.IsNullOrEmpty(system.Title) ? "" : $" of {system.Title}");
        var notes = new StringBuilder();
        notes.AppendLine($"Monte Carlo trial {trial}. Errors:");
        for (int i = 0; i < items.Count; i++)
            notes.AppendLine($"  {items[i].Tolerance.Code} {ToleranceSpec.Surfaces(items[i].Tolerance)} = {x[i].ToString("G6", CultureInfo.InvariantCulture)}");
        var c = CultureInfo.InvariantCulture;
        if (compensator != null)
        {
            notes.AppendLine($"Refocused by surface {compensator.Surface}'s thickness, changed by {r.Change.ToString("G6", c)}.");
            for (int k = 0; k < compensator.Adjustments.Count; k++)
                notes.AppendLine($"  {ToleranceSpec.Format(compensator.Adjustments[k] with { Travel = null })} = {(r.Adjustments?[k] ?? 0.0).ToString("G6", c)}");
        }
        notes.AppendLine($"Criterion {r.Criterion.ToString("G6", c)}, over the fields at {string.Join(", ", views.Select(v => v.Name))}.");
        system.Notes = notes.ToString();
        string path = Path.Combine(dir, $"MC_{trial:D4}.lhlt");
        LhltWriter.Write(system, path);
        return path;
    }

    /// <summary>Every trial's errors, compensator, criterion and saved file, one row each.</summary>
    public static string Csv(MonteCarloResult result, ICriterion criterion)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        // The focus as the saved lens has it: its change from the design thickness.
        sb.Append("trial,criterion,focus_change_mm,at_limit");
        var adjustments = result.Compensator?.Adjustments ?? Array.Empty<Adjustment>();
        foreach (var a in adjustments) sb.Append(',').Append(ToleranceSpec.Format(a with { Travel = null }));
        foreach (var item in result.Items) sb.Append(',').Append(item.Tolerance.Code).Append(' ').Append(ToleranceSpec.Surfaces(item.Tolerance));
        sb.AppendLine(",file");
        foreach (var t in result.Trials)
        {
            sb.Append(t.Index.ToString(c)).Append(',')
              .Append(criterion.Quoted(t.Criterion).ToString("G8", c)).Append(',')
              .Append((t.Compensator + result.NominalFocus).ToString("G8", c)).Append(',')
              .Append(t.AtLimit ? "1" : "0");
            for (int k = 0; k < adjustments.Count; k++)
                sb.Append(',').Append(k < t.Adjustments.Count ? t.Adjustments[k].ToString("G8", c) : "");
            foreach (double v in t.Errors) sb.Append(',').Append(v.ToString("G8", c));
            sb.Append(',').AppendLine(t.File == null ? "" : Path.GetFileName(t.File));
        }
        return sb.ToString();
    }

    /// <summary>
    /// The tolerances drawn together: each x and y pair of one decentre or tilt when the statistics
    /// draw them in polar form and neither names its own distribution, and every other alone.
    /// </summary>
    public static List<int[]> Groups(IReadOnlyList<ToleranceItem> items, StatisticsModel statistics)
    {
        var pairs = new List<(int X, int Y)>();
        if (statistics.Polar != null)
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Tolerance is not IAxisTolerance { Axis: Axis.X } x || items[i].Distribution != null) continue;
                var partner = x.WithAxis(Axis.Y);
                for (int j = 0; j < items.Count; j++)
                    if (items[j].Tolerance.Equals(partner) && items[j].Distribution == null) { pairs.Add((i, j)); break; }
            }
        var paired = new HashSet<int>(pairs.SelectMany(p => new[] { p.X, p.Y }));
        var groups = new List<int[]>();
        for (int i = 0; i < items.Count; i++)
        {
            if (!paired.Contains(i)) { groups.Add(new[] { i }); continue; }
            foreach (var p in pairs.Where(p => p.X == i))
            {
                if (Math.Abs(items[p.X].HalfWidth - items[p.Y].HalfWidth) > 1e-12 * items[p.X].HalfWidth)
                    throw new ArgumentException($"{items[p.X]} and {items[p.Y]} are drawn as one error and need one tolerance");
                groups.Add(new[] { p.X, p.Y });
            }
        }
        return groups;
    }

    /// <summary>One lens's errors, in the units of each tolerance.</summary>
    public static double[] Draw(IReadOnlyList<int[]> groups, IReadOnlyList<ToleranceItem> items,
                                StatisticsModel statistics, Random random)
    {
        var x = new double[items.Count];
        foreach (var g in groups)
        {
            if (g.Length == 1)
            {
                var item = items[g[0]];
                x[g[0]] = item.Center + item.HalfWidth * (item.Distribution ?? statistics.Axis).Sample(random);
                continue;
            }
            double rho = items[g[0]].HalfWidth * statistics.Polar!.SampleRadius(random);
            double theta = 2.0 * Math.PI * random.NextDouble();
            x[g[0]] = items[g[0]].Center + rho * Math.Cos(theta);
            x[g[1]] = items[g[1]].Center + rho * Math.Sin(theta);
        }
        return x;
    }

    /// <summary>The trial's own generator: the draws are the same however the trials are threaded.</summary>
    private static Random TrialRandom(int seed, int trial) => SplitMix.ForTrial(seed, trial);
}

/// <summary>Checks a tolerance set against a lens before a run, so a mistake is reported, not traced as a failed lens.</summary>
public static class ToleranceCheck
{
    public static void Validate(NominalLens lens, IReadOnlyList<ToleranceItem> items)
    {
        foreach (var item in items)
        {
            foreach (var (surface, parameter) in item.Tolerance.Changes)
                if (PickupOf(lens, surface, parameter) is int source)
                    throw new ArgumentException(
                        $"{item}: surface {surface}'s {Name(parameter)} is a pickup of surface {source}'s, which would undo the error; tolerance surface {source} instead");
            var trial = lens.Perturb();
            try { item.Tolerance.Apply(trial, item.Max != 0 ? item.Max : item.Min); }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException)
            {
                throw new ArgumentException($"{item}: {e.Message}", e);
            }
        }
    }

    /// <summary>The surface a pickup sets this value of the surface from, or null when nothing picks it up.</summary>
    public static int? PickupOf(NominalLens lens, int surface, PickupParameter parameter) =>
        lens.System.Pickups.FirstOrDefault(p => p.TargetSurfaceIndex == surface && p.Parameter == parameter)?.SourceSurfaceIndex;

    public static string Name(PickupParameter parameter) => parameter switch
    {
        PickupParameter.Radius => "radius",
        PickupParameter.Thickness => "thickness",
        PickupParameter.Conic => "conic",
        _ => parameter.ToString(),
    };
}

internal static class Stats
{
    /// <summary>The value a fraction <paramref name="p"/> of <paramref name="values"/> lie at or below, interpolated.</summary>
    public static double Percentile(IReadOnlyList<double> values, double p)
    {
        if (values.Count == 0) return double.NaN;
        var sorted = values.OrderBy(v => v).ToArray();
        double at = Math.Clamp(p, 0.0, 1.0) * (sorted.Length - 1);
        int lo = (int)Math.Floor(at), hi = Math.Min(lo + 1, sorted.Length - 1);
        return double.IsInfinity(sorted[hi]) ? sorted[hi] : sorted[lo] + (at - lo) * (sorted[hi] - sorted[lo]);
    }
}
