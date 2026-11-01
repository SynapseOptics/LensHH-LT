using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using LensHH.Core.Analysis;
using LensHH.Core.Enums;

namespace LensHH.Tolerancing.Drawings;

/// <summary>Which drawing practice a sheet follows.</summary>
public enum DrawingStandard
{
    /// <summary>ISO 10110: the three-field table (surface 1 | material | surface 2) and coded indications 0/ to 6/.</summary>
    Iso10110,

    /// <summary>
    /// US practice: ASME Y14.18 notes and a surface table, surface quality as scratch-dig per
    /// MIL-PRF-13830B, wedge as edge thickness difference, on an ANSI A sheet.
    /// </summary>
    Asme,
}

/// <summary>
/// What a drawing needs that a tolerance budget does not hold - the shop's cosmetic and material
/// classes, irregularity, chamfers, coatings, the title block - and the values to use for a
/// tolerance the budget left out. Read from JSON; every field has a default.
/// </summary>
public sealed record DrawingSettings
{
    public DrawingStandard Standard { get; init; } = DrawingStandard.Iso10110;

    /// <summary>
    /// The most power a drawing states in fringes. Fringes are counted against a test plate, which
    /// past ten or so is no longer practical; a looser power is stated as a tolerance on the radius,
    /// measured with a spherometer: ISO 10110-5's 3/−(B), its dash standing for the radius
    /// tolerance given with the radius, and in the US form a radius with its ± and no power.
    /// </summary>
    public double MaximumPowerFringes { get; init; } = 10.0;

    /// <summary>How much larger the edge diameter is than the largest clear aperture, on the diameter.</summary>
    public double EdgeMargin { get; init; } = 1.0;

    /// <summary>The edge diameter's tolerance, all on the minus side, so the lens always fits its cell.</summary>
    public double DiameterTolerance { get; init; } = 0.05;

    /// <summary>Protective chamfer, least face width; null to size it from the diameter (<see cref="Chamfer"/>).</summary>
    public double? ChamferMin { get; init; }

    /// <summary>Protective chamfer, largest face width; null for twice the least.</summary>
    public double? ChamferMax { get; init; }

    /// <summary>
    /// Burge's least bevel face width for a lens diameter (OPTI 521): 0.3 mm to Ø25, 0.5 mm from Ø50,
    /// 1 mm from Ø150, 2 mm from Ø400 - each row from its diameter up to the next.
    /// </summary>
    public static double BevelFor(double diameter) =>
        diameter >= 400.0 ? 2.0 : diameter >= 150.0 ? 1.0 : diameter >= 50.0 ? 0.5 : 0.3;

    /// <summary>
    /// The chamfer's face widths on a lens of this diameter: the settings' own, else Burge's least
    /// to twice that. Only a largest given (the old form) leaves no least.
    /// </summary>
    public (double? Min, double Max) Chamfer(double diameter)
    {
        if (ChamferMin == null && ChamferMax is double onlyMax) return (null, onlyMax);
        double min = ChamferMin ?? BevelFor(diameter);
        return (min, Math.Max(min, ChamferMax ?? 2.0 * min));
    }

    /// <summary>ISO 10110-7 surface imperfections, over the clear aperture.</summary>
    public string Imperfections { get; init; } = "5/3x0.16";

    /// <summary>MIL-PRF-13830B scratch-dig, over the clear aperture.</summary>
    public string ScratchDig { get; init; } = "60-40";

    /// <summary>ISO 10110-2 stress birefringence, nm/cm.</summary>
    public string StressBirefringence { get; init; } = "0/20";

    /// <summary>ISO 10110-3 bubbles and inclusions.</summary>
    public string Bubbles { get; init; } = "1/3x0.16";

    /// <summary>ISO 10110-4 inhomogeneity and striae classes.</summary>
    public string Inhomogeneity { get; init; } = "2/1;1";

    /// <summary>Coating of every surface not named in <see cref="Coatings"/>; none if null.</summary>
    public string? Coating { get; init; }

    /// <summary>Coating by surface number of the lens.</summary>
    public Dictionary<int, string> Coatings { get; init; } = new();

    /// <summary>
    /// What a coating name stands for, written out as a note on each drawing that uses it - so a
    /// surface can say "AR1" and the note give the band, reflectance and durability in full.
    /// </summary>
    public Dictionary<string, string> CoatingSpecs { get; init; } = new();

    /// <summary>
    /// The max slope deviation of every aspheric surface, in mrad, written as a note (ISO 10110-5), over a
    /// sampling length of <see cref="AsphereSlopeSamplingMm"/>: the mid-spatial ripple a shop leaves, which
    /// the analysis does not see. Null for none. The sag table's ±Δs is the low-order part, from the
    /// tolerances; a value tighter than it is warned of.
    /// </summary>
    public double? AsphereSlopeMrad { get; init; }

    /// <summary>The sampling length the slope is measured over, in mm; null to leave it to the shop.</summary>
    public double? AsphereSlopeSamplingMm { get; init; }

    /// <summary>Notes added to every drawing, after the ones the drawing writes itself.</summary>
    public List<string> Notes { get; init; } = new();

    /// <summary>
    /// Per-element changes, keyed by the element's first surface ("3") or its default drawing
    /// number ("OPT-02"): its title block, its own notes, and any value the drawing states.
    /// </summary>
    public Dictionary<string, ElementOverride> Elements { get; init; } = new();

