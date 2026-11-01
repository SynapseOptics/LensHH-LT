namespace LensHH.Tolerancing.Drawings;

/// <summary>A point on the sheet, in millimetres from its lower left corner, y up.</summary>
public readonly record struct Pt(double X, double Y)
{
    public static Pt operator +(Pt a, Pt b) => new(a.X + b.X, a.Y + b.Y);
    public static Pt operator -(Pt a, Pt b) => new(a.X - b.X, a.Y - b.Y);
    public static Pt operator *(double s, Pt a) => new(s * a.X, s * a.Y);
}

/// <summary>The kinds of line a drawing uses (ISO 128-2): visible outlines thick, the rest thin.</summary>
public enum Pen
{
    /// <summary>Visible outline, 0.5 mm.</summary>
    Outline,

    /// <summary>Dimension, extension and leader lines, hatching, table rules: 0.25 mm.</summary>
    Thin,

    /// <summary>Centre lines and the optical axis: thin, long dash and dot.</summary>
    Center,

    /// <summary>The sheet's frame: 0.7 mm.</summary>
    Frame,
}

public enum Align { Left, Center, Right }

public abstract record Mark(string Layer);

/// <summary>Straight segments through the points; closed and filled if asked (an arrowhead).</summary>
public sealed record Polyline(IReadOnlyList<Pt> Points, Pen Pen, string Layer, bool Closed = false, bool Filled = false) : Mark(Layer);

/// <summary>One line of text, its baseline at <see cref="At"/>, height in millimetres.</summary>
public sealed record Label(Pt At, double Height, string Text, Align Align, string Layer, bool Bold = false) : Mark(Layer);

/// <summary>
/// One drawing sheet: everything on it as lines and text, in millimetres. The writers turn it into
/// PDF, DXF or SVG; each draws the same marks, so the three files are the same drawing.
/// </summary>
public sealed class Sheet
{
    public Sheet(string name, double width, double height)
    {
        Name = name;
        Width = width;
        Height = height;
    }

    /// <summary>ISO A4 landscape.</summary>
    public static Sheet A4(string name) => new(name, 297.0, 210.0);

    /// <summary>ANSI A (letter) landscape, 11 × 8.5 in.</summary>
    public static Sheet AnsiA(string name) => new(name, 279.4, 215.9);

    public string Name { get; }
    public double Width { get; }
    public double Height { get; }
    public List<Mark> Marks { get; } = new();

    /// <summary>What the layout could not fit on the sheet.</summary>
    public List<string> Warnings { get; } = new();

    public void Line(Pt a, Pt b, Pen pen = Pen.Thin, string layer = Layers.Dimensions) =>
        Marks.Add(new Polyline(new[] { a, b }, pen, layer));

    public void Lines(IReadOnlyList<Pt> points, Pen pen, string layer, bool closed = false) =>
        Marks.Add(new Polyline(points, pen, layer, closed));

    public void Rect(double x, double y, double w, double h, Pen pen = Pen.Thin, string layer = Layers.Tables) =>
        Marks.Add(new Polyline(new[] { new Pt(x, y), new Pt(x + w, y), new Pt(x + w, y + h), new Pt(x, y + h) }, pen, layer, Closed: true));

    public void Text(double x, double y, string text, double height = 2.5, Align align = Align.Left,
                     string layer = Layers.Text, bool bold = false) =>
        Marks.Add(new Label(new Pt(x, y), height, text, align, layer, bold));

    /// <summary>A filled arrowhead with its tip at <paramref name="tip"/>, pointing along <paramref name="direction"/>.</summary>
    public void Arrow(Pt tip, Pt direction, double length = 2.5, double halfWidth = 0.6)
    {
        double norm = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        var u = (1.0 / norm) * direction;
        var n = new Pt(-u.Y, u.X);
        var back = tip - length * u;
        Marks.Add(new Polyline(new[] { tip, back + halfWidth * n, back - halfWidth * n }, Pen.Thin, Layers.Dimensions,
                               Closed: true, Filled: true));
    }

