using System;
using System.ComponentModel;
using System.Text;
using LensHH.Core.Analysis;
using LensHH.Core.IO;
using LensHH.Core.Models;
using ModelContextProtocol.Server;

namespace LensHH.Mcp.Tools
{
    [McpServerToolType]
    public class SystemTools
    {
        private readonly McpSession _session;
        public SystemTools(McpSession session) => _session = session;

        [McpServerTool, Description("Create a new empty optical system")]
        public async System.Threading.Tasks.Task<string> NewSystem()
        {
            _session.NewSystem();
            try { await RenderAppClient.SendAsync(new OpticalSystem(), "Clear"); } catch { }
            return "New optical system created.";
        }

        [McpServerTool, Description("Load an optical system from a .lhlt file (native format, includes merit function and config editor). Provide the full file path.")]
        public async System.Threading.Tasks.Task<string> LoadSystem(string filePath)
        {
            _session.LoadFromFile(filePath);

            // Solve AUTO semi-diameters on load so get_system reports real apertures and a
            // subsequent save persists them (the .lhlt stores SemiDiameter; Auto surfaces load
            // as 0 = "compute on use"). Mirrors the GUI, which solves on load/render.
            try { LensHH.Core.Analysis.SemiDiameterSolver.Solve(_session.System, _session.GlassCatalog, accurateApertures: true); } catch { }

            // Clear the render window so stale analysis from the previous system is not shown
            try { await RenderAppClient.SendAsync(new OpticalSystem(), "Clear"); } catch { }

            var sys = _session.System;
            var sb = new System.Text.StringBuilder();
            sb.Append($"Loaded '{sys.Title}' from {filePath}: {sys.Surfaces.Count} surfaces, ");
            sb.Append($"{sys.Wavelengths.Count} wavelengths, {sys.Fields.Count} fields.");
            if (_session.MeritFunction != null)
                sb.Append($" Merit function: {_session.MeritFunction.Operands.Count} operands.");
            return sb.ToString();
        }

        [McpServerTool, Description("Save the current optical system to the current .lhlt file. Fails if no file has been loaded or saved before. Use SaveAsSystem to specify a path.")]
        public string SaveSystem()
        {
            if (_session.CurrentFilePath == null)
                return "No file path set. Use SaveAsSystem to specify a path.";
            _session.SaveToFile(_session.CurrentFilePath);
            return $"System saved to {_session.CurrentFilePath}.";
        }

        [McpServerTool, Description("Save the current optical system to a new .lhlt file path (native format, includes merit function, variables, config editor). Provide the full file path.")]
        public string SaveAsSystem(string filePath)
        {
            _session.SaveToFile(filePath);
            return $"System saved to {filePath}.";
        }

        [McpServerTool, Description("Import an optical system from a Zemax .zmx file.")]
        public async System.Threading.Tasks.Task<string> ImportZemax(string filePath)
        {
            _session.ImportZemax(filePath);
            try { await RenderAppClient.SendAsync(new OpticalSystem(), "Clear"); } catch { }
            return FormatImportResult(filePath);
        }

        [McpServerTool, Description("Import an optical system from a Code V .seq file.")]
        public async System.Threading.Tasks.Task<string> ImportCodeV(string filePath)
        {
            _session.ImportCodeV(filePath);
            try { await RenderAppClient.SendAsync(new OpticalSystem(), "Clear"); } catch { }
            return FormatImportResult(filePath);
        }

        [McpServerTool, Description("Import an optical system from an OSLO .len file.")]
        public async System.Threading.Tasks.Task<string> ImportOslo(string filePath)
        {
            _session.ImportOslo(filePath);
            try { await RenderAppClient.SendAsync(new OpticalSystem(), "Clear"); } catch { }
            return FormatImportResult(filePath);
        }

        [McpServerTool, Description("Import an optical system from an Optalix .otx file.")]
        public async System.Threading.Tasks.Task<string> ImportOptalix(string filePath)
        {
            _session.ImportOptalix(filePath);
            try { await RenderAppClient.SendAsync(new OpticalSystem(), "Clear"); } catch { }
            return FormatImportResult(filePath);
        }

        [McpServerTool, Description("Import an optical system from an Optiland .json file.")]
        public async System.Threading.Tasks.Task<string> ImportOptiland(string filePath)
        {
            _session.ImportOptiland(filePath);
            try { await RenderAppClient.SendAsync(new OpticalSystem(), "Clear"); } catch { }
            return FormatImportResult(filePath);
        }

