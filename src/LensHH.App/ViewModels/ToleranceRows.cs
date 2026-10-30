using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using LensHH.Tolerancing;

namespace LensHH.App.ViewModels;

/// <summary>
/// One row of the Tolerancing window's tolerance table: a code, its surfaces, its range, how its
/// errors spread and, for an element tilt, its pivot. A row is a line of the .tol format, so the
/// table reads and writes the same files the CLI and MCP take.
/// </summary>
public partial class ToleranceRowViewModel : ObservableObject
{
    /// <summary>The tolerance codes, as the table's drop-down lists them.</summary>
    public static IReadOnlyList<string> Codes { get; } = new[]
    {
        "TFRN", "TIRR", "TRSI", "TRAD", "TTHI", "TIND", "TABB", "TCON",
        "TSDX", "TSDY", "TSTX", "TSTY", "TEDX", "TEDY", "TETX", "TETY",
    };

    /// <summary>What each code is, in its units.</summary>
    public static string Describe(string code) => code switch
    {
        "TFRN" => "Power (fringes)",
        "TIRR" => "Irregularity (fringes)",
        "TRSI" => "Symmetric irregularity (fringes)",
        "TRAD" => "Radius (mm)",
        "TTHI" => "Thickness after the surface (mm)",
        "TIND" => "Index (nd) of the glass after it",
        "TABB" => "Abbe number of the glass after it",
        "TCON" => "Conic constant",
        "TSDX" => "Surface decentre x (mm)",
        "TSDY" => "Surface decentre y (mm)",
        "TSTX" => "Surface tilt x, wedge (deg)",
        "TSTY" => "Surface tilt y, wedge (deg)",
        "TEDX" => "Element decentre x (mm)",
        "TEDY" => "Element decentre y (mm)",
        "TETX" => "Element tilt x (deg)",
        "TETY" => "Element tilt y (deg)",
        _ => "",
    };

    /// <summary>How the errors spread, as the drop-down lists it; the first is the run's own setting.</summary>
    public static IReadOnlyList<string> Spreads { get; } = new[] { "Run default", "Uniform", "Normal ±2σ", "Normal ±3σ", "End points", "Normal, other σ" };

    private static readonly string[] SpreadText = { "", "uniform", "normal", "normal:3", "endpoint" };

    /// <summary>A normal cut at other than 2 or 3 σ, as read from a file: "normal:2.5". Kept while "Normal, other σ" is chosen.</summary>
    private string _otherNormal = "normal:2.5";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description), nameof(HasPivot))] private string _code = "TFRN";
    [ObservableProperty] private string _surfaces = "1";
    [ObservableProperty] private string _min = "-1";
    [ObservableProperty] private string _max = "1";
    [ObservableProperty] private int _spreadIndex;
    [ObservableProperty] private string _pivot = "";

    public string Description => Describe(Code);

    /// <summary>Only an element tilt turns about a pivot.</summary>
    public bool HasPivot => Code is "TETX" or "TETY";

    /// <summary>The row as a line of a tolerance set.</summary>
    public string ToLine()
    {
        var line = $"{Code} {Surfaces.Trim()} {Min.Trim()} {Max.Trim()}";
        if (SpreadIndex > 0 && SpreadIndex < SpreadText.Length) line += " " + SpreadText[SpreadIndex];
        else if (SpreadIndex == SpreadText.Length) line += " " + _otherNormal;
        if (HasPivot && Pivot.Trim().Length > 0) line += " pivot:" + Pivot.Trim();
        return line;
    }

    public static ToleranceRowViewModel From(ToleranceItem item)
    {
        var c = CultureInfo.InvariantCulture;
        int spread = item.Distribution switch
        {
            null => 0,
            UniformDistribution => 1,
            EndPointDistribution => 4,
            TruncatedNormalDistribution n => Math.Abs(n.Sigmas - 2.0) < 1e-9 ? 2 : Math.Abs(n.Sigmas - 3.0) < 1e-9 ? 3 : 5,
            _ => 0,
        };
        return new ToleranceRowViewModel
        {
            _otherNormal = item.Distribution is TruncatedNormalDistribution other
                ? "normal:" + other.Sigmas.ToString("R", c) : "normal:2.5",
            Code = item.Tolerance.Code,
            Surfaces = ToleranceSpec.Surfaces(item.Tolerance),
            Min = item.Min.ToString("G6", c),
            Max = item.Max.ToString("G6", c),
            SpreadIndex = spread,
            Pivot = ToleranceSpec.Pivot(item.Tolerance) is double p ? p.ToString("G6", c) : "",
        };
    }
}

/// <summary>
/// One row of the compensator table: a thickness (COMP) or an element's decentre or tilt in its cell
/// (CEDX, CEDY, CETX, CETY), and how far it may move either way, blank for freely.
/// </summary>
public partial class CompensatorRowViewModel : ObservableObject
{
    public static IReadOnlyList<string> Codes { get; } = new[] { "COMP", "CEDX", "CEDY", "CETX", "CETY" };

    public static string Describe(string code) => code switch
    {
        "COMP" => "Thickness after the surface (mm)",
        "CEDX" => "Element decentre x in its cell (mm)",
        "CEDY" => "Element decentre y in its cell (mm)",
        "CETX" => "Element tilt x in its cell (deg)",
        "CETY" => "Element tilt y in its cell (deg)",
        _ => "",
    };

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Description))] private string _code = "COMP";
    [ObservableProperty] private string _surfaces = "1";
    [ObservableProperty] private string _travel = "";

    public string Description => Describe(Code);

    public string ToLine() => $"{Code} {Surfaces.Trim()}{(Travel.Trim().Length > 0 ? " " + Travel.Trim() : "")}";

    public static CompensatorRowViewModel From(Adjustment a) => new()
    {
        Code = a.Code,
        Surfaces = a.Surfaces,
        Travel = a.Travel is double t ? t.ToString("G6", CultureInfo.InvariantCulture) : "",
    };
}
