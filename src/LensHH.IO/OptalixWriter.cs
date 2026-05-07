using System;
using System.Globalization;
using System.Linq;
using System.Text;
using LensHH.Core.Enums;
using LensHH.Core.Models;

namespace LensHH.Core.IO
{
    /// <summary>
    /// Writes Optalix .OTX lens files.
    /// </summary>
    public static class OptalixWriter
    {
        public static void Write(OpticalSystem system, string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("VERS 11.82");
            sb.AppendLine($"FILE {filePath}");

            if (!string.IsNullOrEmpty(system.Title))
                sb.AppendLine($"REM 1 {system.Title}");

            sb.AppendLine("RAIM 0");
            if (system.RayAiming == RayAimingMode.Real)
                sb.AppendLine("RAIM 2");

            // Aperture
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "EPD {0:G14}", system.Aperture.Value));

            // Wavelengths
            if (system.Wavelengths.Count > 0)
            {
                var wlSb = new StringBuilder("WL");
                var wtSb = new StringBuilder("WTW");
                foreach (var wl in system.Wavelengths)
                {
                    wlSb.Append(string.Format(CultureInfo.InvariantCulture, " {0:F7}", wl.Value));
                    wtSb.Append(string.Format(CultureInfo.InvariantCulture, " {0:G}", wl.Weight));
                }
                sb.AppendLine(wlSb.ToString());
                sb.AppendLine(wtSb.ToString());
                sb.AppendLine($"REF {system.PrimaryWavelengthIndex + 1}");
            }

            // Fields
            // Optalix FTYP: 0 = object height, 1 = angle.
            int ftyp = system.FieldType == FieldType.ObjectAngle ? 1 : 0;
            sb.AppendLine($"FTYP {ftyp}");
            sb.AppendLine($"NFLD {system.Fields.Count}");
            for (int i = 0; i < system.Fields.Count; i++)
            {
                var f = system.Fields[i];
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "FLD {0} {1:G14} {2:G14} {3:F0} 1 0",
                    i + 1, 0.0, f.Y, f.Weight * 100));
            }

            // Surfaces
            sb.AppendLine("! Surface data :");
            for (int i = 0; i < system.Surfaces.Count; i++)
            {
                var s = system.Surfaces[i];
                sb.AppendLine($"SUR {i}");

                // Determine surface type
                bool isMirror = s.Material != null &&
                    s.Material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase);
                bool hasAspheric = s.Type == SurfaceType.EvenAsphere
                    || s.Conic != 0
                    || (s.AsphericCoefficients != null && s.AsphericCoefficients.Any(c => c != 0));

                string sut;
                if (hasAspheric && isMirror) sut = "AM";
                else if (hasAspheric) sut = "A";
                else if (isMirror) sut = "M";
                else sut = "S";
                sb.AppendLine($"  SUT {sut}");

                double cuy = double.IsInfinity(s.Radius) ? 0.0 : s.Curvature;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  CUY {0:E16}", cuy));

                double thi;
                if (i == system.Surfaces.Count - 1)
                    thi = -999.0; // Image surface convention
                else if (double.IsPositiveInfinity(s.Thickness))
                    thi = 1e20;
                else
                    thi = s.Thickness;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  THI {0:E16}", thi));

                if (!string.IsNullOrEmpty(s.Material) && !isMirror)
                    sb.AppendLine($"  GLA {s.Material}");

                if (s.IsStop)
                    sb.AppendLine("  STO");

                if (s.SemiDiameter > 0)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  APE  1 {0:G12} {0:G12} 0 0 0 1 0 0 1 ''",
                        s.SemiDiameter));
                }

                // APE 2 = central obscuration. Round-trip the value the
                // reader stashed in InnerRadius (mirror central hole) or
                // ObscurationRadius (baffle / secondary shadow on a non-
                // mirror surface). Trailing flags follow the Optalix
                // pattern observed in 43-29 Ritchey-Chretien:
                //   APE 2 r r 0 0 0 1 0 1
                // where the final '1' marks the zone as obscured.
                double obscR = s.InnerRadius > 0 ? s.InnerRadius
                             : s.ObscurationRadius > 0 ? s.ObscurationRadius
                             : 0;
                if (obscR > 0)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  APE  2 {0:G12} {0:G12} 0 0 0 1 0 1",
                        obscR));
                }

                // COM = surface label (PRIMARY, SECONDARY, etc.).
                if (!string.IsNullOrWhiteSpace(s.Comment))
                    sb.AppendLine($"  COM {s.Comment.Trim()}");

                // VAR <count> <param1> [...] — variable flags for
                // optimization. Symmetric with the reader: A/B/C/D are
                // the Optalix names for r⁴/r⁶/r⁸/r¹⁰ aspheric coefficients
                // (internal indices 1..4). Decentration / edge-distance
                // variables aren't modeled in LensHH and so are never
                // emitted.
                var vars = new System.Collections.Generic.List<string>();
                if (s.CurvatureVariable) vars.Add("CUY");
                if (s.ThicknessVariable) vars.Add("THI");
                if (s.ConicVariable)     vars.Add("K");
                if (s.AsphericVariable != null)
                {
                    string[] aspNames = { "A", "B", "C", "D" }; // r⁴..r¹⁰
                    for (int v = 0; v < aspNames.Length; v++)
                    {
                        int idx = v + 1;
                        if (idx < s.AsphericVariable.Length && s.AsphericVariable[idx])
                            vars.Add(aspNames[v]);
                    }
                }
                if (vars.Count > 0)
                    sb.AppendLine($"  VAR {vars.Count} {string.Join(" ", vars)}");

                // Aspheric data: conic + 8 coefficients (A=r⁴, B=r⁶, ...)
                // Internal model: [0]=r², [1]=r⁴, [2]=r⁶, ... → write [1]..[8] as A..H
                if (hasAspheric)
                {
                    var aspSb = new StringBuilder("  ASP ");
                    aspSb.Append(string.Format(CultureInfo.InvariantCulture, " {0:G14}", s.Conic));
                    var aspCoeffs = s.AsphericCoefficients;
                    for (int c = 0; c < 8; c++)
                    {
                        int idx = c + 1; // skip [0] (r² term, not used in Optalix)
                        double coeff = (aspCoeffs != null && idx < aspCoeffs.Length) ? aspCoeffs[idx] : 0.0;
                        aspSb.Append(string.Format(CultureInfo.InvariantCulture, " {0:E10}", coeff));
                    }
                    // Two trailing zeros (I and J terms)
                    aspSb.Append(" 0.000000000 0.000000000");
                    sb.AppendLine(aspSb.ToString());
                }
            }

            System.IO.File.WriteAllText(filePath, sb.ToString());
        }
    }
}
