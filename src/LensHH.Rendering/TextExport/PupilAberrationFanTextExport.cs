using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering.TextExport
{
    public static class PupilAberrationFanTextExport
    {
        /// <summary>
        /// Export pupil aberration fan data in tabular text format.
        /// </summary>
        public static string Export(PupilAberrationResult result, string title = "",
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
            sb.AppendLine("Units are percent.");
            sb.AppendLine();

            sb.AppendLine("Tangential Fan");
            ExportFan(sb, result.TangentialFan, wavelengthsUm);
            sb.AppendLine();

            sb.AppendLine("Sagittal Fan");
            ExportFan(sb, result.SagittalFan, wavelengthsUm);

            return sb.ToString();
        }

        private static void ExportFan(StringBuilder sb, List<PupilAberrationPoint> points,
            double[]? wavelengthsUm)
        {
            var byWl = points.GroupBy(p => p.WavelengthIndex).OrderBy(g => g.Key).ToList();

            int wlDigits = wavelengthsUm != null
                ? LabelFormat.WavelengthDigits(wavelengthsUm)
                : 6;
            string wlFormat = "{0:F" + wlDigits + "}";

            sb.Append("Pupil Coordinate");
            foreach (var g in byWl)
            {
                int wIdx = g.Key;
                string wlLabel = wavelengthsUm != null && wIdx < wavelengthsUm.Length
                    ? string.Format(CultureInfo.InvariantCulture, wlFormat, wavelengthsUm[wIdx])
                    : $"W{wIdx + 1}";
                sb.Append($"\t{wlLabel}");
            }
            sb.AppendLine();

            var allCoords = points.Select(p => p.PupilCoordinate).Distinct().OrderBy(c => c).ToList();

            foreach (double coord in allCoords)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F3}", coord));
                foreach (var g in byWl)
                {
                    var pt = g.FirstOrDefault(p => System.Math.Abs(p.PupilCoordinate - coord) < 1e-8);
                    if (pt != null)
                        sb.Append(string.Format(CultureInfo.InvariantCulture, "\t{0:F6}", pt.Aberration));
                    else
                        sb.Append("\t***");
                }
                sb.AppendLine();
            }
        }
    }
}
