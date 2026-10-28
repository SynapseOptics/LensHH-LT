using System.Globalization;

namespace LensHH.Tolerancing;

/// <summary>
/// A tolerance written as text, <c>CODE:surface:amount</c> - <c>TFRN:3:10</c>, <c>TTHI:4:0.2</c> -
/// or with a range of surfaces for an element, <c>TETX:1-2:0.05</c>.
/// </summary>
public static class ToleranceSpec
{
    public static (Tolerance Tolerance, double Amount) Parse(string text, double testWavelengthUm = PowerFringes.HeNe)
    {
        var parts = text.Split(':');
        if (parts.Length != 3) throw new FormatException($"'{text}' is not CODE:surface:amount");
        string code = parts[0].Trim().ToUpperInvariant();
        var range = parts[1].Split('-');
        int first = int.Parse(range[0], CultureInfo.InvariantCulture);
        int last = range.Length > 1 ? int.Parse(range[1], CultureInfo.InvariantCulture) : first;
        double amount = double.Parse(parts[2], CultureInfo.InvariantCulture);
        if (range.Length > 2 || last < first) throw new FormatException($"'{parts[1]}' is not a surface or a range of them");
        bool element = code.StartsWith("TE", StringComparison.Ordinal);
        if (!element && last != first) throw new FormatException($"{code} takes one surface, not {parts[1]}");

        Tolerance tolerance = code switch
        {
            "TFRN" => new PowerFringes(first, testWavelengthUm),
            "TIRR" => new SurfaceIrregularity(first, testWavelengthUm),
            "TRSI" => new SymmetricIrregularity(first, testWavelengthUm),
            "TRAD" => new RadiusChange(first),
            "TTHI" => new ThicknessChange(first),
            "TIND" => new IndexChange(first),
            "TABB" => new AbbeChange(first),
            "TCON" => new ConicChange(first),
            "TSDX" => new SurfaceDecentre(first, Axis.X),
            "TSDY" => new SurfaceDecentre(first, Axis.Y),
            "TSTX" => new SurfaceTilt(first, Axis.X),
            "TSTY" => new SurfaceTilt(first, Axis.Y),
            "TEDX" => new ElementDecentre(first, last, Axis.X),
            "TEDY" => new ElementDecentre(first, last, Axis.Y),
            "TETX" => new ElementTilt(first, last, Axis.X),
            "TETY" => new ElementTilt(first, last, Axis.Y),
            _ => throw new FormatException($"no tolerance {code}"),
        };
        return (tolerance, amount);
    }

