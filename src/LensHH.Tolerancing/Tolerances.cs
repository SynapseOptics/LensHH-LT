using LensHH.Core.Analysis;
using LensHH.Core.Enums;
using LensHH.Core.Models;

namespace LensHH.Tolerancing;

/// <summary>
/// One kind of manufacturing error, applied to a trial lens by an amount. The codes are the
/// tolerance operands designers already know (TRAD, TTHI, TEDX, …), so a table reads as expected.
/// Surfaces are numbered as the lens's, 1 the first after the object.
/// </summary>
public abstract record Tolerance(int Surface)
{
    public abstract string Code { get; }
    public abstract string Units { get; }
    public abstract void Apply(TrialLens lens, double amount);

    /// <summary>The surface values the tolerance changes that a pickup could set instead.</summary>
    public virtual IEnumerable<(int Surface, PickupParameter Parameter)> Changes => Array.Empty<(int, PickupParameter)>();

    protected static void CheckSurface(TrialLens lens, int surface)
    {
        if (surface < 1 || surface >= lens.System.Surfaces.Count - 1)
            throw new ArgumentOutOfRangeException(nameof(surface), surface,
                $"surface {surface} is not between the object and the image");
    }

    /// <summary>The surface's semi-diameter on the nominal lens: what a test plate covers.</summary>
    public static double SemiDiameter(TrialLens lens, int surface)
    {
        double h = lens.Nominal.System.Surfaces[surface].SemiDiameter;
        if (!(h > 0.0)) throw new InvalidOperationException($"surface {surface} has no semi-diameter");
        return h;
    }

    /// <summary>Sag of a Standard or Even Asphere surface's shape (no departure) at height <paramref name="h"/>.</summary>
    internal static double Sag(Surface s, double h, double curvature) =>
        SystemLayout.ComputeSag(h, curvature, s.Conic,
            s.Type == SurfaceType.EvenAsphere ? s.AsphericCoefficients : null);
}

/// <summary>
/// Power error in fringes (TFRN): the surface's sag at its semi-diameter changes by
/// fringes × λ/2 at the test wavelength. The curvature that does that is found through the
/// surface's whole sag, conic and polynomial terms included.
/// </summary>
public sealed record PowerFringes(int Surface, double TestWavelengthUm = PowerFringes.HeNe) : Tolerance(Surface)
{
    /// <summary>The helium-neon line, the usual test wavelength.</summary>
    public const double HeNe = 0.6328;

    public override string Code => "TFRN";
    public override string Units => "fringes";
    public override IEnumerable<(int Surface, PickupParameter Parameter)> Changes => new[] { (Surface, PickupParameter.Radius) };

    public override void Apply(TrialLens lens, double amount)
    {
        CheckSurface(lens, Surface);
        var s = lens.System.Surfaces[Surface];
        double h = SemiDiameter(lens, Surface);
        double target = Sag(s, h, s.Curvature) + amount * TestWavelengthUm * 1e-3 / 2.0;
        s.Curvature = CurvatureForSag(s, h, target);
    }

    private static double CurvatureForSag(Surface s, double h, double sag)
    {
        double c0 = s.Curvature;
        // The sphere through the point is the start: exact for a sphere, close for anything else.
        double c1 = 2.0 * sag / (sag * sag + h * h);
        double f0 = Sag(s, h, c0) - sag, f1 = Sag(s, h, c1) - sag;
        for (int i = 0; i < 50 && Math.Abs(f1) > 1e-15; i++)
        {
            if (f1 == f0) break;
            double c2 = c1 - f1 * (c1 - c0) / (f1 - f0);
            (c0, f0) = (c1, f1);
            c1 = c2;
            f1 = Sag(s, h, c1) - sag;
        }
        if (!double.IsFinite(f1) || Math.Abs(f1) > 1e-12)
            throw new InvalidOperationException($"no curvature gives a sag of {sag} at height {h}");
        return c1;
    }
}

