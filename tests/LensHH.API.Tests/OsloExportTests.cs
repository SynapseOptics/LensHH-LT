using System;
using System.IO;
using System.Linq;
using LensHH.Core.Enums;
using LensHH.Core.Glass;
using LensHH.Core.IO;
using LensHH.Core.Models;
using Xunit;

namespace LensHH.API.Tests
{
    // The OSLO exporter against what OSLO 6.6 itself writes (see OsloReader/OsloWriter): EBR/ANG
    // for an object at infinity, NAO/OBH for a finite one, RD on a curved object surface, PFL/PFM
    // for an ideal lens, GLA MOD with an index per wavelength for a model glass, and AP CHK only for
    // an aperture that clips. Before 1.0.158 an F-number went out as EBR 5, ideal lenses and model
    // glasses were dropped, the object's radius was dropped, a finite object's field went out as an
    // angle, and every semi-diameter was checked.
    public class OsloExportTests
    {
        // An ideal lens of focal length f at the stop, and the image plane after it. Object at
        // infinity unless objectDistance is given. No catalog glass, so no catalog is needed.
        private static OpticalSystem IdealLens(double f, double objectDistance = double.PositiveInfinity,
                                               double imageDistance = 100.0)
        {
            var sys = new OpticalSystem { Aperture = new Aperture(ApertureType.EPD, 20.0) };
            sys.Surfaces.Add(new Surface { Index = 0, Thickness = objectDistance });
            sys.Surfaces.Add(new Surface { Index = 1, Type = SurfaceType.Paraxial, FocalLength = f,
                                           IsStop = true, Thickness = imageDistance });
            sys.Surfaces.Add(new Surface { Index = 2, Thickness = 0 });
            sys.Wavelengths.Add(new Wavelength(0.58756, 1.0, true));
            sys.Fields.Add(new Field { Y = 0 });
            sys.Fields.Add(new Field { Y = 10 });
            return sys;
        }

        private static string Export(OpticalSystem sys, Func<string, OpticalSystem>? readBack, out OpticalSystem? back)
        {
            var path = Path.GetTempFileName() + ".len";
            try
            {
                OsloWriter.Write(sys, path, new GlassCatalogManager());
                back = readBack?.Invoke(path);
                return File.ReadAllText(path);
            }
            finally { File.Delete(path); }
        }

        private static string[] Lines(string text) =>
            text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();

        private static double Value(string text, string keyword) =>
            double.Parse(Lines(text).First(l => l.StartsWith(keyword + " ")).Substring(keyword.Length).Trim(),
                         System.Globalization.CultureInfo.InvariantCulture);

        [Fact]
        public void AnFNumberAtInfinityBecomesTheEntranceBeamRadius()
        {
            var sys = IdealLens(100.0);
            sys.Aperture = new Aperture(ApertureType.FNumber, 4.0);
            string text = Export(sys, OsloReader.Read, out var back);

            Assert.Equal(12.5, Value(text, "EBR"), 9);            // EFL 100 / (2 x 4)
            Assert.Equal(10.0, Value(text, "ANG"), 12);
            Assert.Equal(ApertureType.EPD, back!.Aperture.Type);
            Assert.Equal(25.0, back.Aperture.Value, 9);
        }

