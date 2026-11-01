using System.Globalization;
using System.Text;
using System.Xml.Linq;
using LensHH.Core.Enums;
using LensHH.Core.Models;
using LensHH.Tolerancing.Drawings;
using Xunit;

namespace LensHH.Tolerancing.Tests;

/// <summary>The lenses the drawing tests draw, and a surface's sag as the drawings take it.</summary>
internal static class DrawingLenses
{
    public static NominalLens Triplet(Action<OpticalSystem>? edit = null) => Lenses.Nominal(Lenses.Triplet(), edit);
    public static NominalLens Doublet(Action<OpticalSystem>? edit = null) => Lenses.Nominal(Lenses.Doublet(), edit);

    /// <summary>The triplet with its first surface an even asphere.</summary>
    public static NominalLens Aspheric() => Triplet(s =>
    {
        s.Surfaces[1].Type = SurfaceType.EvenAsphere;
        s.Surfaces[1].Conic = -0.8;
        s.Surfaces[1].AsphericCoefficients[1] = -2e-5;
        s.Surfaces[1].AsphericCoefficients[2] = 1e-7;
    });

    public static double Sag(Surface s, double h) => Tolerance.Sag(s, h, s.Curvature);

    public static string Text(Sheet sheet) => string.Join("\n", sheet.Marks.OfType<Label>().Select(l => l.Text));
}

/// <summary>Each value on an element drawing is the one the lens and its tolerance set give.</summary>
public class ElementDrawingTests
{
    private static readonly DrawingSettings Settings = new() { Date = "2026-10-12" };

    [Fact]
    public void EveryPieceOfGlassGetsADrawingAndItsValuesComeFromTheBudget()
    {
        var lens = DrawingLenses.Triplet();
        var items = DefaultTolerances.Create(lens, ShopGrade.Precision);
        var drawings = ElementDrawings.Build(lens, items, Settings);

        Assert.Equal(new[] { 1, 3, 6 }, drawings.Select(d => d.First));
        var middle = drawings[1];
        var g = DefaultTolerances.For(ShopGrade.Precision);
        Assert.Equal("F2", middle.Material.Name);
        Assert.Equal("SCHOTT", middle.Material.Catalog);
        Assert.Equal(1.0, middle.Thickness);
        Assert.Equal((-g.ThicknessMm, g.ThicknessMm), (middle.ThicknessMinus, middle.ThicknessPlus));
        Assert.Equal(g.Fringes, middle.Front.PowerFringes, 12);
        Assert.Equal(g.SurfaceTiltDeg * 60.0, middle.Back.TiltArcmin, 12);
        Assert.Equal(g.Index, middle.Material.NdTolerance, 12);
        Assert.Equal(g.AbbePercent, middle.Material.VdTolerancePercent, 9);
        Assert.Equal(632.8, middle.TestWavelengthNm, 9);
        Assert.Empty(middle.Warnings);

        // A biconcave element: both surfaces concave on the part.
        Assert.Equal("CC", ElementDrawing.Shape(middle.Front, front: true));
        Assert.Equal("CC", ElementDrawing.Shape(middle.Back, front: false));
        Assert.Equal("CX", ElementDrawing.Shape(drawings[0].Front, front: true));
    }

    [Fact]
    public void TheEdgeIsTheClearApertureAndAMarginRoundedUpAndItsThicknessIsTheSagsDifference()
    {
        var lens = DrawingLenses.Triplet();
        var d = ElementDrawings.Build(lens, DefaultTolerances.Create(lens), Settings)[0];
        Assert.Equal(14.0, d.Front.ClearAperture, 12);                  // semi-diameter 7
        Assert.Equal(15.0, d.Diameter, 12);                             // 14 + 1, to the half millimetre
        var s = lens.System.Surfaces;
        Assert.Equal(2.5 - DrawingLenses.Sag(s[1], 7.5) + DrawingLenses.Sag(s[2], 7.5), d.EdgeThickness, 12);
        Assert.Contains(d.Warnings, w => w.Contains("edge thickness"));
    }

    [Fact]
    public void FringesCountedAtAnotherWavelengthAreRestatedAtTheDrawingsOne()
    {
        // The first power tolerance sets the drawing's wavelength. Another counted at 546.1 nm is a sag
        // error of 4 × 0.5461/2 µm, which is 4 × 0.5461/0.6328 fringes at 632.8 nm.
        var lens = DrawingLenses.Triplet();
        var items = new[]
        {
            ToleranceItem.Symmetric(new PowerFringes(1, 0.6328), 4.0),
            ToleranceItem.Symmetric(new PowerFringes(2, 0.5461), 4.0),
        };
        var d = ElementDrawings.Build(lens, items, Settings)[0];
        Assert.Equal(4.0, d.Front.PowerFringes, 12);
        Assert.Equal(4.0 * 0.5461 / 0.6328, d.Back.PowerFringes, 12);
    }

    [Fact]
    public void ToleranceSetsLeftOutAreFilledWithTheDefaultsAndSaidSo()
    {
        var lens = DrawingLenses.Triplet();
        var d = ElementDrawings.Build(lens, Array.Empty<ToleranceItem>(), Settings)[1];
        Assert.Equal(Settings.DefaultFringes, d.Front.PowerFringes);
        Assert.Equal(Settings.DefaultTiltArcmin, d.Front.TiltArcmin);
        Assert.Equal(Settings.DefaultIndex, d.Material.NdTolerance);
        Assert.Equal(9, d.Warnings.Count);      // power, irregularity and tilt of each surface, index, Abbe, thickness
    }

    [Fact]
    public void ABiasedThicknessIsDrawnAsItsTwoLimits()
    {
        var lens = DrawingLenses.Triplet();
        var items = new[] { new ToleranceItem(new ThicknessChange(3), -0.02, 0.08) };
        var d = ElementDrawings.Build(lens, items, Settings)[1];
        Assert.Equal((-0.02, 0.08), (d.ThicknessMinus, d.ThicknessPlus));
    }

    [Fact]
    public void ACementedDoubletIsTwoDrawingsTheirSharedSurfaceUncoatedAndTheirMountTolerancesTheGroups()
    {
        var lens = DrawingLenses.Doublet();
        var items = DefaultTolerances.Create(lens);
        var drawings = ElementDrawings.Build(lens, items, Settings with { Coating = "AR" });

        Assert.Equal(2, drawings.Count);
        var (crown, flint) = (drawings[0], drawings[1]);
        Assert.Equal(("N-BK7", "N-SF5"), (crown.Material.Name, flint.Material.Name));
        Assert.False(crown.Front.Cemented);
        Assert.True(crown.Back.Cemented);
        Assert.True(flint.Front.Cemented);
        Assert.Null(crown.Back.Coating);
        Assert.Equal("AR", crown.Front.Coating);
        Assert.Contains("CEMENTED", crown.Title);
        // The group 1-3 moves as one in its cell: both pieces carry its decentre and tilt.
        Assert.Equal(4, crown.MountTolerances.Count);
        Assert.All(flint.MountTolerances, m => Assert.Contains("group 1-3", m));
    }

