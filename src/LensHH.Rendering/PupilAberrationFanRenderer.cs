using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering
{
    public static class PupilAberrationFanRenderer
    {
        private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Render a single pupil aberration fan (tangential or sagittal) as an SVG string.
        /// One curve per wavelength, pupil coordinate on X, aberration (%) on Y.
        /// </summary>
        public static string RenderFan(List<PupilAberrationPoint> points, string title,
            double maxAberration, int numWavelengths, RenderingOptions? options = null)
        {
            var opt = options ?? new RenderingOptions();
            int w = 400, h = 300;
            int margin = 50;
            int pw = w - 2 * margin;
            int ph = h - 2 * margin;

            if (maxAberration <= 0)
            {
                maxAberration = 0;
                foreach (var pt in points)
                {
                    double a = Math.Abs(pt.Aberration);
                    if (a > maxAberration) maxAberration = a;
                }
                // Minimum scale of 0.5% to avoid amplifying numerical noise
                if (maxAberration < 0.1) maxAberration = 0.1;
            }

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
            double yTickStep = maxAberration / numYTicks;
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
            sb.AppendLine(F($"<text x=\"12\" y=\"{h / 2}\" text-anchor=\"middle\" font-size=\"9\" fill=\"#333\" transform=\"rotate(-90 12 {h / 2})\">Aberration (%)</text>"));

            // Group points by wavelength
            var byWavelength = new Dictionary<int, List<PupilAberrationPoint>>();
            foreach (var pt in points)
            {
                if (!byWavelength.ContainsKey(pt.WavelengthIndex))
                    byWavelength[pt.WavelengthIndex] = new List<PupilAberrationPoint>();
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
                    int py2 = h / 2 - (int)(pt.Aberration / maxAberration * (ph / 2.0));
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
        /// Render tangential and sagittal pupil aberration fans side-by-side for a single field.
        /// </summary>
        public static string RenderField(PupilAberrationResult result, string fieldLabel,
            int numWavelengths, double maxAberration = 0, RenderingOptions? options = null)
        {
            if (maxAberration <= 0)
                maxAberration = result.MaxAberration;
            // Minimum scale of 0.5% to avoid amplifying numerical noise
            if (maxAberration < 0.5)
                maxAberration = 0.5;

            string tanSvg = RenderFan(result.TangentialFan,
                fieldLabel + " \u2014 Tangential",
                maxAberration, numWavelengths, options);

            string sagSvg = RenderFan(result.SagittalFan,
                fieldLabel + " \u2014 Sagittal",
                maxAberration, numWavelengths, options);

            return "<div style=\"display:inline-flex;gap:5px\">" + tanSvg + sagSvg + "</div>";
        }

        /// <summary>
        /// Render all fields as an HTML page with pupil aberration fans.
        /// </summary>
        public static string RenderPage(PupilAberrationResult[] results, string[] fieldLabels,
            string title, int numWavelengths, double maxAberration = 0,
            RenderingOptions? options = null)
        {
            if (maxAberration <= 0)
            {
                foreach (var r in results)
                    if (r.MaxAberration > maxAberration) maxAberration = r.MaxAberration;
            }
            if (maxAberration < 0.1) maxAberration = 0.1;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<html><head>");
            sb.AppendLine("<title>" + Esc(title) + " — Pupil Aberration Fan</title>");
            sb.AppendLine("</head><body style=\"font-family:Arial;background:white;margin:20px\">");
            sb.AppendLine("<h2>" + Esc(title) + " — Pupil Aberration Fan</h2>");
            sb.AppendLine($"<p>Max aberration: ±{maxAberration:F2} %</p>");

            for (int i = 0; i < results.Length; i++)
            {
                string label = i < fieldLabels.Length ? fieldLabels[i] : $"Field {i + 1}";
                sb.AppendLine(RenderField(results[i], label, numWavelengths, maxAberration, options));
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
