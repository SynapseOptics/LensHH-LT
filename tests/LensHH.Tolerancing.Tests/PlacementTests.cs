using LensHH.Core.Enums;
using LensHH.Core.Models;
using LensHH.Core.RayTrace;
using Xunit;

namespace LensHH.Tolerancing.Tests;

// The moves of surfaces and elements, made as Coordinate Breaks, against lenses built by hand.
public class PlacementTests
{
    [Theory]
    [InlineData(3.0, -2.0, 7.0)]
    [InlineData(-0.4, 0.9, 0.0)]
    [InlineData(0.0, 0.0, -12.0)]
    public void ABreaksAnglesComeBackFromItsMatrix(double ax, double ay, double az)
    {
        // Rz·Ry·Rx from the angles, as a break applies them, then back.
        const double r = Math.PI / 180.0;
        var x = Placement.Tilt(ax * r, 0);
        var y = Placement.Tilt(0, ay * r);
        double c = Math.Cos(az * r), s = Math.Sin(az * r);
        var z = new Placement(new[] { c, s, 0, -s, c, 0, 0, 0, 1 }, 0, 0, 0);
        var m = z.Within(y.Within(x));
        var (_, _, tx, ty, tz) = m.ToBreak();
        Assert.Equal(ax, tx, 10);
        Assert.Equal(ay, ty, 10);
        Assert.Equal(az, tz, 10);
    }

    private static double[] Image(OpticalSystem sys, params (double X, double Y)[] rays)
    {
        var gm = Lenses.Glass();
        var n = gm.BuildRefractiveIndexArray(sys, 0.5876);
        var tracer = new RayTracer(sys, n, ignoreApertures: true);
        var image = new List<double>();
        foreach (var (x, y) in rays)
        {
            var t = tracer.Trace(x, y, 0, 0, 1, startSurface: 1);
            Assert.True(t.Success);
            image.Add(t.FinalRay.X);
            image.Add(t.FinalRay.Y);
        }
        return image.ToArray();
    }

    private static readonly (double, double)[] Rays = { (0, 0), (0, 4), (0, -4), (3, 1), (-2, -3) };

    [Fact]
    public void AnElementDecentredMovesItsFocusWithIt()
    {
        var nominal = new NominalLens(Lenses.Singlet(), Lenses.Glass(), FieldSymmetry.File);
        var trial = nominal.Perturb();
        new ElementDecentre(1, 2, Axis.Y).Apply(trial, 0.1);
        var built = trial.Build().System;

        // A bundle symmetric about the moved axis lands symmetric about the moved focus.
        var up = Image(built, (0, 0.1 + 3));
        var down = Image(built, (0, 0.1 - 3));
        Assert.Equal(0.1, 0.5 * (up[1] + down[1]), 9);
    }

    [Fact]
    public void AnElementTiltMatchesTheLensTiltedByHand()
    {
        const double a = 0.5, pivot = 2.0;     // degrees about x, the pivot 2 mm behind the first vertex
        var nominal = new NominalLens(Lenses.Singlet(), Lenses.Glass(), FieldSymmetry.File);
        var trial = nominal.Perturb();
        new ElementTilt(1, 2, Axis.X, pivot).Apply(trial, a);
        var ours = Image(trial.Build().System, Rays);

        // By hand: go to the pivot, tilt, come back to the first vertex, the element, back to the
        // pivot along the tilted axis, untilt, and on to the image.
        var sys = Lenses.Singlet();
        var s1 = sys.Surfaces[1]; var s2 = sys.Surfaces[2]; var img = sys.Surfaces[3];
        var hand = new List<Surface>
        {
            sys.Surfaces[0],
            Cb(0, 0, 0, pivot, 0), Cb(a, 0, 0, -pivot, 0),
            s1, s2,
            Cb(0, 0, 0, 0, 0),
            Cb(-a, 0, 0, 52.0 - pivot, 1),
            img,
        };
        s2.Thickness = -(5.0 - pivot);
        sys.Surfaces.Clear();
        for (int i = 0; i < hand.Count; i++) { hand[i].Index = i; sys.Surfaces.Add(hand[i]); }
        var byHand = Image(sys, Rays);

        for (int i = 0; i < ours.Length; i++) Assert.Equal(byHand[i], ours[i], 9);
    }

    private static Surface Cb(double tiltX, double dy, double tiltY, double thickness, int order)
    {
        var cb = new Surface { Type = SurfaceType.CoordinateBreak, Thickness = thickness };
        cb.SetParameter(2, dy);
        cb.SetParameter(3, tiltX);
        cb.SetParameter(4, tiltY);
        cb.SetSetting(1, order);
        return cb;
    }

    [Fact]
    public void ASurfaceTiltIsABreakPairAroundIt()
    {
        var nominal = new NominalLens(Lenses.Singlet(), Lenses.Glass(), FieldSymmetry.File);
        var trial = nominal.Perturb();
        new SurfaceTilt(2, Axis.Y).Apply(trial, 0.2);
        var built = trial.Build();
        Assert.Equal(6, built.System.Surfaces.Count);
        Assert.Equal(3, built.Map[2]);
        var into = built.System.Surfaces[2];
        var back = built.System.Surfaces[4];
        Assert.Equal(SurfaceType.CoordinateBreak, into.Type);
        Assert.Equal(0.2, into.GetParameter(4), 12);
        Assert.Equal(-0.2, back.GetParameter(4), 12);
        Assert.Equal(1, back.GetSetting(1));
        Assert.Equal(47.0, back.Thickness, 12);
        Assert.Equal(0.0, built.System.Surfaces[3].Thickness);
    }
}
