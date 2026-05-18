using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LensHH.Core.Enums;
using LensHH.Core.Models;

namespace LensHH.Core.IO
{
    public static class ZmxReader
    {
        public static OpticalSystem Read(string filePath)
        {
            var lines = ReadFileLines(filePath);
            var system = new OpticalSystem();

            system.Title = ReadTitle(lines);
            system.FieldType = ReadFieldType(lines);
            system.IsAfocal = ReadAfocalMode(lines);
            system.Aperture = ReadAperture(lines);
            system.Wavelengths = ReadWavelengths(lines);
            system.Fields = ReadFields(lines);
            FieldValidation.FilterImportedFields(system);
            system.Surfaces = ReadSurfaces(lines);

            SetPrimaryWavelength(system, lines);
            SetRayAiming(system, lines);
            SetGlassCatalogs(system, lines);

            // FLOA "Float by Stop Size": ReadAperture marked the EPD with value
            // 0 as a sentinel. Resolve to 2 × stop-surface SemiDiameter now that
            // surfaces are parsed. If the stop has no SemiDiameter set, the
            // hardcoded 10.0 fallback below kicks in.
            if (system.Aperture.Type == ApertureType.EPD && system.Aperture.Value == 0.0)
            {
                int floaStopIdx = system.StopSurfaceIndex;
                if (floaStopIdx > 0 && floaStopIdx < system.Surfaces.Count)
                {
                    double stopSd = system.Surfaces[floaStopIdx].SemiDiameter;
                    if (stopSd > 0)
                        system.Aperture = new Aperture(ApertureType.EPD, stopSd * 2.0);
                    else
                        system.Aperture = new Aperture(ApertureType.EPD, 10.0);
                }
                else
                {
                    system.Aperture = new Aperture(ApertureType.EPD, 10.0);
                }
            }

            // Stock-lens EPD override: if Aperture is EPD-type and the stop surface
            // has a CLAP outer radius defined, replace the ENPD-derived value with
            // the optical CA diameter (= 2 × CLAP outer radius). Vendor stock-lens
            // .zmx files set ENPD = mechanical OD (the part's full diameter), but
            // the lens's effective optical aperture is the smaller CLAP zone. Using
            // CLAP as the EPD prevents marginal rays from being launched outside
            // the lens's good optical region (which previously caused rays to
            // appear "through air" past the lens edge in layout drawings).
            if (system.Aperture.Type == ApertureType.EPD)
            {
                double stopClapOuter = ExtractStopClapOuter(lines, system.StopSurfaceIndex);
                if (stopClapOuter > 0)
                    system.Aperture = new Aperture(ApertureType.EPD, stopClapOuter * 2.0);
            }

            // Stock-lens drawing convention: when the .zmx puts the stop on a
            // refractive vertex of the first element and the part has a CLAP
            // defined (single-element vendor catalog files do this), we want
            // the layout to show the lens body at its mechanical OD (MEMA)
            // with rays clipped at the smaller optical CA (CLAP). Achieved by
            // inserting a new zero-thickness dummy surface in air just ahead
            // of the lens, moving IsStop to that dummy, and setting all
            // lens-group vertices to SemiDiameter = MEMA (Fixed).
            InsertStockLensStopDummy(system);

            // Convert all distances from file lens unit to mm
            double scale = ReadLensUnitScale(lines);
            if (scale != 1.0)
                LensUnitConverter.ConvertToMm(system, scale);

            return system;
        }

