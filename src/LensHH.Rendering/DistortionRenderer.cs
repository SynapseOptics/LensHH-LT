using System;
using System.Globalization;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering
{
    public static class DistortionRenderer
    {
        private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        public static string Render(DistortionResult result, string title = "",
            RenderingOptions? options = null, string fieldUnit = "deg")
        {
            int w = 600, h = 400;
            int margin = 55;
            int pw = w - 2 * margin;
            int ph = h - 2 * margin;

            double maxDist = result.MaxDistortion;
            if (maxDist < 0.001) maxDist = 1.0;
            maxDist *= 1.2;

            double maxField = result.Points.Count > 0 ? result.Points[result.Points.Count - 1].FieldY : 20;

            var sb = new StringBuilder();
            sb.AppendLine(F($"<svg width=\"{w}\" height=\"{h}\" xmlns=\"http://www.w3.org/2000/svg\">"));
            sb.AppendLine(F($"<rect width=\"{w}\" height=\"{h}\" fill=\"white\"/>"));

            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"18\" text-anchor=\"middle\" font-size=\"12\" font-weight=\"bold\">{Esc(title)}</text>"));

            string typeLabel = result.Type == DistortionType.FTheta ? "F-\u03b8" : "F-tan(\u03b8)";
            sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"32\" text-anchor=\"middle\" font-size=\"10\" fill=\"#666\">Reference: {typeLabel}, Max: {LabelFormat.Auto(result.MaxDistortion)}%</text>"));

            // Axes: X = field (degrees), Y = distortion (%)
            int zeroY = margin + ph / 2;
            sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{margin}\" x2=\"{margin}\" y2=\"{h - margin}\" stroke=\"black\" stroke-width=\"1\"/>"));
            sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{zeroY}\" x2=\"{w - margin}\" y2=\"{zeroY}\" stroke=\"black\" stroke-width=\"1\"/>"));

            // Y grid (distortion %, symmetric around 0)
            int numYTicks = 4;
            for (int i = -numYTicks; i <= numYTicks; i++)
            {
                double val = i * maxDist / numYTicks;
                int y = zeroY - (int)(i * ph / 2.0 / numYTicks);
                if (i != 0)
                    sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{y}\" x2=\"{w - margin}\" y2=\"{y}\" stroke=\"#eee\" stroke-width=\"0.5\"/>"));
                sb.AppendLine(F($"<text x=\"{margin - 5}\" y=\"{y + 4}\" text-anchor=\"end\" font-size=\"8\" fill=\"#666\">{LabelFormat.Tick(val, maxDist / numYTicks)}%</text>"));
            }

            // X grid (field)
            double fieldStep = FieldAxisHelper.NiceStep(maxField);
            for (double fd = 0; fd <= maxField * 1.001; fd += fieldStep)
            {
                int x = margin + (int)(fd / maxField * pw);
                if (fd > 0)
                    sb.AppendLine(F($"<line x1=\"{x}\" y1=\"{margin}\" x2=\"{x}\" y2=\"{h - margin}\" stroke=\"#eee\" stroke-width=\"0.5\"/>"));
                sb.AppendLine(F($"<text x=\"{x}\" y=\"{h - margin + 15}\" text-anchor=\"middle\" font-size=\"8\" fill=\"#666\">{FieldAxisHelper.FormatTick(fd, fieldUnit)}</text>"));
            }

            sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"{h - 5}\" text-anchor=\"middle\" font-size=\"9\" fill=\"#333\">{FieldAxisHelper.AxisLabel(fieldUnit)}</text>"));
            sb.AppendLine(F($"<text x=\"12\" y=\"{h / 2}\" text-anchor=\"middle\" font-size=\"9\" fill=\"#333\" transform=\"rotate(-90 12 {h / 2})\">Distortion (%)</text>"));

            // Distortion curve (green)
            sb.Append("<polyline fill=\"none\" stroke=\"#20aa20\" stroke-width=\"2\" points=\"");
            foreach (var pt in result.Points)
            {
                int px = margin + (int)(pt.FieldY / maxField * pw);
                int py = zeroY - (int)(pt.Distortion / maxDist * (ph / 2.0));
                py = Math.Max(margin, Math.Min(h - margin, py));
                sb.Append(F($"{px},{py} "));
            }
            sb.AppendLine("\"/>");

            sb.AppendLine(F($"<rect x=\"{margin}\" y=\"{margin}\" width=\"{pw}\" height=\"{ph}\" fill=\"none\" stroke=\"#ccc\" stroke-width=\"0.5\"/>"));
            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        public static string RenderPage(DistortionResult result, string title = "Distortion",
            RenderingOptions? options = null, string fieldUnit = "deg")
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
            sb.AppendLine("<title>" + Esc(title) + "</title>");
            sb.AppendLine("<style>body{font-family:sans-serif;max-width:800px;margin:0 auto;padding:20px} svg{border:1px solid #ddd}</style>");
            sb.AppendLine("</head><body><h1>" + Esc(title) + "</h1>");
            sb.AppendLine(Render(result, title, options, fieldUnit));
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
