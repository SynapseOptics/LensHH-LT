using System.Globalization;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering.TextExport
{
    public static class FieldCurvatureDistortionTextExport
    {
        public static string Export(FieldCurvatureDistortionResult result, string title = "",
            string fieldUnit = "deg")
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(title);
            sb.AppendLine();

            string fcUnit = result.IsAfocal ? "diopters" : "mm";
            string distActual = result.IsAfocal ? "Real Cosine" : "Actual Height (mm)";
            string distIdeal = result.IsAfocal ? "Ref Cosine" : "Ideal Height (mm)";
            sb.AppendLine($"Field ({fieldUnit})\tTan Focus ({fcUnit})\tSag Focus ({fcUnit})\tMedial Focus ({fcUnit})\tDistortion (%)\t{distActual}\t{distIdeal}");

            int count = System.Math.Min(result.FieldCurvaturePoints.Count, result.DistortionPoints.Count);
            for (int i = 0; i < count; i++)
            {
                var fc = result.FieldCurvaturePoints[i];
                var dist = result.DistortionPoints[i];
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F4}\t{1:E8}\t{2:E8}\t{3:E8}\t{4:F6}\t{5:F6}\t{6:F6}",
                    fc.FieldY, fc.TangentialFocus, fc.SagittalFocus, fc.MedialFocus,
                    dist.Distortion, dist.ActualHeight, dist.IdealHeight));
            }

            return sb.ToString();
        }
    }
}
