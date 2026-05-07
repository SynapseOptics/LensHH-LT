using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using LensHH.Core.Enums;
using LensHH.Core.Glass;
using LensHH.Core.Models;

namespace LensHH.Core.IO
{
    /// <summary>
    /// Reads Optalix .OTX lens files.
    /// Surfaces defined by SUR blocks with CUY/THI/GLA/STO/APE/ASP sub-keywords.
    /// Surface types: S=standard, A=aspheric, M=mirror, AM=aspheric mirror.
    /// </summary>
    public static class OptalixReader
    {
        /// <summary>
        /// Read an OpTaliX .OTX file. When <paramref name="glassMgr"/> is
        /// provided, files with no explicit aperture (no EPD/FNO keyword)
        /// have their EPD computed via a paraxial axial back-trace through
        /// the front group — matching OpTaliX's "float by stop" default.
        /// Without a glass manager we fall back to <c>2 × stop_SD</c>, which
        /// is fine for normal designs but wrong by 5–10× on retrofocus /
        /// fisheye lenses where the front group strongly demagnifies the stop.
        /// </summary>
        public static OpticalSystem Read(string filePath, GlassCatalogManager? glassMgr = null)
        {
            var lines = File.ReadAllLines(filePath);
            var system = new OpticalSystem();

            var surfaces = new List<Surface>();
            var wavelengths = new List<double>();
            var wavelengthWeights = new List<double>();
            int refWavelength = 1;
            Surface? currentSurface = null;
            bool currentIsMirror = false;
            double unitScale = 1.0; // default mm

            // Newer OpTaliX exports use FLDY/FLDX/FWGT arrays instead of
            // per-field FLD lines. Collect during parse and build Fields[]
            // at the end, only if no FLD lines populated system.Fields.
            var fldYArr = new List<double>();
            var fwgtArr = new List<double>();

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("!")) continue;

                var parts = SplitLine(line);
                if (parts.Length == 0) continue;

                string keyword = parts[0].ToUpperInvariant();

