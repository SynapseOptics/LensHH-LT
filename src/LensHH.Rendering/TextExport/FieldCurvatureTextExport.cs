using System.Globalization;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering.TextExport
{
    public static class FieldCurvatureTextExport
    {
        public static string Export(FieldCurvatureResult result, string title = "",
            string fieldUnit = "deg")
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(title);
            sb.AppendLine();

            // Afocal systems report focus as optical power in diopters; focal
            // systems report the crossing distance in mm.
            string unit = result.IsAfocal ? "diopters" : "mm";
            sb.AppendLine($"Field ({fieldUnit})\tTan Focus ({unit})\tSag Focus ({unit})\tMedial Focus ({unit})");
            foreach (var pt in result.Points)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F4}\t{1:E8}\t{2:E8}\t{3:E8}",
                    pt.FieldY, pt.TangentialFocus, pt.SagittalFocus, pt.MedialFocus));
            }
            return sb.ToString();
        }
    }
}
