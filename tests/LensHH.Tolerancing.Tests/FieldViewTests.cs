using LensHH.Core.IO;
using LensHH.Core.Models;
using Xunit;

namespace LensHH.Tolerancing.Tests;

// The lens judged across the field in more than one direction: turned about its axis, image and
// all, so that its +y fields show what it does at −y, +x and −x.
public class FieldViewTests
{
    private static NominalLens Triplet(FieldSymmetry fields) =>
        new(LhltReader.Read(Lenses.CookeTriplet).System, Lenses.Glass(), fields);

    private static readonly RmsWavefrontCriterion Rms = new();

    private static double Judge(NominalLens lens, string tolerances)
    {
        var trial = lens.Perturb();
        foreach (var item in ToleranceSpec.ParseSet(tolerances)) item.Tolerance.Apply(trial, item.Max);
        return Rms.Evaluate(trial.Build());
    }

    [Theory]
    [InlineData(90.0, 1.0, 0.0)]     // +x
    [InlineData(180.0, 0.0, -1.0)]   // −y
    [InlineData(270.0, -1.0, 0.0)]   // −x
    public void TheTurnPutsAPlusYFieldWhereItsDirectionIs(double turn, double x, double y)
    {
        var lens = Triplet(FieldSymmetry.File);
        var (turned, map) = FieldTurn.Apply(lens.System, turn);
        var n = lens.Glass.BuildRefractiveIndexArray(turned, turned.Wavelengths[turned.PrimaryWavelengthIndex].Value);
        double u = Math.Tan(14.0 * Math.PI / 180.0);
        double m = u / Math.Sqrt(1 + u * u), l = 1 / Math.Sqrt(1 + u * u);

        // A +y field's chief ray, through the first vertex, as the lens's first surface sees it.
        var t = new LensHH.Core.RayTrace.RayTracer(turned, n, ignoreApertures: true).Trace(0, 0, 0, m, l, startSurface: 1, endSurface: map[1]);
        Assert.True(t.Success);
        double across = Math.Sqrt(t.FinalRay.L * t.FinalRay.L + t.FinalRay.M * t.FinalRay.M);
        Assert.Equal(x, t.FinalRay.L / across, 9);
        Assert.Equal(y, t.FinalRay.M / across, 9);
    }

    [Fact]
    public void ATurnedLensImagesAsTheLensItself()
    {
        // The nominal lens is the same in every direction: each view judges it as the file does.
        var lens = Triplet(FieldSymmetry.File);
        var built = lens.Perturb().Build();
        double plain = Rms.Evaluate(built);
        foreach (var direction in FieldView.All)
            Assert.Equal(plain, Rms.Evaluate(built.Toward(direction)), 9);
    }

    [Fact]
    public void JudgedInYTheLensIsJudgedAtItsMirroredFields()
    {
        // ±y by turning the lens half round is the lens at its fields and their mirror images at −y,
        // the weight of each off-axis field split between the two.
        var lens = Triplet(FieldSymmetry.Y);
        var trial = lens.Perturb();
        foreach (var item in ToleranceSpec.ParseSet("TEDY 3-4 0.05\nTETX 5-6 0.2\nTEDX 1-2 0.03"))
            item.Tolerance.Apply(trial, item.Max);
        double turned = Rms.Evaluate(trial.Build());

        var mirrored = trial.Build().System.DeepClone();
        foreach (var f in mirrored.Fields.ToList())
        {
            if (Math.Abs(f.Y) < 1e-12) continue;
            f.Weight /= 2.0;
            mirrored.Fields.Add(new Field { Y = -f.Y, Weight = f.Weight });
        }
        Assert.Equal(Rms.Evaluate(Built.Of(mirrored, lens.Glass)), turned, 9);
    }

    [Theory]
    [InlineData("TEDX 3-4 0.05", "TEDY 3-4 0.05")]
    [InlineData("TETX 5-6 0.3", "TETY 5-6 0.3")]
    [InlineData("TSTX 2 0.1", "TSTY 2 0.1")]
    public void InEveryDirectionAnErrorInXCountsAsTheSameErrorInY(string inX, string inY)
    {
        // Turning an error a quarter round turns the four directions into each other, so judged in
        // all four the lens is the same whichever way the error lies. Judged in y alone it is not.
        var four = Triplet(FieldSymmetry.XY);
        Assert.Equal(Judge(four, inX), Judge(four, inY), 7);

        var two = Triplet(FieldSymmetry.Y);
        Assert.NotEqual(Judge(two, inX), Judge(two, inY), 3);
    }

    [Fact]
    public void TheFieldOnTheAxisCountsOnce()
    {
        var lens = Triplet(FieldSymmetry.XY);
        var views = lens.Perturb().Build().Views;
        Assert.Equal(4, views.Count);
        double total = views.Sum(v => v.System.Fields.Sum(f => f.Weight));
        Assert.Equal(lens.System.Fields.Sum(f => f.Weight), total, 12);
        Assert.Equal(lens.System.Fields[0].Weight, views[0].System.Fields[0].Weight);
        Assert.All(views.Skip(1), v => Assert.Equal(0.0, v.System.Fields[0].Weight));
    }
}
