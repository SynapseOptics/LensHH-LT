using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using LensHH.Core.Enums;
using LensHH.Core.Models;
using ModelContextProtocol.Server;

namespace LensHH.Mcp.Tools
{
    [McpServerToolType]
    public class SurfaceTools
    {
        private readonly McpSession _session;
        public SurfaceTools(McpSession session) => _session = session;

        [McpServerTool, Description("Add a surface to the optical system. insertIndex is where to insert (before image surface by default). radius is in mm (use 1e18 for flat). thickness in mm. material is glass name or empty for air. conic defaults to 0.")]
        public string AddSurface(double radius, double thickness, string material = "", double conic = 0, int insertIndex = -1)
        {
            var sys = _session.System;
            var surf = new Surface
            {
                Radius = radius,
                Thickness = thickness,
                Material = string.IsNullOrWhiteSpace(material) ? null : material,
                Conic = conic
            };

            if (insertIndex < 0)
                insertIndex = sys.Surfaces.Count > 0 ? sys.Surfaces.Count - 1 : sys.Surfaces.Count;

            // If inserting inside a glass element with default radius, copy exit
            // surface shape (standard sequential-design convention).
            if (radius == 1e18 && string.IsNullOrWhiteSpace(material)
                && insertIndex > 0 && insertIndex < sys.Surfaces.Count)
            {
                var prevSurf = sys.Surfaces[insertIndex - 1];
                if (!string.IsNullOrEmpty(prevSurf.Material))
                {
                    var exitSurf = sys.Surfaces[insertIndex];
                    surf.Radius = exitSurf.Radius;
                    surf.Conic = exitSurf.Conic;
                    surf.Type = exitSurf.Type;
                    if (exitSurf.AsphericCoefficients != null)
                        surf.AsphericCoefficients = (double[])exitSurf.AsphericCoefficients.Clone();
                }
            }

            sys.Surfaces.Insert(insertIndex, surf);

            // Reindex
            for (int i = 0; i < sys.Surfaces.Count; i++)
                sys.Surfaces[i].Index = i;

            // Update surface references in merit function, config editor, pickups
            SurfaceIndexUpdater.OnSurfaceInserted(insertIndex, sys,
                _session.MeritFunction, _session.ConfigEditor);

            return $"Surface added at index {insertIndex}. System now has {sys.Surfaces.Count} surfaces.";
        }

        [McpServerTool, Description(
            "Insert a complete singlet (one glass element) after a given surface. Adds TWO surfaces — glass front and glass back — with every invariant the engine relies on set correctly: "
            + "SemiDiameter (defaults to 12.5 mm = Ø25, override via semiDiameter), SemiDiameterMode=Auto, Material set on the front surface and explicitly empty-string on the back surface (so the JSON serializes consistently and a reload doesn't produce null materials that crash ray tracing), and both curvatures + thicknesses marked as optimization variables by default. "
            + "frontRadius / backRadius in mm (use 1e18 for plano). glassThickness is the center thickness of the glass. material is the glass name (required, no empty allowed). airThicknessAfter is the air gap from the singlet's back surface to whatever comes next (default 5 mm). markCurvaturesVariable / markThicknessesVariable default true. The surface BEFORE afterSurface keeps its existing thickness as the leading air gap to the new singlet — adjust separately if needed. "
            + "Returns the indices of the inserted front and back surfaces.")]
        public string AddSinglet(
            int afterSurface,
            double frontRadius,
            double backRadius,
            double glassThickness,
            string material,
            double semiDiameter = 12.5,
            double airThicknessAfter = 5.0,
            bool markCurvaturesVariable = true,
            bool markThicknessesVariable = true,
            double frontConic = 0,
            double backConic = 0)
        {
            var (frontIdx, backIdx, err) = InsertSingletCore(
                afterSurface, frontRadius, backRadius, glassThickness, material,
                semiDiameter, airThicknessAfter, markCurvaturesVariable,
                markThicknessesVariable, frontConic, backConic);
            if (err != null) return err;
            return $"Singlet inserted at surfaces [{frontIdx}-{backIdx}]: "
                 + $"front R={frontRadius} t={glassThickness} {material}, "
                 + $"back R={backRadius} air={airThicknessAfter}. "
                 + $"SemiDiameter={semiDiameter} (Auto mode). "
                 + $"Variables: C={markCurvaturesVariable} T={markThicknessesVariable}. "
                 + $"System now has {_session.System.Surfaces.Count} surfaces.";
        }

