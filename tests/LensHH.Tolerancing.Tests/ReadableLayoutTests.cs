using LensHH.Core.Analysis;
using LensHH.Core.Enums;
using LensHH.Core.IO;
using LensHH.Core.Models;
using Xunit;

namespace LensHH.Tolerancing.Tests;

// The trial lens as it is saved - each surface with its own thickness, each move a pair of breaks
// whose second picks up the first - against the lens the tolerancing traced; and the lens's own
// pickups, kept through every error and every compensator move.
public class ReadableLayoutTests
{
    private static NominalLens Triplet(Action<OpticalSystem>? edit = null)
    {
        var system = LhltReader.Read(Lenses.CookeTriplet).System;
        edit?.Invoke(system);
        return new NominalLens(system, Lenses.Glass());
    }

    private static readonly RmsWavefrontCriterion Rms = new();

    /// <summary>The trial with every error drawn, as a Monte Carlo makes one.</summary>
    private static TrialLens Draw(NominalLens lens, IReadOnlyList<ToleranceItem> items, int seed)
    {
        var groups = MonteCarlo.Groups(items, StatisticsModel.Default);
        var x = MonteCarlo.Draw(groups, items, StatisticsModel.Default, new Random(seed));
        var trial = lens.Perturb();
        for (int i = 0; i < items.Count; i++) items[i].Tolerance.Apply(trial, x[i]);
        return trial;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void TheSavedLensTracesAsTheTracedOne(int seed)
    {
        var lens = Triplet();
        // The commercial set - wedges on every surface, every element decentred and tilted - with a
        // decentred conic, an element tilted about a pivot behind it, and the whole lens as a group
        // around its elements, ten times the shop's errors so the moves are not small.
        var items = DefaultTolerances.Create(lens, ShopGrade.LowCost).ToList();
        items.AddRange(ToleranceSpec.ParseSet("""
            TSDX 4 0.05
            TETY 3-4 0.2 pivot:3.5
            TEDY 1-6 0.1
            TETX 1-6 0.1
            """));
        var trial = Draw(lens, items, seed);

        // Built the other way, each surface between breaks of its own, every real ray lands in the
        // same place: the two put each surface where its moves take it.
        var other = trial.BuildSurfaceBySurface().System;
        var readable = trial.Readable();
        foreach (var (x, y) in new[] { (0.0, 0.0), (0.0, 3.0), (2.0, -2.5), (-3.0, 1.0) })
            for (int f = 0; f < lens.System.Fields.Count; f++)
            {
                var a = Land(readable, lens, f, x, y);
                var b = Land(other, lens, f, x, y);
                Assert.Equal(a.X, b.X, 9);
                Assert.Equal(a.Y, b.Y, 9);
            }

        // The RMS wavefront differs only in its reference sphere: the paraxial trace that places the
        // exit pupil reads no break, and the surface-by-surface lens moves thicknesses along the
        // axis by z(1 − cos θ) where the readable one keeps them.
        double traced = Rms.Evaluate(trial.Build());
        Assert.True(double.IsFinite(traced));
        Assert.Equal(traced, Rms.Evaluate(trial.BuildSurfaceBySurface()), traced * 1e-5);

        // Through a file and back, pickups and all.
        string path = Path.Combine(Path.GetTempPath(), $"lenshh-readable-{Guid.NewGuid():N}.lhlt");
        try
        {
            LhltWriter.Write(readable, path);
            var reread = LhltReader.Read(path).System;
            PickupSolver.Solve(reread);
            Assert.Equal(traced, Rms.Evaluate(Built.Of(reread, lens.Glass)), 9);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Where a real ray from field <paramref name="field"/> through pupil point (x, y) mm lands on the image.</summary>
    private static (double X, double Y) Land(OpticalSystem system, NominalLens lens, int field, double x, double y)
    {
        var n = lens.Glass.BuildRefractiveIndexArray(system, system.Wavelengths[system.PrimaryWavelengthIndex].Value);
        double u = Math.Tan(system.Fields[field].Y * Math.PI / 180.0);
        double m = u / Math.Sqrt(1 + u * u), l = 1 / Math.Sqrt(1 + u * u);
        var t = new LensHH.Core.RayTrace.RayTracer(system, n, ignoreApertures: true).Trace(x, y, 0, m, l, startSurface: 1);
        Assert.True(t.Success);
        return (t.FinalRay.X, t.FinalRay.Y);
    }

    [Fact]
    public void EverySurfaceKeepsItsThickness()
    {
        var lens = Triplet();
        var trial = Draw(lens, DefaultTolerances.Create(lens), 7);
        var readable = trial.Readable();

        // The lens's own surfaces, in order, are the rows that are neither breaks nor the dummies
        // the layout adds; each has the thickness the trial lens has.
        var own = readable.Surfaces.Where(s => s.Type != SurfaceType.CoordinateBreak
                                               && !(s.Comment ?? "").StartsWith("Back to", StringComparison.Ordinal)
                                               && !(s.Comment ?? "").Contains("along the axis")).ToList();
        Assert.Equal(trial.System.Surfaces.Count, own.Count);
        for (int i = 0; i < own.Count; i++)
        {
            Assert.Equal(trial.System.Surfaces[i].Thickness, own[i].Thickness);
            Assert.Equal(trial.System.Surfaces[i].Radius, own[i].Radius);
            Assert.Equal(trial.System.Surfaces[i].Material, own[i].Material);
        }
    }

    [Fact]
    public void TheBreakBackPicksUpTheBreakIn()
    {
        var lens = Triplet();
        var trial = lens.Perturb();
        new ElementDecentre(3, 4, Axis.Y).Apply(trial, 0.05);
        new ElementTilt(3, 4, Axis.X).Apply(trial, 0.1);
        var readable = trial.Readable();

        var breaks = readable.Surfaces.Where(s => s.Type == SurfaceType.CoordinateBreak).ToList();
        Assert.Equal(2, breaks.Count);
        var (into, back) = (breaks[0], breaks[1]);
        Assert.Equal(0, into.GetSetting(1));
        Assert.Equal(1, back.GetSetting(1));
        Assert.Equal(0.05, into.Parameters[1], 12);
        Assert.Equal(0.1, into.Parameters[2], 12);

        // Edit the break in, solve the pickups, and the break back follows it.
        into.Parameters[2] = 0.3;
        into.Parameters[0] = -0.02;
        PickupSolver.Solve(readable);
        Assert.Equal(-0.3, back.Parameters[2], 15);
        Assert.Equal(0.02, back.Parameters[0], 15);
    }

    [Fact]
    public void AnErrorOnAPickupsSourceCarriesToItsTarget()
    {
        // Surface 4's radius made a pickup of surface 3's, scaled by −1: an equiconcave element.
        var lens = Triplet(s =>
        {
            s.Surfaces[4].Radius = -s.Surfaces[3].Radius;
            s.Pickups.Add(new Pickup { SourceSurfaceIndex = 3, TargetSurfaceIndex = 4, Parameter = PickupParameter.Radius, ScaleFactor = -1.0 });
        });
        var trial = lens.Perturb();
        new RadiusChange(3).Apply(trial, 0.5);
        var built = trial.Build().System;
        Assert.Equal(-built.Surfaces[3].Curvature, built.Surfaces[4].Curvature, 15);
        Assert.Equal(-built.Surfaces[3].Curvature, trial.Readable().Surfaces[4].Curvature, 15);

        // An error on the target itself would be overwritten: refused before a run.
        var items = new[] { ToleranceItem.Symmetric(new RadiusChange(4), 0.5) };
        var e = Assert.Throws<ArgumentException>(() => Sensitivity.Run(lens, items, Rms, null));
        Assert.Contains("pickup of surface 3", e.Message);
    }

    [Fact]
    public void ACompensatorsMoveCarriesToItsPickups()
    {
        // The image surface's thickness is nothing; give surface 6's air space a follower: a dummy
        // space before the image that picks up the back focus, as a spacer tied to it would.
        var lens = Triplet(s =>
        {
            int image = s.Surfaces.Count - 1;
            double back = s.Surfaces[image - 1].Thickness;
            s.Surfaces[image - 1].Thickness = back / 2.0;
            s.Surfaces.Insert(image, new Surface { Thickness = back / 2.0, Comment = "follows" });
            for (int k = 0; k < s.Surfaces.Count; k++) s.Surfaces[k].Index = k;
            s.Pickups.Add(new Pickup { SourceSurfaceIndex = image - 1, TargetSurfaceIndex = image, Parameter = PickupParameter.Thickness });
        });
        var focus = new FocusCompensator(lens.System.Surfaces.Count - 3);
        var trial = lens.Perturb();
        new ThicknessChange(2).Apply(trial, 0.1);
        var r = focus.Solve(trial, Rms);
        Assert.True(double.IsFinite(r.Criterion));
        var built = trial.Build().System;
        int n = built.Surfaces.Count;
        Assert.Equal(built.Surfaces[n - 3].Thickness, built.Surfaces[n - 2].Thickness, 15);

        // The follower itself cannot be the compensator: its pickup would set it back.
        var e = Assert.Throws<ArgumentException>(() =>
            ToleranceRun.Prepare(lens.System, lens.Glass, new ToleranceSettings { Fields = FieldSymmetry.File },
                                 $"COMP {lens.System.Surfaces.Count - 2}"));
        Assert.Contains("pickup", e.Message);
    }

    [Fact]
    public void TheCompensatorsAreReadFromTheSet()
    {
        var lens = Triplet();
        var set = """
            TETX 3-4 0.1
            COMP 4 0.3     # the second air space, within ±0.3 mm
            CETX 3-4
            COMP 6
            """;
        var (_, _, c) = ToleranceRun.Prepare(LhltReader.Read(Lenses.CookeTriplet).System, lens.Glass, new ToleranceSettings(), set);
        Assert.NotNull(c);
        Assert.Equal(6, c!.Surface);                    // the back focus is the focus
        Assert.Null(c.Travel);
        Assert.Equal(2, c.Adjustments.Count);
        Assert.Equal(new SpaceAdjustment(4) { Travel = 0.3 }, c.Adjustments[0]);
        Assert.Equal(new ElementTiltAdjustment(3, 4, Axis.X), c.Adjustments[1]);
        Assert.Single(ToleranceRun.Parse(set, new ToleranceSettings()));
    }

    [Fact]
    public void ATiltCompensatorTakesBackATilt()
    {
        // The middle element tilted: refocusing alone leaves the coma; tilting it back in its cell
        // as well takes the error away.
        var lens = Triplet();
        var errors = new (Tolerance, double)[] { (new ElementTilt(3, 4, Axis.X), 0.5) };
        var focus = FocusCompensator.BackFocus(lens);
        var alone = Sensitivity.Evaluate(lens, errors, Rms, focus);
        var both = Sensitivity.Evaluate(lens, errors, Rms, focus with { Adjustments = new Adjustment[] { new ElementTiltAdjustment(3, 4, Axis.X) } });
        var nominal = Sensitivity.Evaluate(lens, Array.Empty<(Tolerance, double)>(), Rms, focus);
        Assert.True(alone.Criterion > nominal.Criterion * 1.2);
        Assert.Equal(nominal.Criterion, both.Criterion, 3);
        Assert.Equal(-0.5, both.Adjustments![0], 2);
    }
}
