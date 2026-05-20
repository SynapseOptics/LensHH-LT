using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LensHH.Core.Models;
using LensHH.Mcp.GlassCatalog;
using ModelContextProtocol.Server;

namespace LensHH.Mcp.Tools
{
    [McpServerToolType]
    public class GlassTools
    {
        private readonly McpSession _session;
        public GlassTools(McpSession session) => _session = session;

        [McpServerTool, Description("Load glass catalogs from a folder containing AGF files. Returns the number of catalogs and glasses loaded.")]
        public string LoadGlassCatalogs(string folderPath)
        {
            _session.GlassCatalog.LoadCatalogsFromFolder(folderPath);
            var catalogs = _session.GlassCatalog.LoadedCatalogs;
            return $"Loaded {catalogs.Count} catalogs: {string.Join(", ", catalogs)}.";
        }

        [McpServerTool, Description("List loaded glass catalogs.")]
        public string ListCatalogs()
        {
            var catalogs = _session.GlassCatalog.LoadedCatalogs;
            if (catalogs.Count == 0)
                return "No glass catalogs loaded.";
            return $"Loaded catalogs: {string.Join(", ", catalogs)}";
        }

        [McpServerTool, Description("Search for a glass by name across all loaded catalogs. Returns matching glass names and basic properties.")]
        public string SearchGlass(string name)
        {
            var results = _session.GlassCatalog.Search(name);

            if (results.Count == 0)
                return $"No glasses found matching '{name}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Found {results.Count} glasses matching '{name}':");
            sb.AppendLine($"{"Name",-20} {"Catalog",-15} {"Nd",10} {"Vd",8}");

            foreach (var g in results.Take(30))
            {
                double nd = g.GetIndex(0.5876);
                double nf = g.GetIndex(0.4861);
                double nc = g.GetIndex(0.6563);
                double vd = (nf != nc) ? (nd - 1) / (nf - nc) : 0;
                sb.AppendLine($"{g.Name,-20} {g.Catalog,-15} {nd,10:F6} {vd,8:F2}");
            }
            return sb.ToString();
        }

        [McpServerTool, Description("Get detailed information about a specific glass including refractive index at common wavelengths.")]
        public string GlassInfo(string name)
        {
            var glass = _session.GlassCatalog.GetGlass(name);
            if (glass == null)
                return $"Glass '{name}' not found.";

            double nd = glass.GetIndex(0.5876);
            double nf = glass.GetIndex(0.4861);
            double nc = glass.GetIndex(0.6563);
            double vd = (nf != nc) ? (nd - 1) / (nf - nc) : 0;

            var sb = new StringBuilder();
            sb.AppendLine($"Glass: {glass.Name} ({glass.Catalog})");
            sb.AppendLine($"  Nd (587.6nm): {nd:F6}");
            sb.AppendLine($"  Nf (486.1nm): {nf:F6}");
            sb.AppendLine($"  Nc (656.3nm): {nc:F6}");
            sb.AppendLine($"  Abbe Number:  {vd:F2}");
            sb.AppendLine($"  Dispersion Formula: {glass.DispersionFormula}");
            return sb.ToString();
        }

        [McpServerTool, Description("Get the refractive index of a glass at a specific wavelength in micrometers.")]
        public string GetRefractiveIndex(string glassName, double wavelengthUm)
        {
            var glass = _session.GlassCatalog.GetGlass(glassName);
            if (glass == null)
                return $"Glass '{glassName}' not found.";

            double n = glass.GetIndex(wavelengthUm);
            return $"{glassName} at {wavelengthUm:F4} um: n = {n:F6}";
        }

        [McpServerTool, Description("List glass substitution settings for all glass surfaces. Shows surface index, material, substitute flag, and catalog name.")]
        public string ListGlassSubstitutions()
        {
            var sys = _session.System;
            var sb = new StringBuilder();
            int num = 1;
            for (int i = 0; i < sys.Surfaces.Count; i++)
            {
                var s = sys.Surfaces[i];
                if (string.IsNullOrEmpty(s.Material) ||
                    s.Material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase)) continue;

                var setting = sys.GlassSubstitutions.FirstOrDefault(gs => gs.SurfaceIndex == i);
                sb.AppendLine($"Glass {num++}: Surface {i}, Material={s.Material}, Substitute={setting?.Substitute ?? false}, Catalog={setting?.CatalogName ?? ""}");
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "No glass surfaces in the system.";
        }

