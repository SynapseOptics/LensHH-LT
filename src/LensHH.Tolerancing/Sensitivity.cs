namespace LensHH.Tolerancing;

/// <summary>One tolerance at each end of its range: the criterion, and the compensator's move from nominal.</summary>
public sealed record SensitivityRow(ToleranceItem Item,
                                    double MinCriterion, double MinCompensator,
                                    double MaxCriterion, double MaxCompensator,
                                    double Reference)
{
    public double MinChange => MinCriterion - Reference;
    public double MaxChange => MaxCriterion - Reference;

    /// <summary>The larger increase of the two ends, or 0 when both improve the lens.</summary>
    public double WorstIncrease => Math.Max(0.0, Math.Max(MinChange, MaxChange));
}

/// <summary>
/// A sensitivity run: the nominal criterion as the file has it and refocused, and each tolerance's
/// two ends. <see cref="Estimated"/> is the refocused nominal plus the root sum square of the worst
/// increases, the usual quick estimate of the lens as built.
/// </summary>
public sealed record SensitivityTable(string Criterion, double Nominal, double NominalCompensated,
                                      double NominalCompensator, FocusCompensator? Compensator,
                                      IReadOnlyList<SensitivityRow> Rows)
{
    /// <summary>The tolerances asked for. More than <see cref="Rows"/> holds when the run was stopped.</summary>
    public int Requested { get; init; }

    /// <summary>The run was stopped before every tolerance was finished; <see cref="Rows"/> are the finished ones.</summary>
    public bool Stopped => Requested > Rows.Count;

    public double RssIncrease => Math.Sqrt(Rows.Sum(r => r.WorstIncrease * r.WorstIncrease));

    public double Estimated => NominalCompensated + RssIncrease;

    public IEnumerable<SensitivityRow> WorstOffenders => Rows.OrderByDescending(r => r.WorstIncrease);
}

public static class Sensitivity
{
    public static SensitivityTable Run(NominalLens lens, IReadOnlyList<ToleranceItem> items,
                                       ICriterion criterion, FocusCompensator? compensator,
                                       IProgress<int>? progress = null, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(lens);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(criterion);
        ToleranceCheck.Validate(lens, items);

        double nominal = criterion.Evaluate(lens.Perturb().Build());
        var reference = Evaluate(lens, Array.Empty<(Tolerance, double)>(), criterion, compensator, out _, cancel);
        cancel.ThrowIfCancellationRequested();
        // A stop (cancel) ends the run within one evaluation of the criterion and keeps the
        // tolerances whose two ends were both finished.
        var ends = new Compensated?[items.Count * 2];
        int done = 0;
        try
        {
            Parallel.For(0, items.Count * 2, new ParallelOptions { CancellationToken = cancel }, k =>
            {
                var item = items[k / 2];
                try { ends[k] = Evaluate(lens, new[] { (item.Tolerance, k % 2 == 0 ? item.Min : item.Max) }, criterion, compensator, out _, cancel); }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested) { return; }
                progress?.Report(Interlocked.Increment(ref done));
            });
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }

        var rows = items.Select((item, i) => (item, lo: ends[2 * i], hi: ends[2 * i + 1]))
            .Where(e => e.lo.HasValue && e.hi.HasValue)
            .Select(e => new SensitivityRow(e.item, e.lo!.Value.Criterion, e.lo.Value.Change - reference.Change,
                                            e.hi!.Value.Criterion, e.hi.Value.Change - reference.Change, reference.Criterion))
            .ToList();
        if (rows.Count == 0 && cancel.IsCancellationRequested) throw new OperationCanceledException(cancel);
        return new SensitivityTable(criterion.Name, nominal, reference.Criterion, reference.Change, compensator, rows)
        {
            Requested = items.Count,
        };
    }

    /// <summary>
    /// The lens with these errors, refocused by the compensator if there is one: the criterion and
    /// the compensator's change. A lens that cannot be traced has a criterion of +∞.
    /// </summary>
    public static Compensated Evaluate(NominalLens lens, IEnumerable<(Tolerance Tolerance, double Amount)> applied,
                                       ICriterion criterion, FocusCompensator? compensator) =>
        Evaluate(lens, applied, criterion, compensator, out _);

    /// <summary>As <see cref="Evaluate(NominalLens, IEnumerable{ValueTuple{Tolerance, double}}, ICriterion, FocusCompensator?)"/>, giving the trial lens as left.</summary>
    public static Compensated Evaluate(NominalLens lens, IEnumerable<(Tolerance Tolerance, double Amount)> applied,
                                       ICriterion criterion, FocusCompensator? compensator, out TrialLens trial,
                                       CancellationToken cancel = default)
    {
        trial = lens.Perturb();
        try
        {
            foreach (var (tolerance, amount) in applied) tolerance.Apply(trial, amount);
            return compensator != null
                ? compensator.Solve(trial, criterion, cancel)
                : new Compensated(0.0, criterion.Evaluate(trial.Build()));
        }
        catch (Exception e) when (e is InvalidOperationException or ArithmeticException or ArgumentException)
        {
            return new Compensated(double.NaN, double.PositiveInfinity);
        }
    }
}