        [Fact]
        public void AFiniteObjectGetsAnObjectNaAHeightAndItsRadius()
        {
            // Object 200 before an f = 100 ideal lens at the stop: imaged 1:1 at 200 behind it.
            var sys = IdealLens(100.0, objectDistance: 200.0, imageDistance: 200.0);
            sys.FieldType = FieldType.ObjectHeight;
            sys.Surfaces[0].Radius = -300.0;
            string text = Export(sys, OsloReader.Read, out var back);

            // EPD 20 at the lens, 200 from the object: sin(atan(10/200)).
            Assert.Equal(Math.Sin(Math.Atan(10.0 / 200.0)), Value(text, "NAO"), 9);
            Assert.Equal(10.0, Value(text, "OBH"), 12);
            Assert.DoesNotContain(Lines(text), l => l.StartsWith("EBR ") || l.StartsWith("ANG "));
            Assert.Equal(-300.0, Value(text, "RD"), 9);           // the first RD is surface 0's
            Assert.Equal(100.0, Value(text, "PFL"), 12);
            Assert.Equal(-1.0, Value(text, "PFM"), 9);            // 1:1, inverted

            Assert.Equal(ApertureType.ObjectSpaceNA, back!.Aperture.Type);
            Assert.Equal(FieldType.ObjectHeight, back.FieldType);
            Assert.Equal(10.0, back.Fields.Max(f => f.Y), 12);
            Assert.Equal(-300.0, back.Surfaces[0].Radius, 9);
            Assert.Equal(SurfaceType.Paraxial, back.Surfaces[1].Type);
            Assert.Equal(100.0, back.Surfaces[1].FocalLength, 12);
        }

        [Fact]
        public void AFieldAngleAtAFiniteObjectBecomesAnObjectHeight()
        {
            var sys = IdealLens(100.0, objectDistance: 200.0, imageDistance: 200.0);   // fields in degrees
            string text = Export(sys, null, out _);
            // A +10 degree field aims the chief ray up, from an object below the axis; OSLO's OBH
            // is that point's y.
            Assert.Equal(-200.0 * Math.Tan(10.0 * Math.PI / 180.0), Value(text, "OBH"), 9);
        }

