using LensHH.Core.Analysis;
using LensHH.Core.IO;
using LensHH.Core.Models;
using Xunit;

namespace LensHH.Tolerancing.Tests;

// Each tolerance against what it should do, the focus compensator, and a Monte Carlo whose saved
// trials, read back, are the lenses it judged.
public class ToleranceTests
{
    private static NominalLens Singlet() => new(Lenses.Singlet(), Lenses.Glass(), FieldSymmetry.File);

    [Fact]
    public void PowerFringesChangeTheSagAtTheSemiDiameterByHalfAWavePerFringe()
    {
        var lens = Singlet();
        var trial = lens.Perturb();
        double h = lens.System.Surfaces[1].SemiDiameter;
        double before = SystemLayout.ComputeSag(h, trial.System.Surfaces[1].Curvature);
        new PowerFringes(1).Apply(trial, 4.0);
        double after = SystemLayout.ComputeSag(h, trial.System.Surfaces[1].Curvature);
        Assert.Equal(4.0 * 0.6328e-3 / 2.0, after - before, 12);
    }

    [Fact]
    public void IrregularityIsHalfAstigmatismHalfSphericalAndLeavesTheVertex()
    {
        var lens = Singlet();
        var trial = lens.Perturb();
        new SurfaceIrregularity(1).Apply(trial, 2.0);
        var s = trial.System.Surfaces[1];
        double h = lens.System.Surfaces[1].SemiDiameter;
        double half = 2.0 * 0.6328e-3 / 4.0;
        Assert.Equal(0.0, ZernikeDeparture.Sag(s, 0, 0), 15);
        // At the rim the spherical part is back to zero; the astigmatism is ±half/2.
        Assert.Equal(half / 2.0, ZernikeDeparture.Sag(s, h, 0), 12);
        Assert.Equal(-half / 2.0, ZernikeDeparture.Sag(s, 0, h), 12);
        Assert.Equal(h, ZernikeDeparture.Radius(s), 12);
    }

    [Fact]
    public void IndexAndAbbeErrorsGoOnTheGlass()
    {
        var lens = Singlet();
        var trial = lens.Perturb();
        new IndexChange(1).Apply(trial, 5e-4);
        new AbbeChange(1).Apply(trial, -0.5);
        Assert.Equal(5e-4, trial.System.Surfaces[1].IndexOffset);
        Assert.Equal(-0.5, trial.System.Surfaces[1].AbbeOffset);
        Assert.Throws<InvalidOperationException>(() => new IndexChange(2).Apply(trial, 5e-4));   // air
    }

    [Fact]
    public void TheFocusCompensatorTakesBackABackFocusError()
    {
        var lens = Singlet();
        var rms = new RmsWavefrontCriterion();
        var focus = FocusCompensator.BackFocus(lens);
        var nominal = Sensitivity.Evaluate(lens, Array.Empty<(Tolerance, double)>(), rms, focus);
        var moved = Sensitivity.Evaluate(lens, new[] { ((Tolerance)new ThicknessChange(2), 0.2) }, rms, focus);
        // The change is from the file's thickness, so the error taken back leaves it where the nominal's is.
        Assert.Equal(nominal.Change, moved.Change, 4);
        Assert.Equal(nominal.Criterion, moved.Criterion, 6);
    }

    // A wedge of an arcminute on a Cooke triplet costs hundredths of a wave. It once cost waves: a
    // break's slide back to its vertex plane was left out of the optical path, and a break on
    // surface 1 put the plane-wave reference in the tilted frame - both tilts across the pupil.
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void ASmallWedgeAddsLittle(int surface)
    {
        var glass = Lenses.Glass();
        var lens = new NominalLens(LhltReader.Read(Lenses.CookeTriplet).System, glass);
        var rms = new RmsWavefrontCriterion();
        double nominal = rms.Evaluate(lens.Perturb().Build());
        var trial = lens.Perturb();
        new SurfaceTilt(surface, Axis.X).Apply(trial, 1.0 / 60.0);
        Assert.InRange(rms.Evaluate(trial.Build()) - nominal, -0.02, 0.05);
    }

    [Fact]
    public void ASetReadsFromText()
    {
        var items = ToleranceSpec.ParseSet("""
            # a comment
            TRAD 1 0.05
            TTHI 1 -0.02 0.08 normal:3
            TETX 1-2 0.05 pivot:2.5
            TIRR 2 0.5
            """);
        Assert.Equal(4, items.Count);
        Assert.IsType<RadiusChange>(items[0].Tolerance);
        Assert.Equal(-0.02, items[1].Min);
        Assert.Equal(2.5, ((ElementTilt)items[2].Tolerance).PivotOffset);
        Assert.Equal("TIRR", items[3].Tolerance.Code);
    }

    [Fact]
    public void AMistakeIsReportedBeforeARun()
    {
        var lens = Singlet();
        var items = new[] { ToleranceItem.Symmetric(new IndexChange(2), 1e-3) };   // air after surface 2
        Assert.Throws<ArgumentException>(() =>
            Sensitivity.Run(lens, items, new RmsWavefrontCriterion(), null));
    }

    [Fact]
    public void SavedTrialsAreTheLensesTheMonteCarloJudged()
    {
        var glass = Lenses.Glass();
        var system = LhltReader.Read(Lenses.CookeTriplet).System;
        var lens = new NominalLens(system, glass);
        var items = ToleranceSpec.ParseSet("""
            TRAD 1 0.1
            TTHI 2 0.05
            TIND 1 0.0005
            TABB 3 0.5
            TIRR 3 1
            TEDX 1-2 0.02
            TEDY 1-2 0.02
            TETY 5-6 0.05
            TSTX 4 0.03
            """);
        var rms = new RmsWavefrontCriterion();
        var dir = Path.Combine(Path.GetTempPath(), "lenshh-mc-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new MonteCarloOptions
            {
                Trials = 6, Seed = 3, SaveDirectory = dir, SaveTrials = 3,
                MtfFrequencies = new double[] { 0, 10, 20, 30 },
            };
            var mc = MonteCarlo.Run(lens, items, rms, FocusCompensator.BackFocus(lens), options);

            Assert.Equal(6, mc.Trials.Count);
            Assert.Equal(0, mc.Untraceable);
            Assert.True(File.Exists(Path.Combine(dir, "trials.csv")));
            Assert.Equal(3, mc.Trials.Count(t => t.File != null));
            Assert.All(mc.Trials, t => Assert.True(t.Criterion >= mc.NominalCriterion * 0.5));
            Assert.Equal(lens.FileFieldCount, mc.NominalMtfT.Count);
            Assert.Equal(1.0, mc.Trials[0].MtfT[0][0], 6);           // MTF at zero frequency

            // Read back, the saved lens is judged as the trial was: tilts, decentres, irregularity,
            // glass and focus all in the file.
            var first = mc.Trials[0];
            var reread = LhltReader.Read(first.File!).System;
            Assert.Equal(first.Criterion, rms.Evaluate(Built.Of(reread, glass)), 9);

            // The same seed draws the same lenses.
            var again = MonteCarlo.Run(lens, items, rms, FocusCompensator.BackFocus(lens), options with { SaveDirectory = null });
            Assert.Equal(mc.Trials.Select(t => t.Criterion), again.Trials.Select(t => t.Criterion));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
