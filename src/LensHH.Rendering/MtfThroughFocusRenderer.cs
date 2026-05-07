using System;
using System.Globalization;
using System.Text;
using LensHH.Core.Analysis;

namespace LensHH.Rendering
{
    public static class MtfThroughFocusRenderer
    {
        private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        public static string Render(MtfThroughFocusResult result, string title = "",
            RenderingOptions? options = null)
        {
            var opt = options ?? new RenderingOptions();
            int w = 500, h = 350;
            int margin = 55;
            int pw = w - 2 * margin;
            int ph = h - 2 * margin;

            double minShift = double.MaxValue, maxShift = double.MinValue;
            foreach (var p in result.Points)
            {
                if (p.FocusShift < minShift) minShift = p.FocusShift;
                if (p.FocusShift > maxShift) maxShift = p.FocusShift;
            }
            double shiftRange = maxShift - minShift;
            if (shiftRange < 1e-10) shiftRange = 0.2;

            var sb = new StringBuilder();
            sb.AppendLine(F($"<svg width=\"{w}\" height=\"{h}\" xmlns=\"http://www.w3.org/2000/svg\">"));
            sb.AppendLine(F($"<rect width=\"{w}\" height=\"{h}\" fill=\"white\"/>"));

            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"14\" text-anchor=\"middle\" font-size=\"11\" font-weight=\"bold\">{Esc(title)}</text>"));