        [McpServerTool, Description("Set glass substitution for a surface. Enables the surface for glass substitution with the specified filtered catalog.")]
        public string SetGlassSubstitution(int surfaceIndex, string catalogName, bool substitute = true)
        {
            var sys = _session.System;
            if (surfaceIndex < 0 || surfaceIndex >= sys.Surfaces.Count)
                return $"Surface index {surfaceIndex} out of range.";

            var existing = sys.GlassSubstitutions.FirstOrDefault(gs => gs.SurfaceIndex == surfaceIndex);
            if (existing != null)
            {
                existing.Substitute = substitute;
                existing.CatalogName = catalogName;
            }
            else
            {
                sys.GlassSubstitutions.Add(new GlassSubstitutionSetting
                {
                    SurfaceIndex = surfaceIndex,
                    Substitute = substitute,
                    CatalogName = catalogName
                });
            }
            return $"Surface {surfaceIndex}: substitution {(substitute ? "enabled" : "disabled")}, catalog='{catalogName}'.";
        }

        [McpServerTool, Description(
            "Enable glass substitution on every non-aspheric glass surface in the current system, using a stock-lens-derived filtered catalog automatically picked from the system's wavelengths: "
            + "if min(wavelengths) < 0.380 microns the system is in the UV regime and StockGlassesUV is selected (CAF2 + fused-silica variants only — the materials stock lenses are actually built in for UV). Otherwise StockGlassesVisible is selected, which contains every distinct glass found across the non-aspheric singlets in catalogs/stock-lens-catalog.sqlite (~19 materials: the common Schott N-series, B270, BAF2, ACRYLIC, plus fused-silica aliases).\n\n"
            + "Aspheric surfaces (Type=EvenAsphere) are SKIPPED — molded aspheric lenses are typically vendor-specific materials with no stock-lens equivalent, so substitution there would generate non-physical combinations. The action sets Substitute=true on every spherical glass surface, replacing any existing substitution settings on those surfaces. Already-disabled settings on aspheric surfaces are left alone.\n\n"
            + "After calling this, run multistart_optimize with glassSubPercent > 0 to actually swap glasses.")]
        public string SetStockGlassSubstitution()
        {
            var sys = _session.System;
            if (sys.Wavelengths == null || sys.Wavelengths.Count == 0)
                return "SetStockGlassSubstitution error: no wavelengths defined; cannot decide UV vs Visible.";

            double minWl = sys.Wavelengths.Min(w => w.Value);
            string catalog = minWl < 0.380 ? "StockGlassesUV" : "StockGlassesVisible";

            int enabledCount = 0;
            int skippedAspheric = 0;
            int skippedAir = 0;
            int skippedMirror = 0;

            for (int i = 0; i < sys.Surfaces.Count; i++)
            {
                var s = sys.Surfaces[i];
                if (string.IsNullOrEmpty(s.Material)) { skippedAir++; continue; }
                if (s.Material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase)) { skippedMirror++; continue; }
                if (s.Type == LensHH.Core.Enums.SurfaceType.EvenAsphere) { skippedAspheric++; continue; }

                var existing = sys.GlassSubstitutions.FirstOrDefault(gs => gs.SurfaceIndex == i);
                if (existing != null)
                {
                    existing.Substitute  = true;
                    existing.CatalogName = catalog;
                }
                else
                {
                    sys.GlassSubstitutions.Add(new GlassSubstitutionSetting
                    {
                        SurfaceIndex = i,
                        Substitute   = true,
                        CatalogName  = catalog
                    });
                }
                enabledCount++;
            }

