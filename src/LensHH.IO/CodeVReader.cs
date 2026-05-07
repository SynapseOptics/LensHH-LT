using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using LensHH.Core.Enums;
using LensHH.Core.Models;

namespace LensHH.Core.IO
{
    /// <summary>
    /// Reads Code V .seq lens files.
    /// Handles SO/S/SI surface definitions, CIR/STO/ASP/CON sub-keywords,
    /// EPD/FNO aperture, WL wavelengths, YAN field angles.
    /// </summary>
    public static class CodeVReader
    {
        public static OpticalSystem Read(string filePath)
        {
            var lines = File.ReadAllLines(filePath);
            var system = new OpticalSystem();

            var surfaces = new List<Surface>();
            var wavelengths = new List<double>();
            var wavelengthWeights = new List<double>();
            var fieldAnglesY = new List<double>();
            var fieldWeights = new List<double>();
            int refWavelength = 1; // 1-indexed
            double unitScale = 1.0; // default mm
            Surface? currentSurface = null;
            int surfIdx = 0;

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("!")) continue;

                var parts = SplitLine(line);
                if (parts.Length == 0) continue;

                string keyword = parts[0].ToUpperInvariant();

                switch (keyword)
                {
                    case "TIT":
                        // Title in single quotes
                        int q1 = line.IndexOf('\'');
                        int q2 = line.LastIndexOf('\'');
                        if (q1 >= 0 && q2 > q1)
                            system.Title = line.Substring(q1 + 1, q2 - q1 - 1);
                        break;

                    case "EPD":
                        if (parts.Length > 1 && TryParseDouble(parts[1], out double epd))
                            system.Aperture = new Aperture(ApertureType.EPD, epd);
                        break;

                    case "FNO":
                        if (parts.Length > 1 && TryParseDouble(parts[1], out double fno))
                            system.Aperture = new Aperture(ApertureType.FNumber, fno);
                        break;

                    case "WL":
                        // Wavelengths in nanometers
                        for (int i = 1; i < parts.Length; i++)
                        {
                            if (TryParseDouble(parts[i], out double wlNm))
                                wavelengths.Add(wlNm / 1000.0); // nm to um
                        }
                        break;

                    case "WTW":
                        for (int i = 1; i < parts.Length; i++)
                        {
                            if (TryParseDouble(parts[i], out double wt))
                                wavelengthWeights.Add(wt);
                        }
                        break;

                    case "REF":
                        if (parts.Length > 1 && int.TryParse(parts[1], out int r))
                            refWavelength = r;
                        break;

                    case "YAN":
                        for (int i = 1; i < parts.Length; i++)
                        {
                            if (TryParseDouble(parts[i], out double ang))
                                fieldAnglesY.Add(ang);
                        }
                        break;

                    case "WTF":
                        for (int i = 1; i < parts.Length; i++)
                        {
                            if (TryParseDouble(parts[i], out double fw))
                                fieldWeights.Add(fw);
                        }
                        break;

                    case "SO": // Object surface
                    case "S":  // Regular surface
                    case "SI": // Image surface
                        currentSurface = ParseSurfaceLine(parts, keyword, surfIdx);
                        surfaces.Add(currentSurface);
                        surfIdx++;
                        break;

                    case "CIR":
                        if (currentSurface != null && parts.Length > 1 && TryParseDouble(parts[1], out double cir))
                        {
                            currentSurface.SemiDiameter = cir;
                            currentSurface.SemiDiameterMode = SemiDiameterMode.Fixed;
                        }
                        break;

                    case "STO":
                        if (currentSurface != null)
                            currentSurface.IsStop = true;
                        break;

                    case "ASP":
                        // ASP ; A <val> ; B <val> ; C <val> ; D <val> ; E <val> ; F <val> ; G <val> ; H <val>
                        if (currentSurface != null)
                        {
                            ParseAspCoefficients(line, currentSurface);
                            currentSurface.Type = SurfaceType.EvenAsphere;
                        }
                        break;

                    case "CON":
                        // CON ; K <val>
                        if (currentSurface != null)
                        {
                            double kVal = ParseNamedValue(line, "K");
                            if (!double.IsNaN(kVal))
                            {
                                currentSurface.Conic = kVal;
                                if (currentSurface.Type != SurfaceType.EvenAsphere)
                                    currentSurface.Type = SurfaceType.EvenAsphere;
                            }
                        }
                        break;

                    case "K":
                        // Standalone K <val> (conic constant, sometimes without CON prefix)
                        if (currentSurface != null && parts.Length > 1 && TryParseDouble(parts[1], out double kStandalone))
                        {
                            currentSurface.Conic = kStandalone;
                            if (currentSurface.Type != SurfaceType.EvenAsphere)
                                currentSurface.Type = SurfaceType.EvenAsphere;
                        }
                        break;

                    case "DIM":
                        // DIM M = millimeters, DIM C = centimeters, DIM I = inches
                        if (parts.Length > 1)
                        {
                            switch (parts[1].ToUpperInvariant())
                            {
                                case "M": unitScale = 1.0; break;    // millimeters
                                case "C": unitScale = 10.0; break;   // centimeters
                                case "I": unitScale = 25.4; break;   // inches
                            }
                        }
                        break;

                    case "FTYP":
                        // Field type: 0 = angle, 1 = object height
                        if (parts.Length > 1 && int.TryParse(parts[1], out int ft))
                            system.FieldType = ft == 1 ? FieldType.ObjectHeight : FieldType.ObjectAngle;
                        break;
                }
            }

