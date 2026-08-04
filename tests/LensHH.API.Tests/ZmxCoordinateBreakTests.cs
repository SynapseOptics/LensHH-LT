using System.IO;
using System.Text;
using LensHH.Core.Enums;
using LensHH.Core.IO;
using LensHH.Core.Models;
using Xunit;

namespace LensHH.API.Tests
{
    // ZEMAX (.zmx) + native (.lhlt) I/O of the Coordinate Break (PRO) surface.
    // The FORMAT handling is public; the trace/optimization/LDE editing is PRO-gated.
    // Slot map (ZEMAX PARM is 1-based -> Surface.Parameters is 0-based):
    //   PARM 1-5 = Decenter X, Decenter Y, Tilt X, Tilt Y, Tilt Z; PARM 6 = Order.
    public class ZmxCoordinateBreakTests
    {
        private static OpticalSystem MinimalSystem(Surface cb)
        {
            var sys = new OpticalSystem { Aperture = new Aperture(ApertureType.EPD, 10.0) };
            sys.Surfaces.Add(new Surface { Index = 0, Thickness = double.PositiveInfinity });
            cb.Index = 1; sys.Surfaces.Add(cb);
            sys.Surfaces.Add(new Surface { Index = 2, Thickness = 0 }); // image
            sys.Wavelengths.Add(new Wavelength(0.5876, 1.0, true));
            sys.Fields.Add(new Field { Y = 0 });
            return sys;
        }

        [Fact]
        public void Import_CoordinateBreak_ReadsTypeParamsAndOrder()
        {
            var zmx = @"TITL CB
ENPD 10
FTYP 0 0 1 1 0 0 0 0 0
WAVM 1 0.5876 1.0
PWAV 1
SURF 0
  TYPE STANDARD
  CURV 0
  DISZ INFINITY
SURF 1
  TYPE COORDBRK
  CURV 0
  PARM 1 2.5
  PARM 2 -1.5
  PARM 3 3
  PARM 4 -2
  PARM 5 5
  PARM 6 1
  DISZ 10
SURF 2
  TYPE STANDARD
  CURV 0
  DISZ 0
";
            var path = Path.GetTempFileName() + ".zmx";
            File.WriteAllText(path, zmx, Encoding.UTF8);
            try
            {
                var sys = ZmxReader.Read(path);
                var s = sys.Surfaces[1];
                Assert.Equal(SurfaceType.CoordinateBreak, s.Type);
                Assert.Equal(2.5, s.GetParameter(1), 9);   // Decenter X
                Assert.Equal(-1.5, s.GetParameter(2), 9);  // Decenter Y
                Assert.Equal(3.0, s.GetParameter(3), 9);   // Tilt X
                Assert.Equal(-2.0, s.GetParameter(4), 9);  // Tilt Y
                Assert.Equal(5.0, s.GetParameter(5), 9);   // Tilt Z
                Assert.Equal(1, s.GetSetting(1));          // Order
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void ZmxRoundTrip_PreservesParamsAndOrder()
        {
            var cb = new Surface { Type = SurfaceType.CoordinateBreak, Thickness = 12.0 };
            cb.SetParameter(1, 1.25); cb.SetParameter(2, -0.75);
            cb.SetParameter(3, 4.5); cb.SetParameter(4, -6.0); cb.SetParameter(5, 2.0);
            cb.SetSetting(1, 1);
            var sys = MinimalSystem(cb);

            var path = Path.GetTempFileName() + ".zmx";
            try
            {
                ZmxWriter.Write(sys, path);
                var s = ZmxReader.Read(path).Surfaces[1];
                Assert.Equal(SurfaceType.CoordinateBreak, s.Type);
                Assert.Equal(1.25, s.GetParameter(1), 9);
                Assert.Equal(-0.75, s.GetParameter(2), 9);
                Assert.Equal(4.5, s.GetParameter(3), 9);
                Assert.Equal(-6.0, s.GetParameter(4), 9);
                Assert.Equal(2.0, s.GetParameter(5), 9);
                Assert.Equal(1, s.GetSetting(1));
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void ZmxWriter_EmitsVparForVariableParam()
        {
            var cb = new Surface { Type = SurfaceType.CoordinateBreak, Thickness = 5.0 };
            cb.SetParameter(3, 1.0);            // Tilt X value
            cb.ParameterVariable[2] = true;     // Tilt X = param 3 (idx 2) is a variable
            var sys = MinimalSystem(cb);
            var path = Path.GetTempFileName() + ".zmx";
            try
            {
                ZmxWriter.Write(sys, path);
                var text = File.ReadAllText(path);
                Assert.Contains("TYPE COORDBRK", text);
                Assert.Contains("VPAR 3", text);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void LhltRoundTrip_PreservesParamsVariableAndBounds()
        {
            var cb = new Surface { Type = SurfaceType.CoordinateBreak, Thickness = 8.0 };
            cb.SetParameter(1, 0.5); cb.SetParameter(4, -3.0); cb.SetParameter(5, 1.5);
            cb.SetSetting(1, 1);
            cb.ParameterVariable[0] = true;                 // Decenter X variable
            cb.ParameterMin[0] = -2.0; cb.ParameterMax[0] = 2.0;
            var sys = MinimalSystem(cb);

            var path = Path.GetTempFileName() + ".lhlt";
            try
            {
                LhltWriter.Write(sys, path);
                var s = LhltReader.Read(path).System.Surfaces[1];
                Assert.Equal(SurfaceType.CoordinateBreak, s.Type);
                Assert.Equal(0.5, s.GetParameter(1), 9);
                Assert.Equal(-3.0, s.GetParameter(4), 9);
                Assert.Equal(1.5, s.GetParameter(5), 9);
                Assert.Equal(1, s.GetSetting(1));
                Assert.True(s.ParameterVariable[0]);
                Assert.Equal(-2.0, s.ParameterMin[0]);
                Assert.Equal(2.0, s.ParameterMax[0]);
            }
            finally { File.Delete(path); }
        }
    }
}
