using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LensHH.Core.Glass;

namespace LensHH.Core.IO
{
    /// <summary>
    /// The user's own glass catalog: <c>Documents\LensHH-LT\Glass\TABLE.AGF</c>. The shipped
    /// catalogs sit in the install folder, which a user cannot write to; glasses LensHH-LT makes
    /// for the user — a table glass read from a <c>.ZTG</c> file — go here instead, and every
    /// session loads the folder after the shipped catalogs, so a lens saved with one opens with
    /// it again. The file is a standard AGF, which OpticStudio reads as well.
    /// </summary>
    public static class UserGlassCatalog
    {
        /// <summary>The catalog table glasses are written to.</summary>
        public const string TableCatalog = "TABLE";

        /// <summary>For tests: a folder to use instead of the Documents one.</summary>
        public static string? FolderOverride { get; set; }

        /// <summary>Documents\LensHH-LT\Glass (or the override).</summary>
        public static string Folder => FolderOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LensHH-LT", "Glass");

        public static string TableCatalogPath => Path.Combine(Folder, TableCatalog + ".AGF");

        /// <summary>Load the user's catalogs, if there are any, into <paramref name="mgr"/>.</summary>
        public static void LoadInto(GlassCatalogManager mgr)
        {
            try
            {
                if (Directory.Exists(Folder))
                    mgr.LoadCatalogsFromFolder(Folder);
            }
            catch (IOException) { }                  // an unreadable user folder must not stop the app
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>
        /// Write a glass given by the Schott formula into the TABLE catalog, replacing one of the
        /// same name, and load the catalog into <paramref name="mgr"/> when there is one.
        /// </summary>
        public static void AddSchottGlass(string name, double[] coefficients, double wlMinUm, double wlMaxUm,
                                          string source, GlassCatalogManager? mgr)
        {
            var inv = CultureInfo.InvariantCulture;
            double nd = TableGlass.Schott(coefficients, ModelGlass.LambdaD);
            double nF = TableGlass.Schott(coefficients, ModelGlass.LambdaF);
            double nC = TableGlass.Schott(coefficients, ModelGlass.LambdaC);
            double vd = Math.Abs(nF - nC) > 1e-12 ? (nd - 1) / (nF - nC) : 0;

            var block = new List<string>
            {
                // NM name formula MIL nd vd exclude status meltfreq  (formula 1 = Schott)
                string.Format(inv, "NM {0} 1 0 {1:F6} {2:F4} 0 0 0", name, nd, vd),
                "GC " + source,
                "ED 0 0 0 0 0",
                "CD " + string.Join(" ", coefficients.Select(c => c.ToString("E14", inv))),
                "TD 0 0 0 0 0 0 20",
                "OD -1 -1 -1 -1 -1 -1",
                string.Format(inv, "LD {0:F6} {1:F6}", wlMinUm, wlMaxUm),
            };

            Directory.CreateDirectory(Folder);
            var lines = File.Exists(TableCatalogPath) ? File.ReadAllLines(TableCatalogPath).ToList()
                                                      : new List<string> { "CC LensHH-LT table glasses (converted from OpticStudio .ZTG files)" };
            RemoveGlass(lines, name);
            lines.AddRange(block);
            File.WriteAllLines(TableCatalogPath, lines, new UTF8Encoding(false));

            mgr?.LoadCatalog(TableCatalogPath);
        }

        // Drop an existing NM block of this name: its NM line and every line up to the next NM.
        private static void RemoveGlass(List<string> lines, string name)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                var p = lines[i].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 2 || p[0] != "NM" || !p[1].Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                int end = i + 1;
                while (end < lines.Count && !lines[end].TrimStart().StartsWith("NM ")) end++;
                lines.RemoveRange(i, end - i);
                return;
            }
        }
    }
}