    [Fact]
    public void CementedPartnersShareOneDiameterTheSmallestEitherCouldTake()
    {
        // The biconvex crown made 3 mm thick: at the clear aperture plus a millimetre its edge is under
        // half a millimetre, so it alone would be drawn smaller than the flint.
        var lens = DrawingLenses.Doublet(s => s.Surfaces[1].Thickness = 3.0);
        var alone = ElementDrawings.Build(lens, Array.Empty<ToleranceItem>(), Settings);
        Assert.Equal(alone[0].Diameter, alone[1].Diameter);
        var crown = alone[0];
        double aperture = Math.Max(crown.Front.ClearAperture, crown.Back.ClearAperture);
        Assert.True(crown.Diameter >= aperture);
        // The piece whose size changed says so; its partner, which set it, does not.
        Assert.Single(alone, d => d.Warnings.Any(w => w.Contains("to match its cemented partner")));

        // A singlet is left alone.
        var triplet = ElementDrawings.Build(DrawingLenses.Triplet(), Array.Empty<ToleranceItem>(), Settings);
        Assert.DoesNotContain(triplet, d => d.Warnings.Any(w => w.Contains("cemented partner")));
    }

    [Fact]
    public void TheWedgeOfTwoTiltedSurfacesIsTheirRootSumOfSquares()
    {
        var lens = DrawingLenses.Triplet();
        var items = new[]
        {
            ToleranceItem.Symmetric(new SurfaceTilt(3, Axis.X), 3.0 / 60.0),
            ToleranceItem.Symmetric(new SurfaceTilt(4, Axis.X), 4.0 / 60.0),
        };
        var d = ElementDrawings.Build(lens, items, Settings)[1];
        double wedge = 5.0 / 60.0 * Math.PI / 180.0;
        Assert.Equal(wedge, d.WedgeRadians, 14);
        Assert.Equal(d.Diameter * Math.Tan(wedge), d.EdgeThicknessDifference, 14);
        Assert.Equal((d.Material.Nd - 1.0) * 5.0, d.DeviationArcmin, 10);
    }

    [Fact]
    public void AnAsphereIsDrawnThroughItsSagAndItsCoefficientsAreNoted()
    {
        var lens = DrawingLenses.Triplet(s =>
        {
            s.Surfaces[1].Type = SurfaceType.EvenAsphere;
            s.Surfaces[1].Conic = -0.8;
            s.Surfaces[1].AsphericCoefficients[1] = -2e-5;     // the r⁴ term
        });
        var items = DefaultTolerances.Create(lens);
        var d = ElementDrawings.Build(lens, items, Settings)[0];
        Assert.True(d.Front.IsAspheric);
        Assert.NotNull(d.Front.DecentreMm);                   // the default set decentres aspheres
        var s = lens.System.Surfaces;
        Assert.Equal(2.5 - DrawingLenses.Sag(s[1], 7.5) + DrawingLenses.Sag(s[2], 7.5), d.EdgeThickness, 12);

        var text = DrawingLenses.Text(Layout.Draw(d, Settings));
        Assert.Contains("aspheric", text);
        Assert.Contains("k = -0.800000", text);
        Assert.Contains("A4 = -2E-05", text);
    }

    [Theory]
    [InlineData(-0.01, 0.01, "k = -0.800000 ±0.01")]
    [InlineData(-0.005, 0.02, "k = -0.800000 +0.02/-0.005")]
    public void AConicToleranceIsStatedWithTheConic(double min, double max, string stated)
    {
        var lens = DrawingLenses.Triplet(s => s.Surfaces[1].Conic = -0.8);
        var items = new[] { new ToleranceItem(new ConicChange(1), min, max), new ToleranceItem(new ConicChange(3), min, max) };
        var drawings = ElementDrawings.Build(lens, items, Settings);
        Assert.Equal((min, max), drawings[0].Front.ConicTolerance);

        foreach (var standard in new[] { DrawingStandard.Iso10110, DrawingStandard.Asme })
        {
            var text = string.Join(" ", Layout.Draw(drawings[0], Settings with { Standard = standard }).Marks.OfType<Label>().Select(l => l.Text));
            Assert.Contains(stated, text, StringComparison.OrdinalIgnoreCase);
        }

        // Surface 3 is a sphere: a conic tolerance there is analyzed but has nothing to state.
        Assert.Null(drawings[1].Front.ConicTolerance);
        Assert.Contains(drawings[1].Warnings, w => w.Contains("surface 3: a conic tolerance on a sphere"));
    }

    [Fact]
    public void AGroupTiltsPivotIsInItsMountNote()
    {
        var lens = DrawingLenses.Triplet();
        var items = ToleranceSpec.ParseSet("TETX 1-4 0.05 pivot:6.2\nTETY 1-4 0.05\n");
        var d = ElementDrawings.Build(lens, items, Settings)[0];
        Assert.Contains(d.MountTolerances, m => m.Contains("about the axis 6.2 mm after surface 1") && m.Contains("group 1-4"));
        Assert.Contains(d.MountTolerances, m => m.StartsWith("tilt") && !m.Contains("about"));
    }

    [Fact]
    public void SettingsReadBackFromTheirJson()
    {
        var s = new DrawingSettings { Standard = DrawingStandard.Asme, ScratchDig = "40-20", Coatings = new() { [2] = "BBAR" } };
        var back = DrawingSettings.FromJson(s.ToJson());
        Assert.Equal(DrawingStandard.Asme, back.Standard);
        Assert.Equal("40-20", back.ScratchDig);
        Assert.Equal("BBAR", back.Coatings[2]);
        Assert.Equal(DrawingStandard.Iso10110, DrawingSettings.FromJson("{ \"scratchDig\": \"20-10\", }").Standard);
    }

    private const string Custom = """
        {
          "numberPrefix": "DBL-",
          "coating": "AR1",
          "coatings": { "3": "V-coat" },
          "coatingSpecs": { "AR1": "Broadband AR 450-650 nm, Ravg ≤ 0.5 % per surface, MIL-C-675C" },
          "notes": [ "Clean per shop procedure 12." ],
          "elements": {
            "1": { "title": "CROWN, ACHROMAT", "number": "100-2045", "description": "Front of objective" },
            "DBL-02": { "notes": [ "Flint: handle with gloves." ] }
          }
        }
        """;

    [Fact]
    public void SettingsCanRenameAnElementDescribeItAndWriteOutACoatingInFull()
    {
        var lens = DrawingLenses.Doublet();
        var drawings = ElementDrawings.Build(lens, DefaultTolerances.Create(lens), DrawingSettings.FromJson(Custom) with { Date = "2026-10-12" });
        var (crown, flint) = (drawings[0], drawings[1]);

        Assert.Equal(("100-2045", "CROWN, ACHROMAT", "Front of objective"), (crown.Number, crown.Title, crown.Description));
        Assert.Equal("DBL-02", flint.Number);
        Assert.Contains("CEMENTED", flint.Title);                     // left as the drawing has it
        Assert.Equal(new[] { "Clean per shop procedure 12." }, crown.Notes);
        Assert.Equal(new[] { "Clean per shop procedure 12.", "Flint: handle with gloves." }, flint.Notes);
        // Only the crown's front is AR1; the flint's back is the named V-coat, which has no spec to write out.
        Assert.Equal(new[] { "AR1" }, crown.CoatingNotes.Select(c => c.Name));
        Assert.Empty(flint.CoatingNotes);
        Assert.Equal("V-coat", flint.Back.Coating);

        foreach (var standard in new[] { DrawingStandard.Iso10110, DrawingStandard.Asme })
        {
            var sheet = Layout.Draw(crown, new DrawingSettings { Standard = standard });
            var text = DrawingLenses.Text(sheet);
            Assert.Contains("100-2045", text);
            Assert.Contains("Front of objective", text);
            Assert.Contains("Ravg ≤ 0.5 % per surface", text);           // as written, not upper-cased
            Assert.Contains("Clean per shop procedure 12.", text);
            Assert.Empty(sheet.Warnings);
        }
    }

