using System.IO;
using LensHH.Core.Enums;
using LensHH.Core.IO;
using LensHH.Core.Models;
using Xunit;

namespace LensHH.API.Tests
{
    // A Zernike departure on a Standard or Even Asphere surface lives in the generic parameter
    // slots (1 = normalization radius, 2..36 = Fringe Z1..Z35), so .lhlt keeps it as it is.
    public class ZernikeDepartureIoTests
    {
        [Fact]
        public void LhltRoundTripKeepsTheDepartureAndTheGlassOffset()
        {
            var sys = new OpticalSystem { Aperture = new Aperture(ApertureType.EPD, 10.0) };
            sys.Surfaces.Add(new Surface { Index = 0, Thickness = double.PositiveInfinity });
            var front = new Surface
            {
                Index = 1, Type = SurfaceType.EvenAsphere, Radius = 50, Thickness = 5,
                Material = "N-BK7", SemiDiameter = 6.0,
            };
            front.AsphericCoefficients[1] = 1e-6;
            Assert.Null(ZernikeDeparture.Set(front, null, new[] { (5, 2e-4), (9, -1e-4) }));
            front.ParameterVariable[9] = true;
            front.IndexOffset = 3e-4;
            front.AbbeOffset = -0.5;
            sys.Surfaces.Add(front);
            sys.Surfaces.Add(new Surface { Index = 2, Radius = -80, Thickness = 45 });
            sys.Surfaces.Add(new Surface { Index = 3 });
            sys.Wavelengths.Add(new Wavelength(0.5876, 1.0, true));
            sys.Fields.Add(new Field { Y = 0 });

            var path = Path.GetTempFileName() + ".lhlt";
            try
            {
                LhltWriter.Write(sys, path);
                var s = LhltReader.Read(path).System.Surfaces[1];
                Assert.True(s.HasZernikeDeparture);
                Assert.Equal(6.0, ZernikeDeparture.Radius(s));          // the semi-diameter
                Assert.Equal(2e-4, ZernikeDeparture.Term(s, 5));
                Assert.Equal(-1e-4, ZernikeDeparture.Term(s, 9));
                Assert.True(s.ParameterVariable[9]);
                Assert.Equal(1e-6, s.AsphericCoefficients[1]);
                Assert.Equal(3e-4, s.IndexOffset);
                Assert.Equal(-0.5, s.AbbeOffset);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void OnlyStandardAndEvenAsphereSurfacesTakeOne()
        {
            var cb = new Surface { Type = SurfaceType.CoordinateBreak };
            Assert.NotNull(ZernikeDeparture.Set(cb, 5.0, new[] { (4, 1e-4) }));
            var s = new Surface();
            Assert.NotNull(ZernikeDeparture.Set(s, 5.0, new[] { (36, 1e-4) }));   // Z1..Z35 only
        }
    }
}