        /// <summary>
        /// Shared singlet-insertion core. AddSinglet (MCP tool) and BuildSkeleton
        /// both go through this so every singlet in the system has the same set
        /// of invariants honored: SemiDiameter > 0 with Auto mode, Material on the
        /// glass surface and explicit empty-string on the air surface, variable
        /// flags set, SurfaceIndexUpdater fired after each splice. Returns the
        /// (frontIdx, backIdx) of the new surfaces and a null error string on
        /// success, or a populated error string on failure (caller short-circuits).
        /// </summary>
        internal (int frontIdx, int backIdx, string? error) InsertSingletCore(
            int afterSurface,
            double frontRadius,
            double backRadius,
            double glassThickness,
            string material,
            double semiDiameter,
            double airThicknessAfter,
            bool markCurvaturesVariable,
            bool markThicknessesVariable,
            double frontConic = 0,
            double backConic = 0)
        {
            // ── Input validation ────────────────────────────────────────────────
            // Catch every "agent forgot to specify X" failure mode at the boundary
            // so the engine sees a fully-formed pair of surfaces every time.
            if (string.IsNullOrWhiteSpace(material))
                return (-1, -1, "AddSinglet/BuildSkeleton error: material is required (use a glass name like 'N-BK7'; empty/whitespace not allowed for the glass surface).");
            if (semiDiameter <= 0)
                return (-1, -1, $"AddSinglet/BuildSkeleton error: semiDiameter must be positive (got {semiDiameter}).");
            if (glassThickness <= 0)
                return (-1, -1, $"AddSinglet/BuildSkeleton error: glassThickness must be positive (got {glassThickness}).");

            var sys = _session.System;
            if (afterSurface < 0 || afterSurface >= sys.Surfaces.Count - 1)
                return (-1, -1, $"AddSinglet/BuildSkeleton error: afterSurface={afterSurface} out of range (must be 0..{sys.Surfaces.Count - 2}; can't insert after image surface).");

            // ── Build the two surfaces with every required field set ──────────
            // KEY INVARIANTS this helper enforces (vs raw add_surface):
            //   * SemiDiameter > 0       — zero crashes the ray-trace null path
            //   * SemiDiameterMode=Auto  — lets paraxial size the actual rim later
            //   * back.Material = ""    — empty string, NOT null. AddSurface's
            //                              IsNullOrWhitespace-to-null mapping
            //                              causes the JSON serializer to skip
            //                              the field entirely, and the next load
            //                              reads it back as null which trips
            //                              MaterialIsAir checks elsewhere.
            //   * Variable flags set    — caller will optimize; defaults reflect
            //                              the typical "vary everything" intent.
            var front = new Surface
            {
                Radius              = frontRadius,
                Thickness           = glassThickness,
                Material            = material,
                Conic               = frontConic,
                SemiDiameter        = semiDiameter,
                SemiDiameterMode    = SemiDiameterMode.Auto,
                CurvatureVariable   = markCurvaturesVariable,
                ThicknessVariable   = markThicknessesVariable,
            };
            var back = new Surface
            {
                Radius              = backRadius,
                Thickness           = airThicknessAfter,
                Material            = "",   // explicit empty string — see invariant above
                Conic               = backConic,
                SemiDiameter        = semiDiameter,
                SemiDiameterMode    = SemiDiameterMode.Auto,
                CurvatureVariable   = markCurvaturesVariable,
                ThicknessVariable   = markThicknessesVariable,
            };

            // Splice both surfaces in, then fire SurfaceIndexUpdater after each so
            // operand spans, pickups, glass-substitution settings track correctly.
            int frontIdx = afterSurface + 1;
            int backIdx  = frontIdx + 1;
            sys.Surfaces.Insert(frontIdx, front);
            SurfaceIndexUpdater.OnSurfaceInserted(frontIdx, sys,
                _session.MeritFunction, _session.ConfigEditor);
            sys.Surfaces.Insert(backIdx, back);
            SurfaceIndexUpdater.OnSurfaceInserted(backIdx, sys,
                _session.MeritFunction, _session.ConfigEditor);

            // Reindex
            for (int i = 0; i < sys.Surfaces.Count; i++)
                sys.Surfaces[i].Index = i;

            return (frontIdx, backIdx, null);
        }

        // ── Helper #3: build a complete skeleton from a high-level spec ─────

