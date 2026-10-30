using LensHH.Core.Analysis;
using LensHH.Core.Enums;
using LensHH.Core.Glass;
using LensHH.Core.Models;
using LensHH.Core.RayTrace;

namespace LensHH.Tolerancing;

/// <summary>
/// Which directions across the field a lens is judged in: its fields as the file gives them (+y),
/// on both sides of the axis in y (+y and −y), or in x as well (+y, +x, −y, −x).
/// </summary>
public enum FieldSymmetry { File, Y, XY }

/// <summary>
/// One direction a lens is judged in: the lens and its image turned about the axis by
/// <see cref="Turn"/> degrees, so that its +y fields fall on the lens where this direction's would.
/// A Coordinate Break's tilt about z re-expresses the ray in the turned frame, so a turn of 90°
/// puts a +y field on the lens's +x.
/// </summary>
public readonly record struct FieldView(string Name, double Turn)
{
    public static readonly FieldView PlusY = new("+y", 0.0);
    public static readonly FieldView PlusX = new("+x", 90.0);
    public static readonly FieldView MinusY = new("−y", 180.0);
    public static readonly FieldView MinusX = new("−x", 270.0);

    public static readonly IReadOnlyList<FieldView> All = new[] { PlusY, PlusX, MinusY, MinusX };

    /// <summary>The directions a symmetry judges in, +y first.</summary>
    public static IReadOnlyList<FieldView> Of(FieldSymmetry symmetry) => symmetry switch
    {
        FieldSymmetry.File => new[] { PlusY },
        FieldSymmetry.Y => new[] { PlusY, MinusY },
        _ => All,
    };
}

/// <summary>
/// The lens as designed, with what perturbing it needs: its own copy of the system (semi-diameters
/// solved, so a power tolerance has an aperture to be measured over), the glass catalog, and the
/// paraxial values a compensator starts from.
/// </summary>
public sealed class NominalLens
{
    public NominalLens(OpticalSystem system, GlassCatalogManager glass, FieldSymmetry fields = FieldSymmetry.XY)
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
        Views = FieldView.Of(fields);
        Paraxial = Built.ParaxialOf(System, Glass);
        Nd = Glass.BuildRefractiveIndexArray(System, ModelGlass.LambdaD);
        NF = Glass.BuildRefractiveIndexArray(System, ModelGlass.LambdaF);
        NC = Glass.BuildRefractiveIndexArray(System, ModelGlass.LambdaC);
    }

    public OpticalSystem System { get; }
    public GlassCatalogManager Glass { get; }
    public ParaxialResult Paraxial { get; }

    /// <summary>Index of the medium after each surface at the d, F and C lines.</summary>
    public double[] Nd { get; }
    public double[] NF { get; }
    public double[] NC { get; }

    /// <summary>The fields the lens gives.</summary>
    public int FileFieldCount { get; }

    /// <summary>
    /// The directions each lens is judged in. A rotationally symmetric lens performs the same in
    /// every one; a decenter or tilt breaks that, and a lens judged on one side of the field only
    /// can look better than it is on the others.
    /// </summary>
    public IReadOnlyList<FieldView> Views { get; }

    /// <summary>The surface before the image: its thickness is the back focus.</summary>
    public int BackFocusSurface => System.Surfaces.Count - 2;

    /// <summary>Whether the medium after <paramref name="surface"/> is glass rather than air.</summary>
    public bool IsGlass(int surface)
    {
        if (surface < 1 || surface >= System.Surfaces.Count - 1) return false;
        var s = System.Surfaces[surface];
        if (s.IsMirror) return false;
        return Math.Abs(Nd[surface] - 1.0) > 1e-9;
    }

    /// <summary>A copy of the lens to perturb, sharing nothing with this one.</summary>
    public TrialLens Perturb() => new(this);
}

/// <summary>An element or group, surfaces <see cref="First"/> to <see cref="Last"/>, moved as one: the move is given at its first vertex.</summary>
public readonly record struct ElementMove(int First, int Last, Placement Move)
{
    public bool Contains(int surface) => First <= surface && surface <= Last;
}

