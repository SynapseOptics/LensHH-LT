using LensHH.Core.Analysis;
using LensHH.Core.Enums;
using LensHH.Core.Glass;
using LensHH.Core.Models;
using LensHH.Core.RayTrace;

namespace LensHH.Tolerancing;

/// <summary>Which field points a lens is judged at: the file's, or each with its mirror image at −y as well.</summary>
public enum FieldSymmetry { File, Y }

/// <summary>
/// The lens as designed, with what perturbing it needs: its own copy of the system (semi-diameters
/// solved, so a power tolerance has an aperture to be measured over), the glass catalog, and the
/// paraxial values a compensator starts from.
/// </summary>
public sealed class NominalLens
{
    public NominalLens(OpticalSystem system, GlassCatalogManager glass, FieldSymmetry fields = FieldSymmetry.Y)
    {
        Glass = glass ?? throw new ArgumentNullException(nameof(glass));
        System = (system ?? throw new ArgumentNullException(nameof(system))).DeepClone();
        if (System.Surfaces.Count < 3) throw new ArgumentException("the lens has no surfaces between object and image");
        foreach (var s in System.Surfaces)
            if (s.Type == SurfaceType.CoordinateBreak || s.Type == SurfaceType.Abcd)
                throw new NotSupportedException(
                    $"surface {s.Index} is a {s.Type}: tolerancing is for a centred lens of refracting surfaces");
        SemiDiameterSolver.Solve(System, Glass);
        FileFieldCount = System.Fields.Count;
        if (fields == FieldSymmetry.Y) MirrorFields(System);
        Paraxial = Built.ParaxialOf(System, Glass);
    }

    public OpticalSystem System { get; }
    public GlassCatalogManager Glass { get; }
    public ParaxialResult Paraxial { get; }

    /// <summary>The fields the lens gives, before their mirror images: the first this many.</summary>
    public int FileFieldCount { get; }

    /// <summary>The surface before the image: its thickness is the back focus.</summary>
    public int BackFocusSurface => System.Surfaces.Count - 2;

    /// <summary>Whether the medium after <paramref name="surface"/> is glass rather than air.</summary>
    public bool IsGlass(int surface)
    {
        if (surface < 1 || surface >= System.Surfaces.Count - 1) return false;
        var s = System.Surfaces[surface];
        if (s.IsMirror) return false;
        double n = Glass.BuildRefractiveIndexArray(System, ModelGlass.LambdaD)[surface];
        return Math.Abs(n - 1.0) > 1e-9;
    }

    /// <summary>
    /// Adds each off-axis field's mirror image, −y, after the lens's own fields, and splits the
    /// weight between the two. A rotationally symmetric lens performs the same at y and −y; a
    /// decenter or tilt breaks that, and a lens judged on one side of the field only can look
    /// better than it is on the other.
    /// </summary>
    public static void MirrorFields(OpticalSystem system)
    {
        var mirrored = new List<Field>();
        foreach (var f in system.Fields)
        {
            if (Math.Abs(f.Y) < 1e-12) continue;
            f.Weight /= 2.0;
            mirrored.Add(new Field
            {
                Y = -f.Y, Weight = f.Weight,
                VDX = f.VDX, VDY = f.VDY, VCX = f.VCX, VCY = f.VCY, TAN = f.TAN,
            });
        }
        system.Fields.AddRange(mirrored);
    }

    /// <summary>A copy of the lens to perturb, sharing nothing with this one.</summary>
    public TrialLens Perturb() => new(this);
}

/// <summary>
/// One perturbed copy of a <see cref="NominalLens"/>. Tolerances change its surfaces and glass
/// directly and record its surfaces' moves; <see cref="Build"/> makes the system to trace, each
/// moved surface between a pair of Coordinate Breaks.
/// </summary>
public sealed class TrialLens
{
    internal TrialLens(NominalLens nominal)
    {
        Nominal = nominal;
        System = nominal.System.DeepClone();
    }

    private TrialLens(TrialLens other)
    {
        Nominal = other.Nominal;
        System = other.System.DeepClone();
        foreach (var (k, v) in other._elementMoves) _elementMoves[k] = v;
        foreach (var (k, v) in other._surfaceMoves) _surfaceMoves[k] = v;
    }

    /// <summary>A copy of the lens as it now stands, errors and moves included, sharing nothing with it.</summary>
    public TrialLens Clone() => new(this);

    public NominalLens Nominal { get; }

    /// <summary>The lens's surfaces, numbered as the nominal's: tolerances change these.</summary>
    public OpticalSystem System { get; }

    private readonly Dictionary<int, Placement> _elementMoves = new();
    private readonly Dictionary<int, Placement> _surfaceMoves = new();

    /// <summary>Moves the surface as part of its element or group - decentred or tilted in the barrel.</summary>
    public void MoveWithElement(int surface, Placement move) => Add(_elementMoves, surface, move);

    /// <summary>
    /// Moves the surface on its own element - a wedge. It is made inside the element's move,
    /// whichever was applied first, so a wedged element tilted in its cell is both at once.
    /// </summary>
    public void MoveSurface(int surface, Placement move) => Add(_surfaceMoves, surface, move);