    // For a tolerance the budget left out.
    public double DefaultFringes { get; init; } = 5.0;
    public double DefaultIrregularity { get; init; } = 1.0;
    public double DefaultThickness { get; init; } = 0.1;
    public double DefaultTiltArcmin { get; init; } = 3.0;
    public double DefaultIndex { get; init; } = 0.001;
    public double DefaultAbbePercent { get; init; } = 0.8;

    // Title block.
    public string Project { get; init; } = "";
    public string Company { get; init; } = "";
    public string DrawnBy { get; init; } = "";
    public string NumberPrefix { get; init; } = "OPT-";
    public string? Date { get; init; }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static DrawingSettings FromJson(string json) =>
        JsonSerializer.Deserialize<DrawingSettings>(json, Options) ?? new DrawingSettings();

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions(Options) { WriteIndented = true });
}

/// <summary>
/// What the settings change on one element's drawing; a null or blank field keeps the drawing's
/// own. A value given here takes the place of the one the tolerances give, and what follows from
/// it follows: power past <see cref="DrawingSettings.MaximumPowerFringes"/> is still stated as a
/// radius tolerance, the sag table is taken from the values as drawn, the edge thickness from the
/// diameter as drawn. Left out of the JSON when null.
/// </summary>
public sealed record ElementOverride
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Number { get; init; }

    /// <summary>A line under the title in the title block - the part's name or function.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    /// <summary>Notes for this element only, after the settings' notes for every drawing.</summary>
    public List<string> Notes { get; init; } = new();

    /// <summary>
    /// The glass as the drawing names it - "N-BK7 or equivalent", a melt, a vendor's own name. Its
    /// n_d and ν_d stay the design's: they are what the element must have.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Glass { get; init; }

    /// <summary>± on n_d.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? NdTolerance { get; init; }

    /// <summary>± on ν_d, in percent of it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? VdTolerancePercent { get; init; }

    /// <summary>The centre thickness's lower deviation, signed as the drawing states it: −0.05, or +0.02 for a thickness made on the high side.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ThicknessMinus { get; init; }

    /// <summary>The centre thickness's upper deviation, signed: +0.05.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ThicknessPlus { get; init; }

    /// <summary>The edge diameter. A cemented partner is made to it too.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Diameter { get; init; }

    /// <summary>The edge diameter's lower deviation, signed: −0.05.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? DiameterMinus { get; init; }

    /// <summary>The edge diameter's upper deviation, signed: 0 as the drawing has it by default.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? DiameterPlus { get; init; }

    /// <summary>The element's first surface, surface 1 on its drawing.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SurfaceOverride? Front { get; init; }

    /// <summary>The element's second surface, surface 2 on its drawing.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SurfaceOverride? Back { get; init; }
}

/// <summary>What the settings change on one surface of an element's drawing; null keeps the drawing's own.</summary>
public sealed record SurfaceOverride
{
    /// <summary>Power, in fringes at the drawing's test wavelength. Past <see cref="DrawingSettings.MaximumPowerFringes"/> it is stated as the radius tolerance it allows, as a computed power is.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? PowerFringes { get; init; }

    /// <summary>± on the radius, in lens units, stated in place of power in fringes whatever the fringes.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? RadiusTolerance { get; init; }

    /// <summary>Irregularity, in fringes at the drawing's test wavelength: ISO 10110-5's B.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? IrregularityFringes { get; init; }

    /// <summary>Rotationally invariant irregularity, in fringes: ISO 10110-5's C.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? SymmetricIrregularityFringes { get; init; }

    /// <summary>Tilt to the datum axis, in arcminutes: ISO 10110-6's 4/σ.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? TiltArcmin { get; init; }

    /// <summary>Decentre to the datum axis, in mm: ISO 10110-6's L.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? DecentreMm { get; init; }

    /// <summary>The coating's name; "none" for no coating.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Coating { get; init; }

    /// <summary>The clear aperture, on the diameter. The sag table runs to it, and the edge diameter is sized from it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ClearAperture { get; init; }
}

/// <summary>One optical surface of an element, as its drawing states it.</summary>
public sealed record SurfaceSpec(int Surface, double Radius, double ClearAperture, double Conic, IReadOnlyList<double> Aspheric,
                                 double PowerFringes, double IrregularityFringes, double TiltArcmin, double? DecentreMm,
                                 string? Coating, bool Cemented)
{
    /// <summary>The diameter a flat land starts at, for a surface that cannot reach the edge; null if it does.</summary>
    public double? LandFrom { get; init; }

    /// <summary>
    /// The radius tolerance, ± in lens units, that the drawing states in place of power when the
    /// power is more fringes than a test plate is read to (<see cref="DrawingSettings.MaximumPowerFringes"/>);
    /// null when power is stated in fringes.
    /// </summary>
    public double? RadiusTolerance { get; init; }

    /// <summary>The conic constant's range (TCON), below and above the design's; null when it is not toleranced.</summary>
    public (double Min, double Max)? ConicTolerance { get; init; }

    /// <summary>
    /// The rotationally invariant irregularity (TRSI) in fringes at the drawing's test wavelength,
    /// ISO 10110-5's C in 3/A(B/C); null when it is not toleranced.
    /// </summary>
    public double? SymmetricIrregularityFringes { get; init; }

    /// <summary>An aspheric surface's sag table (ISO 10110-12), from the vertex to the clear aperture; empty for a sphere or a plane.</summary>
    public IReadOnlyList<SagRow> SagTable { get; init; } = Array.Empty<SagRow>();

    public bool IsPlane => double.IsInfinity(Radius) || Radius == 0.0;
    public bool IsAspheric => Conic != 0.0 || Aspheric.Any(a => a != 0.0);
}