        [McpServerTool, Description(
            "Build a complete lens skeleton (multiple elements + physical aperture stop + glass substitution) on the CURRENT system in one call. Expects the host to already have the merit function, fields, wavelengths, and stop set up; this helper drops the template's dummy stop, inserts the lens elements, then inserts a REAL flat aperture-stop surface in the chosen air gap with both surrounding thicknesses bounded variables — physically realizable from the start.\n\n"
            + "Architecture (currently supported):\n"
            + "  • 'single-single-single' (alias 'sss', 'cooke'): three singlets — BK7 / SF11 / BK7, biconvex / biconcave / biconvex. Standard Cooke-triplet starting seed.\n\n"
            + "Parameters:\n"
            + "  • stopPosition (default 1): which air gap to place the stop in. 0 = leading air before L1 (wide-angle convention), 1 = between L1 and L2 (Cooke triplet default, mid-stack), 2 = between L2 and L3, … N (= numElements) = BFL gap after the last element.\n"
            + "  • semiDiameterDefault (default 12.5 mm): seed semi-D for every element. Mode=Auto so the SD solver resizes during optimization.\n"
            + "  • airGap (default 10 mm): element-to-element air gap. For the gap that holds the new stop, this is split half-and-half into leading + trailing air thicknesses (both marked variable).\n"
            + "  • bflSeed (default 45 mm): trailing air gap from the last element to the image. Marked variable.\n"
            + "  • substitutionCatalog (default 'auto'): catalog name for glass substitution on every spherical glass surface. 'auto' picks StockGlassesUV if min(wavelengths) < 0.380 µm, else StockGlassesVisible — the same logic as set_stock_glass_substitution. Pass '' to disable substitution; pass any explicit name (e.g. 'SCHOTT', 'StockGlassesVisible') to override.\n\n"
            + "The helper also rewrites any merit-function span operand (CTA / EA / CTG / EG) with Surface1=-3 to Surface1=1, so the spans cover EVERY refractive surface from L1 onward — including the new stop's leading/trailing air pair and the lens elements that now sit before the stop. (-3 = first surface after stop was correct for the old buried-pupil convention; for the physical-stop convention -3 points mid-stack and would under-constrain the design.)\n\n"
            + "After the call, the system is ready for `multistart_optimize_start` with `glassSubPercent > 0`.")]
        public string BuildSkeleton(
            string architecture = "single-single-single",
            int stopPosition = 1,
            double semiDiameterDefault = 12.5,
            double airGap = 10.0,
            double bflSeed = 45.0,
            string substitutionCatalog = "auto")
        {
            // ── Validate architecture key ─────────────────────────────────────
            var arch = (architecture ?? "").Trim().ToLowerInvariant().Replace('_', '-');
            bool isCooke = arch == "single-single-single" || arch == "sss" || arch == "cooke";
            if (!isCooke)
                return $"BuildSkeleton error: unknown architecture '{architecture}'. Supported: 'single-single-single' (Cooke triplet).";

            int numElements = 3; // single-single-single
            if (stopPosition < 0 || stopPosition > numElements)
                return $"BuildSkeleton error: stopPosition={stopPosition} out of range [0, {numElements}] for {numElements}-element architecture.";

            var sys = _session.System;
            if (sys == null || sys.Surfaces.Count < 3)
                return "BuildSkeleton error: host system must already have OBJ + STOP + IMG (use load_system on a template first).";

            // Refuse to run on a system that already has glass elements — we
            // don't want to silently double-insert if the agent calls this on
            // an already-populated design. Caller should reload the template.
            bool hasExistingGlass = false;
            for (int i = 0; i < sys.Surfaces.Count; i++)
                if (!string.IsNullOrEmpty(sys.Surfaces[i].Material)) { hasExistingGlass = true; break; }
            if (hasExistingGlass)
                return "BuildSkeleton error: system already contains glass elements. Reload a fresh template before calling BuildSkeleton.";

            // ── Drop the template's dummy stop ───────────────────────────────
            // The template was authored with a placeholder stop S1 that the
            // old build_skeleton repurposed as the "entrance pupil offset"
            // variable. The physically-realizable convention uses a REAL flat
            // stop surface inserted into a chosen air gap later — the dummy
            // is no longer needed. We delete it and adjust OBJ.Thickness so
            // the OBJ-to-first-refractive distance is preserved (∞ + finite
            // stays ∞ in IEEE 754, the correct behavior for infinite
            // conjugate).
            int oldStopIdx = sys.StopSurfaceIndex;
            if (oldStopIdx <= 0)
                return $"BuildSkeleton error: stop surface not found in template (stopIdx={oldStopIdx}).";

            double origObjT   = sys.Surfaces[0].Thickness;
            double origDummyT = sys.Surfaces[oldStopIdx].Thickness;
            sys.Surfaces.RemoveAt(oldStopIdx);
            sys.Surfaces[0].Thickness = origObjT + origDummyT;
            for (int i = 0; i < sys.Surfaces.Count; i++) sys.Surfaces[i].Index = i;
            SurfaceIndexUpdater.OnSurfaceRemoved(oldStopIdx, sys, _session.MeritFunction, _session.ConfigEditor);

            var report = new StringBuilder();
            report.AppendLine($"BuildSkeleton: architecture='single-single-single (Cooke)', stopPosition={stopPosition}");
            report.AppendLine($"  Dropped template's dummy stop S{oldStopIdx} (was T={origDummyT}).");

            // ── Cooke triplet seed values ─────────────────────────────────────
            // Element 1: BK7 biconvex,  R=±50,  t=4 mm
            // Element 2: SF11 biconcave, R=∓30, t=3 mm
            // Element 3: BK7 biconvex,  R=±50,  t=4 mm
            // Trailing air after each: airGap (or bflSeed for the last)
            var ops = new (string label, double r1, double r2, double t, string mat, double airAfter, double sd)[]
            {
                ("E1 (positive, BK7)",  +50, -50, 4, "N-BK7",  airGap,  semiDiameterDefault),
                ("E2 (negative, SF11)", -30, +30, 3, "N-SF11", airGap,  semiDiameterDefault),
                ("E3 (positive, BK7)",  +50, -50, 4, "N-BK7",  bflSeed, semiDiameterDefault),
            };

            // Insert all singlets in sequence — no stop yet, the lens stack
            // sits directly after OBJ.
            int afterSurface = 0; // After OBJ
            var glassSurfacesForSub = new List<int>();
            var elementBacks = new List<int>(); // index of each element's back surface after insertion
            foreach (var op in ops)
            {
                var (front, back, err) = InsertSingletCore(
                    afterSurface,
                    op.r1, op.r2, op.t, op.mat,
                    op.sd, op.airAfter,
                    markCurvaturesVariable: true,
                    markThicknessesVariable: true);
                if (err != null) return $"BuildSkeleton error during {op.label}: {err}";
                glassSurfacesForSub.Add(front);
                elementBacks.Add(back);
                afterSurface = back;
                report.AppendLine($"  {op.label}: surfaces [{front}-{back}].");
            }

            // ── Insert the physical stop into the chosen air gap ─────────────
            //   stopPosition = 0: leading air gap (between OBJ and L1 front)
            //   stopPosition = k for 1..N-1: between Lk back and L(k+1) front
            //   stopPosition = N: BFL gap (between LN back and IMG)
            int prevSurfIdx = (stopPosition == 0) ? 0 : elementBacks[stopPosition - 1];
            double origGap = sys.Surfaces[prevSurfIdx].Thickness;

            double leadingAir;
            double trailingAir;
            if (double.IsInfinity(origGap))
            {
                // Leading-gap case with infinite-conjugate object: OBJ.Thickness
                // is ∞. Don't try to split — keep OBJ.T = ∞, put a finite air
                // distance from the new stop to the next surface.
                leadingAir  = origGap;          // ∞ stays ∞
                trailingAir = airGap;           // a sensible finite distance to L1
            }
            else
            {
                leadingAir  = origGap / 2.0;
                trailingAir = origGap / 2.0;
            }

            int stopInsertAt = prevSurfIdx + 1;
            InsertStopInGap(sys, prevSurfIdx, leadingAir, trailingAir);

            // Mark prev surface's thickness variable (so the leading-air can
            // also move on re-opt). InsertStopInGap already marked the new
            // stop's trailing-air variable.
            if (!double.IsInfinity(sys.Surfaces[prevSurfIdx].Thickness))
                sys.Surfaces[prevSurfIdx].ThicknessVariable = true;

            // After the stop insert, glass-surface indices >= stopInsertAt
            // shifted +1.
            for (int i = 0; i < glassSurfacesForSub.Count; i++)
                if (glassSurfacesForSub[i] >= stopInsertAt) glassSurfacesForSub[i]++;

            report.AppendLine($"  Inserted physical stop at S{stopInsertAt} in the air gap after S{prevSurfIdx}: "
                + $"leading air = {leadingAir:F4} mm, trailing air = {trailingAir:F4} mm (both variable).");

            // ── Resolve substitutionCatalog (auto-detect UV vs Visible) ──────
            string resolvedCatalog = substitutionCatalog;
            if (string.Equals(substitutionCatalog?.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
            {
                double minWl = (sys.Wavelengths != null && sys.Wavelengths.Count > 0)
                    ? sys.Wavelengths.Min(w => w.Value) : 0.587;
                resolvedCatalog = minWl < 0.380 ? "StockGlassesUV" : "StockGlassesVisible";
                report.AppendLine($"  substitutionCatalog='auto' resolved to '{resolvedCatalog}' (min wavelength {minWl:F4} um).");
            }

            // ── Enable glass substitution on every glass surface ──────────────
            if (!string.IsNullOrWhiteSpace(resolvedCatalog))
            {
                foreach (var glassSurf in glassSurfacesForSub)
                {
                    sys.GlassSubstitutions.Add(new GlassSubstitutionSetting
                    {
                        SurfaceIndex = glassSurf,
                        Substitute = true,
                        CatalogName = resolvedCatalog
                    });
                }
                report.AppendLine($"  Glass substitution enabled on {glassSurfacesForSub.Count} surface(s) against catalog '{resolvedCatalog}'.");
            }
            else
            {
                report.AppendLine("  Glass substitution disabled (empty substitutionCatalog).");
            }

            // ── Rewrite merit-function span operands ──────────────────────────
            // The template was authored with CTA(-3,-1) / EA(-3,-1) / CTG(-3,-1)
            // / EG(-3,-1). -3 = "first surface after stop". For the OLD buried-
            // pupil convention with stop at S1, -3 = L1f and the span correctly
            // covered all refractive surfaces. For the NEW physical-stop
            // convention the stop sits mid-stack, so -3 points at a later
            // surface and misses the lens elements + air gaps that now sit
            // before the stop. Rewriting Surface1=-3 → -5 (sentinel "first
            // refractive surface index in the new layout) restores full
            // coverage.
            int rewrites = 0;
            if (_session.MeritFunction != null)
            {
                foreach (var op in _session.MeritFunction.Operands)
                {
                    bool isSpan = op.Type == LensHH.Core.MeritFunction.OperandType.CTA
                               || op.Type == LensHH.Core.MeritFunction.OperandType.EA
                               || op.Type == LensHH.Core.MeritFunction.OperandType.CTG
                               || op.Type == LensHH.Core.MeritFunction.OperandType.EG;
                    if (isSpan && op.Surface1 == -3) { op.Surface1 = -5; rewrites++; }
                }
            }
            if (rewrites > 0)
                report.AppendLine($"  Merit-function rewrite: {rewrites} span operand(s) had Surface1=-3 → -5 (covers leading/trailing air around new stop + lenses before stop).");

            // ── Final sanity check: a fresh evaluation should succeed ─────────
            // Catches any invariant we missed (e.g., a null Material slipping
            // through). The merit value itself isn't meaningful at the seed —
            // it's just a "the engine doesn't crash" smoke test.
            try
            {
                if (_session.MeritFunction != null && _session.MeritFunction.Operands.Count > 0)
                {
                    var evaluator = new LensHH.Core.MeritFunction.MeritFunctionEvaluator(
                        sys, _session.GlassCatalog);
                    double seedMerit = evaluator.Evaluate(_session.MeritFunction);
                    report.AppendLine($"  Seed merit (pre-optimize): {seedMerit:E4}.");
                }
            }
            catch (Exception ex)
            {
                return $"BuildSkeleton error: skeleton built but merit evaluation crashed — {ex.Message}";
            }

            report.Append($"System now has {sys.Surfaces.Count} surfaces, ready for multistart_optimize_start.");
            return report.ToString();
        }

        [McpServerTool, Description("Remove a surface by index.")]
        public string RemoveSurface(int surfaceIndex)
        {
            var sys = _session.System;
            if (surfaceIndex < 0 || surfaceIndex >= sys.Surfaces.Count)
                return $"Surface index {surfaceIndex} out of range (0-{sys.Surfaces.Count - 1}).";

            sys.Surfaces.RemoveAt(surfaceIndex);

            // Reindex
            for (int i = 0; i < sys.Surfaces.Count; i++)
                sys.Surfaces[i].Index = i;

            // Update surface references in merit function, config editor, pickups
            SurfaceIndexUpdater.OnSurfaceRemoved(surfaceIndex, sys,
                _session.MeritFunction, _session.ConfigEditor);

            return $"Surface {surfaceIndex} removed. System now has {sys.Surfaces.Count} surfaces.";
        }

        [McpServerTool, Description("Edit a surface property. property can be: radius, thickness, material, conic, semi_diameter, semi_diameter_mode (auto/fixed), inner_radius, obscuration, stop (true/false). value is the new value as a string.")]
        public string EditSurface(int surfaceIndex, string property, string value)
        {
            var sys = _session.System;
            if (surfaceIndex < 0 || surfaceIndex >= sys.Surfaces.Count)
                return $"Surface index {surfaceIndex} out of range.";

            var s = sys.Surfaces[surfaceIndex];
            switch (property.ToLowerInvariant())
            {
                case "radius":
                    if (double.TryParse(value, out double r)) s.Radius = r;
                    else return "Invalid radius value.";
                    break;
                case "thickness":
                    if (double.TryParse(value, out double t)) s.Thickness = t;
                    else return "Invalid thickness value.";
                    break;
                case "material":
                    s.Material = string.IsNullOrWhiteSpace(value) ? null : value;
                    break;
                case "conic":
                    if (double.TryParse(value, out double c)) s.Conic = c;
                    else return "Invalid conic value.";
                    break;
                case "semi_diameter":
                    if (double.TryParse(value, out double sd)) s.SemiDiameter = sd;
                    else return "Invalid semi-diameter value.";
                    break;
                case "semi_diameter_mode":
                    if (value.Equals("auto", StringComparison.OrdinalIgnoreCase))
                        s.SemiDiameterMode = SemiDiameterMode.Auto;
                    else if (value.Equals("fixed", StringComparison.OrdinalIgnoreCase))
                        s.SemiDiameterMode = SemiDiameterMode.Fixed;
                    else
                        return "Use 'auto' or 'fixed'.";
                    break;
                case "inner_radius":
                    if (double.TryParse(value, out double ir)) s.InnerRadius = ir;
                    else return "Invalid inner radius value.";
                    break;
                case "obscuration":
                    if (double.TryParse(value, out double ob)) s.ObscurationRadius = ob;
                    else return "Invalid obscuration value.";
                    break;
                case "stop":
                    bool isStop = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    // Clear other stops first
                    if (isStop)
                        foreach (var surf in sys.Surfaces) surf.IsStop = false;
                    s.IsStop = isStop;
                    break;
                default:
                    return $"Unknown property '{property}'.";
            }
            return $"Surface {surfaceIndex} {property} set to {value}.";
        }

        [McpServerTool, Description("Set even asphere coefficients on a surface. Provide coefficients as comma-separated values for A2,A4,A6,A8,A10,A12,A14,A16 (up to 8 terms).")]
        public string SetAsphere(int surfaceIndex, string coefficients)
        {
            var sys = _session.System;
            if (surfaceIndex < 0 || surfaceIndex >= sys.Surfaces.Count)
                return $"Surface index {surfaceIndex} out of range.";

            var s = sys.Surfaces[surfaceIndex];
            s.Type = SurfaceType.EvenAsphere;

            var parts = coefficients.Split(',');
            for (int i = 0; i < Math.Min(parts.Length, 8); i++)
            {
                if (double.TryParse(parts[i].Trim(), out double val))
                    s.AsphericCoefficients[i] = val;
            }

            return $"Surface {surfaceIndex} set to Even Asphere with {Math.Min(parts.Length, 8)} coefficients.";
        }

        [McpServerTool, Description("Set a surface variable for optimization. property can be: radius, thickness, conic, aspheric (with optional index 0-7). Set variable=true to make variable, false to fix. Optional: min and max bounds for constrained optimization.")]
        public string SetVariable(int surfaceIndex, string property, bool variable, double? min = null, double? max = null)
        {
            var sys = _session.System;
            if (surfaceIndex < 0 || surfaceIndex >= sys.Surfaces.Count)
                return $"Surface index {surfaceIndex} out of range.";

            var s = sys.Surfaces[surfaceIndex];
            switch (property.ToLowerInvariant())
            {
                case "curvature":
                    s.CurvatureVariable = variable;
                    if (min.HasValue) s.CurvatureMin = min.Value;
                    if (max.HasValue) s.CurvatureMax = max.Value;
                    break;
                case "thickness":
                    s.ThicknessVariable = variable;
                    if (min.HasValue) s.ThicknessMin = min.Value;
                    if (max.HasValue) s.ThicknessMax = max.Value;
                    break;
                case "conic":
                    s.ConicVariable = variable;
                    if (min.HasValue) s.ConicMin = min.Value;
                    if (max.HasValue) s.ConicMax = max.Value;
                    break;
                default:
                    if (property.StartsWith("aspheric", StringComparison.OrdinalIgnoreCase))
                    {
                        int idx = 0;
                        if (property.Length > 8)
                            int.TryParse(property.Substring(8), out idx);
                        if (idx >= 0 && idx < 8)
                            s.AsphericVariable[idx] = variable;
                        else
                            return $"Aspheric index {idx} out of range (0-7).";
                    }
                    else
                        return $"Unknown property '{property}'.";
                    break;
            }
            return $"Surface {surfaceIndex} {property} variable={variable}" +
                   (min.HasValue ? $" min={min}" : "") +
                   (max.HasValue ? $" max={max}" : "") + ".";
        }

        [McpServerTool, Description("Get details of a specific surface including all parameters.")]
        public string GetSurface(int surfaceIndex)
        {
            var sys = _session.System;
            if (surfaceIndex < 0 || surfaceIndex >= sys.Surfaces.Count)
                return $"Surface index {surfaceIndex} out of range.";

            var s = sys.Surfaces[surfaceIndex];
            var sb = new StringBuilder();
            sb.AppendLine($"Surface {surfaceIndex}:");
            sb.AppendLine($"  Type: {s.Type}");
            sb.AppendLine($"  Radius: {s.Radius:F6} mm (Curvature: {s.Curvature:E6})");
            sb.AppendLine($"  Thickness: {s.Thickness:F6} mm");
            sb.AppendLine($"  Material: {s.Material ?? "(air)"}");
            sb.AppendLine($"  Semi-Diameter: {s.SemiDiameter:F4} mm");
            sb.AppendLine($"  Conic: {s.Conic:F6}");
            sb.AppendLine($"  Stop: {s.IsStop}");
            sb.AppendLine($"  Variables: C={s.CurvatureVariable} T={s.ThicknessVariable} K={s.ConicVariable}");

            if (s.Type == SurfaceType.EvenAsphere)
            {
                sb.Append("  Aspheric Coefficients:");
                for (int i = 0; i < 8; i++)
                    if (s.AsphericCoefficients[i] != 0)
                        sb.Append($" A{2 * (i + 1)}={s.AsphericCoefficients[i]:E4}");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        // ──────────────────────────────────────────────────────────────────────
        // relocate_stop_scan: scan-and-save physical-stop candidates
        //
        // For a buried-pupil design (sourcePath has S1.Thickness < 0), this
        // builds 3 candidate alternative designs PER internal air gap — one
        // each near the front, middle, and back of the gap — with the dummy
        // S1 dropped and a real flat aperture-stop surface inserted into the
        // chosen air gap. Each candidate is saved as its own .lhlt the user
        // can open in the GUI, and the engine-computed entrance-pupil position
        // (via SystemDataCalculator) is reported in the response table.
        //
        // Goal: the user can compare each candidate's reported entrance pupil
        // against the original target (= -S1.Thickness, relative to the first
        // refractive surface). If a candidate matches the target, that's the
        // physical-stop design with the same chief-ray geometry. If no
        // candidate matches, the table reveals which gap's range BRACKETS the
        // target — the user can then re-optimize that candidate with the
        // stop's surrounding air-thicknesses bounded variables.
        //
        // Caveats: merit-function operands using sentinel surface refs
        // (-1/-2/-3/-4) auto-track the new indexing; absolute refs like
        // Surface=3 will point at a different surface in the candidate and
        // should be reviewed before use. Glass-substitution settings and
        // pickups are dropped (their indices are stale after reorder).

        [McpServerTool, Description(
            "For a buried-pupil source design (S1.Thickness < 0), build 3 alternative-stop-position candidate designs PER internal air gap (start, middle, end of each gap), each with the dummy S1 removed and a real flat IsStop surface inserted into the chosen gap. Saves each candidate as a .lhlt under outputDir and reports the engine-computed entrance pupil position for each in a table.\n\n"
            + "Goal: the user can verify in the GUI which candidate (if any) puts the entrance pupil exactly where the original (buried) one was. Target = -S1.Thickness, relative to the first refractive surface. If no candidate matches exactly, the table shows which gap's start/end BRACKET the target — load that candidate and re-optimize with the stop's leading and trailing air-thicknesses set as bounded variables.\n\n"
            + "If S1.Thickness >= 0 in the source, no buried pupil exists; the helper just copies the source unchanged to outputDir and returns.\n\n"
            + "Internal air gaps only: leading (before first lens) and BFL (after last lens) gaps are skipped — physical stops belong between lens elements.\n\n"
            + "Per-surface glass-substitution settings ARE copied to candidates with SurfaceIndex re-mapped for the dummy-drop and stop-insert. The new stop surface is marked ThicknessVariable so re-optimization can slide it within the bounded air gap.\n\n"
            + "Caveats: merit-function operands referencing surfaces by sentinel (-1/-2/-3/-4) auto-track; absolute Surface=k refs may now point at a different surface. Pickups are not copied.")]
        public string RelocateStopScan(string sourcePath, string outputDir)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !System.IO.File.Exists(sourcePath))
                return $"RelocateStopScan error: sourcePath '{sourcePath}' does not exist.";
            if (string.IsNullOrWhiteSpace(outputDir))
                return "RelocateStopScan error: outputDir is required.";

            // 1. Load source
            LensHH.Core.IO.LhltReadResult src;
            try { src = LensHH.Core.IO.LhltReader.Read(sourcePath); }
            catch (Exception ex) { return $"RelocateStopScan error reading source: {ex.Message}"; }

            var srcSys = src.System;
            int n = srcSys.Surfaces.Count;
            if (n < 4) return $"RelocateStopScan error: source has {n} surfaces (need OBJ + dummy + at least one lens + IMG).";

            int oldStopIdx = srcSys.StopSurfaceIndex;
            if (oldStopIdx < 1 || oldStopIdx >= n - 1)
                return $"RelocateStopScan error: stop surface index {oldStopIdx} is OBJ or IMG; nothing to relocate.";

            double oldStopT = srcSys.Surfaces[oldStopIdx].Thickness;

            try { System.IO.Directory.CreateDirectory(outputDir); }
            catch (Exception ex) { return $"RelocateStopScan error creating outputDir: {ex.Message}"; }

            // 2. Not buried? Just copy source unchanged.
            if (oldStopT >= -1e-9)
            {
                var copyName = System.IO.Path.GetFileName(sourcePath);
                var copyPath = System.IO.Path.Combine(outputDir, copyName);
                try { System.IO.File.Copy(sourcePath, copyPath, overwrite: true); }
                catch (Exception ex) { return $"RelocateStopScan error during copy: {ex.Message}"; }
                return $"S{oldStopIdx}.Thickness = {oldStopT:F4} >= 0 (not buried). Source copied unchanged to {copyPath}.";
            }

            // 3. Build the base system (dummy S1 dropped). OBJ thickness is
            //    adjusted so the OBJ→(was S2) distance equals the original
            //    OBJ→S2 distance (∞ + negative = ∞ in IEEE 754, which is what
            //    we want for infinite conjugate; for finite conjugate this
            //    gives the right finite sum).
            var baseSys = BuildBaseSystemWithoutDummyStop(srcSys, oldStopIdx);

            // 4. Compute physical z for each surface in the base system,
            //    anchoring the first refractive surface (new S1) at z=0.
            int baseN = baseSys.Surfaces.Count;
            double[] zBase = new double[baseN];
            zBase[0] = double.NegativeInfinity; // OBJ — never used directly
            zBase[1] = 0;
            for (int i = 2; i < baseN; i++)
            {
                double t = baseSys.Surfaces[i - 1].Thickness;
                if (double.IsInfinity(t) || double.IsNaN(t)) t = 0;
                zBase[i] = zBase[i - 1] + t;
            }

            // 5. Identify INTERNAL air gaps. A gap [zBase[i], zBase[i+1]] is
            //    internal iff: surface i has empty Material (air after it),
            //    surface i+1 has non-empty Material (lens front), AND
            //    surface i-1 has non-empty Material (so surface i is a lens
            //    back, not the OBJ→first-lens air space). i+1 must not be
            //    the image surface.
            var gaps = new List<(int prevIdx, double zLow, double zHigh)>();
            for (int i = 1; i < baseN - 2; i++)
            {
                var s = baseSys.Surfaces[i];
                if (!string.IsNullOrEmpty(s.Material)) continue; // s is glass-front, not air-after
                if (i < 2) continue;
                var prevS = baseSys.Surfaces[i - 1];
                if (string.IsNullOrEmpty(prevS.Material)) continue; // s isn't a lens back; this is the leading gap
                var nextS = baseSys.Surfaces[i + 1];
                if (string.IsNullOrEmpty(nextS.Material)) continue; // i+1 isn't a lens front
                gaps.Add((i, zBase[i], zBase[i + 1]));
            }

            if (gaps.Count == 0)
                return "RelocateStopScan: no internal air gaps found (system has no lens-to-lens air spaces).";

            // 6. Target entrance pupil position relative to first refractive
            //    surface = -oldStopT (positive, since oldStopT < 0).
            double zTarget = -oldStopT;

            // 6a. Rewrite the merit function for the candidates. The source
            //     template was authored with span operands like CTA(-3,-1) /
            //     EA(-3,-1) / CTG(-3,-1) / EG(-3,-1), where -3 = "first
            //     surface after stop". For the buried-pupil source that
            //     deliberately EXCLUDED the dummy stop's leading thickness
            //     (S1.T) so the optimizer could push it negative and bury
            //     the pupil.
            //
            //     In the converted candidates the stop has moved into the
            //     middle of the lens stack, so -3 now points at the surface
            //     AFTER the new stop — which means the lens elements + air
            //     gaps BEFORE the new stop (including the stop's own leading
            //     and trailing air pair) are no longer covered by these
            //     constraints. We want the new candidates' CTA/EA/CTG/EG to
            //     cover EVERY refractive surface from L1 onward, so re-opt
            //     can hold every air gap and every glass thickness in bounds.
            //
            //     The fix (Option A from the user discussion): rewrite each
            //     CTA/EA/CTG/EG operand's Surface1 from -3 to absolute 1
            //     (= first refractive surface in the new layout, since the
            //     dummy S1 has been dropped). Surface2 = -1 stays as the
            //     sentinel for the last refractive surface.
            LensHH.Core.MeritFunction.MeritFunction? candidateMf = null;
            if (src.MeritFunction != null)
            {
                candidateMf = src.MeritFunction.Clone();
                foreach (var op in candidateMf.Operands)
                {
                    bool isSpanOperand = op.Type == LensHH.Core.MeritFunction.OperandType.CTA
                                      || op.Type == LensHH.Core.MeritFunction.OperandType.EA
                                      || op.Type == LensHH.Core.MeritFunction.OperandType.CTG
                                      || op.Type == LensHH.Core.MeritFunction.OperandType.EG;
                    if (isSpanOperand && op.Surface1 == -3)
                    {
                        op.Surface1 = -5; // first refractive surface sentinel
                    }
                }
            }

            // 7. For each gap, generate 3 candidates (start / middle / end).
            //    Use small inset from gap edges to avoid coincident surfaces.
            var sb = new StringBuilder();
            sb.AppendLine($"Source:  {sourcePath}");
            sb.AppendLine($"S{oldStopIdx}.Thickness = {oldStopT:F4} mm (buried, |T|={Math.Abs(oldStopT):F4} mm)");
            sb.AppendLine($"Target entrance pupil position: z = {zTarget:F4} mm (relative to first refractive surface)");
            sb.AppendLine($"Internal air gaps to scan: {gaps.Count}");
            if (candidateMf != null)
            {
                int rewriteCount = 0;
                foreach (var op in candidateMf.Operands)
                {
                    bool isSpan = op.Type == LensHH.Core.MeritFunction.OperandType.CTA
                              || op.Type == LensHH.Core.MeritFunction.OperandType.EA
                              || op.Type == LensHH.Core.MeritFunction.OperandType.CTG
                              || op.Type == LensHH.Core.MeritFunction.OperandType.EG;
                    if (isSpan && op.Surface1 == -5) rewriteCount++;
                }
                sb.AppendLine($"Merit-function rewrite: {rewriteCount} span operand(s) had Surface1=-3 → -5 (covers leading/trailing air around new stop + lenses before stop).");
            }
            sb.AppendLine();
            sb.AppendLine($"{"Gap",-4}{"Pos",-8}{"z_stop",10}{"z_ep",12}{"Δ",12}  File");

            string baseName = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
            string[] labels = { "start", "middle", "end" };

            int gapNum = 0;
            int candidatesSaved = 0;
            int candidatesFailed = 0;
            foreach (var gap in gaps)
            {
                gapNum++;
                double gapLen = gap.zHigh - gap.zLow;
                // ε scales with gap length so tiny gaps still produce 3 distinct positions.
                double eps = Math.Min(0.05, gapLen * 0.1);
                double[] positions =
                {
                    gap.zLow + eps,
                    (gap.zLow + gap.zHigh) / 2.0,
                    gap.zHigh - eps,
                };

                for (int j = 0; j < 3; j++)
                {
                    double p = positions[j];

                    // Build candidate: deep-clone base, then insert stop in gap.
                    var candidate = CloneSystem(baseSys);
                    InsertStopInGap(candidate, gap.prevIdx, p - gap.zLow, gap.zHigh - p);

                    // Compute entrance pupil via the engine's paraxial solver.
                    double zEp = double.NaN;
                    try
                    {
                        var sysData = LensHH.Core.Analysis.SystemDataCalculator.Calculate(candidate, _session.GlassCatalog);
                        zEp = sysData.EntrancePupilPosition;
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"{gapNum,-4}{labels[j],-8}{p,10:F4}    (paraxial failed: {ex.Message})");
                        candidatesFailed++;
                        continue;
                    }

                    // Save candidate.
                    string fileName = $"{baseName}_gap{gapNum}_{labels[j]}.lhlt";
                    string outPath = System.IO.Path.Combine(outputDir, fileName);
                    try { LensHH.Core.IO.LhltWriter.Write(candidate, outPath, candidateMf, src.ConfigEditor); }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"{gapNum,-4}{labels[j],-8}{p,10:F4}    (save failed: {ex.Message})");
                        candidatesFailed++;
                        continue;
                    }

                    double delta = zEp - zTarget;
                    sb.AppendLine($"{gapNum,-4}{labels[j],-8}{p,10:F4}{zEp,12:F4}{delta,12:F4}  {fileName}");
                    candidatesSaved++;
                }
            }