    [Fact]
    public void ARenamedElementIsTheOneItsCementedPartnerNames()
    {
        var lens = DrawingLenses.Doublet();
        var settings = Settings with { Elements = new() { ["1"] = new ElementOverride { Number = "100-2045" },
                                                          ["2"] = new ElementOverride { Number = "100-2046" } } };
        var drawings = ElementDrawings.Build(lens, DefaultTolerances.Create(lens), settings);
        Assert.Equal(new[] { "100-2045", "100-2046" }, drawings.Select(d => d.Number));
        Assert.DoesNotContain(drawings.SelectMany(d => d.Warnings), w => w.Contains("OPT-"));
    }

    [Fact]
    public void SettingsNamingNoElementOrRepeatingANumberAreRefused()
    {
        var lens = DrawingLenses.Doublet();
        var items = DefaultTolerances.Create(lens);
        var stray = Assert.Throws<ArgumentException>(() => ElementDrawings.Build(lens, items,
            Settings with { Elements = new() { ["4"] = new ElementOverride { Title = "X" } } }));
        Assert.Contains("1 (OPT-01)", stray.Message);
        Assert.Throws<ArgumentException>(() => ElementDrawings.Build(lens, items,
            Settings with { Elements = new() { ["1"] = new ElementOverride { Number = "OPT-02" } } }));
    }

    [Fact]
    public void NotesThatDoNotFitAreReportedNotDroppedSilently()
    {
        var lens = DrawingLenses.Doublet();
        var many = Enumerable.Range(1, 40).Select(i => $"Note {i}: a long line of shop instructions that takes room.").ToList();
        foreach (var standard in new[] { DrawingStandard.Iso10110, DrawingStandard.Asme })
        {
            var settings = Settings with { Standard = standard, Notes = many };
            var d = ElementDrawings.Build(lens, DefaultTolerances.Create(lens), settings)[0];
            Assert.Contains(Layout.Draw(d, settings).Warnings, w => w.Contains("did not fit"));
        }
    }
}

/// <summary>The sheets: everything on the sheet, and each writer's file well formed.</summary>
public class SheetTests
{
    public static IEnumerable<object[]> Sheets()
    {
        foreach (var standard in new[] { DrawingStandard.Iso10110, DrawingStandard.Asme })
            foreach (var lens in new[] { "triplet", "doublet" })
                yield return new object[] { standard, lens };
    }

    private static List<Sheet> Draw(DrawingStandard standard, string name)
    {
        var lens = name == "doublet" ? DrawingLenses.Doublet() : DrawingLenses.Triplet();
        var settings = new DrawingSettings { Standard = standard, Date = "2026-10-12", Project = "test" };
        return ElementDrawings.Build(lens, DefaultTolerances.Create(lens), settings).Select(d => Layout.Draw(d, settings)).ToList();
    }

    [Theory]
    [MemberData(nameof(Sheets))]
    public void EverythingIsInsideTheFrame(DrawingStandard standard, string lens)
    {
        foreach (var sheet in Draw(standard, lens))
        {
            foreach (var mark in sheet.Marks)
            {
                switch (mark)
                {
                    case Polyline p:
                        Assert.All(p.Points, q => { Assert.InRange(q.X, 10.0, sheet.Width - 10.0); Assert.InRange(q.Y, 10.0, sheet.Height - 10.0); });
                        break;
                    case Label t:
                        double width = Helvetica.Width(PdfWriter.Sanitize(t.Text), t.Height, t.Bold);
                        double left = t.Align switch { Align.Center => t.At.X - width / 2, Align.Right => t.At.X - width, _ => t.At.X };
                        Assert.InRange(left, 10.0, sheet.Width - 10.0);
                        Assert.InRange(left + width, 10.0, sheet.Width - 10.0);
                        Assert.InRange(t.At.Y, 10.0, sheet.Height - 10.0);
                        break;
                }
            }
        }
    }

    /// <summary>Text keeps 1 mm clear of the frame, measured with Helvetica's own widths: all-capital notes once ran into it.</summary>
    [Theory]
    [MemberData(nameof(Sheets))]
    public void TextKeepsClearOfTheFrame(DrawingStandard standard, string lens)
    {
        foreach (var sheet in Draw(standard, lens))
            foreach (var t in sheet.Marks.OfType<Label>())
            {
                var (left, right, _, _) = Box(t);
                Assert.True(left >= 11.0 && right <= sheet.Width - 11.0, $"{sheet.Name}: \"{t.Text}\" spans {left:F1} to {right:F1} mm");
            }
    }

    /// <summary>
    /// No dimension or extension line runs through text: a thickness wider written than the
    /// element is thick once had both extension lines through it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Sheets))]
    public void NoDimensionLineCrossesText(DrawingStandard standard, string lens)
    {
        foreach (var sheet in Draw(standard, lens))
        {
            var boxes = sheet.Marks.OfType<Label>().Select(t => (t, box: Box(t))).ToList();
            foreach (var line in sheet.Marks.OfType<Polyline>().Where(p => p.Layer == Layers.Dimensions && !p.Filled))
                for (int k = 0; k + 1 < line.Points.Count; k++)
                    foreach (var (t, (x0, x1, y0, y1)) in boxes)
                        Assert.False(Crosses(line.Points[k], line.Points[k + 1], x0, x1, y0, y1),
                                     $"{sheet.Name}: a dimension line crosses \"{t.Text}\"");
        }
    }

    /// <summary>A label's extent: left, right, baseline and capital height.</summary>
    private static (double Left, double Right, double Bottom, double Top) Box(Label t)
    {
        double width = Helvetica.Width(PdfWriter.Sanitize(t.Text), t.Height, t.Bold);
        double left = t.Align switch { Align.Center => t.At.X - width / 2, Align.Right => t.At.X - width, _ => t.At.X };
        return (left, left + width, t.At.Y, t.At.Y + t.Height);
    }

    /// <summary>Whether the segment a-b passes through the inside of the box (Liang-Barsky).</summary>
    private static bool Crosses(Pt a, Pt b, double x0, double x1, double y0, double y1)
    {
        double t0 = 0.0, t1 = 1.0, dx = b.X - a.X, dy = b.Y - a.Y;
        foreach (var (p, q) in new[] { (-dx, a.X - x0), (dx, x1 - a.X), (-dy, a.Y - y0), (dy, y1 - a.Y) })
        {
            if (p == 0.0) { if (q <= 0.0) return false; continue; }
            double r = q / p;
            if (p < 0.0) t0 = Math.Max(t0, r); else t1 = Math.Min(t1, r);
            if (t0 >= t1) return false;
        }
        return true;
    }

    [Fact]
    public void TheSheetSizesAreTheStandardsOwn()
    {
        var iso = Draw(DrawingStandard.Iso10110, "triplet")[0];
        Assert.Equal((297.0, 210.0), (iso.Width, iso.Height));
        var ansi = Draw(DrawingStandard.Asme, "triplet")[0];
        Assert.Equal(11.0 * 25.4, ansi.Width, 9);
        Assert.Equal(8.5 * 25.4, ansi.Height, 9);
    }

    [Fact]
    public void TheIsoTableCarriesTheCodedIndications()
    {
        var text = DrawingLenses.Text(Draw(DrawingStandard.Iso10110, "triplet")[1]);
        Assert.Contains("3/10(2)", text);         // commercial: 10 fringes of power, 2 of irregularity
        Assert.Contains("4/3.44'", text);         // 0.001 rad
        Assert.Contains("5/3x0.16", text);
        Assert.Contains("0/20", text);
        Assert.Contains("nd = 1.62004 ± 0.0020", text);
    }