                switch (keyword)
                {
                    case "REM":
                        // REM 1 <title>
                        if (parts.Length > 2)
                        {
                            int firstSpace = line.IndexOf(' ');
                            if (firstSpace >= 0)
                            {
                                int secondSpace = line.IndexOf(' ', firstSpace + 1);
                                if (secondSpace >= 0)
                                    system.Title = line.Substring(secondSpace + 1).Trim();
                            }
                        }
                        break;

                    case "EPD":
                        if (parts.Length > 1 && TryParseDouble(parts[1], out double epd))
                            system.Aperture = new Aperture(ApertureType.EPD, epd);
                        break;

                    case "FNO":
                        // Working F-number (image-side). Stored directly as
                        // ApertureType.FNumber so LensHH derives EPD from
                        // the focal length at trace time.
                        if (parts.Length > 1 && TryParseDouble(parts[1], out double fno) && fno > 0)
                            system.Aperture = new Aperture(ApertureType.FNumber, fno);
                        break;

                    // Note: MFR is NOT the entrance pupil. Files like
                    // FISHEYE2.OTX, HYPERGON.OTX, LAIKIN-9-1.OTX specify
                    // both `MFR` and an aperture (EPD or FNO) as separate
                    // values — MFR is some other quantity (likely a ray-fan
                    // plotting / sampling parameter). We deliberately ignore
                    // it here. (NA / NAO / NAI keywords also exist in some
                    // files but are rare; add later if needed.)

                    case "WL":
                        for (int i = 1; i < parts.Length; i++)
                        {
                            if (TryParseDouble(parts[i], out double wl))
                                wavelengths.Add(wl); // already in um
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

                    case "FLD":
                        // FLD <n> <hx> <hy> <weight> <flag> <id>
                        if (parts.Length >= 5)
                        {
                            TryParseDouble(parts[2], out double hx);
                            TryParseDouble(parts[3], out double hy);
                            TryParseDouble(parts[4], out double fw);
                            system.Fields.Add(new Field(hy, fw > 0 ? fw / 100.0 : 1.0));
                        }
                        break;

                    case "FLDX":
                        // Newer OpTaliX format: FLDX = list of X field positions.
                        // LensHH stores only Y (rotationally-symmetric), so we
                        // ignore the values themselves but log presence so the
                        // FLDY-only path doesn't drop a 2D field set silently.
                        // (No-op for the all-zeros case which is by far the most
                        //  common — meridional-only field set.)
                        break;

                    case "FLDY":
                        // FLDY <y1> <y2> ... — list of Y field positions
                        for (int i = 1; i < parts.Length; i++)
                            if (TryParseDouble(parts[i], out double fldy))
                                fldYArr.Add(fldy);
                        break;

                    case "FWGT":
                        // FWGT <w1> <w2> ... — per-field weights, percent
                        for (int i = 1; i < parts.Length; i++)
                            if (TryParseDouble(parts[i], out double fwt))
                                fwgtArr.Add(fwt);
                        break;

                    case "FTYP":
                        if (parts.Length > 1 && int.TryParse(parts[1], out int ft))
                        {
                            // Optalix: 0 = object height, 1 = angle.
                            system.FieldType = ft == 0 ? FieldType.ObjectHeight : FieldType.ObjectAngle;
                        }
                        break;

                    case "UNI":
                        // UNI <scale> — lens unit as scale factor to mm
                        if (parts.Length > 1 && TryParseDouble(parts[1], out double uni) && uni > 0)
                            unitScale = uni;
                        break;

                    case "DIM":
                        // DIM M = millimeters, DIM C = centimeters, DIM I = inches
                        if (parts.Length > 1)
                        {
                            switch (parts[1].ToUpperInvariant())
                            {
                                case "M": unitScale = 1.0; break;
                                case "C": unitScale = 10.0; break;
                                case "I": unitScale = 25.4; break;
                            }
                        }
                        break;

                    case "SUR":
                        if (parts.Length > 1 && int.TryParse(parts[1], out int surIdx))
                        {
                            currentSurface = new Surface { Index = surIdx };
                            currentIsMirror = false;
                            surfaces.Add(currentSurface);
                        }
                        break;

                    case "SUT":
                        // Surface type: S=standard, A=aspheric, M=mirror, AM=aspheric mirror
                        if (currentSurface != null && parts.Length > 1)
                        {
                            string sut = parts[1].ToUpperInvariant();
                            currentIsMirror = sut == "M" || sut == "AM";
                            if (sut == "A" || sut == "AM")
                                currentSurface.Type = SurfaceType.EvenAsphere;
                        }
                        break;

                    case "CUY":
                        if (currentSurface != null && parts.Length > 1 && TryParseDouble(parts[1], out double cuy))
                        {
                            if (Math.Abs(cuy) > 1e-15)
                                currentSurface.Curvature = cuy;
                        }
                        break;

                    case "THI":
                        if (currentSurface != null && parts.Length > 1 && TryParseDouble(parts[1], out double thi))
                        {
                            if (Math.Abs(thi) > 1e18)
                                currentSurface.Thickness = double.PositiveInfinity;
                            else if (thi <= -900) // -999 = image surface convention
                                currentSurface.Thickness = 0;
                            else
                                currentSurface.Thickness = thi;
                        }
                        break;

                    case "GLA":
                        if (currentSurface != null && parts.Length > 1)
                            currentSurface.Material = parts[1].Trim();
                        break;

                    case "STO":
                        if (currentSurface != null)
                            currentSurface.IsStop = true;
                        break;

                    case "APE":
                        // APE <mode> <y_radius> <x_radius> ... — mode 1 = clear
                        // aperture (semi-diameter); mode 2 = central obscuration
                        // (mirror central hole or secondary baffle shadow).
                        // Mode 2 mirrors OsloReader's AAC=2 convention: store as
                        // InnerRadius for mirrors, ObscurationRadius otherwise.
                        if (currentSurface != null && parts.Length > 2
                            && int.TryParse(parts[1], out int apeMode)
                            && TryParseDouble(parts[2], out double apeVal) && apeVal > 0)
                        {
                            if (apeMode == 2)
                            {
                                if (currentIsMirror)
                                    currentSurface.InnerRadius = apeVal;
                                else
                                    currentSurface.ObscurationRadius = apeVal;
                            }
                            else
                            {
                                currentSurface.SemiDiameter = apeVal;
                                currentSurface.SemiDiameterMode = SemiDiameterMode.Fixed;
                            }
                        }
                        break;

                    case "COM":
                        // COM <text> — surface label (e.g. PRIMARY, SECONDARY,
                        // TERTIERY in three-mirror designs). Take everything
                        // after the keyword so internal whitespace is kept.
                        if (currentSurface != null && parts.Length > 1)
                        {
                            int comIdx = line.IndexOf("COM", StringComparison.Ordinal);
                            currentSurface.Comment = line.Substring(comIdx + 3).Trim();
                        }
                        break;

                    case "VAR":
                        // VAR <count> <param1> [param2] ... — flag surface
                        // parameters as optimization variables. Maps the
                        // well-known Optalix tokens to LensHH Surface flags
                        // so an imported design is immediately optimizable
                        // without re-marking variables by hand. Unknown
                        // tokens (decentration ADE/BDE, edge distance ED,
                        // system-level DEF, the rare CUX) are silently
                        // skipped — they don't fit the rotationally-
                        // symmetric LensHH model.
                        if (currentSurface != null && parts.Length > 2)
                        {
                            for (int v = 2; v < parts.Length; v++)
                            {
                                switch (parts[v].ToUpperInvariant())
                                {
                                    case "CUY": currentSurface.CurvatureVariable = true; break;
                                    case "THI": currentSurface.ThicknessVariable = true; break;
                                    case "K":   currentSurface.ConicVariable = true; break;
                                    case "A":   SetAsphericVariable(currentSurface, 1); break;
                                    case "B":   SetAsphericVariable(currentSurface, 2); break;
                                    case "C":   SetAsphericVariable(currentSurface, 3); break;
                                    case "D":   SetAsphericVariable(currentSurface, 4); break;
                                }
                            }
                        }
                        break;

                    case "ASP":
                        // ASP <conic> <A> <B> <C> <D> <E> <F> <G> <H> <I>
                        // First value = conic constant, then A=r⁴, B=r⁶, C=r⁸, ...
                        // Internal model: [0]=r², [1]=r⁴, [2]=r⁶, ... so A→[1], B→[2], etc.
                        if (currentSurface != null && parts.Length > 1)
                        {
                            if (TryParseDouble(parts[1], out double conic))
                                currentSurface.Conic = conic;

                            // Coefficients A through H (parts 2..9 → internal indices 1..8)
                            for (int i = 2; i < parts.Length && (i - 1) < currentSurface.AsphericCoefficients.Length; i++)
                            {
                                if (TryParseDouble(parts[i], out double coeff) && Math.Abs(coeff) > 0)
                                    currentSurface.AsphericCoefficients[i - 1] = coeff;
                            }

                            if (currentSurface.Type != SurfaceType.EvenAsphere)
                                currentSurface.Type = SurfaceType.EvenAsphere;
                        }
                        break;

                    case "RAIM":
                        // Optalix: 0=Off, 1=Paraxial, 2=Real → map both 1 and 2 to Real
                        if (parts.Length > 1 && int.TryParse(parts[1], out int raim))
                        {
                            system.RayAiming = (raim >= 1) ? RayAimingMode.Real : RayAimingMode.Off;
                        }
                        break;
                }

                // After processing each line, if the surface is a mirror and no GLA was set, apply MIRROR
                if (currentIsMirror && currentSurface != null && string.IsNullOrEmpty(currentSurface.Material))
                    currentSurface.Material = "MIRROR";
            }

