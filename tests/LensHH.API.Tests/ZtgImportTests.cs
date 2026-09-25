using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LensHH.Core.Enums;
using LensHH.Core.Glass;
using LensHH.Core.IO;
using LensHH.Core.Models;
using Xunit;

namespace LensHH.API.Tests
{
    // OpticStudio table glasses: a .zmx names one as GLAS NAME.ZTG and the index table sits in a
    // separate .ZTG file. Before 1.0.158 the name was kept as a glass name nothing could resolve.
    // A long table is now fitted with the Schott formula and added to the user's TABLE catalog;
    // a short one becomes a model glass (a Conrady curve, exact through three points).
    [Collection("UserGlassCatalog")]   // the tests share UserGlassCatalog.FolderOverride
    public class ZtgImportTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "ztg_" + Guid.NewGuid().ToString("N"));

        public ZtgImportTests()
        {
            Directory.CreateDirectory(_dir);
            UserGlassCatalog.FolderOverride = Path.Combine(_dir, "userglass");
        }

        public void Dispose()
        {
            UserGlassCatalog.FolderOverride = null;
            try { Directory.Delete(_dir, true); } catch { }
        }

        // A singlet whose first surface's glass is the table glass <name>.ZTG.
        private string LensWithTableGlass(string ztgName)
        {
            var sys = new OpticalSystem { Aperture = new Aperture(ApertureType.EPD, 10.0) };
            sys.Wavelengths.Add(new Wavelength(0.55, 1.0, true));
            sys.Fields.Add(new Field { Y = 0 });
            sys.Surfaces.Add(new Surface { Index = 0, Thickness = double.PositiveInfinity });
            sys.Surfaces.Add(new Surface { Index = 1, Radius = 50, Thickness = 5, Material = "N-BK7", IsStop = true });
            sys.Surfaces.Add(new Surface { Index = 2, Radius = -50, Thickness = 45 });
            sys.Surfaces.Add(new Surface { Index = 3 });
            string path = Path.Combine(_dir, "lens.zmx");
            ZmxWriter.Write(sys, path);
            var text = File.ReadAllText(path);
            // As OpticStudio's Code V converter writes a table glass.
            text = System.Text.RegularExpressions.Regex.Replace(text, @"GLAS N-BK7[^\r\n]*", $"GLAS {ztgName} 0 0 1.5 40 0 0 0 0 0 0");
            File.WriteAllText(path, text);
            return path;
        }

        private void WriteZtg(string name, (double um, double n)[] table, bool utf16 = true)
        {
            var sb = new StringBuilder("! Code V Private Glass Catalog Data\r\n");
            foreach (var (um, n) in table)
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0:F16} {1:R}\r\n", um, n));
            File.WriteAllText(Path.Combine(_dir, name), sb.ToString(), utf16 ? Encoding.Unicode : Encoding.ASCII);
        }

        [Fact]
        public void AThreePointTableOfAModelGlassComesBackAsThatModelGlass()
        {
            // The glass of C:\GIT\OSLO_DEBUG\CookeTripletModelIndexFirstGlass: a model glass with a
            // partial-dispersion offset, exported to Code V as a private glass, which OpticStudio's
            // converter turned into a three-point LHHMODEL1.ZTG.
            const double nd = 1.680409965080894, vd = 60.32364915667316, dPgF = -0.001046599903552936;
            WriteZtg("LHHMODEL1.ZTG", new[] { 0.65, 0.55, 0.48 }.Select(l => (l, ModelGlass.Index(l, nd, vd, dPgF))).ToArray());
            var sys = ZmxReader.Read(LensWithTableGlass("LHHMODEL1.ZTG"), new GlassCatalogManager(), out var notes);

            var s = sys.Surfaces[1];
            Assert.True(s.ModelIndexEnabled);
            Assert.Equal(nd, s.ModelNd, 9);
            Assert.Equal(vd, s.ModelVd, 6);
            Assert.Equal(dPgF, s.ModelDPgF, 9);
            Assert.Contains("passes through every point", Assert.Single(notes));
        }

        [Fact]
        public void ALongTableBecomesACatalogGlassThatTracesItsIndices()
        {
            // BK7 every 4 nm from 0.40 to 0.80 µm, as OpticStudio's own TABLETEST.ZTG sample.
            var mgr = new GlassCatalogManager();
            var bk7 = new GlassData { Name = "BK7", DispersionFormula = 2,
                Coefficients = new[] { 1.03961212, 0.00600069867, 0.231792344, 0.0200179144, 1.01046945, 103.560653 } };
            var table = Enumerable.Range(0, 101).Select(k => 0.40 + 0.004 * k).Select(l => (l, bk7.GetIndex(l))).ToArray();
            WriteZtg("TABLETEST.ZTG", table, utf16: false);

            var sys = ZmxReader.Read(LensWithTableGlass("TABLETEST.ZTG"), mgr, out var notes);

            Assert.Equal("TABLETEST", sys.Surfaces[1].Material);
            Assert.Contains(UserGlassCatalog.TableCatalog, sys.GlassCatalogs);
            Assert.True(File.Exists(UserGlassCatalog.TableCatalogPath));
            Assert.Contains("added to your TABLE catalog", Assert.Single(notes));

            // The glass traces with the table's index, between the table's points too.
            foreach (var l in new[] { 0.402, 0.4861, 0.55, 0.6563, 0.798 })
            {
                sys.Wavelengths[0] = new Wavelength(l, 1.0, true);
                Assert.Equal(bk7.GetIndex(l), mgr.BuildRefractiveIndexArray(sys, l)[1], 5);
            }

            // A later session finds it again from the user folder alone.
            var fresh = new GlassCatalogManager();
            UserGlassCatalog.LoadInto(fresh);
            Assert.NotNull(fresh.GetGlass("TABLETEST", new[] { "TABLE" }));

            // Converting the same table again replaces the glass rather than adding a second one.
            ZmxReader.Read(LensWithTableGlass("TABLETEST.ZTG"), mgr, out _);
            Assert.Single(File.ReadAllLines(UserGlassCatalog.TableCatalogPath), l => l.StartsWith("NM TABLETEST "));
        }

        [Fact]
        public void AMissingTableIsReportedByName()
        {
            var sys = ZmxReader.Read(LensWithTableGlass("NOSUCH.ZTG"), new GlassCatalogManager(), out var notes);
            Assert.Equal("NOSUCH.ZTG", sys.Surfaces[1].Material);
            Assert.Contains("NOSUCH.ZTG not found", Assert.Single(notes));
        }

        [Fact]
        public void AGlassFromTheLenssOwnCatalogIsBroughtIn()
        {
            // As OpticStudio's Code V converter writes a formula-defined private glass: into a
            // catalog of its own, named on the lens's GCAT line.
            File.WriteAllText(Path.Combine(_dir, "CODEV_CONVERTED.AGF"),
                "CC Code V Private Glass Catalog Data\r\n" +
                "NM PRVGLASS 2 0 1.5168 64.17 0 0 0\r\nGC\r\nED 0 0 1 0 0\r\n" +
                "CD 1.03961212 0.00600069867 0.231792344 0.0200179144 1.01046945 103.560653 0 0 0 0\r\n" +
                "TD 0 0 0 0 0 0 20\r\nOD -1 -1 -1 -1 -1 -1\r\nLD 0.3 2.5\r\n" +
                "NM UNUSED 2 0 1.6 40 0 0 0\r\nCD 1.2 0.007 0.2 0.02 1.0 100 0 0 0 0\r\nLD 0.3 2.5\r\n");
            string lens = LensWithTableGlass("PRVGLASS");
            var text = File.ReadAllText(lens);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"(?m)^GCAT[^\r\n]*\r?\n", "");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"(?m)^(MODE SEQ\r?\n)", "$1GCAT CODEV_CONVERTED\r\n");
            File.WriteAllText(lens, text);
            Assert.Contains("GCAT CODEV_CONVERTED", File.ReadAllText(lens));
            var mgr = new GlassCatalogManager();

            var sys = ZmxReader.Read(lens, mgr, out var notes);

            Assert.Equal("PRVGLASS", sys.Surfaces[1].Material);
            Assert.Contains("CODEV_CONVERTED", mgr.LoadedCatalogs);
            Assert.Equal(1.5168, mgr.BuildRefractiveIndexArray(sys, 0.5875618)[1], 4);
            Assert.Contains(notes, n => n.StartsWith("Glass catalog CODEV_CONVERTED") && n.Contains("for PRVGLASS"));
            Assert.True(File.Exists(Path.Combine(UserGlassCatalog.Folder, "CODEV_CONVERTED.AGF")));   // kept for next time

            // A lens that needs nothing from its catalogs brings nothing in.
            var mgr2 = new GlassCatalogManager();
            mgr2.LoadCatalog(Path.Combine(UserGlassCatalog.Folder, "CODEV_CONVERTED.AGF"));
            ZmxReader.Read(lens, mgr2, out var notes2);
            Assert.Empty(notes2);
        }

        [Fact]
        public void TheSchottFitFollowsRealGlass()
        {
            var bk7 = new GlassData { Name = "BK7", DispersionFormula = 2,
                Coefficients = new[] { 1.03961212, 0.00600069867, 0.231792344, 0.0200179144, 1.01046945, 103.560653 } };
            var table = Enumerable.Range(0, 11).Select(k => 0.40 + 0.04 * k).Select(l => (l, bk7.GetIndex(l))).ToList();
            var (_, err) = TableGlass.FitSchott(table);
            Assert.True(err < 2e-6, $"Schott fit error {err}");
            Assert.Throws<ArgumentException>(() => TableGlass.FitSchott(table.Take(5).ToList()));
        }
    }
}
