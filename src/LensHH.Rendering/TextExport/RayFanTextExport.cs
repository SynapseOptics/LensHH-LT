using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering.TextExport
{
    public static class RayFanTextExport
    {
        /// <param name="wavelengthsUm">Wavelength values per index (for column headers)</param>
        public static string Export(RayFanResult result, string title = "",
            double[]? wavelengthsUm = null,
            double fieldValue = double.NaN, string fieldUnit = "deg")
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(title);

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Field\t{0}", result.FieldIndex + 1));
            if (!double.IsNaN(fieldValue))
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Field Value ({0})\t{1:F4}", fieldUnit, fieldValue));
            string u = result.IsAfocal ? "arcmin" : "mm";
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Max Aberration ({0})\t{1:E8}", u, result.MaxAberration));
            sb.AppendLine();

            sb.AppendLine("Tangential Fan (EY vs PY)");
            ExportFan(sb, result.TangentialFan, wavelengthsUm, u);
            sb.AppendLine();

            sb.AppendLine("Sagittal Fan (EX vs PX)");
            ExportFan(sb, result.SagittalFan, wavelengthsUm, u);

            return sb.ToString();
        }

        private static void ExportFan(StringBuilder sb, List<RayFanPoint> points,
            double[]? wavelengthsUm, string unit = "mm")
        {
            var byWl = points.GroupBy(p => p.WavelengthIndex).OrderBy(g => g.Key).ToList();

            sb.Append("Pupil Coordinate");
            foreach (var g in byWl)
            {
                int wIdx = g.Key;
                string wlLabel = wavelengthsUm != null && wIdx < wavelengthsUm.Length
                    ? string.Format(CultureInfo.InvariantCulture, "{0:F4}um", wavelengthsUm[wIdx])
                    : $"W{wIdx + 1}";
                sb.Append($"\t{wlLabel} Aberration ({unit})");
            }
            sb.AppendLine();

            var allCoords = points.Select(p => p.PupilCoordinate).Distinct().OrderBy(c => c).ToList();

            foreach (double coord in allCoords)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F6}", coord));
                foreach (var g in byWl)
                {
                    var pt = g.FirstOrDefault(p => System.Math.Abs(p.PupilCoordinate - coord) < 1e-8);
                    if (pt != null)
                        sb.Append(string.Format(CultureInfo.InvariantCulture, "\t{0:E8}", pt.Aberration));
                    else
                        sb.Append("\t***");
                }
                sb.AppendLine();
            }
        }
    }
}
