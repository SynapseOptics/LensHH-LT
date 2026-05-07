using System.Globalization;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering.TextExport
{
    public static class RelativeIlluminationTextExport
    {
        public static string Export(RelativeIlluminationResult result, string title = "",
            string fieldUnit = "deg")
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(title);
            sb.AppendLine();

            sb.AppendLine($"Field ({fieldUnit})\tRelative Illumination");
            foreach (var pt in result.Points)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F4}\t{1:F6}", pt.FieldY, pt.RelativeIllumination));
            }

            return sb.ToString();
        }
    }
}
