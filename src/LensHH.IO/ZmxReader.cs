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

            // Convert all distances from file lens unit to mm
            double scale = ReadLensUnitScale(lines);
            if (scale != 1.0)
                LensUnitConverter.ConvertToMm(system, scale);

            return system;
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
                            if (parts[1] == "INFINITY")
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
                        // DIAM format: DIAM <value> <mode> ...
                        // Zemax modes: 0=Auto, 1=Marginal ray (auto), 2=Maximum (fixed), 3=Manual (fixed)
                        if (parts.Length > 1 && TryParseDouble(parts[1], out double diam))
                        {
                            int diamMode = 0;
                            if (parts.Length > 2) int.TryParse(parts[2], out diamMode);

                            if (diamMode >= 2 && diam > 0)
                            {
                                // Mode 2 (Maximum) or 3 (Manual) = user-specified fixed
                                surface.SemiDiameter = diam;
                                surface.SemiDiameterMode = Enums.SemiDiameterMode.Fixed;
                            }
                            else if (diam > 0)
                            {
                                // Mode 0 or 1 = auto; use value as initial estimate
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
                        // Annular aperture (central hole), e.g. Cassegrain primary mirror
                        if (parts.Length >= 2 && TryParseDouble(parts[1], out double clapInner))
                            surface.InnerRadius = clapInner;
                        if (parts.Length >= 3 && TryParseDouble(parts[2], out double clapOuter))
                            surface.ClapOuterRadius = clapOuter;
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