        [McpServerTool, Description("Export the current optical system to a Zemax .zmx file.")]
        public string ExportZemax(string filePath)
        {
            _session.ExportZemax(filePath);
            return $"Exported Zemax file: {filePath}";
        }

        [McpServerTool, Description("Export the current optical system to a Code V .seq file.")]
        public string ExportCodeV(string filePath)
        {
            _session.ExportCodeV(filePath);
            return $"Exported Code V file: {filePath}";
        }

        [McpServerTool, Description("Export the current optical system to an OSLO .len file.")]
        public string ExportOslo(string filePath)
        {
            _session.ExportOslo(filePath);
            return $"Exported OSLO file: {filePath}";
        }

        [McpServerTool, Description("Export the current optical system to an Optalix .otx file.")]
        public string ExportOptalix(string filePath)
        {
            _session.ExportOptalix(filePath);
            return $"Exported Optalix file: {filePath}";
        }

        [McpServerTool, Description("Export the current optical system to an Optiland .json file.")]
        public string ExportOptiland(string filePath)
        {
            _session.ExportOptiland(filePath);
            return $"Exported Optiland file: {filePath}";
        }

        private string FormatImportResult(string filePath)
        {
            var sys = _session.System;
            return $"Imported '{sys.Title}' from {filePath}: {sys.Surfaces.Count} surfaces, " +
                   $"{sys.Wavelengths.Count} wavelengths, {sys.Fields.Count} fields.";
        }

