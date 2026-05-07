using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering
{
    public static class SpotDiagramRenderer
    {
        private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Render a spot diagram as an SVG string.
        /// </summary>
        public static string Render(SpotDiagramResult result, string title = "",
            RenderingOptions? options = null, double fixedExtent = 0)
        {
            var opt = options ?? new RenderingOptions();
            int svgSize = opt.SvgSize;
            int margin = opt.Margin;
            int plotSize = svgSize - 2 * margin;

            double chiefX = result.ChiefRayX;
            double chiefY = result.ChiefRayY;

            double maxR = result.GeoRadius;
            if (maxR < 1e-10) maxR = 0.01;
            double extent = fixedExtent > 0 ? fixedExtent : maxR * 1.15;

            var sb = new StringBuilder();
            sb.AppendLine(F($"<svg width=\"{svgSize}\" height=\"{svgSize}\" xmlns=\"http://www.w3.org/2000/svg\">"));
            sb.AppendLine(F($"<rect width=\"{svgSize}\" height=\"{svgSize}\" fill=\"{opt.BackgroundColor}\"/>"));

            // Title
            if (!string.IsNullOrEmpty(title))
            {
                sb.AppendLine(F($"<text x=\"{svgSize / 2}\" y=\"16\" text-anchor=\"middle\" font-size=\"12\" font-weight=\"bold\">{Esc(title)}</text>"));
            }

            // Subtitle: pick a unit based on whether the engine produced
            // angular (afocal) or linear (focal) spot coordinates.
            //   focal  → engine values are in mm; display as µm (×1000)
            //   afocal → engine values are in arcmin; display as arcmin (no scale)
            double dispScale = result.IsAfocal ? 1.0 : 1000.0;
            string unitLabel = result.IsAfocal ? "arcmin" : "\u00b5m";
            double rmsDisp = result.RmsRadius * dispScale;
            double geoDisp = result.GeoRadius * dispScale;
            sb.AppendLine(F($"<text x=\"{svgSize / 2}\" y=\"30\" text-anchor=\"middle\" font-size=\"10\" fill=\"#666\">RMS={LabelFormat.Auto(rmsDisp)} {unitLabel}, GEO={LabelFormat.Auto(geoDisp)} {unitLabel}</text>"));

            int cx = margin + plotSize / 2;
            int cy = margin + plotSize / 2;

            // Crosshair
            sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{cy}\" x2=\"{margin + plotSize}\" y2=\"{cy}\" stroke=\"{opt.GridColor}\" stroke-width=\"0.5\"/>"));
            sb.AppendLine(F($"<line x1=\"{cx}\" y1=\"{margin}\" x2=\"{cx}\" y2=\"{margin + plotSize}\" stroke=\"{opt.GridColor}\" stroke-width=\"0.5\"/>"));

            // Scale labels (axis extents) — same unit as the subtitle.
            double extentDisp = extent * dispScale;
            sb.AppendLine(F($"<text x=\"{margin + plotSize - 5}\" y=\"{margin + plotSize + 12}\" text-anchor=\"end\" font-size=\"8\" fill=\"#999\">{LabelFormat.Auto(extentDisp)} {unitLabel}</text>"));
            sb.AppendLine(F($"<text x=\"{margin - 2}\" y=\"{margin + plotSize + 12}\" text-anchor=\"start\" font-size=\"8\" fill=\"#999\">-{LabelFormat.Auto(extentDisp)}</text>"));

            // RMS circle
            if (opt.ShowRmsCircle)
            {
                double rmsPixels = result.RmsRadius / extent * (plotSize / 2.0);
                if (rmsPixels > 1)
                {
                    sb.AppendLine(F($"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{rmsPixels:F1}\" fill=\"none\" stroke=\"{opt.RmsCircleColor}\" stroke-width=\"0.8\" stroke-dasharray=\"3,3\"/>"));
                    double labelX = cx + rmsPixels + 3;
                    double labelY = cy - 3;
                    sb.AppendLine(F($"<text x=\"{labelX:F0}\" y=\"{labelY:F0}\" font-size=\"7\" fill=\"{opt.RmsCircleColor}\">RMS</text>"));
                }
            }

            // GEO circle
            if (opt.ShowGeoCircle)
            {
                double geoPixels = result.GeoRadius / extent * (plotSize / 2.0);
                if (geoPixels > 1)
                {
                    sb.AppendLine(F($"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{geoPixels:F1}\" fill=\"none\" stroke=\"#ccc\" stroke-width=\"0.5\" stroke-dasharray=\"2,2\"/>"));
                }
            }

            // Ray hit dots
            double scale = (plotSize / 2.0) / extent;
            foreach (var pt in result.Points)
            {
                double dx = pt.X - chiefX;
                double dy = pt.Y - chiefY;
                int px = (int)(cx + dx * scale);
                int py = (int)(cy - dy * scale);
                string color = opt.GetWavelengthColor(pt.WavelengthIndex);
                double r = opt.DotRadius;
                double op = opt.DotOpacity;

                sb.AppendLine(F($"<circle cx=\"{px}\" cy=\"{py}\" r=\"{r:F1}\" fill=\"{color}\" opacity=\"{op:F1}\"/>"));
            }

            // Border
            sb.AppendLine(F($"<rect x=\"{margin}\" y=\"{margin}\" width=\"{plotSize}\" height=\"{plotSize}\" fill=\"none\" stroke=\"#ccc\" stroke-width=\"1\"/>"));

            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        /// <summary>
        /// Render multiple spot diagrams (one per field) into a single HTML page.
        /// </summary>
        public static string RenderPage(SpotDiagramResult[] results, string[] titles,
            string pageTitle = "Spot Diagram", string[]? wavelengthLabels = null,
            RenderingOptions? options = null)
        {
            var opt = options ?? new RenderingOptions();
            var sb = new StringBuilder();

            sb.AppendLine("<!DOCTYPE html><html><head>");
            sb.AppendLine("<title>" + Esc(pageTitle) + "</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body{font-family:sans-serif;max-width:1600px;margin:0 auto;padding:20px}");
            sb.AppendLine(".spot-row{display:flex;flex-wrap:wrap;gap:10px;margin-bottom:20px}");
            sb.AppendLine("svg{border:1px solid #ddd}");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine("<h1>" + Esc(pageTitle) + "</h1>");

            if (wavelengthLabels != null)
            {
                sb.Append("<p>Wavelengths: ");
                for (int w = 0; w < wavelengthLabels.Length; w++)
                {
                    string color = opt.GetWavelengthColor(w);
                    sb.Append("<span style=\"color:" + color + ";font-weight:bold\">");
                    sb.Append(Esc(wavelengthLabels[w]));
                    sb.Append("</span>");
                    if (w < wavelengthLabels.Length - 1) sb.Append(", ");
                }
                sb.AppendLine("</p>");
            }

            sb.AppendLine("<div class=\"spot-row\">");
            for (int i = 0; i < results.Length; i++)
            {
                string t = i < titles.Length ? titles[i] : "Field " + (i + 1);
                sb.AppendLine(Render(results[i], t, opt));
            }
            sb.AppendLine("</div>");

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