    [Fact]
    public void TheUsNotesCarryScratchDigAndWedge()
    {
        var text = DrawingLenses.Text(Draw(DrawingStandard.Asme, "triplet")[1]);
        Assert.Contains("MIL-PRF-13830B", text);
        Assert.Contains("ASME Y14.18", text);
        Assert.Contains("EDGE THICKNESS DIFFERENCE", text);
        Assert.Contains("60-40", text);
    }

    [Theory]
    [InlineData(4.3, 4.0)]
    [InlineData(3.99, 2.5)]
    [InlineData(1.0, 1.0)]
    [InlineData(0.3, 0.25)]
    [InlineData(25.0, 10.0)]
    public void ViewsAreDrawnToAStandardScale(double most, double scale) => Assert.Equal(scale, Layout.StandardScale(most));

    [Fact]
    public void NotesWrapToTheirWidth()
    {
        var lines = Layout.Wrap("ONE TWO THREE FOUR FIVE SIX SEVEN EIGHT NINE TEN", 30.0, 2.5);
        Assert.True(lines.Count > 1);
        Assert.All(lines, l => Assert.True(Helvetica.Width(l, 2.5) <= 30.0));
        Assert.Equal("ONE TWO THREE FOUR FIVE SIX SEVEN EIGHT NINE TEN", string.Join(" ", lines));
    }

    [Fact]
    public void ThePdfsCrossReferenceTableFindsEveryObject()
    {
        var sheets = Draw(DrawingStandard.Iso10110, "triplet");
        byte[] pdf = PdfWriter.ToBytes(sheets);
        string text = Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);

        int startxref = int.Parse(text[(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0]);
        Assert.StartsWith("xref", text[startxref..]);
        var lines = text[startxref..].Split('\n');
        int count = int.Parse(lines[1].Split(' ')[1]);
        for (int id = 1; id < count; id++)
        {
            long offset = long.Parse(lines[2 + id][..10]);
            Assert.StartsWith($"{id} 0 obj", text[(int)offset..]);
        }
        Assert.Contains($"/Count {sheets.Count}", text);
        foreach (int length in System.Text.RegularExpressions.Regex.Matches(text, @"/Length (\d+) >>\nstream\n").Select(m => int.Parse(m.Groups[1].Value)))
            Assert.True(length > 0);
    }

    [Fact]
    public void PdfTextIsInWinAnsi()
    {
        Assert.Equal("vd ± 0.5 Ø 3°", PdfWriter.Sanitize("νd ± 0.5 ⌀ 3°"));
        Assert.Equal("lambda <= 1", PdfWriter.Sanitize("λ ≤ 1"));
    }

    [Fact]
    public void TheDxfIsR12WithEveryMarkAsAnEntity()
    {
        var sheet = Draw(DrawingStandard.Asme, "triplet")[0];
        var lines = DxfWriter.ToText(sheet).Split('\n');
        Assert.Equal(0, (lines.Length - 1) % 2);                 // group code, value; a last newline
        var pairs = Enumerable.Range(0, lines.Length / 2).Select(i => (lines[2 * i].Trim(), lines[2 * i + 1])).ToList();
        Assert.Contains(("1", "AC1009"), pairs);
        Assert.Equal(("0", "EOF"), pairs[^1]);
        int entities = pairs.Count(p => p.Item1 == "0" && p.Item2 is "LINE" or "POLYLINE" or "SOLID" or "TEXT");
        Assert.Equal(sheet.Marks.Count, entities);
        foreach (var layer in Layers.All) Assert.Contains(("2", layer), pairs);
        Assert.Equal("%%c15.00 %%p0.1", DxfWriter.Escape("Ø15.00 ±0.1"));
    }

    [Fact]
    public void TheSvgIsWellFormed()
    {
        var sheet = Draw(DrawingStandard.Iso10110, "triplet")[0];
        var svg = XDocument.Parse(SvgWriter.ToText(sheet));
        XNamespace ns = "http://www.w3.org/2000/svg";
        Assert.Equal(sheet.Marks.OfType<Label>().Count(), svg.Descendants(ns + "text").Count());
        Assert.Equal("0 0 297 210", svg.Root!.Attribute("viewBox")!.Value);
    }
}

/// <summary>A title block whose values may be long, and an element aligned at assembly.</summary>
public class TitleBlockAndAdjustmentTests
{
    private static readonly DrawingSettings Settings = new() { Date = "2026-10-15" };

    private static List<Label> Labels(Sheet sheet) => sheet.Marks.OfType<Label>().ToList();

    [Theory]
    [InlineData(DrawingStandard.Iso10110)]
    [InlineData(DrawingStandard.Asme)]
    public void ALongProjectShrinksToItsCellAndALongerOneIsCutShortAndSaidSo(DrawingStandard standard)
    {
        var lens = DrawingLenses.Triplet();
        var d = ElementDrawings.Build(lens, Array.Empty<ToleranceItem>(), Settings)[0];
        const double cell = 120 - 22 - 1.5;

        string longName = "PPP_TRIPLET_ANY_GLASS_UC_T1_AfterSplit_AfterAshpere_MultiStart";
        var sheet = Layout.Draw(d, Settings with { Standard = standard, Project = longName });
        var project = Labels(sheet).Single(l => l.Text == longName);
        Assert.True(project.Height < 2.5 && Helvetica.Width(project.Text, project.Height) <= cell);
        Assert.DoesNotContain(sheet.Warnings, w => w.StartsWith("title block"));
        foreach (var label in new[] { "TITLE", "DRAWING", "MATERIAL", "SCALE", "STANDARD", "PROJECT", "DRAWN" })
            Assert.Contains(Labels(sheet), l => l.Text == label);

        string longer = string.Concat(Enumerable.Repeat(longName + " ", 3)).Trim();
        sheet = Layout.Draw(d, Settings with { Standard = standard, Project = longer });
        var cut = Labels(sheet).Single(l => l.Text.EndsWith("..."));
        Assert.StartsWith(longName[..20], cut.Text);
        Assert.True(Helvetica.Width(cut.Text, cut.Height) <= cell);
        Assert.Contains(sheet.Warnings, w => w.StartsWith("title block project") && w.Contains("shortened"));

        // A short value keeps the usual size.
        Assert.Equal(2.5, Labels(Layout.Draw(d, Settings with { Standard = standard, Project = "Triplet" })).Single(l => l.Text == "Triplet").Height);
    }

    /// <summary>
    /// An element decentred to align the lens is drawn as set at assembly, not held to the mount
    /// tolerance the set gives it; its tilt, and a group's decentre around it, are still tolerances.
    /// </summary>
    [Fact]
    public void AnElementAlignedAtAssemblyIsDrawnSoAndKeepsItsOtherMountTolerances()
    {
        var lens = DrawingLenses.Triplet();
        var items = ToleranceSpec.ParseSet("TEDX 3-4 0.2\nTEDY 3-4 0.2\nTETX 3-4 0.05\nTEDX 1-4 0.03\n");
        var adjust = new Adjustment[]
        {
            new ElementDecentreAdjustment(3, 4, Axis.X) { Travel = 0.3 },
            new ElementDecentreAdjustment(3, 4, Axis.Y) { Travel = 0.3 },
        };

        var plain = ElementDrawings.Build(lens, items, Settings)[1].MountTolerances;
        Assert.Contains("decenter x ±0.2 mm", plain);

        var middle = ElementDrawings.Build(lens, items, Settings, adjustments: adjust)[1].MountTolerances;
        Assert.DoesNotContain(middle, m => m.StartsWith("decenter x ±0.2") || m.StartsWith("decenter y ±0.2"));
        Assert.Contains("decenter x and y set at assembly to align the lens, within ±0.3 mm", middle);
        Assert.Contains(middle, m => m.StartsWith("tilt x ±0.05°"));
        Assert.Contains(middle, m => m.StartsWith("decenter x ±0.03 mm") && m.Contains("group 1-4"));

        // The front element is not adjusted; without a travel the note gives none.
        var front = ElementDrawings.Build(lens, items, Settings, adjustments: adjust)[0].MountTolerances;
        Assert.DoesNotContain(front, m => m.Contains("set at assembly"));
        var untravelled = ElementDrawings.Build(lens, items, Settings,
            adjustments: new Adjustment[] { new ElementTiltAdjustment(3, 4, Axis.X), new ElementTiltAdjustment(3, 4, Axis.Y) })[1].MountTolerances;
        Assert.Contains("tilt x and y set at assembly to align the lens", untravelled);
        Assert.DoesNotContain(untravelled, m => m.StartsWith("tilt x ±"));
    }
}

