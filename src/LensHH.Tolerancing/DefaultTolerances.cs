using LensHH.Core.Enums;

namespace LensHH.Tolerancing;

/// <summary>How tightly the shop works: the four grades of Smith 1985, Table 3.</summary>
public enum ShopGrade { LowCost, Commercial, Precision, ExtraPrecise }

/// <summary>The default size of each kind of tolerance at one shop grade.</summary>
public sealed record GradeValues(double Fringes, double ThicknessMm, double Index, double AbbePercent,
                                 double SurfaceTiltDeg, double SurfaceDecentreMm,
                                 double ElementDecentreMm, double ElementTiltDeg, double IrregularityFringes);

/// <summary>
/// A starting set of tolerances for a lens, from the shop grade - a trial budget, before any is
/// scaled to the lens's sensitivities:
/// <list type="bullet">
/// <item>power and irregularity in fringes, and wedge (surface tilt in x and y), on every surface where the index changes;</item>
/// <item>surface decentre on a conic or aspheric surface - on a sphere it is the same error as a tilt;</item>
/// <item>thickness of every glass and every air space, except the one the compensator refocuses -
/// the back focus unless another is given - and any of no thickness, two surfaces in contact;</item>
/// <item>index and Abbe number of every glass;</item>
/// <item>decentre and tilt in x and y of every element, cemented group and mirror.</item>
/// </list>
/// </summary>
public static class DefaultTolerances
{
    /// <summary>
    /// Smith 1985, Table 3, for power and irregularity (fringes), thickness, index and surface tilt.
    /// Abbe number percentages are the glass makers' standard and closer steps (±0.8 %, ±0.5 %,
    /// ±0.3 %). Element decentre and tilt are spaced between Ginsberg 1981's "fairly loose" and
    /// "fairly tight" (0.25 to 0.025 mm, 2 to 0.3 mrad).
    /// </summary>
    public static GradeValues For(ShopGrade grade) => grade switch
    {
        ShopGrade.LowCost => new(50.0, 0.5, 0.002, 0.8, Deg(0.005), 0.1, 0.2, 0.2, 10.0),
        ShopGrade.Commercial => new(10.0, 0.2, 0.002, 0.8, Deg(0.001), 0.05, 0.1, 0.1, 2.0),
        ShopGrade.Precision => new(5.0, 0.1, 0.001, 0.5, Deg(0.0003), 0.02, 0.05, 0.05, 1.0),
        ShopGrade.ExtraPrecise => new(1.0, 0.05, 0.0002, 0.3, Deg(0.0001), 0.01, 0.025, 0.02, 0.25),
        _ => throw new ArgumentOutOfRangeException(nameof(grade)),
    };

    private static double Deg(double radians) => radians * 180.0 / Math.PI;

    /// <summary>The largest part of a thickness a tolerance may take: beyond it a thin centre or a narrow air space could close up.</summary>
    public const double ThicknessFraction = 0.2;

    /// <summary>A thickness tolerance no larger than <see cref="ThicknessFraction"/> of the thickness itself.</summary>
    public static double ThicknessCap(NominalLens lens, int surface, double tolerance) =>
        Math.Min(tolerance, ThicknessFraction * Math.Abs(lens.System.Surfaces[surface].Thickness));

    private static double Abbe(NominalLens lens, int surface) => (lens.Nd[surface] - 1.0) / (lens.NF[surface] - lens.NC[surface]);

    public static List<ToleranceItem> Create(NominalLens lens, ShopGrade grade = ShopGrade.Commercial,
                                             double testWavelengthUm = PowerFringes.HeNe,
                                             FocusCompensator? compensator = null)
    {
        var g = For(grade);
        var items = new List<ToleranceItem>();
        int last = lens.BackFocusSurface;

        for (int s = 1; s <= last; s++)
        {
            var surface = lens.System.Surfaces[s];
            if (surface.Type != SurfaceType.Standard && surface.Type != SurfaceType.EvenAsphere) continue;
            bool refracts = surface.IsMirror || Math.Abs(lens.Nd[s] - lens.Nd[s - 1]) > 1e-9;
            if (!refracts) continue;
            items.Add(ToleranceItem.Symmetric(new PowerFringes(s, testWavelengthUm), g.Fringes));
            items.Add(ToleranceItem.Symmetric(new SurfaceIrregularity(s, testWavelengthUm), g.IrregularityFringes));
            items.Add(ToleranceItem.Symmetric(new SurfaceTilt(s, Axis.X), g.SurfaceTiltDeg));
            items.Add(ToleranceItem.Symmetric(new SurfaceTilt(s, Axis.Y), g.SurfaceTiltDeg));
            if (surface.Conic != 0.0 || (surface.Type == SurfaceType.EvenAsphere && surface.AsphericCoefficients.Any(a => a != 0.0)))
            {
                items.Add(ToleranceItem.Symmetric(new SurfaceDecentre(s, Axis.X), g.SurfaceDecentreMm));
                items.Add(ToleranceItem.Symmetric(new SurfaceDecentre(s, Axis.Y), g.SurfaceDecentreMm));
            }
        }
        int focus = compensator?.Surface ?? last;
        // A space of no thickness - two surfaces in contact - has none to tolerance.
        for (int s = 1; s <= last; s++)
            if (s != focus && ThicknessCap(lens, s, g.ThicknessMm) is double t && t > 0.0)
                items.Add(ToleranceItem.Symmetric(new ThicknessChange(s), t));
        for (int s = 1; s < last; s++)
        {
            if (!lens.IsGlass(s)) continue;
            items.Add(ToleranceItem.Symmetric(new IndexChange(s), g.Index));
            items.Add(ToleranceItem.Symmetric(new AbbeChange(s), Abbe(lens, s) * g.AbbePercent / 100.0));
        }
        foreach (var (first, end) in Elements(lens))
        {
            items.Add(ToleranceItem.Symmetric(new ElementDecentre(first, end, Axis.X), g.ElementDecentreMm));
            items.Add(ToleranceItem.Symmetric(new ElementDecentre(first, end, Axis.Y), g.ElementDecentreMm));
            items.Add(ToleranceItem.Symmetric(new ElementTilt(first, end, Axis.X), g.ElementTiltDeg));
            items.Add(ToleranceItem.Symmetric(new ElementTilt(first, end, Axis.Y), g.ElementTiltDeg));
        }
        return items;
    }

    /// <summary>
    /// The elements of the lens, first and last surface: a run of glass, cemented surfaces included,
    /// from the surface it enters at to the one it leaves by; and every mirror on its own.
    /// </summary>
    public static List<(int First, int Last)> Elements(NominalLens lens)
    {
        var elements = new List<(int, int)>();
        int last = lens.BackFocusSurface;
        for (int s = 1; s <= last; s++)
        {
            if (lens.System.Surfaces[s].IsMirror) { elements.Add((s, s)); continue; }
            if (!lens.IsGlass(s)) continue;
            int end = s;
            while (end < last && lens.IsGlass(end)) end++;
            elements.Add((s, end));
            s = end;
        }
        return elements;
    }
}
