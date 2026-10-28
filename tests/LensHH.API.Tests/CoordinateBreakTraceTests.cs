using System.Linq;
using LensHH.Core.Enums;
using LensHH.Core.Models;
using LensHH.Core.RayTrace;
using Xunit;

namespace LensHH.API.Tests
{
    // A Coordinate Break traces in this edition's engine: an ideal lens decentred by a
    // break pair focuses a collimated beam onto its own (shifted) axis.
    public class CoordinateBreakTraceTests
    {
        private static Surface Break(double dy, int order)
        {
            var cb = new Surface { Type = SurfaceType.CoordinateBreak };
            cb.SetParameter(2, dy);
            cb.SetSetting(1, order);
            return cb;
        }

        [Theory]
        [InlineData(0.5)]
        [InlineData(-0.5)]
        public void ADecentredIdealLensFocusesOnItsOwnAxis(double rayHeight)
        {
            const double f = 50.0, dy = 0.1;
            var sys = new OpticalSystem { Aperture = new Aperture(ApertureType.EPD, 2.0) };
            sys.Surfaces.Add(new Surface { Thickness = double.PositiveInfinity });     // object
            var before = Break(dy, 0); before.Thickness = 0; sys.Surfaces.Add(before);
            sys.Surfaces.Add(new Surface { Type = SurfaceType.Paraxial, FocalLength = f, Thickness = 0 });
            var after = Break(-dy, 1); after.Thickness = f; sys.Surfaces.Add(after);
            sys.Surfaces.Add(new Surface { Thickness = 0 });                            // image
            for (int i = 0; i < sys.Surfaces.Count; i++) sys.Surfaces[i].Index = i;

            var tracer = new RayTracer(sys, Enumerable.Repeat(1.0, sys.Surfaces.Count).ToArray());
            var r = tracer.Trace(0, rayHeight, 0, 0, 1, startSurface: 1, endSurface: 4);

            Assert.True(r.Success);
            Assert.Equal(dy, r.SurfaceRays[4].Y, 9);
            Assert.Equal(0.0, r.SurfaceRays[4].X, 9);
        }

        // A decentred or tilted lens is for analysis, not optimization: the optimizer refuses it,
        // and accepts the same lens once its breaks are zero.
        [Fact]
        public void ADecentredLensIsNotOptimized()
        {
            var sys = new OpticalSystem { Aperture = new Aperture(ApertureType.EPD, 2.0) };
            sys.Surfaces.Add(new Surface { Thickness = double.PositiveInfinity });
            var before = Break(0.1, 0); before.Thickness = 0; sys.Surfaces.Add(before);
            sys.Surfaces.Add(new Surface { Curvature = 0.02, CurvatureVariable = true, Thickness = 0 });
            var after = Break(-0.1, 1); after.Thickness = 50; sys.Surfaces.Add(after);
            sys.Surfaces.Add(new Surface { Thickness = 0 });
            for (int i = 0; i < sys.Surfaces.Count; i++) sys.Surfaces[i].Index = i;
            sys.Wavelengths.Add(new Wavelength(0.5876, 1.0, true));
            sys.Fields.Add(new Field { Y = 0 });

            var optimizer = new LensHH.Core.Optimization.LocalOptimizer(
                sys, new LensHH.Core.MeritFunction.MeritFunction(), new LensHH.Core.Glass.GlassCatalogManager());
            var e = Assert.Throws<System.InvalidOperationException>(() => optimizer.CollectVariables());
            Assert.Contains("Surface 1", e.Message);

            before.SetParameter(2, 0); after.SetParameter(2, 0);
            optimizer.CollectVariables();
            Assert.Single(optimizer.Variables);
        }
    }
}