        /// <summary>
        /// For stock-lens imports: if the stop sits on a refractive vertex and a
        /// CLAP/MEMA is defined on it, insert a dummy stop in air in front of the
        /// lens element group and set the lens vertices to SemiDiameter=MEMA Fixed.
        /// </summary>
        private static void InsertStockLensStopDummy(OpticalSystem system)
        {
            int stopIdx = system.StopSurfaceIndex;
            if (stopIdx <= 0 || stopIdx >= system.Surfaces.Count - 1) return;

            var stopSurf = system.Surfaces[stopIdx];
            if (string.IsNullOrEmpty(stopSurf.Material)) return; // stop already in air
            double clap = stopSurf.SemiDiameter > 0
                ? stopSurf.SemiDiameter
                : stopSurf.ClapOuterRadius; // MEMA-only vendor files: no DIAM/CLAP, only MEMA
            if (clap <= 0) return;                                // no aperture info at all

            // Walk forward through the bonded element group: contiguous vertices
            // whose outgoing medium is glass (Material non-empty). The group ends
            // at the first surface with empty Material (air on the back side).
            int groupEnd = stopIdx;
            while (groupEnd < system.Surfaces.Count - 1
                   && !string.IsNullOrEmpty(system.Surfaces[groupEnd].Material))
            {
                groupEnd++;
            }
            // groupEnd is the air-side back surface of the element.

            double mema = 0.0;
            for (int i = stopIdx; i <= groupEnd; i++)
            {
                double v = system.Surfaces[i].ClapOuterRadius;
                if (v > mema) mema = v;
            }
            if (mema <= 0) mema = clap;
            double cap = clap > 0 ? mema / clap * 100.0 : 100.0;

            var dummy = new Surface
            {
                Type = SurfaceType.Standard,
                Radius = double.PositiveInfinity,
                Thickness = 0.0,
                Material = string.Empty,
                SemiDiameter = clap,
                SemiDiameterMode = SemiDiameterMode.Auto,
                IsStop = true,
                Comment = string.Empty
            };

            for (int i = stopIdx; i <= groupEnd; i++)
            {
                var s = system.Surfaces[i];
                s.SemiDiameter = mema;
                s.SemiDiameterMode = SemiDiameterMode.Fixed;
                s.ClearAperturePercent = cap;
                if (i == stopIdx) s.IsStop = false;
            }

            system.Surfaces.Insert(stopIdx, dummy);
            for (int i = 0; i < system.Surfaces.Count; i++)
                system.Surfaces[i].Index = i;

            // Stock-lens convention: real ray aiming buys nothing for a singlet
            // (or simple cemented multiplet) at a modest aperture. Some Edmund
            // vendor .zmx files ship with RAIM=Real anyway; force Off here so
            // the .lhlt convention is consistent regardless of source. Users
            // who genuinely need ray aiming can re-enable it after import.
            system.RayAiming = LensHH.Core.Enums.RayAimingMode.Off;

            // Some vendor .zmx files insert extra air-only "best-focus offset"
            // dummies between the last refractive vertex and IMG (e.g. Edmund
            // 29-094 / 29-095 plano-convex singlets; a few Thorlabs AC / LA /
            // LJ). For a stock-lens-as-building-block use the offset is noise
            // — host systems determine their own image plane. They also break
            // the engine's paraxial BFL = -y/u at surface[N-2] (which then
            // measures from the dummy, not the lens back). Collapse them into
            // the lens-back vertex's thickness.
            CollapseTrailingDummies(system);
        }

        /// <summary>
        /// Sum any air-only surfaces between the last refractive vertex and
        /// IMG into the lens-back vertex's thickness, then remove the
        /// intermediate dummies. After this runs, the IMG surface always sits
        /// directly after the lens-back air-side vertex.
        /// </summary>
        private static void CollapseTrailingDummies(OpticalSystem system)
        {
            int n = system.Surfaces.Count;
            if (n < 4) return;

            int lastGlassIdx = -1;
            for (int i = 0; i < n; i++)
            {
                if (!string.IsNullOrEmpty(system.Surfaces[i].Material))
                    lastGlassIdx = i;
            }
            if (lastGlassIdx < 0) return;

            int lastBackIdx = lastGlassIdx + 1;
            int imgIdx = n - 1;
            int nTrail = imgIdx - lastBackIdx - 1;
            if (nTrail <= 0) return;

            double sumT = 0.0;
            for (int i = lastBackIdx; i < imgIdx; i++)
                sumT += system.Surfaces[i].Thickness;
            system.Surfaces[lastBackIdx].Thickness = sumT;

            // Remove surfaces (lastBackIdx+1) through (imgIdx-1) in reverse so
            // indices stay valid through the loop.
            for (int i = imgIdx - 1; i >= lastBackIdx + 1; i--)
                system.Surfaces.RemoveAt(i);

            for (int i = 0; i < system.Surfaces.Count; i++)
                system.Surfaces[i].Index = i;
        }

