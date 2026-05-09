using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LensHH.Core.Analysis;
using LensHH.Core.Glass;
using LensHH.Core.Models;

namespace LensHH.Rendering.TextExport
{
    public static class SystemDataTextExport
    {
        public static string Export(SystemDataResult result, OpticalSystem system,
            GlassCatalogManager glassMgr, string title = "")
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

            AppendIndexOfRefractionTable(sb, system, glassMgr);
            return sb.ToString();
        }

        private static void Row(StringBuilder sb, string param, double value, string unit)
        {
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0}\t{1:G10}\t{2}", param, value, unit));
        }

        private static void AppendIndexOfRefractionTable(StringBuilder sb,
            OpticalSystem system, GlassCatalogManager glassMgr)
        {
            // Surface.Material represents the medium AFTER that surface, so
            // BuildRefractiveIndexArray returns indices indexed by surface number.
            // Build a deduplicated material->representative-surface map preserving
            // first-encountered order. Skip air (empty material) and mirrors.
            var materialToSurface = new List<KeyValuePair<string, int>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < system.Surfaces.Count; i++)
            {
                string mat = system.Surfaces[i].Material;
                if (string.IsNullOrEmpty(mat)) continue;
                if (mat.Equals("AIR", StringComparison.OrdinalIgnoreCase)) continue;
                if (mat.Equals("MIRROR", StringComparison.OrdinalIgnoreCase)) continue;
                if (seen.Add(mat))
                    materialToSurface.Add(new KeyValuePair<string, int>(mat, i));
            }

            if (materialToSurface.Count == 0 || system.Wavelengths.Count == 0)
                return;

            sb.AppendLine();
            sb.AppendLine("Refractive Index Table");
            sb.AppendLine();

            sb.Append("Wavelength (µm)");
            foreach (var kv in materialToSurface)
                sb.Append('\t').Append(kv.Key);
            sb.AppendLine();

            foreach (var wl in system.Wavelengths)
            {
                double[] indices;
                try
                {
                    indices = glassMgr.BuildRefractiveIndexArray(system, wl.Value);
                }
                catch
                {
                    // Catalog miss / out-of-range — emit a row of blanks rather than
                    // failing the whole export. This is rare; happens if the user
                    // has a wavelength with no catalog data for one of the materials.
                    sb.Append(wl.Value.ToString("F4", CultureInfo.InvariantCulture));
                    foreach (var _ in materialToSurface) sb.Append("\t-");
                    sb.AppendLine();
                    continue;
                }

                sb.Append(wl.Value.ToString("F4", CultureInfo.InvariantCulture));
                foreach (var kv in materialToSurface)
                {
                    int surfIdx = kv.Value;
                    string cell = (surfIdx >= 0 && surfIdx < indices.Length)
                        ? indices[surfIdx].ToString("F5", CultureInfo.InvariantCulture)
                        : "-";
                    sb.Append('\t').Append(cell);
                }
                sb.AppendLine();
            }
        }
    }
}