/// <summary>
/// One row of a sag table: the height, the sag there from the vertex along the axis (positive to
/// the right, ISO 10110-12's sign), and ± the most the surface's form tolerances move it, in µm,
/// and tilt it, in mrad.
/// </summary>
public sealed record SagRow(double H, double Z, double DeltaZUm)
{
    /// <summary>± the most the same tolerances tilt the surface there, along the radius, in mrad: ISO 10110-5's max slope deviation.</summary>
    public double DeltaSlopeMrad { get; init; }
}

/// <summary>The glass of an element and its tolerances.</summary>
public sealed record MaterialSpec(string Name, string? Catalog, double Nd, double Vd, double NdTolerance, double VdTolerancePercent);

/// <summary>
/// Everything one element's drawing says, from the lens and its allocated tolerances: the two
/// surfaces, the glass, the centre thickness and edge diameter with their tolerances. Radii are
/// the lens's, signed as it has them; each surface knows whether it is convex on the part.
/// </summary>
public sealed record ElementDrawing(string Number, string Title, int First, MaterialSpec Material,
                                    SurfaceSpec Front, SurfaceSpec Back,
                                    double Thickness, double ThicknessMinus, double ThicknessPlus,
                                    double Diameter, double DiameterMinus, double DiameterPlus,
                                    double TestWavelengthNm, double EdgeThickness,
                                    IReadOnlyList<string> MountTolerances, IReadOnlyList<string> Warnings)
{
    /// <summary>The title block's description line; empty for none.</summary>
    public string Description { get; init; } = "";

    /// <summary>Notes the settings add: those for every drawing, then this element's own.</summary>
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();

    /// <summary>The full specification of each coating name on the element, in surface order, from the settings.</summary>
    public IReadOnlyList<(string Name, string Spec)> CoatingNotes { get; init; } = Array.Empty<(string, string)>();

    /// <summary>Front convex when its centre of curvature is behind it, back convex when in front.</summary>
    public static string Shape(SurfaceSpec s, bool front) =>
        s.IsPlane ? "PLANO" : (front ? s.Radius > 0.0 : s.Radius < 0.0) ? "CX" : "CC";

    /// <summary>
    /// The surface's own sag at height <paramref name="h"/>: conic and polynomial terms. Past the
    /// height a conic reaches - a hemisphere's rim - it has none, NaN.
    /// </summary>
    internal static double SurfaceSag(SurfaceSpec s, double h)
    {
        double c = s.IsPlane ? 0.0 : 1.0 / s.Radius;
        if ((1.0 + s.Conic) * c * c * h * h > 1.0) return double.NaN;
        return SystemLayout.ComputeSag(h, c, s.Conic, s.Aspheric.Count > 0 ? s.Aspheric.ToArray() : null);
    }

    /// <summary>The part's sag at height <paramref name="h"/>: the surface's, and flat beyond its land.</summary>
    public static double Sag(SurfaceSpec s, double h)
    {
        double at = s.LandFrom is double from ? Math.Min(Math.Abs(h), from / 2.0) : Math.Abs(h);
        return SurfaceSag(s, at);
    }

    /// <summary>The wedge both surfaces' tilts make, root sum of squares, in radians: they are independent.</summary>
    public double WedgeRadians => Math.Sqrt(Front.TiltArcmin * Front.TiltArcmin + Back.TiltArcmin * Back.TiltArcmin) / 60.0 * Math.PI / 180.0;

    /// <summary>The edge thickness difference that wedge makes across the edge diameter.</summary>
    public double EdgeThicknessDifference => Diameter * Math.Tan(WedgeRadians);

    /// <summary>The deviation of a thin prism of that wedge, (n − 1) × wedge, in arcminutes.</summary>
    public double DeviationArcmin => (Material.Nd - 1.0) * WedgeRadians * 180.0 / Math.PI * 60.0;
}