        /// <summary>
        /// Walk the raw .zmx lines and return the CLAP outer radius written on
        /// the given stop-surface block, or 0 if no CLAP keyword appears there.
        /// Independent re-scan because ParseSurfaceBlock writes CLAP into
        /// surface.ClapOuterRadius only when MEMA hasn't already populated it
        /// — so the surface model alone can't tell us the original CLAP value.
        /// </summary>
        private static double ExtractStopClapOuter(string[] lines, int stopIndex)
        {
            int currentSurfIdx = -1;
            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimStart();
                if (line.Length == 0) continue;
                var parts = SplitLine(line);
                if (parts.Length == 0) continue;
                if (parts[0] == "SURF" && parts.Length > 1 && int.TryParse(parts[1], out int idx))
                {
                    currentSurfIdx = idx;
                    continue;
                }
                if (currentSurfIdx == stopIndex
                    && parts[0] == "CLAP" && parts.Length >= 3
                    && TryParseDouble(parts[2], out double clapOuter))
                {
                    return clapOuter;
                }
            }
            return 0;
        }

        private static string[] ReadFileLines(string filePath)
        {
            // ZMX files can be UTF-16 LE or UTF-8
            byte[] bytes = File.ReadAllBytes(filePath);
            string[] lines;

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                string text = Encoding.Unicode.GetString(bytes);
                lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            }
            else
            {
                lines = File.ReadAllLines(filePath);
            }

            // Trim leading whitespace — some exporters (e.g. Optalix) indent keywords
            for (int i = 0; i < lines.Length; i++)
                lines[i] = lines[i].TrimStart();