/// <summary>
/// Irregularity (TIRR) in fringes at the test wavelength over the surface's semi-diameter: its
/// departure from the best-fitting sphere, half astigmatism and half spherical, the split a shop's
/// surfaces usually show:
///
/// <list type="bullet">
/// <item>astigmatism, Fringe Z5 = ρ² cos 2θ, its cylinder along x and y;</item>
/// <item>spherical, Fringe Z9 − Z1 = 6ρ⁴ − 6ρ², which has no power and leaves the vertex where it is.</item>
/// </list>
///
/// <para>Each has a peak to valley of half the irregularity, fringes × λ/4 of sag. Neither changes
/// the paraxial lens. A negative amount turns the astigmatism through 90° and the spherical part
/// over. The orientation is the same on every surface, so astigmatism on two surfaces adds as if
/// aligned, which leans to the cautious side.</para>
/// </summary>
public sealed record SurfaceIrregularity(int Surface, double TestWavelengthUm = PowerFringes.HeNe) : Tolerance(Surface)
{
    public override string Code => "TIRR";
    public override string Units => "fringes";

    public override void Apply(TrialLens lens, double amount)
    {
        CheckSurface(lens, Surface);
        var s = lens.System.Surfaces[Surface];
        double half = amount * TestWavelengthUm * 1e-3 / 4.0;      // each half's peak to valley, sag
        Irregularity.Add(lens, Surface, (5, half / 2.0), (9, half / 1.5), (1, -half / 1.5));
    }
}

/// <summary>
/// Rotationally invariant irregularity (TRSI) in fringes at the test wavelength over the same
/// aperture: the part of the irregularity that is the same at every azimuth, the zonal error an
/// asphere is left with. Traced as Fringe Z9 − Z1 = 6ρ⁴ − 6ρ², with a peak to valley of
/// fringes × λ/2 of sag. A negative amount turns it over.
/// </summary>
public sealed record SymmetricIrregularity(int Surface, double TestWavelengthUm = PowerFringes.HeNe) : Tolerance(Surface)
{
    public override string Code => "TRSI";
    public override string Units => "fringes";

    public override void Apply(TrialLens lens, double amount)
    {
        CheckSurface(lens, Surface);
        double pv = amount * TestWavelengthUm * 1e-3 / 2.0;         // peak to valley, sag
        Irregularity.Add(lens, Surface, (9, pv / 1.5), (1, -pv / 1.5));
    }

    /// <summary>The sag it adds at height <paramref name="h"/> of an aperture of radius <paramref name="radius"/>, per unit of peak to valley.</summary>
    public static double Shape(double h, double radius)
    {
        double r2 = h * h / (radius * radius);
        return (6.0 * r2 * r2 - 6.0 * r2) / 1.5;
    }

    /// <summary><see cref="Shape"/>'s slope along the radius at height <paramref name="h"/>, per unit of peak to valley.</summary>
    public static double Slope(double h, double radius)
    {
        double rho = h / radius;
        return (24.0 * rho * rho * rho - 12.0 * rho) / (1.5 * radius);
    }
}

internal static class Irregularity
{
    /// <summary>Adds Fringe terms to the surface's Zernike departure, over its semi-diameter.</summary>
    public static void Add(TrialLens lens, int surface, params (int Fringe, double Sag)[] terms)
    {
        var s = lens.System.Surfaces[surface];
        if (s.Type != SurfaceType.Standard && s.Type != SurfaceType.EvenAsphere)
            throw new InvalidOperationException($"surface {surface} is a {s.Type}; irregularity is for a Standard or Even Asphere surface");
        double radius = ZernikeDeparture.Radius(s);
        double h = Tolerance.SemiDiameter(lens, surface);
        if (radius > 0.0 && Math.Abs(radius - h) > 1e-9 * h)
            throw new InvalidOperationException(
                $"surface {surface} already carries a Zernike departure over a radius of {radius}, not its semi-diameter {h}");
        var updated = terms.Select(t => (t.Fringe, ZernikeDeparture.Term(s, t.Fringe) + t.Sag));
        string? error = ZernikeDeparture.Set(s, h, updated);
        if (error != null) throw new InvalidOperationException(error);
    }
}