/// <summary>
/// The form indications: irregularity and its rotationally invariant part, power past what a test
/// plate reads as a radius tolerance, and an asphere's sag table (ISO 10110-5, -12).
/// </summary>
public class DrawingFormTests
{
    private const double Lambda = PowerFringes.HeNe * 1e-3;       // mm

    private static readonly DrawingSettings Settings = new() { Date = "2026-10-14" };

    [Fact]
    public void ADrawingStatesTheAllocatedIrregularity()
    {
        var lens = DrawingLenses.Triplet();
        var items = ToleranceSpec.ParseSet("TFRN 3 4\nTIRR 3 0.6\nTFRN 4 4\n");
        var d = ElementDrawings.Build(lens, items, Settings)[1];
        Assert.Equal(0.6, d.Front.IrregularityFringes, 12);
        Assert.Equal(Settings.DefaultIrregularity, d.Back.IrregularityFringes);
        Assert.Contains(d.Warnings, w => w.Contains("surface 4: irregularity not toleranced"));
        Assert.Null(d.Front.RadiusTolerance);
    }

    [Fact]
    public void PowerPastWhatATestPlateReadsBecomesARadiusTolerance()
    {
        var lens = DrawingLenses.Triplet();
        var items = ToleranceSpec.ParseSet("TFRN 1 30\nTIRR 1 2\nTFRN 2 8\nTIRR 2 2\n");
        var d = ElementDrawings.Build(lens, items, Settings)[0];

        // Surface 1: 30 fringes over its aperture, a sag of 30 λ/2 at its edge - as a radius, about 2R²Δs/h².
        double r = lens.System.Surfaces[1].Radius, h = Tolerance.SemiDiameter(lens.Perturb(), 1);
        double approx = 2.0 * r * r * (30.0 * Lambda / 2.0) / (h * h);
        Assert.NotNull(d.Front.RadiusTolerance);
        Assert.InRange(d.Front.RadiusTolerance!.Value, 0.7 * approx, approx);
        Assert.Null(d.Back.RadiusTolerance);                        // 8 fringes stay fringes

        var iso = DrawingLenses.Text(Layout.Draw(d, Settings));
        Assert.Contains($"±{d.Front.RadiusTolerance.Value.ToString(CultureInfo.InvariantCulture)} CX", iso);
        Assert.Contains("3/-(2)", iso);
        Assert.Contains("3/8(2)", iso);

        var asme = Settings with { Standard = DrawingStandard.Asme };
        var us = ElementDrawings.Build(lens, items, asme)[0];
        var cells = Layout.Draw(us, asme).Marks.OfType<Label>().Select(l => l.Text).ToList();
        Assert.Contains("-", cells);                                // no power for surface 1
        Assert.Contains("8 FR", cells);
    }

    [Fact]
    public void TheSymmetricIrregularityIsDrawnAsCAndWarnedWhenMoreThanTheIrregularity()
    {
        var lens = DrawingLenses.Triplet();
        var items = ToleranceSpec.ParseSet("TFRN 3 4\nTIRR 3 1\nTRSI 3 0.5\nTFRN 4 4\nTIRR 4 0.5\nTRSI 4 1\n");
        var d = ElementDrawings.Build(lens, items, Settings)[1];
        Assert.Equal(0.5, d.Front.SymmetricIrregularityFringes);
        Assert.Null(ElementDrawings.Build(lens, items.Take(2).ToList(), Settings)[1].Front.SymmetricIrregularityFringes);

        string Text(DrawingStandard standard) =>
            string.Join(" ", Layout.Draw(d, Settings with { Standard = standard }).Marks.OfType<Label>().Select(l => l.Text));
        Assert.Contains("3/4(1/0.5)", Text(DrawingStandard.Iso10110));
        Assert.Contains("3/4(0.5/1)", Text(DrawingStandard.Iso10110));
        Assert.Contains("ROTATIONALLY SYMMETRIC IRREGULARITY 0.5 FR MAX", Text(DrawingStandard.Asme));
        Assert.Contains(d.Warnings, w => w.StartsWith("surface 4: rotationally invariant irregularity 1 fringes is more than the irregularity"));
        Assert.DoesNotContain(d.Warnings, w => w.StartsWith("surface 3: rotationally"));
    }

    /// <summary>ISO 10110-6 writes an asphere's centering as 4/σ(L); the US form keeps its note.</summary>
    [Fact]
    public void AnAspheresDecentreIsLIn4SigmaL()
    {
        var lens = DrawingLenses.Aspheric();
        var items = ToleranceSpec.ParseSet("TSTX 1 0.05\nTSDX 1 0.01\n");
        var d = ElementDrawings.Build(lens, items, Settings)[0];
        string Text(DrawingStandard standard) =>
            string.Join(" ", Layout.Draw(d, Settings with { Standard = standard }).Marks.OfType<Label>().Select(l => l.Text));

        var iso = Text(DrawingStandard.Iso10110);
        Assert.Contains("4/3'(0.010)", iso);
        Assert.Contains("4/3'", iso.Replace("4/3'(0.010)", ""));            // the spherical back surface: no L
        Assert.Contains("SURFACE 1 DECENTER 0.010 MAX TO THE DATUM AXIS", Text(DrawingStandard.Asme));
    }

    /// <summary>
    /// Power past what a test plate reads is drawn as a radius tolerance, rounded down; the table
    /// follows the radius as drawn, so it allows no more than the radius callout does.
    /// </summary>
    [Fact]
    public void WherePowerIsARadiusToleranceTheTableFollowsTheRadius()
    {
        var lens = DrawingLenses.Aspheric();
        var items = ToleranceSpec.ParseSet("TFRN 1 30\nTIRR 1 1\n");
        var front = ElementDrawings.Build(lens, items, Settings)[0].Front;
        double dr = front.RadiusTolerance!.Value;

        var nominal = lens.System.Surfaces[1];
        double h = front.SagTable[^1].H, power = 0.0;
        foreach (int sign in new[] { -1, 1 })
        {
            var p = lens.Perturb();
            p.System.Surfaces[1].Curvature = 1.0 / (nominal.Radius + sign * dr);
            power = Math.Max(power, Math.Abs(DrawingLenses.Sag(p.System.Surfaces[1], h) - DrawingLenses.Sag(nominal, h)));
        }
        Assert.Equal((power + Lambda / 8.0) * 1000.0, front.SagTable[^1].DeltaZUm, 1e-9);
        Assert.True(power < 30.0 * Lambda / 2.0, $"{power} against {30.0 * Lambda / 2.0}");     // rounded down
    }

