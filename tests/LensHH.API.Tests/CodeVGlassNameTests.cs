using System.IO;
using LensHH.Core.Enums;
using LensHH.Core.Glass;
using LensHH.Core.IO;
using LensHH.Core.Models;
using Xunit;

namespace LensHH.API.Tests
{
    // Code V writes glass names without punctuation, for every vendor: the
    // catalog names N-BK7, S-FPL51 and H-ZF52 are NBK7, SFPL51 and HZF52 there.
    //
    // Export used to strip the dash only for the Schott N-prefix, so 639 of the
    // 754 punctuated names in catalogs/Glass went out in a spelling Code V does
    // not accept. Import guessed the punctuation back from the spelling, which
    // no string rule can do: Hoya NBFD10 is a real catalog name and the guess
    // turned it into N-BFD10, which is not.
    public class CodeVGlassNameTests
    {
        /// <summary>catalogs/Glass walking up from the test binary, or null if not found.</summary>
        private static string? FindCatalogsGlassFolder()
        {
            var dir = System.AppContext.BaseDirectory;
            for (int i = 0; i < 12 && dir != null; i++, dir = Path.GetDirectoryName(dir))
            {
                var cat = Path.Combine(dir, "catalogs", "Glass");
                if (Directory.Exists(cat)) return cat;
            }
            return null;
        }

        private static GlassCatalogManager? LoadCatalogs()
        {
            var folder = FindCatalogsGlassFolder();
            if (folder == null) return null;
            var mgr = new GlassCatalogManager();
            mgr.LoadCatalogsFromFolder(folder);
            return mgr;
        }

        private static OpticalSystem SystemWith(params string[] materials)
        {
            var sys = new OpticalSystem { Aperture = new Aperture(ApertureType.EPD, 10.0) };
            sys.Wavelengths.Add(new Wavelength(0.5876, 1.0, true));
            sys.Surfaces.Add(new Surface { Index = 0, Thickness = double.PositiveInfinity });
            for (int i = 0; i < materials.Length; i++)
            {
                sys.Surfaces.Add(new Surface { Index = 2 * i + 1, Curvature = 0.02, Thickness = 4.0, Material = materials[i] });
                sys.Surfaces.Add(new Surface { Index = 2 * i + 2, Curvature = -0.02, Thickness = 3.0 });
            }
            sys.Surfaces.Add(new Surface { Index = 2 * materials.Length + 1 });
            return sys;
        }

        // A minimal .seq in the shape CodeVWriter emits, so import can be tested
        // on material spellings the writer would never produce (catalog-qualified
        // names, glasses absent from our catalogs).
        private static string SeqWithMaterial(string material) =>
            "! test\nRDM;LEN\nDIM M\nEPD 10\nWL   587.6000\nWTW   1\nREF 1\n" +
            "SO 0 1e18 AIR\n" +
            "S 50 4 " + material + "\n" +
            "S -50 3 AIR\n" +
            "SI 0 0\nGO\n";