    /// <summary>
    /// A dimension between two points: the dimension line offset from them by <paramref name="offset"/>
    /// (perpendicular, to the left of a → b), extension lines, arrows inside, the text centred above.
    /// </summary>
    public void Dimension(Pt a, Pt b, double offset, string text, double height = 2.5)
    {
        var d = b - a;
        double len = Math.Sqrt(d.X * d.X + d.Y * d.Y);
        var u = (1.0 / len) * d;
        var n = new Pt(-u.Y, u.X);
        var o = Math.Sign(offset) * n;                // away from the part
        var a2 = a + offset * n;
        var b2 = b + offset * n;
        const double overshoot = 1.5;                 // of the extension lines past the dimension line
        Line(a + 1.0 * o, a2 + overshoot * o);
        Line(b + 1.0 * o, b2 + overshoot * o);
        Line(a2, b2);
        if (len >= 9.0)
        {
            Arrow(a2, a2 - b2);
            Arrow(b2, b2 - a2);
        }
        else
        {
            // Too short for arrows inside: they point in from outside, ISO 129-1's way.
            Arrow(a2, u);
            Arrow(b2, -1.0 * u);
            Line(a2 - 5.0 * u, a2);
            Line(b2, b2 + 5.0 * u);
        }
        var mid = 0.5 * (a2 + b2);
        if (Math.Abs(u.X) >= Math.Abs(u.Y))
        {
            // Text wider than the dimension reaches past its extension lines: clear their overshoot.
            double gap = Helvetica.Width(text, height) > len - 1.0 ? overshoot + 1.0 : 1.0;
            Text(mid.X, o.Y > 0 ? mid.Y + gap : mid.Y - gap - height, text, height, Align.Center);
        }
        else
            Text(o.X > 0 ? mid.X + 1.5 : mid.X - 1.5, mid.Y - height / 2.0, text, height, o.X > 0 ? Align.Left : Align.Right);
    }
}

/// <summary>The layers of a drawing, kept in the DXF so a vendor's CAD can switch them.</summary>
public static class Layers
{
    public const string Outline = "OUTLINE";
    public const string Center = "CENTER";
    public const string Dimensions = "DIMENSIONS";
    public const string Text = "TEXT";
    public const string Tables = "TABLES";
    public const string Frame = "FRAME";

    public static readonly string[] All = { Outline, Center, Dimensions, Text, Tables, Frame };
}

/// <summary>
/// Helvetica's and Helvetica-Bold's advance widths, in thousandths of an em, from Adobe's AFM files
/// for the standard fonts the PDF names: for placing centred and right-aligned text, and wrapping
/// notes, so that a line of capitals - which run 556 to 944 - ends where it is meant to.
/// </summary>
internal static class Helvetica
{
    /// <summary>The width of the text at a capital height of <paramref name="height"/>: Helvetica's capitals are 0.718 of its em.</summary>
    public static double Width(string text, double height, bool bold = false) =>
        text.Sum(c => Advance(c, bold)) * height / 0.718 / 1000.0;

    private static readonly int[] Upper = { 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778, 667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611 };
    private static readonly int[] Lower = { 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556, 556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500 };
    private static readonly int[] UpperBold = { 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778, 667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611 };
    private static readonly int[] LowerBold = { 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611, 611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500 };

    private static double Advance(char c, bool bold) => c switch
    {
        >= 'A' and <= 'Z' => (bold ? UpperBold : Upper)[c - 'A'],
        >= 'a' and <= 'z' => (bold ? LowerBold : Lower)[c - 'a'],
        >= '0' and <= '9' or 'µ' or '#' or '$' or '_' => 556,
        ' ' or '.' or ',' or '/' or '\\' => 278,
        ':' or ';' or '!' or '[' or ']' or '|' => bold ? 333 : 278,
        '(' or ')' or '-' => 333,
        '\'' => bold ? 238 : 191,
        '"' => bold ? 474 : 355,
        '±' or '+' or '=' or '<' or '>' or '×' => 584,
        '°' => 400,
        'Ø' => 778,
        '%' => 889,
        '&' => bold ? 722 : 667,
        '*' => 389,
        _ => 600,
    };
}
