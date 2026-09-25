using System.IO;
using System.Linq;
using System.Text;
using LensHH.Core.Enums;
using LensHH.Core.IO;
using Xunit;

namespace LensHH.API.Tests
{
    // The stock-lens pass in ZmxReader inserts a dummy stop in air ahead of a stop that sits on a
    // lens vertex. It took a stop on a MIRROR for one (the material MIRROR is not empty): a
    // paraboloid imported with a flat stop at the mirror's vertex instead of on the mirror, its
    // image surface given a fixed semi-diameter, and ray aiming turned off. Before 1.0.158.
    public class ZmxStopOnMirrorTests
    {
        // A paraboloidal mirror, f = 200, stop on the mirror, as RelativeIlluminationCalculator's
        // tests/fixtures/lenses/Paraboloid_Mirror.zmx has it.
        private const string ParaboloidZmx = @"VERS 190513 80 123457 L123457
MODE SEQ
UNIT MM X W X CM MR CPMM
ENPD 50
FTYP 0 0 2 1 0 0 0
XFLN 0 0
YFLN 0 0.5
WAVM 1 0.55 1
PWAV 1
RAIM 0 0 1 1 0 0 0 0 0 1
SURF 0
  CURV 0
  DISZ INFINITY
SURF 1
  STOP
  CURV -0.0025
  CONI -1
  DISZ -200
  GLAS MIRROR 0 0 0 0 0
  DIAM 25 1 0 0 1 """"
SURF 2
  CURV 0
  DISZ 0
";

        [Fact]
        public void AStopOnAMirrorStaysOnTheMirror()
        {
            var path = Path.GetTempFileName() + ".zmx";
            File.WriteAllText(path, ParaboloidZmx, Encoding.UTF8);
            try
            {
                var sys = ZmxReader.Read(path);

                Assert.Equal(3, sys.Surfaces.Count);                  // no dummy inserted
                Assert.Equal(1, sys.StopSurfaceIndex);                // the stop is the mirror
                Assert.True(sys.Surfaces[1].IsMirror);
                Assert.Equal(-400.0, sys.Surfaces[1].Radius, 9);
                Assert.Equal(-1.0, sys.Surfaces[1].Conic, 12);
                Assert.Equal(SemiDiameterMode.Auto, sys.Surfaces[2].SemiDiameterMode);   // image not fixed
            }
            finally { File.Delete(path); }
        }
    }
}
