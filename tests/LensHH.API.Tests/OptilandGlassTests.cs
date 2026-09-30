using System;
using System.IO;
using System.Linq;
using LensHH.Core.Enums;
using LensHH.Core.Glass;
using LensHH.Core.IO;
using LensHH.Core.Models;
using Xunit;

namespace LensHH.API.Tests
{
    // Glass in Optiland files. Before 1.0.158 each glass went out as a bare name with
    // robust_search, which lets Optiland take the nearest name from any catalog, BK7 among them
    // (Optiland files N-BK7 under a "BK7" group of equivalents). Of the glasses in
    // the catalogs LensHH-LT ships, 96 Schott and 146 Sumita names came back as a different glass
    // that way. A model glass went out as a name Optiland could not know. On the way in, only a
    // Material's name was kept, and every other kind of material became air.
    public class OptilandGlassTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "optiland_" + Guid.NewGuid().ToString("N"));

        public OptilandGlassTests()
        {
            Directory.CreateDirectory(_dir);
            OptilandGlass.UserCatalogsFolderOverride = Path.Combine(_dir, "home", ".optiland", "catalogs");
        }

        public void Dispose()
        {
            OptilandGlass.UserCatalogsFolderOverride = null;
            try { Directory.Delete(_dir, true); } catch { }
        }

        // Optiland's refractiveindex.info formulas, as optiland/materials/material_file.py
        // evaluates them.
        private static double Rii(int formula, double[] c, double w)
        {
            double n;
            switch (formula)
            {
                case 2:
                    n = 1 + c[0];
                    for (int k = 1; k < c.Length; k += 2) n += c[k] * w * w / (w * w - c[k + 1]);
                    return Math.Sqrt(n);
                case 3:
                    n = c[0];
                    for (int k = 1; k < c.Length; k += 2) n += c[k] * Math.Pow(w, c[k + 1]);
                    return Math.Sqrt(n);
                case 4:
                    n = c[0] + c[1] * Math.Pow(w, c[2]) / (w * w - Math.Pow(c[3], c[4]))
                             + c[5] * Math.Pow(w, c[6]) / (w * w - Math.Pow(c[7], c[8]));
                    for (int k = 9; k < c.Length; k += 2) n += c[k] * Math.Pow(w, c[k + 1]);
                    return Math.Sqrt(n);
                case 5:
                    n = c[0];
                    for (int k = 1; k < c.Length; k += 2) n += c[k] * Math.Pow(w, c[k + 1]);
                    return n;
                case 7:
                    n = c[0] + c[1] / (w * w - 0.028) + c[2] * Math.Pow(1 / (w * w - 0.028), 2);
                    for (int k = 3; k < c.Length; k++) n += c[k] * Math.Pow(w, 2 * (k - 2));
                    return n;
                default:
                    throw new ArgumentException($"formula {formula}");
            }
        }

        [Theory]
        [InlineData(1, new[] { 2.27, -0.01, 0.012, 0.0002, 0.00001, 0.000001 })]
        [InlineData(2, new[] { 1.03961212, 0.00600069867, 0.231792344, 0.0200179144, 1.01046945, 103.560653 })]
        [InlineData(3, new[] { 1.5, 0.004, 0.0001, -0.002, 0.00001, -0.0000001 })]
        [InlineData(4, new[] { 0.2, 1.0, 0.1, 0.01, 10.0 })]
        [InlineData(5, new[] { 1.5, 0.006, 0.0002 })]
        [InlineData(6, new[] { 1.0, 0.006, 0.2, 0.02, 1.0, 100.0, 0.01, 200.0 })]
        [InlineData(7, new[] { 2.3, 0.01, 0.02, 0.001 })]
        [InlineData(8, new[] { 2.3, 0.01, 0.02, 0.001 })]
        [InlineData(9, new[] { 1.3, 1.0, 0.01, 0.5, 100.0 })]
        [InlineData(10, new[] { 2.27, -0.01, 0.012, 0.0002, 0.00001, 0.000001, 0.0000001, 0.00000001 })]
        [InlineData(11, new[] { 1.0, 0.006, 0.2, 0.02, 1.0, 100.0, 0.01, 200.0, 0.001, 300.0 })]
        [InlineData(12, new[] { 2.27, -0.01, 0.012, 0.0002, 0.00001, 0.000001, 0.0001, 0.00001 })]
        [InlineData(13, new[] { 2.27, -0.01, 0.0001, 0.012, 0.0002, 0.00001, 0.000001, 0.0000001, 0.00000001 })]
        public void EveryAgfFormulaGoesOutExactly(int formula, double[] c)
        {
            var g = new GlassData { Name = "X", Catalog = "T", DispersionFormula = formula, Coefficients = c.Concat(new double[10 - c.Length]).ToArray() };
            var (rii, rc) = OptilandGlass.Dispersion(g)!.Value;
            foreach (var w in new[] { 0.40, 0.4861327, 0.5875618, 0.6562725, 0.80, 1.50 })
                Assert.Equal(g.GetIndex(w), Rii(rii, rc, w), 12);
        }

        [Theory]
        [InlineData(1.5168, 64.17, 0.0)]
        [InlineData(1.7847, 25.68, 0.0092)]
        [InlineData(1.45, 85.0, -0.03)]
        [InlineData(1.62, 0.0, 0.0)]        // Vd 0: a constant index
        public void TheModelGlassGoesOutExactly(double nd, double vd, double dPgF)
        {
            var (f, c) = OptilandGlass.ModelDispersion(nd, vd, dPgF);
            Assert.Equal(5, f);
            foreach (var w in new[] { 0.36, 0.4861327, 0.5875618, 0.6562725, 1.0, 2.5 })
                Assert.Equal(ModelGlass.Index(w, nd, vd, dPgF), Rii(f, c, w), 12);
        }

        private GlassCatalogManager Catalogs()
        {
            // N-BK7 in SCHOTT, and an SK16 in both SCHOTT and SUMITA that differ.
            File.WriteAllText(Path.Combine(_dir, "SCHOTT.AGF"),
                "NM N-BK7 2 517642 1.5168 64.17 0 1 0\r\n" +
                "CD 1.03961212 0.00600069867 0.231792344 0.0200179144 1.01046945 103.560653 0 0 0 0\r\n" +
                "LD 0.3 2.5\r\n" +
                "NM SK16 2 620603 1.62041 60.32 0 1 0\r\n" +
                "CD 1.34317774 0.00704687339 0.241144399 0.0229005 0.994317969 92.7508526 0 0 0 0\r\n" +
                "LD 0.31 2.5\r\n");
            File.WriteAllText(Path.Combine(_dir, "SUMITA.AGF"),
                "NM SK16 1 620603 1.62041 60.34 0 1 0\r\n" +
                "CD 2.5 -0.01 0.018 0.0003 0.00001 0.000001 0 0 0 0\r\n" +
                "LD 0.36 1.0\r\n");
            var mgr = new GlassCatalogManager();
            mgr.LoadCatalog(Path.Combine(_dir, "SCHOTT.AGF"));
            mgr.LoadCatalog(Path.Combine(_dir, "SUMITA.AGF"));
            return mgr;
        }

        private static OpticalSystem Doublet()
        {
            var sys = new OpticalSystem { Aperture = new Aperture(ApertureType.EPD, 20.0) };
            sys.GlassCatalogs.Add("SUMITA");
            sys.Surfaces.Add(new Surface { Index = 0, Thickness = double.PositiveInfinity });
            sys.Surfaces.Add(new Surface { Index = 1, Radius = 60.0, Thickness = 6.0, IsStop = true, Material = "N-BK7" });
            sys.Surfaces.Add(new Surface { Index = 2, Radius = -45.0, Thickness = 3.0, Material = "SK16" });
            sys.Surfaces.Add(new Surface { Index = 3, Radius = -200.0, Thickness = 2.0,
                                           ModelIndexEnabled = true, ModelNd = 1.7847, ModelVd = 25.68, ModelDPgF = 0.0092 });
            sys.Surfaces.Add(new Surface { Index = 4, Radius = 300.0, Thickness = 90.0 });
            sys.Surfaces.Add(new Surface { Index = 5, Thickness = 0 });
            sys.Wavelengths.Add(new Wavelength(0.58756, 1.0, true));
            sys.Fields.Add(new Field { Y = 0 });
            return sys;
        }

        [Fact]
        public void EachGlassGoesOutWithItsCatalogAndItsData()
        {
            var mgr = Catalogs();
            string path = Path.Combine(_dir, "doublet.json");
            string? folder = OptilandWriter.Write(Doublet(), path, mgr).GlassFolder;
            string json = File.ReadAllText(path);

            // Named with the catalog that owns it - SK16 from SUMITA, as the system prefers - under
            // a name Optiland's own database does not use, and strict, so Optiland never
            // substitutes another glass.
            Assert.Contains("\"name\": \"N-BK7\", \"reference\": null, \"catalog\": \"lenshh-schott\", \"match_policy\": \"strict\"", json);
            Assert.Contains("\"name\": \"SK16\", \"reference\": null, \"catalog\": \"lenshh-sumita\"", json);
            Assert.DoesNotContain("\"robust_search\": true", json);
            string model = OptilandGlass.ModelName(1.7847, 25.68, 0.0092);
            Assert.Contains($"\"name\": \"{model}\", \"reference\": null, \"catalog\": \"lenshh-model\"", json);

            // And beside it, the data itself, as Optiland user catalogs.
            Assert.Equal(Path.Combine(_dir, "doublet_glass"), folder);
            Assert.True(File.Exists(Path.Combine(folder!, "lenshh-schott", "N-BK7.yml")));
            Assert.True(File.Exists(Path.Combine(folder!, "lenshh-sumita", "SK16.yml")));
            Assert.False(File.Exists(Path.Combine(folder!, "lenshh-schott", "SK16.yml")));
            Assert.True(File.Exists(Path.Combine(folder!, "lenshh-model", model + ".yml")));
            Assert.True(File.Exists(Path.Combine(folder!, "README.txt")));
            string yml = File.ReadAllText(Path.Combine(folder!, "lenshh-sumita", "SK16.yml"));
            Assert.Contains("type: formula 3", yml);
            Assert.Contains("wavelength_range: 0.36 1", yml);
        }

        [Fact]
        public void TheSameLensComesBack()
        {
            var mgr = Catalogs();
            string path = Path.Combine(_dir, "doublet.json");
            OptilandWriter.Write(Doublet(), path, mgr);

            var back = OptilandReader.Read(path, mgr, out var notes);

            Assert.Empty(notes);
            Assert.Equal("N-BK7", back.Surfaces[1].Material);
            Assert.Equal("SK16", back.Surfaces[2].Material);
            Assert.Contains("SUMITA", back.GlassCatalogs);
            Assert.Equal(mgr.GetGlass("SUMITA:SK16")!.GetIndex(0.5), mgr.GetGlass("SK16", back.GlassCatalogs)!.GetIndex(0.5));
            var m = back.Surfaces[3];
            Assert.True(m.ModelIndexEnabled);
            Assert.Equal(1.7847, m.ModelNd, 8);
            Assert.Equal(25.68, m.ModelVd, 6);
            Assert.Equal(0.0092, m.ModelDPgF, 8);
        }

        // Installed where Optiland reads user catalogs, so the lens opens there as it is. Only
        // lenshh- folders are written, and nothing already there is removed.
        [Fact]
        public void TheGlassesAreInstalledForOptiland()
        {
            var mgr = Catalogs();
            string catalogs = OptilandGlass.UserCatalogsFolder;
            Directory.CreateDirectory(Path.Combine(catalogs, "mine"));
            File.WriteAllText(Path.Combine(catalogs, "mine", "X.yml"), "x");
            Directory.CreateDirectory(Path.Combine(catalogs, "lenshh-schott"));
            File.WriteAllText(Path.Combine(catalogs, "lenshh-schott", "F2.yml"), "from another lens");

            var export = OptilandWriter.Write(Doublet(), Path.Combine(_dir, "doublet.json"), mgr);

            Assert.Equal(catalogs, export.InstalledTo);
            Assert.Equal(3, export.InstalledGlasses);
            Assert.Null(export.InstallError);
            Assert.Equal(File.ReadAllText(Path.Combine(export.GlassFolder!, "lenshh-sumita", "SK16.yml")),
                         File.ReadAllText(Path.Combine(catalogs, "lenshh-sumita", "SK16.yml")));
            Assert.True(File.Exists(Path.Combine(catalogs, "lenshh-schott", "N-BK7.yml")));
            Assert.True(File.Exists(Path.Combine(catalogs, "lenshh-model", OptilandGlass.ModelName(1.7847, 25.68, 0.0092) + ".yml")));
            Assert.True(File.Exists(Path.Combine(catalogs, "lenshh-schott", "F2.yml")));
            Assert.True(File.Exists(Path.Combine(catalogs, "mine", "X.yml")));
            Assert.False(File.Exists(Path.Combine(catalogs, "README.txt")));
            Assert.Contains(catalogs, export.Describe());

            // And not, when asked not to.
            Directory.Delete(Path.Combine(catalogs, "lenshh-sumita"), true);
            var local = OptilandWriter.Write(Doublet(), Path.Combine(_dir, "doublet.json"), mgr, install: false);
            Assert.Null(local.InstalledTo);
            Assert.False(Directory.Exists(Path.Combine(catalogs, "lenshh-sumita")));
        }

        [Fact]
        public void AFailedInstallLeavesTheExport()
        {
            // A file where the catalogs folder should be: it cannot be created.
            File.WriteAllText(Path.Combine(_dir, "blocked"), "");
            OptilandGlass.UserCatalogsFolderOverride = Path.Combine(_dir, "blocked", "catalogs");

            var export = OptilandWriter.Write(Doublet(), Path.Combine(_dir, "doublet.json"), Catalogs());

            Assert.Null(export.InstalledTo);
            Assert.NotNull(export.InstallError);
            Assert.True(File.Exists(Path.Combine(_dir, "doublet.json")));
            Assert.True(Directory.Exists(export.GlassFolder));
            Assert.Contains("by hand", export.Describe());
        }

        [Fact]
        public void AGlassFolderNoLongerNeededIsRemoved()
        {
            var mgr = Catalogs();
            string path = Path.Combine(_dir, "doublet.json");
            OptilandWriter.Write(Doublet(), path, mgr);
            var air = Doublet();
            foreach (var s in air.Surfaces) { s.Material = null; s.ModelIndexEnabled = false; }

            Assert.Null(OptilandWriter.Write(air, path, mgr).GlassFolder);
            Assert.False(Directory.Exists(Path.Combine(_dir, "doublet_glass")));
        }

        [Fact]
        public void OptilandsOtherMaterialsAreNotAir()
        {
            string Surface(string material) =>
                "{\"type\": \"Surface\", \"geometry\": {\"type\": \"StandardGeometry\", \"cs\": {\"z\": 0.0}, \"radius\": 50.0, \"conic\": 0.0}, " +
                "\"material_post\": " + material + ", \"is_stop\": false}";
            string json = "{\"aperture\": {\"type\": \"EPD\", \"value\": 10.0}, \"surface_group\": {\"surfaces\": [" +
                "{\"type\": \"ObjectSurface\", \"geometry\": {\"type\": \"Plane\", \"cs\": {\"z\": -Infinity}, \"radius\": Infinity}, \"material_post\": {\"type\": \"IdealMaterial\", \"index\": 1.0}}, " +
                Surface("{\"type\": \"AbbeMaterial\", \"index\": 1.6, \"abbe\": 40.0}") + ", " +
                Surface("{\"type\": \"IdealMaterial\", \"index\": 1.45, \"absorp\": 0.0}") + ", " +
                Surface("{\"type\": \"MaterialFile\", \"filename\": \"C:\\\\Users\\\\x\\\\.optiland\\\\catalogs\\\\ohara\\\\S-LAH55V.yml\"}") + ", " +
                Surface("{\"type\": \"IdealMaterial\", \"index\": 1.0}") + "]}}";
            string path = Path.Combine(_dir, "other.json");
            File.WriteAllText(path, json);

            var sys = OptilandReader.Read(path, null, out var notes);

            Assert.True(sys.Surfaces[1].ModelIndexEnabled);
            Assert.Equal(1.6, sys.Surfaces[1].ModelNd);
            Assert.Equal(40.0, sys.Surfaces[1].ModelVd);
            Assert.True(sys.Surfaces[2].ModelIndexEnabled);
            Assert.Equal(1.45, ModelGlass.Index(0.45, sys.Surfaces[2].ModelNd, sys.Surfaces[2].ModelVd, 0.0));
            Assert.Equal("S-LAH55V", sys.Surfaces[3].Material);
            Assert.Contains("OHARA", sys.GlassCatalogs);
            Assert.Equal(2, notes.Count);
        }

        private static string Surface(int z, string material) =>
            "{\"type\": \"Surface\", \"geometry\": {\"type\": \"StandardGeometry\", \"cs\": {\"z\": " + z + "}, \"radius\": 50.0, \"conic\": 0.0}, " +
            "\"material_post\": " + material + ", \"is_stop\": false}";

        private const string Air = "{\"type\": \"IdealMaterial\", \"index\": 1.0}";

        // A finite object, laid out as Optiland writes one: the object at z = -d, the first surface at
        // 0, an object-space NA. Every object used to be read as at infinity, and objectNA as an EPD of
        // the same number.
        [Fact]
        public void AFiniteObjectAndAnObjectNaAreReadAsOptilandWritesThem()
        {
            string json = "{\"aperture\": {\"type\": \"objectNA\", \"value\": 0.05}, \"surface_group\": {\"surfaces\": [" +
                "{\"type\": \"ObjectSurface\", \"geometry\": {\"type\": \"Plane\", \"cs\": {\"z\": -200.0}, \"radius\": Infinity}, \"material_post\": " + Air + "}, " +
                Surface(0, Air) + ", " + Surface(5, Air) + ", " + Surface(85, Air) + "]}}";
            string path = Path.Combine(_dir, "finite.json");
            File.WriteAllText(path, json);

            var sys = OptilandReader.Read(path, Catalogs(), out _);

            Assert.Equal(200.0, sys.Surfaces[0].Thickness, 12);
            Assert.Equal(ApertureType.ObjectSpaceNA, sys.Aperture.Type);
            Assert.Equal(0.05, sys.Aperture.Value, 12);
        }

        // Written out, an object-space NA is Optiland's objectNA, and a finite object keeps its
        // distance; read back, both come home. (The NA went out as an EPD of the same number, and the
        // object came back at infinity.)
        [Fact]
        public void AnObjectNaAndAFiniteObjectGoOutAndComeBack()
        {
            var sys = Doublet();
            sys.Surfaces[0].Thickness = 200.0;
            sys.FieldType = FieldType.ObjectHeight;
            sys.Aperture = new Aperture(ApertureType.ObjectSpaceNA, 0.05);
            sys.Fields.Clear();
            sys.Fields.Add(new Field(0.0, 1.0));
            sys.Fields.Add(new Field(3.0, 1.0));
            string path = Path.Combine(_dir, "finite_out.json");
            var mgr = Catalogs();
            OptilandWriter.Write(sys, path, mgr);
            string json = File.ReadAllText(path);
            Assert.Contains("\"type\": \"objectNA\"", json);

            var back = OptilandReader.Read(path, mgr, out _);
            Assert.Equal(ApertureType.ObjectSpaceNA, back.Aperture.Type);
            Assert.Equal(0.05, back.Aperture.Value, 12);
            Assert.Equal(200.0, back.Surfaces[0].Thickness, 9);
            Assert.Equal(3.0, back.Fields[1].Y, 12);
        }

        // And an object at infinity, as Optiland writes it (z = -inf), stays there.
        [Fact]
        public void AnObjectAtInfinityStaysThere()
        {
            string json = "{\"aperture\": {\"type\": \"EPD\", \"value\": 10.0}, \"surface_group\": {\"surfaces\": [" +
                "{\"type\": \"ObjectSurface\", \"geometry\": {\"type\": \"Plane\", \"cs\": {\"z\": -Infinity}, \"radius\": Infinity}, \"material_post\": " + Air + "}, " +
                Surface(0, Air) + ", " + Surface(5, Air) + "]}}";
            string path = Path.Combine(_dir, "infinite.json");
            File.WriteAllText(path, json);

            Assert.True(double.IsPositiveInfinity(OptilandReader.Read(path, Catalogs(), out _).Surfaces[0].Thickness));
        }
    }
}
