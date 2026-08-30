using System.IO;
using System.Text;
using LensHH.Core.IO;
using Xunit;

namespace LensHH.API.Tests
{
    // Code V (.seq) glass names carry their catalog as a NAME_CATALOG suffix.
    //
    // A bare name is ambiguous: which physical glass it denotes depends on which
    // catalog the reader happens to consult, and legacy SK16 and modern N-SK16 are
    // different glasses that different catalogs both answer to. Schott "N-" glasses
    // are additionally written without the dash, and the catalog is the cue a reader
    // uses to restore it — so NBK7_SCHOTT resolves while a bare NBK7 resolves nowhere.
    public class CodeVGlassCatalogTests
    {
        private static string WriteTemp(string text, string ext)
        {
            var path = Path.GetTempFileName() + ext;
            File.WriteAllText(path, text, Encoding.UTF8);
            return path;
        }

        private const string ZmxTemplate = @"TITL Catalog Test
GCAT {0}
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
  GLAS {1}
SURF 2
  TYPE STANDARD
  CURV -0.006
  DISZ 6
SURF 3
  TYPE STANDARD
  CURV 0
  DISZ 0
";

        private static string ExportCodeV(string catalog, string glass)
        {
            var zmxPath = WriteTemp(string.Format(ZmxTemplate, catalog, glass), ".zmx");
            var seqPath = Path.GetTempFileName() + ".seq";
            try
            {
                CodeVWriter.Write(ZmxReader.Read(zmxPath), seqPath);
                return File.ReadAllText(seqPath);
            }
            finally
            {
                File.Delete(zmxPath);
                if (File.Exists(seqPath)) File.Delete(seqPath);
            }
        }

        [Fact]
        public void Export_VendorCatalogGlass_QualifiesTheName()
        {
            Assert.Contains("SK16_SCHOTT", ExportCodeV("SCHOTT", "SK16"));
        }

        [Fact]
        public void Export_SchottNPrefixGlass_DropsDashAndQualifies()
        {
            // The dash-stripped form is only resolvable with the catalog attached.
            var seq = ExportCodeV("SCHOTT", "N-BK7");
            Assert.Contains("NBK7_SCHOTT", seq);
            Assert.DoesNotContain("N-BK7", seq);
        }

        [Fact]
        public void Export_NonVendorCatalog_LeavesNameBare()
        {
            // MISC is a ZEMAX catalog, not one Code V ships. Qualifying with it would
            // name a catalog no reader could resolve, so the name is left bare.
            var seq = ExportCodeV("MISC", "POLYSTYR");
            Assert.Contains("POLYSTYR", seq);
            Assert.DoesNotContain("POLYSTYR_MISC", seq);
        }

        private const string SeqTemplate = @"RDM;LEN
DIM M
EPD 10
TIT 'Catalog Test'
WL   587.6000
WTW   1
REF 1
XAN    0.00000
YAN   0.00000
WTF   100
SO 0 1E+20 AIR
S 21.2765 2 {0}
S -166.6667 6 AIR
SI 0 0 AIR
GO
";

        private static LensHH.Core.Models.OpticalSystem ImportCodeV(string glass)
        {
            var path = WriteTemp(string.Format(SeqTemplate, glass), ".seq");
            try { return CodeVReader.Read(path); }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Import_QualifiedGlass_RecordsCatalogAndRestoresDash()
        {
            var sys = ImportCodeV("NBK7_SCHOTT");
            Assert.Equal("N-BK7", sys.Surfaces[1].Material);
            Assert.Contains("SCHOTT", sys.GlassCatalogs);
        }

        [Fact]
        public void Import_QualifiedGlass_KeepsLegacyNameUnchanged()
        {
            var sys = ImportCodeV("SK16_SCHOTT");
            Assert.Equal("SK16", sys.Surfaces[1].Material);
            Assert.Contains("SCHOTT", sys.GlassCatalogs);
        }

        [Fact]
        public void Import_UnderscoreInGlassName_IsNotTreatedAsACatalog()
        {
            // The MoldStress extension generates MS_PMMA / MS_POLYSTYR. Splitting on
            // the first underscore would invent the glass "MS" in a catalog "PMMA".
            var sys = ImportCodeV("MS_PMMA");
            Assert.Equal("MS_PMMA", sys.Surfaces[1].Material);
            Assert.Empty(sys.GlassCatalogs);
        }

        [Fact]
        public void RoundTrip_PreservesGlassNameAndCatalog()
        {
            var zmxPath = WriteTemp(string.Format(ZmxTemplate, "SCHOTT", "N-BK7"), ".zmx");
            var seqPath = Path.GetTempFileName() + ".seq";
            try
            {
                var original = ZmxReader.Read(zmxPath);
                CodeVWriter.Write(original, seqPath);
                var reloaded = CodeVReader.Read(seqPath);

                Assert.Equal(original.Surfaces[1].Material, reloaded.Surfaces[1].Material);
                Assert.Contains("SCHOTT", reloaded.GlassCatalogs);
            }
            finally
            {
                File.Delete(zmxPath);
                if (File.Exists(seqPath)) File.Delete(seqPath);
            }
        }
    }
}