/// <summary>
/// The drawings of a lens's elements from its tolerance set: one per piece of glass, cemented
/// pieces each on their own. A mirror has no thickness in a lens file, so it is not drawn.
/// </summary>
public static class ElementDrawings
{
    /// <param name="adjustments">
    /// The adjustments made at assembly (the set's CEDX, CEDY, CETX, CETY): an element decentred or
    /// tilted to align the lens is drawn as set at assembly, not held to the tolerance its mount
    /// would otherwise have.
    /// </param>
    public static List<ElementDrawing> Build(NominalLens lens, IReadOnlyList<ToleranceItem> items, DrawingSettings settings,
                                             double testWavelengthUm = PowerFringes.HeNe,
                                             IReadOnlyList<Adjustment>? adjustments = null)
    {
        adjustments ??= Array.Empty<Adjustment>();
        var drawings = new List<ElementDrawing>();
        var defaults = new List<(string First, string Number)>();
        var overrides = new List<ElementOverride>();
        int count = 0;
        for (int s = 1; s <= lens.BackFocusSurface; s++)
        {
            if (!lens.IsGlass(s)) continue;
            count++;
            string number = $"{settings.NumberPrefix}{count:D2}";
            var own = Override(settings, s, number);
            Check(own, s);
            if (!string.IsNullOrWhiteSpace(own.Number)) number = own.Number.Trim();
            drawings.Add(Element(lens, items, settings, s, s + 1, number, testWavelengthUm, adjustments, own));
            defaults.Add(($"{s.ToString(CultureInfo.InvariantCulture)}", $"{settings.NumberPrefix}{count:D2}"));
            overrides.Add(own);
        }
        foreach (var key in settings.Elements.Keys)
            if (!defaults.Any(k => k.First == key || k.Number == key))
                throw new ArgumentException($"drawing settings name element \"{key}\", which the lens does not have; " +
                                            "name an element by its first surface or drawing number: " +
                                            string.Join(", ", defaults.Select(k => $"{k.First} ({k.Number})")));
        var repeated = drawings.GroupBy(d => d.Number).FirstOrDefault(g => g.Count() > 1);
        if (repeated != null)
            throw new ArgumentException($"drawing number {repeated.Key} is given to more than one element");

        // Cemented pieces are made to one diameter, so the group is edged and mounted as one: the
        // smallest any of them could take - each piece's own edge and surfaces allow it - but never
        // less than the largest clear aperture among them. A diameter the settings give is the
        // group's; two different ones are drawn as given, and said so.
        for (int i = 0; i < drawings.Count; i++)
        {
            int j = i;
            while (j + 1 < drawings.Count && drawings[j].Back.Cemented) j++;
            if (j == i) continue;
            var group = drawings.GetRange(i, j - i + 1);
            var given = Enumerable.Range(i, j - i + 1).Where(k => overrides[k].Diameter != null).ToList();
            double shared;
            ElementDrawing setBy;
            if (given.Select(k => overrides[k].Diameter).Distinct().Count() > 1)
            {
                for (int k = i; k <= j; k++)
                    drawings[k] = drawings[k] with
                    {
                        Warnings = drawings[k].Warnings.Append("the settings give cemented partners different diameters: " +
                            string.Join(", ", given.Select(g => $"{drawings[g].Number} Ø{drawings[g].Diameter:G4}"))).ToList(),
                    };
                i = j;
                continue;
            }
            if (given.Count > 0)
            {
                setBy = drawings[given[0]];
                shared = setBy.Diameter;
            }
            else
            {
                double aperture = group.Max(d => Math.Max(d.Front.ClearAperture, d.Back.ClearAperture));
                shared = Math.Max(aperture, group.Min(d => d.Diameter));
                setBy = group.First(d => d.Diameter == group.Min(g => g.Diameter));
            }
            for (int k = i; k <= j; k++)
            {
                var own = drawings[k];
                if (own.Diameter == shared) continue;
                var redrawn = Element(lens, items, settings, own.First, own.First + 1, own.Number, testWavelengthUm, adjustments,
                                      overrides[k], shared);
                var notes = redrawn.Warnings.ToList();
                notes.Insert(0, $"diameter Ø{own.Diameter:G4} made Ø{shared:G4} to match its cemented partner {setBy.Number}");
                drawings[k] = redrawn with { Warnings = notes };
            }
            i = j;
        }
        return drawings.Select((d, i) => Customised(d, settings, overrides[i])).ToList();
    }

    /// <summary>The settings' changes to the element at <paramref name="first"/>, named by that surface or its default number.</summary>
    private static ElementOverride Override(DrawingSettings settings, int first, string defaultNumber) =>
        settings.Elements.TryGetValue(first.ToString(CultureInfo.InvariantCulture), out var byFirst) ? byFirst ?? new ElementOverride()
        : settings.Elements.TryGetValue(defaultNumber, out var byNumber) ? byNumber ?? new ElementOverride() : new ElementOverride();

    /// <summary>Refuses an override no drawing could state: a negative tolerance, a diameter of nothing, a thickness range upside down.</summary>
    private static void Check(ElementOverride own, int first)
    {
        void Size(double? v, string name, bool positive = false)
        {
            if (v is double x && (!double.IsFinite(x) || x < 0.0 || (positive && x == 0.0)))
                throw new ArgumentException($"drawing settings, element {first}: {name} {x.ToString(CultureInfo.InvariantCulture)} " +
                                            $"must be {(positive ? "more than" : "at least")} zero");
        }
        Size(own.NdTolerance, "ndTolerance");
        Size(own.VdTolerancePercent, "vdTolerancePercent");
        Size(own.Diameter, "diameter", positive: true);
        foreach (var (s, name) in new[] { (own.Front, "front"), (own.Back, "back") })
        {
            if (s == null) continue;
            Size(s.PowerFringes, $"{name} powerFringes");
            Size(s.RadiusTolerance, $"{name} radiusTolerance");
            Size(s.IrregularityFringes, $"{name} irregularityFringes");
            Size(s.SymmetricIrregularityFringes, $"{name} symmetricIrregularityFringes");
            Size(s.TiltArcmin, $"{name} tiltArcmin");
            Size(s.DecentreMm, $"{name} decentreMm");
            Size(s.ClearAperture, $"{name} clearAperture", positive: true);
        }
        foreach (var (v, name) in new[] { (own.ThicknessMinus, "thicknessMinus"), (own.ThicknessPlus, "thicknessPlus"),
                                          (own.DiameterMinus, "diameterMinus"), (own.DiameterPlus, "diameterPlus") })
            if (v is double x && !double.IsFinite(x))
                throw new ArgumentException($"drawing settings, element {first}: {name} is not a number");
        if (own.ThicknessMinus is double lo && own.ThicknessPlus is double hi && lo > hi)
            throw new ArgumentException($"drawing settings, element {first}: thicknessMinus {lo} is above thicknessPlus {hi}");
        if (own.DiameterMinus is double dlo && own.DiameterPlus is double dhi && dlo > dhi)
            throw new ArgumentException($"drawing settings, element {first}: diameterMinus {dlo} is above diameterPlus {dhi}");
    }

