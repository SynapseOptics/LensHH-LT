using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using LensHH.Core.Glass;

namespace LensHH.Core.IO
{
    /// <summary>
    /// Glass as Optiland needs it written, and read back.
    ///
    /// <para><b>A bare glass name is not safe in Optiland.</b> Optiland finds a <c>Material</c> by a
    /// fuzzy search over the refractiveindex.info database. Its default policy takes the nearest
    /// name, from any catalog, with only a warning:</para>
    /// <list type="bullet">
    /// <item>Of the glasses in the catalogs LensHH-LT ships, 96 Schott and 146 Sumita names
    /// resolve that way to a different glass.</item>
    /// <item>About 800 names are not in Optiland's database at all.</item>
    /// <item>A model glass has no name Optiland could know.</item>
    /// </list>
    ///
    /// <para>Naming the catalog is not enough either. Optiland also files glasses under groups of
    /// equivalents: its "BK7" group holds Schott's N-BK7, Ohara's S-BSL7, CDGM's H-K9L and
    /// others, and "SF10" and "BAK1" are groups too. So BK7 in Schott's catalog answers to
    /// N-BK7, a different glass.</para>
    ///
    /// <para><b>So each glass is written in two parts.</b></para>
    /// <list type="bullet">
    /// <item>Beside the lens file, the glass's own dispersion data is written as a
    /// refractiveindex.info <c>.yml</c>. It goes in a folder named for its catalog, prefixed
    /// <c>lenshh-</c> (<c>lenshh-schott</c>, <c>lenshh-model</c>). That is Optiland's user-catalog
    /// layout, and the names are ones its own database does not use.</item>
    /// <item>In the lens file, a <c>Material</c> names that catalog and has
    /// <c>match_policy: "strict"</c>. Optiland then uses exactly the index LensHH-LT used, or,
    /// if the catalogs were not installed, stops with an error naming the one it is missing. It
    /// never substitutes another glass.</item>
    /// </list>
    ///
    /// <para>Every AGF dispersion formula has an exact refractiveindex.info equivalent:</para>
    /// <list type="bullet">
    /// <item>the Sellmeier forms as formula 2;</item>
    /// <item>the Schott and Extended polynomials as formula 3;</item>
    /// <item>Sellmeier 2 and the two Handbook of Optics forms as formula 4;</item>
    /// <item>Conrady, and the LensHH-LT model glass (itself a Conrady curve), as formula 5;</item>
    /// <item>Herzberger as formula 7.</item>
    /// </list>
    /// </summary>
    public static class OptilandGlass
    {
        /// <summary>What the catalogs written for Optiland are prefixed with.</summary>
        public const string CatalogPrefix = "lenshh-";

        /// <summary>The catalog model glasses are written to.</summary>
        public const string ModelCatalog = CatalogPrefix + "model";

        private const string ModelPrefix = "MODEL_";

        /// <summary>
        /// A model glass's name: its parameters, so two lenses' model glasses cannot collide in
        /// an installed catalog, and the parameters come back on import.
        /// </summary>
        public static string ModelName(double nd, double vd, double dPgF) =>
            string.Format(CultureInfo.InvariantCulture, "{0}{1:F8}_{2:F6}_{3:F8}", ModelPrefix, nd, vd, dPgF);

        /// <summary>The parameters of a model glass written by <see cref="ModelName"/>.</summary>
        public static bool TryParseModelName(string? name, out double nd, out double vd, out double dPgF)
        {
            nd = vd = dPgF = 0.0;
            if (name == null || !name.StartsWith(ModelPrefix, StringComparison.OrdinalIgnoreCase)) return false;
            var parts = name.Substring(ModelPrefix.Length).Split('_');
            var inv = CultureInfo.InvariantCulture;
            return parts.Length == 3
                && double.TryParse(parts[0], NumberStyles.Float, inv, out nd)
                && double.TryParse(parts[1], NumberStyles.Float, inv, out vd)
                && double.TryParse(parts[2], NumberStyles.Float, inv, out dPgF)
                && nd > 1.0;
        }

        /// <summary>The catalog one of ours is written to for Optiland.</summary>
        public static string DataCatalog(string catalog) => CatalogPrefix + catalog.ToLowerInvariant();

