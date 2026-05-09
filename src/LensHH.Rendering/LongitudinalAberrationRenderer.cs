using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering
{
    /// <summary>
    /// Renders the standard "longitudinal spherical aberration with axial
    /// chromatic" plot: pupil radius (Y) vs longitudinal focal shift (X)
    /// relative to the primary-wavelength paraxial focus, one curve per
    /// wavelength.
    /// </summary>
    public static class LongitudinalAberrationRenderer
    {
        private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        public static string Render(LongitudinalAberrationResult result, string title = "",
            RenderingOptions? options = null, string[]? wavelengthLabels = null)
        {
            var opt = options ?? new RenderingOptions();
            int w = 600, h = 460;
            int margin = 60;
            int pw = w - 2 * margin;
            int ph = h - 2 * margin;

            var sb = new StringBuilder();
            sb.AppendLine(F($"<svg width=\"{w}\" height=\"{h}\" xmlns=\"http://www.w3.org/2000/svg\">"));
            sb.AppendLine(F($"<rect width=\"{w}\" height=\"{h}\" fill=\"white\"/>"));

            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"18\" text-anchor=\"middle\" font-size=\"12\" font-weight=\"bold\">{Esc(title)}</text>"));

            if (result.IsAfocal)
            {
                sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"{h / 2}\" text-anchor=\"middle\" font-size=\"11\" fill=\"#888\">Not applicable for afocal systems</text>"));
                sb.AppendLine("</svg>");
                return sb.ToString();
            }

            if (result.Points.Count == 0 || result.PupilRadiusMax <= 0)
            {
                sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"{h / 2}\" text-anchor=\"middle\" font-size=\"11\" fill=\"#888\">No data</text>"));
                sb.AppendLine("</svg>");
                return sb.ToString();
            }

            int numWl = result.WavelengthsUm.Length;

            // Group points by wavelength index, sorted by ascending pupil radius.
            var perWl = new List<List<LongitudinalAberrationPoint>>(numWl);
            for (int i = 0; i < numWl; i++) perWl.Add(new List<LongitudinalAberrationPoint>());
            foreach (var p in result.Points)
            {
                if (p.WavelengthIndex >= 0 && p.WavelengthIndex < numWl)
                    perWl[p.WavelengthIndex].Add(p);
            }
            for (int i = 0; i < numWl; i++)
                perWl[i].Sort((a, b) => a.PupilRadius.CompareTo(b.PupilRadius));

            // X half-extent: largest |shift| seen across all points; pad by 20%.
            double maxShift = 0;
            foreach (var p in result.Points)
            {
                double s = Math.Abs(p.LongitudinalShift);
                if (s > maxShift) maxShift = s;
            }
            if (maxShift <= 0) maxShift = 0.001; // 1 µm fallback so axis is drawn
            maxShift *= 1.2;

            double maxR = result.PupilRadiusMax;

            // Axes: vertical zero-shift line at center, horizontal pupil=0 line at bottom.
            int centerX = margin + pw / 2;
            int baseY = h - margin;
            sb.AppendLine(F($"<line x1=\"{centerX}\" y1=\"{margin}\" x2=\"{centerX}\" y2=\"{baseY}\" stroke=\"black\" stroke-width=\"1\"/>"));
            sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{baseY}\" x2=\"{margin + pw}\" y2=\"{baseY}\" stroke=\"black\" stroke-width=\"1\"/>"));

            // Y ticks (pupil radius, mm), 0 at bottom, maxR at top.
            for (int i = 0; i <= 5; i++)
            {
                double r = maxR * i / 5.0;
                int y = baseY - (int)((double)i / 5.0 * ph);
                sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{y}\" x2=\"{margin + pw}\" y2=\"{y}\" stroke=\"#eee\" stroke-width=\"0.5\"/>"));
                sb.AppendLine(F($"<text x=\"{margin - 5}\" y=\"{y + 4}\" text-anchor=\"end\" font-size=\"8\" fill=\"#666\">{LabelFormat.Tick(r, maxR / 5.0)}</text>"));
            }

            // X ticks (shift, mm), symmetric.
            int numXTicks = 4;
            for (int i = -numXTicks; i <= numXTicks; i++)
            {
                double s = i * maxShift / numXTicks;
                int x = centerX + (int)((double)i / numXTicks * (pw / 2.0));
                if (i != 0)
                    sb.AppendLine(F($"<line x1=\"{x}\" y1=\"{margin}\" x2=\"{x}\" y2=\"{baseY}\" stroke=\"#eee\" stroke-width=\"0.5\"/>"));
                sb.AppendLine(F($"<text x=\"{x}\" y=\"{baseY + 12}\" text-anchor=\"middle\" font-size=\"7\" fill=\"#666\">{LabelFormat.Tick(s, maxShift / numXTicks)}</text>"));
            }

            // Axis titles
            sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"{h - 5}\" text-anchor=\"middle\" font-size=\"9\" fill=\"#333\">Focus Shift (mm)</text>"));
            sb.AppendLine(F($"<text x=\"12\" y=\"{h / 2}\" text-anchor=\"middle\" font-size=\"9\" fill=\"#333\" transform=\"rotate(-90 12 {h / 2})\">Pupil Radius (mm)</text>"));

            // One curve per wavelength.
            for (int wIdx = 0; wIdx < numWl; wIdx++)
            {
                var pts = perWl[wIdx];
                if (pts.Count < 2) continue;
                string color = opt.GetWavelengthColor(wIdx);

                sb.Append(F($"<polyline fill=\"none\" stroke=\"{color}\" stroke-width=\"1.5\" points=\""));
                foreach (var p in pts)
                {
                    int px = centerX + (int)(p.LongitudinalShift / maxShift * (pw / 2.0));
                    int py = baseY - (int)(p.PupilRadius / maxR * ph);
                    px = Math.Max(margin, Math.Min(margin + pw, px));
                    py = Math.Max(margin, Math.Min(baseY, py));
                    sb.Append(F($"{px},{py} "));
                }
                sb.AppendLine("\"/>");
            }

            // Legend
            int lx = margin + 5, ly = margin + 10;
            for (int wIdx = 0; wIdx < numWl; wIdx++)
            {
                int lyi = ly + wIdx * 13;
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

        public static string RenderPage(LongitudinalAberrationResult result,
            string title = "Longitudinal Aberration",
            RenderingOptions? options = null, string[]? wavelengthLabels = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
            sb.AppendLine("<title>" + Esc(title) + "</title>");
            sb.AppendLine("<style>body{font-family:sans-serif;max-width:800px;margin:0 auto;padding:20px} svg{border:1px solid #ddd}</style>");
            sb.AppendLine("</head><body>");
            sb.AppendLine("<h1>" + Esc(title) + "</h1>");
            sb.AppendLine(Render(result, title, options, wavelengthLabels));
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private static string Esc(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