    [Theory]
    [InlineData(7.0, new[] { 0.0, 1, 2, 3, 4, 5, 6, 7 })]
    [InlineData(4.5, new[] { 0.0, 0.5, 1, 1.5, 2, 2.5, 3, 3.5, 4, 4.5 })]
    [InlineData(10.0, new[] { 0.0, 2, 4, 6, 8, 10 })]
    [InlineData(0.9, new[] { 0.0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9 })]
    public void TheTablesHeightsAreRoundStepsToTheClearAperture(double sd, double[] heights)
    {
        var got = ElementDrawings.Heights(sd);
        Assert.Equal(heights.Length, got.Count);
        for (int i = 0; i < heights.Length; i++) Assert.Equal(heights[i], got[i], 12);
        Assert.True(got.Count <= ElementDrawings.SagRows);
    }

    [Fact]
    public void AnAsphereHasASagTableAndASphereNone()
    {
        var lens = DrawingLenses.Aspheric();
        var d = ElementDrawings.Build(lens, DefaultTolerances.Create(lens), Settings)[0];
        var table = d.Front.SagTable;
        Assert.Equal(ElementDrawings.Heights(7.0), table.Select(r => r.H));
        Assert.All(table, r => Assert.Equal(DrawingLenses.Sag(lens.System.Surfaces[1], r.H), r.Z, 15));
        Assert.Equal(0.0, table[0].DeltaZUm);
        Assert.Empty(d.Back.SagTable);

        var text = DrawingLenses.Text(Layout.Draw(d, Settings));
        Assert.Contains("Surface 1 sag", text);
        Assert.Contains("±Δz µm", text);
        Assert.Contains("Sag tables:", text);
        Assert.DoesNotContain("Surface 2 sag", text);
    }

    /// <summary>
    /// Δz is what the analysis traced: of every sign of power, irregularity and its rotationally
    /// invariant part, each at its full tolerance, the most the sag moves at each height - along x
    /// or y, where the astigmatic half is largest - is Δz, neither more nor less. The traced surface
    /// is the engine's own, its Zernike departure included.
    /// </summary>
    [Fact]
    public void DeltaZIsTheMostTheTracedFormErrorsMoveTheSag()
    {
        var lens = DrawingLenses.Aspheric();
        var items = ToleranceSpec.ParseSet("TFRN 1 3\nTIRR 1 1\nTRSI 1 0.5\n");
        var table = ElementDrawings.Build(lens, items, Settings)[0].Front.SagTable;
        var nominal = lens.System.Surfaces[1];

        var most = new double[table.Count];
        var steepest = new double[table.Count];
        const double e = 1e-4;
        foreach (int a in new[] { -1, 1 })
            foreach (int b in new[] { -1, 1 })
                foreach (int c in new[] { -1, 1 })
                {
                    var p = lens.Perturb();
                    new PowerFringes(1).Apply(p, 3.0 * a);
                    new SurfaceIrregularity(1).Apply(p, 1.0 * b);
                    new SymmetricIrregularity(1).Apply(p, 0.5 * c);
                    var s = p.System.Surfaces[1];
                    double D(double x, double y)
                    {
                        double r = Math.Sqrt(x * x + y * y);
                        return DrawingLenses.Sag(s, r) + ZernikeDeparture.Sag(s, x, y) - DrawingLenses.Sag(nominal, r);
                    }
                    for (int i = 0; i < table.Count; i++)
                    {
                        double h = table[i].H;
                        foreach (var (x, y) in new[] { (h, 0.0), (0.0, h) })
                        {
                            most[i] = Math.Max(most[i], Math.Abs(D(x, y)) * 1000.0);
                            double gx = (D(x + e, y) - D(x - e, y)) / (2 * e), gy = (D(x, y + e) - D(x, y - e)) / (2 * e);
                            steepest[i] = Math.Max(steepest[i], Math.Sqrt(gx * gx + gy * gy) * 1000.0);
                        }
                    }
                }
        for (int i = 0; i < table.Count; i++) Assert.Equal(most[i], table[i].DeltaZUm, 1e-9);
        // Δs likewise: the steepest the same surfaces tilt, by their gradient on the x and y axes.
        for (int i = 0; i < table.Count; i++) Assert.Equal(steepest[i], table[i].DeltaSlopeMrad, 1e-6);

        // At the edge the zone has no sag: power's half wave a fringe, and the astigmatic half's λ/8.
        Assert.Equal((3.0 * Lambda / 2.0 + Lambda / 8.0) * 1000.0, table[^1].DeltaZUm, 1e-6);
    }

    [Theory]
    [InlineData(0.0153, 0.015)]
    [InlineData(0.149, 0.12)]
    [InlineData(2.2, 2.0)]
    [InlineData(7.9, 6.0)]
    [InlineData(1.0, 1.0)]
    public void ToleranceIsStatedRoundedDownToAPreferredNumber(double value, double stated) =>
        Assert.Equal(stated, Allocator.RoundDown(value));
}

/// <summary>
/// The settings' own value for anything a drawing states, per element: it takes the place of the
/// computed one on the drawing and in each file, and what follows from it follows.
/// </summary>
public class ElementOverrideTests
{
    private static readonly DrawingSettings Settings = new() { Date = "2026-10-31" };

    private static readonly ElementOverride Middle = new()
    {
        Glass = "F2 or equivalent",
        NdTolerance = 0.0005,
        VdTolerancePercent = 0.3,
        ThicknessMinus = -0.02,
        ThicknessPlus = 0.03,
        Diameter = 12.0,
        DiameterMinus = -0.02,
        Front = new SurfaceOverride { PowerFringes = 3.0, IrregularityFringes = 0.5, TiltArcmin = 1.5, Coating = "BBAR", ClearAperture = 9.0 },
        Back = new SurfaceOverride { RadiusTolerance = 0.1, SymmetricIrregularityFringes = 0.25, DecentreMm = 0.01, Coating = "none" },
    };

    [Fact]
    public void AnOverrideReplacesTheComputedValueOnTheDrawing()
    {
        var lens = DrawingLenses.Triplet();
        var items = DefaultTolerances.Create(lens);
        var computed = ElementDrawings.Build(lens, items, Settings with { Coating = "AR" })[1];
        var d = ElementDrawings.Build(lens, items, Settings with { Coating = "AR", Elements = new() { ["3"] = Middle } })[1];

        Assert.Equal(("F2 or equivalent", (string?)null), (d.Material.Name, d.Material.Catalog));
        Assert.Equal(computed.Material.Nd, d.Material.Nd);                  // the design's glass still
        Assert.Equal((0.0005, 0.3), (d.Material.NdTolerance, d.Material.VdTolerancePercent));
        Assert.Equal((-0.02, 0.03), (d.ThicknessMinus, d.ThicknessPlus));
        Assert.Equal((12.0, -0.02, 0.0), (d.Diameter, d.DiameterMinus, d.DiameterPlus));
        Assert.Equal((3.0, 0.5, 1.5, "BBAR", 9.0), (d.Front.PowerFringes, d.Front.IrregularityFringes, d.Front.TiltArcmin, d.Front.Coating, d.Front.ClearAperture));
        Assert.Equal((0.1, 0.25, 0.01), (d.Back.RadiusTolerance, d.Back.SymmetricIrregularityFringes, d.Back.DecentreMm));
        Assert.Null(d.Back.Coating);
        Assert.Equal(computed.Back.PowerFringes, d.Back.PowerFringes);    // what is not given stays

        // The edge follows the diameter as drawn.
        var s = lens.System.Surfaces;
        Assert.Equal(1.0 - DrawingLenses.Sag(s[3], 6.0) + DrawingLenses.Sag(s[4], 6.0), d.EdgeThickness, 12);

        var iso = DrawingLenses.Text(Layout.Draw(d, Settings));
        Assert.Contains("F2 or equivalent", iso);
        Assert.Contains("nd = 1.62004 ± 0.0005", iso);
        Assert.Contains("νd = 36.37 ± 0.3 %", iso);
        Assert.Contains("1.000 +0.030/-0.020", iso);
        Assert.Contains("Ø12.00 +0.000/-0.020", iso);
        Assert.Contains("Øe  9.00", iso);
        Assert.Contains("3/3(0.5)", iso);
        Assert.Contains("4/1.5'", iso);
        Assert.Contains("±0.1 CC", iso);                                   // the back's radius, its power a tolerance on it
        Assert.Contains("3/-(2/0.25)", iso);
        Assert.Contains("(0.010)", iso);
        Assert.Contains("Coating: BBAR", iso);
        Assert.Contains("Coating: —", iso);

        var asme = DrawingLenses.Text(Layout.Draw(d, Settings with { Standard = DrawingStandard.Asme }));
        Assert.Contains("F2 or equivalent", asme);
        Assert.Contains("COATING: SURFACE 1 BBAR; SURFACE 2 NONE.", asme);
        Assert.Contains("3 FR", asme);
    }