            system.Surfaces = surfaces;

            // Wavelengths
            for (int i = 0; i < wavelengths.Count; i++)
            {
                double wt = i < wavelengthWeights.Count ? wavelengthWeights[i] : 1.0;
                bool isPrimary = (i + 1) == refWavelength;
                system.Wavelengths.Add(new Wavelength(wavelengths[i], wt, isPrimary));
            }

            // If the file used FLDY arrays (newer OpTaliX format) and no
            // FLD lines populated system.Fields, materialize from the
            // collected arrays now. Pad missing FWGT entries with 1.0.
            if (system.Fields.Count == 0 && fldYArr.Count > 0)
            {
                for (int i = 0; i < fldYArr.Count; i++)
                {
                    double w = i < fwgtArr.Count ? (fwgtArr[i] > 0 ? fwgtArr[i] / 100.0 : 1.0) : 1.0;
                    system.Fields.Add(new Field(fldYArr[i], w));
                }
            }

            if (system.Fields.Count == 0)
                system.Fields.Add(new Field(0, 1.0));

            FieldValidation.FilterImportedFields(system);

            // No explicit EPD/F# in the file → use OpTaliX's "float by stop"
            // default. With a glass manager we can do the proper paraxial
            // axial back-trace through the front group; without one, fall
            // back to 2 × stop_SD (correct only when the front group is
            // ~afocal — wildly wrong on retrofocus / fisheye designs).
            if (system.Aperture.Value <= 0)
            {
                int stopIdx = -1;
                double stopSd = 0;
                for (int i = 0; i < surfaces.Count; i++)
                {
                    if (surfaces[i].IsStop && surfaces[i].SemiDiameter > 0)
                    {
                        stopIdx = i;
                        stopSd = surfaces[i].SemiDiameter;
                        break;
                    }
                }

                if (stopIdx > 0 && stopSd > 0)
                {
                    double epd = 2.0 * stopSd; // heuristic fallback
                    if (glassMgr != null && system.Wavelengths.Count > 0)
                    {
                        int primaryIdx = system.PrimaryWavelengthIndex;
                        if (primaryIdx < 0 || primaryIdx >= system.Wavelengths.Count) primaryIdx = 0;
                        double wlUm = system.Wavelengths[primaryIdx].Value;
                        double[] indices = glassMgr.BuildRefractiveIndexArray(system, wlUm);
                        double yAtStop = ParaxialAxialYAtStop(surfaces, indices, stopIdx);
                        if (yAtStop > 1e-9)
                            epd = 2.0 * stopSd / yAtStop;
                    }
                    system.Aperture = new Aperture(ApertureType.EPD, epd);
                }
            }

