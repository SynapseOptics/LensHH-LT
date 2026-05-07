using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering
{
    public static class SeidelRenderer
    {
        private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        // Coefficient colors: one color per aberration type so the legend is
        // a fixed 7 entries regardless of surface count.
        private static readonly string[] CoeffColors = {
            "#d62728", // S1 Spherical — red
            "#2ca02c", // S2 Coma — green
            "#7f3fbf", // S3 Astigmatism — purple
            "#17becf", // S4 Field Curvature — cyan
            "#ffd400", // S5 Distortion — yellow
            "#7b6e3a", // CL Axial Color — olive
            "#9bc985", // CT Lateral Color — pale green
        };
        private static readonly string[] CoeffNames = {
            "Spherical", "Coma", "Astigmatism", "Field Curvature",
            "Distortion", "Axial Color", "Lateral Color"
        };
        private static readonly string[] CoeffShort = { "S1", "S2", "S3", "S4", "S5", "CL", "CT" };

        /// <summary>
        /// Render Seidel coefficients as a bar chart SVG.
        /// X axis: one column per surface, plus a final "SUM" column for the
        /// system totals. Inside each column, the seven aberration types are
        /// drawn as side-by-side colored bars (one color per type).
        /// </summary>
        public static string Render(SeidelResult result, string title = "",
            RenderingOptions? options = null)
        {
            int numSurf = result.SurfaceData.Count;
            int numCols = numSurf + 1; // +1 for SUM column
            int numCoef = CoeffShort.Length;

            // Width scales with surface count. Each column needs room for 7
            // sub-bars plus a small gap; ~30 px per column reads well.
            int margin = 60;
            int rightPad = 20;
            int colWidth = Math.Max(28, Math.Min(60, 600 / Math.Max(numCols, 1)));
            int pw = colWidth * numCols;
            // Reserve enough horizontal space for the bottom legend so the
            // longest label ("Field Curvature") doesn't get clipped on lenses
            // with few surfaces.
            int legendItemWidth = 105;
            int legendW = numCoef * legendItemWidth;
            int w = Math.Max(margin + pw + rightPad, margin + legendW + rightPad);
            int h = 460; // includes legend strip at the bottom
            int legendH = 60;
            int ph = h - 2 * margin - legendH;

            // Y scale uses the largest absolute coefficient anywhere (per-surface
            // or in the totals) so the SUM column never gets clipped.
            double maxVal = 0;
            foreach (var sd in result.SurfaceData)
            {
                maxVal = Math.Max(maxVal, AbsMax(sd.S1, sd.S2, sd.S3, sd.S4, sd.S5, sd.CL, sd.CT));
            }
            maxVal = Math.Max(maxVal, AbsMax(result.S1, result.S2, result.S3,
                result.S4, result.S5, result.CL, result.CT));
            if (maxVal < 1e-10) maxVal = 0.1;
            maxVal *= 1.15;

            var sb = new StringBuilder();
            sb.AppendLine(F($"<svg width=\"{w}\" height=\"{h}\" xmlns=\"http://www.w3.org/2000/svg\">"));
            sb.AppendLine(F($"<rect width=\"{w}\" height=\"{h}\" fill=\"white\"/>"));

            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"18\" text-anchor=\"middle\" font-size=\"12\" font-weight=\"bold\">{Esc(title)}</text>"));

            // Axes
            int zeroY = margin + ph / 2;
            sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{zeroY}\" x2=\"{margin + pw}\" y2=\"{zeroY}\" stroke=\"black\" stroke-width=\"1\"/>"));
            sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{margin}\" x2=\"{margin}\" y2=\"{margin + ph}\" stroke=\"black\" stroke-width=\"1\"/>"));

            // Y-axis label
            sb.AppendLine(F($"<text x=\"14\" y=\"{margin + ph / 2}\" text-anchor=\"middle\" font-size=\"10\" fill=\"#333\" transform=\"rotate(-90 14 {margin + ph / 2})\">Seidel Coefficient (mm)</text>"));

            // Y grid + tick labels
            int numYTicks = 4;
            for (int i = -numYTicks; i <= numYTicks; i++)
            {
                double val = i * maxVal / numYTicks;
                int y = zeroY - (int)(i * ph / 2.0 / numYTicks);
                if (i != 0)
                    sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{y}\" x2=\"{margin + pw}\" y2=\"{y}\" stroke=\"#eee\" stroke-width=\"0.5\"/>"));
                sb.AppendLine(F($"<text x=\"{margin - 5}\" y=\"{y + 4}\" text-anchor=\"end\" font-size=\"7\" fill=\"#666\">{LabelFormat.Tick(val, maxVal / numYTicks)}</text>"));
            }

            // Per-column inner bar layout: 7 bars with small gaps inside each column.
            double innerPad = colWidth * 0.10;
            double slotWidth = (colWidth - 2 * innerPad) / numCoef;
            double barWidth = slotWidth * 0.85;

            for (int c = 0; c < numCols; c++)
            {
                double colX = margin + c * colWidth;

                // Vertical separator between columns
                if (c > 0)
                    sb.AppendLine(F($"<line x1=\"{colX:F1}\" y1=\"{margin}\" x2=\"{colX:F1}\" y2=\"{margin + ph}\" stroke=\"#ddd\" stroke-width=\"0.5\"/>"));

                // Column label (surface number, or "SUM" for the last column)
                bool isSum = (c == numCols - 1);
                string colLabel = isSum ? "SUM" : result.SurfaceData[c].SurfaceIndex.ToString(CultureInfo.InvariantCulture);
                int labelX = (int)(colX + colWidth / 2);
                sb.AppendLine(F($"<text x=\"{labelX}\" y=\"{margin - 6}\" text-anchor=\"middle\" font-size=\"9\" font-weight=\"bold\" fill=\"#333\">{colLabel}</text>"));

                // Bars: one per coefficient type
                for (int k = 0; k < numCoef; k++)
                {
                    double val = isSum ? GetCoeff(result, k) : GetCoeff(result.SurfaceData[c], k);
                    double barX = colX + innerPad + k * slotWidth + (slotWidth - barWidth) / 2.0;
                    double barH = Math.Abs(val) / maxVal * (ph / 2.0);
                    double barY = val >= 0 ? zeroY - barH : zeroY;
                    sb.AppendLine(F($"<rect x=\"{barX:F1}\" y=\"{barY:F1}\" width=\"{barWidth:F1}\" height=\"{barH:F1}\" fill=\"{CoeffColors[k]}\" opacity=\"0.92\"/>"));
                }
            }

            // Right edge of plot area
            sb.AppendLine(F($"<line x1=\"{margin + pw}\" y1=\"{margin}\" x2=\"{margin + pw}\" y2=\"{margin + ph}\" stroke=\"#bbb\" stroke-width=\"0.5\"/>"));

            // Legend strip at the bottom (one swatch per coefficient type).
            // Width is reserved up-front in `legendItemWidth`/`legendW` so the
            // last label ("Lateral Color") never falls off the right edge.
            int legY = margin + ph + 20;
            int legSwatchW = 16, legSwatchH = 12, legGap = 6;
            int legStartX = Math.Max(margin, (w - legendW) / 2);
            for (int k = 0; k < numCoef; k++)
            {
                int lx = legStartX + k * legendItemWidth;
                sb.AppendLine(F($"<rect x=\"{lx}\" y=\"{legY}\" width=\"{legSwatchW}\" height=\"{legSwatchH}\" fill=\"{CoeffColors[k]}\" opacity=\"0.92\"/>"));
                sb.AppendLine(F($"<text x=\"{lx + legSwatchW + legGap}\" y=\"{legY + legSwatchH - 2}\" font-size=\"9\" fill=\"#333\">{CoeffNames[k]}</text>"));
            }

            // Totals line below the legend
            string totals = F($"Totals: S1={LabelFormat.Auto(result.S1)} S2={LabelFormat.Auto(result.S2)} S3={LabelFormat.Auto(result.S3)} S4={LabelFormat.Auto(result.S4)} S5={LabelFormat.Auto(result.S5)} CL={LabelFormat.Auto(result.CL)} CT={LabelFormat.Auto(result.CT)}");
            sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"{legY + legSwatchH + 16}\" text-anchor=\"middle\" font-size=\"8\" fill=\"#555\">{totals}</text>"));

            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        private static double AbsMax(params double[] vals)
        {
            double m = 0;
            for (int i = 0; i < vals.Length; i++)
            {
                double a = Math.Abs(vals[i]);
                if (a > m) m = a;
            }
            return m;
        }

        private static double GetCoeff(SeidelSurfaceData sd, int k) => k switch
        {
            0 => sd.S1, 1 => sd.S2, 2 => sd.S3, 3 => sd.S4,
            4 => sd.S5, 5 => sd.CL, 6 => sd.CT, _ => 0,
        };

        private static double GetCoeff(SeidelResult r, int k) => k switch
        {
            0 => r.S1, 1 => r.S2, 2 => r.S3, 3 => r.S4,
            4 => r.S5, 5 => r.CL, 6 => r.CT, _ => 0,
        };

        /// <summary>
        /// Render totals-only bar chart: one bar per aberration type (S1-S5, CL, CT).
        /// </summary>
        public static string RenderTotals(SeidelResult result, string title = "",
            RenderingOptions? options = null)
        {
            int w = 500, h = 350;
            int margin = 60;
            int pw = w - 2 * margin;
            int ph = h - 2 * margin;

            string[] labels = { "S1", "S2", "S3", "S4", "S5", "CL", "CT" };
            double[] values = { result.S1, result.S2, result.S3, result.S4, result.S5, result.CL, result.CT };
            string[] colors = { "#2060ff", "#20aa20", "#ff2020", "#cc6600", "#8800ff", "#008888", "#aa2060" };

            double maxVal = 0;
            foreach (double v in values)
                if (Math.Abs(v) > maxVal) maxVal = Math.Abs(v);
            if (maxVal < 1e-10) maxVal = 0.01;
            maxVal *= 1.2;

            var sb = new StringBuilder();
            sb.AppendLine(F($"<svg width=\"{w}\" height=\"{h}\" xmlns=\"http://www.w3.org/2000/svg\">"));
            sb.AppendLine(F($"<rect width=\"{w}\" height=\"{h}\" fill=\"white\"/>"));

            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"18\" text-anchor=\"middle\" font-size=\"12\" font-weight=\"bold\">{Esc(title)}</text>"));

            int zeroY = margin + ph / 2;
            sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{zeroY}\" x2=\"{w - margin}\" y2=\"{zeroY}\" stroke=\"black\" stroke-width=\"1\"/>"));
            sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{margin}\" x2=\"{margin}\" y2=\"{h - margin}\" stroke=\"black\" stroke-width=\"1\"/>"));

            // Y-axis label
            sb.AppendLine(F($"<text x=\"14\" y=\"{h / 2}\" text-anchor=\"middle\" font-size=\"10\" fill=\"#333\" transform=\"rotate(-90 14 {h / 2})\">Seidel Coefficient (mm)</text>"));

            // Y grid
            int numYTicks = 4;
            for (int i = -numYTicks; i <= numYTicks; i++)
            {
                double val = i * maxVal / numYTicks;
                int y = zeroY - (int)(i * ph / 2.0 / numYTicks);
                if (i != 0)
                    sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{y}\" x2=\"{w - margin}\" y2=\"{y}\" stroke=\"#eee\" stroke-width=\"0.5\"/>"));
                sb.AppendLine(F($"<text x=\"{margin - 5}\" y=\"{y + 4}\" text-anchor=\"end\" font-size=\"8\" fill=\"#666\">{LabelFormat.Tick(val, maxVal / numYTicks)}</text>"));
            }

            // Bars
            double barWidth = pw * 0.7 / labels.Length;
            double gap = pw * 0.3 / (labels.Length + 1);

            for (int g = 0; g < labels.Length; g++)
            {
                double barX = margin + gap * (g + 1) + barWidth * g;
                double barH = Math.Abs(values[g]) / maxVal * (ph / 2.0);
                double barY = values[g] >= 0 ? zeroY - barH : zeroY;

                sb.AppendLine(F($"<rect x=\"{barX:F1}\" y=\"{barY:F1}\" width=\"{barWidth:F1}\" height=\"{barH:F1}\" fill=\"{colors[g]}\" opacity=\"0.85\"/>"));

                // Label
                int labelX = (int)(barX + barWidth / 2);
                sb.AppendLine(F($"<text x=\"{labelX}\" y=\"{h - margin + 15}\" text-anchor=\"middle\" font-size=\"10\" font-weight=\"bold\">{labels[g]}</text>"));

                // Value above/below bar
                int valY = values[g] >= 0 ? (int)barY - 4 : (int)(barY + barH + 12);
                sb.AppendLine(F($"<text x=\"{labelX}\" y=\"{valY}\" text-anchor=\"middle\" font-size=\"7\" fill=\"#333\">{LabelFormat.Auto(values[g])}</text>"));
            }

            sb.AppendLine(F($"<rect x=\"{margin}\" y=\"{margin}\" width=\"{pw}\" height=\"{ph}\" fill=\"none\" stroke=\"#ccc\" stroke-width=\"0.5\"/>"));
            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        /// <summary>
        /// Render Seidel coefficients as a full HTML page.
        /// </summary>
        public static string RenderPage(SeidelResult result, string title = "Seidel Coefficients",
            RenderingOptions? options = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head>");
            sb.AppendLine("<meta charset=\"utf-8\">");
            sb.AppendLine("<title>" + Esc(title) + "</title>");
            sb.AppendLine("<style>body{font-family:sans-serif;max-width:900px;margin:0 auto;padding:20px} svg{border:1px solid #ddd} table{border-collapse:collapse;margin-top:15px} td,th{border:1px solid #ddd;padding:4px 8px;text-align:right;font-size:11px} th{background:#f5f5f5}</style>");
            sb.AppendLine("</head><body>");
            sb.AppendLine("<h1>" + Esc(title) + "</h1>");
            sb.AppendLine("<h2>System Totals</h2>");
            sb.AppendLine(RenderTotals(result, "Total Seidel Coefficients", options));
            sb.AppendLine("<h2>Per-Surface Contributions</h2>");
            sb.AppendLine(Render(result, "Per-Surface Seidel Coefficients", options));

            // Also add a table
            sb.AppendLine("<table><tr><th>Surface</th><th>S1</th><th>S2</th><th>S3</th><th>S4</th><th>S5</th><th>CL</th><th>CT</th></tr>");
            foreach (var sd in result.SurfaceData)
            {
                sb.AppendLine($"<tr><td>{sd.SurfaceIndex}</td><td>{LabelFormat.Auto(sd.S1)}</td><td>{LabelFormat.Auto(sd.S2)}</td><td>{LabelFormat.Auto(sd.S3)}</td><td>{LabelFormat.Auto(sd.S4)}</td><td>{LabelFormat.Auto(sd.S5)}</td><td>{LabelFormat.Auto(sd.CL)}</td><td>{LabelFormat.Auto(sd.CT)}</td></tr>");
            }
            sb.AppendLine($"<tr style=\"font-weight:bold\"><td>Total</td><td>{LabelFormat.Auto(result.S1)}</td><td>{LabelFormat.Auto(result.S2)}</td><td>{LabelFormat.Auto(result.S3)}</td><td>{LabelFormat.Auto(result.S4)}</td><td>{LabelFormat.Auto(result.S5)}</td><td>{LabelFormat.Auto(result.CL)}</td><td>{LabelFormat.Auto(result.CT)}</td></tr>");
            sb.AppendLine("</table>");

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
