using System.Globalization;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering.TextExport
{
    public static class SpotDiagramTextExport
    {
        /// <param name="wavelengthsUm">Wavelength values per index (for ray breakdown)</param>
        public static string Export(SpotDiagramResult result, string title = "",
            double[]? wavelengthsUm = null,
            double fieldValue = double.NaN, string fieldUnit = "deg")
        {
            string u = result.IsAfocal ? "arcmin" : "mm";
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(title);

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Field\t{0}", result.FieldIndex + 1));
            if (!double.IsNaN(fieldValue))
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Field Value ({0})\t{1:F4}", fieldUnit, fieldValue));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "RMS Radius ({0})\t{1:E8}", u, result.RmsRadius));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "RMS X ({0})\t{1:E8}", u, result.RmsX));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "RMS Y ({0})\t{1:E8}", u, result.RmsY));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "GEO Radius ({0})\t{1:E8}", u, result.GeoRadius));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Chief Ray X ({0})\t{1:E8}", u, result.ChiefRayX));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Chief Ray Y ({0})\t{1:E8}", u, result.ChiefRayY));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Centroid X ({0})\t{1:E8}", u, result.CentroidX));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Centroid Y ({0})\t{1:E8}", u, result.CentroidY));
            sb.AppendLine();

            // Ray statistics
            sb.AppendLine("Ray Statistics");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Rays Attempted\t{0}", result.RaysAttempted));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Rays Succeeded\t{0}", result.Points.Count));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Rays Failed\t{0}", result.RaysFailed));
            if (result.RaysAttempted > 0)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Success Rate\t{0:F1}%", 100.0 * result.Points.Count / result.RaysAttempted));
            sb.AppendLine();

            // Per-wavelength breakdown
            if (result.PerWavelengthRays.Count > 0)
            {
                sb.AppendLine("Per-Wavelength Ray Counts");
                sb.AppendLine("Wavelength\tAttempted\tSucceeded\tFailed");
                int wlDigits = wavelengthsUm != null
                    ? LabelFormat.WavelengthDigits(wavelengthsUm)
                    : 4;
                string wlFormat = "{0:F" + wlDigits + "} um";
                for (int w = 0; w < result.PerWavelengthRays.Count; w++)
                {
                    var (attempted, succeeded) = result.PerWavelengthRays[w];
                    string wlLabel = wavelengthsUm != null && w < wavelengthsUm.Length
                        ? string.Format(CultureInfo.InvariantCulture, wlFormat, wavelengthsUm[w])
                        : $"W{w + 1}";
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0}\t{1}\t{2}\t{3}", wlLabel, attempted, succeeded, attempted - succeeded));
                }
                sb.AppendLine();
            }

            // Ray hit data
            sb.AppendLine($"X ({u})\tY ({u})\tWavelength Index");
            foreach (var pt in result.Points)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:E8}\t{1:E8}\t{2}", pt.X, pt.Y, pt.WavelengthIndex));
            }

            return sb.ToString();
        }
    }
}