/// <summary>Radius error (TRAD), in lens units. A plane has no radius to change: use fringes.</summary>
public sealed record RadiusChange(int Surface) : Tolerance(Surface)
{
    public override string Code => "TRAD";
    public override string Units => "mm";
    public override IEnumerable<(int Surface, PickupParameter Parameter)> Changes => new[] { (Surface, PickupParameter.Radius) };

    public override void Apply(TrialLens lens, double amount)
    {
        CheckSurface(lens, Surface);
        var s = lens.System.Surfaces[Surface];
        if (s.Curvature == 0.0)
            throw new InvalidOperationException($"surface {Surface} is a plane; tolerance its power in fringes");
        s.Radius += amount;
    }
}

/// <summary>
/// Thickness error (TTHI), in lens units. With <paramref name="Adjust"/>, that surface's thickness
/// takes up the change, so everything beyond it stays where it was - an element made thick inside
/// a fixed barrel.
/// </summary>
public sealed record ThicknessChange(int Surface, int? Adjust = null) : Tolerance(Surface)
{
    public override string Code => "TTHI";
    public override string Units => "mm";
    public override IEnumerable<(int Surface, PickupParameter Parameter)> Changes =>
        Adjust is int a ? new[] { (Surface, PickupParameter.Thickness), (a, PickupParameter.Thickness) } : new[] { (Surface, PickupParameter.Thickness) };

    public override void Apply(TrialLens lens, double amount)
    {
        CheckSurface(lens, Surface);
        lens.System.Surfaces[Surface].Thickness += amount;
        if (Adjust is int a)
        {
            CheckSurface(lens, a);
            lens.System.Surfaces[a].Thickness -= amount;
        }
    }
}

/// <summary>
/// Index error (TIND) of the glass after the surface: n_d changes by the amount and the Abbe number
/// stays, so every wavelength's index moves with the dispersion scaled as n_d − 1.
/// </summary>
public sealed record IndexChange(int Surface) : Tolerance(Surface)
{
    public override string Code => "TIND";
    public override string Units => "";

    public override void Apply(TrialLens lens, double amount)
    {
        CheckGlass(lens, Surface);
        lens.System.Surfaces[Surface].IndexOffset += amount;
    }

    internal static void CheckGlass(TrialLens lens, int surface)
    {
        CheckSurface(lens, surface);
        if (!lens.Nominal.IsGlass(surface))
            throw new InvalidOperationException($"the medium after surface {surface} is not glass");
    }
}

/// <summary>
/// Abbe number error (TABB) of the glass after the surface, absolute: n_d stays and the dispersion
/// n_F − n_C changes to (n_d − 1)/(V_d + amount).
/// </summary>
public sealed record AbbeChange(int Surface) : Tolerance(Surface)
{
    public override string Code => "TABB";
    public override string Units => "";

    public override void Apply(TrialLens lens, double amount)
    {
        IndexChange.CheckGlass(lens, Surface);
        lens.System.Surfaces[Surface].AbbeOffset += amount;
    }
}

/// <summary>Conic constant error (TCON).</summary>
public sealed record ConicChange(int Surface) : Tolerance(Surface)
{
    public override string Code => "TCON";
    public override string Units => "";
    public override IEnumerable<(int Surface, PickupParameter Parameter)> Changes => new[] { (Surface, PickupParameter.Conic) };

    public override void Apply(TrialLens lens, double amount)
    {
        CheckSurface(lens, Surface);
        lens.System.Surfaces[Surface].Conic += amount;
    }
}

/// <summary>
/// Decentre of one surface on its element (TSDX, TSDY), in lens units. On a sphere it is a wedge:
/// the same as tilting the surface about its centre of curvature.
/// </summary>
public sealed record SurfaceDecentre(int Surface, Axis Axis) : Tolerance(Surface), IAxisTolerance
{
    public Tolerance WithAxis(Axis axis) => this with { Axis = axis };

    public override string Code => Axis == Axis.X ? "TSDX" : "TSDY";
    public override string Units => "mm";

    public override void Apply(TrialLens lens, double amount)
    {
        CheckSurface(lens, Surface);
        lens.MoveSurface(Surface, Axis == Axis.X ? Placement.Decentre(amount, 0.0) : Placement.Decentre(0.0, amount));
    }
}

