using System.IO;
using System.Linq;
using LensHH.Core.Enums;
using LensHH.Core.IO;
using LensHH.Core.Models;
using Xunit;

namespace LensHH.API.Tests
{
    // OSLO has no primary-wavelength keyword: the primary IS wavelength 1, the first entry on the
    // WV line (OSLO Program Reference pp. 26, 123). The writer must therefore put the primary
    // first, whatever order the wavelengths are stored in, and the reader must take the first.
    // Before 1.0.158 the writer kept stored order, so an F d C lens with d primary opened in OSLO
    // - and read back here - as an F-line lens.
    public class OsloWavelengthTests
    {
        private static OpticalSystem Singlet(params Wavelength[] wavelengths)
        {
            var sys = new OpticalSystem { Aperture = new Aperture(ApertureType.EPD, 10.0) };
            sys.Surfaces.Add(new Surface { Index = 0, Thickness = double.PositiveInfinity });
            sys.Surfaces.Add(new Surface { Index = 1, Radius = 50.0, Thickness = 5.0, Material = "N-BK7", IsStop = true });
            sys.Surfaces.Add(new Surface { Index = 2, Radius = -50.0, Thickness = 48.0 });
            sys.Surfaces.Add(new Surface { Index = 3, Thickness = 0 }); // image
            foreach (var w in wavelengths) sys.Wavelengths.Add(w);
            sys.Fields.Add(new Field { Y = 0 });
            return sys;
        }

        private static string Line(string text, string keyword) =>
            text.Split('\n').Select(l => l.Trim()).First(l => l.StartsWith(keyword + " "));

        [Fact]
        public void TheWriterPutsThePrimaryFirstAndTheRestShortToLong()
        {
            // Stored short to long with d primary, and a distinct weight on each so a weight
            // left behind in the old order would show.
            var sys = Singlet(new Wavelength(0.48613, 2.0, false),
                              new Wavelength(0.58756, 3.0, true),
                              new Wavelength(0.65627, 5.0, false));
            var path = Path.GetTempFileName() + ".len";
            try
            {
                OsloWriter.Write(sys, path);
                string text = File.ReadAllText(path);
                Assert.Equal("WV  0.58756 0.48613 0.65627", Line(text, "WV"));
                Assert.Equal("WW  3 2 5", Line(text, "WW"));

                var back = OsloReader.Read(path);
                Assert.Equal(0.58756, back.Wavelengths[back.PrimaryWavelengthIndex].Value, 5);
                Assert.Equal(3.0, back.Wavelengths[back.PrimaryWavelengthIndex].Weight);
                Assert.Equal(new[] { 2.0, 3.0, 5.0 },
                             back.Wavelengths.OrderBy(w => w.Value).Select(w => w.Weight).ToArray());
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void AnyStoredOrderGivesTheSameFile()
        {
            var a = Singlet(new Wavelength(0.48613, 1.0, false), new Wavelength(0.58756, 1.0, true),
                            new Wavelength(0.65627, 1.0, false));
            var b = Singlet(new Wavelength(0.65627, 1.0, false), new Wavelength(0.48613, 1.0, false),
                            new Wavelength(0.58756, 1.0, true));
            var pa = Path.GetTempFileName() + ".len";
            var pb = Path.GetTempFileName() + ".len";
            try
            {
                OsloWriter.Write(a, pa);
                OsloWriter.Write(b, pb);
                Assert.Equal(Line(File.ReadAllText(pa), "WV"), Line(File.ReadAllText(pb), "WV"));
            }
            finally { File.Delete(pa); File.Delete(pb); }
        }

        [Fact]
        public void TheReaderTakesTheFirstAsPrimary()
        {
            // A file written F d C is an F-line lens in OSLO, and must be one here.
            var sys = Singlet(new Wavelength(0.58756, 1.0, true));
            var path = Path.GetTempFileName() + ".len";
            try
            {
                OsloWriter.Write(sys, path);
                string text = File.ReadAllText(path);
                File.WriteAllText(path, text.Replace(Line(text, "WV"), "WV  0.48613 0.58756 0.65627")
                                            .Replace(Line(text, "WW"), "WW  1 1 1"));
                var back = OsloReader.Read(path);
                Assert.Equal(3, back.Wavelengths.Count);
                Assert.Equal(0.48613, back.Wavelengths[back.PrimaryWavelengthIndex].Value, 5);
            }
            finally { File.Delete(path); }
        }
    }
}
