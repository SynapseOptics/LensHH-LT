using System;
using System.Globalization;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering
{
    public static class FieldCurvatureDistortionRenderer
    {
        private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Render field curvature (left) and distortion (right) side by side.
        /// </summary>
        public static string Render(FieldCurvatureDistortionResult result, string title = "",
            RenderingOptions? options = null, string fieldUnit = "deg")
        {
            int w = 700, h = 400;
            int margin = 55;
            int gap = 30;
            int plotW = (w - 2 * margin - gap) / 2;
            int ph = h - 2 * margin;

            var sb = new StringBuilder();
            sb.AppendLine(F($"<svg width=\"{w}\" height=\"{h}\" xmlns=\"http://www.w3.org/2000/svg\">"));
            sb.AppendLine(F($"<rect width=\"{w}\" height=\"{h}\" fill=\"white\"/>"));

            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"18\" text-anchor=\"middle\" font-size=\"12\" font-weight=\"bold\">{Esc(title)}</text>"));

            // --- Field Curvature (left plot) ---
            int fcLeft = margin;
            double maxShift = 0;
            foreach (var pt in result.FieldCurvaturePoints)
            {
                maxShift = Math.Max(maxShift, Math.Max(Math.Abs(pt.TangentialFocus), Math.Abs(pt.SagittalFocus)));
            }
            if (maxShift < 0.001) maxShift = 0.1;
            maxShift *= 1.2;

            double maxField = 0;
            if (result.FieldCurvaturePoints.Count > 0)
                maxField = result.FieldCurvaturePoints[result.FieldCurvaturePoints.Count - 1].FieldY;
            if (maxField <= 0) maxField = 20;

            // FC axes (horizontal = shift, vertical = field)
            int fcCenterX = fcLeft + plotW / 2;
            sb.AppendLine(F($"<line x1=\"{fcCenterX}\" y1=\"{margin}\" x2=\"{fcCenterX}\" y2=\"{h - margin}\" stroke=\"black\" stroke-width=\"1\"/>"));
            sb.AppendLine(F($"<line x1=\"{fcLeft}\" y1=\"{h - margin}\" x2=\"{fcLeft + plotW}\" y2=\"{h - margin}\" stroke=\"black\" stroke-width=\"1\"/>"));

            sb.AppendLine(F($"<text x=\"{fcCenterX}\" y=\"{margin - 5}\" text-anchor=\"middle\" font-size=\"10\" font-weight=\"bold\">Field Curvature</text>"));
            string fcXLabel = result.IsAfocal ? "Diopters" : "Focus Shift (mm)";
            sb.AppendLine(F($"<text x=\"{fcCenterX}\" y=\"{h - margin + 15}\" text-anchor=\"middle\" font-size=\"8\" fill=\"#666\">{fcXLabel}</text>"));

            // Y axis labels (field)
            for (int i = 0; i <= 4; i++)
            {
                double fd = maxField * i / 4;
                int y = (int)(h - margin - fd / maxField * ph);
                sb.AppendLine(F($"<text x=\"{fcLeft - 5}\" y=\"{y + 4}\" text-anchor=\"end\" font-size=\"8\" fill=\"#666\">{FieldAxisHelper.FormatTick(fd, fieldUnit)}</text>"));
                sb.AppendLine(F($"<line x1=\"{fcLeft}\" y1=\"{y}\" x2=\"{fcLeft + plotW}\" y2=\"{y}\" stroke=\"#eee\" stroke-width=\"0.5\"/>"));
            }

            // X shift labels
            sb.AppendLine(F($"<text x=\"{fcLeft}\" y=\"{h - margin + 25}\" text-anchor=\"middle\" font-size=\"7\" fill=\"#999\">-{LabelFormat.Auto(maxShift)}</text>"));
            sb.AppendLine(F($"<text x=\"{fcLeft + plotW}\" y=\"{h - margin + 25}\" text-anchor=\"middle\" font-size=\"7\" fill=\"#999\">{LabelFormat.Auto(maxShift)}</text>"));

            // Tangential (blue solid)
            sb.Append("<polyline fill=\"none\" stroke=\"#2060ff\" stroke-width=\"1.5\" points=\"");
            foreach (var pt in result.FieldCurvaturePoints)
            {
                int px = fcCenterX + (int)(pt.TangentialFocus / maxShift * (plotW / 2.0));
                int py = (int)(h - margin - pt.FieldY / maxField * ph);
                sb.Append(F($"{px},{py} "));
            }
            sb.AppendLine("\"/>");

            // Sagittal (red dashed)
            sb.Append("<polyline fill=\"none\" stroke=\"#ff2020\" stroke-width=\"1.5\" stroke-dasharray=\"5,3\" points=\"");
            foreach (var pt in result.FieldCurvaturePoints)
            {
                int px = fcCenterX + (int)(pt.SagittalFocus / maxShift * (plotW / 2.0));
                int py = (int)(h - margin - pt.FieldY / maxField * ph);
                sb.Append(F($"{px},{py} "));
            }
            sb.AppendLine("\"/>");

            sb.AppendLine(F($"<rect x=\"{fcLeft}\" y=\"{margin}\" width=\"{plotW}\" height=\"{ph}\" fill=\"none\" stroke=\"#ccc\" stroke-width=\"0.5\"/>"));

            // --- Distortion (right plot) ---
            int distLeft = fcLeft + plotW + gap;
            double maxDist = 0;
            foreach (var pt in result.DistortionPoints)
                maxDist = Math.Max(maxDist, Math.Abs(pt.Distortion));
            if (maxDist < 0.001) maxDist = 1.0;
            maxDist *= 1.2;

            int distCenterX = distLeft + plotW / 2;
            sb.AppendLine(F($"<line x1=\"{distCenterX}\" y1=\"{margin}\" x2=\"{distCenterX}\" y2=\"{h - margin}\" stroke=\"black\" stroke-width=\"1\"/>"));
            sb.AppendLine(F($"<line x1=\"{distLeft}\" y1=\"{h - margin}\" x2=\"{distLeft + plotW}\" y2=\"{h - margin}\" stroke=\"black\" stroke-width=\"1\"/>"));

            sb.AppendLine(F($"<text x=\"{distCenterX}\" y=\"{margin - 5}\" text-anchor=\"middle\" font-size=\"10\" font-weight=\"bold\">Distortion</text>"));
            sb.AppendLine(F($"<text x=\"{distCenterX}\" y=\"{h - margin + 15}\" text-anchor=\"middle\" font-size=\"8\" fill=\"#666\">Distortion (%)</text>"));

            sb.AppendLine(F($"<text x=\"{distLeft}\" y=\"{h - margin + 25}\" text-anchor=\"middle\" font-size=\"7\" fill=\"#999\">-{LabelFormat.Auto(maxDist)}%</text>"));
            sb.AppendLine(F($"<text x=\"{distLeft + plotW}\" y=\"{h - margin + 25}\" text-anchor=\"middle\" font-size=\"7\" fill=\"#999\">{LabelFormat.Auto(maxDist)}%</text>"));

            // Distortion curve (green)
            sb.Append("<polyline fill=\"none\" stroke=\"#20aa20\" stroke-width=\"1.5\" points=\"");
            foreach (var pt in result.DistortionPoints)
            {
                int px = distCenterX + (int)(pt.Distortion / maxDist * (plotW / 2.0));
                int py = (int)(h - margin - pt.FieldY / maxField * ph);
                sb.Append(F($"{px},{py} "));
            }
            sb.AppendLine("\"/>");

            sb.AppendLine(F($"<rect x=\"{distLeft}\" y=\"{margin}\" width=\"{plotW}\" height=\"{ph}\" fill=\"none\" stroke=\"#ccc\" stroke-width=\"0.5\"/>"));

            // Legend
            int lx = fcLeft + 5, ly = margin + 10;
            sb.AppendLine(F($"<line x1=\"{lx}\" y1=\"{ly}\" x2=\"{lx + 20}\" y2=\"{ly}\" stroke=\"#2060ff\" stroke-width=\"1.5\"/>"));
            sb.AppendLine(F($"<text x=\"{lx + 25}\" y=\"{ly + 4}\" font-size=\"8\">Tangential</text>"));
            sb.AppendLine(F($"<line x1=\"{lx}\" y1=\"{ly + 14}\" x2=\"{lx + 20}\" y2=\"{ly + 14}\" stroke=\"#ff2020\" stroke-width=\"1.5\" stroke-dasharray=\"5,3\"/>"));
            sb.AppendLine(F($"<text x=\"{lx + 25}\" y=\"{ly + 18}\" font-size=\"8\">Sagittal</text>"));

            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        public static string RenderPage(FieldCurvatureDistortionResult result, string title = "Field Curvature & Distortion",
            RenderingOptions? options = null, string fieldUnit = "deg")
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
            sb.AppendLine("<title>" + Esc(title) + "</title>");
            sb.AppendLine("<style>body{font-family:sans-serif;max-width:800px;margin:0 auto;padding:20px} svg{border:1px solid #ddd}</style>");
            sb.AppendLine("</head><body>");
            sb.AppendLine("<h1>" + Esc(title) + "</h1>");
            sb.AppendLine(Render(result, title, options, fieldUnit));
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private static string Esc(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