/// <summary>
/// One perturbed copy of a <see cref="NominalLens"/>. Tolerances change its surfaces and glass
/// directly and record its elements' and surfaces' moves; <see cref="Build"/> makes the system to
/// trace, each moved surface between a pair of Coordinate Breaks, and <see cref="Readable"/> the
/// same lens as a designer would lay it out, to save.
///
/// <para>The lens's pickups are solved on what is built, so an error or a compensator's move of a
/// pickup's source carries to its target, as the design ties them.</para>
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
        _elementMoves.AddRange(other._elementMoves);
        foreach (var (k, v) in other._surfaceMoves) _surfaceMoves[k] = v;
    }

    /// <summary>A copy of the lens as it now stands, errors and moves included, sharing nothing with it.</summary>
    public TrialLens Clone() => new(this);

    public NominalLens Nominal { get; }

    /// <summary>The lens's surfaces, numbered as the nominal's: tolerances change these.</summary>
    public OpticalSystem System { get; }

    private readonly List<ElementMove> _elementMoves = new();
    private readonly Dictionary<int, Placement> _surfaceMoves = new();

    /// <summary>
    /// Moves an element or group, surfaces <paramref name="first"/> to <paramref name="last"/>, as a
    /// rigid body - decentred or tilted in the barrel - by a move given at its first vertex. A second
    /// move of the same surfaces is made inside the first.
    /// </summary>
    public void MoveElement(int first, int last, Placement move)
    {
        int k = _elementMoves.FindIndex(m => m.First == first && m.Last == last);
        if (k >= 0) _elementMoves[k] = _elementMoves[k] with { Move = move.Within(_elementMoves[k].Move) };
        else _elementMoves.Add(new ElementMove(first, last, move));
    }

    /// <summary>
    /// Moves the surface on its own element - a wedge. It is made inside the element's move,
    /// whichever was applied first, so a wedged element tilted in its cell is both at once.
    /// </summary>
    public void MoveSurface(int surface, Placement move) =>
        _surfaceMoves[surface] = _surfaceMoves.TryGetValue(surface, out var sofar) ? move.Within(sofar) : move;

    /// <summary>
    /// The element moves, the outermost first: a group's before an element's inside it. Moves of the
    /// same size keep the order they were made in.
    /// </summary>
    public IReadOnlyList<ElementMove> ElementMoves =>
        _elementMoves.Where(m => !m.Move.IsIdentity).OrderByDescending(m => m.Last - m.First).ToList();

    public IReadOnlyDictionary<int, Placement> SurfaceMoves => _surfaceMoves;

    /// <summary>
    /// Each moved surface's composite placement on <paramref name="system"/>: its elements' moves,
    /// outermost first, each carried to this vertex, then its own move inside them.
    /// </summary>
    public IReadOnlyDictionary<int, Placement> MovesOn(OpticalSystem system)
    {
        var elements = ElementMoves;
        var all = new Dictionary<int, Placement>();
        var moved = elements.SelectMany(e => Enumerable.Range(e.First, e.Last - e.First + 1)).Union(_surfaceMoves.Keys);
        foreach (int s in moved)
        {
            var p = Placement.Identity;
            foreach (var e in elements.Where(e => e.Contains(s)))
                p = e.Move.At(Distance(system, e.First, s)).Within(p);
            if (_surfaceMoves.TryGetValue(s, out var own)) p = own.Within(p);
            all[s] = p;
        }
        return all;
    }

    /// <summary>Along the axis from vertex <paramref name="from"/> to vertex <paramref name="to"/>, by the thicknesses between.</summary>
    internal static double Distance(OpticalSystem system, int from, int to)
    {
        double z = 0.0;
        for (int k = from; k < to; k++) z += system.Surfaces[k].Thickness;
        return z;
    }

    /// <summary>The lens's surfaces with its pickups solved: what is built from.</summary>
    private OpticalSystem Solved()
    {
        var system = System.DeepClone();
        PickupSolver.Solve(system);
        return system;
    }

    /// <summary>
    /// The lens as it now stands, as a system LensHH traces: laid out as <see cref="ReadableLayout"/>
    /// does, every surface keeping its thickness and each move a pair of Coordinate Breaks whose
    /// second picks up the first. Moves of elements that overlap without one inside the other cannot
    /// be nested so; then each moved surface is put between a pair of its own.
    /// </summary>
    public Built Build()
    {
        var system = Solved();
        var elements = ElementMoves;
        if (!ReadableLayout.CanNest(elements)) return BuildSurfaceBySurface();
        var (laid, map) = ReadableLayout.Lay(system, elements, _surfaceMoves);
        return Built.Laid(laid, map, Nominal.Glass, Nominal.Views);
    }

    /// <summary>
    /// The lens with each moved surface between a pair of breaks of its own, placed by its elements'
    /// and its own moves together: the other way to build it, and a check on the first.
    /// </summary>
    public Built BuildSurfaceBySurface()
    {
        var system = Solved();
        return Built.From(system, MovesOn(system), Nominal.Glass, Nominal.Views);
    }

    /// <summary>The lens as it now stands, to save: the system <see cref="Build"/> traces.</summary>
    public OpticalSystem Readable() => Build().System;
}

