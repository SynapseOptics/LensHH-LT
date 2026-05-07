using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering
{
    public static class MtfVsFieldRenderer
    {
        private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        private static readonly string[] FreqColors = {
            "#2060ff", "#20aa20", "#ff2020", "#cc6600", "#8800ff",
            "#008888", "#aa2060", "#6060a0"
        };

        /// <summary>
        /// Render MTF vs Field for multiple spatial frequencies on one plot.
        /// Each frequency gets its own color. T = solid, S = dashed.
        /// </summary>
        public static string Render(MtfVsFieldMultiFreqResult result, string title = "",
            RenderingOptions? options = null, string fieldUnit = "deg")
        {
            int w = 600, h = 400;
            int margin = 55;
            int pw = w - 2 * margin;
            int ph = h - 2 * margin;

            double maxField = result.MaxFieldY;
            if (maxField <= 0) maxField = 20;

            var sb = new StringBuilder();
            sb.AppendLine(F($"<svg width=\"{w}\" height=\"{h}\" xmlns=\"http://www.w3.org/2000/svg\">"));
            sb.AppendLine(F($"<rect width=\"{w}\" height=\"{h}\" fill=\"white\"/>"));

            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"18\" text-anchor=\"middle\" font-size=\"12\" font-weight=\"bold\">{Esc(title)}</text>"));

            // Axes
            sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{margin}\" x2=\"{margin}\" y2=\"{h - margin}\" stroke=\"black\" stroke-width=\"1\"/>"));
            sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{h - margin}\" x2=\"{w - margin}\" y2=\"{h - margin}\" stroke=\"black\" stroke-width=\"1\"/>"));

            // Y grid (MTF 0 to 1)
            for (int i = 0; i <= 5; i++)
            {
                double val = i * 0.2;
                int y = (int)(h - margin - val * ph);
                sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{y}\" x2=\"{w - margin}\" y2=\"{y}\" stroke=\"#eee\" stroke-width=\"0.5\"/>"));
                sb.AppendLine(F($"<text x=\"{margin - 5}\" y=\"{y + 4}\" text-anchor=\"end\" font-size=\"8\" fill=\"#666\">{LabelFormat.Tick(val, 0.2)}</text>"));
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
            sb.AppendLine(F($"<text x=\"12\" y=\"{h / 2}\" text-anchor=\"middle\" font-size=\"9\" fill=\"#333\" transform=\"rotate(-90 12 {h / 2})\">Modulation</text>"));

            // Draw curves for each frequency using cubic Bézier (smooth)
            int numFreqs = result.Frequencies.Length;
            int nPts = result.Points.Count;

            for (int fk = 0; fk < numFreqs; fk++)
            {
                string color = FreqColors[fk % FreqColors.Length];

                // Build pixel arrays for tangential and sagittal
                var txArr = new double[nPts];
                var tyArr = new double[nPts];
                var sxArr = new double[nPts];
                var syArr = new double[nPts];

                for (int i = 0; i < nPts; i++)
                {
                    var pt = result.Points[i];
                    txArr[i] = sxArr[i] = margin + pt.fieldY / maxField * pw;
                    tyArr[i] = h - margin - Math.Max(0, Math.Min(1, pt.Item2[fk].tang)) * ph;
                    syArr[i] = h - margin - Math.Max(0, Math.Min(1, pt.Item2[fk].sag)) * ph;
                }

                // Tangential (solid)
                sb.AppendLine(BuildSmoothPath(txArr, tyArr, nPts, color, "", 1.5));

                // Sagittal (dashed)
                sb.AppendLine(BuildSmoothPath(sxArr, syArr, nPts, color, "5,3", 1.5));
            }

            // Legend
            string freqUnit = result.IsAfocal ? "cy/arc-min" : "cy/mm";
            int lx = w - margin - 130, ly = margin + 8;
            for (int fk = 0; fk < numFreqs; fk++)
            {
                string color = FreqColors[fk % FreqColors.Length];
                int yy = ly + fk * 14;
                sb.AppendLine(F($"<line x1=\"{lx}\" y1=\"{yy}\" x2=\"{lx + 15}\" y2=\"{yy}\" stroke=\"{color}\" stroke-width=\"1.5\"/>"));
                sb.AppendLine(F($"<line x1=\"{lx + 18}\" y1=\"{yy}\" x2=\"{lx + 33}\" y2=\"{yy}\" stroke=\"{color}\" stroke-width=\"1.5\" stroke-dasharray=\"5,3\"/>"));
                sb.AppendLine(F($"<text x=\"{lx + 38}\" y=\"{yy + 4}\" font-size=\"8\">{LabelFormat.Auto(result.Frequencies[fk])} {freqUnit}</text>"));
            }
            int tsY = ly + numFreqs * 14 + 4;
            sb.AppendLine(F($"<text x=\"{lx}\" y=\"{tsY + 4}\" font-size=\"8\" fill=\"#666\">Solid=T  Dashed=S</text>"));

            sb.AppendLine(F($"<rect x=\"{margin}\" y=\"{margin}\" width=\"{pw}\" height=\"{ph}\" fill=\"none\" stroke=\"#ccc\" stroke-width=\"0.5\"/>"));
            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        /// <summary>
        /// Render as full HTML page.
        /// </summary>
        public static string RenderPage(MtfVsFieldMultiFreqResult result, string title = "FFT MTF vs Field",
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

        /// <summary>
        /// Build an SVG path with cubic Bézier curves using Catmull-Rom to Bézier conversion.
        /// This creates smooth curves that pass through every data point.
        /// </summary>
        private static string BuildSmoothPath(double[] px, double[] py, int n,
            string color, string dash, double strokeWidth)
        {
            if (n < 2) return "";

            var sb = new StringBuilder();
            sb.Append(F($"<path fill=\"none\" stroke=\"{color}\" stroke-width=\"{strokeWidth:F1}\""));
            if (!string.IsNullOrEmpty(dash))
                sb.Append(F($" stroke-dasharray=\"{dash}\""));
            sb.Append(" d=\"");

            // Move to first point
            sb.Append(F($"M{px[0]:F1},{py[0]:F1}"));

            if (n == 2)
            {
                sb.Append(F($" L{px[1]:F1},{py[1]:F1}"));
            }
            else
            {
                // Catmull-Rom to cubic Bézier conversion
                // For each segment [i, i+1], we need points [i-1, i, i+1, i+2]
                // Control points: cp1 = P_i + (P_{i+1} - P_{i-1}) / 6
                //                 cp2 = P_{i+1} - (P_{i+2} - P_i) / 6
                for (int i = 0; i < n - 1; i++)
                {
                    // Clamp indices for boundary segments
                    int im1 = Math.Max(0, i - 1);
                    int ip2 = Math.Min(n - 1, i + 2);

                    double cp1x = px[i] + (px[i + 1] - px[im1]) / 6.0;
                    double cp1y = py[i] + (py[i + 1] - py[im1]) / 6.0;
                    double cp2x = px[i + 1] - (px[ip2] - px[i]) / 6.0;
                    double cp2y = py[i + 1] - (py[ip2] - py[i]) / 6.0;

                    sb.Append(F($" C{cp1x:F1},{cp1y:F1} {cp2x:F1},{cp2y:F1} {px[i + 1]:F1},{py[i + 1]:F1}"));
                }
            }

            sb.Append("\"/>");
            return sb.ToString();
        }

        private static string Esc(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