        [Theory]
        [InlineData("ANG -10", FieldType.ObjectAngle)]
        [InlineData("OBH -10", FieldType.ObjectHeight)]
        public void ANegativeFieldIsReadBySize(string fieldLine, FieldType type)
        {
            // The paraxial field scale takes the largest signed field, so a lens whose only field
            // is negative would have none; a negative ANG used to be dropped altogether.
            var path = Path.GetTempFileName() + ".len";
            try
            {
                File.WriteAllText(path, "LEN NEW \"x\"\nUNI 1.0\nEBR 5\n" + fieldLine +
                                        "\n// SRF 0\n  TH 200\nNXT // SRF 1\n  PFL 100\n  AST\n  TH 200\nNXT // SRF 2\n  TH 0\nEND 2\n");
                var sys = OsloReader.Read(path);
                Assert.Equal(type, sys.FieldType);
                Assert.Equal(new[] { 0.0, 10.0 }, sys.Fields.Select(f => f.Y).ToArray());
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void AModelGlassGoesOutAsItsIndexAtEachWavelength()
        {
            var sys = IdealLens(100.0);
            sys.Wavelengths.Clear();
            sys.Wavelengths.Add(new Wavelength(0.48613, 1.0, false));
            sys.Wavelengths.Add(new Wavelength(0.58756, 1.0, true));
            sys.Wavelengths.Add(new Wavelength(0.65627, 1.0, false));
            sys.Surfaces.Insert(2, new Surface { Index = 2, Radius = -200.0, Thickness = 5.0,
                                                 ModelIndexEnabled = true, ModelNd = 1.6201, ModelVd = 60.4 });
            sys.Surfaces[3].Index = 3;
            string text = Export(sys, OsloReader.Read, out var back);

            // As OSLO writes it: the wavelengths, primary first, then the index at each.
            var lines = Lines(text);
            int gla = Array.FindIndex(lines, l => l.StartsWith("GLA MOD "));
            Assert.True(gla > 0, "no GLA MOD line");
            Assert.Equal("WV 0.58756 0.48613 0.65627", lines[gla - 1]);
            var n = lines[gla].Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(3)
                              .Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            Assert.Equal(3, n.Length);
            // The indices are the model's own, so OSLO traces the lens this program traces. The model
            // is a fit with nd and Vd as parameters: its index at 0.58756 um is within 1e-6 of nd,
            // and (nd - 1)/(nF - nC) at these lines is 60.35, not quite its Vd of 60.4.
            var mgr = new GlassCatalogManager();
            Assert.Equal(mgr.BuildRefractiveIndexArray(sys, 0.58756)[2], n[0], 15);
            Assert.Equal(mgr.BuildRefractiveIndexArray(sys, 0.48613)[2], n[1], 15);
            Assert.Equal(mgr.BuildRefractiveIndexArray(sys, 0.65627)[2], n[2], 15);
            Assert.Equal(1.6201, n[0], 5);
            Assert.Equal(60.4, (n[0] - 1.0) / (n[1] - n[2]), 1);

            // Read back as a model glass with those indices' nd and Vd: the same glass to 1e-5.
            var s = back!.Surfaces[2];
            Assert.True(s.ModelIndexEnabled);
            Assert.Equal(1.6201, s.ModelNd, 5);
            Assert.Equal(60.4, s.ModelVd, 1);
            double nFBack = new GlassCatalogManager().BuildRefractiveIndexArray(back, 0.48613)[2];
            Assert.True(Math.Abs(nFBack - n[1]) < 1e-5, $"F-line index {nFBack} read back, {n[1]} written");
            Assert.Equal(3, back.Wavelengths.Count);              // the repeated WV line did not add three more
        }

        [Fact]
        public void NoNumberStandsAsAWordInTheLensName()
        {
            // OSLO read "200" in LEN NEW "Ideal lens, curved image R 200" as the surface count and
            // refused the file. The numbers go from LEN NEW; SNO1 keeps the title whole.
            var sys = IdealLens(100.0);
            sys.Title = "Ideal lens, curved image R 200";
            string text = Export(sys, OsloReader.Read, out var back);
            Assert.Contains("LEN NEW \"Ideal lens, curved image R\"", Lines(text));
            Assert.Contains("SNO1 \"Ideal lens, curved image R 200\"", Lines(text));
            Assert.Equal("Ideal lens, curved image R 200", back!.Title);

            sys.Title = "Topogon US 2031792 Fig 1";
            Assert.Contains("LEN NEW \"Topogon US Fig\"", Lines(Export(sys, null, out _)));

            sys.Title = "1 2 3";
            Assert.Contains("LEN NEW \"Untitled\"", Lines(Export(sys, null, out _)));
        }

        [Fact]
        public void CuttingTheNameTo32CharactersLeavesNoNumberAtItsEnd()
        {
            // Cut to 32 characters this read "...; 26.50MM DIA; 1", and OSLO took the 1 as the surface
            // count. The numbers are dropped again after the cut.
            var sys = IdealLens(100.0);
            sys.Title = "POSITIVE DOUBLET; 26.50MM DIA; 100.00MM EFL";
            string text = Export(sys, null, out _);
            Assert.Contains("LEN NEW \"POSITIVE DOUBLET; 26.50MM DIA;\"", Lines(text));
            Assert.Contains("SNO1 \"POSITIVE DOUBLET; 26.50MM DIA; 100.00MM EFL\"", Lines(text));
        }

        [Fact]
        public void AnR2AsphericTermIsRefusedNotDropped()
        {
            // OSLO's standard asphere starts at r^4: the term was left out, and OSLO opened another lens.
            var sys = IdealLens(100.0);
            sys.Surfaces.Insert(2, new Surface { Index = 2, Radius = -200.0, Thickness = 5.0, Type = SurfaceType.EvenAsphere });
            sys.Surfaces[3].Index = 3;
            sys.Surfaces[2].AsphericCoefficients[0] = 1e-4;
            var ex = Assert.Throws<InvalidOperationException>(() => Export(sys, null, out _));
            Assert.Contains("r²", ex.Message);
        }

        [Fact]
        public void OnlyAnApertureThatClipsIsChecked()
        {
            var sys = IdealLens(100.0);
            sys.Surfaces[1].SemiDiameterMode = SemiDiameterMode.Fixed;
            sys.Surfaces[1].SemiDiameter = 10.0;
            sys.Surfaces[2].SemiDiameterMode = SemiDiameterMode.Auto;
            sys.Surfaces[2].SemiDiameter = 20.0;
            string text = Export(sys, OsloReader.Read, out var back);
            Assert.Contains("AP CHK 10", Lines(text));             // Fixed: checked, it clips
            Assert.Contains("AP 20", Lines(text));                 // Auto: sized for drawing, never clips
            Assert.Equal(SemiDiameterMode.Fixed, back!.Surfaces[1].SemiDiameterMode);
            Assert.Equal(SemiDiameterMode.Auto, back.Surfaces[2].SemiDiameterMode);
            Assert.Equal(20.0, back.Surfaces[2].SemiDiameter, 12);

            // An automatic stop is left for OSLO to size from EBR.
            sys.Surfaces[1].SemiDiameterMode = SemiDiameterMode.Auto;
            text = Export(sys, null, out _);
            Assert.DoesNotContain("AP 10", Lines(text));
            Assert.DoesNotContain("AP CHK 10", Lines(text));

            // An automatic semi-diameter held under 100 % of the beam vignettes by design: checked.
            sys.Surfaces[2].ClearAperturePercent = 90.0;
            text = Export(sys, null, out _);
            Assert.Contains("AP CHK 20", Lines(text));
        }

        // Written by OSLO 6.6 EDU itself (2026-09-24): a finite object with a curved surface, a
        // perfect lens, a catalog glass, a model glass, one checked and one unchecked aperture.
        private const string OslosOwnFile = @"// OSLO 6.6 58447     0     0
LEN NEW ""No name"" 100 5
NAO  0.05
OBH  10.0
DES  ""OSLO""
UNI  1.0
// SRF 0
AIR
RD   -300.0
TH   200.0
AP  10.0
NXT  // SRF 1
AIR
TH   100.0
PFL    100.0
NXT  // SRF 2
GLA BAF4
RD   200.0
TH   10.0
AP CHK 0.0
NXT  // SRF 3
WV 0.58756 0.48613 0.65627
GLA MOD MODEL1      1.6201 1.6272360458395 1.6169694895481
TCE  236.0
TH   20.0
NXT  // SRF 4
AIR
TH   30.0
AP  0.4595340390767
NXT  // SRF 5
AIR
WV 0.58756 0.48613 0.65627
WW 1.0 1.0 1.0
END  5
";

        [Fact]
        public void ReadsWhatOsloWrites()
        {
            var path = Path.GetTempFileName() + ".len";
            try
            {
                File.WriteAllText(path, OslosOwnFile);
                var sys = OsloReader.Read(path);

                Assert.Equal(ApertureType.ObjectSpaceNA, sys.Aperture.Type);
                Assert.Equal(0.05, sys.Aperture.Value, 12);
                Assert.Equal(FieldType.ObjectHeight, sys.FieldType);
                Assert.Equal(10.0, sys.Fields.Max(f => f.Y), 12);
                Assert.Equal(-300.0, sys.Surfaces[0].Radius, 12);
                Assert.Equal(SurfaceType.Paraxial, sys.Surfaces[1].Type);
                Assert.Equal(100.0, sys.Surfaces[1].FocalLength, 12);
                Assert.Equal("BAF4", sys.Surfaces[2].Material);
                Assert.True(sys.Surfaces[3].ModelIndexEnabled);
                Assert.Equal(1.6201, sys.Surfaces[3].ModelNd, 9);
                Assert.Equal(60.4, sys.Surfaces[3].ModelVd, 3);
                Assert.Equal(SemiDiameterMode.Auto, sys.Surfaces[4].SemiDiameterMode);   // AP, not AP CHK
                Assert.Equal(3, sys.Wavelengths.Count);
                Assert.Equal(0.58756, sys.Wavelengths[sys.PrimaryWavelengthIndex].Value, 5);
            }
            finally { File.Delete(path); }
        }
    }
}