/// <summary>
/// A trial lens made traceable, as <see cref="ReadableLayout"/> lays it out or, for element moves
/// that cannot nest, by <see cref="From"/>: every moved surface s becomes three, a Coordinate Break
/// into its frame, the surface itself with no thickness, and the break back (Order 1, the same values
/// negated) carrying the thickness. A move along the axis, Dz, goes on the thickness before and comes
/// off the thickness after. <see cref="Map"/> gives each original surface's index in <see cref="System"/>.
/// </summary>
public sealed class Built
{
    private Built(OpticalSystem system, int[] map, GlassCatalogManager glass, IReadOnlyList<FieldView>? views = null)
    {
        System = system;
        Map = map;
        Glass = glass;
        Directions = views ?? new[] { FieldView.PlusY };
    }

    public OpticalSystem System { get; }
    public int[] Map { get; }
    public GlassCatalogManager Glass { get; }

    /// <summary>The directions across the field the lens is judged in, +y first.</summary>
    public IReadOnlyList<FieldView> Directions { get; }

    private IReadOnlyList<Built>? _views;

    /// <summary>
    /// The lens turned to each of its <see cref="Directions"/> (<see cref="FieldTurn"/>), its fields'
    /// weights shared among them: each off-axis field's weight split evenly, and the field on the
    /// axis, the same in every direction, judged once, in the first. A criterion summed over the
    /// views with these weights is the lens's over the whole field. The lens itself when it is
    /// judged at +y alone.
    /// </summary>
    public IReadOnlyList<Built> Views => _views ??= MakeViews();

    private IReadOnlyList<Built> MakeViews()
    {
        if (Directions.Count == 1 && Directions[0].Turn == 0.0) return new[] { this };
        var views = new List<Built>();
        for (int v = 0; v < Directions.Count; v++)
        {
            var (turned, map) = FieldTurn.Apply(System, Directions[v].Turn);
            foreach (var f in turned.Fields)
                f.Weight = Math.Abs(f.Y) < 1e-12 ? (v == 0 ? f.Weight : 0.0) : f.Weight / Directions.Count;
            views.Add(new Built(turned, Map.Select(i => map[i]).ToArray(), Glass, new[] { Directions[v] }));
        }
        return views;
    }

    /// <summary>The lens turned to one direction, its fields' weights as the file gives them: to show it so.</summary>
    public Built Toward(FieldView direction)
    {
        var (turned, map) = FieldTurn.Apply(System, direction.Turn);
        return new Built(turned, Map.Select(i => map[i]).ToArray(), Glass, new[] { direction });
    }

    private ParaxialResult? _paraxial;

    /// <summary>The paraxial values. Breaks are flat planes to a paraxial trace, so these are the lens's without its moves.</summary>
    public ParaxialResult Paraxial => _paraxial ??= ParaxialOf(System, Glass);

    internal static ParaxialResult ParaxialOf(OpticalSystem system, GlassCatalogManager glass)
    {
        var indices = glass.BuildRefractiveIndexArray(system, system.Wavelengths[system.PrimaryWavelengthIndex].Value);
        return new ParaxialRayTracer(system, indices).Solve();
    }

    /// <summary>A lens as it stands, with no moves to make: a saved trial read back, to judge again.</summary>
    public static Built Of(OpticalSystem system, GlassCatalogManager glass, IReadOnlyList<FieldView>? views = null) =>
        From(system, new Dictionary<int, Placement>(), glass, views);

    internal static Built Laid(OpticalSystem system, int[] map, GlassCatalogManager glass, IReadOnlyList<FieldView> views) =>
        new(system, map, glass, views);

    internal static Built From(OpticalSystem trial, IReadOnlyDictionary<int, Placement> moves, GlassCatalogManager glass,
                               IReadOnlyList<FieldView>? views = null)
    {
        var system = trial.DeepClone();
        var map = new int[trial.Surfaces.Count];
        if (moves.Count == 0 || moves.Values.All(m => m.IsIdentity))
        {
            for (int i = 0; i < map.Length; i++) map[i] = i;
            return new Built(system, map, glass, views);
        }

        var source = system.Surfaces.ToList();
        var surfaces = new List<Surface>();
        for (int i = 0; i < source.Count; i++)
        {
            var s = source[i];
            // The wavefront's plane-wave reference for an object at infinity is taken on surface 1,
            // in its own frame. A break there would put it in the tilted one, so a moved first
            // surface gets a plain plane in front of its break.
            if (i == 1 && moves.TryGetValue(1, out var first) && !first.IsIdentity)
                surfaces.Add(new Surface { Thickness = 0.0, Material = source[0].Material, Comment = "Reference plane" });
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
        return new Built(system, map, glass, views);
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
