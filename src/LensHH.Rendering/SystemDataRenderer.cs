using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering
{
    public static class SystemDataRenderer
    {
        private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Render system data as an SVG table.
        /// </summary>
        public static string Render(SystemDataResult result, string title = "System Data",
            RenderingOptions? options = null)
        {
            var rows = BuildRows(result);

            int paramColW = 220;
            int valueColW = 120;
            int unitColW = 40;
            int tableW = paramColW + valueColW + unitColW + 20;
            int rowH = 22;
            int headerH = 30;
            int sectionH = 26;
            int titleH = 50;

            // Calculate total height
            int dataRows = 0;
            int sectionRows = 0;
            foreach (var row in rows)
            {
                if (row.IsSection) sectionRows++;
                else dataRows++;
            }
            int totalH = titleH + headerH + dataRows * rowH + sectionRows * sectionH + 10;

            var sb = new StringBuilder();
            sb.AppendLine(F($"<svg width=\"{tableW}\" height=\"{totalH}\" xmlns=\"http://www.w3.org/2000/svg\">"));
            sb.AppendLine(F($"<rect width=\"{tableW}\" height=\"{totalH}\" fill=\"white\"/>"));

            // Title
            sb.AppendLine(F($"<text x=\"{tableW / 2}\" y=\"20\" text-anchor=\"middle\" font-size=\"14\" font-weight=\"bold\" font-family=\"sans-serif\">{Esc(title)}</text>"));

            // Subtitle (focal/afocal)
            string mode = result.IsAfocal ? "Afocal System" : "Focal System";
            sb.AppendLine(F($"<text x=\"{tableW / 2}\" y=\"38\" text-anchor=\"middle\" font-size=\"11\" fill=\"#666\" font-family=\"sans-serif\">{Esc(mode)}</text>"));

            int y = titleH;

            // Header row
            sb.AppendLine(F($"<rect x=\"0\" y=\"{y}\" width=\"{tableW}\" height=\"{headerH}\" fill=\"#f0f0f0\"/>"));
            sb.AppendLine(F($"<line x1=\"0\" y1=\"{y}\" x2=\"{tableW}\" y2=\"{y}\" stroke=\"#999\" stroke-width=\"1\"/>"));
            sb.AppendLine(F($"<text x=\"10\" y=\"{y + 20}\" font-size=\"11\" font-weight=\"bold\" font-family=\"sans-serif\">Parameter</text>"));
            sb.AppendLine(F($"<text x=\"{paramColW + valueColW}\" y=\"{y + 20}\" text-anchor=\"end\" font-size=\"11\" font-weight=\"bold\" font-family=\"sans-serif\">Value</text>"));
            sb.AppendLine(F($"<text x=\"{paramColW + valueColW + 8}\" y=\"{y + 20}\" font-size=\"11\" font-weight=\"bold\" font-family=\"sans-serif\">Unit</text>"));
            sb.AppendLine(F($"<line x1=\"0\" y1=\"{y + headerH}\" x2=\"{tableW}\" y2=\"{y + headerH}\" stroke=\"#ccc\" stroke-width=\"1\"/>"));
            y += headerH;

            // Data rows
            foreach (var row in rows)
            {
                if (row.IsSection)
                {
                    sb.AppendLine(F($"<rect x=\"0\" y=\"{y}\" width=\"{tableW}\" height=\"{sectionH}\" fill=\"#e8eef8\"/>"));
                    sb.AppendLine(F($"<line x1=\"0\" y1=\"{y}\" x2=\"{tableW}\" y2=\"{y}\" stroke=\"#aab\" stroke-width=\"1.5\"/>"));
                    sb.AppendLine(F($"<text x=\"10\" y=\"{y + 18}\" font-size=\"11\" font-weight=\"bold\" fill=\"#446\" font-family=\"sans-serif\">{Esc(row.Parameter)}</text>"));
                    y += sectionH;
                }
                else
                {
                    // Alternating row background
                    sb.AppendLine(F($"<text x=\"14\" y=\"{y + 16}\" font-size=\"11\" font-family=\"sans-serif\" fill=\"#222\">{Esc(row.Parameter)}</text>"));
                    sb.AppendLine(F($"<text x=\"{paramColW + valueColW}\" y=\"{y + 16}\" text-anchor=\"end\" font-size=\"11\" font-family=\"monospace\" fill=\"#111\">{Esc(row.Value)}</text>"));
                    sb.AppendLine(F($"<text x=\"{paramColW + valueColW + 8}\" y=\"{y + 16}\" font-size=\"10\" font-family=\"sans-serif\" fill=\"#666\">{Esc(row.Unit)}</text>"));
                    sb.AppendLine(F($"<line x1=\"0\" y1=\"{y + rowH}\" x2=\"{tableW}\" y2=\"{y + rowH}\" stroke=\"#eee\" stroke-width=\"0.5\"/>"));
                    y += rowH;
                }
            }

            // Bottom border
            sb.AppendLine(F($"<line x1=\"0\" y1=\"{y}\" x2=\"{tableW}\" y2=\"{y}\" stroke=\"#999\" stroke-width=\"1\"/>"));

            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        /// <summary>
        /// Render system data as an HTML page with embedded SVG table.
        /// </summary>
        public static string RenderPage(SystemDataResult result, string title = "System Data",
            RenderingOptions? options = null)
        {
            string svg = Render(result, title, options);

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head>");
            sb.AppendLine("<meta charset=\"utf-8\">");
            sb.AppendLine("<title>" + Esc(title) + "</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body{font-family:sans-serif;max-width:600px;margin:0 auto;padding:20px;background:#fff}");
            sb.AppendLine("svg{display:block;margin:0 auto}");
            sb.AppendLine("</style>");
            sb.AppendLine("</head><body>");
            sb.AppendLine(svg);
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private static List<DataRow> BuildRows(SystemDataResult r)
        {
            var rows = new List<DataRow>();

            if (r.IsAfocal)
            {
                rows.Add(DataRow.Section("Afocal Properties"));
                rows.Add(new DataRow("Angular Magnification", Fmt(r.AngularMagnification), "\u00d7"));

                rows.Add(DataRow.Section("Pupils"));
                rows.Add(new DataRow("Entrance Pupil Diameter", Fmt(r.EntrancePupilDiameter), "mm"));
                rows.Add(new DataRow("Entrance Pupil Position", Fmt(r.EntrancePupilPosition), "mm"));
                rows.Add(new DataRow("Exit Pupil Diameter", Fmt(r.ExitPupilDiameter), "mm"));
                rows.Add(new DataRow("Exit Pupil Position", Fmt(r.ExitPupilPosition), "mm"));

                rows.Add(DataRow.Section("Geometry"));
                rows.Add(new DataRow("Total Track", Fmt(r.TotalTrack), "mm"));
                rows.Add(new DataRow("Maximum Field", Fmt(r.MaximumField), r.FieldUnit));
            }
            else
            {
                rows.Add(DataRow.Section("Focal Lengths"));
                rows.Add(new DataRow("Effective Focal Length", Fmt(r.Efl), "mm"));
                rows.Add(new DataRow("Back Focal Length", Fmt(r.Bfl), "mm"));
                rows.Add(new DataRow("Front Focal Length", Fmt(r.Ffl), "mm"));

                rows.Add(DataRow.Section("F/# and NA"));
                rows.Add(new DataRow("Image Space F/#", Fmt(r.ImageSpaceFNumber), ""));
                rows.Add(new DataRow("Working F/#", Fmt(r.WorkingFNumber), ""));
                rows.Add(new DataRow("Image Space NA", Fmt(r.ImageSpaceNA), ""));

                rows.Add(DataRow.Section("Pupils"));
                rows.Add(new DataRow("Entrance Pupil Diameter", Fmt(r.EntrancePupilDiameter), "mm"));
                rows.Add(new DataRow("Entrance Pupil Position", Fmt(r.EntrancePupilPosition), "mm"));
                rows.Add(new DataRow("Exit Pupil Diameter", Fmt(r.ExitPupilDiameter), "mm"));
                rows.Add(new DataRow("Exit Pupil Position", Fmt(r.ExitPupilPosition), "mm"));

                rows.Add(DataRow.Section("Image"));
                rows.Add(new DataRow("Paraxial Image Height", Fmt(r.ParaxialImageHeight), "mm"));
                rows.Add(new DataRow("Paraxial Magnification", Fmt(r.ParaxialMagnification), ""));

                rows.Add(DataRow.Section("Geometry"));
                rows.Add(new DataRow("Total Track", Fmt(r.TotalTrack), "mm"));
                rows.Add(new DataRow("Maximum Field", Fmt(r.MaximumField), r.FieldUnit));
            }

            return rows;
        }

        private static string Fmt(double v)
        {
            if (Math.Abs(v) < 1e-15) return "0.0000";
            return LabelFormat.Auto(v);
        }

        private static string Esc(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;");
        }

        private class DataRow
        {
            public string Parameter { get; }
            public string Value { get; }
            public string Unit { get; }
            public bool IsSection { get; }

            public DataRow(string parameter, string value, string unit)
            {
                Parameter = parameter;
                Value = value;
                Unit = unit;
                IsSection = false;
            }

            private DataRow(string sectionName)
            {
                Parameter = sectionName;
                Value = "";
                Unit = "";
                IsSection = true;
            }

            public static DataRow Section(string name) => new DataRow(name);
        }
    }
}
