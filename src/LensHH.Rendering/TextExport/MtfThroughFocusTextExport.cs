using System.Globalization;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering.TextExport
{
    public static class MtfThroughFocusTextExport
    {
        public static string Export(MtfThroughFocusResult result, string title = "",
            double fieldValue = double.NaN, string fieldUnit = "deg",
            double wavelengthUm = double.NaN)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(title);

            string freqUnit = result.IsAfocal ? "cy/arc-min" : "cy/mm";
            string focusUnit = result.IsAfocal ? "diopters" : "mm";
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Spatial Frequency\t{0:F2} {1}", result.SpatialFrequency, freqUnit));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Field\t{0}", result.FieldIndex + 1));
            if (!double.IsNaN(fieldValue))
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Field Value ({0})\t{1:F4}", fieldUnit, fieldValue));
            if (!double.IsNaN(wavelengthUm))
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Wavelength (\u00b5m)\t{0:F6}", wavelengthUm));
            sb.AppendLine();

            sb.AppendLine($"Focus Shift ({focusUnit})\tTangential\tSagittal");
            foreach (var pt in result.Points)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F6}\t{1:F6}\t{2:F6}", pt.FocusShift, pt.Tangential, pt.Sagittal));
            }

            return sb.ToString();
        }
    }
}