    /// <summary>The drawing with the settings' title, description, notes and coating specifications; its number is set already.</summary>
    private static ElementDrawing Customised(ElementDrawing d, DrawingSettings settings, ElementOverride own)
    {
        var coatings = new[] { d.Front.Coating, d.Back.Coating }.OfType<string>().Distinct()
                           .Where(settings.CoatingSpecs.ContainsKey).Select(c => (c, settings.CoatingSpecs[c])).ToList();
        return d with
        {
            Title = string.IsNullOrWhiteSpace(own.Title) ? d.Title : own.Title.Trim(),
            Description = own.Description?.Trim() ?? "",
            Notes = settings.Notes.Concat(own.Notes).Where(n => !string.IsNullOrWhiteSpace(n)).ToList(),
            CoatingNotes = coatings,
        };
    }

    private static ElementDrawing Element(NominalLens lens, IReadOnlyList<ToleranceItem> items, DrawingSettings settings,
                                          int first, int last, string number, double testWavelengthUm,
                                          IReadOnlyList<Adjustment> adjustments, ElementOverride own, double? forcedDiameter = null)
    {
        var warnings = new List<string>();
        var system = lens.System;
        T? Find<T>(Func<T, bool> match) where T : Tolerance =>
            items.Select(i => i.Tolerance).OfType<T>().FirstOrDefault(match);
        ToleranceItem? Item(Tolerance? t) => t == null ? null : items.First(i => ReferenceEquals(i.Tolerance, t));

        // Fringes are stated at one test wavelength: the first power tolerance's, else the default.
        double wavelengthUm = items.Select(i => i.Tolerance).OfType<PowerFringes>().Select(p => p.TestWavelengthUm)
                                   .DefaultIfEmpty(testWavelengthUm).First();

        SurfaceSpec Surface(int s, bool cemented, SurfaceOverride? given)
        {
            given ??= new SurfaceOverride();
            var surface = system.Surfaces[s];
            double sd = surface.SemiDiameter > 0.0
                ? surface.SemiDiameter
                : Math.Abs(At(lens.Paraxial.MarginalRay, s)) + Math.Abs(At(lens.Paraxial.ChiefRay, s));
            if (given.ClearAperture is double ca) sd = ca / 2.0;
            var power = Item(Find<PowerFringes>(p => p.Surface == s));
            double fringes;
            if (given.PowerFringes is double givenFringes) fringes = givenFringes;
            else if (power == null)
            {
                fringes = settings.DefaultFringes;
                if (given.RadiusTolerance == null)
                    warnings.Add($"surface {s}: power not toleranced; {fringes:G3} fringe{(fringes == 1.0 ? "" : "s")} used");
            }
            else
            {
                // Stated at the drawing's one test wavelength: the sag error is the same, the fringes scale.
                double atOwn = Math.Max(Math.Abs(power.Min), Math.Abs(power.Max));
                fringes = atOwn * ((PowerFringes)power.Tolerance).TestWavelengthUm / wavelengthUm;
            }
            var irregular = Item(Find<SurfaceIrregularity>(p => p.Surface == s));
            double irregularity;
            if (given.IrregularityFringes is double givenIrregularity) irregularity = givenIrregularity;
            else if (irregular == null)
            {
                irregularity = settings.DefaultIrregularity;
                warnings.Add($"surface {s}: irregularity not toleranced; {irregularity:G3} fringe{(irregularity == 1.0 ? "" : "s")} used");
            }
            else
                irregularity = Math.Max(Math.Abs(irregular.Min), Math.Abs(irregular.Max))
                               * ((SurfaceIrregularity)irregular.Tolerance).TestWavelengthUm / wavelengthUm;

            // More power than a test plate is read to: the radius tolerance those fringes allow,
            // either way - the smaller, rounded down, so the drawing asks no more than the budget.
            double? radiusTolerance = null;
            if (given.RadiusTolerance is double givenRadius)
            {
                if (surface.Curvature != 0.0) radiusTolerance = givenRadius;
                else warnings.Add($"surface {s}: a radius tolerance on a plane is not drawn; its power is");
            }
            else if (fringes > settings.MaximumPowerFringes && surface.Curvature != 0.0)
            {
                double RadiusAt(double f)
                {
                    var p = lens.Perturb();
                    new PowerFringes(s, wavelengthUm).Apply(p, f);
                    return p.System.Surfaces[s].Radius;
                }
                double dr = Math.Min(Math.Abs(RadiusAt(fringes) - surface.Radius), Math.Abs(RadiusAt(-fringes) - surface.Radius));
                radiusTolerance = Allocator.RoundDown(dr);
            }
            var tilts = items.Where(i => i.Tolerance is SurfaceTilt t && t.Surface == s).ToList();
            double tilt;
            if (given.TiltArcmin is double givenTilt) tilt = givenTilt;
            else if (tilts.Count == 0)
            {
                tilt = settings.DefaultTiltArcmin;
                warnings.Add($"surface {s}: tilt not toleranced; {tilt:G3}' used");
            }
            else tilt = tilts.Max(i => Math.Max(Math.Abs(i.Min), Math.Abs(i.Max))) * 60.0;
            var decentres = items.Where(i => i.Tolerance is SurfaceDecentre d && d.Surface == s).ToList();
            double? decentre = given.DecentreMm
                               ?? (decentres.Count == 0 ? null : decentres.Max(i => Math.Max(Math.Abs(i.Min), Math.Abs(i.Max))));
            var conic = Item(Find<ConicChange>(k => k.Surface == s));
            var polynomial = surface.Type == SurfaceType.EvenAsphere ? surface.AsphericCoefficients : Array.Empty<double>();
            bool aspheric = surface.Conic != 0.0 || polynomial.Any(a => a != 0.0);
            if (conic != null && !aspheric)
                warnings.Add($"surface {s}: a conic tolerance on a sphere is not drawn");
            var symmetric = Item(Find<SymmetricIrregularity>(p => p.Surface == s));
            double? rsi = given.SymmetricIrregularityFringes
                          ?? (symmetric == null ? null
                              : Math.Max(Math.Abs(symmetric.Min), Math.Abs(symmetric.Max)) * ((SymmetricIrregularity)symmetric.Tolerance).TestWavelengthUm / wavelengthUm);
            if (rsi > irregularity)
                warnings.Add($"surface {s}: rotationally invariant irregularity {rsi:G3} fringes is more than the irregularity, " +
                             $"{irregularity:G3}: ISO 10110-5's C is a part of B");
            var sagTable = aspheric ? SagTable(lens, s, sd, wavelengthUm, fringes, irregularity, rsi ?? 0.0, radiusTolerance) : Array.Empty<SagRow>();
            if (settings.AsphereSlopeMrad is double slope && sagTable.Length > 0 && sagTable.Max(r => r.DeltaSlopeMrad) > slope)
                warnings.Add($"surface {s}: a slope deviation of {slope:G3} mrad is tighter than the {sagTable.Max(r => r.DeltaSlopeMrad):G3} mrad " +
                             "its power and irregularity tolerances allow");
            string? coating = cemented ? null : settings.Coatings.TryGetValue(s, out var c) ? c : settings.Coating;
            if (!string.IsNullOrWhiteSpace(given.Coating))
                coating = given.Coating.Trim().Equals("none", StringComparison.OrdinalIgnoreCase) ? null : given.Coating.Trim();
            return new SurfaceSpec(s, surface.Radius, 2.0 * sd, surface.Conic, polynomial.ToArray(),
                                   fringes, irregularity, tilt, decentre, coating, cemented)
            {
                RadiusTolerance = radiusTolerance,
                ConicTolerance = conic != null && aspheric ? (conic.Min, conic.Max) : null,
                SymmetricIrregularityFringes = rsi,
                SagTable = sagTable,
            };
        }

        var front = Surface(first, lens.IsGlass(first - 1), own.Front);
        var back = Surface(last, lens.IsGlass(last), own.Back);

        var medium = system.Surfaces[first];
        double nd = lens.Nd[first], vd = (nd - 1.0) / (lens.NF[first] - lens.NC[first]);
        string glassName;
        string? catalog = null;
        if (!string.IsNullOrWhiteSpace(own.Glass))
        {
            glassName = own.Glass.Trim();
            catalog = CatalogOf(lens, glassName);
        }
        else if (medium.ModelIndexEnabled)
        {
            glassName = "Model glass";
            warnings.Add("the glass is a model, n_d and ν_d only: name a glass in the element's settings");
        }
        else
        {
            glassName = string.IsNullOrWhiteSpace(medium.Material) ? "?" : medium.Material;
            catalog = CatalogOf(lens, glassName);
        }
        var index = Item(Find<IndexChange>(t => t.Surface == first));
        var abbe = Item(Find<AbbeChange>(t => t.Surface == first));
        if (index == null && own.NdTolerance == null) warnings.Add($"index not toleranced; ±{settings.DefaultIndex:G3} used");
        if (abbe == null && own.VdTolerancePercent == null) warnings.Add($"Abbe number not toleranced; ±{settings.DefaultAbbePercent:G3} % used");
        var material = new MaterialSpec(glassName, catalog, nd, vd,
                                        own.NdTolerance ?? (index == null ? settings.DefaultIndex : Math.Max(Math.Abs(index.Min), Math.Abs(index.Max))),
                                        own.VdTolerancePercent ?? (abbe == null ? settings.DefaultAbbePercent : Math.Max(Math.Abs(abbe.Min), Math.Abs(abbe.Max)) / vd * 100.0));

        double ct = medium.Thickness;
        var thickness = Item(Find<ThicknessChange>(t => t.Surface == first));
        double ctMinus, ctPlus;
        if (thickness == null)
        {
            ctMinus = -settings.DefaultThickness;
            ctPlus = settings.DefaultThickness;
            if (own.ThicknessMinus == null || own.ThicknessPlus == null)
                warnings.Add($"center thickness not toleranced; ±{settings.DefaultThickness:G3} mm used");
        }
        else (ctMinus, ctPlus) = (thickness.Min, thickness.Max);
        ctMinus = own.ThicknessMinus ?? ctMinus;
        ctPlus = own.ThicknessPlus ?? ctPlus;
        if (ctMinus > ctPlus)
            warnings.Add($"center thickness {ctMinus:+0.###;-0.###}/{ctPlus:+0.###;-0.###}: the lower deviation is above the upper");

        double aperture = Math.Max(front.ClearAperture, back.ClearAperture);
        forcedDiameter ??= own.Diameter;
        double diameter = forcedDiameter ?? Math.Ceiling((aperture + settings.EdgeMargin) * 2.0) / 2.0;
        if (own.Diameter is double givenDiameter && givenDiameter < aperture)
            warnings.Add($"edge diameter Ø{givenDiameter:G4} is less than the clear aperture Ø{aperture:G4}");
        // A steep surface ends where its sag does - a hemisphere of radius R is no wider than 2R - and
        // a deep concave face under a wide element (a fisheye's front meniscus) cannot reach the edge.
        // Such a surface is made with a flat land from its clear aperture out to the edge.
        SurfaceSpec Land(SurfaceSpec s)
        {
            double reach = HeightLimit(s);
            if (0.999 * reach >= diameter / 2.0) return s;
            double from = s.ClearAperture;
            if (from / 2.0 >= 0.999 * reach)
            {
                from = Math.Floor(2.0 * 0.999 * reach * 100.0) / 100.0;
                warnings.Add($"surface {s.Surface}: its clear aperture Ø{s.ClearAperture:G4} is wider than the surface (rim at Ø{2 * reach:G4})");
            }
            return s with { LandFrom = from };
        }
        front = Land(front);
        back = Land(back);
        double Edge(double d) => ct - ElementDrawing.Sag(front, d / 2.0) + ElementDrawing.Sag(back, d / 2.0);
        double edge = Edge(diameter);
        if (forcedDiameter == null && edge < 0.5 && diameter > aperture)
        {
            // The margin took the edge past where the surfaces meet: back towards the clear aperture,
            // by tenths, to the widest edge at least half a millimetre thick.
            double d = diameter;
            while (d - 0.1 >= aperture && !(Edge(d) >= 0.5)) d = Math.Round(d - 0.1, 6);
            if (!(Edge(d) >= 0.5)) d = aperture;
            warnings.Add($"edge diameter Ø{diameter:G4} cut to Ø{d:G4}: the edge was {edge:F2} mm thick there");
            diameter = d;
            edge = Edge(diameter);
        }
        if (!double.IsFinite(edge)) warnings.Add($"a surface does not reach the edge diameter Ø{diameter:G4}");
        else if (edge < 1.0) warnings.Add($"edge thickness {edge:F2} mm: thin to make");
        // A 45° bevel of face width w takes w/√2 off the edge; a cemented face has none.
        int bevels = (front.Cemented ? 0 : 1) + (back.Cemented ? 0 : 1);
        double widest = settings.Chamfer(diameter).Max;
        if (double.IsFinite(edge) && bevels > 0 && bevels * widest / Math.Sqrt(2.0) >= edge)
            warnings.Add($"bevels up to {widest:G3} mm wide would take the whole {edge:F2} mm edge");
        if (ct + ctMinus <= 0.0) warnings.Add($"center thickness {ct:G4} {ctMinus:+0.###;-0.###} mm goes to nothing: the tolerance is larger than the glass");
        else if (ct < 0.5) warnings.Add($"center thickness {ct:G4} mm: thin to make");

        // An element or group aligned at assembly is set there, not held to a mount tolerance: its
        // decentre or tilt is the adjustment, stated as such, with its travel when one was given.
        var adjusted = adjustments
            .Select(a => a switch
            {
                ElementDecentreAdjustment d => (Kind: "decenter", d.First, d.Last, a.Travel, Units: " mm"),
                ElementTiltAdjustment t => (Kind: "tilt", t.First, t.Last, a.Travel, Units: "°"),
                _ => default,
            })
            .Where(a => a.Kind != null && a.First <= first && a.Last >= last)
            .GroupBy(a => (a.Kind, a.First, a.Last))
            .Select(g => (g.Key.Kind, g.Key.First, g.Key.Last, Travel: g.Select(a => a.Travel).Max(), g.First().Units))
            .ToList();
        bool Adjusted(Tolerance t) => adjusted.Any(a => a.First == t.Surface && a.Last == Last(t)
                                                       && a.Kind == (t is ElementDecentre ? "decenter" : "tilt"));

        var mount = items.Where(i => i.Tolerance is ElementDecentre or ElementTilt && i.Tolerance.Surface <= first
                                     && Last(i.Tolerance) >= last && !Adjusted(i.Tolerance))
                         .Select(i => $"{(i.Tolerance is ElementDecentre ? "decenter" : "tilt")} {Axis(i.Tolerance)} " +
                                      $"±{Math.Max(Math.Abs(i.Min), Math.Abs(i.Max)):G3}{(i.Tolerance is ElementTilt ? "°" : " mm")}" +
                                      (ToleranceSpec.Pivot(i.Tolerance) is double p
                                          ? $" about the axis {Math.Abs(p):G4} mm {(p > 0 ? "after" : "before")} surface {i.Tolerance.Surface}" : "") +
                                      (Last(i.Tolerance) - i.Tolerance.Surface > 1 ? $" (group {ToleranceSpec.Surfaces(i.Tolerance)})" : ""))
                         .ToList();

        mount.AddRange(adjusted.Select(a => $"{a.Kind} x and y set at assembly to align the lens" +
                                            (a.Travel is double t ? $", within ±{t:G3}{a.Units}" : "") +
                                            (a.Last - a.First > 1 ? $" (group {a.First}-{a.Last})" : "")));

        string title = $"LENS {first}-{last}" + (front.Cemented || back.Cemented ? " (CEMENTED)" : "");
        return new ElementDrawing(number, title, first, material, front, back, ct, ctMinus, ctPlus,
                                  diameter, own.DiameterMinus ?? -settings.DiameterTolerance, own.DiameterPlus ?? 0.0,
                                  wavelengthUm * 1000.0, edge, mount, warnings);
    }

