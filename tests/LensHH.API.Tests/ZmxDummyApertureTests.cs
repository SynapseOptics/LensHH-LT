using System.IO;
using System.Text;
using LensHH.Core.Enums;
using LensHH.Core.IO;
using Xunit;

namespace LensHH.API.Tests
{
    // In a .zmx file a semi-diameter blocks light only through an aperture record on the surface;
    // a dummy's fixed DIAM and its MEMA size the drawing. A Maksutov's dummy written with DIAM and
    // MEMA 1e-6, to hide it in the layout, was imported as a Fixed 1e-6 semi-diameter and a 1e-6
    // ClapOuterRadius, and every ray stopped there. Before 1.0.159.
    public class ZmxDummyApertureTests
    {
        // A dummy (surface 1), a lens whose first surface has a fixed DIAM (surface 2), a flat dummy
        // with a CLAP of its own (surface 4), and the image.
        private const string Zmx = @"VERS 190513 80 123457 L123457
MODE SEQ
UNIT MM X W X CM MR CPMM
ENPD 10
FTYP 0 0 1 1 0 0 0
YFLN 0
WAVM 1 0.55 1
PWAV 1
SURF 0
  CURV 0
  DISZ INFINITY
SURF 1
  CURV 0.0 0 0 0 0 """"
  DISZ 5
  DIAM 9.9999999700000005e-07 1 0 0 1 """"
  MEMA 9.9999999700000005e-07 0 0 0 1 """"
SURF 2
  CURV 0.02
  DISZ 4
  GLAS N-BK7 0 0 0 0 0
  DIAM 6 1 0 0 1 """"
  MEMA 6 0 0 0 1 """"
SURF 3
  STOP
  CURV -0.02
  DISZ 10
  DIAM 6 1 0 0 1 """"
  MEMA 6 0 0 0 1 """"
SURF 4
  CURV 0
  DISZ 30
  DIAM 3 1 0 0 1 """"
  MEMA 3 0 0 0 1 """"
  CLAP 0 3 0
SURF 5
  CURV 0
  DISZ 0
";

        [Fact]
        public void AFlatAirDummyWithoutAnApertureDoesNotClip()
        {
            var path = Path.GetTempFileName() + ".zmx";
            File.WriteAllText(path, Zmx, Encoding.UTF8);
            try
            {
                var sys = ZmxReader.Read(path);

                // The dummy: no aperture record, so neither its DIAM nor its MEMA clips.
                Assert.Equal(SemiDiameterMode.Auto, sys.Surfaces[1].SemiDiameterMode);
                Assert.Equal(0.0, sys.Surfaces[1].ClapOuterRadius);
                // A lens surface keeps its fixed semi-diameter.
                Assert.Equal(SemiDiameterMode.Fixed, sys.Surfaces[2].SemiDiameterMode);
                Assert.Equal(6.0, sys.Surfaces[2].SemiDiameter, 9);
                // A flat dummy WITH an aperture record keeps it.
                Assert.Equal(SemiDiameterMode.Fixed, sys.Surfaces[4].SemiDiameterMode);
                Assert.Equal(3.0, sys.Surfaces[4].ClapOuterRadius, 9);
            }
            finally { File.Delete(path); }
        }
    }
}