            // Cemented-interface SD inheritance: any surface with glass on
            // both sides (= a cemented joint) is by construction the same
            // physical aperture as its neighbours. Code V doesn't always
            // write a CIR for the middle surface of a doublet; without one
            // the surface ends up Auto-SD with value 0, which renders the
            // 2D layout incorrectly. If the previous surface has Fixed-SD,
            // inherit it.
            for (int i = 1; i < surfaces.Count; i++)
            {
                bool isCementedInterface =
                    IsGlassMaterial(surfaces[i - 1].Material) &&
                    IsGlassMaterial(surfaces[i].Material);
                if (!isCementedInterface) continue;

                var prev = surfaces[i - 1];
                var cur  = surfaces[i];
                if (prev.SemiDiameterMode == SemiDiameterMode.Fixed &&
                    cur.SemiDiameterMode != SemiDiameterMode.Fixed &&
                    cur.SemiDiameter <= 0)
                {
                    cur.SemiDiameter = prev.SemiDiameter;
                    cur.SemiDiameterMode = SemiDiameterMode.Fixed;
                }
            }

            // Populate system
            system.Surfaces = surfaces;

            // Wavelengths
            for (int i = 0; i < wavelengths.Count; i++)
            {
                double wt = i < wavelengthWeights.Count ? wavelengthWeights[i] : 1.0;
                bool isPrimary = (i + 1) == refWavelength;
                system.Wavelengths.Add(new Wavelength(wavelengths[i], wt, isPrimary));
            }

            // Fields
            for (int i = 0; i < fieldAnglesY.Count; i++)
            {
                double wt = i < fieldWeights.Count ? fieldWeights[i] / 100.0 : 1.0;
                if (i == 0 && fieldAnglesY[i] == 0 && wt == 0) wt = 1.0;
                system.Fields.Add(new Field(fieldAnglesY[i], wt > 0 ? wt : 1.0));
            }
            if (system.Fields.Count == 0)
                system.Fields.Add(new Field(0, 1.0));

            FieldValidation.FilterImportedFields(system);

            // Convert from file units to mm
            if (unitScale != 1.0)
                LensUnitConverter.ConvertToMm(system, unitScale);

