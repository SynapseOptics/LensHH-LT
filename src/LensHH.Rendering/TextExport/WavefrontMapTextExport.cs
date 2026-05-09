using System.Globalization;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering.TextExport
{
    public static class WavefrontMapTextExport
    {
        public static string Export(WavefrontResult result, string title = "",
            double fieldValue = double.NaN, double wavelengthUm = double.NaN, string fieldUnit = "deg")
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(title);

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Field\t{0}", result.FieldIndex + 1));
            if (!double.IsNaN(fieldValue))
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Field Value ({0})\t{1:F4}", fieldUnit, fieldValue));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Wavelength\t{0}", result.WavelengthIndex + 1));
            if (!double.IsNaN(wavelengthUm))
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Wavelength (µm)\t{0:F6}", wavelengthUm));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Grid Size\t{0}", result.GridSize));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Peak to Valley (waves)\t{0:F6}", result.PeakToValley));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "RMS Wavefront (waves)\t{0:F6}", result.RmsWavefront));
            sb.AppendLine();

            int n = result.GridSize;
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "OPD Grid ({0}x{0}), in waves. Empty cells = invalid (outside pupil).", n));

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    if (j > 0) sb.Append('\t');
                    if (result.Valid[i, j])
                        sb.Append(string.Format(CultureInfo.InvariantCulture,
                            "{0:E8}", result.Opd[i, j]));
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
