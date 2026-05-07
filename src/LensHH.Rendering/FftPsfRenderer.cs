using System;
using System.Globalization;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering
{
    public static class FftPsfRenderer
    {
        private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Render a PSF as an SVG heatmap image.
        /// Uses a log-scale color map (hot) to show the PSF structure.
        /// </summary>
        public static string Render(PsfResult result, string title = "",
            int imageSize = 300, double logFloor = -4, RenderingOptions? options = null)
        {
            int n = result.GridSize;
            int margin = 40;
            int cbWidth = 55; // color bar + labels
            int svgW = imageSize + 2 * margin + cbWidth;
            int svgH = imageSize + 2 * margin;

            // Crop to central region (show ~50% of grid where the PSF structure is)
            int cropSize = n / 2;
            int cropStart = (n - cropSize) / 2;
            // PixelSizeMm is mm in focal mode and arc-min in afocal mode (see PsfResult).
            // For focal we display µm (×1000); for afocal we display arc-min as-is.
            string scaleUnit = result.IsAfocal ? "arc-min" : "\u00b5m";
            double pixelSizeDisplay = result.IsAfocal ? result.PixelSizeMm : result.PixelSizeMm * 1000;
            double halfExtentDisplay = cropSize / 2.0 * pixelSizeDisplay;

            var sb = new StringBuilder();
            sb.AppendLine(F($"<svg width=\"{svgW}\" height=\"{svgH}\" xmlns=\"http://www.w3.org/2000/svg\">"));
            sb.AppendLine(F($"<rect width=\"{svgW}\" height=\"{svgH}\" fill=\"white\"/>"));

            // Title — center over the heatmap area only
            int heatmapCenterX = margin + imageSize / 2;
            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(F($"<text x=\"{heatmapCenterX}\" y=\"16\" text-anchor=\"middle\" font-size=\"11\" font-weight=\"bold\">{Esc(title)}</text>"));

            // Subtitle
            double strehlPct = result.StrehlRatio * 100;
            sb.AppendLine(F($"<text x=\"{heatmapCenterX}\" y=\"30\" text-anchor=\"middle\" font-size=\"9\" fill=\"#666\">Strehl={LabelFormat.Auto(strehlPct)}%, pixel={LabelFormat.Auto(pixelSizeDisplay)} {scaleUnit}</text>"));

            // Build pixel data as an embedded image using SVG rects
            // For efficiency, use a single large data URL image
            double scale = (double)imageSize / cropSize;

            // Generate color data row by row
            // Using CSS-based pixel rendering for clean SVG
            int pixelSize = Math.Max(1, imageSize / cropSize);
            int actualImageSize = pixelSize * cropSize;

            for (int i = 0; i < cropSize; i++)
            {
                for (int j = 0; j < cropSize; j++)
                {
                    double val = result.Intensity[cropStart + i, cropStart + j];
                    if (val <= 0) continue; // skip black pixels for smaller SVG

                    // Log scale
                    double logVal = Math.Log10(Math.Max(val, Math.Pow(10, logFloor)));
                    double normalized = (logVal - logFloor) / (0 - logFloor);
                    normalized = Math.Max(0, Math.Min(1, normalized));

                    if (normalized < 0.01) continue; // skip very dark pixels

                    string color = HotColorMap(normalized);
                    int px = margin + j * pixelSize;
                    int py = margin + i * pixelSize;

                    sb.Append(F($"<rect x=\"{px}\" y=\"{py}\" width=\"{pixelSize}\" height=\"{pixelSize}\" fill=\"{color}\"/>"));
                }
                sb.AppendLine();
            }

            // Scale labels
            sb.AppendLine(F($"<text x=\"{margin + actualImageSize / 2}\" y=\"{margin + actualImageSize + 14}\" text-anchor=\"middle\" font-size=\"8\" fill=\"#666\">\u00b1{LabelFormat.Auto(halfExtentDisplay)} {scaleUnit}</text>"));

            // Border
            sb.AppendLine(F($"<rect x=\"{margin}\" y=\"{margin}\" width=\"{actualImageSize}\" height=\"{actualImageSize}\" fill=\"none\" stroke=\"#ccc\" stroke-width=\"1\"/>"));

            // Color bar (right side)
            int cbX = margin + actualImageSize + 12;
            int cbH = actualImageSize - 20;
            int cbTop = margin + 10;
            int cbBarW = 12;

            for (int ci = 0; ci < cbH; ci++)
            {
                double t = 1.0 - (double)ci / cbH; // 1 at top, 0 at bottom
                string color = HotColorMap(t);
                sb.AppendLine(F($"<rect x=\"{cbX}\" y=\"{cbTop + ci}\" width=\"{cbBarW}\" height=\"1\" fill=\"{color}\"/>"));
            }
            sb.AppendLine(F($"<rect x=\"{cbX}\" y=\"{cbTop}\" width=\"{cbBarW}\" height=\"{cbH}\" fill=\"none\" stroke=\"#999\" stroke-width=\"0.5\"/>"));

            // Color bar labels — log scale tick marks
            int numTicks = (int)(-logFloor) + 1; // e.g. logFloor=-4 → ticks at 0,-1,-2,-3,-4
            for (int ti = 0; ti < numTicks; ti++)
            {
                double frac = (double)ti / (numTicks - 1); // 0 at top, 1 at bottom
                int ty = cbTop + (int)(frac * cbH);
                int decade = -ti; // 0, -1, -2, -3, -4
                sb.AppendLine(F($"<line x1=\"{cbX}\" y1=\"{ty}\" x2=\"{cbX + cbBarW + 2}\" y2=\"{ty}\" stroke=\"#666\" stroke-width=\"0.5\"/>"));
                sb.AppendLine(F($"<text x=\"{cbX + cbBarW + 4}\" y=\"{ty + 3}\" font-size=\"8\" fill=\"#333\">{decade}</text>"));
            }
            // Unit label
            sb.AppendLine(F($"<text x=\"{cbX + cbBarW / 2}\" y=\"{cbTop + cbH + 14}\" text-anchor=\"middle\" font-size=\"8\" fill=\"#666\">log\u2081\u2080(I)</text>"));

            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        /// <summary>
        /// Render multiple PSFs (one per field) into a single HTML page.
        /// </summary>
        public static string RenderPage(PsfResult[] results, string[] titles,
            string pageTitle = "FFT PSF", int imageSize = 300, double logFloor = -4,
            RenderingOptions? options = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head>");
            sb.AppendLine("<title>" + Esc(pageTitle) + "</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body{font-family:sans-serif;max-width:1600px;margin:0 auto;padding:20px}");
            sb.AppendLine(".psf-row{display:flex;flex-wrap:wrap;gap:10px;margin-bottom:20px}");
            sb.AppendLine("svg{border:1px solid #ddd}");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine("<h1>" + Esc(pageTitle) + "</h1>");
            sb.AppendLine(F($"<p>Log scale (floor = {LabelFormat.Auto(logFloor)} decades). Hot colormap.</p>"));

            sb.AppendLine("<div class=\"psf-row\">");
            for (int i = 0; i < results.Length; i++)
            {
                string t = i < titles.Length ? titles[i] : "Field " + (i + 1);
                sb.AppendLine(Render(results[i], t, imageSize, logFloor, options));
            }
            sb.AppendLine("</div>");

            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        /// <summary>
        /// Hot colormap: black → red → yellow → white
        /// </summary>
        private static string HotColorMap(double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            int r, g, b;

            if (t < 0.33)
            {
                double s = t / 0.33;
                r = (int)(s * 255);
                g = 0;
                b = 0;
            }
            else if (t < 0.66)
            {
                double s = (t - 0.33) / 0.33;
                r = 255;
                g = (int)(s * 255);
                b = 0;
            }
            else
            {
                double s = (t - 0.66) / 0.34;
                r = 255;
                g = 255;
                b = (int)(s * 255);
            }

            return F($"rgb({r},{g},{b})");
        }

        private static string Esc(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;");
        }
    }
}
