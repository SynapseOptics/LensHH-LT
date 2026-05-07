using System.Globalization;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering.TextExport
{
    public static class MtfVsFieldTextExport
    {
        public static string Export(MtfVsFieldMultiFreqResult result, string title = "",
            string fieldUnit = "deg", bool isAfocal = false)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(title);

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                $"Max Field ({fieldUnit})\t{{0:F2}}", result.MaxFieldY));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Number of Frequencies\t{0}", result.Frequencies.Length));
            sb.AppendLine();

            // Header
            sb.Append($"Field ({fieldUnit})");
            foreach (double freq in result.Frequencies)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture,
                    "\t{0:F1}{1} T\t{0:F1}{1} S", freq, isAfocal ? "cy/arcmin" : "cy/mm"));
            }
            sb.AppendLine();

            // Data rows
            foreach (var pt in result.Points)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F4}", pt.fieldY));
                for (int fk = 0; fk < result.Frequencies.Length; fk++)
                {
                    sb.Append(string.Format(CultureInfo.InvariantCulture,
                        "\t{0:F6}\t{1:F6}", pt.Item2[fk].tang, pt.Item2[fk].sag));
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
