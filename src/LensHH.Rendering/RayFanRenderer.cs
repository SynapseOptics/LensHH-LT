using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering
{
    public static class RayFanRenderer
    {
        private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Render a single ray fan (tangential or sagittal) as an SVG string.
        /// One curve per wavelength, pupil coordinate on X, aberration on Y.
        /// Y unit follows <paramref name="isAfocal"/>: µm for focal, arcmin for afocal.
        /// </summary>
        public static string RenderFan(List<RayFanPoint> points, string title,
            double maxAberration, int numWavelengths, RenderingOptions? options = null,
            bool isAfocal = false)
        {
            var opt = options ?? new RenderingOptions();
            int w = 400, h = 300;
            int margin = 50;
            int pw = w - 2 * margin;
            int ph = h - 2 * margin;

            // Auto-scale if max aberration not provided
            if (maxAberration <= 0)
            {
                maxAberration = 0.001;
                foreach (var pt in points)
                {
                    double a = Math.Abs(pt.Aberration);
                    if (a > maxAberration) maxAberration = a;
                }
            }
            // Engine returns Aberration in mm (focal) or arcmin (afocal). Display
            // µm for focal (×1000) and arcmin (×1) otherwise.
            double dispScale = isAfocal ? 1.0 : 1000.0;
            string unitLabel = isAfocal ? "arcmin" : "\u00b5m";
            double maxDisp = maxAberration * dispScale;

            var sb = new StringBuilder();
            sb.AppendLine(F($"<svg width=\"{w}\" height=\"{h}\" xmlns=\"http://www.w3.org/2000/svg\">"));
            sb.AppendLine(F($"<rect width=\"{w}\" height=\"{h}\" fill=\"white\"/>"));

            // Title
            sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"16\" text-anchor=\"middle\" font-size=\"11\" font-weight=\"bold\">{Esc(title)}</text>"));

            // Axes
            sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{margin}\" x2=\"{margin}\" y2=\"{h - margin}\" stroke=\"black\" stroke-width=\"1\"/>"));
            sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{h / 2}\" x2=\"{w - margin}\" y2=\"{h / 2}\" stroke=\"black\" stroke-width=\"1\"/>"));

            // Y axis grid lines and labels
            int numYTicks = 4;
            double yTickStep = maxDisp / numYTicks;
            for (int i = -numYTicks; i <= numYTicks; i++)
            {
                double val = i * yTickStep;
                int y = h / 2 - (int)(i * ph / 2.0 / numYTicks);
                if (i != 0)
                {
                    sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{y}\" x2=\"{w - margin}\" y2=\"{y}\" stroke=\"#eee\" stroke-width=\"0.5\"/>"));
                }
                string label = LabelFormat.Tick(val, yTickStep);
                sb.AppendLine(F($"<text x=\"{margin - 5}\" y=\"{y + 4}\" text-anchor=\"end\" font-size=\"8\" fill=\"#666\">{label}</text>"));
            }

            // X axis labels
            for (int i = -2; i <= 2; i++)
            {
                double pupil = i * 0.5;
                int x = margin + (int)((pupil + 1) / 2 * pw);
                string xLabel = LabelFormat.Tick(pupil, 0.5);
                sb.AppendLine(F($"<text x=\"{x}\" y=\"{h - margin + 15}\" text-anchor=\"middle\" font-size=\"8\" fill=\"#666\">{xLabel}</text>"));
                if (i != 0)
                {
                    sb.AppendLine(F($"<line x1=\"{x}\" y1=\"{margin}\" x2=\"{x}\" y2=\"{h - margin}\" stroke=\"#eee\" stroke-width=\"0.5\"/>"));
                }
            }

            // Axis labels
            sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"{h - 5}\" text-anchor=\"middle\" font-size=\"9\" fill=\"#333\">Pupil Coordinate</text>"));
            sb.AppendLine(F($"<text x=\"12\" y=\"{h / 2}\" text-anchor=\"middle\" font-size=\"9\" fill=\"#333\" transform=\"rotate(-90 12 {h / 2})\">Aberration ({unitLabel})</text>"));

            // Group points by wavelength
            var byWavelength = new Dictionary<int, List<RayFanPoint>>();
            foreach (var pt in points)
            {
                if (!byWavelength.ContainsKey(pt.WavelengthIndex))
                    byWavelength[pt.WavelengthIndex] = new List<RayFanPoint>();
                byWavelength[pt.WavelengthIndex].Add(pt);
            }

            // Draw curves (with gaps for obscured pupil regions)
            foreach (var kvp in byWavelength.OrderBy(k => k.Key))
            {
                int wIdx = kvp.Key;
                var wlPoints = kvp.Value.OrderBy(p => p.PupilCoordinate).ToList();
                string color = opt.GetWavelengthColor(wIdx);

                double expectedStep = wlPoints.Count > 1
                    ? (wlPoints[wlPoints.Count - 1].PupilCoordinate - wlPoints[0].PupilCoordinate) / (wlPoints.Count - 1)
                    : 0.1;
                double gapThreshold = expectedStep * 2.5;

                bool inSegment = false;
                for (int idx = 0; idx < wlPoints.Count; idx++)
                {
                    var pt = wlPoints[idx];
                    bool startNew = !inSegment;
                    if (idx > 0 && Math.Abs(pt.PupilCoordinate - wlPoints[idx - 1].PupilCoordinate) > gapThreshold)
                    {
                        if (inSegment) sb.AppendLine("\"/>");
                        startNew = true;
                    }

                    if (startNew)
                    {
                        sb.Append(F($"<polyline fill=\"none\" stroke=\"{color}\" stroke-width=\"1.5\" points=\""));
                        inSegment = true;
                    }

                    int px2 = margin + (int)((pt.PupilCoordinate + 1) / 2 * pw);
                    double aberrDisp = pt.Aberration * dispScale;
                    int py2 = h / 2 - (int)(aberrDisp / maxDisp * (ph / 2.0));
                    py2 = Math.Max(margin, Math.Min(h - margin, py2));
                    sb.Append(F($"{px2},{py2} "));
                }
                if (inSegment) sb.AppendLine("\"/>");
            }

            // Border
            sb.AppendLine(F($"<rect x=\"{margin}\" y=\"{margin}\" width=\"{pw}\" height=\"{ph}\" fill=\"none\" stroke=\"#ccc\" stroke-width=\"0.5\"/>"));

            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        /// <summary>
        /// Render tangential and sagittal fans side-by-side for a single field.
        /// </summary>
        public static string RenderField(RayFanResult result, string fieldLabel,
            int numWavelengths, double maxAberration = 0, RenderingOptions? options = null)
        {
            // Use shared max aberration across both fans
            if (maxAberration <= 0)
                maxAberration = result.MaxAberration;

            string tanSvg = RenderFan(result.TangentialFan,
                fieldLabel + " \u2014 Tangential (EY vs PY)",
                maxAberration, numWavelengths, options, result.IsAfocal);

            string sagSvg = RenderFan(result.SagittalFan,
                fieldLabel + " \u2014 Sagittal (EX vs PX)",
                maxAberration, numWavelengths, options, result.IsAfocal);

            return "<div style=\"display:inline-flex;gap:5px\">" + tanSvg + sagSvg + "</div>";
        }

        /// <summary>
        /// Render a full page with ray fans for multiple fields.
        /// </summary>
        public static string RenderPage(RayFanResult[] results, string[] fieldLabels,
            string pageTitle = "Transverse Ray Fan", string[]? wavelengthLabels = null,
            int numWavelengths = 3, double maxAberration = 0, RenderingOptions? options = null)
        {
            var opt = options ?? new RenderingOptions();

            // Find global max aberration for consistent Y scale across all fields
            if (maxAberration <= 0)
            {
                foreach (var r in results)
                {
                    if (r.MaxAberration > maxAberration)
                        maxAberration = r.MaxAberration;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head>");
            sb.AppendLine("<title>" + Esc(pageTitle) + "</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body{font-family:sans-serif;max-width:1600px;margin:0 auto;padding:20px}");
            sb.AppendLine(".field-row{margin-bottom:20px}");
            sb.AppendLine("svg{border:1px solid #ddd}");
            sb.AppendLine("h2{margin-top:30px;border-bottom:1px solid #ccc;padding-bottom:5px}");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine("<h1>" + Esc(pageTitle) + "</h1>");

            // Wavelength legend
            if (wavelengthLabels != null)
            {
                sb.Append("<p>Wavelengths: ");
                for (int i = 0; i < wavelengthLabels.Length; i++)
                {
                    string color = opt.GetWavelengthColor(i);
                    sb.Append("<span style=\"color:" + color + ";font-weight:bold\">");
                    sb.Append(Esc(wavelengthLabels[i]));
                    sb.Append("</span>");
                    if (i < wavelengthLabels.Length - 1) sb.Append(", ");
                }
                sb.AppendLine("</p>");
            }

            // Match the per-fan unit: µm for focal, arcmin for afocal.
            bool pageAfocal = results.Length > 0 && results[0].IsAfocal;
            double pageScale = pageAfocal ? 1.0 : 1000.0;
            string pageUnit = pageAfocal ? "arcmin" : "\u00b5m";
            sb.AppendLine(F($"<p>Max aberration scale: \u00b1{maxAberration * pageScale:F1} {pageUnit}</p>"));

            for (int i = 0; i < results.Length; i++)
            {
                string label = i < fieldLabels.Length ? fieldLabels[i] : "Field " + (i + 1);
                sb.AppendLine("<div class=\"field-row\">");
                sb.AppendLine(RenderField(results[i], label, numWavelengths, maxAberration, opt));
                sb.AppendLine("</div>");
            }

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