/// <summary>
/// Tilt of one surface about its vertex (TSTX, TSTY), in degrees: the wedge between the two faces
/// of an element. A tilt about x turns the surface in the y-z plane, with a Coordinate Break's sign.
/// </summary>
public sealed record SurfaceTilt(int Surface, Axis Axis) : Tolerance(Surface), IAxisTolerance
{
    public Tolerance WithAxis(Axis axis) => this with { Axis = axis };

    public override string Code => Axis == Axis.X ? "TSTX" : "TSTY";
    public override string Units => "deg";

    public override void Apply(TrialLens lens, double amount)
    {
        CheckSurface(lens, Surface);
        double a = amount * Math.PI / 180.0;
        lens.MoveSurface(Surface, Axis == Axis.X ? Placement.Tilt(a, 0.0) : Placement.Tilt(0.0, a));
    }
}

/// <summary>
/// Decentre of an element or group, surfaces <see cref="Tolerance.Surface"/> to <paramref name="Last"/>,
/// moved together (TEDX, TEDY), in lens units: the element sits off the barrel's axis, unwedged.
/// </summary>
public sealed record ElementDecentre(int Surface, int Last, Axis Axis) : Tolerance(Surface), IAxisTolerance
{
    public Tolerance WithAxis(Axis axis) => this with { Axis = axis };

    public override string Code => Axis == Axis.X ? "TEDX" : "TEDY";
    public override string Units => "mm";

    public override void Apply(TrialLens lens, double amount)
    {
        CheckSurface(lens, Surface);
        CheckSurface(lens, Last);
        lens.MoveElement(Surface, Last, Axis == Axis.X ? Placement.Decentre(amount, 0.0) : Placement.Decentre(0.0, amount));
    }
}

/// <summary>
/// Tilt of an element or group as a rigid body (TETX, TETY), in degrees, about a pivot on the axis
/// <paramref name="PivotOffset"/> from its first vertex - the first vertex itself by default. Every
/// vertex swings off the axis and along it; nothing beyond the element moves.
/// </summary>
public sealed record ElementTilt(int Surface, int Last, Axis Axis, double PivotOffset = 0.0) : Tolerance(Surface), IAxisTolerance
{
    public Tolerance WithAxis(Axis axis) => this with { Axis = axis };

    public override string Code => Axis == Axis.X ? "TETX" : "TETY";
    public override string Units => "deg";

    public override void Apply(TrialLens lens, double amount)
    {
        CheckSurface(lens, Surface);
        CheckSurface(lens, Last);
        double a = amount * Math.PI / 180.0;
        lens.MoveElement(Surface, Last, Axis == Axis.X ? Placement.TiltAbout(a, 0.0, PivotOffset) : Placement.TiltAbout(0.0, a, PivotOffset));
    }
}

public enum Axis { X, Y }

/// <summary>
/// A tolerance with an x and a y kind - a decentre or a tilt. The two of one surface or element are
/// one error in some direction, so a Monte Carlo may draw them together.
/// </summary>
public interface IAxisTolerance
{
    Axis Axis { get; }

    /// <summary>The same tolerance about the other axis, or this one.</summary>
    Tolerance WithAxis(Axis axis);
}

/// <summary>
/// A tolerance and the range it may take: <see cref="Min"/> is usually negative. A range not centred
/// on nominal is a bias - a shop that makes thicknesses on the high side. <see cref="Distribution"/>
/// says how errors spread over the range, if not as the run's default.
/// </summary>
public sealed record ToleranceItem(Tolerance Tolerance, double Min, double Max)
{
    public Distribution? Distribution { get; init; }

    public double Center => 0.5 * (Min + Max);
    public double HalfWidth => 0.5 * (Max - Min);

    public static ToleranceItem Symmetric(Tolerance tolerance, double plusMinus) =>
        new(tolerance, -Math.Abs(plusMinus), Math.Abs(plusMinus));

    public override string ToString() =>
        $"{Tolerance.Code} {ToleranceSpec.Surfaces(Tolerance)} [{Min:G4}, {Max:G4}]{(Distribution == null ? "" : $" {Distribution}")}";
}
