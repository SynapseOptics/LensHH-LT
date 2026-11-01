using System.Globalization;
using System.Text;
using LensHH.Core.Glass;
using LensHH.Core.Models;

namespace LensHH.Tolerancing.Drawings;

/// <summary>The files a drawing run writes: one PDF of every sheet, and an SVG and a DXF per element.</summary>
[Flags]
public enum DrawingFormats
{
    Pdf = 1,
    Svg = 2,
    Dxf = 4,
    All = Pdf | Svg | Dxf,
}

/// <summary>The drawings of a lens's elements, their sheets, the mirrors left undrawn, and the files written.</summary>
public sealed record DrawingRunResult(IReadOnlyList<ElementDrawing> Drawings, IReadOnlyList<Sheet> Sheets,
                                      IReadOnlyList<int> Mirrors, DrawingSettings Settings)
{
    public IReadOnlyList<string> Files { get; init; } = Array.Empty<string>();
}

/// <summary>
/// The one way in to element drawings for every front end: the lens, its tolerance set as text and
/// the drawing settings in; the drawings, their sheets and their files out.
/// </summary>
public static class DrawingRun
{
    /// <summary>The name of the PDF that holds every sheet.</summary>
    public const string PdfName = "drawings.pdf";

    /// <summary>The settings in a JSON file; the defaults for no file.</summary>
    public static DrawingSettings ReadSettings(string? path) =>
        string.IsNullOrWhiteSpace(path) ? new DrawingSettings() : DrawingSettings.FromJson(File.ReadAllText(path));

    /// <summary>"pdf", "svg", "dxf", "all", or several joined by commas or '+': "pdf,dxf".</summary>
    public static DrawingFormats ParseFormat(string text)
    {
        DrawingFormats formats = 0;
        foreach (var part in text.ToLowerInvariant().Split(new[] { ',', '+', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            formats |= part switch
            {
                "pdf" => DrawingFormats.Pdf,
                "svg" => DrawingFormats.Svg,
                "dxf" => DrawingFormats.Dxf,
                "all" => DrawingFormats.All,
                _ => throw new ArgumentException($"no drawing format {part}: pdf, svg, dxf or all"),
            };
        return formats == 0 ? DrawingFormats.Pdf : formats;
    }

    /// <summary>"iso" (ISO 10110) or "asme" (US practice; "mil" and "ansi" too).</summary>
    public static DrawingStandard ParseStandard(string text) => text.Trim().ToLowerInvariant() switch
    {
        "iso" or "iso10110" => DrawingStandard.Iso10110,
        "asme" or "mil" or "ansi" or "us" => DrawingStandard.Asme,
        var s => throw new ArgumentException($"no drawing standard {s}: iso or asme"),
    };

    /// <summary>
    /// The drawings and their sheets. The set's compensators that move an element in its cell
    /// (CEDX, CEDY, CETX, CETY) are the adjustments made at assembly, so that element is drawn as
    /// aligned there. A settings file with no project takes the lens's title.
    /// </summary>
    public static DrawingRunResult Draw(OpticalSystem system, GlassCatalogManager glass, string tolerances, DrawingSettings settings,
                                        double testWavelengthUm = PowerFringes.HeNe)
    {
        var lens = new NominalLens(system, glass, FieldSymmetry.File);
        var items = ToleranceSpec.ParseSet(tolerances, testWavelengthUm);
        var adjustments = ToleranceSpec.ParseCompensators(tolerances);
        if (settings.Project.Length == 0 && !string.IsNullOrWhiteSpace(system.Title)) settings = settings with { Project = system.Title };
        var drawings = ElementDrawings.Build(lens, items, settings, testWavelengthUm, adjustments);
        var sheets = drawings.Select(d => Layout.Draw(d, settings)).ToList();
        var mirrors = Enumerable.Range(1, lens.BackFocusSurface).Where(s => lens.System.Surfaces[s].IsMirror).ToList();
        return new DrawingRunResult(drawings, sheets, mirrors, settings);
    }

    /// <summary>
    /// The drawings, written to <paramref name="folder"/>: <see cref="PdfName"/> with every sheet, and
    /// each element's SVG and DXF named by its drawing number.
    /// </summary>
    public static DrawingRunResult Write(OpticalSystem system, GlassCatalogManager glass, string tolerances, DrawingSettings settings,
                                         string folder, DrawingFormats formats = DrawingFormats.Pdf,
                                         double testWavelengthUm = PowerFringes.HeNe)
    {
        if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("drawings need a folder to be written to");
        var result = Draw(system, glass, tolerances, settings, testWavelengthUm);
        var files = new List<string>();
        if (result.Sheets.Count > 0)
        {
            Directory.CreateDirectory(folder);
            if (formats.HasFlag(DrawingFormats.Pdf))
            {
                string pdf = Path.Combine(folder, PdfName);
                PdfWriter.Write(pdf, result.Sheets);
                files.Add(pdf);
            }
            for (int i = 0; i < result.Sheets.Count; i++)
            {
                string stem = Path.Combine(folder, FileName(result.Drawings[i].Number));
                if (formats.HasFlag(DrawingFormats.Svg))
                {
                    SvgWriter.Write(stem + ".svg", result.Sheets[i]);
                    files.Add(stem + ".svg");
                }
                if (formats.HasFlag(DrawingFormats.Dxf))
                {
                    DxfWriter.Write(stem + ".dxf", result.Sheets[i]);
                    files.Add(stem + ".dxf");
                }
            }
        }
        return result with { Files = files };
    }

    /// <summary>A drawing number as a file name: anything a file name cannot hold made '_'.</summary>
    public static string FileName(string number) =>
        string.Concat(number.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == '/' || c == '\\' ? '_' : c));

    /// <summary>One line per drawing - number, title, glass, centre thickness, diameter, edge - with its warnings; the mirrors not drawn; the files.</summary>
    public static string Report(DrawingRunResult r)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        string standard = r.Settings.Standard == DrawingStandard.Iso10110 ? "ISO 10110" : "ASME Y14.18";
        sb.AppendLine(r.Drawings.Count == 0 ? "No element drawings: the lens has no glass." : $"{r.Drawings.Count} element drawing{(r.Drawings.Count == 1 ? "" : "s")} ({standard})");
        for (int i = 0; i < r.Drawings.Count; i++)
        {
            var d = r.Drawings[i];
            sb.AppendLine(string.Create(c, $"  {d.Number}  {d.Title,-22} {d.Material.Name,-10} CT {d.Thickness:F3}  Ø{d.Diameter:F2}  edge {d.EdgeThickness:F2}"));
            foreach (var w in d.Warnings.Concat(r.Sheets[i].Warnings)) sb.AppendLine($"      warning: {w}");
        }
        if (r.Mirrors.Count > 0)
            sb.AppendLine($"  mirror{(r.Mirrors.Count > 1 ? "s" : "")} at surface{(r.Mirrors.Count > 1 ? "s" : "")} {string.Join(", ", r.Mirrors)} not drawn: a lens file gives no substrate");
        if (r.Files.Count > 0)
        {
            sb.AppendLine($"  Wrote {r.Files.Count} file{(r.Files.Count == 1 ? "" : "s")} in {Path.GetDirectoryName(r.Files[0])}:");
            foreach (var f in r.Files) sb.AppendLine($"    {Path.GetFileName(f)}");
        }
        return sb.ToString();
    }
}