            return $"Stock glass substitution enabled: catalog={catalog} (min wavelength {minWl:F4} um). "
                + $"Substitute=true set on {enabledCount} spherical glass surface(s). "
                + $"Skipped: {skippedAspheric} aspheric, {skippedAir} air, {skippedMirror} mirror.";
        }

        [McpServerTool, Description("Clear glass substitution settings. If surfaceIndex is -1, clears all; otherwise clears the specified surface.")]
        public string ClearGlassSubstitutions(int surfaceIndex = -1)
        {
            var sys = _session.System;
            if (surfaceIndex < 0)
            {
                sys.GlassSubstitutions.Clear();
                return "Cleared all glass substitution settings.";
            }
            sys.GlassSubstitutions.RemoveAll(gs => gs.SurfaceIndex == surfaceIndex);
            return $"Cleared substitution for surface {surfaceIndex}.";
        }

        [McpServerTool, Description("Generate a filtered glass catalog from AGF source files. sourceDir: folder containing .agf files. outputPath: path for the output .agf file. catalogs: comma-separated catalog names to include (empty = all). filters: comma-separated filter expressions like 'preferred', 'nd=1.4-2.1', 'vd=0-100', 'dpgf=-0.2-0.2', 'tce=0-20', 'cost=5', 'minwl=0.42', 'maxwl=2.0', 'melt=3'.")]
        public string GenerateFilteredCatalog(string sourceDir, string outputPath, string catalogs = "", string filters = "")
        {
            if (!Directory.Exists(sourceDir))
                return $"Source directory not found: {sourceDir}";

            var parser = new AgfFileParser();
            var filter = new GlassFilterService();
            var exporter = new CatalogExportService();

            var allCatalogs = parser.DiscoverCatalogs(sourceDir);
            if (allCatalogs.Count == 0)
                return $"No .agf files found in: {sourceDir}";

            // Parse catalog selection
            var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(catalogs))
            {
                foreach (var c in catalogs.Split(','))
                {
                    var trimmed = c.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        selectedNames.Add(trimmed);
                }
            }
            if (selectedNames.Count == 0)
                selectedNames = new HashSet<string>(allCatalogs.Keys, StringComparer.OrdinalIgnoreCase);

            // Parse filters
            if (!string.IsNullOrWhiteSpace(filters))
            {
                foreach (var token in filters.Split(','))
                {
                    var f = token.Trim();
                    if (f.Equals("preferred", StringComparison.OrdinalIgnoreCase))
                    { filter.PreferredEnabled = true; continue; }

                    var kv = f.Split(new[] { '=' }, 2);
                    if (kv.Length != 2) continue;
                    string key = kv[0].ToLowerInvariant();
                    string val = kv[1];

                    switch (key)
                    {
                        case "nd":
                            if (TryParseRange(val, out double ndMin, out double ndMax))
                            { filter.NdRangeEnabled = true; filter.NdMin = ndMin; filter.NdMax = ndMax; }
                            break;
                        case "vd":
                            if (TryParseRange(val, out double vdMin, out double vdMax))
                            { filter.VdRangeEnabled = true; filter.VdMin = vdMin; filter.VdMax = vdMax; }
                            break;
                        case "dpgf":
                            if (TryParseRange(val, out double dpMin, out double dpMax))
                            { filter.DPgFRangeEnabled = true; filter.DPgFMin = dpMin; filter.DPgFMax = dpMax; }
                            break;
                        case "tce":
                            if (TryParseRange(val, out double tceMin, out double tceMax))
                            { filter.TCERangeEnabled = true; filter.TCEMin = tceMin; filter.TCEMax = tceMax; }
                            break;
                        case "cost":
                            if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double cv))
                            { filter.CostEnabled = true; filter.CostLimit = cv; }
                            break;
                        case "minwl":
                            if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double mnw))
                            { filter.MinWavelengthEnabled = true; filter.MinWavelengthValue = mnw; }
                            break;
                        case "maxwl":
                            if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double mxw))
                            { filter.MaxWavelengthEnabled = true; filter.MaxWavelengthValue = mxw; }
                            break;
                        case "melt":
                            if (int.TryParse(val, out int mv))
                            { filter.MeltFrequencyEnabled = true; filter.MeltFrequencyLimit = mv; }
                            break;
                    }
                }
            }

            // Load glasses
            var glasses = new List<GlassCatalog.GlassEntry>();
            foreach (var catName in selectedNames)
            {
                if (allCatalogs.TryGetValue(catName, out string? path))
                    glasses.AddRange(parser.ParseCatalog(path, catName));
            }

            if (glasses.Count == 0)
                return "No glasses loaded from selected catalogs.";

            var filtered = filter.Apply(glasses);
            if (filtered.Count == 0)
                return $"No glasses match the filter criteria (from {glasses.Count} total).";

            var duplicates = exporter.FindDuplicateNames(filtered);
            string catalogName = Path.GetFileNameWithoutExtension(outputPath);
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            exporter.Export(filtered, outputPath, catalogName);

            var sb = new StringBuilder();
            sb.AppendLine($"Generated catalog: {filtered.Count} glasses (from {glasses.Count} total) saved to {outputPath}");
            if (duplicates.Count > 0)
            {
                sb.AppendLine("Warning: duplicate glass names:");
                foreach (var d in duplicates) sb.AppendLine($"  {d}");
            }
            return sb.ToString();
        }

        private static bool TryParseRange(string val, out double min, out double max)
        {
            min = 0; max = 0;
            var parts = val.Split('-');
            if (parts.Length == 3 && parts[0] == "")
                return double.TryParse("-" + parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out min) &&
                       double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out max);
            if (parts.Length == 2)
                return double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out min) &&
                       double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out max);
            return false;
        }
    }
}
