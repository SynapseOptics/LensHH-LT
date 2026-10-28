using System.IO;
using System.Text;
using LensHH.Core.Enums;
using LensHH.Core.Glass;
using LensHH.Core.IO;
using Xunit;

namespace LensHH.API.Tests
{
    // OpticStudio's "Float by Stop Size" makes the entrance pupil the paraxial image of the stop.
    // It was imported as EPD = 2 x the stop semi-diameter, which is right only with nothing before
    // the stop: a Cox inverse telephoto came in at F/2.86 where the stop's image gives F/3.59.
    public class ZmxFloatByStopSizeTests
    {
        // A paraboloid (f = 200) with a flat fold mirror 100 before its focus as the stop. A ray
        // parallel to the axis at height h meets the fold at h (1 - 100/200) = h/2, so a stop of
        // semi-diameter 10 is filled by an entrance pupil of diameter 40.
        private static string Folded(string stopLine) => $@"VERS 190513 80 123457 L123457
MODE SEQ
UNIT MM X W X CM MR CPMM
ENPD 10
FLOA
FTYP 0 0 1 1 0 0 0
XFLN 0
YFLN 0
WAVM 1 0.55 1
PWAV 1
RAIM 0 0 1 1 0 0 0 0 0 1
SURF 0
  CURV 0
  DISZ INFINITY
SURF 1
  CURV -0.0025
  CONI -1
  DISZ -100
  GLAS MIRROR 0 0 0 0 0
  DIAM 25 1 0 0 1 """"
SURF 2
  {stopLine}
  CURV 0
  DISZ 100
  GLAS MIRROR 0 0 0 0 0
  DIAM 10 1 0 0 1 """"
SURF 3
  CURV 0
  DISZ 0
";

        [Fact]
        public void TheEntrancePupilIsTheParaxialImageOfTheStop()
        {
            var path = Path.GetTempFileName() + ".zmx";
            File.WriteAllText(path, Folded("STOP"), Encoding.UTF8);
            try
            {
                var sys = ZmxReader.Read(path, new GlassCatalogManager(), out var notes);

                Assert.Equal(ApertureType.EPD, sys.Aperture.Type);
                Assert.Equal(40.0, sys.Aperture.Value, 9);
                Assert.Contains(notes, n => n.Contains("floats by stop size") && n.Contains("EPD 40"));
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void AStopOnTheFirstSurfaceIsItsOwnPupil()
        {
            // The stop on the paraboloid: nothing before it, so EPD = 2 x 25.
            var text = Folded("").Replace("SURF 1\n", "SURF 1\n  STOP\n");
            var path = Path.GetTempFileName() + ".zmx";
            File.WriteAllText(path, text, Encoding.UTF8);
            try
            {
                var sys = ZmxReader.Read(path, new GlassCatalogManager(), out _);

                Assert.Equal(1, sys.StopSurfaceIndex);
                Assert.Equal(50.0, sys.Aperture.Value, 9);
            }
            finally { File.Delete(path); }
        }
    }
}