    /// <summary>
    /// A tolerance set from text, one per line: <c>CODE surfaces ±t</c> or <c>CODE surfaces min max</c>,
    /// then optionally a distribution - <c>uniform</c>, <c>endpoint</c>, <c>normal</c> or <c>normal:k</c>
    /// for ±t = kσ - and, on an element tilt, <c>pivot:d</c>, the pivot d lens units along the axis
    /// from the first vertex. <c>#</c> starts a comment. <c>TTHI 4 -0.02 0.08 normal:3</c> is a
    /// thickness made on the high side, normally distributed; <c>TETX 1-4 0.05 pivot:6.2</c> is a
    /// group tilted about a point 6.2 mm behind its first vertex.
    /// </summary>
    public static List<ToleranceItem> ParseSet(string text, double testWavelengthUm = PowerFringes.HeNe)
    {
        var items = new List<ToleranceItem>();
        int lineNumber = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNumber++;
            var line = raw.Split('#')[0].Trim();
            if (line.Length == 0) continue;
            var f = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            try
            {
                if (f.Length < 3 || f.Length > 6) throw new FormatException("expected CODE surfaces ±t, or CODE surfaces min max [distribution] [pivot:d]");
                var (tolerance, first) = Parse($"{f[0]}:{f[1]}:{f[2]}", testWavelengthUm);
                bool range = f.Length >= 4 && double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out _);
                var item = range
                    ? new ToleranceItem(tolerance, first, double.Parse(f[3], CultureInfo.InvariantCulture))
                    : ToleranceItem.Symmetric(tolerance, first);
                if (!(item.Max > item.Min)) throw new FormatException("the range is empty");
                bool pivot = false;
                foreach (var option in f.Skip(range ? 4 : 3))
                {
                    if (option.StartsWith("pivot:", StringComparison.OrdinalIgnoreCase))
                    {
                        if (item.Tolerance is not ElementTilt tilt) throw new FormatException($"only an element tilt (TETX, TETY) takes {option}");
                        if (pivot) throw new FormatException("two pivots");
                        if (!double.TryParse(option[6..], NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || !double.IsFinite(d))
                            throw new FormatException($"'{option}': the pivot is a distance in lens units, pivot:6.2");
                        item = item with { Tolerance = tilt with { PivotOffset = d } };
                        pivot = true;
                    }
                    else if (item.Distribution != null) throw new FormatException("too many fields");
                    else item = item with { Distribution = ParseDistribution(option) };
                }
                items.Add(item);
            }
            catch (FormatException e)
            {
                throw new FormatException($"line {lineNumber}: '{line}': {e.Message}", e);
            }
        }
        return items;
    }

    /// <summary>A tolerance set as <see cref="ParseSet"/> reads it.</summary>
    public static string FormatSet(IEnumerable<ToleranceItem> items) =>
        string.Concat(items.Select(i =>
        {
            var c = CultureInfo.InvariantCulture;
            string range = i.Center == 0.0 ? Short(i.Max, up: false) : $"{Short(i.Min, up: true)} {Short(i.Max, up: false)}";
            return $"{i.Tolerance.Code} {Surfaces(i.Tolerance)} {range}{(i.Distribution == null ? "" : " " + FormatDistribution(i.Distribution))}" +
                   (Pivot(i.Tolerance) is double p ? $" pivot:{p.ToString("R", c)}" : "") + "\n";
        }));

    /// <summary>An element tilt's pivot, from its first vertex along the axis, when it is not the vertex itself.</summary>
    public static double? Pivot(Tolerance t) => t is ElementTilt { PivotOffset: not 0.0 } e ? e.PivotOffset : null;

    /// <summary>
    /// The value in six significant figures - an Abbe tolerance of 0.8 % of ν is 0.463696, not
    /// 0.4636964888078359 - when that does not widen the range: a maximum may only come down, a
    /// minimum only go up. Otherwise in full.
    /// </summary>
    private static string Short(double value, bool up)
    {
        var c = CultureInfo.InvariantCulture;
        string text = value.ToString("G6", c);
        double back = double.Parse(text, c);
        return (up ? back >= value : back <= value) ? text : value.ToString("R", c);
    }

    public static Distribution ParseDistribution(string text)
    {
        var parts = text.ToLowerInvariant().Split(':');
        try
        {
            return parts[0] switch
            {
                "uniform" when parts.Length == 1 => Distribution.Uniform,
                "endpoint" when parts.Length == 1 => Distribution.EndPoint,
                "normal" when parts.Length <= 2 => Distribution.Normal(parts.Length > 1 ? double.Parse(parts[1], CultureInfo.InvariantCulture) : 2.0),
                _ => throw new FormatException($"no distribution {text}: uniform, endpoint or normal[:k]"),
            };
        }
        catch (ArgumentOutOfRangeException e)
        {
            throw new FormatException($"{text}: {e.Message.Split('(')[0].Trim()}", e);
        }
    }

    public static string FormatDistribution(Distribution d) => d switch
    {
        UniformDistribution => "uniform",
        EndPointDistribution => "endpoint",
        TruncatedNormalDistribution n => $"normal:{n.Sigmas.ToString("R", CultureInfo.InvariantCulture)}",
        _ => throw new ArgumentException($"no text for {d}"),
    };

    /// <summary>The surfaces a tolerance names, as text: <c>3</c>, or <c>1-2</c> for an element.</summary>
    public static string Surfaces(Tolerance t) => t switch
    {
        ElementDecentre e => $"{e.Surface}-{e.Last}",
        ElementTilt e => $"{e.Surface}-{e.Last}",
        _ => t.Surface.ToString(CultureInfo.InvariantCulture),
    };
}