            sb.AppendLine();
            sb.AppendLine($"Saved {candidatesSaved} candidate(s) under {outputDir}."
                + (candidatesFailed > 0 ? $" {candidatesFailed} failed." : ""));
            sb.AppendLine("Verify in GUI: open any candidate .lhlt and check the entrance pupil position in System Data.");
            sb.AppendLine("Δ column is engine_z_ep - target. Δ0 → physical stop reproduces the original chief-ray geometry exactly.");

            return sb.ToString();
        }

        /// <summary>
        /// Drop the dummy stop surface (oldStopIdx) and return a new
        /// OpticalSystem. OBJ thickness is set to (origOBJ.T + origDummy.T)
        /// so the geometric OBJ→(was S2) distance is preserved. For infinite
        /// conjugate this stays ∞ in IEEE 754 arithmetic; for finite it gives
        /// the correct finite sum. Wavelengths, fields, catalogs and
        /// per-surface GlassSubstitution settings preserved; entries pointing
        /// at the dropped dummy are removed and remaining SurfaceIndex values
        /// are shifted to match the dropped-surface renumbering. Pickups are
        /// not copied — their references aren't worth re-mapping for a one-
        /// shot conversion.
        /// </summary>
        private static LensHH.Core.Models.OpticalSystem BuildBaseSystemWithoutDummyStop(
            LensHH.Core.Models.OpticalSystem srcSys, int oldStopIdx)
        {
            var dst = new LensHH.Core.Models.OpticalSystem
            {
                Title              = srcSys.Title,
                Notes              = srcSys.Notes,
                Designer           = srcSys.Designer,
                Aperture           = new LensHH.Core.Models.Aperture(srcSys.Aperture.Type, srcSys.Aperture.Value),
                FieldType          = srcSys.FieldType,
                IsAfocal           = srcSys.IsAfocal,
                PenalizeVignetting = srcSys.PenalizeVignetting,
                RayAiming          = srcSys.RayAiming,
            };

            double origObjT   = srcSys.Surfaces[0].Thickness;
            double origDummyT = srcSys.Surfaces[oldStopIdx].Thickness;

            for (int i = 0; i < srcSys.Surfaces.Count; i++)
            {
                if (i == oldStopIdx) continue;
                var s = LensHH.Core.IO.LensInsertHelpers.CloneSurface(srcSys.Surfaces[i]);
                s.IsStop = false; // any stop flag goes onto the newly-inserted surface later
                dst.Surfaces.Add(s);
            }

            // Adjust OBJ.Thickness so OBJ→(was S2) distance is preserved.
            // ∞ + finite = ∞ in IEEE 754, which is the correct behavior for
            // infinite-conjugate systems.
            dst.Surfaces[0].Thickness = origObjT + origDummyT;

            // Reindex
            for (int i = 0; i < dst.Surfaces.Count; i++) dst.Surfaces[i].Index = i;

            dst.Wavelengths.AddRange(srcSys.Wavelengths);
            dst.Fields.AddRange(srcSys.Fields);
            dst.GlassCatalogs.AddRange(srcSys.GlassCatalogs);

            // Copy GlassSubstitution settings with index re-mapping for the
            // dropped dummy. Each entry's SurfaceIndex was its position in the
            // ORIGINAL system; in the new system surfaces with original index
            // > oldStopIdx have shifted down by 1. Entries pointing AT the
            // dummy itself (unusual but possible) are dropped.
            foreach (var gs in srcSys.GlassSubstitutions)
            {
                if (gs.SurfaceIndex == oldStopIdx) continue;
                int newIdx = gs.SurfaceIndex > oldStopIdx ? gs.SurfaceIndex - 1 : gs.SurfaceIndex;
                dst.GlassSubstitutions.Add(new LensHH.Core.Models.GlassSubstitutionSetting
                {
                    SurfaceIndex = newIdx,
                    Substitute   = gs.Substitute,
                    CatalogName  = gs.CatalogName,
                });
            }

            return dst;
        }