            string xAxisLabel = result.IsAfocal ? "Focus shift (diopters)" : "Focus Shift (mm)";
            sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"{h - 3}\" text-anchor=\"middle\" font-size=\"10\">{xAxisLabel}</text>"));

            // Axes
            sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{margin}\" x2=\"{margin}\" y2=\"{h - margin}\" stroke=\"black\"/>"));
            sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{h - margin}\" x2=\"{w - margin}\" y2=\"{h - margin}\" stroke=\"black\"/>"));

            // Y grid
            for (int g = 0; g <= 5; g++)
            {
                double v = g * 0.2;
                int gy = h - margin - (int)(v * ph);
                sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{gy}\" x2=\"{w - margin}\" y2=\"{gy}\" stroke=\"#eee\"/>"));
                sb.AppendLine(F($"<text x=\"{margin - 4}\" y=\"{gy + 4}\" text-anchor=\"end\" font-size=\"9\">{LabelFormat.Tick(v, 0.2)}</text>"));
            }

            // X axis tick labels
            int numXTicks = 5;
            double xTickStep = shiftRange / numXTicks;
            for (int g = 0; g <= numXTicks; g++)
            {
                double frac = (double)g / numXTicks;
                double val = minShift + frac * shiftRange;
                int gx = margin + (int)(frac * pw);
                sb.AppendLine(F($"<line x1=\"{gx}\" y1=\"{h - margin}\" x2=\"{gx}\" y2=\"{h - margin + 4}\" stroke=\"black\"/>"));
                sb.AppendLine(F($"<text x=\"{gx}\" y=\"{h - margin + 15}\" text-anchor=\"middle\" font-size=\"8\" fill=\"#666\">{LabelFormat.Tick(val, xTickStep)}</text>"));
            }

            // Tangential (blue)
            var tangPath = new StringBuilder();
            foreach (var p in result.Points)
            {
                int x = margin + (int)((p.FocusShift - minShift) / shiftRange * pw);
                int y = h - margin - (int)(Math.Max(0, Math.Min(1, p.Tangential)) * ph);
                if (tangPath.Length == 0) tangPath.Append($"M{x},{y}"); else tangPath.Append($" L{x},{y}");
            }
            sb.AppendLine(F($"<path d=\"{tangPath}\" fill=\"none\" stroke=\"blue\" stroke-width=\"1.5\"/>"));

            // Sagittal (red)
            var sagPath = new StringBuilder();
            foreach (var p in result.Points)
            {
                int x = margin + (int)((p.FocusShift - minShift) / shiftRange * pw);
                int y = h - margin - (int)(Math.Max(0, Math.Min(1, p.Sagittal)) * ph);
                if (sagPath.Length == 0) sagPath.Append($"M{x},{y}"); else sagPath.Append($" L{x},{y}");
            }
            sb.AppendLine(F($"<path d=\"{sagPath}\" fill=\"none\" stroke=\"red\" stroke-width=\"1.5\"/>"));

            // Legend
            sb.AppendLine(F($"<line x1=\"{margin + 5}\" y1=\"{margin + 10}\" x2=\"{margin + 25}\" y2=\"{margin + 10}\" stroke=\"blue\" stroke-width=\"1.5\"/>"));
            sb.AppendLine(F($"<text x=\"{margin + 30}\" y=\"{margin + 14}\" font-size=\"9\">Tangential</text>"));
            sb.AppendLine(F($"<line x1=\"{margin + 5}\" y1=\"{margin + 24}\" x2=\"{margin + 25}\" y2=\"{margin + 24}\" stroke=\"red\" stroke-width=\"1.5\"/>"));
            sb.AppendLine(F($"<text x=\"{margin + 30}\" y=\"{margin + 28}\" font-size=\"9\">Sagittal</text>"));
            string freqUnit = result.IsAfocal ? "cy/arc-min" : "cy/mm";
            sb.AppendLine(F($"<text x=\"{margin + 5}\" y=\"{margin + 42}\" font-size=\"9\" fill=\"#666\">{LabelFormat.Auto(result.SpatialFrequency)} {freqUnit}</text>"));

            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        // Field colors: shared 15-entry qualitative palette.
        private static readonly string[] FieldColors = RenderingOptions.DefaultPalette15;

        /// <summary>
        /// Render all fields on one through-focus plot. Each field gets its own color.
        /// T = solid, S = dashed.
        /// </summary>
        public static string RenderAllFields(MtfThroughFocusResult[] results, string[] fieldLabels,
            string title = "", RenderingOptions? options = null)
        {
            var opt = options ?? new RenderingOptions();
            int w = 600, h = 400;
            int margin = 55;
            int pw = w - 2 * margin;
            int ph = h - 2 * margin;

            // Common focus range across all fields
            double minShift = double.MaxValue, maxShift = double.MinValue;
            foreach (var r in results)
            {
                foreach (var p in r.Points)
                {
                    if (p.FocusShift < minShift) minShift = p.FocusShift;
                    if (p.FocusShift > maxShift) maxShift = p.FocusShift;
                }
            }
            double shiftRange = maxShift - minShift;
            if (shiftRange < 1e-10) shiftRange = 0.2;

            var sb = new StringBuilder();
            sb.AppendLine(F($"<svg width=\"{w}\" height=\"{h}\" xmlns=\"http://www.w3.org/2000/svg\">"));
            sb.AppendLine(F($"<rect width=\"{w}\" height=\"{h}\" fill=\"white\"/>"));

            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"14\" text-anchor=\"middle\" font-size=\"11\" font-weight=\"bold\">{Esc(title)}</text>"));

            bool isAfocalAll = results.Length > 0 && results[0].IsAfocal;
            string xAxisLabelAll = isAfocalAll ? "Focus shift (diopters)" : "Focus Shift (mm)";
            sb.AppendLine(F($"<text x=\"{w / 2}\" y=\"{h - 3}\" text-anchor=\"middle\" font-size=\"10\">{xAxisLabelAll}</text>"));

            // Axes
            sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{margin}\" x2=\"{margin}\" y2=\"{h - margin}\" stroke=\"black\"/>"));
            sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{h - margin}\" x2=\"{w - margin}\" y2=\"{h - margin}\" stroke=\"black\"/>"));

            // Y grid
            for (int g = 0; g <= 5; g++)
            {
                double v = g * 0.2;
                int gy = h - margin - (int)(v * ph);
                sb.AppendLine(F($"<line x1=\"{margin}\" y1=\"{gy}\" x2=\"{w - margin}\" y2=\"{gy}\" stroke=\"#eee\"/>"));
                sb.AppendLine(F($"<text x=\"{margin - 4}\" y=\"{gy + 4}\" text-anchor=\"end\" font-size=\"9\">{LabelFormat.Tick(v, 0.2)}</text>"));
            }

            // X axis ticks
            int numXTicks = 5;
            double xTickStep = shiftRange / numXTicks;
            for (int g = 0; g <= numXTicks; g++)
            {
                double frac = (double)g / numXTicks;
                double val = minShift + frac * shiftRange;
                int gx = margin + (int)(frac * pw);
                sb.AppendLine(F($"<line x1=\"{gx}\" y1=\"{h - margin}\" x2=\"{gx}\" y2=\"{h - margin + 4}\" stroke=\"black\"/>"));
                sb.AppendLine(F($"<text x=\"{gx}\" y=\"{h - margin + 15}\" text-anchor=\"middle\" font-size=\"8\" fill=\"#666\">{LabelFormat.Tick(val, xTickStep)}</text>"));
            }

            // Draw curves for each field
            for (int fi = 0; fi < results.Length; fi++)
            {
                string color = FieldColors[fi % FieldColors.Length];
                var r = results[fi];

                // Tangential (solid)
                var tangPath = new StringBuilder();
                foreach (var p in r.Points)
                {
                    int x = margin + (int)((p.FocusShift - minShift) / shiftRange * pw);
                    int y = h - margin - (int)(Math.Max(0, Math.Min(1, p.Tangential)) * ph);
                    if (tangPath.Length == 0) tangPath.Append($"M{x},{y}"); else tangPath.Append($" L{x},{y}");
                }
                sb.AppendLine(F($"<path d=\"{tangPath}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"1.5\"/>"));

                // Sagittal (dashed)
                var sagPath = new StringBuilder();
                foreach (var p in r.Points)
                {
                    int x = margin + (int)((p.FocusShift - minShift) / shiftRange * pw);
                    int y = h - margin - (int)(Math.Max(0, Math.Min(1, p.Sagittal)) * ph);
                    if (sagPath.Length == 0) sagPath.Append($"M{x},{y}"); else sagPath.Append($" L{x},{y}");
                }
                sb.AppendLine(F($"<path d=\"{sagPath}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"1.5\" stroke-dasharray=\"5,3\"/>"));
            }

            // Legend
            int lx = w - margin - 130, ly = margin + 8;
            for (int fi = 0; fi < results.Length; fi++)
            {
                string color = FieldColors[fi % FieldColors.Length];
                string label = fi < fieldLabels.Length ? fieldLabels[fi] : $"Field {fi + 1}";
                int yy = ly + fi * 14;
                sb.AppendLine(F($"<line x1=\"{lx}\" y1=\"{yy}\" x2=\"{lx + 15}\" y2=\"{yy}\" stroke=\"{color}\" stroke-width=\"1.5\"/>"));
                sb.AppendLine(F($"<line x1=\"{lx + 18}\" y1=\"{yy}\" x2=\"{lx + 33}\" y2=\"{yy}\" stroke=\"{color}\" stroke-width=\"1.5\" stroke-dasharray=\"5,3\"/>"));
                sb.AppendLine(F($"<text x=\"{lx + 38}\" y=\"{yy + 4}\" font-size=\"8\">{Esc(label)}</text>"));
            }
            int tsY = ly + results.Length * 14;
            sb.AppendLine(F($"<text x=\"{lx}\" y=\"{tsY + 4}\" font-size=\"8\" fill=\"#666\">Solid=T  Dashed=S</text>"));

            if (results.Length > 0)
            {
                string freqUnitAll = results[0].IsAfocal ? "cy/arc-min" : "cy/mm";
                sb.AppendLine(F($"<text x=\"{lx}\" y=\"{tsY + 18}\" font-size=\"8\" fill=\"#666\">{LabelFormat.Auto(results[0].SpatialFrequency)} {freqUnitAll}</text>"));
            }

            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        public static string RenderPage(MtfThroughFocusResult result, string title = "",
            RenderingOptions? options = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
            sb.AppendLine("<style>body{font-family:sans-serif;margin:20px;}</style>");
            sb.AppendLine("</head><body>");
            if (!string.IsNullOrEmpty(title))
                sb.AppendLine($"<h2>{Esc(title)}</h2>");
            sb.AppendLine(Render(result, title, options));
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private static string Esc(string s) => s
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
