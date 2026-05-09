using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering.TextExport
{
    public static class LongitudinalAberrationTextExport
    {
        public static string Export(LongitudinalAberrationResult result, string title = "")
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(title);

            if (result.IsAfocal)
            {
                sb.AppendLine("Not applicable for afocal systems.");
                return sb.ToString();
            }

            int numWl = result.WavelengthsUm.Length;
            int wlDigits = LabelFormat.WavelengthDigits(result.WavelengthsUm);
            string wlFmt = "F" + wlDigits;

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Max Pupil Radius (mm)\t{0:F8}", result.PupilRadiusMax));
            sb.AppendLine();

            // Group by wavelength index, sort by ascending pupil radius.
            var perWl = new List<List<LongitudinalAberrationPoint>>(numWl);
            for (int i = 0; i < numWl; i++) perWl.Add(new List<LongitudinalAberrationPoint>());
            foreach (var p in result.Points)
                if (p.WavelengthIndex >= 0 && p.WavelengthIndex < numWl)
                    perWl[p.WavelengthIndex].Add(p);
            for (int i = 0; i < numWl; i++)
                perWl[i].Sort((a, b) => a.PupilRadius.CompareTo(b.PupilRadius));

            // Header: Pupil Radius then one column per wavelength.
            sb.Append("Pupil Radius (mm)");
            for (int i = 0; i < numWl; i++)
                sb.Append('\t').Append(string.Format(CultureInfo.InvariantCulture,
                    "Shift @ {0} um (mm)", result.WavelengthsUm[i].ToString(wlFmt, CultureInfo.InvariantCulture)));
            sb.AppendLine();

            // Build a unified pupil-radius axis from any wavelength's grid
            // (all wavelengths share the same zone count and pupil radius).
            int nRows = 0;
            for (int i = 0; i < numWl; i++) if (perWl[i].Count > nRows) nRows = perWl[i].Count;

            for (int row = 0; row < nRows; row++)
            {
                double radius = 0;
                for (int i = 0; i < numWl; i++)
                    if (row < perWl[i].Count) { radius = perWl[i][row].PupilRadius; break; }

                sb.Append(radius.ToString("F8", CultureInfo.InvariantCulture));
                for (int i = 0; i < numWl; i++)
                {
                    sb.Append('\t');
                    if (row < perWl[i].Count)
                        sb.Append(perWl[i][row].LongitudinalShift.ToString("E8", CultureInfo.InvariantCulture));
                    else
                        sb.Append("-");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
