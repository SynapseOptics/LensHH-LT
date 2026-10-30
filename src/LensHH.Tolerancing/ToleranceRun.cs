using System.Globalization;
using System.Text;
using LensHH.Core.Enums;
using LensHH.Core.Glass;
using LensHH.Core.Models;

namespace LensHH.Tolerancing;

/// <summary>A tolerance run's settings, as the GUI, the CLI and MCP all take them.</summary>
public sealed record ToleranceSettings
{
    /// <summary>"rms" (RMS wavefront) or "mtf:field:frequency:t|s".</summary>
    public string Criterion { get; init; } = "rms";

    /// <summary>Every wavelength by its weight, or the primary alone.</summary>
    public bool Polychromatic { get; init; } = true;

    /// <summary>Refocus each lens by the back focus.</summary>
    public bool Compensate { get; init; } = true;

    /// <summary>The focus's travel either way from where it focuses the nominal lens; null for none.</summary>
    public double? Travel { get; init; }

    /// <summary>The lens's fields, or each with its mirror image at −y as well.</summary>
    public FieldSymmetry Fields { get; init; } = FieldSymmetry.Y;

    /// <summary>Wavelength the fringes of TFRN, TIRR and TRSI are counted at, µm.</summary>
    public double TestWavelengthUm { get; init; } = PowerFringes.HeNe;

    public int Trials { get; init; } = 200;
    public int Seed { get; init; } = 1;

    /// <summary>"uniform", "normal", "normal:k" or "endpoint", for a tolerance that names none; "+disc" or "+ring" draws x and y pairs as one error.</summary>
    public string Statistics { get; init; } = "uniform";

    /// <summary>Highest frequency, cycles/mm, of the Monte Carlo MTF; 0 for none.</summary>
    public double MtfMaxFrequency { get; init; }

    /// <summary>Frequency step of the Monte Carlo MTF, cycles/mm.</summary>
    public double MtfStep { get; init; } = 5.0;

    /// <summary>A folder for the trial lenses and the CSV; null for none.</summary>
    public string? SaveDirectory { get; init; }

    public int SaveTrials { get; init; } = 20;
}

/// <summary>
/// The one way in to tolerancing for every front end: read a tolerance set, prepare the lens, run,
/// and report as text.
/// </summary>
public static class ToleranceRun
{
    public static List<ToleranceItem> Parse(string text, ToleranceSettings settings) =>
        ToleranceSpec.ParseSet(text, settings.TestWavelengthUm);

    /// <summary>A starting tolerance set for the lens at a shop grade, as text in the .tol format.</summary>
    public static string Defaults(OpticalSystem system, GlassCatalogManager glass, ShopGrade grade, ToleranceSettings settings)
    {
        var lens = new NominalLens(system, glass, FieldSymmetry.File);
        var items = DefaultTolerances.Create(lens, grade, settings.TestWavelengthUm);
        return $"# {grade} tolerances for {(string.IsNullOrEmpty(system.Title) ? "the lens" : system.Title)}\n"
               + "# CODE surfaces ±t | min max [uniform|normal[:k]|endpoint] [pivot:d]\n"
               + ToleranceSpec.FormatSet(items);
    }

    /// <summary>
    /// The lens, the criterion and the compensator a run uses. The compensator is the back focus
    /// (<see cref="ToleranceSettings.Compensate"/>, <see cref="ToleranceSettings.Travel"/>) unless the
    /// tolerance set names its own (<see cref="ToleranceSpec.ParseCompensators"/>): then the thickness
    /// before the image if it is one of them, or else the first COMP, is the focus, and the rest are
    /// solved with it. Without a COMP the focus is the back focus.
    /// </summary>
    public static (NominalLens Lens, ICriterion Criterion, FocusCompensator? Compensator) Prepare(
        OpticalSystem system, GlassCatalogManager glass, ToleranceSettings settings, string tolerances = "")
    {
        var lens = new NominalLens(system, glass, settings.Fields);
        var rms = new RmsWavefrontCriterion { Polychromatic = settings.Polychromatic };
        ICriterion criterion = settings.Criterion.Trim().ToLowerInvariant() switch
        {
            "rms" or "wavefront" => rms,
            var c when c.StartsWith("mtf") => MtfCriterion.Parse(c, rms),
            _ => throw new ArgumentException($"no criterion {settings.Criterion}: rms, or mtf:field:frequency:t|s"),
        };
        if (criterion is MtfCriterion m && (m.Field < 1 || m.Field > lens.FileFieldCount))
            throw new ArgumentException($"{settings.Criterion}: the lens has fields 1 to {lens.FileFieldCount}");
        FocusCompensator? compensator = null;
        if (settings.Compensate)
        {
            var named = ToleranceSpec.ParseCompensators(tolerances);
            var spaces = named.OfType<SpaceAdjustment>().ToList();
            var focus = spaces.FirstOrDefault(s => s.Surface == lens.BackFocusSurface) ?? spaces.FirstOrDefault();
            compensator = focus != null
                ? new FocusCompensator(focus.Surface, focus.Travel)
                : FocusCompensator.BackFocus(lens, settings.Travel);
            compensator = compensator with { Adjustments = named.Where(a => !ReferenceEquals(a, focus)).ToList() };
            Check(lens, compensator);
            if (compensator.Travel != null) compensator = compensator.Centred(lens, criterion);
        }
        return (lens, criterion, compensator);
    }

