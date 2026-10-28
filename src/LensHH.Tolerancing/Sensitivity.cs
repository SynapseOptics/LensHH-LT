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
        var reference = Evaluate(lens, Array.Empty<(Tolerance, double)>(), criterion, compensator);
        var ends = new Compensated[items.Count * 2];
        int done = 0;
        Parallel.For(0, items.Count * 2, new ParallelOptions { CancellationToken = cancel }, k =>
        {
            var item = items[k / 2];
            ends[k] = Evaluate(lens, new[] { (item.Tolerance, k % 2 == 0 ? item.Min : item.Max) }, criterion, compensator);
            progress?.Report(Interlocked.Increment(ref done));
        });

        var rows = items.Select((item, i) =>
        {
            var (lo, hi) = (ends[2 * i], ends[2 * i + 1]);
            return new SensitivityRow(item, lo.Criterion, lo.Change - reference.Change,
                                      hi.Criterion, hi.Change - reference.Change, reference.Criterion);
        }).ToList();
        return new SensitivityTable(criterion.Name, nominal, reference.Criterion, reference.Change, compensator, rows);
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
                                       ICriterion criterion, FocusCompensator? compensator, out TrialLens trial)
    {
        trial = lens.Perturb();
        try
        {
            foreach (var (tolerance, amount) in applied) tolerance.Apply(trial, amount);
            return compensator != null
                ? compensator.Solve(trial, criterion)
                : new Compensated(0.0, criterion.Evaluate(trial.Build()));
        }
        catch (Exception e) when (e is InvalidOperationException or ArithmeticException or ArgumentException)
        {
            return new Compensated(double.NaN, double.PositiveInfinity);
        }
    }
}
