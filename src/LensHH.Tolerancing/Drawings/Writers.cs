using System.Globalization;
using System.Text;

namespace LensHH.Tolerancing.Drawings;

/// <summary>Line widths and dash pattern in millimetres, as ISO 128-2's 0.25/0.5 group has them.</summary>
internal static class Pens
{
    public static double Width(Pen pen) => pen switch
    {
        Pen.Outline => 0.5,
        Pen.Frame => 0.7,
        _ => 0.25,
    };

    /// <summary>The centre line's long dash, gap, dot, gap.</summary>
    public static readonly double[] CenterDash = { 6.0, 1.5, 1.0, 1.5 };
}

/// <summary>
/// PDF 1.4, one page per sheet, written by hand: lines, filled arrowheads and Helvetica text in
/// WinAnsi encoding - the standard fonts need nothing embedded. ±, °, Ø and µ are in WinAnsi;
/// anything else is spelled out.
/// </summary>
public static class PdfWriter
{
    private const double Pt = 72.0 / 25.4;

    public static void Write(string path, IReadOnlyList<Sheet> sheets) => File.WriteAllBytes(path, ToBytes(sheets));

    public static byte[] ToBytes(IReadOnlyList<Sheet> sheets)
    {
        if (sheets.Count == 0) throw new ArgumentException("no sheets");
        var objects = new List<byte[]>();
        int Reserve() { objects.Add(Array.Empty<byte>()); return objects.Count; }
        void Set(int id, string body) => objects[id - 1] = Latin1(body);

        int catalog = Reserve(), pages = Reserve(), font = Reserve(), bold = Reserve();
        var pageIds = new List<int>();
        foreach (var sheet in sheets)
        {
            int page = Reserve(), content = Reserve();
            pageIds.Add(page);
            byte[] stream = Latin1(Content(sheet));
            Set(page, $"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 {N(sheet.Width * Pt)} {N(sheet.Height * Pt)}] " +
                      $"/Resources << /Font << /F1 {font} 0 R /F2 {bold} 0 R >> >> /Contents {content} 0 R >>");
            objects[content - 1] = Latin1($"<< /Length {stream.Length} >>\nstream\n").Concat(stream).Concat(Latin1("\nendstream")).ToArray();
        }
        Set(catalog, $"<< /Type /Catalog /Pages {pages} 0 R >>");
        Set(pages, $"<< /Type /Pages /Kids [{string.Join(" ", pageIds.Select(p => $"{p} 0 R"))}] /Count {pageIds.Count} >>");
        Set(font, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Set(bold, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");

        using var output = new MemoryStream();
        void Put(string s) { var b = Latin1(s); output.Write(b, 0, b.Length); }
        Put("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");
        var offsets = new long[objects.Count];
        for (int i = 0; i < objects.Count; i++)
        {
            offsets[i] = output.Position;
            Put($"{i + 1} 0 obj\n");
            output.Write(objects[i], 0, objects[i].Length);
            Put("\nendobj\n");
        }
        long xref = output.Position;
        Put($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (long o in offsets) Put($"{o:D10} 00000 n \n");
        Put($"trailer\n<< /Size {objects.Count + 1} /Root {catalog} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    private static string Content(Sheet sheet)
    {
        var s = new StringBuilder();
        s.Append("1 J 1 j\n");
        foreach (var mark in sheet.Marks)
        {
            switch (mark)
            {
                case Polyline p:
                    s.Append(N(Pens.Width(p.Pen) * Pt)).Append(" w ");
                    s.Append(p.Pen == Pen.Center ? $"[{string.Join(" ", Pens.CenterDash.Select(d => N(d * Pt)))}] 0 d " : "[] 0 d ");
                    for (int i = 0; i < p.Points.Count; i++)
                        s.Append(N(p.Points[i].X * Pt)).Append(' ').Append(N(p.Points[i].Y * Pt)).Append(i == 0 ? " m " : " l ");
                    s.Append(p.Filled ? "h f\n" : p.Closed ? "h S\n" : "S\n");
                    break;
                case Label t:
                    string text = Sanitize(t.Text);
                    double width = Helvetica.Width(text, t.Height, t.Bold);
                    double x = t.Align switch { Align.Center => t.At.X - width / 2.0, Align.Right => t.At.X - width, _ => t.At.X };
                    s.Append($"BT /{(t.Bold ? "F2" : "F1")} {N(t.Height * Pt / 0.718)} Tf {N(x * Pt)} {N(t.At.Y * Pt)} Td (");
                    foreach (char c in text) s.Append(c is '(' or ')' or '\\' ? "\\" + c : c.ToString());
                    s.Append(") Tj ET\n");
                    break;
            }
        }
        return s.ToString();
    }

    /// <summary>The text in characters WinAnsi has.</summary>
    internal static string Sanitize(string text)
    {
        var s = new StringBuilder();
        foreach (char c in text)
        {
            s.Append(c switch
            {
                '≤' => "<=",
                '≥' => ">=",
                'λ' => "lambda",
                'ν' => "v",
                '⌀' => "Ø",
                'σ' => "s",
                'Δ' => "d",
                '√' => "sqrt",
                '−' => "-",
                'Σ' => "sum ",
                '–' or '—' => "-",
                _ when c < 0x7F || (c >= 0xA0 && c <= 0xFF) => c.ToString(),
                _ => "?",
            });
        }
        return s.ToString();
    }

    private static byte[] Latin1(string s) => s.Select(c => c <= 0xFF ? (byte)c : (byte)'?').ToArray();

    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>
/// DXF R12 (AC1009), the version every CAD program reads: LINE, POLYLINE, SOLID for arrowheads and
/// TEXT, on the drawing's layers, in millimetres. ±, ° and Ø are AutoCAD's %%p, %%d and %%c.
/// </summary>
public static class DxfWriter
{
    public static void Write(string path, Sheet sheet) => File.WriteAllText(path, ToText(sheet), Encoding.ASCII);

    public static string ToText(Sheet sheet)
    {
        var s = new StringBuilder();
        void G(int code, object value) => s.Append(code).Append('\n').Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append('\n');
        void P(int code, double v) => G(code, v.ToString("0.######", CultureInfo.InvariantCulture));

        G(0, "SECTION"); G(2, "HEADER");
        G(9, "$ACADVER"); G(1, "AC1009");
        G(9, "$INSUNITS"); G(70, 4);
        G(9, "$EXTMIN"); P(10, 0); P(20, 0);
        G(9, "$EXTMAX"); P(10, sheet.Width); P(20, sheet.Height);
        G(0, "ENDSEC");

        G(0, "SECTION"); G(2, "TABLES");
        G(0, "TABLE"); G(2, "LTYPE"); G(70, 2);
        G(0, "LTYPE"); G(2, "CONTINUOUS"); G(70, 0); G(3, "Solid line"); G(72, 65); G(73, 0); P(40, 0);
        G(0, "LTYPE"); G(2, "CENTER"); G(70, 0); G(3, "Center ____ . ____"); G(72, 65); G(73, 4);
        P(40, Pens.CenterDash.Sum());
        for (int i = 0; i < 4; i++) P(49, i % 2 == 0 ? Pens.CenterDash[i] : -Pens.CenterDash[i]);
        G(0, "ENDTAB");
        G(0, "TABLE"); G(2, "LAYER"); G(70, Layers.All.Length);
        foreach (var layer in Layers.All)
        {
            G(0, "LAYER"); G(2, layer); G(70, 0);
            G(62, layer switch { Layers.Center => 1, Layers.Dimensions => 3, Layers.Tables => 8, _ => 7 });
            G(6, layer == Layers.Center ? "CENTER" : "CONTINUOUS");
        }
        G(0, "ENDTAB");
        G(0, "ENDSEC");

        G(0, "SECTION"); G(2, "ENTITIES");
        foreach (var mark in sheet.Marks)
        {
            switch (mark)
            {
                case Polyline { Filled: true } f:
                    G(0, "SOLID"); G(8, f.Layer);
                    var q = f.Points;
                    // SOLID's corners go 1, 2, 4, 3; a triangle repeats its last.
                    var corners = q.Count == 3 ? new[] { q[0], q[1], q[2], q[2] } : new[] { q[0], q[1], q[3], q[2] };
                    for (int i = 0; i < 4; i++) { P(10 + i, corners[i].X); P(20 + i, corners[i].Y); P(30 + i, 0); }
                    break;
                case Polyline { Points.Count: 2, Closed: false } l:
                    G(0, "LINE"); G(8, l.Layer);
                    P(10, l.Points[0].X); P(20, l.Points[0].Y); P(30, 0);
                    P(11, l.Points[1].X); P(21, l.Points[1].Y); P(31, 0);
                    break;
                case Polyline p:
                    G(0, "POLYLINE"); G(8, p.Layer); G(66, 1); P(10, 0); P(20, 0); P(30, 0); G(70, p.Closed ? 1 : 0);
                    foreach (var v in p.Points) { G(0, "VERTEX"); G(8, p.Layer); P(10, v.X); P(20, v.Y); P(30, 0); }
                    G(0, "SEQEND"); G(8, p.Layer);
                    break;
                case Label t:
                    G(0, "TEXT"); G(8, t.Layer);
                    P(10, t.At.X); P(20, t.At.Y); P(30, 0); P(40, t.Height); G(1, Escape(t.Text));
                    if (t.Align != Align.Left)
                    {
                        G(72, t.Align == Align.Center ? 1 : 2);
                        P(11, t.At.X); P(21, t.At.Y); P(31, 0);
                    }
                    break;
            }
        }
        G(0, "ENDSEC");
        G(0, "EOF");
        return s.ToString();
    }

    internal static string Escape(string text)
    {
        var s = new StringBuilder();
        foreach (char c in PdfWriter.Sanitize(text))
            s.Append(c switch { '±' => "%%p", '°' => "%%d", 'Ø' => "%%c", _ when c < 0x7F => c.ToString(), _ => "?" });
        return s.ToString();
    }
}

/// <summary>SVG, to look at a drawing in a browser: the sheet in millimetres, y turned down as SVG has it.</summary>
public static class SvgWriter
{
    public static void Write(string path, Sheet sheet) => File.WriteAllText(path, ToText(sheet), new UTF8Encoding(false));

    public static string ToText(Sheet sheet)
    {
        string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        string Y(double y) => N(sheet.Height - y);
        var s = new StringBuilder();
        s.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(sheet.Width)}mm\" height=\"{N(sheet.Height)}mm\" " +
                 $"viewBox=\"0 0 {N(sheet.Width)} {N(sheet.Height)}\">\n");
        s.Append($"<rect width=\"{N(sheet.Width)}\" height=\"{N(sheet.Height)}\" fill=\"white\"/>\n");
        foreach (var mark in sheet.Marks)
        {
            switch (mark)
            {
                case Polyline p:
                    string points = string.Join(" ", p.Points.Select(v => $"{N(v.X)},{Y(v.Y)}"));
                    string dash = p.Pen == Pen.Center ? $" stroke-dasharray=\"{string.Join(" ", Pens.CenterDash.Select(N))}\"" : "";
                    string fill = p.Filled ? "black" : "none";
                    s.Append($"<{(p.Closed || p.Filled ? "polygon" : "polyline")} points=\"{points}\" fill=\"{fill}\" stroke=\"black\" " +
                             $"stroke-width=\"{N(Pens.Width(p.Pen))}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"{dash}/>\n");
                    break;
                case Label t:
                    string anchor = t.Align switch { Align.Center => "middle", Align.Right => "end", _ => "start" };
                    s.Append($"<text x=\"{N(t.At.X)}\" y=\"{Y(t.At.Y)}\" font-family=\"Helvetica, Arial, sans-serif\" " +
                             $"font-size=\"{N(t.Height / 0.718)}\" text-anchor=\"{anchor}\"{(t.Bold ? " font-weight=\"bold\"" : "")}>" +
                             $"{System.Security.SecurityElement.Escape(t.Text)}</text>\n");
                    break;
            }
        }
        s.Append("</svg>\n");
        return s.ToString();
    }
}
