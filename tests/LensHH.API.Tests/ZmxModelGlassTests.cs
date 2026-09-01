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

        // A hybrid asphere: a cemented glass doublet with a thin aspheric layer moulded on
        // the back, the layer being a model glass. Edmund's aspherized achromats (49-656..
        // 49-665) and the HOYA moulded aspheres are built this way.
        //
        // CollapseTrailingDummies sums the air-only surfaces between the last refracting
        // vertex and IMG into the lens-back vertex. Deciding which surface that is by
        // testing Material alone treats the moulded layer as air, because a model glass has
        // an index and no name — so the layer's back surface was removed, the asphere with
        // it, and the image ended up formed inside the layer's glass. On 49-658 that read
        // EFL 20.65 mm against the part's nominal 14 mm.
        private const string HybridAsphereZmx = @"TITL Hybrid Asphere
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
  CURV 0.05
  DISZ 4
  GLAS N-BK7
  DIAM 5.0 1 0 0 1 """"
  STOP
SURF 2
  TYPE STANDARD
  CURV -0.02
  DISZ 0.5
  GLAS ___BLANK 1 0 1.517 52 0 0 0 0 0 0
SURF 3
  TYPE EVENASPH
  CURV -0.01
  PARM 2 0.0007
  DISZ 40
SURF 4
  TYPE STANDARD
  CURV 0
  DISZ 0
";

        [Fact]
        public void Import_MouldedAsphericLayer_IsNotCollapsedAsATrailingDummy()
        {
            var path = Path.GetTempFileName() + ".zmx";
            File.WriteAllText(path, HybridAsphereZmx, Encoding.UTF8);
            try
            {
                var sys = ZmxReader.Read(path);

                // object, the stop dummy the stock-lens pass inserts, singlet front,
                // singlet back / layer front, aspheric layer back, image.
                Assert.Equal(6, sys.Surfaces.Count);

                var layerFront = sys.Surfaces[3];
                Assert.True(layerFront.ModelIndexEnabled, "the moulded layer's model glass was collapsed away");
                Assert.Equal(1.517, layerFront.ModelNd, 6);
                Assert.Equal(0.5, layerFront.Thickness, 9);

                // The aspheric surface behind it, and its coefficient, survive with it.
                var layerBack = sys.Surfaces[4];
                Assert.Equal(SurfaceType.EvenAsphere, layerBack.Type);
                Assert.Equal(40.0, layerBack.Thickness, 9);
                Assert.Contains(layerBack.AsphericCoefficients, c => c != 0.0);

                // The consequence that matters: image space is air, not the layer's glass.
                var last = sys.Surfaces[sys.Surfaces.Count - 2];
                Assert.False(last.ModelIndexEnabled);
                Assert.True(string.IsNullOrEmpty(last.Material));
            }
            finally { File.Delete(path); }
        }

        // The collapse itself must still do its job: a genuine air spacer between the lens
        // back and IMG carries no index and is summed into the lens-back thickness.
        [Fact]
        public void Import_PlainAirSpacer_IsStillCollapsed()
        {
            var zmx = @"TITL Spacer
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
  CURV 0.05
  DISZ 4
  GLAS N-BK7
  DIAM 5.0 1 0 0 1 """"
  STOP
SURF 2
  TYPE STANDARD
  CURV -0.02
  DISZ 20
SURF 3
  TYPE STANDARD
  CURV 0
  DISZ 15
SURF 4
  TYPE STANDARD
  CURV 0
  DISZ 0
";
            var path = Path.GetTempFileName() + ".zmx";
            File.WriteAllText(path, zmx, Encoding.UTF8);
            try
            {
                var sys = ZmxReader.Read(path);

                Assert.Equal(5, sys.Surfaces.Count);                    // the spacer is gone
                Assert.Equal(35.0, sys.Surfaces[3].Thickness, 9);       // its distance is kept
            }
            finally { File.Delete(path); }
        }
    }
}