    /// <summary>Refuses a compensator that is not on the lens, or that a pickup would set back.</summary>
    private static void Check(NominalLens lens, FocusCompensator compensator)
    {
        var spaces = new[] { compensator.Surface }
            .Concat(compensator.Adjustments.OfType<SpaceAdjustment>().Select(a => a.Surface)).ToList();
        foreach (int s in spaces)
        {
            if (s < 1 || s > lens.BackFocusSurface)
                throw new ArgumentException($"COMP {s}: the compensator is a thickness between surface 1 and surface {lens.BackFocusSurface}");
            if (ToleranceCheck.PickupOf(lens, s, PickupParameter.Thickness) is int source)
                throw new ArgumentException($"COMP {s}: surface {s}'s thickness is a pickup of surface {source}'s, which would set it back; compensate by surface {source}");
        }
        if (spaces.Count != spaces.Distinct().Count()) throw new ArgumentException("a thickness is named as a compensator twice");
        foreach (var a in compensator.Adjustments.Where(a => a is not SpaceAdjustment))
        {
            try { a.Apply(lens.Perturb(), 0.0); }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                throw new ArgumentException($"{ToleranceSpec.Format(a)}: {e.Message}", e);
            }
        }
    }

    public static StatisticsModel ParseStatistics(string text)
    {
        var parts = text.ToLowerInvariant().Split('+');
        var axis = ToleranceSpec.ParseDistribution(parts[0]);
        RadialDistribution? polar = parts.Length < 2 ? null : parts[1] switch
        {
            "disc" => RadialDistribution.Disc,
            "ring" => RadialDistribution.Ring,
            "rayleigh" => RadialDistribution.Rayleigh(),
            _ => throw new ArgumentException($"{text}: pairs are drawn as disc, ring or rayleigh"),
        };
        return new StatisticsModel(axis, polar);
    }

    public static double[] Frequencies(ToleranceSettings settings)
    {
        if (!(settings.MtfMaxFrequency > 0)) return Array.Empty<double>();
        double step = settings.MtfStep > 0 ? settings.MtfStep : 5.0;
        int n = (int)Math.Floor(settings.MtfMaxFrequency / step + 1e-9);
        return Enumerable.Range(0, n + 1).Select(k => k * step).ToArray();
    }

    public static SensitivityTable Sensitivity(OpticalSystem system, GlassCatalogManager glass, string tolerances,
                                               ToleranceSettings settings, IProgress<int>? progress = null,
                                               CancellationToken cancel = default)
    {
        var (lens, criterion, compensator) = Prepare(system, glass, settings, tolerances);
        return Tolerancing.Sensitivity.Run(lens, Parse(tolerances, settings), criterion, compensator, progress, cancel);
    }

    public static MonteCarloResult MonteCarlo(OpticalSystem system, GlassCatalogManager glass, string tolerances,
                                              ToleranceSettings settings, IProgress<int>? progress = null,
                                              CancellationToken cancel = default)
    {
        var (lens, criterion, compensator) = Prepare(system, glass, settings, tolerances);
        var options = new MonteCarloOptions
        {
            Trials = settings.Trials,
            Seed = settings.Seed,
            Statistics = ParseStatistics(settings.Statistics),
            MtfFrequencies = Frequencies(settings),
            SaveDirectory = settings.SaveDirectory,
            SaveTrials = settings.SaveTrials,
        };
        var result = Tolerancing.MonteCarlo.Run(lens, Parse(tolerances, settings), criterion, compensator, options, progress, cancel);
        if (settings.SaveDirectory != null && options.MtfFrequencies.Count > 0)
            for (int f = 0; f < result.Fields.Count; f++)
                File.WriteAllText(Path.Combine(settings.SaveDirectory, $"mtf_field{f + 1}.svg"), MonteCarloPlot.Svg(result, f));
        return result;
    }

    /// <summary>The compensator as a list: the focus thickness, its travel, and each adjustment.</summary>
    public static string Compensators(FocusCompensator c)
    {
        var inv = CultureInfo.InvariantCulture;
        var parts = new List<string> { $"surface {c.Surface}'s thickness (focus{(c.Travel is double t ? $", ±{t.ToString("G6", inv)} mm" : "")})" };
        parts.AddRange(c.Adjustments.Select(a => ToleranceSpec.Format(a with { Travel = null }) +
                                                (a.Travel is double at ? $" (±{at.ToString("G6", inv)} {a.Units})" : "")));
        return string.Join(", ", parts);
    }

    private static string G(double v) => double.IsFinite(v) ? v.ToString("G5", CultureInfo.InvariantCulture) : "untraceable";

    /// <summary>The sensitivity table, worst first, with the estimate it gives.</summary>
    public static string Report(SensitivityTable table, int show = 0)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Sensitivity: {table.Criterion}");
        sb.AppendLine($"  Nominal {G(table.Nominal)}; refocused {G(table.NominalCompensated)}" +
                      (table.Compensator != null ? $" (focus {table.NominalCompensator:+0.0000;-0.0000} mm)" : ""));
        if (table.Compensator != null) sb.AppendLine($"  Compensators: {Compensators(table.Compensator)}");
        sb.AppendLine();
        sb.AppendLine($"  {"Tolerance",-14} {"Min",10} {"Max",10} {"At min",11} {"At max",11} {"Worst +",10}" +
                      (table.Compensator != null ? $" {"Focus min",10} {"Focus max",10}" : ""));
        var rows = table.WorstOffenders.ToList();
        if (show > 0) rows = rows.Take(show).ToList();
        foreach (var r in rows)
        {
            string name = $"{r.Item.Tolerance.Code} {ToleranceSpec.Surfaces(r.Item.Tolerance)}";
            sb.Append($"  {name,-14} {r.Item.Min,10:G4} {r.Item.Max,10:G4} {G(r.MinCriterion),11} {G(r.MaxCriterion),11} {G(r.WorstIncrease),10}");
            if (table.Compensator != null) sb.Append($" {r.MinCompensator,10:F4} {r.MaxCompensator,10:F4}");
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine($"  Root sum square of the worst increases: {G(table.RssIncrease)}");
        sb.AppendLine($"  Estimated as built: {G(table.Estimated)}");
        return sb.ToString();
    }

    /// <summary>The Monte Carlo summary: the criterion's spread, the focus it took, and the MTF most lenses reach.</summary>
    public static string Report(MonteCarloResult r)
    {
        var c = r.CriterionInfo;
        Func<double, double> q = v => c == null ? v : c.Quoted(v);
        bool floor = c?.IsFloor ?? false;
        var sb = new StringBuilder();
        sb.AppendLine($"Monte Carlo: {r.Trials.Count} lenses, {r.Criterion}");
        sb.AppendLine($"  Nominal {G(q(r.NominalCriterion))}; mean {G(q(r.Mean))}");
        foreach (double p in new[] { 0.5, 0.8, 0.9, 0.98 })
            sb.AppendLine($"  {p * 100,3:F0} % of lenses {(floor ? "at least" : "at most")} {G(q(r.Percentile(p)))}");
        if (r.Untraceable > 0) sb.AppendLine($"  {r.Untraceable} could not be traced");
        var (lo, hi) = r.CompensatorRange;
        if (r.Compensator != null) sb.AppendLine($"  Compensators: {Compensators(r.Compensator)}");
        if (r.Trials.Any(t => double.IsFinite(t.Compensator) && t.Compensator != 0))
            sb.AppendLine($"  Focus (surface {r.Compensator?.Surface}) moved {lo:+0.0000;-0.0000} to {hi:+0.0000;-0.0000} mm" +
                          (r.CompensatorAtLimit > 0 ? $"; {r.CompensatorAtLimit} at the end of a travel" : ""));
        var adjustments = r.Compensator?.Adjustments ?? Array.Empty<Adjustment>();
        for (int k = 0; k < adjustments.Count; k++)
        {
            var moves = r.Trials.Where(t => double.IsFinite(t.Criterion) && t.Adjustments.Count > k).Select(t => t.Adjustments[k]).ToList();
            if (moves.Count > 0)
                sb.AppendLine($"  {ToleranceSpec.Format(adjustments[k] with { Travel = null })} moved {moves.Min():+0.0000;-0.0000} to {moves.Max():+0.0000;-0.0000} {adjustments[k].Units}");
        }

        if (r.Frequencies.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  MTF (polychromatic): nominal / reached by 50 % / 90 % of lenses");
            var picks = r.Frequencies.Select((f, k) => (f, k)).Where(x => x.f > 0)
                         .Where((x, i) => r.Frequencies.Count <= 7 || i % Math.Max(1, (r.Frequencies.Count - 1) / 6) == 0).ToList();
            sb.Append($"  {"Field",-9}");
            foreach (var (f, _) in picks) sb.Append($" {f,16:G4}");
            sb.AppendLine("  cy/mm");
            for (int s = 0; s < r.Fields.Count; s++)
                foreach (var o in new[] { MtfOrientation.Tangential, MtfOrientation.Sagittal })
                {
                    var nominal = o == MtfOrientation.Tangential ? r.NominalMtfT[s] : r.NominalMtfS[s];
                    var p50 = r.MtfAtLeast(s, o, 0.5);
                    var p90 = r.MtfAtLeast(s, o, 0.9);
                    sb.Append($"  {r.Fields[s] + 1} {(o == MtfOrientation.Tangential ? "T" : "S"),-7}");
                    foreach (var (_, k) in picks) sb.Append($" {nominal[k],4:F2}/{p50[k],4:F2}/{p90[k],4:F2}");
                    sb.AppendLine();
                }
        }
        var saved = r.Trials.Where(t => t.File != null).ToList();
        if (saved.Count > 0)
            sb.AppendLine().AppendLine($"  Saved {saved.Count} trial lenses and trials.csv in {Path.GetDirectoryName(saved[0].File)}");
        return sb.ToString();
    }
}
