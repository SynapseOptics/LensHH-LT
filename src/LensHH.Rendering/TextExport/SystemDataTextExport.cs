using System;
using System.Globalization;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering.TextExport
{
    public static class SystemDataTextExport
    {
        public static string Export(SystemDataResult result, string title = "")
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(title);
            sb.AppendLine();
            sb.AppendLine(result.IsAfocal ? "Afocal System" : "Focal System");
            sb.AppendLine();

            sb.AppendLine("Parameter\tValue\tUnit");

            if (result.IsAfocal)
            {
                Row(sb, "Angular Magnification", result.AngularMagnification, "x");
                sb.AppendLine();
                Row(sb, "Entrance Pupil Diameter", result.EntrancePupilDiameter, "mm");
                Row(sb, "Entrance Pupil Position", result.EntrancePupilPosition, "mm");
                Row(sb, "Exit Pupil Diameter", result.ExitPupilDiameter, "mm");
                Row(sb, "Exit Pupil Position", result.ExitPupilPosition, "mm");
                sb.AppendLine();
                Row(sb, "Total Track", result.TotalTrack, "mm");
                Row(sb, "Maximum Field", result.MaximumField, result.FieldUnit);
            }
            else
            {
                Row(sb, "Effective Focal Length", result.Efl, "mm");
                Row(sb, "Back Focal Length", result.Bfl, "mm");
                Row(sb, "Front Focal Length", result.Ffl, "mm");
                sb.AppendLine();
                Row(sb, "Image Space F/#", result.ImageSpaceFNumber, "");
                Row(sb, "Working F/#", result.WorkingFNumber, "");
                Row(sb, "Image Space NA", result.ImageSpaceNA, "");
                sb.AppendLine();
                Row(sb, "Entrance Pupil Diameter", result.EntrancePupilDiameter, "mm");
                Row(sb, "Entrance Pupil Position", result.EntrancePupilPosition, "mm");
                Row(sb, "Exit Pupil Diameter", result.ExitPupilDiameter, "mm");
                Row(sb, "Exit Pupil Position", result.ExitPupilPosition, "mm");
                sb.AppendLine();
                Row(sb, "Paraxial Image Height", result.ParaxialImageHeight, "mm");
                Row(sb, "Paraxial Magnification", result.ParaxialMagnification, "");
                sb.AppendLine();
                Row(sb, "Total Track", result.TotalTrack, "mm");
                Row(sb, "Maximum Field", result.MaximumField, result.FieldUnit);
            }

            return sb.ToString();
        }

        private static void Row(StringBuilder sb, string param, double value, string unit)
        {
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0}\t{1:G10}\t{2}", param, value, unit));
        }
    }
}