        private static OpticalSystem ReadSeq(string text, GlassCatalogManager? mgr)
        {
            var path = Path.GetTempFileName() + ".seq";
            try { File.WriteAllText(path, text); return CodeVReader.Read(path, mgr); }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Export_StripsPunctuation_ForEveryVendorNotJustSchott()
        {
            var sys = SystemWith("N-BK7", "S-FPL51", "H-ZF52", "HPFS_7980");
            var path = Path.GetTempFileName() + ".seq";
            try
            {
                CodeVWriter.Write(sys, path);
                var text = File.ReadAllText(path);

                Assert.Contains("NBK7", text);
                Assert.Contains("SFPL51", text);
                Assert.Contains("HZF52", text);
                Assert.Contains("HPFS7980", text);

                // The punctuated spellings must not survive. The underscore
                // matters most: Code V reads it as the GLASS_CATALOG separator,
                // so HPFS_7980 would be taken as glass HPFS from catalog 7980.
                Assert.DoesNotContain("S-FPL51", text);
                Assert.DoesNotContain("H-ZF52", text);
                Assert.DoesNotContain("HPFS_7980", text);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Import_HoyaNBFD10_IsLeftAlone()
        {
            var mgr = LoadCatalogs();
            if (mgr == null) return;   // no catalogs beside the test binary

            // The regression: NBFD10 is a real Hoya name, not a de-punctuated
            // N-prefix glass. The old rule rewrote it to N-BFD10, which no
            // catalog contains, and the surface lost its glass.
            var sys = ReadSeq(SeqWithMaterial("NBFD10"), mgr);
            Assert.Equal("NBFD10", sys.Surfaces[1].Material);
            Assert.NotNull(mgr.GetGlass(sys.Surfaces[1].Material!));
        }

        [Fact]
        public void Import_NBK7_RecoversTheDashedCatalogName()
        {
            var mgr = LoadCatalogs();
            if (mgr == null) return;

            var sys = ReadSeq(SeqWithMaterial("NBK7"), mgr);
            Assert.Equal("N-BK7", sys.Surfaces[1].Material);
        }

        [Theory]
        [InlineData("SFPL51", "S-FPL51")]
        [InlineData("HZF52", "H-ZF52")]
        [InlineData("NSF10", "N-SF10")]
        public void Import_RecoversPunctuation_AcrossVendors(string codeV, string expected)
        {
            var mgr = LoadCatalogs();
            if (mgr == null) return;

            var sys = ReadSeq(SeqWithMaterial(codeV), mgr);
            Assert.Equal(expected, sys.Surfaces[1].Material);
        }

        [Fact]
        public void RoundTrip_PunctuatedNames_ComeBackUnchanged()
        {
            var mgr = LoadCatalogs();
            if (mgr == null) return;

            var sys = SystemWith("N-BK7", "S-FPL51", "H-ZF52", "NBFD10");
            var path = Path.GetTempFileName() + ".seq";
            try
            {
                CodeVWriter.Write(sys, path);
                var back = CodeVReader.Read(path, mgr);

                Assert.Equal("N-BK7", back.Surfaces[1].Material);
                Assert.Equal("S-FPL51", back.Surfaces[3].Material);
                Assert.Equal("H-ZF52", back.Surfaces[5].Material);
                Assert.Equal("NBFD10", back.Surfaces[7].Material);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Import_CatalogQualifier_SeparatesTheCollidingNames()
        {
            var mgr = LoadCatalogs();
            if (mgr == null) return;

            // Sumita P-SK50 and Schott PSK50 are the only two glasses in the
            // shipped catalogs that strip to the same Code V name. The
            // GLASS_CATALOG qualifier is what tells them apart.
            var sumita = ReadSeq(SeqWithMaterial("PSK50_SUMITA"), mgr);
            Assert.Equal("P-SK50", sumita.Surfaces[1].Material);

            var schott = ReadSeq(SeqWithMaterial("PSK50_SCHOTT"), mgr);
            Assert.Equal("PSK50", schott.Surfaces[1].Material);

            // Unqualified, the name as literally spelled wins.
            var bare = ReadSeq(SeqWithMaterial("PSK50"), mgr);
            Assert.Equal("PSK50", bare.Surfaces[1].Material);
        }

        [Fact]
        public void Export_QualifiesOnlyTheCollidingNames()
        {
            var mgr = LoadCatalogs();
            if (mgr == null) return;

            var sys = SystemWith("P-SK50", "N-BK7", "S-FPL51");
            var path = Path.GetTempFileName() + ".seq";
            try
            {
                CodeVWriter.Write(sys, path, mgr);
                var text = File.ReadAllText(path);

                // Sumita P-SK50 collides with Schott PSK50 once punctuation is
                // gone, so it carries its catalog.
                Assert.Contains("PSK50_SUMITA", text);

                // Everything else stays bare: the qualifier is not a licence to
                // decorate names that were never ambiguous.
                Assert.Contains("NBK7", text);
                Assert.Contains("SFPL51", text);
                Assert.DoesNotContain("NBK7_", text);
                Assert.DoesNotContain("SFPL51_", text);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void RoundTrip_CollidingName_ReturnsToItsOwnVendor()
        {
            var mgr = LoadCatalogs();
            if (mgr == null) return;

            // Sumita P-SK50 used to come back as Schott PSK50: a different
            // glass, silently substituted by a round trip.
            var sys = SystemWith("P-SK50");
            var path = Path.GetTempFileName() + ".seq";
            try
            {
                CodeVWriter.Write(sys, path, mgr);
                var back = CodeVReader.Read(path, mgr);
                Assert.Equal("P-SK50", back.Surfaces[1].Material);

                var glass = mgr.GetGlass(back.Surfaces[1].Material!);
                Assert.NotNull(glass);
                Assert.Equal("SUMITA", glass!.Catalog, ignoreCase: true);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Export_NeverNamesACatalogCodeVDoesNotHave()
        {
            var mgr = LoadCatalogs();
            if (mgr == null) return;

            // Corning fused silica is ours, not Code V's. Whatever we do about
            // ambiguity, we must not write _CORNING_FS as a catalog: Code V
            // cannot resolve it, which is worse than the ambiguity.
            var sys = SystemWith("HPFS_7980");
            var path = Path.GetTempFileName() + ".seq";
            try
            {
                CodeVWriter.Write(sys, path, mgr);
                var text = File.ReadAllText(path);
                Assert.Contains("HPFS7980", text);
                Assert.DoesNotContain("CORNING", text);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Import_UnknownGlass_IsPassedThroughUnchanged()
        {
            var mgr = LoadCatalogs();
            if (mgr == null) return;

            // Not in any loaded catalog: leave it as the file spelled it rather
            // than inventing punctuation we cannot justify.
            var sys = ReadSeq(SeqWithMaterial("NZZQQ99"), mgr);
            Assert.Equal("NZZQQ99", sys.Surfaces[1].Material);
        }

        [Fact]
        public void Import_WithoutCatalogManager_KeepsTheLegacyNPrefixRule()
        {
            // Documented degradation: with no catalogs to look in, the old
            // assumption is all that is left. Right for Schott, wrong for Hoya.
            var sys = ReadSeq(SeqWithMaterial("NBK7"), null);
            Assert.Equal("N-BK7", sys.Surfaces[1].Material);
        }
    }
}
