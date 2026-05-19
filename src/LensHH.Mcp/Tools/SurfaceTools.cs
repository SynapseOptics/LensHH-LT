using System;
using System.Collections.Generic;
using System.ComponentModel;
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
            "Build a complete lens skeleton (multiple elements + entrance-pupil offset + glass substitution) on the CURRENT system in one call. Expects the host to already have the merit function, fields, wavelengths, and stop set up; this helper adds the lens elements and configures them ready for `multistart_optimize_start`.\n\n"
            + "Architecture (currently supported):\n"
            + "  • 'single-single-single' (alias 'sss', 'cooke'): three singlets — front + (biconcave SF11) + back. Standard Cooke-triplet starting seed: BK7 / SF11 / BK7, biconvex / biconcave / biconvex, with all curvatures + thicknesses + entrance-pupil-offset marked variable and glass substitution enabled on every glass surface against substitutionCatalog (default 'SCHOTT').\n\n"
            + "More architectures (doublets, Tessar, double-Gauss skeletons) will land here next; for now the single-single-single seed already covers the bulk of v5/v6/v7's free-optimize starting points.\n\n"
            + "Parameters:\n"
            + "  • entrancePupilOffset (default −10 mm): seed for the stop-surface trailing thickness. Negative = buried pupil (pupil ahead of S1), as v6/v7 free-opts settled on.\n"
            + "  • semiDiameterDefault (default 12.5 mm = Ø25): seed semi-D for every element. Mode=Auto so the SD solver resizes during optimization.\n"
            + "  • airGap (default 10 mm): air spacing between adjacent elements. Marked variable.\n"
            + "  • bflSeed (default 45 mm): trailing air gap from the last element to the image. Marked variable.\n"
            + "  • substitutionCatalog (default 'SCHOTT'): catalog name for glass substitution on every glass surface. Pass empty to disable substitution.\n\n"
            + "After the call, the system is ready to call `multistart_optimize_start` with `glassSubPercent > 0`. Eliminates the 12–15 micro-tool ritual (edit_surface stop thickness, set_variable thickness, add_singlet × N, set_glass_substitution × N).")]
        public string BuildSkeleton(
            string architecture = "single-single-single",
            double entrancePupilOffset = -10.0,
            double semiDiameterDefault = 12.5,
            double airGap = 10.0,
            double bflSeed = 45.0,
            string substitutionCatalog = "SCHOTT")
        {
            // ── Validate architecture key ─────────────────────────────────────
            var arch = (architecture ?? "").Trim().ToLowerInvariant().Replace('_', '-');
            bool isCooke = arch == "single-single-single" || arch == "sss" || arch == "cooke";
            if (!isCooke)
                return $"BuildSkeleton error: unknown architecture '{architecture}'. Supported: 'single-single-single' (Cooke triplet).";

            var sys = _session.System;
            if (sys == null || sys.Surfaces.Count < 3)
                return "BuildSkeleton error: host system must already have OBJ + STOP + IMG (use load_system on a template first).";

            // ── Identify the stop surface ─────────────────────────────────────
            // We assume the standard template layout: OBJ=0, STOP=1 (IsStop=true,
            // air), IMG=last. If the host is shaped differently we still try —
            // we just use sys.StopSurfaceIndex regardless of where it sits.
            int stopIdx = sys.StopSurfaceIndex;
            if (stopIdx <= 0)
                return $"BuildSkeleton error: stop surface not found (stopIdx={stopIdx}). Make sure the template has IsStop=true on one of the surfaces.";

            // Refuse to run on a system that already has glass elements — we
            // don't want to silently double-insert if the agent calls this on
            // an already-populated design. Caller should reload the template.
            bool hasExistingGlass = false;
            for (int i = 0; i < sys.Surfaces.Count; i++)
                if (!string.IsNullOrEmpty(sys.Surfaces[i].Material)) { hasExistingGlass = true; break; }
            if (hasExistingGlass)
                return "BuildSkeleton error: system already contains glass elements. Reload a fresh template before calling BuildSkeleton.";

            // ── Set entrance-pupil offset and make it variable ────────────────
            // S1.Thickness is the distance from stop to first lens. Free-opt
            // basins often want negative values (buried pupil); seeding with
            // −10 mm gets the optimizer going in roughly the right direction.
            sys.Surfaces[stopIdx].Thickness = entrancePupilOffset;
            sys.Surfaces[stopIdx].ThicknessVariable = true;

            var report = new StringBuilder();
            report.AppendLine($"BuildSkeleton: architecture='{(isCooke ? "single-single-single (Cooke)" : architecture)}'");
            report.AppendLine($"  Stop at S{stopIdx}, entrance-pupil offset = {entrancePupilOffset} mm (variable).");

            // ── Cooke triplet seed values ─────────────────────────────────────
            // Element 1: BK7 biconvex,  R=±50,  t=4 mm,  airAfter=airGap
            // Element 2: SF11 biconcave, R=∓30, t=3 mm,  airAfter=airGap
            // Element 3: BK7 biconvex,  R=±50,  t=4 mm,  airAfter=bflSeed
            //
            // The biconvex/biconcave seeds give the optimizer a clean +,−,+
            // power signature with substantial wiggle room. Glass substitution
            // on top will explore alternative glasses; the optimizer can also
            // bend the radii via the curvature variables.
            var ops = new (string label, double r1, double r2, double t, string mat, double airAfter, double sd)[]
            {
                ("E1 (positive, BK7)",  +50, -50, 4, "N-BK7",  airGap,  semiDiameterDefault),
                ("E2 (negative, SF11)", -30, +30, 3, "N-SF11", airGap,  semiDiameterDefault),
                ("E3 (positive, BK7)",  +50, -50, 4, "N-BK7",  bflSeed, semiDiameterDefault),
            };

            int afterSurface = stopIdx;
            var glassSurfacesForSub = new List<int>();
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
                afterSurface = back;
                report.AppendLine($"  {op.label}: surfaces [{front}-{back}].");
            }

            // ── Enable glass substitution on every glass surface ──────────────
            // The Cooke triplet has 3 glass surfaces (one per singlet's front,
            // since the back is already an air surface). Glass substitution
            // will let multistart pick from the chosen catalog during trials.
            if (!string.IsNullOrWhiteSpace(substitutionCatalog))
            {
                foreach (var glassSurf in glassSurfacesForSub)
                {
                    sys.GlassSubstitutions.Add(new GlassSubstitutionSetting
                    {
                        SurfaceIndex = glassSurf,
                        Substitute = true,
                        CatalogName = substitutionCatalog
                    });
                }
                report.AppendLine($"  Glass substitution enabled on {glassSurfacesForSub.Count} surface(s) against catalog '{substitutionCatalog}'.");
            }
            else
            {
                report.AppendLine("  Glass substitution disabled (empty substitutionCatalog).");
            }

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
