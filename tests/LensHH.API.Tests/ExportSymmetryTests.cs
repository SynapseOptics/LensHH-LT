using System;
using System.IO;
using System.Linq;
using System.Text;
using LensHH.Core.Enums;
using LensHH.Core.IO;
using LensHH.Core.Models;
using Xunit;

namespace LensHH.API.Tests
{
    // Which export forms take a lens that is not a plain centred lens: the Zemax file writes each
    // field in four directions for a lens that is not rotationally symmetric; the Code V, OSLO and
    // Optalix files hold no coordinate breaks, Zernike departures or glass offsets, and refuse a lens
    // with any of them.
    public class ExportSymmetryTests
    {
        // A singlet with an on-axis and an off-axis field; tilt != 0 puts the singlet between
        // a coordinate break and the break back.
        private static OpticalSystem Singlet(double tilt = 0.0)
        {
            var sys = new OpticalSystem { Aperture = new Aperture(ApertureType.EPD, 10.0) };
            sys.Surfaces.Add(new Surface { Thickness = double.PositiveInfinity });
            if (tilt != 0.0)
            {
                var cb = new Surface { Type = SurfaceType.CoordinateBreak };
                cb.SetParameter(3, tilt);
                sys.Surfaces.Add(cb);
            }
            sys.Surfaces.Add(new Surface { Radius = 50, Thickness = 4, Material = "N-BK7", IsStop = true });
            sys.Surfaces.Add(new Surface { Radius = -50, Thickness = 45 });
            sys.Surfaces.Add(new Surface { Thickness = 0 });
            for (int i = 0; i < sys.Surfaces.Count; i++) sys.Surfaces[i].Index = i;
            sys.Wavelengths.Add(new Wavelength(0.5876, 1.0, true));
            sys.Fields.Add(new Field { Y = 0 });
            sys.Fields.Add(new Field { Y = 5 });
            return sys;
        }

        private static string FieldLine(OpticalSystem sys, string key)
        {
            var path = Path.GetTempFileName() + ".zmx";
            try
            {
                ZmxWriter.Write(sys, path);
                return File.ReadAllLines(path, Encoding.Unicode).First(l => l.StartsWith(key + " "));
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Zemax_SymmetricLens_WritesFieldsAsTheyAre()
        {
            var sys = Singlet();
            Assert.True(LensSymmetry.IsRotationallySymmetric(sys));
            Assert.StartsWith("YFLN 0 5", FieldLine(sys, "YFLN"));
            Assert.DoesNotContain("-5", FieldLine(sys, "YFLN"));
        }

        [Fact]
        public void Zemax_TiltedLens_WritesEachFieldInFourDirections()
        {
            var sys = Singlet(tilt: 0.5);
            Assert.False(LensSymmetry.IsRotationallySymmetric(sys));
            var x = FieldLine(sys, "XFLN").Split(' ').Skip(1).Select(double.Parse).ToArray();
            var y = FieldLine(sys, "YFLN").Split(' ').Skip(1).Select(double.Parse).ToArray();
            // The axis once, then the off-axis field at +y, +x, -y, -x.
            Assert.Equal(new[] { 0.0, 0.0, 5.0, 0.0, -5.0 }, x.Take(5));
            Assert.Equal(new[] { 0.0, 5.0, 0.0, -5.0, 0.0 }, y.Take(5));
        }

        [Fact]
        public void CentredFormats_RefuseALensWithCoordinateBreaksOrGlassOffsets()
        {
            var tilted = Singlet(tilt: 0.5);
            var offset = Singlet();
            offset.Surfaces[1].IndexOffset = 0.001;
            var path = Path.GetTempFileName();
            try
            {
                var e = Assert.Throws<InvalidOperationException>(() => CodeVWriter.Write(tilted, path));
                Assert.Contains("coordinate breaks", e.Message);
                e = Assert.Throws<InvalidOperationException>(() => OsloWriter.Write(offset, path));
                Assert.Contains("glass offsets", e.Message);
                Assert.Throws<InvalidOperationException>(() => OptalixWriter.Write(tilted, path));
                Assert.True(LensSymmetry.IsPlainCentred(Singlet()));
            }
            finally { File.Delete(path); }
        }
    }
}