    /// <summary>The catalog a glass is found in, in the lens's own catalogs first; null when none has it.</summary>
    private static string? CatalogOf(NominalLens lens, string name)
    {
        var glass = lens.Glass.GetGlass(name, lens.System.GlassCatalogs);
        return string.IsNullOrWhiteSpace(glass?.Catalog) ? null : glass.Catalog;
    }

    /// <summary>A paraxial ray's height at surface <paramref name="s"/>, 0 where the trace has none.</summary>
    private static double At(IReadOnlyList<LensHH.Core.RayTrace.ParaxialRayData> ray, int s) => s < ray.Count ? ray[s].Y : 0.0;

    /// <summary>The height beyond which a conic surface has no sag: 1/(|c|√(1 + k)), unbounded for a plane or a hyperbola.</summary>
    private static double HeightLimit(SurfaceSpec s)
    {
        double c = s.IsPlane ? 0.0 : Math.Abs(1.0 / s.Radius), k1 = 1.0 + s.Conic;
        return c == 0.0 || k1 <= 0.0 ? double.PositiveInfinity : 1.0 / (c * Math.Sqrt(k1));
    }

    /// <summary>The most rows a sag table is given, the vertex and the clear aperture's edge included.</summary>
    public const int SagRows = 11;

    /// <summary>
    /// The sag table of surface <paramref name="s"/> out to its clear semi-aperture <paramref name="sd"/>:
    /// at each height the sag, and as Δz the most the form tolerances, each at its full size, move
    /// it together - the shapes the analysis traced, so the table asks nothing it did not:
    /// <list type="bullet">
    /// <item>power: the change the curvature for ± the fringes makes, through the full aspheric sag - or, where
    /// the drawing states power as a radius tolerance, the change ± that radius makes;</item>
    /// <item>irregularity (TIRR): its astigmatic half at the azimuth where it is largest, and its spherical half;</item>
    /// <item>rotationally invariant irregularity (TRSI): its 6ρ⁴ − 6ρ².</item>
    /// </list>
    /// Fringes are at <paramref name="wavelengthUm"/>, one fringe λ/2 of sag.
    /// </summary>
    internal static SagRow[] SagTable(NominalLens lens, int s, double sd, double wavelengthUm,
                                      double powerFringes, double irregularityFringes, double symmetricFringes,
                                      double? radiusTolerance = null)
    {
        var surface = lens.System.Surfaces[s];
        // Power as the drawing states it: the radius tolerance when there is one, which is rounded
        // down from the fringes, else the fringes.
        LensHH.Core.Models.Surface Powered(int sign)
        {
            var p = lens.Perturb();
            if (radiusTolerance is double dr) p.System.Surfaces[s].Curvature = 1.0 / (surface.Radius + sign * dr);
            else new PowerFringes(s, wavelengthUm).Apply(p, sign * powerFringes);
            return p.System.Surfaces[s];
        }
        double Sag(LensHH.Core.Models.Surface on, double h) => Tolerance.Sag(on, h, on.Curvature);
        var (plus, minus) = (Powered(1), Powered(-1));
        double lambda = wavelengthUm * 1e-3, step = 1e-4 * sd;
        return Heights(sd).Select(h =>
        {
            double z = Sag(surface, h), rho2 = h * h / (sd * sd);
            double power = Math.Max(Math.Abs(Sag(plus, h) - z), Math.Abs(Sag(minus, h) - z));
            double zonal = Math.Abs(SymmetricIrregularity.Shape(h, sd));            // per unit peak to valley
            double dz = power + irregularityFringes * lambda / 8.0 * rho2
                      + (irregularityFringes * lambda / 4.0 + symmetricFringes * lambda / 2.0) * zonal;

            // The slope of each, along the radius, where all are steepest together. The astigmatic
            // half's gradient is 2c ρ/R at every azimuth, all of it radial on the x and y axes.
            double PowerSlope(LensHH.Core.Models.Surface p) =>
                ((Sag(p, h + step) - Sag(surface, h + step)) - (Sag(p, h - step) - Sag(surface, h - step))) / (2.0 * step);
            double ds = Math.Max(Math.Abs(PowerSlope(plus)), Math.Abs(PowerSlope(minus)))
                      + 2.0 * irregularityFringes * lambda / 8.0 * h / (sd * sd)
                      + (irregularityFringes * lambda / 4.0 + symmetricFringes * lambda / 2.0) * Math.Abs(SymmetricIrregularity.Slope(h, sd));
            return new SagRow(h, z, dz * 1000.0) { DeltaSlopeMrad = ds * 1000.0 };
        }).ToArray();
    }

    /// <summary>
    /// Heights from the vertex in a round step - 1, 2, 2.5 or 5 times a power of ten - fine enough
    /// for <see cref="SagRows"/> rows, and the clear semi-aperture as the last.
    /// </summary>
    internal static List<double> Heights(double sd)
    {
        double least = sd / (SagRows - 2), decade = Math.Pow(10.0, Math.Floor(Math.Log10(least)));
        double step = new[] { 1.0, 2.0, 2.5, 5.0, 10.0 }.Select(m => m * decade).First(v => v >= least * (1.0 - 1e-12));
        var heights = new List<double>();
        for (int k = 0; k * step < sd * (1.0 - 1e-9); k++) heights.Add(k * step);
        heights.Add(sd);
        return heights;
    }

    private static int Last(Tolerance t) => t switch { ElementDecentre e => e.Last, ElementTilt e => e.Last, _ => t.Surface };

    private static string Axis(Tolerance t) => t is IAxisTolerance a ? a.Axis.ToString().ToLowerInvariant() : "";
}
