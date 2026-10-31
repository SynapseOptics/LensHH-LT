using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using LensHH.Core.Enums;
using LensHH.Core.Glass;
using LensHH.Core.IO;
using LensHH.Core.Models;
using Xunit;

namespace LensHH.API.Tests
{
    // A lens as a tolerancing trial has it, for Optiland: Optiland has no Coordinate Break, so each
    // surface carries its own place in space; a Zernike departure (with asphere terms) goes out as
    // the XY polynomial it adds up to; a glass offset as a tabulated glass; and, the lens not being
    // rotationally symmetric, each field at +y, +x, -y and -x. (Ray landings were checked against
    // Optiland itself: within 1.3 µm on a 20 mm image, every field direction.)
    public class OptilandTrialExportTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "optiland_" + Guid.NewGuid().ToString("N"));

        public OptilandTrialExportTests()
        {
            Directory.CreateDirectory(_dir);
            OptilandGlass.UserCatalogsFolderOverride = Path.Combine(_dir, "home", ".optiland", "catalogs");
        }

        public void Dispose()
        {
            OptilandGlass.UserCatalogsFolderOverride = null;
            try { Directory.Delete(_dir, true); } catch { }
        }

        // Object, break (decentre 0.2, tilt 1° about x), aspheric front with a Zernike departure,
        // back, image; a model glass with an offset; fields 0 and 10°.
        private static OpticalSystem Trial()
        {
            var sys = new OpticalSystem { Aperture = new Aperture(ApertureType.EPD, 10.0) };
            sys.Surfaces.Add(new Surface { Thickness = double.PositiveInfinity });
            var cb = new Surface { Type = SurfaceType.CoordinateBreak, Thickness = 1.0 };
            cb.SetParameter(2, 0.2);
            cb.SetParameter(3, 1.0);
            sys.Surfaces.Add(cb);
            var front = new Surface
            {
                Type = SurfaceType.EvenAsphere, Radius = 40, Thickness = 5, IsStop = true,
                ModelIndexEnabled = true, ModelNd = 1.6, ModelVd = 50, IndexOffset = 0.001, AbbeOffset = -0.5,
            };
            front.AsphericCoefficients[1] = 2e-6;
            front.AsphericCoefficients[2] = -1e-9;
            ZernikeDeparture.Set(front, 6.0, new[] { (5, 1e-4), (7, -2e-4), (9, 5e-5), (16, 3e-5) });
            sys.Surfaces.Add(front);
            sys.Surfaces.Add(new Surface { Radius = -60, Thickness = 50 });
            sys.Surfaces.Add(new Surface { Thickness = 0 });
            for (int i = 0; i < sys.Surfaces.Count; i++) sys.Surfaces[i].Index = i;
            sys.Wavelengths.Add(new Wavelength(0.5876, 1.0, true));
            sys.Fields.Add(new Field { Y = 0 });
            sys.Fields.Add(new Field { Y = 10 });
            return sys;
        }

        private JsonElement Export(OpticalSystem sys, out string path)
        {
            path = Path.Combine(_dir, "trial.json");
            OptilandWriter.Write(sys, path, new GlassCatalogManager(), install: false);
            // Optiland's JSON writes infinities as bare Infinity, which System.Text.Json does not read.
            string text = File.ReadAllText(path).Replace("-Infinity", "-1e308").Replace("Infinity", "1e308");
            return JsonDocument.Parse(text).RootElement.Clone();
        }

        [Fact]
        public void Breaks_AreNotWritten_EachSurfaceCarriesItsPlace()
        {
            var root = Export(Trial(), out _);
            var surfaces = root.GetProperty("surface_group").GetProperty("surfaces").EnumerateArray().ToArray();
            Assert.Equal(4, surfaces.Length);  // object, front, back, image: the break is not a surface

            // Decentred 0.2 by the break, then its thickness 1 along the axis it tilted by 1°.
            double sin = Math.Sin(Math.PI / 180), cos = Math.Cos(Math.PI / 180);
            var cs = surfaces[1].GetProperty("geometry").GetProperty("cs");
            Assert.Equal(0.2 - sin, cs.GetProperty("y").GetDouble(), 12);
            Assert.Equal(cos, cs.GetProperty("z").GetDouble(), 12);
            Assert.Equal(1.0 * Math.PI / 180, cs.GetProperty("rx").GetDouble(), 12);

            // The back surface 5 further along the same tilted axis.
            var back = surfaces[2].GetProperty("geometry").GetProperty("cs");
            Assert.Equal(0.2 - 6 * sin, back.GetProperty("y").GetDouble(), 12);
            Assert.Equal(6 * cos, back.GetProperty("z").GetDouble(), 12);
        }

        [Fact]
        public void ZernikeDeparture_IsTheSameSurface_AsAnXYPolynomial()
        {
            var sys = Trial();
            var front = sys.Surfaces[2];
            var geometry = Export(sys, out _).GetProperty("surface_group").GetProperty("surfaces")[1].GetProperty("geometry");
            Assert.Equal("PolynomialGeometry", geometry.GetProperty("type").GetString());
            var c = geometry.GetProperty("coefficients").EnumerateArray()
                .Select(row => row.EnumerateArray().Select(v => v.GetDouble()).ToArray()).ToArray();

            foreach (var (x, y) in new[] { (0.0, 0.0), (1.5, -2.0), (-3.0, 4.0), (5.5, 2.0), (-7.0, -1.0) })
            {
                double poly = 0;
                for (int i = 0; i < c.Length; i++)
                    for (int j = 0; j < c[i].Length; j++)
                        poly += c[i][j] * Math.Pow(x, i) * Math.Pow(y, j);
                double r2 = x * x + y * y, asphere = 0;
                for (int k = 0; k < front.AsphericCoefficients.Length; k++)
                    asphere += front.AsphericCoefficients[k] * Math.Pow(r2, k + 1);
                Assert.Equal(asphere + ZernikeDeparture.Sag(front, x, y), poly, 12);
            }
        }

        [Fact]
        public void GlassOffset_IsTabulated_AsLensHHComputesIt_AndFieldsGoInFourDirections()
        {
            var sys = Trial();
            var root = Export(sys, out var path);
            var material = root.GetProperty("surface_group").GetProperty("surfaces")[1].GetProperty("material_post");
            Assert.Equal(OptilandGlass.OffsetCatalog, material.GetProperty("catalog").GetString());
            string name = material.GetProperty("name").GetString()!;
            Assert.DoesNotContain("+", name);

            string yml = File.ReadAllText(Path.Combine(Path.ChangeExtension(path, null) + "_glass",
                OptilandGlass.OffsetCatalog, name + ".yml"));
            Assert.Contains("tabulated n", yml);
            var row = yml.Split('\n').Select(l => l.Trim().Split(' '))
                .First(p => p.Length == 2 && p[0] == 0.5876.ToString("R", CultureInfo.InvariantCulture));
            double expected = new GlassCatalogManager().BuildRefractiveIndexArray(sys, 0.5876)[2];
            Assert.Equal(expected, double.Parse(row[1], CultureInfo.InvariantCulture), 14);

            var fields = root.GetProperty("fields").GetProperty("fields").EnumerateArray()
                .Select(f => (f.GetProperty("x").GetDouble(), f.GetProperty("y").GetDouble())).ToArray();
            Assert.Equal(new[] { (0.0, 0.0), (0.0, 10.0), (10.0, 0.0), (0.0, -10.0), (-10.0, 0.0) }, fields);
        }
    }
}
