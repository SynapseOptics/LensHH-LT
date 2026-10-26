using System;
using System.IO;
using System.Text;
using LensHH.Core.Enums;
using LensHH.Core.Glass;
using LensHH.Core.IO;
using Xunit;

namespace LensHH.API.Tests
{
    // OpticStudio fields given as paraxial (FTYP 2) or real (FTYP 3) image height were read as
    // object angles: an SCT with 0-20 mm of image height imported with 0-20 degree fields and
    // every off-axis ray failed. They are now converted to the object angles that reach them.
    public class ZmxImageHeightFieldTests
    {
        // A paraboloid, f = 200, stop on the mirror: the chief ray leaves the vertex at the field
        // angle, so the image height is exactly 200 tan(angle), paraxial and real alike.
        private static string Paraboloid(int ftyp) => $@"VERS 190513 80 123457 L123457
MODE SEQ
UNIT MM X W X CM MR CPMM
ENPD 50
FTYP {ftyp} 0 3 1 0 0 0
XFLN 0 0 0
YFLN 0 5 10
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

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        public void ImageHeightsBecomeTheObjectAnglesThatReachThem(int ftyp)
        {
            var path = Path.GetTempFileName() + ".zmx";
            File.WriteAllText(path, Paraboloid(ftyp), Encoding.UTF8);
            try
            {
                var sys = ZmxReader.Read(path, new GlassCatalogManager(), out var notes);

                Assert.Equal(FieldType.ObjectAngle, sys.FieldType);
                Assert.Equal(3, sys.Fields.Count);
                Assert.Equal(0.0, sys.Fields[0].Y, 12);
                Assert.Equal(Math.Atan(5.0 / 200.0) * 180.0 / Math.PI, sys.Fields[1].Y, 6);
                Assert.Equal(Math.Atan(10.0 / 200.0) * 180.0 / Math.PI, sys.Fields[2].Y, 6);
                Assert.Contains(notes, n => n.Contains("image heights") && n.Contains("converted"));
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void AngleFieldsAreLeftAlone()
        {
            var path = Path.GetTempFileName() + ".zmx";
            File.WriteAllText(path, Paraboloid(0), Encoding.UTF8);
            try
            {
                var sys = ZmxReader.Read(path, new GlassCatalogManager(), out var notes);

                Assert.Equal(FieldType.ObjectAngle, sys.FieldType);
                Assert.Equal(5.0, sys.Fields[1].Y, 12);
                Assert.DoesNotContain(notes, n => n.Contains("image heights"));
            }
            finally { File.Delete(path); }
        }
    }
}
