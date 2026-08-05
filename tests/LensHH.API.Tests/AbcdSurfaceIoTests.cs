using System.IO;
using LensHH.Core.Enums;
using LensHH.Core.IO;
using LensHH.Core.Models;
using Xunit;

namespace LensHH.API.Tests
{
    // ABCD ray-transfer-matrix surface (BASE — available in LT and PRO). Phase 1:
    // model + native (.lhlt) I/O. The 4 params map onto the generic Surface.Parameters
    // slots (user/ZEMAX PARM 1-based -> Surface.Parameters 0-based):
    //   Parameter 1=A, 2=B, 3=C, 4=D   (Ax=Ay, Bx=By, Cx=Cy, Dx=Dy in this implementation).
    // Each param may be Fixed / Variable / Pickup via VariableType.SurfaceParameter,
    // reusing the generic indexed-parameter machinery. ZEMAX .zmx I/O (fixed values only)
    // lands once the ZEMAX ABCD surface format is pinned from a reference file.
    public class AbcdSurfaceIoTests
    {
        private static OpticalSystem MinimalSystem(Surface abcd)
        {
            var sys = new OpticalSystem { Aperture = new Aperture(ApertureType.EPD, 10.0) };
            sys.Surfaces.Add(new Surface { Index = 0, Thickness = double.PositiveInfinity });
            abcd.Index = 1; sys.Surfaces.Add(abcd);
            sys.Surfaces.Add(new Surface { Index = 2, Thickness = 0 }); // image
            sys.Wavelengths.Add(new Wavelength(0.5876, 1.0, true));
            sys.Fields.Add(new Field { Y = 0 });
            return sys;
        }

        [Fact]
        public void LhltRoundTrip_PreservesAbcdParamsVariableAndBounds()
        {
            var s0 = new Surface { Type = SurfaceType.Abcd, Thickness = 10.0 };
            // A general (non-thin-lens) matrix so every slot is exercised and non-zero.
            s0.SetParameter(1, 0.8);    // A
            s0.SetParameter(2, 2.0);    // B
            s0.SetParameter(3, -0.05);  // C
            s0.SetParameter(4, 1.1);    // D
            s0.ParameterVariable[2] = true;                  // C is an optimization variable
            s0.ParameterMin[2] = -0.2; s0.ParameterMax[2] = 0.0;
            var sys = MinimalSystem(s0);

            var path = Path.GetTempFileName() + ".lhlt";
            try
            {
                LhltWriter.Write(sys, path);
                var s = LhltReader.Read(path).System.Surfaces[1];
                Assert.Equal(SurfaceType.Abcd, s.Type);
                Assert.Equal(0.8, s.GetParameter(1), 9);
                Assert.Equal(2.0, s.GetParameter(2), 9);
                Assert.Equal(-0.05, s.GetParameter(3), 9);
                Assert.Equal(1.1, s.GetParameter(4), 9);
                Assert.True(s.ParameterVariable[2]);
                Assert.Equal(-0.2, s.ParameterMin[2]);
                Assert.Equal(0.0, s.ParameterMax[2]);
            }
            finally { File.Delete(path); }
        }
    }
}
