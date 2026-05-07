using System;
using System.Globalization;
using System.Linq;
using System.Text;
using LensHH.Core.Enums;
using LensHH.Core.Models;

namespace LensHH.Core.IO
{
    /// <summary>
    /// Writes Code V .seq lens files.
    /// </summary>
    public static class CodeVWriter
    {
        public static void Write(OpticalSystem system, string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("! Lens exported from LensHH-LT");
            sb.AppendLine("RDM;LEN");
            sb.AppendLine("DIM M");

            // Aperture
            if (system.Aperture.Type == ApertureType.EPD)
                sb.AppendLine(D("EPD", system.Aperture.Value));
            else
                sb.AppendLine(D("FNO", system.Aperture.Value));

            // Title
            if (!string.IsNullOrEmpty(system.Title))
                sb.AppendLine($"TIT '{system.Title}'");

            // Wavelengths in nm
            if (system.Wavelengths.Count > 0)
            {
                var wlSb = new StringBuilder("WL  ");
                var wtSb = new StringBuilder("WTW  ");
                foreach (var wl in system.Wavelengths)
                {
                    wlSb.Append(string.Format(CultureInfo.InvariantCulture, " {0:F4}", wl.Value * 1000.0));
                    wtSb.Append(string.Format(CultureInfo.InvariantCulture, " {0:G}", wl.Weight));
                }
                sb.AppendLine(wlSb.ToString());
                sb.AppendLine(wtSb.ToString());
                sb.AppendLine($"REF {system.PrimaryWavelengthIndex + 1}");
            }

            // Fields
            if (system.Fields.Count > 0)
            {
                var xSb = new StringBuilder("XAN  ");
                var ySb = new StringBuilder("YAN  ");
                var fwSb = new StringBuilder("WTF  ");
                foreach (var f in system.Fields)
                {
                    xSb.Append("  0.00000");
                    ySb.Append(string.Format(CultureInfo.InvariantCulture, " {0:F5}", f.Y));
                    fwSb.Append(string.Format(CultureInfo.InvariantCulture, " {0:F0}", f.Weight * 100));
                }
                sb.AppendLine(xSb.ToString());
                sb.AppendLine(ySb.ToString());
                sb.AppendLine(fwSb.ToString());
            }

            // Surfaces
            for (int i = 0; i < system.Surfaces.Count; i++)
            {
                var s = system.Surfaces[i];
                string prefix;
                if (i == 0) prefix = "SO";
                else if (i == system.Surfaces.Count - 1) prefix = "SI";
                else prefix = "S";

                double radius = double.IsInfinity(s.Radius) || double.IsNaN(s.Radius) ? 0.0 : s.Radius;
                double thickness = double.IsPositiveInfinity(s.Thickness) ? 1e20 : s.Thickness;

                bool isMirror = s.Material != null &&
                    s.Material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase);
                string material = isMirror ? "REFL" :
                    string.IsNullOrEmpty(s.Material) ? "AIR" :
                    CatalogNamesToCodeV(s.Material!);

                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0} {1:G14} {2:G14} {3}", prefix, radius, thickness, material));

                if (s.IsStop)
                    sb.AppendLine("  STO");
                if (s.SemiDiameter > 0 && s.SemiDiameterMode == SemiDiameterMode.Fixed)
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  CIR {0:G14}", s.SemiDiameter));

                // Aspheric coefficients
                var aspCoeffs = s.AsphericCoefficients;
                bool hasAspCoeffs = aspCoeffs != null && aspCoeffs.Any(c => c != 0);
                if (hasAspCoeffs && aspCoeffs != null)
                {
                    // Internal: [0]=r², [1]=r⁴, [2]=r⁶, ... → Code V: A=r⁴([1]), B=r⁶([2]), ...
                    var aspSb = new StringBuilder("  ASP");
                    string[] coeffNames = { "A", "B", "C", "D", "E", "F", "G", "H" };
                    for (int c = 0; c < coeffNames.Length; c++)
                    {
                        int idx = c + 1;
                        double coeff = idx < aspCoeffs.Length ? aspCoeffs[idx] : 0.0;
                        aspSb.Append(string.Format(CultureInfo.InvariantCulture,
                            " ; {0} {1:E10}", coeffNames[c], coeff));
                    }
                    sb.AppendLine(aspSb.ToString());
                }

                // Conic constant
                if (s.Conic != 0)
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  CON ; K {0:G14}", s.Conic));
            }

            sb.AppendLine("GO");
            System.IO.File.WriteAllText(filePath, sb.ToString());
        }

        private static string D(string keyword, double value)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} {1:G14}", keyword, value);
        }

        /// <summary>
        /// Translate a LensHH/Zemax-style glass name to the form Code V
        /// expects. Schott "N-prefix" glasses are written without the dash
        /// in Code V (<c>N-SF10</c> → <c>NSF10</c>); inverse of
        /// <c>CodeVReader.CodeVNamesToCatalog</c>. All other names pass
        /// through unchanged.
        /// </summary>
        private static string CatalogNamesToCodeV(string name)
        {
            if (name.Length >= 2 && name[0] == 'N' && name[1] == '-')
                return "N" + name.Substring(2);
            return name;
        }
    }
}
