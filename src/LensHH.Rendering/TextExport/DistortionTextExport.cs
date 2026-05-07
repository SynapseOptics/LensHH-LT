using System.Globalization;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering.TextExport
{
    public static class DistortionTextExport
    {
        public static string Export(DistortionResult result, string title = "",
            string fieldUnit = "deg")
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(title);

            string typeLabel = result.Type == DistortionType.FTheta ? "F-Theta" : "F-Tan(Theta)";
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Distortion Type\t{0}", typeLabel));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Max Distortion (%)\t{0:F6}", result.MaxDistortion));
            sb.AppendLine();

            // Afocal: chief-ray Y direction cosines.
            // Focal: image heights in mm.
            string actualLabel = result.IsAfocal ? "Real Cosine" : "Actual Height (mm)";
            string idealLabel  = result.IsAfocal ? "Ref Cosine"  : "Ideal Height (mm)";
            sb.AppendLine($"Field ({fieldUnit})\tDistortion (%)\t{actualLabel}\t{idealLabel}");
            foreach (var pt in result.Points)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F4}\t{1:F6}\t{2:F6}\t{3:F6}",
                    pt.FieldY, pt.Distortion, pt.ActualHeight, pt.IdealHeight));
            }
            return sb.ToString();
        }
    }
}
