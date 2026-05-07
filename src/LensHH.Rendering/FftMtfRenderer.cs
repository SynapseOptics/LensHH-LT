using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering
{
    public static class FftMtfRenderer
    {
        private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Diffraction-limited MTF for a circular aperture.
        /// </summary>
        public static double DiffractionLimit(double freq, double cutoff)
        {
            if (cutoff <= 0 || freq >= cutoff) return 0;
            if (freq <= 0) return 1;
            double rho = freq / cutoff;
            return (2.0 / Math.PI) * (Math.Acos(rho) - rho * Math.Sqrt(1.0 - rho * rho));
        }

        /// <summary>
        /// Render a single FFT MTF vs spatial frequency plot as SVG.
        /// Shows tangential (solid) and sagittal (dashed) curves.
        /// If cutoffFrequency > 0, draws the diffraction limit curve automatically.
        /// </summary>
        public static string Render(MtfResult result, string title,
            double maxFrequency = 0, double cutoffFrequency = 0,
            RenderingOptions? options = null)
        {
            var opt = options ?? new RenderingOptions();
            int w = 500, h = 320;
            int margin = 50;
            int pw = w - 2 * margin;
            int ph = h - 2 * margin;

            if (maxFrequency <= 0)
                maxFrequency = result.MaxFrequency > 0 ? result.MaxFrequency : 100;

            bool showDL = cutoffFrequency > 0 ||
                (result.DiffractionLimit != null && result.DiffractionLimit.Count > 0);

            var sb = new StringBuilder();
            sb.AppendLine(F($"<svg width=\"{w}\" height=\"{h}\" xmlns=\"http://www.w3.org/2000/svg\">"));
            sb.AppendLine(F($"<rect width=\"{w}\" height=\"{h}\" fill=\"white\"/>"));
            sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"16\" text-anchor=\"middle\" font-size=\"11\" font-weight=\"bold\">{Esc(title)}</text>"));

            DrawAxes(sb, w, h, margin, pw, ph, maxFrequency, result.IsAfocal);

            // Diffraction limit
            if (showDL)
            {
                if (result.DiffractionLimit != null && result.DiffractionLimit.Count > 0)
                {
                    DrawCurve(sb, result.DiffractionLimit.Select(p => p.SpatialFrequency).ToList(),
                        result.DiffractionLimit.Select(p => p.Tangential).ToList(),
                        "#000", "3,2", 1.0, maxFrequency, margin, pw, ph, h);
                }
                else if (cutoffFrequency > 0)
                {
                    DrawDiffractionLimit(sb, cutoffFrequency, maxFrequency, margin, pw, ph, h);
                }
            }

            // Tangential (solid blue)
            DrawCurve(sb, result.Points.Select(p => p.SpatialFrequency).ToList(),
                result.Points.Select(p => p.Tangential).ToList(),
                "#2060ff", "", 1.5, maxFrequency, margin, pw, ph, h);

            // Sagittal (dashed red)
            DrawCurve(sb, result.Points.Select(p => p.SpatialFrequency).ToList(),
                result.Points.Select(p => p.Sagittal).ToList(),
                "#ff2020", "5,3", 1.5, maxFrequency, margin, pw, ph, h);

            // Legend
            int lx = w - margin - 110, ly = margin + 8;
            sb.AppendLine(F($"<line x1=\"{lx}\" y1=\"{ly}\" x2=\"{lx + 20}\" y2=\"{ly}\" stroke=\"#2060ff\" stroke-width=\"1.5\"/>"));
            sb.AppendLine(F($"<text x=\"{lx + 25}\" y=\"{ly + 4}\" font-size=\"8\">Tangential</text>"));
            sb.AppendLine(F($"<line x1=\"{lx}\" y1=\"{ly + 14}\" x2=\"{lx + 20}\" y2=\"{ly + 14}\" stroke=\"#ff2020\" stroke-width=\"1.5\" stroke-dasharray=\"5,3\"/>"));
            sb.AppendLine(F($"<text x=\"{lx + 25}\" y=\"{ly + 18}\" font-size=\"8\">Sagittal</text>"));
            if (showDL)
            {
                sb.AppendLine(F($"<line x1=\"{lx}\" y1=\"{ly + 28}\" x2=\"{lx + 20}\" y2=\"{ly + 28}\" stroke=\"#000\" stroke-width=\"1\" stroke-dasharray=\"3,2\"/>"));
                sb.AppendLine(F($"<text x=\"{lx + 25}\" y=\"{ly + 32}\" font-size=\"8\">Diff. Limit</text>"));
            }

            sb.AppendLine(F($"<rect x=\"{margin}\" y=\"{margin}\" width=\"{pw}\" height=\"{ph}\" fill=\"none\" stroke=\"#ccc\" stroke-width=\"0.5\"/>"));
            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        /// <summary>
        /// Render MTF vs spatial frequency for multiple wavelengths overlaid on one plot.
        /// Each wavelength shows T (solid) and S (dashed) in its wavelength color.
        /// Diffraction limit is drawn automatically from the shortest wavelength cutoff,
        /// or from per-wavelength DiffractionLimit data if present, or from cutoffFrequencies.
        /// </summary>
        public static string RenderMultiWavelength(MtfResult[] results, string title,
            double maxFrequency = 0, double[]? cutoffFrequencies = null,
            RenderingOptions? options = null)
        {
            var opt = options ?? new RenderingOptions();
            int w = 500, h = 320;
            int margin = 50;
            int pw = w - 2 * margin;
            int ph = h - 2 * margin;

            if (maxFrequency <= 0)
            {
                foreach (var r in results)
                    if (r.MaxFrequency > maxFrequency) maxFrequency = r.MaxFrequency;
                if (maxFrequency <= 0) maxFrequency = 100;
            }

            var sb = new StringBuilder();
            sb.AppendLine(F($"<svg width=\"{w}\" height=\"{h}\" xmlns=\"http://www.w3.org/2000/svg\">"));
            sb.AppendLine(F($"<rect width=\"{w}\" height=\"{h}\" fill=\"white\"/>"));
            sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"16\" text-anchor=\"middle\" font-size=\"11\" font-weight=\"bold\">{Esc(title)}</text>"));

            DrawAxes(sb, w, h, margin, pw, ph, maxFrequency,
                isAfocal: results.Length > 0 && results[0].IsAfocal);

            // Diffraction limit — use the lowest cutoff (shortest wavelength has highest cutoff)
            // Draw one DL curve per wavelength if cutoffs provided
            bool drewDL = false;
            if (cutoffFrequencies != null && cutoffFrequencies.Length > 0)
            {
                // Draw DL for each wavelength in its color (thin dotted)
                for (int i = 0; i < cutoffFrequencies.Length && i < results.Length; i++)
                {
                    if (cutoffFrequencies[i] > 0)
                    {
                        string color = opt.GetWavelengthColor(i);
                        DrawDiffractionLimit(sb, cutoffFrequencies[i], maxFrequency,
                            margin, pw, ph, h, color, "2,2", 0.8);
                    }
                }
                drewDL = true;
            }
            else
            {
                // Try to find DL data from the results
                foreach (var r in results)
                {
                    if (r.DiffractionLimit != null && r.DiffractionLimit.Count > 0)
                    {
                        DrawCurve(sb, r.DiffractionLimit.Select(p => p.SpatialFrequency).ToList(),
                            r.DiffractionLimit.Select(p => p.Tangential).ToList(),
                            "#000", "3,2", 1.0, maxFrequency, margin, pw, ph, h);
                        drewDL = true;
                        break;
                    }
                }
            }

            // Each wavelength: T solid, S dashed
            for (int i = 0; i < results.Length; i++)
            {
                var r = results[i];
                string color = opt.GetWavelengthColor(i);

                DrawCurve(sb, r.Points.Select(p => p.SpatialFrequency).ToList(),
                    r.Points.Select(p => p.Tangential).ToList(),
                    color, "", 1.5, maxFrequency, margin, pw, ph, h);

                DrawCurve(sb, r.Points.Select(p => p.SpatialFrequency).ToList(),
                    r.Points.Select(p => p.Sagittal).ToList(),
                    color, "5,3", 1.5, maxFrequency, margin, pw, ph, h);
            }

            // Legend
            int lx = w - margin - 110, ly = margin + 8;
            for (int i = 0; i < results.Length; i++)
            {
                string color = opt.GetWavelengthColor(i);
                int yy = ly + i * 26;
                sb.AppendLine(F($"<line x1=\"{lx}\" y1=\"{yy}\" x2=\"{lx + 20}\" y2=\"{yy}\" stroke=\"{color}\" stroke-width=\"1.5\"/>"));
                sb.AppendLine(F($"<text x=\"{lx + 25}\" y=\"{yy + 4}\" font-size=\"8\">W{i + 1} T</text>"));
                sb.AppendLine(F($"<line x1=\"{lx}\" y1=\"{yy + 12}\" x2=\"{lx + 20}\" y2=\"{yy + 12}\" stroke=\"{color}\" stroke-width=\"1.5\" stroke-dasharray=\"5,3\"/>"));
                sb.AppendLine(F($"<text x=\"{lx + 25}\" y=\"{yy + 16}\" font-size=\"8\">W{i + 1} S</text>"));
            }
            if (drewDL)
            {
                int yy = ly + results.Length * 26;
                sb.AppendLine(F($"<line x1=\"{lx}\" y1=\"{yy}\" x2=\"{lx + 20}\" y2=\"{yy}\" stroke=\"#000\" stroke-width=\"1\" stroke-dasharray=\"3,2\"/>"));
                sb.AppendLine(F($"<text x=\"{lx + 25}\" y=\"{yy + 4}\" font-size=\"8\">Diff. Limit</text>"));
            }

            sb.AppendLine(F($"<rect x=\"{margin}\" y=\"{margin}\" width=\"{pw}\" height=\"{ph}\" fill=\"none\" stroke=\"#ccc\" stroke-width=\"0.5\"/>"));
            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        /// <summary>
        /// Render a full page with FFT MTF plots for multiple fields and wavelengths.
        /// </summary>
        public static string RenderPage(MtfResult[][] resultsByField, string[] fieldLabels,
            string pageTitle = "FFT MTF vs Spatial Frequency",
            string[]? wavelengthLabels = null, double maxFrequency = 0,
            double[]? cutoffFrequencies = null, RenderingOptions? options = null)
        {
            var opt = options ?? new RenderingOptions();
            var sb = new StringBuilder();

            sb.AppendLine("<!DOCTYPE html><html><head>");
            sb.AppendLine("<title>" + Esc(pageTitle) + "</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body{font-family:sans-serif;max-width:1600px;margin:0 auto;padding:20px}");
            sb.AppendLine(".field-row{display:flex;flex-wrap:wrap;gap:10px;margin-bottom:20px}");
            sb.AppendLine("svg{border:1px solid #ddd}");
            sb.AppendLine("h2{margin-top:30px;border-bottom:1px solid #ccc;padding-bottom:5px}");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine("<h1>" + Esc(pageTitle) + "</h1>");
            sb.AppendLine("<p>Solid = Tangential, Dashed = Sagittal. Dotted = Diffraction Limit.</p>");

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

            sb.AppendLine("<div class=\"field-row\">");
            for (int f = 0; f < resultsByField.Length; f++)
            {
                string label = f < fieldLabels.Length ? fieldLabels[f] : "Field " + (f + 1);
                sb.AppendLine(RenderMultiWavelength(resultsByField[f], label,
                    maxFrequency, cutoffFrequencies, opt));
            }
            sb.AppendLine("</div>");

            sb.AppendLine("</body></html>");
            return sb.ToString();
        }


        // Field colors: shared 15-entry qualitative palette so designs
        // with up to 15 fields keep distinguishable line colors.
        private static readonly string[] FieldColors = RenderingOptions.DefaultPalette15;

        /// <summary>
        /// Render all fields on one plot for a single wavelength (or polychromatic).
        /// Each field gets its own color. T = solid, S = dashed.
        /// On-axis diffraction limit in black dotted.
        /// </summary>
        public static string RenderAllFields(MtfResult[] fieldResults, string[] fieldLabels,
            string title, double maxFrequency = 0, double onAxisCutoff = 0,
            RenderingOptions? options = null)
        {
            var opt = options ?? new RenderingOptions();
            int w = 600, h = 400;
            int margin = 50;
            int pw = w - 2 * margin;
            int ph = h - 2 * margin;

            if (maxFrequency <= 0)
            {
                foreach (var r in fieldResults)
                    if (r.MaxFrequency > maxFrequency) maxFrequency = r.MaxFrequency;
                if (maxFrequency <= 0) maxFrequency = 100;
            }

            var sb = new StringBuilder();
            sb.AppendLine(F($"<svg width=\"{w}\" height=\"{h}\" xmlns=\"http://www.w3.org/2000/svg\">"));
            sb.AppendLine(F($"<rect width=\"{w}\" height=\"{h}\" fill=\"white\"/>"));
            sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"18\" text-anchor=\"middle\" font-size=\"12\" font-weight=\"bold\">{Esc(title)}</text>"));

            DrawAxes(sb, w, h, margin, pw, ph, maxFrequency,
                isAfocal: fieldResults.Length > 0 && fieldResults[0].IsAfocal);

            // Each field (drawn first so DL appears on top)
            for (int i = 0; i < fieldResults.Length; i++)
            {
                var r = fieldResults[i];
                string color = FieldColors[i % FieldColors.Length];

                // Tangential (solid)
                DrawCurve(sb, r.Points.Select(p => p.SpatialFrequency).ToList(),
                    r.Points.Select(p => p.Tangential).ToList(),
                    color, "", 1.5, maxFrequency, margin, pw, ph, h);

                // Sagittal (dashed)
                DrawCurve(sb, r.Points.Select(p => p.SpatialFrequency).ToList(),
                    r.Points.Select(p => p.Sagittal).ToList(),
                    color, "5,3", 1.5, maxFrequency, margin, pw, ph, h);
            }

            // Diffraction limit (drawn on top of field curves so it's visible)
            if (fieldResults.Length > 0 && fieldResults[0].DiffractionLimit != null
                && fieldResults[0].DiffractionLimit.Count > 0)
            {
                var dl = fieldResults[0].DiffractionLimit;
                DrawCurve(sb, dl.Select(p => p.SpatialFrequency).ToList(),
                    dl.Select(p => p.Tangential).ToList(),
                    "#000", "", 2.0, maxFrequency, margin, pw, ph, h);
            }
            else if (onAxisCutoff > 0)
            {
                DrawDiffractionLimit(sb, onAxisCutoff, maxFrequency, margin, pw, ph, h, "#000", "", 2.0);
            }

            // Legend
            int lx = w - margin - 130, ly = margin + 8;
            for (int i = 0; i < fieldResults.Length; i++)
            {
                string color = FieldColors[i % FieldColors.Length];
                string label = i < fieldLabels.Length ? fieldLabels[i] : $"Field {i + 1}";
                int yy = ly + i * 14;
                sb.AppendLine(F($"<line x1=\"{lx}\" y1=\"{yy}\" x2=\"{lx + 15}\" y2=\"{yy}\" stroke=\"{color}\" stroke-width=\"1.5\"/>"));
                sb.AppendLine(F($"<line x1=\"{lx + 18}\" y1=\"{yy}\" x2=\"{lx + 33}\" y2=\"{yy}\" stroke=\"{color}\" stroke-width=\"1.5\" stroke-dasharray=\"5,3\"/>"));
                sb.AppendLine(F($"<text x=\"{lx + 38}\" y=\"{yy + 4}\" font-size=\"8\">{Esc(label)}</text>"));
            }
            int dlY = ly + fieldResults.Length * 14;
            sb.AppendLine(F($"<line x1=\"{lx}\" y1=\"{dlY}\" x2=\"{lx + 33}\" y2=\"{dlY}\" stroke=\"#000\" stroke-width=\"1\"/>"));
            sb.AppendLine(F($"<text x=\"{lx + 38}\" y=\"{dlY + 4}\" font-size=\"8\">Diff. Limit</text>"));
            int tsY = dlY + 16;
            sb.AppendLine(F($"<text x=\"{lx}\" y=\"{tsY + 4}\" font-size=\"8\" fill=\"#666\">Solid=T  Dashed=S</text>"));

            sb.AppendLine(F($"<rect x=\"{margin}\" y=\"{margin}\" width=\"{pw}\" height=\"{ph}\" fill=\"none\" stroke=\"#ccc\" stroke-width=\"0.5\"/>"));
            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        private static void DrawAxes(StringBuilder sb, int w, int h,
            int margin, int pw, int ph, double maxFrequency, bool isAfocal = false)
        {
            sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{margin}\" x2=\"{margin}\" y2=\"{h - margin}\" stroke=\"black\" stroke-width=\"1\"/>"));
            sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{h - margin}\" x2=\"{w - margin}\" y2=\"{h - margin}\" stroke=\"black\" stroke-width=\"1\"/>"));

            for (int i = 0; i <= 5; i++)
            {
                double val = i * 0.2;
                int y = (int)(h - margin - val * ph);
                sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{y}\" x2=\"{w - margin}\" y2=\"{y}\" stroke=\"#eee\" stroke-width=\"0.5\"/>"));
                sb.AppendLine(F($"<text x=\"{margin - 5}\" y=\"{y + 4}\" text-anchor=\"end\" font-size=\"8\" fill=\"#666\">{LabelFormat.Tick(val, 0.2)}</text>"));
            }

            double freqStep = FieldAxisHelper.NiceStep(maxFrequency, 6);
            for (double freq = 0; freq <= maxFrequency + freqStep * 0.01; freq += freqStep)
            {
                int x = margin + (int)(freq / maxFrequency * pw);
                if (x > w - margin) break;
                if (freq > 0)
                    sb.AppendLine(F($"<line x1=\"{x}\" y1=\"{margin}\" x2=\"{x}\" y2=\"{h - margin}\" stroke=\"#eee\" stroke-width=\"0.5\"/>"));
                sb.AppendLine(F($"<text x=\"{x}\" y=\"{h - margin + 15}\" text-anchor=\"middle\" font-size=\"8\" fill=\"#666\">{LabelFormat.Tick(freq, freqStep)}</text>"));
            }

            string xAxisTitle = isAfocal
                ? "Angular Frequency (cy/arc-min)"
                : "Spatial Frequency (cy/mm)";
            sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"{h - 5}\" text-anchor=\"middle\" font-size=\"9\" fill=\"#333\">{xAxisTitle}</text>"));
            sb.AppendLine(F($"<text x=\"12\" y=\"{h / 2}\" text-anchor=\"middle\" font-size=\"9\" fill=\"#333\" transform=\"rotate(-90 12 {h / 2})\">Modulation</text>"));
        }

        private static void DrawDiffractionLimit(StringBuilder sb,
            double cutoff, double maxFreq,
            int margin, int pw, int ph, int h,
            string color = "#000", string dash = "3,2", double strokeWidth = 1.0)
        {
            var freqs = new List<double>();
            var mtf = new List<double>();
            double step = maxFreq / 200;
            for (double f = 0; f <= Math.Min(cutoff, maxFreq); f += step)
            {
                freqs.Add(f);
                mtf.Add(DiffractionLimit(f, cutoff));
            }
            DrawCurve(sb, freqs, mtf, color, dash, strokeWidth, maxFreq, margin, pw, ph, h);
        }

        private static void DrawCurve(StringBuilder sb, List<double> freqs, List<double> mtf,
            string color, string dash, double strokeWidth,
            double maxFreq, int margin, int pw, int ph, int h)
        {
            sb.Append(F($"<polyline fill=\"none\" stroke=\"{color}\" stroke-width=\"{strokeWidth:F1}\""));
            if (!string.IsNullOrEmpty(dash))
                sb.Append(F($" stroke-dasharray=\"{dash}\""));
            sb.Append(" points=\"");
            for (int i = 0; i < freqs.Count; i++)
            {
                if (freqs[i] > maxFreq) break;
                // Skip NaN/Inf — drawing them would clamp to MTF=0 (the
                // x-axis) via the int cast and produce a phantom curve.
                if (double.IsNaN(mtf[i]) || double.IsInfinity(mtf[i])) continue;
                int x = margin + (int)(freqs[i] / maxFreq * pw);
                int y = (int)(h - margin - Math.Max(0, Math.Min(1, mtf[i])) * ph);
                sb.Append(F($"{x},{y} "));
            }
            sb.AppendLine("\"/>");
        }

        private static string Esc(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;");
        }
    }
}
