using System;
using System.Globalization;
using System.IO;
using System.Linq;
using LensHH.Core.Enums;
using LensHH.Core.Glass;
using LensHH.Core.IO;
using LensHH.Core.Models;
using Xunit;

namespace LensHH.API.Tests
{
    // The Code V exporter and importer against the syntax Code V writes, as Zemax's
    // CODEV-to-OpticStudio macro (v2.06) reads it. Before 1.0.158 the exporter wrote any aperture
    // that was not an EPD as FNO (an object NA of 0.25 became F/0.25), object heights as XAN/YAN,
    // a model glass as AIR, an asphere as ASP followed by CON (which makes it a plain conic in
    // Code V), and an ideal lens as a flat surface in air. The importer did not split commands at
    // semicolons, did not join & continuations, ignored A/B/C lines under ASP, NAO and YOB, took
    // Code V's 0.1E+14 object distance for a finite one, and knew no fictitious, MIL or private
    // glass.
    public class CodeVExportTests
    {
        private const double Nd = 1.5168, Vd = 64.17;

        private static OpticalSystem Singlet(double objectDistance = double.PositiveInfinity)
        {
            var sys = new OpticalSystem { Aperture = new Aperture(ApertureType.EPD, 20.0) };
            sys.Surfaces.Add(new Surface { Index = 0, Thickness = objectDistance });
            sys.Surfaces.Add(new Surface { Index = 1, Radius = 100.0, Thickness = 5.0, IsStop = true,
                                           ModelIndexEnabled = true, ModelNd = Nd, ModelVd = Vd });
            sys.Surfaces.Add(new Surface { Index = 2, Radius = -100.0, Thickness = 95.0 });
            sys.Surfaces.Add(new Surface { Index = 3, Thickness = 0 });
            sys.Wavelengths.Add(new Wavelength(0.58756, 1.0, true));
            sys.Fields.Add(new Field { Y = 0 });
            sys.Fields.Add(new Field { Y = 5 });
            return sys;
        }

        private static string Export(OpticalSystem sys, out OpticalSystem back)
        {
            var path = Path.GetTempFileName() + ".seq";
            try
            {
                CodeVWriter.Write(sys, path, new GlassCatalogManager());
                back = CodeVReader.Read(path);
                return File.ReadAllText(path);
            }
            finally { File.Delete(path); }
        }

        private static OpticalSystem ReadSeq(string text)
        {
            var path = Path.GetTempFileName() + ".seq";
            try { File.WriteAllText(path, text); return CodeVReader.Read(path); }
            finally { File.Delete(path); }
        }

        private static string[] Lines(string text) =>
            text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();

        private static string[] With(string text, string keyword) =>
            Lines(text).Where(l => l.StartsWith(keyword + " ") || l == keyword).ToArray();

        [Fact]
        public void TheApertureGoesOutAsCodeVStatesIt()
        {
            var sys = Singlet();
            sys.Aperture = new Aperture(ApertureType.FNumber, 4.0);
            string text = Export(sys, out var back);
            Assert.Equal(new[] { "FNO 4" }, With(text, "FNO"));
            Assert.Equal(ApertureType.FNumber, back.Aperture.Type);

            // A finite object: an object NA as NAO (it went out as FNO), and an F-number as the EPD
            // it gives, Code V's FNO being defined at infinity.
            var fin = Singlet(objectDistance: 300.0);
            fin.Aperture = new Aperture(ApertureType.ObjectSpaceNA, 0.05);
            text = Export(fin, out back);
            Assert.Equal(new[] { "NAO 0.05" }, With(text, "NAO"));
            Assert.Empty(With(text, "FNO"));
            Assert.Equal(ApertureType.ObjectSpaceNA, back.Aperture.Type);
            Assert.Equal(0.05, back.Aperture.Value, 12);

            fin.Aperture = new Aperture(ApertureType.FNumber, 4.0);
            text = Export(fin, out _);
            double phi1 = (Nd - 1) / 100.0, power = 2 * phi1 - 5.0 / Nd * phi1 * phi1;
            double epd = double.Parse(With(text, "EPD").Single().Split(' ')[1], CultureInfo.InvariantCulture);
            Assert.Equal(1.0 / power / 4.0, epd, 3);
        }

