using System;
using System.Globalization;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering
{
    public static class RelativeIlluminationRenderer
    {
        private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        public static string Render(RelativeIlluminationResult result, string title = "",
            double maxField = 0, RenderingOptions? options = null,
            string fieldUnit = "deg")
        {
            int w = 600, h = 400;
            int margin = 55;
            int pw = w - 2 * margin;
            int ph = h - 2 * margin;

            if (maxField <= 0 && result.Points.Count > 0)
                maxField = result.Points[result.Points.Count - 1].FieldY;
            if (maxField <= 0) maxField = 20;

            var sb = new StringBuilder();
            sb.AppendLine(F($"<svg width=\"{w}\" height=\"{h}\" xmlns=\"http://www.w3.org/2000/svg\">"));
            sb.AppendLine(F($"<rect width=\"{w}\" height=\"{h}\" fill=\"white\"/>"));

            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"18\" text-anchor=\"middle\" font-size=\"12\" font-weight=\"bold\">{Esc(title)}</text>"));

            // Axes
            sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{margin}\" x2=\"{margin}\" y2=\"{h - margin}\" stroke=\"black\" stroke-width=\"1\"/>"));
            sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{h - margin}\" x2=\"{w - margin}\" y2=\"{h - margin}\" stroke=\"black\" stroke-width=\"1\"/>"));

            // Y grid (0 to 1)
            for (int i = 0; i <= 5; i++)
            {
                double val = i * 0.2;
                int y = (int)(h - margin - val * ph);
                sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{y}\" x2=\"{w - margin}\" y2=\"{y}\" stroke=\"#eee\" stroke-width=\"0.5\"/>"));
                sb.AppendLine(F($"<text x=\"{margin - 5}\" y=\"{y + 4}\" text-anchor=\"end\" font-size=\"8\" fill=\"#666\">{LabelFormat.Tick(val, 0.2)}</text>"));
            }

            // X grid
            double fieldStep = FieldAxisHelper.NiceStep(maxField);
            for (double fd = 0; fd <= maxField * 1.001; fd += fieldStep)
            {
                int x = margin + (int)(fd / maxField * pw);
                if (fd > 0)
                    sb.AppendLine(F($"<line x1=\"{x}\" y1=\"{margin}\" x2=\"{x}\" y2=\"{h - margin}\" stroke=\"#eee\" stroke-width=\"0.5\"/>"));
                sb.AppendLine(F($"<text x=\"{x}\" y=\"{h - margin + 15}\" text-anchor=\"middle\" font-size=\"8\" fill=\"#666\">{FieldAxisHelper.FormatTick(fd, fieldUnit)}</text>"));
            }

            sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"{h - 5}\" text-anchor=\"middle\" font-size=\"9\" fill=\"#333\">{FieldAxisHelper.AxisLabel(fieldUnit)}</text>"));
            sb.AppendLine(F($"<text x=\"12\" y=\"{h / 2}\" text-anchor=\"middle\" font-size=\"9\" fill=\"#333\" transform=\"rotate(-90 12 {h / 2})\">Relative Illumination</text>"));

            // Curve
            sb.Append("<polyline fill=\"none\" stroke=\"#2060ff\" stroke-width=\"2\" points=\"");
            foreach (var pt in result.Points)
            {
                int px = margin + (int)(pt.FieldY / maxField * pw);
                int py = (int)(h - margin - Math.Max(0, Math.Min(1, pt.RelativeIllumination)) * ph);
                sb.Append(F($"{px},{py} "));
            }
            sb.AppendLine("\"/>");

            sb.AppendLine(F($"<rect x=\"{margin}\" y=\"{margin}\" width=\"{pw}\" height=\"{ph}\" fill=\"none\" stroke=\"#ccc\" stroke-width=\"0.5\"/>"));
            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        public static string RenderPage(RelativeIlluminationResult result, string title = "Relative Illumination",
            double maxField = 0, RenderingOptions? options = null,
            string fieldUnit = "deg")
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
            sb.AppendLine("<title>" + Esc(title) + "</title>");
            sb.AppendLine("<style>body{font-family:sans-serif;max-width:800px;margin:0 auto;padding:20px} svg{border:1px solid #ddd}</style>");
            sb.AppendLine("</head><body>");
            sb.AppendLine("<h1>" + Esc(title) + "</h1>");
            sb.AppendLine(Render(result, title, maxField, options, fieldUnit));
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private static string Esc(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
