using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering.TextExport
{
    public static class LateralColorTextExport
    {
        /// <param name="wavelengthsUm">Wavelength values per index (for column headers)</param>
        public static string Export(LateralColorResult result, string title = "",
            double[]? wavelengthsUm = null, string fieldUnit = "deg")
        {
            string u = result.IsAfocal ? "arcmin" : "µm";
            double scale = result.IsAfocal ? 1.0 : 1000.0; // mm to µm for focal
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(title);

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Max Lateral Color ({0})\t{1:E8}", u, result.MaxLateralColor * scale));
            sb.AppendLine();

            var byWl = result.Points.GroupBy(p => p.WavelengthIndex).OrderBy(g => g.Key).ToList();

            sb.Append($"Field Y ({fieldUnit})");
            foreach (var g in byWl)
            {
                int wIdx = g.Key;
                string wlLabel = wavelengthsUm != null && wIdx < wavelengthsUm.Length
                    ? string.Format(CultureInfo.InvariantCulture, "{0:F4}um", wavelengthsUm[wIdx])
                    : $"W{wIdx + 1}";
                sb.Append($"\t{wlLabel} Shift ({u})");
            }
            sb.AppendLine();

            var allFields = result.Points.Select(p => p.FieldY).Distinct().OrderBy(f => f).ToList();

            foreach (double field in allFields)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F4}", field));
                foreach (var g in byWl)
                {
                    var pt = g.FirstOrDefault(p => System.Math.Abs(p.FieldY - field) < 1e-8);
                    if (pt != null)
                        sb.Append(string.Format(CultureInfo.InvariantCulture, "\t{0:E8}", pt.LateralShift * scale));
                    else
                        sb.Append("\t");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
