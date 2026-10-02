using System.Globalization;
using System.IO;
using System.Text;
using LensHH.Core.IO;
using Xunit;

namespace LensHH.API.Tests
{
    // In a .zmx file MEMA is the part's mechanical semi-diameter: the drawn edge, auto or fixed,
    // never a ray block. Before 1.0.159 it was read into ClapOuterRadius, the clear aperture, so a
    // MEMA smaller than the beam vignetted, and a MEMA larger than a CLAP on the same surface
    // overrode it. And the stop's CLAP replaced ENPD even when larger: a Ritchey-Chretien's primary,
    // CLAP 26 80 under ENPD 150, was imported with a 160 mm pupil and lost 144 of 324 axial rays.
    public class ZmxMechanicalApertureTests
    {
        private static string Zmx(double enpd, double stopClap, double memaStop) =>
            $@"VERS 190513 80 123457 L123457
MODE SEQ
UNIT MM X W X CM MR CPMM
ENPD {enpd.ToString(CultureInfo.InvariantCulture)}
FTYP 0 0 1 1 0 0 0
YFLN 0
WAVM 1 0.55 1
PWAV 1
SURF 0
  CURV 0
  DISZ INFINITY
SURF 1
  STOP
  CURV 0
  DISZ 10
  MEMA {memaStop.ToString(CultureInfo.InvariantCulture)} 0 0 0 1 """"
  CLAP 0 {stopClap.ToString(CultureInfo.InvariantCulture)} 0
SURF 2
  CURV 0
  DISZ 10
  DIAM 2 0 0 0 1 """"
  MEMA 2 0 0 0 1 """"
SURF 3
  TYPE PARAXIAL
  PARM 1 100
  DISZ 100
SURF 4
  CURV 0
  DISZ 0
";

        private static string Temp(string text, string ext)
        {
            var path = Path.GetTempFileName() + ext;
            File.WriteAllText(path, text, Encoding.UTF8);
            return path;
        }

        [Theory]
        [InlineData(150.0, 80.0, 150.0)]   // a CLAP larger than ENPD does not limit the beam
        [InlineData(25.4, 11.43, 22.86)]   // a stock lens: ENPD the part's diameter, CLAP its CA
        public void TheStopClapOnlyNarrowsThePupil(double enpd, double clap, double expected)
        {
            var path = Temp(Zmx(enpd, clap, clap + 1.0), ".zmx");
            try { Assert.Equal(expected, ZmxReader.Read(path).Aperture.Value, 9); }
            finally { File.Delete(path); }
        }

        [Fact]
        public void MemaIsTheMechanicalEdgeAndClapTheClearAperture()
        {
            var path = Temp(Zmx(10.0, 4.0, 6.0), ".zmx");
            try
            {
                var sys = ZmxReader.Read(path);
                // CLAP is the clear aperture even where a larger MEMA is on the surface.
                Assert.Equal(4.0, sys.Surfaces[1].ClapOuterRadius, 9);
                Assert.Equal(6.0, sys.Surfaces[1].MechanicalSemiDiameter, 9);
                // A MEMA smaller than the beam (2 against a 5 mm marginal ray) is only drawn.
                Assert.Equal(0.0, sys.Surfaces[2].ClapOuterRadius);
                Assert.Equal(2.0, sys.Surfaces[2].MechanicalSemiDiameter, 9);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void BothRadiiSurviveLhltAndZmx()
        {
            var path = Temp(Zmx(10.0, 4.0, 6.0), ".zmx");
            var lhlt = Path.GetTempFileName() + ".lhlt";
            var zmx = Path.GetTempFileName() + ".zmx";
            try
            {
                var sys = ZmxReader.Read(path);

                LhltWriter.Write(sys, lhlt);
                var back = LhltReader.Read(lhlt).System;
                Assert.Equal(4.0, back.Surfaces[1].ClapOuterRadius, 9);
                Assert.Equal(6.0, back.Surfaces[1].MechanicalSemiDiameter, 9);
                Assert.Equal(2.0, back.Surfaces[2].MechanicalSemiDiameter, 9);

                ZmxWriter.Write(sys, zmx);
                var again = ZmxReader.Read(zmx);
                Assert.Equal(4.0, again.Surfaces[1].ClapOuterRadius, 9);
                Assert.Equal(6.0, again.Surfaces[1].MechanicalSemiDiameter, 9);
                Assert.Equal(0.0, again.Surfaces[2].ClapOuterRadius);
                Assert.Equal(2.0, again.Surfaces[2].MechanicalSemiDiameter, 9);
            }
            finally { File.Delete(path); File.Delete(lhlt); File.Delete(zmx); }
        }
    }
}