        [Fact]
        public void ObjectHeightsGoOutAsYOB()
        {
            var sys = Singlet(objectDistance: 300.0);
            sys.FieldType = FieldType.ObjectHeight;
            string text = Export(sys, out var back);
            Assert.Single(With(text, "YOB"));
            Assert.Empty(With(text, "YAN"));
            Assert.Equal(FieldType.ObjectHeight, back.FieldType);
            Assert.Equal(5.0, back.Fields[1].Y, 12);

            // An object at infinity has no object heights to give.
            Assert.Throws<InvalidOperationException>(() => Export(Singlet().With(s => s.FieldType = FieldType.ObjectHeight), out _));
        }

        [Fact]
        public void AModelGlassIsCodeVsFictitiousGlass()
        {
            string text = Export(Singlet(), out var back);
            Assert.Contains(Lines(text), l => l.StartsWith("S 100 5 516800.641700"));
            Assert.True(back.Surfaces[1].ModelIndexEnabled);
            Assert.Equal(Nd, back.Surfaces[1].ModelNd, 12);
            Assert.Equal(Vd, back.Surfaces[1].ModelVd, 9);

            // A partial-dispersion offset has no place in the code: the glass goes out as a private
            // one, its index at each wavelength.
            var sys = Singlet();
            sys.Surfaces[1].ModelDPgF = 0.002;
            text = Export(sys, out back);
            Assert.Single(With(text, "PRV"));
            Assert.Contains(Lines(text), l => l.StartsWith("S 100 5 LHHMODEL1"));
            Assert.True(back.Surfaces[1].ModelIndexEnabled);
            Assert.Equal(Nd, back.Surfaces[1].ModelNd, 3);
        }

        [Theory]
        [InlineData(1.5168, 64.17, "516800.641700")]
        [InlineData(1.7725, 49.6, "772500.496000")]
        [InlineData(1.755131, 27.3, "755131.273000")]
        public void TheFictitiousGlassCodeIsCodeVs(double nd, double vd, string code)
        {
            Assert.Equal(code, CodeVWriter.FictitiousGlassCode(nd, vd, 0.0));
            Assert.True(CodeVReader.TryGlassCode(code, out double rnd, out double rvd));
            Assert.Equal(nd, rnd, 12);
            Assert.Equal(vd, rvd, 9);
        }

        [Fact]
        public void AMilCodeIsAModelGlass()
        {
            Assert.True(CodeVReader.TryGlassCode("517.642", out double nd, out double vd));
            Assert.Equal(1.517, nd, 12);
            Assert.Equal(64.2, vd, 12);
            Assert.False(CodeVReader.TryGlassCode("NBK7", out _, out _));
        }

        [Fact]
        public void AnAsphereIsASPWithItsTermsBeneath()
        {
            var sys = Singlet();
            var s = sys.Surfaces[2];
            s.Type = SurfaceType.EvenAsphere;
            s.Conic = -1.0;
            s.AsphericCoefficients[1] = 1e-7;
            s.AsphericCoefficients[2] = -2e-11;
            string text = Export(sys, out var back);
            Assert.Single(With(text, "ASP"));
            Assert.Empty(With(text, "CON"));        // CON would make it a plain conic in Code V
            Assert.Equal(new[] { "K -1" }, With(text, "K"));
            Assert.Equal(-1.0, back.Surfaces[2].Conic, 12);
            Assert.Equal(1e-7, back.Surfaces[2].AsphericCoefficients[1], 1e-20);
            Assert.Equal(-2e-11, back.Surfaces[2].AsphericCoefficients[2], 1e-24);

            // A conic alone is CON.
            s.AsphericCoefficients[1] = s.AsphericCoefficients[2] = 0;
            text = Export(sys, out back);
            Assert.Single(With(text, "CON"));
            Assert.Empty(With(text, "ASP"));
            Assert.Equal(-1.0, back.Surfaces[2].Conic, 12);

            // Code V's asphere has no r² term, and it has no ideal lens: refused, not dropped.
            s.AsphericCoefficients[0] = 1e-4;
            Assert.Throws<InvalidOperationException>(() => Export(sys, out _));
            var ideal = Singlet();
            ideal.Surfaces[2].Type = SurfaceType.Paraxial;
            ideal.Surfaces[2].FocalLength = 100.0;
            Assert.Throws<InvalidOperationException>(() => Export(ideal, out _));
        }

