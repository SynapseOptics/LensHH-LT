using System.Globalization;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering.TextExport
{
    public static class SeidelTextExport
    {
        public static string Export(SeidelResult result, string title = "")
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(title);
            sb.AppendLine();

            sb.AppendLine("Seidel sums in system length units (mm).");
            sb.AppendLine("S1=Spherical, S2=Coma, S3=Astigmatism, S4=Petzval, S5=Distortion, CL=Axial Chromatic, CT=Lateral Chromatic");
            sb.AppendLine();
            sb.AppendLine("Surface\tS1 (mm)\tS2 (mm)\tS3 (mm)\tS4 (mm)\tS5 (mm)\tCL (mm)\tCT (mm)");
            foreach (var sd in result.SurfaceData)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0}\t{1:E8}\t{2:E8}\t{3:E8}\t{4:E8}\t{5:E8}\t{6:E8}\t{7:E8}",
                    sd.SurfaceIndex, sd.S1, sd.S2, sd.S3, sd.S4, sd.S5, sd.CL, sd.CT));
            }
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Total\t{0:E8}\t{1:E8}\t{2:E8}\t{3:E8}\t{4:E8}\t{5:E8}\t{6:E8}",
                result.S1, result.S2, result.S3, result.S4, result.S5, result.CL, result.CT));

            return sb.ToString();
        }
    }
}
