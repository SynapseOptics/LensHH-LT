using System.IO;
using System.Text;
using LensHH.Core.Enums;
using LensHH.Core.IO;
using LensHH.Core.Models;
using Xunit;

namespace LensHH.API.Tests
{
    // ZEMAX (.zmx) import/export of the Paraxial (ideal thin lens) surface:
    // TYPE PARAXIAL + PARM 1 = focal length (mm). Cross-checked against a real
    // ZEMAX file — the optimized focal length round-trips exactly.
    public class ZmxParaxialTests
    {
        [Fact]
        public void Import_ParaxialSurface_ReadsTypeAndFocalLength()
        {
            var zmx = @"TITL Paraxial
ENPD 12.5
FTYP 0 0 1 1 0 0 0 0 0
WAVM 1 0.5876 1.0
PWAV 1
SURF 0
  TYPE STANDARD
  CURV 0
  DISZ INFINITY
SURF 1
  STOP
  TYPE PARAXIAL
  CURV 0
  PARM 1 100
  PARM 2 1
  DISZ 4
SURF 2
  TYPE PARAXIAL
  CURV 0
  PARM 1 92.380465339848755
  PARM 2 1
  DISZ 47.40312519060367
SURF 3
  TYPE STANDARD
  CURV 0
  DISZ 0
";
            var path = Path.GetTempFileName() + ".zmx";
            File.WriteAllText(path, zmx, Encoding.UTF8);
            try
            {
                var sys = ZmxReader.Read(path);
                Assert.Equal(SurfaceType.Paraxial, sys.Surfaces[1].Type);
                Assert.Equal(100.0, sys.Surfaces[1].FocalLength, 9);
                Assert.Equal(SurfaceType.Paraxial, sys.Surfaces[2].Type);
                Assert.Equal(92.380465339848755, sys.Surfaces[2].FocalLength, 9);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void RoundTrip_ParaxialSurface_PreservesFocalLength()
        {
            var sys = new OpticalSystem { Aperture = new Aperture(ApertureType.EPD, 12.5) };
            sys.Wavelengths.Add(new Wavelength(0.5876, 1.0, true));
            sys.Surfaces.Add(new Surface { Index = 0, Thickness = double.PositiveInfinity });
            sys.Surfaces.Add(new Surface { Index = 1, IsStop = true, Type = SurfaceType.Paraxial, FocalLength = 92.380465339848755, FocalLengthVariable = true, Thickness = 47.40312519060367 });
            sys.Surfaces.Add(new Surface { Index = 2 });

            var path = Path.GetTempFileName() + ".zmx";
            try
            {
                ZmxWriter.Write(sys, path);
                string text = File.ReadAllText(path);
                Assert.Contains("TYPE PARAXIAL", text);
                Assert.Contains("PARM 1 92.380465339848755", text);
                Assert.Contains("VPAR 1", text); // focal length variable → ZEMAX variable marker

                var back = ZmxReader.Read(path);
                Assert.Equal(SurfaceType.Paraxial, back.Surfaces[1].Type);
                Assert.Equal(92.380465339848755, back.Surfaces[1].FocalLength, 9);
            }
            finally { File.Delete(path); }
        }
    }
}
