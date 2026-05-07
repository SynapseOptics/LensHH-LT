using System;
using System.Globalization;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering
{
    public static class WavefrontMapRenderer
    {
        private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Render a single wavefront OPD map as an SVG heatmap.
        /// Blue-white-red diverging colormap. Includes pupil outline and color bar.
        /// </summary>
        /// <param name="maxOpdScale">Shared color scale (if 0, auto-scale from this map)</param>
        public static string Render(WavefrontResult result, string title = "",
            int imageSize = 300, double maxOpdScale = 0, RenderingOptions? options = null)
        {
            int n = result.GridSize;
            int margin = 45;
            int cbWidth = 50; // color bar width
            int svgW = imageSize + margin + cbWidth;
            int svgH = imageSize + 2 * margin;

            double maxOpdAbs = maxOpdScale;
            if (maxOpdAbs <= 0)
            {
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                        if (result.Valid[i, j] && Math.Abs(result.Opd[i, j]) > maxOpdAbs)
                            maxOpdAbs = Math.Abs(result.Opd[i, j]);
            }
            if (maxOpdAbs < 1e-10) maxOpdAbs = 1.0;

            int pixelSize = Math.Max(1, imageSize / n);
            int actualSize = pixelSize * n;
            int cx = margin + actualSize / 2;
            int cy = margin + actualSize / 2;
            int radius = actualSize / 2;

            var sb = new StringBuilder();
            sb.AppendLine(F($"<svg width=\"{svgW}\" height=\"{svgH}\" xmlns=\"http://www.w3.org/2000/svg\">"));
            sb.AppendLine(F($"<rect width=\"{svgW}\" height=\"{svgH}\" fill=\"#f8f8f8\"/>"));

            // Title
            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(F($"<text x=\"{margin + actualSize / 2}\" y=\"16\" text-anchor=\"middle\" font-size=\"12\" font-weight=\"bold\">{Esc(title)}</text>"));

            // Subtitle with stats
            sb.AppendLine(F($"<text x=\"{margin + actualSize / 2}\" y=\"30\" text-anchor=\"middle\" font-size=\"10\" fill=\"#555\">P-V={LabelFormat.Auto(result.PeakToValley)}  RMS={LabelFormat.Auto(result.RmsWavefront)} waves</text>"));

            // Grid center matches LensHH convention: col=n/2, row=n/2-1
            double gridCenterCol = n / 2.0;
            double gridCenterRow = n / 2.0 - 1.0;
            double cxData = margin + (gridCenterCol + 0.5) * pixelSize;
            double cyData = margin + (gridCenterRow + 0.5) * pixelSize;
            double halfScale = n / 2.0 - 0.5;
            double dataRadius = halfScale * pixelSize;

            // Pupil base — black so vignetted (invalid) pixels show as
            // black after the data layer overdraws only the valid pixels.
            // Outside the pupil disk stays the page background (#f8f8f8).
            sb.AppendLine(F($"<circle cx=\"{cxData:F1}\" cy=\"{cyData:F1}\" r=\"{dataRadius:F1}\" fill=\"black\"/>"));

            // Clip to circle for clean edges
            sb.AppendLine(F($"<defs><clipPath id=\"pupil_{result.FieldIndex}_{result.WavelengthIndex}\">"));
            sb.AppendLine(F($"<circle cx=\"{cxData:F1}\" cy=\"{cyData:F1}\" r=\"{dataRadius:F1}\"/>"));
            sb.AppendLine("</clipPath></defs>");
            sb.AppendLine(F($"<g clip-path=\"url(#pupil_{result.FieldIndex}_{result.WavelengthIndex})\">"));

            // Render pixels
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    if (!result.Valid[i, j]) continue;
                    double val = result.Opd[i, j];
                    double normalized = val / maxOpdAbs;
                    string color = DivergingColorMap(normalized);
                    int px = margin + j * pixelSize;
                    int py = margin + i * pixelSize;
                    sb.Append(F($"<rect x=\"{px}\" y=\"{py}\" width=\"{pixelSize}\" height=\"{pixelSize}\" fill=\"{color}\"/>"));
                }
            }

            sb.AppendLine("</g>");

            // Pupil outline
            sb.AppendLine(F($"<circle cx=\"{cxData:F1}\" cy=\"{cyData:F1}\" r=\"{dataRadius:F1}\" fill=\"none\" stroke=\"#666\" stroke-width=\"1.5\"/>"));

            // Central obscuration (black circle) — detect from invalid center region
            // Check if the center pixel is invalid (obscured)
            int centerI = n / 2 - 1;
            int centerJ = n / 2;
            if (centerI >= 0 && centerI < n && centerJ >= 0 && centerJ < n && !result.Valid[centerI, centerJ])
            {
                // Find obscuration radius by scanning outward from center
                int obscPixels = 0;
                for (int r = 0; r < n / 2; r++)
                {
                    int testJ = centerJ + r;
                    if (testJ >= n || result.Valid[centerI, testJ]) break;
                    obscPixels = r + 1;
                }
                if (obscPixels > 1)
                {
                    double obscR = obscPixels * pixelSize;
                    sb.AppendLine(F($"<circle cx=\"{cxData:F1}\" cy=\"{cyData:F1}\" r=\"{obscR:F1}\" fill=\"black\"/>"));
                    sb.AppendLine(F($"<circle cx=\"{cxData:F1}\" cy=\"{cyData:F1}\" r=\"{obscR:F1}\" fill=\"none\" stroke=\"#666\" stroke-width=\"1\"/>"));
                }
            }

            // Color bar (right side)
            int cbX = margin + actualSize + 12;
            int cbH = actualSize - 20;
            int cbTop = margin + 10;
            int cbBarW = 12;

            for (int i = 0; i < cbH; i++)
            {
                double val = 1.0 - 2.0 * i / cbH;
                string color = DivergingColorMap(val);
                sb.AppendLine(F($"<rect x=\"{cbX}\" y=\"{cbTop + i}\" width=\"{cbBarW}\" height=\"1\" fill=\"{color}\"/>"));
            }
            sb.AppendLine(F($"<rect x=\"{cbX}\" y=\"{cbTop}\" width=\"{cbBarW}\" height=\"{cbH}\" fill=\"none\" stroke=\"#999\" stroke-width=\"0.5\"/>"));

            // Color bar labels
            sb.AppendLine(F($"<text x=\"{cbX + cbBarW + 3}\" y=\"{cbTop + 5}\" font-size=\"9\" fill=\"#333\">+{LabelFormat.Auto(maxOpdAbs)}</text>"));
            sb.AppendLine(F($"<text x=\"{cbX + cbBarW + 3}\" y=\"{cbTop + cbH / 2 + 3}\" font-size=\"9\" fill=\"#333\">0</text>"));
            sb.AppendLine(F($"<text x=\"{cbX + cbBarW + 3}\" y=\"{cbTop + cbH}\" font-size=\"9\" fill=\"#333\">{LabelFormat.Auto(-maxOpdAbs)}</text>"));
            sb.AppendLine(F($"<text x=\"{cbX + cbBarW + 3}\" y=\"{cbTop + cbH + 14}\" font-size=\"8\" fill=\"#666\">waves</text>"));

            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        /// <summary>
        /// Render wavefront maps as a grid: rows = fields, columns = wavelengths.
        /// Shared color scale across all maps for fair comparison.
        /// </summary>
        public static string RenderPage(WavefrontResult[] results, string[] titles,
            string pageTitle = "Wavefront Map", int imageSize = 300,
            RenderingOptions? options = null)
        {
            // Determine layout: group by field, then wavelength
            // results should be ordered: [F0W0, F0W1, F0W2, F1W0, F1W1, ...]
            int numResults = results.Length;

            // Find global max OPD for shared color scale
            double globalMax = 0;
            for (int i = 0; i < numResults; i++)
            {
                var r = results[i];
                for (int row = 0; row < r.GridSize; row++)
                    for (int col = 0; col < r.GridSize; col++)
                        if (r.Valid[row, col] && Math.Abs(r.Opd[row, col]) > globalMax)
                            globalMax = Math.Abs(r.Opd[row, col]);
            }
            if (globalMax < 1e-10) globalMax = 1.0;

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head>");
            sb.AppendLine("<meta charset=\"utf-8\">");
            sb.AppendLine("<title>" + Esc(pageTitle) + "</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body{font-family:sans-serif;max-width:1400px;margin:0 auto;padding:20px;background:#fff}");
            sb.AppendLine("h1{margin-bottom:5px}");
            sb.AppendLine(".wf-grid{display:grid;gap:8px;margin-top:15px}");
            sb.AppendLine("svg{display:block}");
            sb.AppendLine(".scale-note{color:#666;font-size:13px;margin-bottom:10px}");
            sb.AppendLine("</style>");
            sb.AppendLine("</head><body>");
            sb.AppendLine("<h1>" + Esc(pageTitle) + "</h1>");
            sb.AppendLine(F($"<p class=\"scale-note\">Shared color scale: \u00b1{LabelFormat.Auto(globalMax)} waves. Blue = negative, Red = positive, White = zero.</p>"));

            // Determine grid columns from data (group by wavelength per field)
            // Assume results are field-major: [F0W0, F0W1, F0W2, F1W0, ...]
            // Find how many wavelengths per field by looking at consecutive same-field results
            int numWlPerField = 0;
            if (numResults > 0)
            {
                int firstField = results[0].FieldIndex;
                for (int i = 0; i < numResults; i++)
                {
                    if (results[i].FieldIndex == firstField) numWlPerField++;
                    else break;
                }
            }
            if (numWlPerField == 0) numWlPerField = numResults;

            int mapWidth = imageSize + 50; // account for color bar
            sb.AppendLine(F($"<div class=\"wf-grid\" style=\"grid-template-columns: repeat({numWlPerField}, {mapWidth}px)\">"));

            for (int i = 0; i < numResults; i++)
            {
                string t = i < titles.Length ? titles[i] : "";
                sb.AppendLine(Render(results[i], t, imageSize, globalMax, options));
            }

            sb.AppendLine("</div>");
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        /// <summary>
        /// Blue-white-red diverging colormap. val in [-1, +1].
        /// </summary>
        private static string DivergingColorMap(double val)
        {
            val = Math.Max(-1, Math.Min(1, val));
            int r, g, b;

            if (val < 0)
            {
                double t = 1 + val; // 0 to 1 (blue to white)
                r = (int)(t * 255);
                g = (int)(50 + t * 205);
                b = (int)(200 + t * 55);
            }
            else
            {
                double t = val; // 0 to 1 (white to red)
                r = (int)(255 - t * 55);
                g = (int)(255 - t * 225);
                b = (int)(255 - t * 225);
            }

            r = Math.Max(0, Math.Min(255, r));
            g = Math.Max(0, Math.Min(255, g));
            b = Math.Max(0, Math.Min(255, b));

            return F($"rgb({r},{g},{b})");
        }

        private static string Esc(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;");
        }
    }
}