        /// <summary>Deep clone of an OpticalSystem (surfaces, wavelengths, fields, catalogs, glass substitutions).</summary>
        private static LensHH.Core.Models.OpticalSystem CloneSystem(LensHH.Core.Models.OpticalSystem src)
        {
            var dst = new LensHH.Core.Models.OpticalSystem
            {
                Title              = src.Title,
                Notes              = src.Notes,
                Designer           = src.Designer,
                Aperture           = new LensHH.Core.Models.Aperture(src.Aperture.Type, src.Aperture.Value),
                FieldType          = src.FieldType,
                IsAfocal           = src.IsAfocal,
                PenalizeVignetting = src.PenalizeVignetting,
                RayAiming          = src.RayAiming,
            };
            foreach (var s in src.Surfaces) dst.Surfaces.Add(LensHH.Core.IO.LensInsertHelpers.CloneSurface(s));
            dst.Wavelengths.AddRange(src.Wavelengths);
            dst.Fields.AddRange(src.Fields);
            dst.GlassCatalogs.AddRange(src.GlassCatalogs);
            foreach (var gs in src.GlassSubstitutions)
            {
                dst.GlassSubstitutions.Add(new LensHH.Core.Models.GlassSubstitutionSetting
                {
                    SurfaceIndex = gs.SurfaceIndex,
                    Substitute   = gs.Substitute,
                    CatalogName  = gs.CatalogName,
                });
            }
            return dst;
        }

