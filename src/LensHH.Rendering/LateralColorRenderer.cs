using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering
{
    public static class LateralColorRenderer
    {
        private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Render lateral color vs field as SVG.
        /// One curve per wavelength, X = field (degrees), Y = lateral shift
        /// — µm for focal systems, arcmin for afocal (engine emits arcmin).
        /// </summary>
        public static string Render(LateralColorResult result, string title,
            double maxField, int numWavelengths,
            string[]? wavelengthLabels = null, RenderingOptions? options = null,
            string fieldUnit = "deg", string? referenceWavelengthLabel = null)
        {
            var opt = options ?? new RenderingOptions();
            int w = 600, h = 400;
            int margin = 55;
            int pw = w - 2 * margin;
            int ph = h - 2 * margin;

            // Engine returns LateralShift / MaxLateralColor in mm (focal) or
            // arcmin (afocal). Show µm for focal (×1000) and arcmin (×1) otherwise.
            double dispScale = result.IsAfocal ? 1.0 : 1000.0;
            string unitLabel = result.IsAfocal ? "arcmin" : "\u00b5m";

            double maxShift = result.MaxLateralColor * dispScale;
            if (maxShift < 0.01) maxShift = 1.0;
            maxShift *= 1.15; // padding

            var sb = new StringBuilder();
            sb.AppendLine(F($"<svg width=\"{w}\" height=\"{h}\" xmlns=\"http://www.w3.org/2000/svg\">"));
            sb.AppendLine(F($"<rect width=\"{w}\" height=\"{h}\" fill=\"white\"/>"));
            sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"18\" text-anchor=\"middle\" font-size=\"12\" font-weight=\"bold\">{Esc(title)}</text>"));

            // Axes
            sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{margin}\" x2=\"{margin}\" y2=\"{h - margin}\" stroke=\"black\" stroke-width=\"1\"/>"));
            sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{h / 2}\" x2=\"{w - margin}\" y2=\"{h / 2}\" stroke=\"black\" stroke-width=\"1\"/>"));

            // Y grid (lateral shift in µm, symmetric around 0)
            int numYTicks = 4;
            for (int i = -numYTicks; i <= numYTicks; i++)
            {
                double val = i * maxShift / numYTicks;
                int y = h / 2 - (int)(i * ph / 2.0 / numYTicks);
                if (i != 0)
                    sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{y}\" x2=\"{w - margin}\" y2=\"{y}\" stroke=\"#eee\" stroke-width=\"0.5\"/>"));
                sb.AppendLine(F($"<text x=\"{margin - 5}\" y=\"{y + 4}\" text-anchor=\"end\" font-size=\"8\" fill=\"#666\">{LabelFormat.Tick(val, maxShift / numYTicks)}</text>"));
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
            sb.AppendLine(F($"<text x=\"12\" y=\"{h / 2}\" text-anchor=\"middle\" font-size=\"9\" fill=\"#333\" transform=\"rotate(-90 12 {h / 2})\">Lateral Color ({unitLabel})</text>"));

            // Group by wavelength
            var byWl = new Dictionary<int, List<LateralColorPoint>>();
            foreach (var pt in result.Points)
            {
                if (!byWl.ContainsKey(pt.WavelengthIndex))
                    byWl[pt.WavelengthIndex] = new List<LateralColorPoint>();
                byWl[pt.WavelengthIndex].Add(pt);
            }

            foreach (var kvp in byWl.OrderBy(k => k.Key))
            {
                int wIdx = kvp.Key;
                var pts = kvp.Value.OrderBy(p => p.FieldY).ToList();
                string color = opt.GetWavelengthColor(wIdx);

                sb.Append(F($"<polyline fill=\"none\" stroke=\"{color}\" stroke-width=\"1.5\" points=\""));
                foreach (var pt in pts)
                {
                    int px = margin + (int)(pt.FieldY / maxField * pw);
                    double shiftDisp = pt.LateralShift * dispScale;
                    int py = h / 2 - (int)(shiftDisp / maxShift * (ph / 2.0));
                    py = Math.Max(margin, Math.Min(h - margin, py));
                    sb.Append(F($"{px},{py} "));
                }
                sb.AppendLine("\"/>");
            }

            // Legend
            int lx = w - margin - 130, ly = margin + 8;
            int legendIdx = 0;
            foreach (var kvp in byWl.OrderBy(k => k.Key))
            {
                int wIdx = kvp.Key;
                string color = opt.GetWavelengthColor(wIdx);
                string label = wavelengthLabels != null && wIdx < wavelengthLabels.Length
                    ? wavelengthLabels[wIdx] : $"W{wIdx + 1}";
                int yy = ly + legendIdx * 14;
                sb.AppendLine(F($"<line x1=\"{lx}\" y1=\"{yy}\" x2=\"{lx + 20}\" y2=\"{yy}\" stroke=\"{color}\" stroke-width=\"1.5\"/>"));
                sb.AppendLine(F($"<text x=\"{lx + 25}\" y=\"{yy + 4}\" font-size=\"8\">{Esc(label)}</text>"));
                legendIdx++;
            }

            // Reference wavelength label
            if (!string.IsNullOrEmpty(referenceWavelengthLabel))
            {
                int refY = h - margin + 30;
                sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"{refY}\" text-anchor=\"middle\" font-size=\"9\" fill=\"#666\">Reference: {Esc(referenceWavelengthLabel)} (zero line)</text>"));
            }

            sb.AppendLine(F($"<rect x=\"{margin}\" y=\"{margin}\" width=\"{pw}\" height=\"{ph}\" fill=\"none\" stroke=\"#ccc\" stroke-width=\"0.5\"/>"));
            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        /// <summary>
        /// Render lateral color as a full HTML page.
        /// </summary>
        public static string RenderPage(LateralColorResult result, string title,
            double maxField, int numWavelengths,
            string[]? wavelengthLabels = null, RenderingOptions? options = null,
            string fieldUnit = "deg")
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head>");
            sb.AppendLine("<title>" + Esc(title) + "</title>");
            sb.AppendLine("<style>body{font-family:sans-serif;max-width:800px;margin:0 auto;padding:20px} svg{border:1px solid #ddd}</style>");
            sb.AppendLine("</head><body>");
            sb.AppendLine("<h1>" + Esc(title) + "</h1>");
            sb.AppendLine("<p>Lateral shift of chief ray relative to primary wavelength.</p>");
            sb.AppendLine(Render(result, title, maxField, numWavelengths, wavelengthLabels, options, fieldUnit));
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