        [McpServerTool, Description("Get complete information about the current optical system including surfaces, wavelengths, fields, and aperture.")]
        public string GetSystem()
        {
            var sys = _session.System;
            var sb = new StringBuilder();

            sb.AppendLine($"Title: {sys.Title}");
            sb.AppendLine($"Aperture: {sys.Aperture.Type} = {sys.Aperture.Value}");
            sb.AppendLine($"Field Type: {sys.FieldType}");
            sb.AppendLine($"Ray Aiming: {sys.RayAiming}");
            sb.AppendLine($"Auto Semi-Diameters: {sys.SemiDiameterSolve}");
            sb.AppendLine($"Bound Handling: {sys.BoundHandling}");
            sb.AppendLine($"Afocal: {sys.IsAfocal}");
            sb.AppendLine($"Telecentric Object Space: {sys.TelecentricObjectSpace}");
            sb.AppendLine($"Penalize Vignetting: {sys.PenalizeVignetting}");
            sb.AppendLine($"Stop Surface: {sys.StopSurfaceIndex}");
            sb.AppendLine();

            sb.AppendLine("Surfaces:");
            sb.AppendLine($"{"#",-4} {"Type",-12} {"Radius",12} {"Thickness",12} {"Material",-12} {"SemiDiam",10} {"Conic",10}");
            for (int i = 0; i < sys.Surfaces.Count; i++)
            {
                var s = sys.Surfaces[i];
                string typeStr = i == 0 ? "OBJ" : i == sys.Surfaces.Count - 1 ? "IMG" : s.Type.ToString();
                if (s.IsStop) typeStr += "*";
                sb.AppendLine($"{i,-4} {typeStr,-12} {s.Radius,12:F6} {s.Thickness,12:F6} {s.Material ?? "",-12} {s.SemiDiameter,10:F4} {s.Conic,10:F6}");
            }
            sb.AppendLine();

            sb.AppendLine("Wavelengths:");
            for (int i = 0; i < sys.Wavelengths.Count; i++)
            {
                var w = sys.Wavelengths[i];
                string primary = i == sys.PrimaryWavelengthIndex ? " (primary)" : "";
                sb.AppendLine($"  [{i}] {w.Value:F6} um, weight={w.Weight}{primary}");
            }
            sb.AppendLine();

            sb.AppendLine("Fields:");
            for (int i = 0; i < sys.Fields.Count; i++)
            {
                var f = sys.Fields[i];
                sb.AppendLine($"  [{i}] Y={f.Y}, weight={f.Weight}");
            }

            return sb.ToString();
        }

        [McpServerTool, Description("Get system data: first-order optical properties (EFL, BFL, FFL, F/#, NA, pupils, magnification). Shows focal or afocal properties depending on system mode.")]
        public string GetParaxialData()
        {
            var sys = _session.System;
            var result = SystemDataCalculator.Calculate(sys, _session.GlassCatalog);

            var sb = new StringBuilder();
            sb.AppendLine(result.IsAfocal ? "System Data (Afocal)" : "System Data (Focal)");
            sb.AppendLine();

            if (result.IsAfocal)
            {
                sb.AppendLine($"{"Angular Magnification",-28} {result.AngularMagnification:G6} x");
                sb.AppendLine();
                sb.AppendLine($"{"Entrance Pupil Diameter",-28} {result.EntrancePupilDiameter:F4} mm");
                sb.AppendLine($"{"Entrance Pupil Position",-28} {result.EntrancePupilPosition:F4} mm");
                sb.AppendLine($"{"Exit Pupil Diameter",-28} {result.ExitPupilDiameter:F4} mm");
                sb.AppendLine($"{"Exit Pupil Position",-28} {result.ExitPupilPosition:F4} mm");
                sb.AppendLine();
                sb.AppendLine($"{"Total Track",-28} {result.TotalTrack:F4} mm");
                sb.AppendLine($"{"Maximum Field",-28} {result.MaximumField:F4} {result.FieldUnit}");
            }
            else
            {
                sb.AppendLine($"{"Effective Focal Length",-28} {result.Efl:F4} mm");
                sb.AppendLine($"{"Back Focal Length",-28} {result.Bfl:F4} mm");
                sb.AppendLine($"{"Front Focal Length",-28} {result.Ffl:F4} mm");
                sb.AppendLine();
                sb.AppendLine($"{"Image Space F/#",-28} {result.ImageSpaceFNumber:F4}");
                sb.AppendLine($"{"Working F/#",-28} {result.WorkingFNumber:F4}");
                sb.AppendLine($"{"Image Space NA",-28} {result.ImageSpaceNA:F6}");
                sb.AppendLine();
                sb.AppendLine($"{"Entrance Pupil Diameter",-28} {result.EntrancePupilDiameter:F4} mm");
                sb.AppendLine($"{"Entrance Pupil Position",-28} {result.EntrancePupilPosition:F4} mm");
                sb.AppendLine($"{"Exit Pupil Diameter",-28} {result.ExitPupilDiameter:F4} mm");
                sb.AppendLine($"{"Exit Pupil Position",-28} {result.ExitPupilPosition:F4} mm");
                sb.AppendLine();
                sb.AppendLine($"{"Paraxial Image Height",-28} {result.ParaxialImageHeight:F4} mm");
                sb.AppendLine($"{"Paraxial Magnification",-28} {result.ParaxialMagnification:G6}");
                sb.AppendLine();
                sb.AppendLine($"{"Total Track",-28} {result.TotalTrack:F4} mm");
                sb.AppendLine($"{"Maximum Field",-28} {result.MaximumField:F4} {result.FieldUnit}");
            }
            return sb.ToString();
        }

        [McpServerTool, Description("Set the system aperture. type must be 'EPD', 'FNumber', or 'ObjectSpaceNA'. value is the numeric value: EPD in lens units, F-number, or object-space numerical aperture (NA = n0·sin(u)). ObjectSpaceNA is only valid for a finite-conjugate system with Object Height fields.")]
        public string SetAperture(string type, double value)
        {
            var sys = _session.System;
            if (type.Equals("EPD", StringComparison.OrdinalIgnoreCase))
                sys.Aperture = new Aperture(Core.Enums.ApertureType.EPD, value);
            else if (type.Equals("FNumber", StringComparison.OrdinalIgnoreCase) || type.Equals("F/#", StringComparison.OrdinalIgnoreCase))
                sys.Aperture = new Aperture(Core.Enums.ApertureType.FNumber, value);
            else if (type.Equals("ObjectSpaceNA", StringComparison.OrdinalIgnoreCase) || type.Equals("NA", StringComparison.OrdinalIgnoreCase) || type.Equals("ObjectNA", StringComparison.OrdinalIgnoreCase))
                sys.Aperture = new Aperture(Core.Enums.ApertureType.ObjectSpaceNA, value);
            else
                return $"Unknown aperture type '{type}'. Use 'EPD', 'FNumber', or 'ObjectSpaceNA'.";

            // Telecentric object space is only meaningful with Object Space NA — clear it otherwise.
            if (sys.Aperture.Type != Core.Enums.ApertureType.ObjectSpaceNA && sys.TelecentricObjectSpace)
                sys.TelecentricObjectSpace = false;

            string note = "";
            if (sys.Aperture.Type == Core.Enums.ApertureType.ObjectSpaceNA)
            {
                var err = Core.RayTrace.ApertureRadius.ObjectSpaceNaError(sys);
                if (err != null) note = " WARNING: " + err;
            }
            return $"Aperture set to {sys.Aperture.Type} = {sys.Aperture.Value}.{note}";
        }

        [McpServerTool, Description("Set wavelengths for the system. Provide wavelengths in micrometers as comma-separated values (e.g. '0.4861,0.5876,0.6563'). Optional: primaryIndex (0-based, default 1 for middle).")]
        public string SetWavelengths(string wavelengths, int primaryIndex = -1)
        {
            var sys = _session.System;
            var parts = wavelengths.Split(',');
            sys.Wavelengths.Clear();
            foreach (var p in parts)
            {
                if (double.TryParse(p.Trim(), out double val))
                    sys.Wavelengths.Add(new Wavelength(val, 1.0));
            }

            if (primaryIndex < 0) primaryIndex = sys.Wavelengths.Count / 2;
            if (primaryIndex >= 0 && primaryIndex < sys.Wavelengths.Count)
            {
                for (int i = 0; i < sys.Wavelengths.Count; i++)
                    sys.Wavelengths[i].IsPrimary = (i == primaryIndex);
            }

            return $"Set {sys.Wavelengths.Count} wavelengths. Primary index: {sys.PrimaryWavelengthIndex}.";
        }

        [McpServerTool, Description("Set field points. Provide Y values in degrees (for ObjectAngle) as comma-separated values (e.g. '0,7,10'). Optional: fieldType ('ObjectAngle' or 'ObjectHeight', default ObjectAngle).")]
        public string SetFields(string fields, string fieldType = "ObjectAngle")
        {
            var sys = _session.System;

            if (fieldType.Equals("ObjectAngle", StringComparison.OrdinalIgnoreCase))
                sys.FieldType = Core.Enums.FieldType.ObjectAngle;
            else if (fieldType.Equals("ObjectHeight", StringComparison.OrdinalIgnoreCase))
                sys.FieldType = Core.Enums.FieldType.ObjectHeight;

            var parts = fields.Split(',');

            // Parse and validate every value before mutating the system, so
            // a bad value doesn't leave the field list half-rewritten.
            var parsed = new System.Collections.Generic.List<double>();
            foreach (var p in parts)
            {
                if (!double.TryParse(p.Trim(), out double val))
                    return $"Could not parse field value '{p.Trim()}'.";
                if (!Core.Models.FieldValidation.IsValid(val, sys.FieldType, out string? error))
                    return error!;
                parsed.Add(val);
            }

            sys.Fields.Clear();
            foreach (double val in parsed)
                sys.Fields.Add(new Field(val, 1.0));

            return $"Set {sys.Fields.Count} field points ({sys.FieldType}).";
        }

        [McpServerTool, Description("Set ray aiming mode. mode must be 'Off', 'Real', or 'Robust'. Robust uses wide-angle ray aiming for systems with large field angles.")]
        public string SetRayAiming(string mode)
        {
            var sys = _session.System;
            if (mode.Equals("Off", StringComparison.OrdinalIgnoreCase))
                sys.RayAiming = Core.Enums.RayAimingMode.Off;
            else if (mode.Equals("Real", StringComparison.OrdinalIgnoreCase))
                sys.RayAiming = Core.Enums.RayAimingMode.Real;
            else if (mode.Equals("Robust", StringComparison.OrdinalIgnoreCase))
                sys.RayAiming = Core.Enums.RayAimingMode.Robust;
            else
                return $"Unknown ray aiming mode '{mode}'. Use 'Off', 'Real', or 'Robust'.";

            return $"Ray aiming set to {sys.RayAiming}.";
        }
        [McpServerTool, Description("Set how Auto semi-diameters are derived. mode must be 'RealRay' or 'Paraxial'. RealRay (default, historical) sizes each surface to whichever real traced rays survive. Paraxial sizes it to the beam the design actually needs (|marginal| + |chief| height at max field) with no ray tracing, so nothing can fail or be vignetted. Paraxial suits fast paraxial-only screening, and it also keeps edge-thickness and semi-diameter constraints meaningful on a design that has stopped passing light: under RealRay the apertures shrink to fit the surviving rays, which quietly SATISFIES those constraints instead of violating them. SYSTEM-LEVEL and orthogonal to the per-surface Auto/Fixed mode - surfaces set to Fixed are unaffected either way. Switching an existing design to Paraxial will move its ET/EA/EG/SD operand values, sometimes substantially.")]
        public string SetAutoDiameterSolve(string mode)
        {
            var sys = _session.System;
            if (mode.Equals("RealRay", StringComparison.OrdinalIgnoreCase)
                || mode.Equals("Real", StringComparison.OrdinalIgnoreCase))
                sys.SemiDiameterSolve = Core.Enums.SemiDiameterSolve.RealRay;
            else if (mode.Equals("Paraxial", StringComparison.OrdinalIgnoreCase))
                sys.SemiDiameterSolve = Core.Enums.SemiDiameterSolve.Paraxial;
            else
                return $"Unknown semi-diameter solve '{mode}'. Use 'RealRay' or 'Paraxial'.";

            string note = sys.SemiDiameterSolve == Core.Enums.SemiDiameterSolve.Paraxial
                ? " Auto apertures now follow the beam the design needs rather than the rays that"
                  + " survive, so ET/EA/EG/SD values will change."
                : "";
            return $"Auto semi-diameter solve set to {sys.SemiDiameterSolve}.{note}";
        }

        [McpServerTool, Description("Set how bounded optimization variables are mapped into the optimizer's search space. mode must be 'Sigmoid' or 'Reflect'. Sigmoid (default) is smooth but its gradient vanishes at a bound, so a variable pushed to a limit stops responding to the optimizer. Reflect keeps the variable in physical units and folds out-of-range values back inside, so |gradient| stays constant at the bounds — usually better for constrained and stochastic search. System-level: applies to every optimizer and is saved with the design.")]
        public string SetBoundHandling(string mode)
        {
            var sys = _session.System;
            if (mode.Equals("Sigmoid", StringComparison.OrdinalIgnoreCase))
                sys.BoundHandling = Core.Enums.BoundHandlingMode.Sigmoid;
            else if (mode.Equals("Reflect", StringComparison.OrdinalIgnoreCase))
                sys.BoundHandling = Core.Enums.BoundHandlingMode.Reflect;
            else
                return $"Unknown bound handling mode '{mode}'. Use 'Sigmoid' or 'Reflect'.";

            return $"Bound handling set to {sys.BoundHandling}.";
        }

        [McpServerTool, Description("Set afocal mode on or off. Afocal systems have no finite focal length (e.g. telescopes, beam expanders). afocal=true for afocal, false for focal.")]
        public string SetAfocal(bool afocal)
        {
            _session.System.IsAfocal = afocal;
            return $"Afocal mode set to {(afocal ? "On" : "Off")}.";
        }

        [McpServerTool, Description("Set object-space telecentric mode on or off. When on, the entrance pupil is at infinity so the chief ray is parallel to the axis in object space (metrology / machine-vision optics). Only valid with the Object Space NA aperture AND ray aiming Off. telecentric=true to enable.")]
        public string SetTelecentricObjectSpace(bool telecentric)
        {
            var sys = _session.System;
            if (telecentric)
            {
                if (sys.Aperture.Type != Core.Enums.ApertureType.ObjectSpaceNA)
                    return "Telecentric Object Space requires the Object Space NA aperture. Set the aperture to ObjectSpaceNA first.";
                if (sys.RayAiming != Core.Enums.RayAimingMode.Off)
                    return "Telecentric Object Space requires Ray Aiming to be Off. Turn ray aiming off first.";
            }
            sys.TelecentricObjectSpace = telecentric;
            return $"Telecentric Object Space set to {(telecentric ? "On" : "Off")}.";
        }

        [McpServerTool, Description("Toggle the system's PenalizeVignetting flag. When true, the merit-function evaluator emits a stiff per-ray penalty for EVERY vignetted ray, including off-axis. Default (false) preserves the legacy 'off-axis vignetting is free' behavior. Turn this on for stock-lens designs where apertures are fixed by the catalog and the optimizer must not 'buy' aberration relief by clipping pupil rays. Persisted in .lhlt.")]
        public string SetPenalizeVignetting(bool penalize)
        {
            _session.System.PenalizeVignetting = penalize;
            return $"PenalizeVignetting set to {(penalize ? "On" : "Off")}.";
        }

        [McpServerTool, Description("Set preferred glass catalogs for glass search and substitution. Provide catalog names as comma-separated values (e.g. 'SCHOTT,OHARA,HOYA'). Empty string clears the list.")]
        public string SetCatalogs(string catalogs)
        {
            var sys = _session.System;
            sys.GlassCatalogs.Clear();
            if (!string.IsNullOrWhiteSpace(catalogs))
            {
                foreach (var c in catalogs.Split(','))
                {
                    var trimmed = c.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        sys.GlassCatalogs.Add(trimmed.ToUpperInvariant());
                }
            }
            return sys.GlassCatalogs.Count > 0
                ? $"Preferred catalogs set to: {string.Join(", ", sys.GlassCatalogs)}."
                : "Preferred catalogs cleared.";
        }

        [McpServerTool, Description("Scale the entire optical system by a factor. Scales radii, thicknesses, semi-diameters, aspheric coefficients, aperture, and field heights. factor must be positive and not 1.0.")]
        public string ScaleLens(double factor)
        {
            if (factor <= 0)
                return "Scale factor must be positive.";
            if (factor == 1.0)
                return "Scale factor is 1.0 — no change.";

            var system = _session.System;
            double s = factor;

            for (int i = 0; i < system.Surfaces.Count; i++)
            {
                var surf = system.Surfaces[i];

                if (!double.IsInfinity(surf.Radius) && surf.Radius != 0)
                    surf.Radius *= s;

                if (!double.IsInfinity(surf.Thickness) && !double.IsNaN(surf.Thickness))
                    surf.Thickness *= s;

                if (surf.SemiDiameterMode == Core.Enums.SemiDiameterMode.Fixed && surf.SemiDiameter > 0)
                    surf.SemiDiameter *= s;

                if (surf.InnerRadius > 0) surf.InnerRadius *= s;
                if (surf.ClapOuterRadius > 0) surf.ClapOuterRadius *= s;
                if (surf.ObscurationRadius > 0) surf.ObscurationRadius *= s;
                if (surf.FloatingApertureRadius > 0) surf.FloatingApertureRadius *= s;

                if (surf.AsphericCoefficients != null)
                {
                    for (int j = 0; j < surf.AsphericCoefficients.Length; j++)
                    {
                        if (surf.AsphericCoefficients[j] != 0)
                        {
                            int twoN = (j + 1) * 2;
                            surf.AsphericCoefficients[j] *= Math.Pow(s, 1.0 - twoN);
                        }
                    }
                }

                if (surf.ThicknessMin.HasValue) surf.ThicknessMin *= s;
                if (surf.ThicknessMax.HasValue) surf.ThicknessMax *= s;
                if (surf.CurvatureMin.HasValue) surf.CurvatureMin /= s;
                if (surf.CurvatureMax.HasValue) surf.CurvatureMax /= s;
            }

            if (system.Aperture.Type == Core.Enums.ApertureType.EPD)
                system.Aperture.Value *= s;

            if (system.FieldType == Core.Enums.FieldType.ObjectHeight)
            {
                foreach (var field in system.Fields)
                    field.Y *= s;
            }

            return $"System scaled by factor {s}.";
        }

        [McpServerTool, Description("Show refractive indices for all glass surfaces at all system wavelengths. Returns a table of surface index, material, catalog, and refractive index at each wavelength.")]
        public string GetSystemGlassIndices()
        {
            var sys = _session.System;
            var glassMgr = _session.GlassCatalog;

            var sb = new StringBuilder();
            sb.Append($"{"Surf",5} {"Material",-14} {"Catalog",-10}");
            foreach (var wl in sys.Wavelengths)
                sb.Append($" {"n@" + wl.Value.ToString("F6"),12}");
            sb.AppendLine();

            for (int i = 0; i < sys.Surfaces.Count; i++)
            {
                var s = sys.Surfaces[i];
                var material = s.Material;
                sb.Append($"{i,5} ");

                if (string.IsNullOrEmpty(material))
                {
                    sb.Append($"{"(air)",-14} {"",-10}");
                    foreach (var wl in sys.Wavelengths)
                        sb.Append($" {"1.000000",12}");
                }
                else if (material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append($"{"MIRROR",-14} {"",-10}");
                    foreach (var wl in sys.Wavelengths)
                        sb.Append($" {"(mirror)",12}");
                }
                else
                {
                    var glass = glassMgr.GetGlass(material, sys.GlassCatalogs.Count > 0 ? sys.GlassCatalogs : null);
                    sb.Append($"{material,-14} {(glass != null ? glass.Catalog : "NOT FOUND"),-10}");
                    foreach (var wl in sys.Wavelengths)
                    {
                        if (glass != null)
                            sb.Append($" {glass.GetIndex(wl.Value).ToString("F6"),12}");
                        else
                            sb.Append($" {"1.000000",12}");
                    }
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