        /// <summary>
        /// Insert a flat aperture-stop surface in the air gap immediately
        /// after baseSys.Surfaces[prevIdx]. leadingAir = air thickness from
        /// the previous lens back to the new stop; trailingAir = air thickness
        /// from the new stop to the next lens front. The total of these two
        /// should equal the original gap length so the rest of the lens stack
        /// stays at its original physical positions. The new stop surface is
        /// marked ThicknessVariable so re-optimization can slide the stop
        /// within the bounded air gap (CTA min/max from the merit function).
        /// GlassSubstitution entries with SurfaceIndex >= the insert position
        /// are shifted up by 1 to match the new surface numbering.
        /// </summary>
        private static void InsertStopInGap(LensHH.Core.Models.OpticalSystem sys,
            int prevIdx, double leadingAir, double trailingAir)
        {
            var prevS = sys.Surfaces[prevIdx];
            var nextS = sys.Surfaces[prevIdx + 1];

            // Pick SemiDiameter that doesn't pinch either neighbor.
            double sd = Math.Max(prevS.SemiDiameter, nextS.SemiDiameter);
            if (sd <= 0) sd = 12.5; // sensible fallback

            var stopS = new LensHH.Core.Models.Surface
            {
                Type              = LensHH.Core.Enums.SurfaceType.Standard,
                Radius            = 1e18,           // plano (engine's flat-surface sentinel)
                Thickness         = trailingAir,
                Material          = "",             // explicit empty-string (not null) — matches add_singlet convention
                SemiDiameter      = sd,
                SemiDiameterMode  = LensHH.Core.Enums.SemiDiameterMode.Auto,
                Conic             = 0,
                IsStop            = true,
                ThicknessVariable = true,           // re-opt should be able to slide the stop within the gap
            };

            // Modify the leading-side surface's thickness: was the full air-gap
            // length; becomes just the leading-air segment.
            prevS.Thickness = leadingAir;

            int insertAt = prevIdx + 1;
            sys.Surfaces.Insert(insertAt, stopS);

            for (int i = 0; i < sys.Surfaces.Count; i++) sys.Surfaces[i].Index = i;

            // Shift GlassSubstitution SurfaceIndex for entries at or after
            // the insert position.
            foreach (var gs in sys.GlassSubstitutions)
            {
                if (gs.SurfaceIndex >= insertAt) gs.SurfaceIndex++;
            }
        }

        [McpServerTool, Description("Set clear aperture percentage for a range of surfaces. Only affects Auto (non-fixed, non-stop) surfaces. Values >100 increase semi-diameter beyond clear aperture, <100 causes vignetting.")]
        public string SetClearAperturePercent(int surface1, int surface2, double caPercent = 100)
        {
            var sys = _session.System;
            int s1 = Math.Max(0, surface1);
            int s2 = Math.Min(sys.Surfaces.Count - 1, surface2);
            int count = 0;
            for (int i = s1; i <= s2; i++)
            {
                var surf = sys.Surfaces[i];
                if (surf.SemiDiameterMode == SemiDiameterMode.Auto && !surf.IsStop)
                {
                    surf.ClearAperturePercent = caPercent;
                    count++;
                }
            }
            return $"Set CA% = {caPercent:F1} on {count} Auto surfaces (range {s1}-{s2}).";
        }
    }
}
