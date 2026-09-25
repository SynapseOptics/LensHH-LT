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
    // The Optalix exporter and importer against what Optalix itself writes, from the 1019 lens files
    // that ship with it and its reference manual (§32.2). Before 1.0.158 the exporter wrote any
    // aperture as EPD (an F/6.3 lens got a 6.3 mm pupil), object heights as FTYP 0, a bare M for a
    // mirror, RAIM 0 then RAIM 2, no ideal lens and no model glass (the surface went out as air), no
    // FH (so nothing clipped in Optalix), and eleven ASP values; the importer read FTYP, RAIM and APE
    // wrongly and knew no NAO, lens module or fictitious glass.
    public class OptalixExportTests
    {
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

        private static string Export(OpticalSystem sys, out OpticalSystem back)
        {
            var path = Path.GetTempFileName() + ".otx";
            try
            {
                OptalixWriter.Write(sys, path, new GlassCatalogManager());
                back = OptalixReader.Read(path);
                return File.ReadAllText(path);
            }
            finally { File.Delete(path); }
        }

        private static string[] Lines(string text) =>
            text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();

        private static string[] With(string text, string keyword) =>
            Lines(text).Where(l => l.StartsWith(keyword + " ") || l == keyword).ToArray();

        [Fact]
        public void TheApertureGoesOutAsOptalixStatesIt()
        {
            var sys = IdealLens(100.0);
            sys.Aperture = new Aperture(ApertureType.FNumber, 4.0);
            string text = Export(sys, out var back);
            Assert.Equal(new[] { "FNO 4" }, With(text, "FNO"));
            Assert.Empty(With(text, "EPD"));
            Assert.Equal(ApertureType.FNumber, back.Aperture.Type);

            // A finite object: an object NA as NAO, an F-number as the EPD it gives (Optalix's FNO is
            // defined at infinity), and object heights as FTYP 2.
            var fin = IdealLens(100.0, objectDistance: 200.0, imageDistance: 200.0);
            fin.FieldType = FieldType.ObjectHeight;
            fin.Aperture = new Aperture(ApertureType.ObjectSpaceNA, 0.05);
            text = Export(fin, out back);
            Assert.Equal(new[] { "NAO 0.05" }, With(text, "NAO"));
            Assert.Equal(new[] { "FTYP 2" }, With(text, "FTYP"));
            Assert.Equal(ApertureType.ObjectSpaceNA, back.Aperture.Type);
            Assert.Equal(FieldType.ObjectHeight, back.FieldType);

            fin.Aperture = new Aperture(ApertureType.FNumber, 4.0);
            text = Export(fin, out _);
            Assert.Equal(new[] { "EPD 25" }, With(text, "EPD"));     // EFL 100 / 4
        }

        [Fact]
        public void RayAimingIsOneLineInOptalixsCodes()
        {
            var sys = IdealLens(100.0);
            Assert.Equal(new[] { "RAIM 1" }, With(Export(sys, out var back), "RAIM"));          // paraxial pupil
            Assert.Equal(RayAimingMode.Off, back.RayAiming);
            sys.RayAiming = RayAimingMode.Real;
            Assert.Equal(new[] { "RAIM 2" }, With(Export(sys, out back), "RAIM"));              // real stop
            Assert.Equal(RayAimingMode.Real, back.RayAiming);

            var tel = IdealLens(100.0, objectDistance: 200.0, imageDistance: 200.0);
            tel.Aperture = new Aperture(ApertureType.ObjectSpaceNA, 0.05);
            tel.TelecentricObjectSpace = true;
            Assert.Equal(new[] { "RAIM 3" }, With(Export(tel, out back), "RAIM"));              // telecentric
            Assert.True(back.TelecentricObjectSpace);
        }

        [Fact]
        public void AnIdealLensIsOptalixsLensModule()
        {
            string text = Export(IdealLens(100.0), out var back);
            Assert.Equal(2, With(text, "SUT L").Length);
            Assert.Equal(new[] { "LMOD 0.01 0 0 0 0", "LMOD 0 0 0 0 0" }, With(text, "LMOD"));   // a power, 1/f

            Assert.Equal(3, back.Surfaces.Count);                 // the pair is one ideal lens again
            Assert.Equal(SurfaceType.Paraxial, back.Surfaces[1].Type);
            Assert.Equal(100.0, back.Surfaces[1].FocalLength, 9);
            Assert.Equal(100.0, back.Surfaces[1].Thickness, 12);
            Assert.True(back.Surfaces[1].IsStop);
        }

        [Fact]
        public void AModelGlassIsOptalixsFictitiousGlass()
        {
            var sys = IdealLens(100.0);
            sys.Surfaces.Insert(2, new Surface { Index = 2, Radius = -200.0, Thickness = 5.0,
                                                 ModelIndexEnabled = true, ModelNd = 1.6201, ModelVd = 60.4 });
            sys.Surfaces[3].Index = 3;
            string text = Export(sys, out var back);
            Assert.Contains("GLA 6201.604", Lines(text));
            Assert.True(back.Surfaces[2].ModelIndexEnabled);
            Assert.Equal(1.6201, back.Surfaces[2].ModelNd, 12);
            Assert.Equal(60.4, back.Surfaces[2].ModelVd, 12);

            // A partial-dispersion offset has no place in the code: the index at each wavelength goes
            // out instead, as Optalix's PRI.
            sys.Surfaces[2].ModelDPgF = 0.002;
            text = Export(sys, out back);
            Assert.Single(With(text, "PRI"));
            Assert.True(back.Surfaces[2].ModelIndexEnabled);
        }

        [Theory]
        [InlineData(1.6201, 60.4, "6201.604")]
        [InlineData(1.516, 64.0, "516.64")]
        [InlineData(1.7725, 49.6, "7725.496")]
        [InlineData(1.755131, 27.3, "755131.273")]
        public void TheFictitiousGlassCodeIsOptalixs(double nd, double vd, string code)
        {
            Assert.Equal(code, OptalixWriter.FictitiousGlassCode(nd, vd, 0.0));
            Assert.True(OptalixReader.TryFictitiousGlass(code, out double rnd, out double rvd));
            Assert.Equal(nd, rnd, 12);
            Assert.Equal(vd, rvd, 12);
        }

        [Theory]
        [InlineData("6204.603", 1.6204, 60.3)]      // the forms Optalix's own lens files use
        [InlineData("617.366", 1.617, 36.6)]
        [InlineData("7725.4960", 1.7725, 49.60)]
        [InlineData("516.64", 1.516, 64.0)]
        [InlineData("620603", 1.620, 60.3)]
        [InlineData("74400.449", 1.744, 44.9)]
        public void OptalixsFictitiousGlassesAreRead(string code, double nd, double vd)
        {
            Assert.True(OptalixReader.TryFictitiousGlass(code, out double rnd, out double rvd));
            Assert.Equal(nd, rnd, 12);
            Assert.Equal(vd, rvd, 12);
            Assert.False(OptalixReader.TryFictitiousGlass("N-BK7", out _, out _));
        }

        [Fact]
        public void OnlyAnApertureThatClipsIsMarkedFH()
        {
            var sys = IdealLens(100.0);
            sys.Surfaces.Insert(2, new Surface { Index = 2, Radius = -200.0, Thickness = 5.0, Material = "N-BK7",
                                                 SemiDiameter = 12.0, SemiDiameterMode = SemiDiameterMode.Fixed });
            sys.Surfaces.Insert(3, new Surface { Index = 3, Radius = 200.0, Thickness = 50.0,
                                                 SemiDiameter = 13.0, SemiDiameterMode = SemiDiameterMode.Auto });
            sys.Surfaces[4].Index = 4;
            var path = Path.GetTempFileName() + ".otx";
            try
            {
                OptalixWriter.Write(sys, path, new GlassCatalogManager());
                string text = File.ReadAllText(path);
                Assert.Single(With(text, "FH"));
                var back = OptalixReader.Read(path);
                // Surface numbers as written: the ideal lens reads back as one surface again.
                Assert.Equal(SemiDiameterMode.Fixed, back.Surfaces[2].SemiDiameterMode);
                Assert.Equal(SemiDiameterMode.Auto, back.Surfaces[3].SemiDiameterMode);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void MirrorsAndAsphereGoOutAsOptalixWritesThem()
        {
            var sys = IdealLens(100.0);
            sys.Surfaces[1] = new Surface { Index = 1, Radius = -400.0, Material = "MIRROR", IsStop = true,
                                            Thickness = -200.0 };
            string text = Export(sys, out var back);
            Assert.Contains("SUT SM", Lines(text));
            Assert.True(back.Surfaces[1].IsMirror);

            sys.Surfaces[1].Conic = -1.0;
            sys.Surfaces[1].Type = SurfaceType.EvenAsphere;
            sys.Surfaces[1].AsphericCoefficients[1] = 1e-8;
            text = Export(sys, out back);
            Assert.Contains("SUT AM", Lines(text));
            Assert.Equal(11, With(text, "ASP").Single().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);  // ASP + 10
            Assert.Equal(-1.0, back.Surfaces[1].Conic, 12);
            Assert.Equal(1e-8, back.Surfaces[1].AsphericCoefficients[1], 15);

            // Optalix's even asphere has no r² term: refused, not dropped.
            sys.Surfaces[1].AsphericCoefficients[0] = 1e-4;
            Assert.Throws<InvalidOperationException>(() => Export(sys, out _));
        }

        // Lines copied from the lens files that ship with Optalix: the tube lens of
        // Gross-HOS/Misc/45-132_Chromat-with-tube-lens.otx (an L pair, 1/160 mm), a fictitious glass
        // and a PRI glass from Eye/EYE_NEW_CHROMATIC.OTX, an FH line, and RAIM 2.
        private const string OptalixStyle = @"VERS 11.82
RAIM  2
EPD  10.0000
WL   0.54600     0.48600     0.65000
WTW  100 100 100
REF    1
FTYP    1
NFLD    2
FLD    1   0.000000000       0.000000000      100  1        2594861
FLD    2   0.000000000       2.000000000      100  1        2594861
SUR   0
SUT S
CUY 0.0000000000000
THI  0.1000000000E+21
SUR   1
SUT S
CUY  0.0100000000000
THI   5.000000000
GLA 613369
APE  1   10.00000000       10.00000000      0.000000000      0.000000000      0.000000000        1   0   0   1
FH    1  1
SUR   2
SUT S
CUY -0.0100000000000
THI   10.00000000
PRI   1.336000000       1.336000000       1.336000000
APE  1   10.00000000       10.00000000      0.000000000      0.000000000      0.000000000        1   0   0   1
SUR   3
SUT L
CUY 0.0000000000000
THI 0.000000000
APE  1   0.000000000       0.000000000       0.000000000       0.000000000       0.000000000        1   0   0
LMOD  0.6250000000E-02   0.000000000       0.000000000       0.000000000       0.000000000
COM TUBUSLINSE
STO
SUR   4
SUT L
CUY 0.0000000000000
THI 140.0000000
APE  1   8.239765462       8.239765462       0.000000000       0.000000000       0.000000000        1   0   0
LMOD   0.000000000       0.000000000       0.000000000       0.000000000       0.000000000
SUR   5
SUT S
CUY 0.0000000000000
THI 0.000000000
";

        [Fact]
        public void ReadsWhatOptalixWrites()
        {
            var path = Path.GetTempFileName() + ".otx";
            try
            {
                File.WriteAllText(path, OptalixStyle);
                var sys = OptalixReader.Read(path);

                Assert.Equal(RayAimingMode.Real, sys.RayAiming);
                Assert.Equal(FieldType.ObjectAngle, sys.FieldType);
                Assert.True(sys.Surfaces[1].ModelIndexEnabled);                      // GLA 613369
                Assert.Equal(1.613, sys.Surfaces[1].ModelNd, 12);
                Assert.Equal(36.9, sys.Surfaces[1].ModelVd, 12);
                Assert.Equal(SemiDiameterMode.Fixed, sys.Surfaces[1].SemiDiameterMode); // FH 1
                Assert.True(sys.Surfaces[2].ModelIndexEnabled);                      // PRI
                Assert.Equal(1.336, sys.Surfaces[2].ModelNd, 12);
                Assert.Equal(SemiDiameterMode.Auto, sys.Surfaces[2].SemiDiameterMode);  // no FH

                Assert.Equal(5, sys.Surfaces.Count);                                 // the L pair is one lens
                var tube = sys.Surfaces[3];
                Assert.Equal(SurfaceType.Paraxial, tube.Type);
                Assert.Equal(160.0, tube.FocalLength, 9);
                Assert.Equal(140.0, tube.Thickness, 12);
                Assert.True(tube.IsStop);
            }
            finally { File.Delete(path); }
        }
    }
}