            return system;
        }

        private static Surface ParseSurfaceLine(string[] parts, string keyword, int index)
        {
            var surface = new Surface { Index = index };

            // Format: SO/S/SI <radius> <thickness> <material>
            if (parts.Length > 1 && TryParseDouble(parts[1], out double radius))
            {
                if (Math.Abs(radius) > 1e-15)
                    surface.Radius = radius;
                // else default infinity
            }

            if (parts.Length > 2 && TryParseDouble(parts[2], out double thickness))
            {
                // Code V uses ~1e20 for infinity
                if (Math.Abs(thickness) > 1e18)
                    surface.Thickness = double.PositiveInfinity;
                else
                    surface.Thickness = thickness;
            }

            if (parts.Length > 3)
            {
                string material = parts[3].Trim();
                if (material.Equals("AIR", StringComparison.OrdinalIgnoreCase))
                {
                    // no material
                }
                else if (material.Equals("REFL", StringComparison.OrdinalIgnoreCase))
                {
                    surface.Material = "MIRROR";
                }
                else
                {
                    // Remove catalog suffix like "_SCHOTT"
                    int underscoreIdx = material.IndexOf('_');
                    if (underscoreIdx > 0)
                        material = material.Substring(0, underscoreIdx);
                    // Schott N-prefix glasses are written without the dash
                    // in Code V (e.g. NSF10), but the LensHH glass catalogs
                    // (and Zemax) use the dashed form (N-SF10). Apply the
                    // transform on import so the loaded glass actually
                    // resolves against the catalog.
                    surface.Material = CodeVNamesToCatalog(material);
                }
            }

            return surface;
        }

        /// <summary>
        /// Parse ASP line: ASP ; A <val> ; B <val> ; C <val> ...
        /// Code V: A=r⁴, B=r⁶, C=r⁸, ... Internal: [0]=r², [1]=r⁴, [2]=r⁶, ...
        /// So A→[1], B→[2], etc.
        /// </summary>
        private static void ParseAspCoefficients(string line, Surface surface)
        {
            string[] coeffNames = { "A", "B", "C", "D", "E", "F", "G", "H" };
            for (int i = 0; i < coeffNames.Length; i++)
            {
                double val = ParseNamedValue(line, coeffNames[i]);
                int idx = i + 1; // A→[1], B→[2], etc.
                if (!double.IsNaN(val) && idx < surface.AsphericCoefficients.Length)
                    surface.AsphericCoefficients[idx] = val;
            }
        }

        /// <summary>
        /// Parse a named value from a Code V line with ; delimiters.
        /// E.g., "ASP ; A 1.23E-09 ; B -4.56E-15" → ParseNamedValue(line, "A") → 1.23E-09
        /// </summary>
        private static double ParseNamedValue(string line, string name)
        {
            // Split by ; and find the segment starting with the name
            var segments = line.Split(';');
            foreach (var seg in segments)
            {
                var trimmed = seg.Trim();
                var segParts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (segParts.Length >= 2 && segParts[0].Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseDouble(segParts[1], out double val))
                        return val;
                }
            }
            return double.NaN;
        }

        private static string[] SplitLine(string line)
        {
            return line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool TryParseDouble(string s, out double value)
        {
            return double.TryParse(s, NumberStyles.Float | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// Translate a Code V glass name to the form used in the LensHH
        /// glass catalogs (and in Zemax). Schott "N-prefix" glasses appear
        /// in Code V without the dash, so <c>NSF10</c> needs to become
        /// <c>N-SF10</c> before catalog lookup. Triggers only on names of
        /// the form N + uppercase letter (so legacy names that happen to
        /// start with N — e.g. <c>NULL</c>, none of which are real glass
        /// names anyway — are unaffected, and old non-N glasses like
        /// <c>SF10</c>, <c>LAFN7</c> are passed through untouched).
        /// </summary>
        private static string CodeVNamesToCatalog(string name)
        {
            if (name.Length >= 2 && name[0] == 'N' && char.IsUpper(name[1]))
                return "N-" + name.Substring(1);
            return name;
        }

        private static bool IsGlassMaterial(string? material)
        {
            if (string.IsNullOrEmpty(material)) return false;
            if (material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }
    }
}
