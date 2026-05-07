using System.Globalization;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering.TextExport
{
    public static class ChromaticFocalShiftTextExport
    {
        public static string Export(ChromaticFocalShiftResult result, string title = "")
        {
            string u = result.IsAfocal ? "diopters" : "mm";
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(title);

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Max Focal Shift ({0})\t{1:E8}", u, result.MaxShift));
            sb.AppendLine();

            sb.AppendLine($"Wavelength (um)\tFocal Shift ({u})\tEFL (mm)");
            foreach (var pt in result.Points)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F6}\t{1:E8}\t{2:F6}", pt.Wavelength, pt.FocalShift, pt.Efl));
            }

            return sb.ToString();
        }
    }
}