        /// <summary>
        /// Optiland's own name for one of our catalogs, for a glass whose data we do not have. We
        /// split Corning in two (CORNING_B and CORNING_FS); Optiland keeps one <c>corning</c>
        /// catalog.
        /// </summary>
        public static string VendorCatalog(string catalog)
        {
            if (catalog.StartsWith("CORNING", StringComparison.OrdinalIgnoreCase)) return "corning";
            return catalog.ToLowerInvariant();
        }

        /// <summary>Our name for a catalog an Optiland file names: one written by
        /// <see cref="DataCatalog"/> loses its prefix.</summary>
        public static string FromOptilandCatalog(string catalog) =>
            catalog.StartsWith(CatalogPrefix, StringComparison.OrdinalIgnoreCase)
                ? catalog.Substring(CatalogPrefix.Length) : catalog;

        /// <summary>
        /// Whether a glass name can be a file name, which Optiland's user catalogs require:
        /// the file's name is the glass's name.
        /// </summary>
        public static bool IsFileName(string name) =>
            name.Length > 0 && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && name.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) < 0
            && name.Trim() == name && name != "." && name != "..";

        /// <summary>
        /// The refractiveindex.info formula and coefficients that give exactly this glass's
        /// index, or null for a formula that has none (no AGF formula lacks one).
        /// </summary>
        public static (int Formula, double[] Coefficients)? Dispersion(GlassData g)
        {
            double C(int i) => i < g.Coefficients.Length ? g.Coefficients[i] : 0.0;
            switch (g.DispersionFormula)
            {
                case 1: // Schott: n^2 = a0 + a1 L^2 + a2 L^-2 + ... + a5 L^-8
                    return (3, new[] { C(0), C(1), 2, C(2), -2, C(3), -4, C(4), -6, C(5), -8 });
                case 2: // Sellmeier 1
                    return (2, new[] { 0.0, C(0), C(1), C(2), C(3), C(4), C(5) });
                case 3: // Herzberger: the same form, with the same 0.028
                    return (7, new[] { C(0), C(1), C(2), C(3), C(4), C(5) });
                case 4: // Sellmeier 2: 1 + A + B1 L^2/(L^2 - l1^2) + B2/(L^2 - l2^2)
                    return (4, new[] { 1.0 + C(0), C(1), 2, C(2), 2, C(3), 0, C(4), 2 });
                case 5: // Conrady
                    return (5, new[] { C(0), C(1), -1, C(2), -3.5 });
                case 6: // Sellmeier 3
                    return (2, new[] { 0.0, C(0), C(1), C(2), C(3), C(4), C(5), C(6), C(7) });
                case 7: // Handbook of Optics 1: A + B/(L^2 - C) - D L^2
                    return (4, new[] { C(0), C(1), 0, C(2), 1, 0, 0, 0, 1, -C(3), 2 });
                case 8: // Handbook of Optics 2: A + B L^2/(L^2 - C) - D L^2
                    return (4, new[] { C(0), C(1), 2, C(2), 1, 0, 0, 0, 1, -C(3), 2 });
                case 9: // Sellmeier 4: A + B L^2/(L^2 - C) + D L^2/(L^2 - E)
                    return (2, new[] { C(0) - 1.0, C(1), C(2), C(3), C(4) });
                case 10: // Extended: Schott continued to L^-10 and L^-12
                    return (3, new[] { C(0), C(1), 2, C(2), -2, C(3), -4, C(4), -6, C(5), -8, C(6), -10, C(7), -12 });
                case 11: // Sellmeier 5
                    return (2, new[] { 0.0, C(0), C(1), C(2), C(3), C(4), C(5), C(6), C(7), C(8), C(9) });
                case 12: // Extended 2: Schott plus L^4 and L^6
                    return (3, new[] { C(0), C(1), 2, C(2), -2, C(3), -4, C(4), -6, C(5), -8, C(6), 4, C(7), 6 });
                case 13: // Extended 3: a0 + a1 L^2 + a2 L^4 + a3 L^-2 + ... + a8 L^-12
                    return (3, new[] { C(0), C(1), 2, C(2), 4, C(3), -2, C(4), -4, C(5), -6, C(6), -8, C(7), -10, C(8), -12 });
                default:
                    return null;
            }
        }