    private static void Add(Dictionary<int, Placement> moves, int surface, Placement move) =>
        moves[surface] = moves.TryGetValue(surface, out var sofar) ? move.Within(sofar) : move;

    /// <summary>Each moved surface's composite placement: its own move inside its element's.</summary>
    public IReadOnlyDictionary<int, Placement> Moves
    {
        get
        {
            var all = new Dictionary<int, Placement>();
            foreach (int s in _elementMoves.Keys.Union(_surfaceMoves.Keys))
            {
                var element = _elementMoves.TryGetValue(s, out var e) ? e : Placement.Identity;
                var own = _surfaceMoves.TryGetValue(s, out var o) ? o : Placement.Identity;
                all[s] = own.Within(element);
            }
            return all;
        }
    }

    /// <summary>The lens as it now stands, as a system LensHH traces and saves.</summary>
    public Built Build() => Built.From(System, Moves, Nominal.Glass);
}

/// <summary>
/// A trial lens made traceable: every moved surface s becomes three, a Coordinate Break into its
/// frame, the surface itself with no thickness, and the break back (Order 1, the same values
/// negated) carrying the thickness. A move along the axis, Dz, goes on the thickness before and comes
/// off the thickness after. <see cref="Map"/> gives each original surface's index in <see cref="System"/>.
/// </summary>
public sealed class Built
{
    private Built(OpticalSystem system, int[] map, GlassCatalogManager glass)
    {
        System = system;
        Map = map;
        Glass = glass;
    }

    public OpticalSystem System { get; }
    public int[] Map { get; }
    public GlassCatalogManager Glass { get; }

    private ParaxialResult? _paraxial;

    /// <summary>The paraxial values. Breaks are flat planes to a paraxial trace, so these are the lens's without its moves.</summary>
    public ParaxialResult Paraxial => _paraxial ??= ParaxialOf(System, Glass);

    internal static ParaxialResult ParaxialOf(OpticalSystem system, GlassCatalogManager glass)
    {
        var indices = glass.BuildRefractiveIndexArray(system, system.Wavelengths[system.PrimaryWavelengthIndex].Value);
        return new ParaxialRayTracer(system, indices).Solve();
    }

    /// <summary>A lens as it stands, with no moves to make: a saved trial read back, to judge again.</summary>
    public static Built Of(OpticalSystem system, GlassCatalogManager glass) =>
        From(system, new Dictionary<int, Placement>(), glass);

    internal static Built From(OpticalSystem trial, IReadOnlyDictionary<int, Placement> moves, GlassCatalogManager glass)
    {
        var system = trial.DeepClone();
        var map = new int[trial.Surfaces.Count];
        if (moves.Count == 0 || moves.Values.All(m => m.IsIdentity))
        {
            for (int i = 0; i < map.Length; i++) map[i] = i;
            return new Built(system, map, glass);
        }

        var source = system.Surfaces.ToList();
        var surfaces = new List<Surface>();
        for (int i = 0; i < source.Count; i++)
        {
            var s = source[i];
            if (!moves.TryGetValue(i, out var move) || move.IsIdentity)
            {
                map[i] = surfaces.Count;
                surfaces.Add(s);
                continue;
            }
            var (dx, dy, tx, ty, tz) = move.ToBreak();
            if (move.Dz != 0.0) surfaces[^1].Thickness += move.Dz;

            var into = Break(dx, dy, tx, ty, tz, order: 0, thickness: 0.0, $"S{i} into its frame");
            var back = Break(-dx, -dy, -tx, -ty, -tz, order: 1, thickness: s.Thickness - move.Dz, $"S{i} back");
            s.Thickness = 0.0;
            surfaces.Add(into);
            map[i] = surfaces.Count;
            surfaces.Add(s);
            surfaces.Add(back);
        }

        system.Surfaces.Clear();
        for (int k = 0; k < surfaces.Count; k++)
        {
            surfaces[k].Index = k;
            system.Surfaces.Add(surfaces[k]);
        }
        RenumberSurfaceReferences(system, map);
        return new Built(system, map, glass);
    }

    private static Surface Break(double dx, double dy, double tx, double ty, double tz, int order,
                                 double thickness, string comment)
    {
        var cb = new Surface { Type = SurfaceType.CoordinateBreak, Thickness = thickness, Comment = comment };
        cb.SetParameter(1, dx);
        cb.SetParameter(2, dy);
        cb.SetParameter(3, tx);
        cb.SetParameter(4, ty);
        cb.SetParameter(5, tz);
        cb.SetSetting(1, order);
        return cb;
    }

    // Pickups name surfaces by index; with breaks inserted they must name the same surfaces still.
    private static void RenumberSurfaceReferences(OpticalSystem system, int[] map)
    {
        foreach (var p in system.Pickups)
        {
            if (p.TargetSurfaceIndex >= 0 && p.TargetSurfaceIndex < map.Length) p.TargetSurfaceIndex = map[p.TargetSurfaceIndex];
            if (p.SourceSurfaceIndex >= 0 && p.SourceSurfaceIndex < map.Length) p.SourceSurfaceIndex = map[p.SourceSurfaceIndex];
        }
    }
}