    [Fact]
    public void TheOverriddenValuesAreInTheFiles()
    {
        var lens = DrawingLenses.Triplet();
        var settings = Settings with { Elements = new() { ["OPT-02"] = Middle with { Title = "NEGATIVE", Description = "Middle of the triplet" } } };
        var d = ElementDrawings.Build(lens, DefaultTolerances.Create(lens), settings)[1];
        var sheet = Layout.Draw(d, settings);

        string pdf = Encoding.Latin1.GetString(PdfWriter.ToBytes(new[] { sheet }));
        string svg = SvgWriter.ToText(sheet);
        string dxf = DxfWriter.ToText(sheet);
        foreach (var text in new[] { "NEGATIVE", "Middle of the triplet", "F2 or equivalent" })
        {
            Assert.Contains(text, pdf);
            Assert.Contains(text, svg);
            Assert.Contains(text, dxf);
        }
        Assert.Contains(@"3/3\(0.5\)", pdf);                                // a PDF string escapes its parentheses
        Assert.Contains("3/3(0.5)", svg);
        Assert.Contains("3/3(0.5)", dxf);
        Assert.Contains("Ø12.00 +0.000/-0.020", pdf);
        Assert.Contains("vd = 36.37 ± 0.3 %", pdf);                         // ν spelled in WinAnsi
        Assert.Contains("Ø12.00 +0.000/-0.020", svg);
        Assert.Contains("%%c12.00 +0.000/-0.020", dxf);
    }

    [Fact]
    public void AGivenValueSilencesTheWarningThatTheDefaultWasUsed()
    {
        var lens = DrawingLenses.Triplet();
        var given = new ElementOverride
        {
            NdTolerance = 0.001, VdTolerancePercent = 0.5, ThicknessMinus = -0.05, ThicknessPlus = 0.05,
            Front = new SurfaceOverride { PowerFringes = 4, IrregularityFringes = 1, TiltArcmin = 2 },
            Back = new SurfaceOverride { RadiusTolerance = 0.05, IrregularityFringes = 1, TiltArcmin = 2 },
        };
        var d = ElementDrawings.Build(lens, Array.Empty<ToleranceItem>(), Settings with { Elements = new() { ["3"] = given } })[1];
        Assert.Empty(d.Warnings);
    }

    /// <summary>
    /// Power given past <see cref="DrawingSettings.MaximumPowerFringes"/> is drawn as the computed
    /// power is, as the radius tolerance it allows; a radius tolerance given is drawn as given.
    /// </summary>
    [Fact]
    public void PowerGivenPastTheTestPlateIsARadiusToleranceAsComputedPowerIs()
    {
        var lens = DrawingLenses.Triplet();
        var computed = ElementDrawings.Build(lens, ToleranceSpec.ParseSet("TFRN 1 30\n"), Settings)[0].Front;
        var given = ElementDrawings.Build(lens, ToleranceSpec.ParseSet("TFRN 1 4\n"),
            Settings with { Elements = new() { ["1"] = new ElementOverride { Front = new SurfaceOverride { PowerFringes = 30 } } } })[0].Front;
        Assert.Equal(30.0, given.PowerFringes);
        Assert.NotNull(given.RadiusTolerance);
        Assert.Equal(computed.RadiusTolerance, given.RadiusTolerance);

        var radius = ElementDrawings.Build(lens, ToleranceSpec.ParseSet("TFRN 1 30\n"),
            Settings with { Elements = new() { ["1"] = new ElementOverride { Front = new SurfaceOverride { RadiusTolerance = 0.02 } } } })[0].Front;
        Assert.Equal(0.02, radius.RadiusTolerance);

        // On a plane there is no radius to hold: its power is drawn, and the override said to be left out.
        var flat = ElementDrawings.Build(DrawingLenses.Triplet(s => s.Surfaces[2].Radius = double.PositiveInfinity), Array.Empty<ToleranceItem>(),
            Settings with { Elements = new() { ["1"] = new ElementOverride { Back = new SurfaceOverride { RadiusTolerance = 0.02 } } } })[0];
        Assert.Null(flat.Back.RadiusTolerance);
        Assert.Contains(flat.Warnings, w => w.StartsWith("surface 2: a radius tolerance on a plane"));
    }

    [Fact]
    public void AnAspheresSagTableFollowsTheGivenForm()
    {
        var lens = DrawingLenses.Aspheric();
        var given = ElementDrawings.Build(lens, ToleranceSpec.ParseSet("TFRN 1 1\n"), Settings with
        {
            Elements = new() { ["1"] = new ElementOverride { Front = new SurfaceOverride { PowerFringes = 3, IrregularityFringes = 1, ClearAperture = 12 } } },
        })[0].Front;
        var expected = ElementDrawings.SagTable(lens, 1, 6.0, PowerFringes.HeNe, 3.0, 1.0, 0.0);
        Assert.Equal(expected.Select(r => (r.H, r.Z, r.DeltaZUm)), given.SagTable.Select(r => (r.H, r.Z, r.DeltaZUm)));
        Assert.Equal(6.0, given.SagTable[^1].H);
    }

    [Fact]
    public void ADiameterGivenToOneCementedPieceIsTheGroups()
    {
        var lens = DrawingLenses.Doublet();
        var drawings = ElementDrawings.Build(lens, DefaultTolerances.Create(lens),
            Settings with { Elements = new() { ["2"] = new ElementOverride { Diameter = 24.0 } } });
        Assert.Equal(new[] { 24.0, 24.0 }, drawings.Select(d => d.Diameter));
        Assert.Contains(drawings[0].Warnings, w => w.Contains("made Ø24 to match its cemented partner OPT-02"));

        var apart = ElementDrawings.Build(lens, DefaultTolerances.Create(lens),
            Settings with { Elements = new() { ["1"] = new ElementOverride { Diameter = 23.0 }, ["2"] = new ElementOverride { Diameter = 24.0 } } });
        Assert.Equal(new[] { 23.0, 24.0 }, apart.Select(d => d.Diameter));
        Assert.All(apart, d => Assert.Contains(d.Warnings, w => w.Contains("different diameters")));
    }

