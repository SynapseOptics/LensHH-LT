using System.Globalization;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering.TextExport
{
    public static class ZernikeTextExport
    {
        public static string Export(ZernikeResult result, string title = "",
            double fieldValue = double.NaN, double wavelengthUm = double.NaN, string fieldUnit = "deg")
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(title);

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Ordering\t{0}", result.IsFringe ? "Fringe" : "Standard (Noll)"));
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
                "Number of Terms\t{0}", result.NumTerms));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Peak to Valley (waves)\t{0:F6}", result.PeakToValley));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "RMS Wavefront (waves)\t{0:F6}", result.RmsWavefront));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "RMS Fit Residual (waves)\t{0:E4}", result.RmsFit));
            sb.AppendLine();

            sb.AppendLine("Term\tCoefficient (waves)\tName");
            for (int k = 0; k < result.Coefficients.Length; k++)
            {
                string name = result.IsFringe
                    ? ZernikeCalculator.GetFringeTermName(k + 1)
                    : ZernikeCalculator.GetStandardTermName(k + 1);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Z{0}\t{1:E8}\t{2}", k + 1, result.Coefficients[k], name));
            }

            return sb.ToString();
        }
    }
}