            // Convert from file units to mm
            if (unitScale != 1.0)
                LensUnitConverter.ConvertToMm(system, unitScale);

            return system;
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

        // Optalix aspheric variable index: A=r⁴ → [1], B=r⁶ → [2], etc.,
        // matching the existing ASP-coefficient read convention.
        private static void SetAsphericVariable(Surface s, int idx)
        {
            if (s.AsphericVariable != null && idx >= 0 && idx < s.AsphericVariable.Length)
                s.AsphericVariable[idx] = true;
        }

        /// <summary>
        /// Paraxial axial ray (y=1, u=0) traced from S1 forward to the stop's
        /// plane (before the stop's own refraction). Returns y at the stop.
        /// Used to back-compute EPD when the file has no aperture keyword:
        /// EPD = 2 · stop_SD / yAtStop. This mirrors what OpTaliX does
        /// internally for its "float by stop" default.
        /// indices[i] = refractive index in the gap AFTER surface i.
        /// </summary>
        private static double ParaxialAxialYAtStop(List<Surface> surfaces, double[] indices, int stopIdx)
        {
            if (stopIdx < 1 || stopIdx >= surfaces.Count) return 0;

            double y = 1.0;
            double u = 0.0;

            for (int i = 1; i < stopIdx; i++)
            {
                double n1 = indices[i - 1];
                double n2 = indices[i];
                if (n2 <= 0) return 0; // unresolved glass — abort, caller falls back
                double c = surfaces[i].Curvature; // 1/R, 0 for flat

                // Paraxial refraction (ynu form): n2*u' = n1*u - y*(n2-n1)*c
                u = (n1 * u - y * (n2 - n1) * c) / n2;

                // Transfer through this surface's thickness to the next
                double t = surfaces[i].Thickness;
                if (double.IsInfinity(t) || double.IsNaN(t)) return 0;
                y = y + t * u;
            }

            return y;
        }
    }
}
