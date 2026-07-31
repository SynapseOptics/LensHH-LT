using System.IO;
using System.Text;
using LensHH.Core.Enums;
using LensHH.Core.IO;
using LensHH.Core.Models;
using Xunit;

namespace LensHH.API.Tests
{
    // ZEMAX (.zmx) import/export of a model glass (Nd/Vd/dPgF). ZEMAX writes it as
    //   GLAS ___BLANK 1 0 <Nd> <Vd> <dPgF> 0 0 0 0 0
    // Cross-checked against a real ZEMAX file — values round-trip to full precision.
    public class ZmxModelGlassTests
    {
        [Fact]
        public void Import_ModelGlass_ReadsNdVdDPgF()
        {
            var zmx = @"TITL Model Glass
ENPD 10
FTYP 0 0 1 1 0 0 0 0 0
WAVM 1 0.5876 1.0
PWAV 1
SURF 0
  TYPE STANDARD
  CURV 0
  DISZ INFINITY
SURF 1
  TYPE STANDARD
  CURV 0.047
  DISZ 2
  GLAS ___BLANK 1 0 1.62040996508 60.3236491567 -0.0011 0 0 0 0 0
SURF 2
  TYPE STANDARD
  CURV -0.006
  DISZ 6
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
                var s = sys.Surfaces[1];
                Assert.True(s.ModelIndexEnabled);
                Assert.Equal(1.62040996508, s.ModelNd, 9);
                Assert.Equal(60.3236491567, s.ModelVd, 6);
                Assert.Equal(-0.0011, s.ModelDPgF, 9);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void RoundTrip_ModelGlass_PreservesModelParameters()
        {
            var sys = new OpticalSystem { Aperture = new Aperture(ApertureType.EPD, 10.0) };
            sys.Wavelengths.Add(new Wavelength(0.5876, 1.0, true));
            sys.Surfaces.Add(new Surface { Index = 0, Thickness = double.PositiveInfinity });
            sys.Surfaces.Add(new Surface { Index = 1, Curvature = 0.047, Thickness = 2.0,
                ModelIndexEnabled = true, ModelNd = 1.6299999999999999, ModelVd = 61.3236491567, ModelDPgF = -0.0111 });
            sys.Surfaces.Add(new Surface { Index = 2, Curvature = -0.006, Thickness = 6.0 });
            sys.Surfaces.Add(new Surface { Index = 3 });

            var path = Path.GetTempFileName() + ".zmx";
            try
            {
                ZmxWriter.Write(sys, path);
                Assert.Contains("GLAS ___BLANK 1 0 1.6299999999999999 61.3236491567 -0.0111", File.ReadAllText(path));

                var back = ZmxReader.Read(path);
                var s = back.Surfaces[1];
                Assert.True(s.ModelIndexEnabled);
                Assert.Equal(1.6299999999999999, s.ModelNd, 12);
                Assert.Equal(61.3236491567, s.ModelVd, 9);
                Assert.Equal(-0.0111, s.ModelDPgF, 12);
            }
            finally { File.Delete(path); }
        }
    }
}