        /// <summary>
        /// The model glass as refractiveindex.info formula 5: it is a Conrady curve
        /// c0 + c1/L + c2/L^3.5, whose constants are solved here from three of its own indices.
        /// A model glass with Vd 0 is a constant index, and comes out with c1 = c2 = 0.
        /// </summary>
        public static (int Formula, double[] Coefficients) ModelDispersion(double nd, double vd, double dPgF)
        {
            double[] l = { 0.4, 0.6, 1.0 };
            var a = new double[3, 3];
            var b = new double[3];
            for (int i = 0; i < 3; i++)
            {
                a[i, 0] = 1.0;
                a[i, 1] = 1.0 / l[i];
                a[i, 2] = Math.Pow(l[i], -3.5);
                b[i] = ModelGlass.Index(l[i], nd, vd, dPgF);
            }
            var c = Solve3(a, b);
            return (5, new[] { c[0], c[1], -1, c[2], -3.5 });
        }

        private static double[] Solve3(double[,] a, double[] b)
        {
            double Det(double[,] m) =>
                m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1])
              - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0])
              + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
            double d = Det(a);
            var x = new double[3];
            for (int k = 0; k < 3; k++)
            {
                var m = (double[,])a.Clone();
                for (int i = 0; i < 3; i++) m[i, k] = b[i];
                x[k] = Det(m) / d;
            }
            return x;
        }

        /// <summary>A refractiveindex.info material file for one dispersion.</summary>
        public static string Yml(string description, int formula, double[] coefficients,
                                 double lambdaMin, double lambdaMax)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("REFERENCES: \"").Append(description.Replace("\"", "'")).Append("\"\n");
            sb.Append("DATA:\n");
            sb.Append("  - type: formula ").Append(formula.ToString(inv)).Append('\n');
            sb.Append("    wavelength_range: ").Append(lambdaMin.ToString("R", inv)).Append(' ')
              .Append(lambdaMax.ToString("R", inv)).Append('\n');
            sb.Append("    coefficients:");
            foreach (var c in coefficients) sb.Append(' ').Append(c.ToString("R", inv));
            sb.Append('\n');
            return sb.ToString();
        }

        /// <summary>The "readme" written into the folder of glasses beside a lens file.</summary>
        public static string ReadMe(string lensFile, IEnumerable<string> catalogs)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"The glasses of {lensFile}, as LensHH-LT computes them.");
            sb.AppendLine();
            sb.AppendLine("Each folder is an Optiland user catalog: one refractiveindex.info .yml per glass,");
            sb.AppendLine("holding that glass's dispersion data exactly. The lens file names each glass with");
            sb.AppendLine("one of these catalogs and match_policy \"strict\", so Optiland uses exactly these");
            sb.AppendLine("glasses, and never substitutes another.");
            sb.AppendLine();
            sb.AppendLine("Optiland needs the folders installed. Copy them into ~/.optiland/catalogs/");
            sb.AppendLine("(on Windows, %USERPROFILE%\\.optiland\\catalogs\\); Optiland loads them when it");
            sb.AppendLine("starts. Folders of the same name from other lenses merge: a glass is the same file");
            sb.AppendLine("in each.");
            sb.AppendLine();
            sb.AppendLine("Or load them in the session, before loading the lens:");
            sb.AppendLine("    from optiland.materials.registry import MaterialRegistry");
            sb.AppendLine("    for d in [" + string.Join(", ", ToPython(catalogs)) + "]:");
            sb.AppendLine("        MaterialRegistry.instance().load_catalog(\"<this folder>/\" + d)");
            sb.AppendLine("Load a catalog once per session: loaded again, from another lens's folder, each of");
            sb.AppendLine("its glasses has two entries, and a strict lookup refuses both.");
            sb.AppendLine();
            sb.AppendLine("Without them, Optiland stops with an error naming the catalog it is missing.");
            return sb.ToString();
        }

        private static IEnumerable<string> ToPython(IEnumerable<string> names)
        {
            foreach (var n in names) yield return "\"" + n + "\"";
        }
    }
}