            return lines;
        }

        private static string ReadTitle(string[] lines)
        {
            foreach (var line in lines)
            {
                if (line.StartsWith("TITL"))
                    return line.Length > 5 ? line.Substring(5).Trim() : string.Empty;
                // NAME is used by some exporters (e.g. Optalix) as an alias for TITL
                if (line.StartsWith("NOTE 1 ") || line.StartsWith("NAME "))
                {
                    int space = line.IndexOf(' ');
                    if (line.StartsWith("NOTE"))
                    {
                        // NOTE 1 <title>
                        int secondSpace = line.IndexOf(' ', space + 1);
                        return secondSpace >= 0 ? line.Substring(secondSpace + 1).Trim() : string.Empty;
                    }
                    return space >= 0 ? line.Substring(space + 1).Trim() : string.Empty;
                }
            }
            return string.Empty;
        }

        private static FieldType ReadFieldType(string[] lines)
        {
            foreach (var line in lines)
            {
                if (line.StartsWith("FTYP"))
                {
                    var parts = SplitLine(line);
                    if (parts.Length > 1 && int.TryParse(parts[1], out int ftype))
                    {
                        switch (ftype)
                        {
                            case 0: return FieldType.ObjectAngle;
                            case 1: return FieldType.ObjectHeight;
                            default: return FieldType.ObjectAngle;
                        }
                    }
                }
            }
            return FieldType.ObjectAngle;
        }

        private static bool ReadAfocalMode(string[] lines)
        {
            foreach (var line in lines)
            {
                if (line.StartsWith("FTYP"))
                {
                    var parts = SplitLine(line);
                    // FTYP format: FTYP [0]field_type [1]telecentric [2]num_fields [3]num_wavelengths [4]? [5]? [6]afocal [7]?
                    // parts[7] corresponds to FTYP_Settings[6] (0-indexed values, 1-indexed parts due to keyword)
                    if (parts.Length > 7 && int.TryParse(parts[7], out int afocal))
                    {
                        return afocal != 0;
                    }
                }
            }
            return false;
        }

        private static Aperture ReadAperture(string[] lines)
        {
            // FLOA "Float by Stop Size" supersedes ENPD/FNUM in Zemax: the
            // pupil diameter is set from the stop surface's DIAM at runtime.
            // We return EPD=0 as a sentinel here; Read() resolves the actual
            // value after surfaces are parsed (see ResolveFloaAperture).
            foreach (var line in lines)
            {
                if (line.StartsWith("FLOA"))
                    return new Aperture(ApertureType.EPD, 0.0);
            }
            foreach (var line in lines)
            {
                if (line.StartsWith("ENPD"))
                {
                    var parts = SplitLine(line);
                    if (parts.Length > 1 && TryParseDouble(parts[1], out double val))
                        return new Aperture(ApertureType.EPD, val);
                }
                else if (line.StartsWith("FNUM"))
                {
                    var parts = SplitLine(line);
                    if (parts.Length > 1 && TryParseDouble(parts[1], out double val))
                        return new Aperture(ApertureType.FNumber, val);
                }
            }
            return new Aperture(ApertureType.EPD, 10.0);
        }

        private static List<Wavelength> ReadWavelengths(string[] lines)
        {
            // Read number of wavelengths from FTYP line (4th value, index 3)
            int numWavelengths = 0;
            foreach (var line in lines)
            {
                if (line.StartsWith("FTYP"))
                {
                    var parts = SplitLine(line);
                    // FTYP format: FTYP <field_type> <telecentric> <num_fields> <num_wavelengths> ...
                    if (parts.Length > 4 && int.TryParse(parts[4], out int nw))
                        numWavelengths = nw;
                    break;
                }
            }

            var wavelengths = new List<Wavelength>();

            // Try WAVM format first (one line per wavelength)
            foreach (var line in lines)
            {
                if (line.StartsWith("WAVM"))
                {
                    var parts = SplitLine(line);
                    if (parts.Length >= 3)
                    {
                        if (TryParseDouble(parts[2], out double wl) && wl > 0)
                        {
                            double weight = 1.0;
                            if (parts.Length > 3)
                                TryParseDouble(parts[3], out weight);

                            wavelengths.Add(new Wavelength(wl, weight));

                            // Stop once we've read the expected number of wavelengths
                            if (numWavelengths > 0 && wavelengths.Count >= numWavelengths)
                                break;
                        }
                    }
                }
            }

            // Fallback: WAVL/WWGT format (all wavelengths on one line, used by some exporters)
            if (wavelengths.Count == 0)
            {
                var wlValues = new List<double>();
                var wlWeights = new List<double>();
                foreach (var line in lines)
                {
                    if (line.StartsWith("WAVL"))
                    {
                        var parts = SplitLine(line);
                        for (int i = 1; i < parts.Length; i++)
                            if (TryParseDouble(parts[i], out double wl) && wl > 0) wlValues.Add(wl);
                    }
                    else if (line.StartsWith("WWGT"))
                    {
                        var parts = SplitLine(line);
                        for (int i = 1; i < parts.Length; i++)
                            if (TryParseDouble(parts[i], out double wt)) wlWeights.Add(wt);
                    }
                }
                for (int i = 0; i < wlValues.Count; i++)
                {
                    double wt = i < wlWeights.Count ? wlWeights[i] : 1.0;
                    wavelengths.Add(new Wavelength(wlValues[i], wt));
                }
            }

            return wavelengths;
        }

        private static void SetPrimaryWavelength(OpticalSystem system, string[] lines)
        {
            int primaryIndex = 0;
            foreach (var line in lines)
            {
                if (line.StartsWith("PWAV"))
                {
                    var parts = SplitLine(line);
                    if (parts.Length > 1 && int.TryParse(parts[1], out int idx))
                    {
                        primaryIndex = idx - 1; // ZMX is 1-indexed
                    }
                    break;
                }
            }

            if (primaryIndex >= 0 && primaryIndex < system.Wavelengths.Count)
            {
                system.Wavelengths[primaryIndex].IsPrimary = true;
            }
            else if (system.Wavelengths.Count > 0)
            {
                system.Wavelengths[0].IsPrimary = true;
            }
        }

        private static void SetRayAiming(OpticalSystem system, string[] lines)
        {
            foreach (var line in lines)
            {
                if (line.StartsWith("RAIM"))
                {
                    var parts = SplitLine(line);
                    // RAIM format: RAIM <v0> <mode> <v2> <v3> <v4> <robust> ...
                    // Mode: 0=Off, 1=Paraxial, 2=Real
                    // Robust flag at index 6 (parts[6]): 1=robust
                    if (parts.Length > 2 && int.TryParse(parts[2], out int mode))
                    {
                        bool robust = parts.Length > 6
                            && int.TryParse(parts[6], out int robustFlag) && robustFlag != 0;
                        switch (mode)
                        {
                            case 1:
                            case 2:
                                system.RayAiming = robust
                                    ? Enums.RayAimingMode.Robust
                                    : Enums.RayAimingMode.Real;
                                break;
                            default:
                                system.RayAiming = Enums.RayAimingMode.Off;
                                break;
                        }
                    }
                    break;
                }
            }
        }

        private static List<Field> ReadFields(string[] lines)
        {
            var fields = new List<Field>();
            double[] yValues = new double[0];
            double[] weights = new double[0];
            int numFields = -1; // -1 = use length-based fallback

            foreach (var line in lines)
            {
                if (line.StartsWith("YFLN"))
                {
                    yValues = ParseDoubleList(line);
                }
                else if (line.StartsWith("FWGN") || line.StartsWith("FWGT"))
                {
                    weights = ParseDoubleList(line);
                }
                else if (line.StartsWith("FTYP"))
                {
                    // FTYP format: FTYP <field_type> <telecentric> <num_fields> ...
                    // ZMX always writes 12 XFLN/YFLN slots regardless of how many
                    // fields are actually defined; honor num_fields so beam
                    // expanders and other on-axis-only systems don't import as
                    // 12 zero-field copies.
                    var parts = SplitLine(line);
                    if (parts.Length > 3 && int.TryParse(parts[3], out int nf) && nf > 0)
                        numFields = nf;
                }
            }

            int limit = numFields > 0 ? Math.Min(numFields, yValues.Length) : yValues.Length;
            for (int i = 0; i < limit; i++)
            {
                double w = (i < weights.Length && weights[i] > 0) ? weights[i] : 1.0;
                fields.Add(new Field(yValues[i], w));
            }

            // Always have at least the on-axis field
            if (fields.Count == 0)
                fields.Add(new Field(0, 1.0));

            return fields;
        }

        private static List<Surface> ReadSurfaces(string[] lines)
        {
            var surfaces = new List<Surface>();
            var surfaceBlocks = ExtractSurfaceBlocks(lines);

            foreach (var block in surfaceBlocks)
            {
                var surface = ParseSurfaceBlock(block);
                surfaces.Add(surface);
            }

            return surfaces;
        }

        private static List<List<string>> ExtractSurfaceBlocks(string[] lines)
        {
            var blocks = new List<List<string>>();
            List<string>? currentBlock = null;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                if (line.StartsWith("SURF"))
                {
                    currentBlock = new List<string> { line };
                    blocks.Add(currentBlock);
                }
                else if (currentBlock != null)
                {
                    currentBlock.Add(line);
                }
            }

            return blocks;
        }

        private static Surface ParseSurfaceBlock(List<string> block)
        {
            var surface = new Surface();

            // Parse surface index from SURF line
            var surfParts = SplitLine(block[0]);
            if (surfParts.Length > 1 && int.TryParse(surfParts[1], out int idx))
                surface.Index = idx;

            foreach (var rawLine in block)
            {
                var line = rawLine.TrimStart();
                var parts = SplitLine(line);
                if (parts.Length == 0) continue;

                switch (parts[0])
                {
                    case "TYPE":
                        surface.Type = ParseSurfaceType(parts);
                        break;

                    case "CURV":
                        if (parts.Length > 1 && TryParseDouble(parts[1], out double curv))
                            surface.Curvature = curv;
                        break;

                    case "DISZ":
                        if (parts.Length > 1)
                        {
                            // Accept "INFINITY" / "Infinity" / "infinity" — the
                            // .zmx format uses all caps, but tolerate any
                            // casing on read.
                            if (string.Equals(parts[1], "Infinity", StringComparison.OrdinalIgnoreCase))
                                surface.Thickness = double.PositiveInfinity;
                            else if (TryParseDouble(parts[1], out double disz))
                            {
                                // OpTaliX-exported ZMX files use 1e20 as the
                                // object-at-infinity sentinel instead of the
                                // "INFINITY" keyword. Normalize on read.
                                if (Math.Abs(disz) > 1e18)
                                    surface.Thickness = double.PositiveInfinity;
                                else
                                    surface.Thickness = disz;
                            }
                        }
                        break;

                    case "GLAS":
                        if (parts.Length > 1)
                        {
                            // Standard Zemax GLAS layout:
                            //   GLAS <name> <pickupFrom> <solveType> <nd> <Vd> <PartialDispersion> ...
                            // Most files have a real catalog name in parts[1]
                            // (e.g. "N-BK7"). OpTaliX exports use a fictitious
                            // numeric label there (e.g. "6200.6030") with the
                            // real nd/V at parts[4]/parts[5] — the engine can't
                            // resolve the label, so the surface ends up
                            // unresolved-glass. When parts[4]/parts[5] are
                            // physical nd/V, repack as a Schott-style numeric
                            // code (XXX.XXXX = (nd-1)·1000 . V·100) so the
                            // GUI's substitution pass has a deterministic
                            // signal to match against the loaded catalogs.
                            string mat = parts[1];
                            // Only repack when the name token isn't already
                            // a real glass name. Treat any token that starts
                            // with a digit as a fictitious numeric label;
                            // letter-starting tokens (e.g. "N-BK7", "N-SF10",
                            // "___BLANK") are kept verbatim for the engine
                            // to resolve normally.
                            bool nameIsNumeric = mat.Length > 0 && char.IsDigit(mat[0]);
                            if (nameIsNumeric &&
                                parts.Length > 5 &&
                                TryParseDouble(parts[4], out double glasNd) &&
                                TryParseDouble(parts[5], out double glasVd) &&
                                glasNd >= 1.0 && glasNd <= 3.0 &&
                                glasVd >= 5.0 && glasVd <= 200.0)
                            {
                                int ndCode = (int)System.Math.Round((glasNd - 1.0) * 1000.0);
                                int vdCode = (int)System.Math.Round(glasVd * 100.0);
                                mat = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                    "{0:D3}.{1:D4}", ndCode, vdCode);
                            }
                            surface.Material = mat;
                        }
                        break;

                    case "DIAM":
                        // DIAM format in OpticStudio: DIAM <value> <user_defined_flag> <x_dec> <y_dec> <display_flag> <comment>
                        // field 2 (user_defined_flag): 0 = auto (value is last-computed semi-diameter from ray trace),
                        //                              1 = user-fixed (value is user input, locked).
                        // Empirically verified against Edmund Optics vendor .zmx 2026-05-17: fixed surfaces write
                        // field 2 = 1; auto surfaces write field 2 = 0. The earlier "0..3" interpretation was wrong.
                        if (parts.Length > 1 && TryParseDouble(parts[1], out double diam))
                        {
                            int diamMode = 0;
                            if (parts.Length > 2) int.TryParse(parts[2], out diamMode);

                            if (diamMode >= 1 && diam > 0)
                            {
                                // User-fixed semi-diameter
                                surface.SemiDiameter = diam;
                                surface.SemiDiameterMode = Enums.SemiDiameterMode.Fixed;
                            }
                            else if (diam > 0)
                            {
                                // Auto: keep last-computed value as initial estimate
                                surface.SemiDiameter = diam;
                            }
                        }
                        break;

                    case "CONI":
                        if (parts.Length > 1 && TryParseDouble(parts[1], out double coni))
                            surface.Conic = coni;
                        break;

                    case "PARM":
                        ParseAsphericParam(surface, parts);
                        break;

                    case "STOP":
                        surface.IsStop = true;
                        break;

                    case "COMM":
                        if (parts.Length > 1)
                            surface.Comment = string.Join(" ", parts.Skip(1));
                        break;

                    case "CLAP":
                        // CLAP inner_radius outer_radius x_decenter
                        // inner_radius : central hole (annular aperture, e.g. Cassegrain primary).
                        // outer_radius : optical clear-aperture zone (the "good" optical region).
                        //
                        // For stock-lens imports we prefer MEMA (mechanical extent) over CLAP
                        // for ClapOuterRadius (the drawn extent). Apply CLAP only if MEMA
                        // hasn't already populated it — keeps the answer order-independent
                        // regardless of which keyword OpticStudio writes first.
                        // The CLAP outer is RE-EXTRACTED at end of Read() to override the
                        // system EPD aperture (see ExtractStopClapOuter).
                        if (parts.Length >= 2 && TryParseDouble(parts[1], out double clapInner))
                            surface.InnerRadius = clapInner;
                        if (parts.Length >= 3 && TryParseDouble(parts[2], out double clapOuter)
                            && surface.ClapOuterRadius <= 0)
                            surface.ClapOuterRadius = clapOuter;
                        break;

                    case "MEMA":
                        // MEMA semi_diameter ... — mechanical maximum aperture (full lens OD / 2).
                        // Drives ClapOuterRadius (drawn extent) for stock-lens imports, where
                        // we want the layout to show the part's mechanical edge, not just the
                        // optical CA zone. Overrides any CLAP value previously set on this
                        // surface (CLAP only sets ClapOuterRadius when ClapOuterRadius is 0).
                        if (parts.Length >= 2 && TryParseDouble(parts[1], out double memaR) && memaR > 0)
                            surface.ClapOuterRadius = memaR;
                        break;

                    case "OBSC":
                        // OBSC min_radius max_radius x_decenter
                        // Central obscuration (secondary mirror shadow)
                        if (parts.Length >= 3 && TryParseDouble(parts[2], out double obscMax))
                            surface.ObscurationRadius = obscMax;
                        break;

                    case "FLAP":
                        // FLAP min_radius max_radius x_decenter
                        // Floating aperture
                        if (parts.Length >= 3 && TryParseDouble(parts[2], out double flapMax))
                            surface.FloatingApertureRadius = flapMax;
                        break;
                }
            }

            return surface;
        }

        private static SurfaceType ParseSurfaceType(string[] parts)
        {
            if (parts.Length < 2) return SurfaceType.Standard;

            switch (parts[1].ToUpperInvariant())
            {
                case "STANDARD":
                    return SurfaceType.Standard;
                case "EVENASPH":
                    return SurfaceType.EvenAsphere;
                default:
                    // Unsupported surface types default to Standard
                    return SurfaceType.Standard;
            }
        }

        private static void ParseAsphericParam(Surface surface, string[] parts)
        {
            // PARM index value
            if (parts.Length >= 3 && int.TryParse(parts[1], out int paramIndex))
            {
                if (TryParseDouble(parts[2], out double val))
                {
                    // PARM 1-8 map to AsphericCoefficients[0-7]
                    int arrayIndex = paramIndex - 1;
                    if (arrayIndex >= 0 && arrayIndex < surface.AsphericCoefficients.Length)
                    {
                        surface.AsphericCoefficients[arrayIndex] = val;
                        if (surface.Type == SurfaceType.Standard && val != 0)
                            surface.Type = SurfaceType.EvenAsphere;
                    }
                }
            }
        }

        private static double[] ParseDoubleList(string line)
        {
            var parts = SplitLine(line);
            var values = new List<double>();

            for (int i = 1; i < parts.Length; i++)
            {
                if (TryParseDouble(parts[i], out double val))
                    values.Add(val);
            }

            return values.ToArray();
        }

        private static void SetGlassCatalogs(OpticalSystem system, string[] lines)
        {
            foreach (var line in lines)
            {
                if (line.StartsWith("GCAT"))
                {
                    var parts = SplitLine(line);
                    for (int i = 1; i < parts.Length; i++)
                    {
                        if (!string.IsNullOrWhiteSpace(parts[i]))
                            system.GlassCatalogs.Add(parts[i].ToUpperInvariant());
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// Read the UNIT line and return the scale factor to convert to mm.
        /// Supported units: MM (1.0), IN (25.4), CM (10.0), M (1000.0).
        /// </summary>
        private static double ReadLensUnitScale(string[] lines)
        {
            foreach (var line in lines)
            {
                if (line.StartsWith("UNIT"))
                {
                    var parts = SplitLine(line);
                    if (parts.Length > 1)
                    {
                        switch (parts[1].ToUpperInvariant())
                        {
                            case "MM": return 1.0;
                            case "IN": return 25.4;
                            case "CM": return 10.0;
                            case "M":
                            case "METER": return 1000.0;
                        }
                    }
                    break;
                }
            }
            return 1.0; // default to mm
        }

        // Unit conversion now handled by LensUnitConverter.ConvertToMm()

        private static string[] SplitLine(string line)
        {
            return line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool TryParseDouble(string s, out double value)
        {
            return double.TryParse(s, NumberStyles.Float | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture, out value);
        }
    }
}
