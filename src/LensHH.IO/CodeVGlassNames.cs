using System;
using System.Collections.Generic;
using System.Text;
using LensHH.Core.Glass;

namespace LensHH.Core.IO
{
    /// <summary>
    /// Glass-name translation between the LensHH/ZEMAX catalogs and Code V.
    ///
    /// Code V glass names carry no punctuation. The catalogs' <c>N-BK7</c>,
    /// <c>S-FPL51</c>, <c>H-ZF52</c> and <c>D-ZLAF52LA</c> are written
    /// <c>NBK7</c>, <c>SFPL51</c>, <c>HZF52</c> and <c>DZLAF52LA</c>. This is a
    /// general rule, not a Schott one: of the 1515 glasses in catalogs/Glass,
    /// 754 contain punctuation and only 115 of those carry the Schott N-prefix.
    ///
    /// The inverse is not a string operation. Punctuation cannot be re-inserted
    /// by guesswork -- Hoya ships 28 real names of the form <c>NBF1</c>,
    /// <c>NBFD10</c>, <c>NBFD265</c>, which must survive an import untouched,
    /// while <c>NBK7</c> has to become <c>N-BK7</c>. Nothing in the spelling
    /// separates the two; only a catalog lookup does, which is the job of
    /// <see cref="CodeVGlassResolver"/>.
    /// </summary>
    internal static class CodeVGlassNames
    {
        /// <summary>
        /// Catalog name to Code V name: drop every character Code V does not
        /// accept in a glass name. The underscore goes too, because Code V
        /// reads it as the separator in <c>GLASS_CATALOG</c> -- left in place,
        /// Corning's <c>HPFS_7980</c> would be taken as glass HPFS from a
        /// catalog named 7980.
        /// </summary>
        public static string ToCodeV(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                    sb.Append(c);
            }

            // A name that is nothing but punctuation cannot be helped, and an
            // empty material would corrupt the surface line. Pass the original
            // through and let Code V report it.
            return sb.Length > 0 ? sb.ToString() : name;
        }

        /// <summary>
        /// The pre-1.0.153 import rule, kept only for the path where no catalog
        /// manager is available: assume a leading N before an uppercase letter
        /// is a de-punctuated Schott N-prefix. Right for <c>NBK7</c>, wrong for
        /// Hoya's <c>NBFD10</c>, and without a catalog there is no way to tell.
        /// </summary>
        public static string LegacyNPrefix(string name)
        {
            if (name.Length >= 2 && name[0] == 'N' && char.IsUpper(name[1]))
                return "N-" + name.Substring(1);
            return name;
        }
    }

    /// <summary>
    /// Resolves a Code V glass name back to the catalog name it was written
    /// from, by looking it up rather than by transforming it. Built once per
    /// file read; the index costs a single pass over the loaded catalogs.
    /// </summary>
    internal sealed class CodeVGlassResolver
    {
        private readonly GlassCatalogManager? _mgr;

        // Stripped form -> real catalog name, globally and per catalog. The
        // per-catalog map is what lets a GLASS_CATALOG qualifier pick Sumita's
        // P-SK50 over Schott's PSK50: those two are the only names in the
        // shipped catalogs that collide once punctuation is removed.
        private readonly Dictionary<string, string> _byStripped;
        private readonly Dictionary<string, Dictionary<string, string>> _byCatalogStripped;

        public CodeVGlassResolver(GlassCatalogManager? mgr)
        {
            _mgr = mgr;
            _byStripped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _byCatalogStripped = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            if (mgr == null) return;

            foreach (var catalog in mgr.LoadedCatalogs)
            {
                var perCatalog = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var glass in mgr.GetGlassesInCatalog(catalog))
                {
                    if (string.IsNullOrEmpty(glass.Name)) continue;
                    var stripped = CodeVGlassNames.ToCodeV(glass.Name);

                    if (!perCatalog.ContainsKey(stripped))
                        perCatalog[stripped] = glass.Name;

                    // First catalog in load order wins a contested stripped
                    // form. Both contested forms resolve earlier than this, on
                    // the exact-name or per-catalog check in Resolve.
                    if (!_byStripped.ContainsKey(stripped))
                        _byStripped[stripped] = glass.Name;
                }
                _byCatalogStripped[catalog] = perCatalog;
            }
        }

        /// <summary>
        /// Translate one Code V material token to a catalog glass name. Returns
        /// the token unchanged when no loaded catalog claims it.
        /// </summary>
        public string Resolve(string material)
        {
            if (string.IsNullOrEmpty(material)) return material;

            // Code V qualifies a name as GLASS_CATALOG. ToCodeV has already
            // removed any underscore from the glass part, so the first
            // underscore is the separator and the catalog keeps its own
            // (CORNING_FS).
            string name = material;
            string? catalog = null;
            int underscoreIdx = material.IndexOf('_');
            if (underscoreIdx > 0)
            {
                name = material.Substring(0, underscoreIdx);
                catalog = material.Substring(underscoreIdx + 1);
            }

            if (_mgr == null) return CodeVGlassNames.LegacyNPrefix(name);

            // A catalog qualifier is the only thing separating the colliding
            // names, so honour it before any global lookup.
            if (catalog != null)
            {
                var exact = _mgr.GetGlass(catalog.ToUpperInvariant() + ":" + name);
                if (exact != null) return exact.Name;

                if (_byCatalogStripped.TryGetValue(catalog, out var perCatalog) &&
                    perCatalog.TryGetValue(name, out var inCatalog))
                    return inCatalog;
            }

            // The name as written wins next: Hoya's NBFD10 and its 27 siblings
            // are real catalog names that merely look like de-punctuated
            // N-prefix glasses, and transforming them breaks a working import.
            if (_mgr.GetGlass(name) != null) return name;

            // Otherwise it lost its punctuation on the way out to Code V; find
            // the catalog entry that strips down to it.
            if (_byStripped.TryGetValue(name, out var real)) return real;

            // Unknown to every loaded catalog. Leave it as the file spelled it
            // rather than decorating it with a dash we cannot justify.
            return name;
        }
    }
}
