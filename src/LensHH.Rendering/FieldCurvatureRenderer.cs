using System;
using System.Globalization;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering
{
    public static class FieldCurvatureRenderer
    {
        private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        public static string Render(FieldCurvatureResult result, string title = "",
            RenderingOptions? options = null, string fieldUnit = "deg")
        {
            int w = 450, h = 400;
            int margin = 55;
            int pw = w - 2 * margin;
            int ph = h - 2 * margin;

            double maxShift = 0;
            foreach (var pt in result.Points)
                maxShift = Math.Max(maxShift, Math.Max(Math.Abs(pt.TangentialFocus), Math.Abs(pt.SagittalFocus)));
            if (maxShift < 0.001) maxShift = 0.1;
            maxShift *= 1.2;

            double maxField = result.Points.Count > 0 ? result.Points[result.Points.Count - 1].FieldY : 20;

            var sb = new StringBuilder();
            sb.AppendLine(F($"<svg width=\"{w}\" height=\"{h}\" xmlns=\"http://www.w3.org/2000/svg\">"));
            sb.AppendLine(F($"<rect width=\"{w}\" height=\"{h}\" fill=\"white\"/>"));

            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"18\" text-anchor=\"middle\" font-size=\"12\" font-weight=\"bold\">{Esc(title)}</text>"));

            // Axes: X = focus shift, Y = field angle (vertical, 0 at bottom)
            int centerX = margin + pw / 2;
            sb.AppendLine(F($"<line x1=\"{centerX}\" y1=\"{margin}\" x2=\"{centerX}\" y2=\"{h - margin}\" stroke=\"black\" stroke-width=\"1\"/>"));
            sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{h - margin}\" x2=\"{margin + pw}\" y2=\"{h - margin}\" stroke=\"black\" stroke-width=\"1\"/>"));

            // Y labels (field)
            for (int i = 0; i <= 4; i++)
            {
                double fd = maxField * i / 4;
                int y = (int)(h - margin - fd / maxField * ph);
                sb.AppendLine(F($"<text x=\"{margin - 5}\" y=\"{y + 4}\" text-anchor=\"end\" font-size=\"8\" fill=\"#666\">{FieldAxisHelper.FormatTick(fd, fieldUnit)}</text>"));
                sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{y}\" x2=\"{margin + pw}\" y2=\"{y}\" stroke=\"#eee\" stroke-width=\"0.5\"/>"));
            }

            string xAxisLabel = result.IsAfocal ? "Diopters" : "Focus Shift (mm)";
            sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"{h - 5}\" text-anchor=\"middle\" font-size=\"9\" fill=\"#333\">{xAxisLabel}</text>"));
            sb.AppendLine(F($"<text x=\"{margin - 5}\" y=\"{h - margin + 25}\" text-anchor=\"end\" font-size=\"7\" fill=\"#999\">-{LabelFormat.Auto(maxShift)}</text>"));
            sb.AppendLine(F($"<text x=\"{margin + pw + 5}\" y=\"{h - margin + 25}\" text-anchor=\"start\" font-size=\"7\" fill=\"#999\">{LabelFormat.Auto(maxShift)}</text>"));

            // Tangential (blue solid)
            sb.Append("<polyline fill=\"none\" stroke=\"#2060ff\" stroke-width=\"1.5\" points=\"");
            foreach (var pt in result.Points)
            {
                int px = centerX + (int)(pt.TangentialFocus / maxShift * (pw / 2.0));
                int py = (int)(h - margin - pt.FieldY / maxField * ph);
                sb.Append(F($"{px},{py} "));
            }
            sb.AppendLine("\"/>");

            // Sagittal (red dashed)
            sb.Append("<polyline fill=\"none\" stroke=\"#ff2020\" stroke-width=\"1.5\" stroke-dasharray=\"5,3\" points=\"");
            foreach (var pt in result.Points)
            {
                int px = centerX + (int)(pt.SagittalFocus / maxShift * (pw / 2.0));
                int py = (int)(h - margin - pt.FieldY / maxField * ph);
                sb.Append(F($"{px},{py} "));
            }
            sb.AppendLine("\"/>");

            // Legend
            int lx = margin + 5, ly = margin + 10;
            sb.AppendLine(F($"<line x1=\"{lx}\" y1=\"{ly}\" x2=\"{lx + 20}\" y2=\"{ly}\" stroke=\"#2060ff\" stroke-width=\"1.5\"/>"));
            sb.AppendLine(F($"<text x=\"{lx + 25}\" y=\"{ly + 4}\" font-size=\"8\">Tangential</text>"));
            sb.AppendLine(F($"<line x1=\"{lx}\" y1=\"{ly + 14}\" x2=\"{lx + 20}\" y2=\"{ly + 14}\" stroke=\"#ff2020\" stroke-width=\"1.5\" stroke-dasharray=\"5,3\"/>"));
            sb.AppendLine(F($"<text x=\"{lx + 25}\" y=\"{ly + 18}\" font-size=\"8\">Sagittal</text>"));

            sb.AppendLine(F($"<rect x=\"{margin}\" y=\"{margin}\" width=\"{pw}\" height=\"{ph}\" fill=\"none\" stroke=\"#ccc\" stroke-width=\"0.5\"/>"));
            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        public static string RenderPage(FieldCurvatureResult result, string title = "Field Curvature",
            RenderingOptions? options = null, string fieldUnit = "deg")
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
            sb.AppendLine("<title>" + Esc(title) + "</title>");
            sb.AppendLine("<style>body{font-family:sans-serif;max-width:600px;margin:0 auto;padding:20px} svg{border:1px solid #ddd}</style>");
            sb.AppendLine("</head><body><h1>" + Esc(title) + "</h1>");
            sb.AppendLine(Render(result, title, options, fieldUnit));
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        public static string RenderMultiWavelengthPage(MultiWavelengthFieldCurvatureResult mwResult,
            string title = "Field Curvature", string[]? wavelengthLabels = null,
            RenderingOptions? options = null, string fieldUnit = "deg")
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
            sb.AppendLine("<title>" + Esc(title) + "</title>");
            sb.AppendLine("<style>body{font-family:sans-serif;max-width:600px;margin:0 auto;padding:20px} svg{border:1px solid #ddd}</style>");
            sb.AppendLine("</head><body><h1>" + Esc(title) + "</h1>");
            sb.AppendLine(RenderMultiWavelength(mwResult, title, wavelengthLabels, options, fieldUnit));
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        /// <summary>
        /// Render field curvature for all wavelengths overlaid on one plot.
        /// Each wavelength gets T (solid) and S (dashed) curves in its color.
        /// </summary>
        public static string RenderMultiWavelength(MultiWavelengthFieldCurvatureResult mwResult,
            string title = "", string[]? wavelengthLabels = null,
            RenderingOptions? options = null, string fieldUnit = "deg")
        {
            var opt = options ?? new RenderingOptions();
            int w = 450, h = 400;
            int margin = 55;
            int pw = w - 2 * margin;
            int ph = h - 2 * margin;

            double maxShift = 0;
            double maxField = 0;
            foreach (var fc in mwResult.PerWavelength)
            {
                foreach (var pt in fc.Points)
                {
                    maxShift = Math.Max(maxShift, Math.Max(Math.Abs(pt.TangentialFocus), Math.Abs(pt.SagittalFocus)));
                    if (pt.FieldY > maxField) maxField = pt.FieldY;
                }
            }
            if (maxShift < 0.001) maxShift = 0.1;
            maxShift *= 1.2;
            if (maxField <= 0) maxField = 20;

            var sb = new StringBuilder();
            sb.AppendLine(F($"<svg width=\"{w}\" height=\"{h}\" xmlns=\"http://www.w3.org/2000/svg\">"));
            sb.AppendLine(F($"<rect width=\"{w}\" height=\"{h}\" fill=\"white\"/>"));

            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"18\" text-anchor=\"middle\" font-size=\"12\" font-weight=\"bold\">{Esc(title)}</text>"));

            int centerX = margin + pw / 2;
            sb.AppendLine(F($"<line x1=\"{centerX}\" y1=\"{margin}\" x2=\"{centerX}\" y2=\"{h - margin}\" stroke=\"black\" stroke-width=\"1\"/>"));
            sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{h - margin}\" x2=\"{margin + pw}\" y2=\"{h - margin}\" stroke=\"black\" stroke-width=\"1\"/>"));

            for (int i = 0; i <= 4; i++)
            {
                double fd = maxField * i / 4;
                int y = (int)(h - margin - fd / maxField * ph);
                sb.AppendLine(F($"<text x=\"{margin - 5}\" y=\"{y + 4}\" text-anchor=\"end\" font-size=\"8\" fill=\"#666\">{FieldAxisHelper.FormatTick(fd, fieldUnit)}</text>"));
                sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{y}\" x2=\"{margin + pw}\" y2=\"{y}\" stroke=\"#eee\" stroke-width=\"0.5\"/>"));
            }

            string xAxisLabelMw = mwResult.IsAfocal ? "Diopters" : "Focus Shift (mm)";
            sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"{h - 5}\" text-anchor=\"middle\" font-size=\"9\" fill=\"#333\">{xAxisLabelMw}</text>"));
            sb.AppendLine(F($"<text x=\"{margin - 5}\" y=\"{h - margin + 25}\" text-anchor=\"end\" font-size=\"7\" fill=\"#999\">-{LabelFormat.Auto(maxShift)}</text>"));
            sb.AppendLine(F($"<text x=\"{margin + pw + 5}\" y=\"{h - margin + 25}\" text-anchor=\"start\" font-size=\"7\" fill=\"#999\">{LabelFormat.Auto(maxShift)}</text>"));

            // Draw T/S curves for each wavelength
            for (int wIdx = 0; wIdx < mwResult.PerWavelength.Count; wIdx++)
            {
                var fc = mwResult.PerWavelength[wIdx];
                string color = opt.GetWavelengthColor(wIdx);

                // Tangential (solid)
                sb.Append(F($"<polyline fill=\"none\" stroke=\"{color}\" stroke-width=\"1.5\" points=\""));
                foreach (var pt in fc.Points)
                {
                    int px = centerX + (int)(pt.TangentialFocus / maxShift * (pw / 2.0));
                    int py = (int)(h - margin - pt.FieldY / maxField * ph);
                    sb.Append(F($"{px},{py} "));
                }
                sb.AppendLine("\"/>");

                // Sagittal (dashed)
                sb.Append(F($"<polyline fill=\"none\" stroke=\"{color}\" stroke-width=\"1.5\" stroke-dasharray=\"5,3\" points=\""));
                foreach (var pt in fc.Points)
                {
                    int px = centerX + (int)(pt.SagittalFocus / maxShift * (pw / 2.0));
                    int py = (int)(h - margin - pt.FieldY / maxField * ph);
                    sb.Append(F($"{px},{py} "));
                }
                sb.AppendLine("\"/>");
            }

            // Legend: T/S line style, then wavelength colors
            int lx = margin + 5, ly = margin + 10;
            sb.AppendLine(F($"<line x1=\"{lx}\" y1=\"{ly}\" x2=\"{lx + 20}\" y2=\"{ly}\" stroke=\"#333\" stroke-width=\"1.5\"/>"));
            sb.AppendLine(F($"<text x=\"{lx + 25}\" y=\"{ly + 4}\" font-size=\"8\">T (solid)</text>"));
            sb.AppendLine(F($"<line x1=\"{lx}\" y1=\"{ly + 13}\" x2=\"{lx + 20}\" y2=\"{ly + 13}\" stroke=\"#333\" stroke-width=\"1.5\" stroke-dasharray=\"5,3\"/>"));
            sb.AppendLine(F($"<text x=\"{lx + 25}\" y=\"{ly + 17}\" font-size=\"8\">S (dashed)</text>"));

            for (int wIdx = 0; wIdx < mwResult.PerWavelength.Count; wIdx++)
            {
                int lyi = ly + 26 + wIdx * 13;
                string color = opt.GetWavelengthColor(wIdx);
                string label = wavelengthLabels != null && wIdx < wavelengthLabels.Length
                    ? wavelengthLabels[wIdx] : $"W{wIdx + 1}";
                sb.AppendLine(F($"<line x1=\"{lx}\" y1=\"{lyi}\" x2=\"{lx + 20}\" y2=\"{lyi}\" stroke=\"{color}\" stroke-width=\"2\"/>"));
                sb.AppendLine(F($"<text x=\"{lx + 25}\" y=\"{lyi + 4}\" font-size=\"8\">{Esc(label)}</text>"));
            }

            sb.AppendLine(F($"<rect x=\"{margin}\" y=\"{margin}\" width=\"{pw}\" height=\"{ph}\" fill=\"none\" stroke=\"#ccc\" stroke-width=\"0.5\"/>"));
            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