        // A .seq in the shape Code V saves one: several commands to a line, a continued line, an
        // asphere's terms under ASP, Code V's 0.1E+14 infinity, a fictitious glass, a private glass
        // and a decenter.
        private const string CodeVStyle = @"RDM;LEN       ""VERSION: 10.4""
TITLE 'DOUBLE GAUSS'
EPD   20.0
DIM   M
WL    656.3 587.6 486.1
REF   2
WTW   1 2 1
XAN   0.0 0.0 0.0
YAN   0.0 10.0 &
      14.0
WTF   1.0 1.0 1.0
VUY   0.0 0.0 0.0
SO    0.0 0.1E+14
S     50.0 5.0 516800.641700 ; CIR 12.5
  CCY 0; THC 0
S     -50.0 2.0 'LHHP'
  ASP
  K  -1.0
  A  1.0E-05 ; B -2.0E-08 ! a comment
S     0.0 3.0
  STO
S     -200 -40 REFL
  XDE 1.5
SI    0.0 0.0
PRV
PWL 656.3 587.6 486.1
'LHHP' 1.61 1.62 1.63
END
GO
";

        [Fact]
        public void ReadsWhatCodeVWrites()
        {
            var sys = ReadSeq(CodeVStyle);
            Assert.Equal("DOUBLE GAUSS", sys.Title);
            Assert.Equal(6, sys.Surfaces.Count);
            Assert.True(double.IsPositiveInfinity(sys.Surfaces[0].Thickness));  // 0.1E+14
            Assert.Equal(3, sys.Wavelengths.Count);
            Assert.True(sys.Wavelengths[1].IsPrimary);
            Assert.Equal(3, sys.Fields.Count);                                // YAN continued with &
            Assert.Equal(14.0, sys.Fields[2].Y, 12);

            var s1 = sys.Surfaces[1];
            Assert.True(s1.ModelIndexEnabled);
            Assert.Equal(1.5168, s1.ModelNd, 12);
            Assert.Equal(SemiDiameterMode.Fixed, s1.SemiDiameterMode);        // CIR after the ;
            Assert.Equal(12.5, s1.SemiDiameter, 12);

            var s2 = sys.Surfaces[2];
            Assert.True(s2.ModelIndexEnabled);                                // the private glass
            Assert.Equal(1.62, s2.ModelNd, 9);
            Assert.Equal(0.62 / 0.02, s2.ModelVd, 6);
            Assert.Equal(-1.0, s2.Conic, 12);
            Assert.Equal(1.0e-5, s2.AsphericCoefficients[1], 1e-20);            // A on its own line
            Assert.Equal(-2.0e-8, s2.AsphericCoefficients[2], 1e-22);

            Assert.True(sys.Surfaces[3].IsStop);
            Assert.True(sys.Surfaces[4].IsMirror);
            Assert.Contains("XDE", sys.Notes);                                 // not converted, and said so

            // The same list, for the import notice: one entry, the decenter on Code V surface 4.
            var path = Path.GetTempFileName() + ".seq";
            try
            {
                File.WriteAllText(path, CodeVStyle);
                CodeVReader.Read(path, null, out var notConverted);
                Assert.Equal(new[] { "XDE (tilt or decenter) (S4)" }, notConverted);

                File.WriteAllText(path, "RDM;LEN\nEPD 10\nWL 587.6\nSO 0 1e10\nS 50 5 516800.641700\nS 0 95\nSI 0 0\nGO\n");
                CodeVReader.Read(path, null, out notConverted);
                Assert.Empty(notConverted);                                     // nothing left out, no notice
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void CurvatureModeReadsCurvatures()
        {
            var sys = ReadSeq("RDM N\nLEN\nEPD 10\nWL 587.6\nSO 0 1e10\nS 0.02 5 516800.641700\nS 0 95\nSI 0 0\nGO\n");
            Assert.Equal(50.0, sys.Surfaces[1].Radius, 9);
            Assert.True(double.IsPositiveInfinity(sys.Surfaces[0].Thickness));
        }
    }

    internal static class OpticalSystemTestExtensions
    {
        public static OpticalSystem With(this OpticalSystem sys, Action<OpticalSystem> change)
        {
            change(sys);
            return sys;
        }
    }
}
