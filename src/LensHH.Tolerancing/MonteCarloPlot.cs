using System.Globalization;
using System.Text;

namespace LensHH.Tolerancing;

/// <summary>
/// The Monte Carlo MTF of one field as an SVG: the nominal lens's tangential and sagittal curves,
/// the curve half the lenses reach, the curve 90 % reach, and the band from 10 % to 90 % shaded.
/// </summary>
public static class MonteCarloPlot
{
    private const string Tangential = "#2563c9";   // blue
    private const string Sagittal = "#d9730d";     // orange: the pair stays apart for colour-blind readers

    public static string Svg(MonteCarloResult r, int fieldSlot, int width = 640, int height = 400)
    {
        const double left = 56, right = 16, top = 34, bottom = 46;
        double plotW = width - left - right, plotH = height - top - bottom;
        double fMax = r.Frequencies.Count > 0 ? r.Frequencies[^1] : 1.0;
        if (!(fMax > 0)) fMax = 1.0;
        string X(double f) => (left + plotW * f / fMax).ToString("F1", CultureInfo.InvariantCulture);
        string Y(double m) => (top + plotH * (1.0 - Math.Clamp(m, 0.0, 1.0))).ToString("F1", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\" font-family=\"Segoe UI, Arial, sans-serif\" font-size=\"12\">");
        sb.AppendLine($"<rect width=\"{width}\" height=\"{height}\" fill=\"#ffffff\"/>");
        sb.AppendLine($"<text x=\"{left}\" y=\"20\" font-size=\"14\" fill=\"#222\">Monte Carlo MTF, field {r.Fields[fieldSlot] + 1} ({r.Trials.Count} lenses)</text>");

        // Grid and axes, recessive.
        for (int i = 0; i <= 10; i++)
        {
            double m = i / 10.0;
            sb.AppendLine($"<line x1=\"{left}\" y1=\"{Y(m)}\" x2=\"{left + plotW}\" y2=\"{Y(m)}\" stroke=\"#e6e6e6\"/>");
            if (i % 2 == 0) sb.AppendLine($"<text x=\"{left - 8}\" y=\"{Y(m)}\" dy=\"4\" text-anchor=\"end\" fill=\"#666\">{m:F1}</text>");
        }
        int ticks = 5;
        for (int i = 0; i <= ticks; i++)
        {
            double f = fMax * i / ticks;
            sb.AppendLine($"<line x1=\"{X(f)}\" y1=\"{top}\" x2=\"{X(f)}\" y2=\"{top + plotH}\" stroke=\"#f0f0f0\"/>");
            sb.AppendLine($"<text x=\"{X(f)}\" y=\"{top + plotH + 18}\" text-anchor=\"middle\" fill=\"#666\">{f.ToString("G4", CultureInfo.InvariantCulture)}</text>");
        }
        sb.AppendLine($"<text x=\"{left + plotW / 2}\" y=\"{height - 8}\" text-anchor=\"middle\" fill=\"#444\">Spatial frequency (cycles/mm)</text>");
        sb.AppendLine($"<text transform=\"translate(14 {top + plotH / 2}) rotate(-90)\" text-anchor=\"middle\" fill=\"#444\">MTF</text>");
        sb.AppendLine($"<rect x=\"{left}\" y=\"{top}\" width=\"{plotW}\" height=\"{plotH}\" fill=\"none\" stroke=\"#999\"/>");

        foreach (var (o, colour) in new[] { (MtfOrientation.Tangential, Tangential), (MtfOrientation.Sagittal, Sagittal) })
        {
            var nominal = o == MtfOrientation.Tangential ? r.NominalMtfT[fieldSlot] : r.NominalMtfS[fieldSlot];
            var hi = r.MtfAtLeast(fieldSlot, o, 0.1);
            var p50 = r.MtfAtLeast(fieldSlot, o, 0.5);
            var p90 = r.MtfAtLeast(fieldSlot, o, 0.9);

            // The band from the curve 10 % reach down to the one 90 % reach.
            var band = new StringBuilder();
            for (int k = 0; k < r.Frequencies.Count; k++) band.Append($"{X(r.Frequencies[k])},{Y(hi[k])} ");
            for (int k = r.Frequencies.Count - 1; k >= 0; k--) band.Append($"{X(r.Frequencies[k])},{Y(p90[k])} ");
            sb.AppendLine($"<polygon points=\"{band}\" fill=\"{colour}\" fill-opacity=\"0.12\" stroke=\"none\"/>");

            sb.AppendLine(Line(r.Frequencies, nominal, X, Y, colour, 2.0, null));
            sb.AppendLine(Line(r.Frequencies, p50, X, Y, colour, 1.5, "6 3"));
            sb.AppendLine(Line(r.Frequencies, p90, X, Y, colour, 1.5, "2 3"));
        }

        // Legend: colour names the orientation; line style names the curve.
        double lx = left + plotW - 210, ly = top + 12;
        sb.AppendLine($"<rect x=\"{lx - 8}\" y=\"{ly - 12}\" width=\"210\" height=\"86\" fill=\"#ffffff\" fill-opacity=\"0.9\" stroke=\"#ddd\"/>");
        string[] labels = { "Nominal", "Reached by 50 % of lenses", "Reached by 90 % of lenses" };
        string?[] dashes = { null, "6 3", "2 3" };
        for (int i = 0; i < 3; i++)
        {
            double y = ly + 14 * i;
            sb.AppendLine($"<line x1=\"{lx}\" y1=\"{y}\" x2=\"{lx + 26}\" y2=\"{y}\" stroke=\"#444\" stroke-width=\"1.5\"{(dashes[i] == null ? "" : $" stroke-dasharray=\"{dashes[i]}\"")}/>");
            sb.AppendLine($"<text x=\"{lx + 32}\" y=\"{y}\" dy=\"4\" fill=\"#222\">{labels[i]}</text>");
        }
        sb.AppendLine($"<line x1=\"{lx}\" y1=\"{ly + 46}\" x2=\"{lx + 26}\" y2=\"{ly + 46}\" stroke=\"{Tangential}\" stroke-width=\"2\"/>");
        sb.AppendLine($"<text x=\"{lx + 32}\" y=\"{ly + 46}\" dy=\"4\" fill=\"#222\">Tangential</text>");
        sb.AppendLine($"<line x1=\"{lx + 104}\" y1=\"{ly + 46}\" x2=\"{lx + 130}\" y2=\"{ly + 46}\" stroke=\"{Sagittal}\" stroke-width=\"2\"/>");
        sb.AppendLine($"<text x=\"{lx + 136}\" y=\"{ly + 46}\" dy=\"4\" fill=\"#222\">Sagittal</text>");
        sb.AppendLine($"<text x=\"{lx}\" y=\"{ly + 64}\" dy=\"4\" fill=\"#666\">Shaded: 10 % to 90 % of lenses</text>");
        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    private static string Line(IReadOnlyList<double> f, double[] m, Func<double, string> x, Func<double, string> y,
                               string colour, double width, string? dash)
    {
        var points = new StringBuilder();
        for (int k = 0; k < f.Count; k++)
            if (double.IsFinite(m[k])) points.Append($"{x(f[k])},{y(m[k])} ");
        return $"<polyline points=\"{points}\" fill=\"none\" stroke=\"{colour}\" stroke-width=\"{width.ToString(CultureInfo.InvariantCulture)}\"" +
               (dash == null ? "" : $" stroke-dasharray=\"{dash}\"") + " stroke-linejoin=\"round\"/>";
    }
}
