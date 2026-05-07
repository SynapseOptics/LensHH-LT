using System;
using System.Globalization;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering
{
    public static class ChromaticFocalShiftRenderer
    {
        private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Render chromatic focal shift as SVG: focal shift (µm) vs wavelength (µm).
        /// </summary>
        public static string Render(ChromaticFocalShiftResult result, string title = "",
            RenderingOptions? options = null)
        {
            var opt = options ?? new RenderingOptions();
            int w = 600, h = 400;
            int margin = 60;
            int pw = w - 2 * margin;
            int ph = h - 2 * margin;

            if (result.Points.Count == 0) return "";

            double minWl = result.Points[0].Wavelength;
            double maxWl = result.Points[result.Points.Count - 1].Wavelength;
            // Engine emits diopters for afocal mode and mm for focal mode. For
            // focal we display µm (×1000); afocal we display diopters as-is.
            double valueScale = result.IsAfocal ? 1.0 : 1000.0;
            string yUnit = result.IsAfocal ? "diopters" : "\u00b5m";
            // Y-axis half-extent: largest |scaled shift| seen in the data.
            // (result.MaxShift is the peak-to-trough range, not the half-extent,
            // so we recompute from the points to keep the curve centered.)
            double maxShift = 0;
            foreach (var pt in result.Points)
            {
                double s = Math.Abs(pt.FocalShift * valueScale);
                if (s > maxShift) maxShift = s;
            }
            if (maxShift < 0.01) maxShift = 1.0;
            maxShift *= 1.2;

            var sb = new StringBuilder();
            sb.AppendLine(F($"<svg width=\"{w}\" height=\"{h}\" xmlns=\"http://www.w3.org/2000/svg\">"));
            sb.AppendLine(F($"<rect width=\"{w}\" height=\"{h}\" fill=\"white\"/>"));

            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"18\" text-anchor=\"middle\" font-size=\"12\" font-weight=\"bold\">{Esc(title)}</text>"));

            // Axes
            int zeroY = margin + ph / 2;
            sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{zeroY}\" x2=\"{w - margin}\" y2=\"{zeroY}\" stroke=\"black\" stroke-width=\"1\"/>"));
            sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{margin}\" x2=\"{margin}\" y2=\"{h - margin}\" stroke=\"black\" stroke-width=\"1\"/>"));

            // Y grid (shift in µm)
            int numYTicks = 4;
            for (int i = -numYTicks; i <= numYTicks; i++)
            {
                double val = i * maxShift / numYTicks;
                int y = zeroY - (int)(i * ph / 2.0 / numYTicks);
                if (i != 0)
                    sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{y}\" x2=\"{w - margin}\" y2=\"{y}\" stroke=\"#eee\" stroke-width=\"0.5\"/>"));
                sb.AppendLine(F($"<text x=\"{margin - 5}\" y=\"{y + 4}\" text-anchor=\"end\" font-size=\"8\" fill=\"#666\">{LabelFormat.Tick(val, maxShift / numYTicks)}</text>"));
            }

            // X grid (wavelength)
            double wlRange = maxWl - minWl;
            double wlStep = wlRange <= 0.1 ? 0.02 : 0.05;
            for (double wl = Math.Ceiling(minWl / wlStep) * wlStep; wl <= maxWl; wl += wlStep)
            {
                int x = margin + (int)((wl - minWl) / wlRange * pw);
                sb.AppendLine(F($"<line x1=\"{x}\" y1=\"{margin}\" x2=\"{x}\" y2=\"{h - margin}\" stroke=\"#eee\" stroke-width=\"0.5\"/>"));
                sb.AppendLine(F($"<text x=\"{x}\" y=\"{h - margin + 15}\" text-anchor=\"middle\" font-size=\"8\" fill=\"#666\">{LabelFormat.Tick(wl, wlStep)}</text>"));
            }

            sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"{h - 5}\" text-anchor=\"middle\" font-size=\"9\" fill=\"#333\">Wavelength (\u00b5m)</text>"));
            sb.AppendLine(F($"<text x=\"12\" y=\"{h / 2}\" text-anchor=\"middle\" font-size=\"9\" fill=\"#333\" transform=\"rotate(-90 12 {h / 2})\">Focal Shift ({yUnit})</text>"));

            // Curve
            sb.Append("<polyline fill=\"none\" stroke=\"#2060ff\" stroke-width=\"2\" points=\"");
            foreach (var pt in result.Points)
            {
                int px = margin + (int)((pt.Wavelength - minWl) / wlRange * pw);
                double shiftScaled = pt.FocalShift * valueScale;
                int py = zeroY - (int)(shiftScaled / maxShift * (ph / 2.0));
                py = Math.Max(margin, Math.Min(h - margin, py));
                sb.Append(F($"{px},{py} "));
            }
            sb.AppendLine("\"/>");

            // Mark system wavelengths with dots (if available from the points)
            // Zero reference line already drawn as the main X axis

            sb.AppendLine(F($"<rect x=\"{margin}\" y=\"{margin}\" width=\"{pw}\" height=\"{ph}\" fill=\"none\" stroke=\"#ccc\" stroke-width=\"0.5\"/>"));
            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        /// <summary>
        /// Render as full HTML page.
        /// </summary>
        public static string RenderPage(ChromaticFocalShiftResult result, string title = "Chromatic Focal Shift",
            RenderingOptions? options = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head>");
            sb.AppendLine("<meta charset=\"utf-8\">");
            sb.AppendLine("<title>" + Esc(title) + "</title>");
            sb.AppendLine("<style>body{font-family:sans-serif;max-width:800px;margin:0 auto;padding:20px} svg{border:1px solid #ddd}</style>");
            sb.AppendLine("</head><body>");
            sb.AppendLine("<h1>" + Esc(title) + "</h1>");
            string maxLabel = result.IsAfocal
                ? F($"{LabelFormat.Auto(result.MaxShift)} diopters")
                : F($"{LabelFormat.Auto(result.MaxShift * 1000)} \u00b5m");
            sb.AppendLine(F($"<p>Max focal shift: {maxLabel}</p>"));
            sb.AppendLine(Render(result, title, options));
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private static string Esc(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;");
        }
    }
}