    [Fact]
    public void AnOverrideNoDrawingCouldStateIsRefused()
    {
        var lens = DrawingLenses.Triplet();
        DrawingSettings With(ElementOverride o) => Settings with { Elements = new() { ["3"] = o } };
        Assert.Throws<ArgumentException>(() => ElementDrawings.Build(lens, Array.Empty<ToleranceItem>(),
            With(new ElementOverride { Front = new SurfaceOverride { PowerFringes = -1 } })));
        Assert.Throws<ArgumentException>(() => ElementDrawings.Build(lens, Array.Empty<ToleranceItem>(),
            With(new ElementOverride { Diameter = 0 })));
        Assert.Throws<ArgumentException>(() => ElementDrawings.Build(lens, Array.Empty<ToleranceItem>(),
            With(new ElementOverride { ThicknessMinus = 0.05, ThicknessPlus = -0.05 })));
    }

    [Fact]
    public void OverridesReadBackFromTheirJsonAndLeaveOutWhatTheyDoNotGive()
    {
        var settings = Settings with
        {
            Elements = new() { ["3"] = Middle with { Title = "NEGATIVE", Notes = new() { "Handle with gloves." } }, ["OPT-01"] = new ElementOverride { Number = "100-1" } },
        };
        string json = settings.ToJson();
        Assert.Contains("\"thicknessMinus\": -0.02", json);
        Assert.Contains("\"clearAperture\": 9", json);
        Assert.DoesNotContain("\"diameterPlus\"", json);                  // null: left out
        Assert.DoesNotContain("\"title\": null", json);

        var back = DrawingSettings.FromJson(json);
        var middle = back.Elements["3"];
        Assert.Equal(Middle.Front, middle.Front);
        Assert.Equal(Middle.Back, middle.Back);
        Assert.Equal(("NEGATIVE", "F2 or equivalent", 0.0005, -0.02, 12.0), (middle.Title, middle.Glass, middle.NdTolerance, middle.ThicknessMinus, middle.Diameter));
        Assert.Equal(new[] { "Handle with gloves." }, middle.Notes);
        Assert.Equal("100-1", back.Elements["OPT-01"].Number);
        Assert.Null(back.Elements["OPT-01"].Front);

        // The shop's settings alone, for another lens: the same with no elements.
        Assert.Empty((back with { Elements = new() }).Elements);
        Assert.NotEmpty(back.Elements);
    }

    /// <summary>A settings file written before the overrides were more than a title block still reads.</summary>
    [Fact]
    public void AnOldSettingsFileStillReads()
    {
        const string old = """
            {
              "standard": "Iso10110",
              "maximumPowerFringes": 10,
              "edgeMargin": 1,
              "diameterTolerance": 0.05,
              "chamferMin": null,
              "chamferMax": null,
              "imperfections": "5/3x0.16",
              "scratchDig": "60-40",
              "stressBirefringence": "0/20",
              "bubbles": "1/3x0.16",
              "inhomogeneity": "2/1;1",
              "coating": null,
              "coatings": {},
              "coatingSpecs": {},
              "asphereSlopeMrad": null,
              "asphereSlopeSamplingMm": null,
              "notes": [],
              "elements": { "3": { "title": "NEGATIVE", "number": "100-2", "description": "Middle", "notes": [ "Old note." ] } },
              "defaultFringes": 5,
              "defaultIrregularity": 1,
              "defaultThickness": 0.1,
              "defaultTiltArcmin": 3,
              "defaultIndex": 0.001,
              "defaultAbbePercent": 0.8,
              "project": "",
              "company": "",
              "drawnBy": "",
              "numberPrefix": "OPT-",
              "date": null
            }
            """;
        var settings = DrawingSettings.FromJson(old);
        var own = settings.Elements["3"];
        Assert.Equal(("NEGATIVE", "100-2", "Middle"), (own.Title, own.Number, own.Description));
        Assert.Null(own.Front);
        Assert.Null(own.Diameter);

        var lens = DrawingLenses.Triplet();
        var items = DefaultTolerances.Create(lens);
        var d = ElementDrawings.Build(lens, items, settings)[1];
        var plain = ElementDrawings.Build(lens, items, new DrawingSettings())[1];
        Assert.Equal(("100-2", "NEGATIVE", "Middle"), (d.Number, d.Title, d.Description));
        Assert.Equal((plain.Front.PowerFringes, plain.Front.IrregularityFringes, plain.Front.TiltArcmin, plain.Front.ClearAperture),
                     (d.Front.PowerFringes, d.Front.IrregularityFringes, d.Front.TiltArcmin, d.Front.ClearAperture));
        Assert.Equal((plain.Diameter, plain.ThicknessMinus, plain.Material), (d.Diameter, d.ThicknessMinus, d.Material));
        Assert.Equal(new[] { "Old note." }, d.Notes);
    }
}

/// <summary>The one way in for the CLI and MCP: the lens and its tolerance set in, the files out.</summary>
public class DrawingRunTests
{
    [Fact]
    public void ItWritesOnePdfOfEverySheetAndAnSvgAndDxfPerElement()
    {
        string folder = Path.Combine(Path.GetTempPath(), "lenshh-drawings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var lens = DrawingLenses.Doublet();
            string set = ToleranceSpec.FormatSet(DefaultTolerances.Create(lens)) + "CEDX 1-3 0.2\nCEDY 1-3 0.2\n";
            var settings = new DrawingSettings { Date = "2026-10-31", Elements = new() { ["1"] = new ElementOverride { Number = "100/1" } } };
            var r = DrawingRun.Write(Lenses.Doublet(), Lenses.Glass(), set, settings, folder, DrawingRun.ParseFormat("all"));

            Assert.Equal(2, r.Drawings.Count);
            Assert.Equal("CEMENTED DOUBLET F/5", r.Settings.Project);          // the lens's title, the settings giving none
            Assert.Contains(r.Drawings[0].MountTolerances, m => m.Contains("set at assembly"));
            Assert.Equal(new[] { DrawingRun.PdfName, "100_1.svg", "100_1.dxf", "OPT-02.svg", "OPT-02.dxf" }, r.Files.Select(Path.GetFileName));
            Assert.All(r.Files, f => Assert.True(File.Exists(f), f));
            Assert.Contains("/Count 2", Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(folder, DrawingRun.PdfName))));

            string report = DrawingRun.Report(r);
            Assert.Contains("2 element drawings (ISO 10110)", report);
            Assert.Contains("100/1", report);
            Assert.Contains(DrawingRun.PdfName, report);
            // A drawing's warnings are reported under it.
            var bare = DrawingRun.Draw(Lenses.Doublet(), Lenses.Glass(), "", settings);
            Assert.Contains("      warning: surface 1: power not toleranced", DrawingRun.Report(bare));

            var only = DrawingRun.Write(Lenses.Doublet(), Lenses.Glass(), set, settings with { Standard = DrawingRun.ParseStandard("asme") },
                                        folder, DrawingRun.ParseFormat("dxf"));
            Assert.All(only.Files, f => Assert.EndsWith(".dxf", f));
            Assert.Contains("(ASME Y14.18)", DrawingRun.Report(only));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("pdf", DrawingFormats.Pdf)]
    [InlineData("SVG", DrawingFormats.Svg)]
    [InlineData("pdf,dxf", DrawingFormats.Pdf | DrawingFormats.Dxf)]
    [InlineData("all", DrawingFormats.All)]
    [InlineData("", DrawingFormats.Pdf)]
    public void FormatsAreReadByName(string text, DrawingFormats formats) => Assert.Equal(formats, DrawingRun.ParseFormat(text));

    [Fact]
    public void AnUnknownFormatOrStandardIsRefused()
    {
        Assert.Throws<ArgumentException>(() => DrawingRun.ParseFormat("png"));
        Assert.Throws<ArgumentException>(() => DrawingRun.ParseStandard("din"));
    }
}
