using System.Globalization;

namespace LensHH.Tolerancing.Drawings;

/// <summary>
/// Lays one element's drawing out on a sheet: the section through the axis, to a standard scale,
/// with its centre thickness and edge diameter dimensioned; the indications as the standard has
/// them; and a title block.
///
/// <para>ISO 10110: A4 landscape, the table of ISO 10110-1 below the view - left surface, material,
/// right surface - with the coded indications 0/ to 5/ (ISO 10110-2 to -7).</para>
///
/// <para>US practice: ANSI A landscape, a surface table and numbered notes in the manner of ASME
/// Y14.18: power and irregularity in fringes, scratch-dig per MIL-PRF-13830B, wedge as an edge
/// thickness difference with its beam deviation.</para>
/// </summary>
public static class Layout
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    public static Sheet Draw(ElementDrawing d, DrawingSettings settings)
    {
        bool iso = settings.Standard == DrawingStandard.Iso10110;
        var sheet = iso ? Sheet.A4(d.Number) : Sheet.AnsiA(d.Number);
        double w = sheet.Width, h = sheet.Height;
        sheet.Rect(10, 10, w - 20, h - 20, Pen.Frame, Layers.Frame);

        // The view sits in the upper left, the table or notes to its right and below; an asphere's
        // sag tables take the right of the view's space.
        double viewLeft = 22, viewRight = iso ? w - 22 : 150, viewBottom = iso ? 104 : 62, viewTop = h - 18;
        double tables = SagTables(sheet, d, viewRight, viewTop, viewBottom + 10);
        viewRight -= tables;
        // Room below the part for its thickness dimension, above it for the surface numbers; with
        // tables beside it, the part keeps to the left so its diameter's dimension clears them.
        double width = viewRight - viewLeft - 50;
        double cx = tables > 0 ? viewLeft + 5 + width / 2.0 : (viewLeft + viewRight) / 2.0;
        double scale = Section(sheet, d, cx, (viewBottom + 14 + viewTop) / 2.0, width, viewTop - viewBottom - 22);

        if (iso) IsoTable(sheet, d, settings, 22, viewBottom - 4, w - 44);
        else AsmeNotes(sheet, d, settings, 152, h - 18, w - 162);

        TitleBlock(sheet, d, settings, w - 10 - 120, 10, scale);
        return sheet;
    }

    /// <summary>
    /// The section: both surfaces sampled through their full sag, the cylinder between, the axis as a
    /// centre line, centre thickness below and edge diameter beside. Returns the scale used.
    /// </summary>
    private static double Section(Sheet sheet, ElementDrawing d, double cx, double cy, double maxWidth, double maxHeight)
    {
        double r = d.Diameter / 2.0;

        double zMin = Math.Min(0.0, Math.Min(ElementDrawing.Sag(d.Front, r), d.Thickness + ElementDrawing.Sag(d.Back, r)));
        double zMax = Math.Max(d.Thickness, Math.Max(ElementDrawing.Sag(d.Front, r), d.Thickness + ElementDrawing.Sag(d.Back, r)));
        double scale = StandardScale(Math.Min(maxWidth / (zMax - zMin), maxHeight / d.Diameter));
        double x0 = cx - scale * (zMin + zMax) / 2.0;
        Pt P(double z, double y) => new(x0 + scale * z, cy + scale * y);

        const int n = 64;
        var outline = new List<Pt>();
        for (int i = 0; i <= n; i++)
        {
            double y = r - 2.0 * r * i / n;
            outline.Add(P(ElementDrawing.Sag(d.Front, y), y));
        }
        for (int i = 0; i <= n; i++)
        {
            double y = -r + 2.0 * r * i / n;
            outline.Add(P(d.Thickness + ElementDrawing.Sag(d.Back, y), y));
        }
        sheet.Lines(outline, Pen.Outline, Layers.Outline, closed: true);

        double left = x0 + scale * zMin, right = x0 + scale * zMax;
        sheet.Line(new Pt(left - 6, cy), new Pt(right + 6, cy), Pen.Center, Layers.Center);

        // Centre thickness, below; the edge diameter, to the right of the back edge.
        double below = scale * r + 8.0;
        sheet.Dimension(P(0, 0), P(d.Thickness, 0), -below, $"{F(d.Thickness, 3)} {PlusMinus(d.ThicknessPlus, d.ThicknessMinus)}");
        double edgeZ = Math.Max(d.Thickness + ElementDrawing.Sag(d.Back, r), ElementDrawing.Sag(d.Front, r));
        sheet.Dimension(P(edgeZ, -r), P(edgeZ, r), -(right - P(edgeZ, 0).X + 8.0),
                        $"Ø{F(d.Diameter, 2)} {PlusMinus(d.DiameterPlus, d.DiameterMinus)}");

        sheet.Text(P(0, 0).X - 2.0, cy + scale * r + 4.0, "1", 3.5, Align.Right, bold: true);
        sheet.Text(P(d.Thickness, 0).X + 2.0, cy + scale * r + 4.0, "2", 3.5, Align.Left, bold: true);
        return scale;
    }

    /// <summary>The largest of ISO 5455's scales, 1:10 to 10:1, no larger than <paramref name="most"/>.</summary>
    internal static double StandardScale(double most)
    {
        double[] scales = { 10, 5, 4, 2.5, 2, 1.5, 1, 0.5, 0.4, 0.25, 0.2, 0.1 };
        foreach (double s in scales)
            if (s <= most) return s;
        return 0.1;
    }

    private static string ScaleText(double s) => s >= 1 ? $"{F(s, s % 1 == 0 ? 0 : 1)}:1" : $"1:{F(1 / s, (1 / s) % 1 == 0 ? 0 : 1)}";

    // --- ISO 10110 -------------------------------------------------------------------------

    private static void IsoTable(Sheet sheet, ElementDrawing d, DrawingSettings settings, double x, double top, double width)
    {
        double col = width / 3.0, row = 5.0, size = 2.5;
        var left = IsoSurface(d.Front, true, d.Diameter, settings);
        var right = IsoSurface(d.Back, false, d.Diameter, settings);
        var middle = new List<string>
        {
            $"{d.Material.Name}{(d.Material.Catalog == null ? "" : $" ({d.Material.Catalog})")}",
            $"nd = {F(d.Material.Nd, 5)} ± {F(d.Material.NdTolerance, 4)}",
            $"νd = {F(d.Material.Vd, 2)} ± {F(d.Material.VdTolerancePercent, 1)} %",
            settings.StressBirefringence,
            settings.Bubbles,
            settings.Inhomogeneity,
        };
        int rows = Math.Max(Math.Max(left.Count, right.Count), middle.Count) + 1;
        double y0 = top;
        sheet.Text(x, y0, "Left surface", size, bold: true);
        sheet.Text(x + col, y0, "Material specification", size, bold: true);
        sheet.Text(x + 2 * col, y0, "Right surface", size, bold: true);
        sheet.Line(new Pt(x, y0 - 1.5), new Pt(x + width, y0 - 1.5), Pen.Thin, Layers.Tables);
        for (int i = 0; i < rows - 1; i++)
        {
            double y = y0 - (i + 1) * row;
            if (i < left.Count) sheet.Text(x, y, left[i], size);
            if (i < middle.Count) sheet.Text(x + col, y, middle[i], size);
            if (i < right.Count) sheet.Text(x + 2 * col, y, right[i], size);
        }
        double bottom = y0 - rows * row + 2.0;
        sheet.Rect(x - 2, bottom, width + 4, y0 + 5 - bottom, Pen.Thin, Layers.Tables);
        sheet.Line(new Pt(x + col - 2, bottom), new Pt(x + col - 2, y0 + 5), Pen.Thin, Layers.Tables);
        sheet.Line(new Pt(x + 2 * col - 2, bottom), new Pt(x + 2 * col - 2, y0 + 5), Pen.Thin, Layers.Tables);

        var notes = new List<string>
        {
            $"Indications to ISO 10110. Dimensions in mm. 3/ in fringes at λ = {F(d.TestWavelengthNm, 1)} nm.",
            $"Edge thickness {F(d.EdgeThickness, 2)} (reference). 4/ tilt referred to the datum axis of the cylinder.",
        };
        notes.AddRange(AsphereNotes(d, settings, decentres: false));          // the 4/ indication has them
        notes.AddRange(CementNotes(d));
        if (d.MountTolerances.Count > 0) notes.Add("In its mount (assembly drawing): " + string.Join(", ", d.MountTolerances) + ".");
        notes.AddRange(d.CoatingNotes.Select(c => $"Coating {c.Name}: {c.Spec}"));
        notes.AddRange(d.Notes);
        double ny = bottom - 5.0;
        double noteWidth = sheet.Width - 10 - 120 - x - 8;      // left of the title block, clear of it
        for (int k = 0; k < notes.Count; k++)
        {
            var lines = Wrap(notes[k], noteWidth, 2.0);
            if (ny - 3.5 * (lines.Count - 1) < 13)
            {
                sheet.Warnings.Add($"{notes.Count - k} note(s) did not fit on the sheet, from \"{Clip(notes[k])}\"");
                break;
            }
            foreach (var line in lines)
            {
                sheet.Text(x, ny, line, 2.0);
                ny -= 3.5;
            }
        }
    }

    /// <summary>"0.3-0.6", or "0.3 max" when only the largest is set.</summary>
    private static string ChamferText((double? Min, double Max) c) =>
        c.Min is double min ? $"{G(min)}-{G(c.Max)}" : $"{G(c.Max)} max";

    private static string Clip(string note) => note.Length <= 40 ? note : note[..40] + "…";

    private static List<string> IsoSurface(SurfaceSpec s, bool front, double diameter, DrawingSettings settings)
    {
        var lines = new List<string>
        {
            s.IsPlane ? "R  ∞" : $"R  {F(Math.Abs(s.Radius), 3)}{RadiusPlusMinus(s)} {ElementDrawing.Shape(s, front)}" + (s.IsAspheric ? " (asph.)" : ""),
            $"Øe  {F(s.ClearAperture, 2)}",
            s.Cemented ? "Cemented" : $"Prot. chamfer {ChamferText(settings.Chamfer(diameter))}",
            $"3/{(s.RadiusTolerance == null ? G(s.PowerFringes) : "-")}({G(s.IrregularityFringes)}" +
                (s.SymmetricIrregularityFringes is double rsi ? $"/{G(rsi)})" : ")"),
            $"4/{G(s.TiltArcmin)}'" + (s.DecentreMm is double dc ? $"({F(dc, 3)})" : ""),      // ISO 10110-6's 4/σ(L)
        };
        if (!s.Cemented) lines.Add(settings.Imperfections);
        lines.Add(s.Coating == null ? "Coating: —" : $"Coating: {s.Coating}");
        return lines;
    }

    // --- US practice ---------------------------------------------------------------------

    private static void AsmeNotes(Sheet sheet, ElementDrawing d, DrawingSettings settings, double x, double top, double width)
    {
        double size = 2.2;
        // Surface table under the view, clear of the title block.
        string[] head = { "SURF", "RADIUS", "CA Ø", "POWER", "IRREG", "TILT", "SURF QUAL" };
        double[] cols = { 0, 10, 44, 58, 72, 86, 100 };
        double tx = 22, ty = 56, tableWidth = 122;
        for (int i = 0; i < head.Length; i++) sheet.Text(tx + cols[i], ty, head[i], size, bold: true);
        sheet.Line(new Pt(tx - 1, ty - 1.5), new Pt(tx + tableWidth - 1, ty - 1.5), Pen.Thin, Layers.Tables);
        var surfaces = new[] { (d.Front, true), (d.Back, false) };
        for (int k = 0; k < 2; k++)
        {
            var (s, front) = surfaces[k];
            double y = ty - 6.0 - 5.0 * k;
            string[] cells =
            {
                $"{k + 1}",
                s.IsPlane ? "PLANO" : $"{F(Math.Abs(s.Radius), 3)}{RadiusPlusMinus(s)} {ElementDrawing.Shape(s, front)}" + (s.IsAspheric ? " ASPH" : ""),
                F(s.ClearAperture, 2),
                s.RadiusTolerance == null ? $"{G(s.PowerFringes)} FR" : "-",
                $"{G(s.IrregularityFringes)} FR",
                $"{G(s.TiltArcmin)}'",
                s.Cemented ? "CEMENTED" : settings.ScratchDig,
            };
            for (int i = 0; i < cells.Length; i++) sheet.Text(tx + cols[i], y, cells[i], size);
        }
        sheet.Rect(tx - 2, ty - 15, tableWidth, 20, Pen.Thin, Layers.Tables);

        string Coat(SurfaceSpec s) => s.Cemented ? "CEMENTED" : s.Coating?.ToUpperInvariant() ?? "NONE";
        var notes = new List<string>
        {
            "INTERPRET DRAWING PER ASME Y14.18 AND ASME Y14.5. DIMENSIONS IN MILLIMETERS.",
            $"MATERIAL: {d.Material.Name}{(d.Material.Catalog == null ? "" : $" ({d.Material.Catalog})")} OR EQUIVALENT, " +
            $"nd = {F(d.Material.Nd, 5)} ± {F(d.Material.NdTolerance, 4)}, vd = {F(d.Material.Vd, 2)} ± {F(d.Material.VdTolerancePercent, 1)}%.",
            $"POWER AND IRREGULARITY IN FRINGES AT {F(d.TestWavelengthNm, 1)} NM OVER THE CLEAR APERTURE.",
            $"SURFACE QUALITY {settings.ScratchDig} PER MIL-PRF-13830B OVER THE CLEAR APERTURE.",
            $"WEDGE: EDGE THICKNESS DIFFERENCE {F(d.EdgeThicknessDifference, 4)} MAX (BEAM DEVIATION {F(d.DeviationArcmin, 2)}' MAX). " +
            "SURFACE TILT AS TABLE, REFERRED TO DATUM AXIS OF THE OUTSIDE DIAMETER.",
            $"PROTECTIVE BEVEL {ChamferText(settings.Chamfer(d.Diameter)).ToUpperInvariant()} FACE WIDTH X 45°.",
            $"COATING: SURFACE 1 {Coat(d.Front)}; SURFACE 2 {Coat(d.Back)}.",
            $"EDGE THICKNESS {F(d.EdgeThickness, 2)} REF.",
        };
        notes.AddRange(AsphereNotes(d, settings, decentres: true).Select(n => n.ToUpperInvariant()));
        foreach (var (s, n) in new[] { (d.Front, 1), (d.Back, 2) })
            if (s.SymmetricIrregularityFringes is double rsi)
                notes.Add($"SURFACE {n}: ROTATIONALLY SYMMETRIC IRREGULARITY {G(rsi)} FR MAX, PART OF THE IRREGULARITY (ISO 10110-5 C).");
        notes.AddRange(CementNotes(d).Select(n => n.ToUpperInvariant()));
        if (d.MountTolerances.Count > 0)
            notes.Add("IN ITS MOUNT (ASSEMBLY DRAWING): " + string.Join(", ", d.MountTolerances).ToUpperInvariant() + ".");
        // The user's own text as written: upper-casing would turn nm into NM and Ravg into RAVG.
        notes.AddRange(d.CoatingNotes.Select(c => $"COATING {c.Name.ToUpperInvariant()}: {c.Spec}"));
        notes.AddRange(d.Notes);

        sheet.Text(x, top - 2, "NOTES:", 2.5, bold: true);
        double ny = top - 8.0;
        for (int k = 0; k < notes.Count; k++)
        {
            var lines = Wrap(notes[k], width - 8, size);
            if (ny - 3.6 * (lines.Count - 1) < 56)              // above the title block
            {
                sheet.Warnings.Add($"{notes.Count - k} note(s) did not fit on the sheet, from \"{Clip(notes[k])}\"");
                break;
            }
            sheet.Text(x, ny, $"{k + 1}.", size);
            foreach (var line in lines)
            {
                sheet.Text(x + 6, ny, line, size);
                ny -= 3.6;
            }
            ny -= 0.8;
        }
    }

    /// <summary>The text broken at spaces into lines no wider than <paramref name="width"/> mm.</summary>
    internal static List<string> Wrap(string text, double width, double height)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' '))
        {
            string trial = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && Helvetica.Width(trial, height) > width)
            {
                lines.Add(line);
                line = word;
            }
            else line = trial;
        }
        if (line.Length > 0) lines.Add(line);
        return lines;
    }

    private static IEnumerable<string> AsphereNotes(ElementDrawing d, DrawingSettings settings, bool decentres)
    {
        foreach (var (s, n) in new[] { (d.Front, 1), (d.Back, 2) })
        {
            if (!s.IsAspheric) continue;
            var terms = s.Aspheric.Select((a, i) => (a, power: 2 * i + 2)).Where(t => t.a != 0.0)
                                  .Select(t => $"A{t.power} = {t.a.ToString("0.######E+00", C)}");
            yield return $"Surface {n} aspheric: z = (h²/R)/(1 + √(1 − (1+k)h²/R²)) + Σ A_i h^i, k = {F(s.Conic, 6)}{ConicPlusMinus(s)}" +
                         (terms.Any() ? ", " + string.Join(", ", terms) : "") + ".";
            if (decentres && s.DecentreMm is double dc) yield return $"Surface {n} decenter {F(dc, 3)} max to the datum axis.";
        }
        if (d.Front.SagTable.Count > 0 || d.Back.SagTable.Count > 0)
            yield return "Sag tables: z from the vertex along the axis, positive to the right. ±Δz and ±Δs are the most the " +
                         "surface's power and irregularity tolerances move the sag and tilt the surface at that height.";
        if (settings.AsphereSlopeMrad is double slope)
            foreach (var (s, n) in new[] { (d.Front, 1), (d.Back, 2) })
                if (s.IsAspheric)
                    yield return $"Surface {n} slope deviation max {slope.ToString("0.####", C)} mrad" +
                                 (settings.AsphereSlopeSamplingMm is double g ? $" over a sampling length of {g.ToString("0.###", C)} mm" : "") + " (ISO 10110-5).";
    }

    /// <summary>
    /// Each aspheric surface's sag table, set against the right of the view's space - one above the
    /// other if both fit, else side by side. Returns the width they take, gap included; 0 for none.
    /// </summary>
    private static double SagTables(Sheet sheet, ElementDrawing d, double right, double top, double bottom)
    {
        var tables = new[] { (d.Front, 1), (d.Back, 2) }.Where(t => t.Item1.SagTable.Count > 0).ToList();
        if (tables.Count == 0) return 0.0;
        const double row = 3.2, size = 2.0, width = 60.0, gap = 6.0;
        double[] cols = { 1.5, 11.5, 26.0, 42.0 };
        double Height(SurfaceSpec s) => (s.SagTable.Count + 2) * row + 3.0;
        bool stacked = tables.Sum(t => Height(t.Item1)) + gap * (tables.Count - 1) <= top - bottom;
        double x = right - width, y = top;
        foreach (var (s, n) in tables)
        {
            sheet.Text(x + cols[0], y - row, $"Surface {n} sag", size, bold: true);
            sheet.Text(x + cols[0], y - 2 * row, "h", size, bold: true);
            sheet.Text(x + cols[1], y - 2 * row, "z", size, bold: true);
            sheet.Text(x + cols[2], y - 2 * row, "±Δz µm", size, bold: true);
            sheet.Text(x + cols[3], y - 2 * row, "±Δs mrad", size, bold: true);
            sheet.Line(new Pt(x, y - 2 * row - 1.2), new Pt(x + width, y - 2 * row - 1.2), Pen.Thin, Layers.Tables);
            for (int i = 0; i < s.SagTable.Count; i++)
            {
                var r = s.SagTable[i];
                double ry = y - (i + 3) * row - 1.0;
                sheet.Text(x + cols[0], ry, F(r.H, 2), size);
                sheet.Text(x + cols[1], ry, F(r.Z, 4), size);
                sheet.Text(x + cols[2], ry, F(r.DeltaZUm, 2), size);
                sheet.Text(x + cols[3], ry, F(r.DeltaSlopeMrad, 3), size);
            }
            double h = Height(s);
            sheet.Rect(x, y - h, width, h, Pen.Thin, Layers.Tables);
            if (stacked) y -= h + gap;
            else x -= width + gap;
        }
        return stacked ? width + gap : tables.Count * (width + gap);
    }

    private static IEnumerable<string> CementNotes(ElementDrawing d)
    {
        foreach (var (s, n) in new[] { (d.Front, 1), (d.Back, 2) })
            if (s.LandFrom is double from)
                yield return $"Surface {n} has a flat land from Ø{F(from, 2)} to the edge.";
        if (d.Front.Cemented) yield return "Surface 1 is cemented to the preceding element: radius to match its mating surface.";
        if (d.Back.Cemented) yield return "Surface 2 is cemented to the following element: radius to match its mating surface.";
    }

    // --- Title block ---------------------------------------------------------------------

    private static void TitleBlock(Sheet sheet, ElementDrawing d, DrawingSettings settings, double x, double y, double scale)
    {
        double w = 120, rowH = 6;
        string date = settings.Date ?? DateTime.Today.ToString("yyyy-MM-dd", C);
        var rows = new (string Label, string Value)[]
        {
            ("TITLE", d.Title),
            ("DRAWING", d.Number),
            ("MATERIAL", d.Material.Name),
            ("SCALE", ScaleText(scale) + "   UNITS mm   SHEET 1/1"),
            ("STANDARD", settings.Standard == DrawingStandard.Iso10110 ? "ISO 10110" : "ASME Y14.18 / MIL-PRF-13830B"),
            ("PROJECT", settings.Project),
            ("DRAWN", $"{settings.DrawnBy}   {date}".Trim()),
        };
        if (d.Description.Length > 0) rows = rows.Take(1).Append(("DESCRIPTION", d.Description)).Concat(rows.Skip(1)).ToArray();
        if (settings.Company.Length > 0) rows = rows.Append(("COMPANY", settings.Company)).ToArray();
        for (int i = 0; i < rows.Length; i++)
        {
            double ry = y + i * rowH;
            sheet.Rect(x, ry, w, rowH, Pen.Thin, Layers.Tables);
            var (label, value) = rows[rows.Length - 1 - i];
            sheet.Text(x + 1.5, ry + 1.8, label, 1.8);
            bool bold = i == rows.Length - 1;
            var (text, size) = Fit(value, w - 22 - 1.5, 2.5, 1.8, bold);
            if (text != value) sheet.Warnings.Add($"title block {label.ToLowerInvariant()} \"{value}\" is too long for its cell: shortened to \"{text}\"");
            sheet.Text(x + 22, ry + 1.6, text, size, bold: bold);
        }
        sheet.Line(new Pt(x + 20, y), new Pt(x + 20, y + rows.Length * rowH), Pen.Thin, Layers.Tables);
    }

    /// <summary>
    /// The text at <paramref name="size"/>, or smaller down to <paramref name="least"/>, to fit
    /// <paramref name="width"/> mm; failing that at the least size, cut short with "...".
    /// </summary>
    internal static (string Text, double Size) Fit(string text, double width, double size, double least, bool bold = false)
    {
        for (double h = size; h >= least - 1e-9; h -= 0.1)
            if (Helvetica.Width(text, h, bold) <= width) return (text, h);
        string cut = text;
        while (cut.Length > 0 && Helvetica.Width(cut + "...", least, bold) > width) cut = cut[..^1];
        return (cut.TrimEnd() + "...", least);
    }

    private static string F(double v, int decimals) => v.ToString("F" + decimals, C);

    /// <summary>" ±0.15" after a radius whose power is stated as a radius tolerance, else nothing.</summary>
    private static string RadiusPlusMinus(SurfaceSpec s) => s.RadiusTolerance is double t ? $" ±{t.ToString("0.#####", C)}" : "";

    /// <summary>" ±0.01" after a conic constant that is toleranced, " +0.02/-0.01" for a biased range, else nothing.</summary>
    private static string ConicPlusMinus(SurfaceSpec s)
    {
        if (s.ConicTolerance is not { } range) return "";
        var (min, max) = range;
        string K(double v) => Math.Abs(v).ToString("0.######", C);
        return Math.Abs(min + max) < 1e-12 ? $" ±{K(max)}" : $" {(max >= 0 ? "+" : "-")}{K(max)}/{(min <= 0 ? "-" : "+")}{K(min)}";
    }

    /// <summary>A count of fringes or minutes, as short as it can be: 25, 2.5, 0.25.</summary>
    private static string G(double v) => v.ToString("0.##", C);

    /// <summary>An upper and a lower deviation, each with its own sign: ±0.100, +0.080/-0.020, -0.020/-0.080.</summary>
    private static string PlusMinus(double plus, double minus) =>
        Math.Abs(plus + minus) < 1e-12 && plus >= 0.0
            ? $"±{F(plus, 3)}"
            : $"{(plus >= 0 ? "+" : "-")}{F(Math.Abs(plus), 3)}/{(minus <= 0 ? "-" : "+")}{F(Math.Abs(minus), 3)}";
}
