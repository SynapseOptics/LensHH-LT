using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using LensHH.CLI.GlassCatalog;
using LensHH.Core.Glass;
using LensHH.Core.Models;
using Spectre.Console;

namespace LensHH.CLI.Commands
{
    public class GlassCommand : ICommand
    {
        public string Name => "glass";
        public string Description => "Glass catalog: search, info, catalogs, load, substitution, generate";

        public string Help => @"[bold]glass[/] - Glass catalog operations
  [green]glass catalogs[/]             List loaded catalogs
  [green]glass load <folder>[/]        Load AGF catalogs from folder
  [green]glass search <pattern>[/]     Search for glass by name
  [green]glass info <name>[/]          Show glass properties
  [green]glass index <name> <wl>[/]    Compute index at wavelength (um)
  [green]glass system-indices[/]       Show refractive indices for current system
  [green]glass substitution list[/]    Show glass substitution settings
  [green]glass substitution set <surf> <catalog>[/]  Enable substitution for surface
  [green]glass substitution clear [surf][/]          Clear substitution (all or one surface)
  [green]glass generate <source_dir> <output.agf>[/] Generate filtered glass catalog
    [[catalogs=SCHOTT,OHARA,...]] [[preferred]] [[nd=1.4-2.1]] [[vd=0-100]]
    [[dpgf=-0.2-0.2]] [[tce=0-20]] [[cost=5]] [[minwl=0.42]] [[maxwl=2.0]] [[melt=3]]";

        public void Execute(Session session, string[] args)
        {
            if (args.Length == 0)
            {
                AnsiConsole.MarkupLine(Help);
                return;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "catalogs":
                    ShowCatalogs(session);
                    break;
                case "load":
                    LoadCatalogs(session, args);
                    break;
                case "search":
                    SearchGlass(session, args);
                    break;
                case "info":
                    ShowInfo(session, args);
                    break;
                case "index":
                    ShowIndex(session, args);
                    break;
                case "system-indices":
                    ShowSystemIndices(session);
                    break;
                case "substitution":
                case "sub":
                    HandleSubstitution(session, args.Skip(1).ToArray());
                    break;
                case "generate":
                    GenerateCatalog(args);
                    break;
                default:
                    AnsiConsole.MarkupLine($"[yellow]Unknown subcommand: {Markup.Escape(args[0])}[/]");
                    break;
            }
        }

        private void ShowCatalogs(Session session)
        {
            var glassMgr = session.EnsureGlassCatalog();
            if (glassMgr.LoadedCatalogs.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No catalogs loaded. Use 'glass load <folder>'.[/]");
                return;
            }

            AnsiConsole.MarkupLine("[bold]Loaded Catalogs:[/]");
            foreach (var cat in glassMgr.LoadedCatalogs)
                AnsiConsole.MarkupLine($"  {Markup.Escape(cat)}");
        }

        private void LoadCatalogs(Session session, string[] args)
        {
            if (args.Length < 2)
            {
                AnsiConsole.MarkupLine("[red]Usage: glass load <folder>[/]");
                return;
            }

            var folder = string.Join(" ", args.Skip(1));
            var glassMgr = session.EnsureGlassCatalog();
            glassMgr.LoadCatalogsFromFolder(folder);

            AnsiConsole.MarkupLine($"[green]Loaded catalogs from: {Markup.Escape(folder)}[/]");
            foreach (var cat in glassMgr.LoadedCatalogs)
                AnsiConsole.MarkupLine($"  {Markup.Escape(cat)}");
        }

        private void SearchGlass(Session session, string[] args)
        {
            if (args.Length < 2)
            {
                AnsiConsole.MarkupLine("[red]Usage: glass search <pattern>[/]");
                return;
            }

            var glassMgr = session.EnsureGlassCatalog();
            var results = glassMgr.Search(args[1]);

            if (results.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No glasses found.[/]");
                return;
            }

            var table = new Table();
            table.AddColumn("Name");
            table.AddColumn("Catalog");
            table.AddColumn("Nd");
            table.AddColumn("Vd");

            foreach (var g in results.Take(50))
            {
                table.AddRow(
                    Markup.Escape(g.Name),
                    Markup.Escape(g.Catalog),
                    g.Nd.ToString("F4"),
                    g.Vd.ToString("F2")
                );
            }

            AnsiConsole.Write(table);
            if (results.Count > 50)
                AnsiConsole.MarkupLine($"[grey]...and {results.Count - 50} more[/]");
        }

        private void ShowInfo(Session session, string[] args)
        {
            if (args.Length < 2)
            {
                AnsiConsole.MarkupLine("[red]Usage: glass info <name>[/]");
                return;
            }

            var glassMgr = session.EnsureGlassCatalog();
            var glass = glassMgr.GetGlass(args[1]);

            if (glass == null)
            {
                AnsiConsole.MarkupLine($"[red]Glass '{Markup.Escape(args[1])}' not found.[/]");
                return;
            }

            AnsiConsole.MarkupLine($"[bold]{Markup.Escape(glass.Name)}[/] ({Markup.Escape(glass.Catalog)})");
            AnsiConsole.MarkupLine($"  Nd: {glass.Nd:F6}  Vd: {glass.Vd:F2}");
            AnsiConsole.MarkupLine($"  Formula: {glass.DispersionFormula}");
            AnsiConsole.MarkupLine($"  Wavelength Range: {glass.WavelengthMin:F3} - {glass.WavelengthMax:F3} um");

            // Show index at common wavelengths
            var table = new Table();
            table.AddColumn("Wavelength");
            table.AddColumn("um");
            table.AddColumn("Index");

            var commonWl = new[] {
                ("F (Blue)", 0.4861),
                ("d (Yellow)", 0.5876),
                ("C (Red)", 0.6563)
            };

            foreach (var (name, wl) in commonWl)
            {
                double idx = glass.GetIndex(wl);
                table.AddRow(name, wl.ToString("F4"), idx.ToString("F6"));
            }

            AnsiConsole.Write(table);
        }

        private void ShowSystemIndices(Session session)
        {
            var sys = session.EnsureSystem();
            var glassMgr = session.EnsureGlassCatalog();

            if (sys.GlassCatalogs.Count > 0)
                AnsiConsole.MarkupLine($"[bold]Preferred catalogs:[/] {string.Join(", ", sys.GlassCatalogs)}");

            var table = new Table();
            table.AddColumn("Surf");
            table.AddColumn("Material");
            table.AddColumn("Catalog");
            foreach (var wl in sys.Wavelengths)
                table.AddColumn($"n@{wl.Value:F4}");

            for (int i = 0; i < sys.Surfaces.Count; i++)
            {
                var s = sys.Surfaces[i];
                var material = s.Material;
                if (string.IsNullOrEmpty(material))
                {
                    var row = new string[3 + sys.Wavelengths.Count];
                    row[0] = i.ToString();
                    row[1] = "(air)";
                    row[2] = "";
                    for (int w = 0; w < sys.Wavelengths.Count; w++)
                        row[3 + w] = "1.000000";
                    table.AddRow(row);
                }
                else
                {
                    var glass = glassMgr.GetGlass(material, sys.GlassCatalogs.Count > 0 ? sys.GlassCatalogs : null);
                    var row = new string[3 + sys.Wavelengths.Count];
                    row[0] = i.ToString();
                    row[1] = Markup.Escape(material);
                    row[2] = glass != null ? Markup.Escape(glass.Catalog) : "[red]NOT FOUND[/]";
                    for (int w = 0; w < sys.Wavelengths.Count; w++)
                    {
                        if (material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase))
                            row[3 + w] = "(mirror)";
                        else if (glass != null)
                            row[3 + w] = glass.GetIndex(sys.Wavelengths[w].Value).ToString("F6");
                        else
                            row[3 + w] = "[red]1.000000[/]";
                    }
                    table.AddRow(row);
                }
            }

            AnsiConsole.Write(table);

            if (glassMgr.LoadedCatalogs.Count == 0)
                AnsiConsole.MarkupLine("[yellow]No glass catalogs loaded. Use 'glass load <folder>'.[/]");
        }

        private void HandleSubstitution(Session session, string[] args)
        {
            var sys = session.EnsureSystem();
            var sub = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

            switch (sub)
            {
                case "list":
                {
                    var table = new Table();
                    table.AddColumn("Glass #");
                    table.AddColumn("Surface");
                    table.AddColumn("Material");
                    table.AddColumn("Substitute");
                    table.AddColumn("Catalog");

                    int num = 1;
                    for (int i = 0; i < sys.Surfaces.Count; i++)
                    {
                        var s = sys.Surfaces[i];
                        if (string.IsNullOrEmpty(s.Material) ||
                            s.Material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase)) continue;

                        var setting = sys.GlassSubstitutions.FirstOrDefault(gs => gs.SurfaceIndex == i);
                        table.AddRow(
                            num++.ToString(),
                            i.ToString(),
                            Markup.Escape(s.Material),
                            setting?.Substitute == true ? "Yes" : "No",
                            setting != null ? Markup.Escape(setting.CatalogName) : ""
                        );
                    }
                    AnsiConsole.Write(table);
                    break;
                }
                case "set":
                {
                    if (args.Length < 3)
                    {
                        AnsiConsole.MarkupLine("[red]Usage: glass substitution set <surfaceIndex> <catalogName>[/]");
                        return;
                    }
                    if (!int.TryParse(args[1], out int surfIdx))
                    {
                        AnsiConsole.MarkupLine("[red]Invalid surface index.[/]");
                        return;
                    }
                    string catName = args[2];
                    var existing = sys.GlassSubstitutions.FirstOrDefault(gs => gs.SurfaceIndex == surfIdx);
                    if (existing != null)
                    {
                        existing.Substitute = true;
                        existing.CatalogName = catName;
                    }
                    else
                    {
                        sys.GlassSubstitutions.Add(new GlassSubstitutionSetting
                        {
                            SurfaceIndex = surfIdx, Substitute = true, CatalogName = catName
                        });
                    }
                    AnsiConsole.MarkupLine($"[green]Surface {surfIdx} substitution enabled with catalog '{Markup.Escape(catName)}'.[/]");
                    break;
                }
                case "clear":
                {
                    if (args.Length >= 2 && int.TryParse(args[1], out int clearIdx))
                    {
                        sys.GlassSubstitutions.RemoveAll(gs => gs.SurfaceIndex == clearIdx);
                        AnsiConsole.MarkupLine($"[green]Cleared substitution for surface {clearIdx}.[/]");
                    }
                    else
                    {
                        sys.GlassSubstitutions.Clear();
                        AnsiConsole.MarkupLine("[green]Cleared all glass substitution settings.[/]");
                    }
                    break;
                }
                default:
                    AnsiConsole.MarkupLine($"[yellow]Unknown substitution subcommand: {Markup.Escape(sub)}[/]");
                    break;
            }
        }

        private void GenerateCatalog(string[] args)
        {
            if (args.Length < 3)
            {
                AnsiConsole.MarkupLine("[red]Usage: glass generate <source_dir> <output.agf> [options...][/]");
                AnsiConsole.MarkupLine("[dim]Options: catalogs=SCHOTT,OHARA  preferred  nd=1.4-2.1  vd=0-100[/]");
                AnsiConsole.MarkupLine("[dim]         dpgf=-0.2-0.2  tce=0-20  cost=5  minwl=0.42  maxwl=2.0  melt=3[/]");
                return;
            }

            string sourceDir = args[1];
            string outputPath = args[2];

            if (!Directory.Exists(sourceDir))
            {
                AnsiConsole.MarkupLine($"[red]Source directory not found: {Markup.Escape(sourceDir)}[/]");
                return;
            }

            var parser = new AgfFileParser();
            var filter = new GlassFilterService();
            var exporter = new CatalogExportService();

            // Discover available catalogs
            var allCatalogs = parser.DiscoverCatalogs(sourceDir);
            if (allCatalogs.Count == 0)
            {
                AnsiConsole.MarkupLine($"[red]No .agf files found in: {Markup.Escape(sourceDir)}[/]");
                return;
            }

            // Parse options
            var selectedCatalogNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 3; i < args.Length; i++)
            {
                var arg = args[i];

                if (arg.Equals("preferred", StringComparison.OrdinalIgnoreCase))
                {
                    filter.PreferredEnabled = true;
                    continue;
                }

                var kv = arg.Split(new[] { '=' }, 2);
                if (kv.Length != 2) continue;
                string key = kv[0].ToLowerInvariant();
                string val = kv[1];

                switch (key)
                {
                    case "catalogs":
                        foreach (var c in val.Split(','))
                            selectedCatalogNames.Add(c.Trim());
                        break;
                    case "nd":
                        if (TryParseRange(val, out double ndMin, out double ndMax))
                        {
                            filter.NdRangeEnabled = true;
                            filter.NdMin = ndMin;
                            filter.NdMax = ndMax;
                        }
                        break;
                    case "vd":
                        if (TryParseRange(val, out double vdMin, out double vdMax))
                        {
                            filter.VdRangeEnabled = true;
                            filter.VdMin = vdMin;
                            filter.VdMax = vdMax;
                        }
                        break;
                    case "dpgf":
                        if (TryParseRange(val, out double dpMin, out double dpMax))
                        {
                            filter.DPgFRangeEnabled = true;
                            filter.DPgFMin = dpMin;
                            filter.DPgFMax = dpMax;
                        }
                        break;
                    case "tce":
                        if (TryParseRange(val, out double tceMin, out double tceMax))
                        {
                            filter.TCERangeEnabled = true;
                            filter.TCEMin = tceMin;
                            filter.TCEMax = tceMax;
                        }
                        break;
                    case "cost":
                        if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double costVal))
                        {
                            filter.CostEnabled = true;
                            filter.CostLimit = costVal;
                        }
                        break;
                    case "minwl":
                        if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double minWl))
                        {
                            filter.MinWavelengthEnabled = true;
                            filter.MinWavelengthValue = minWl;
                        }
                        break;
                    case "maxwl":
                        if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double maxWl))
                        {
                            filter.MaxWavelengthEnabled = true;
                            filter.MaxWavelengthValue = maxWl;
                        }
                        break;
                    case "melt":
                        if (int.TryParse(val, out int meltVal))
                        {
                            filter.MeltFrequencyEnabled = true;
                            filter.MeltFrequencyLimit = meltVal;
                        }
                        break;
                }
            }

            // If no catalogs specified, use all
            if (selectedCatalogNames.Count == 0)
                selectedCatalogNames = new HashSet<string>(allCatalogs.Keys, StringComparer.OrdinalIgnoreCase);

            // Load and parse selected catalogs
            var glasses = new List<GlassCatalog.GlassEntry>();
            foreach (var catName in selectedCatalogNames)
            {
                if (allCatalogs.TryGetValue(catName, out string? path))
                {
                    var parsed = parser.ParseCatalog(path, catName);
                    glasses.AddRange(parsed);
                    AnsiConsole.MarkupLine($"  Loaded {parsed.Count} glasses from {Markup.Escape(catName)}");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[yellow]  Catalog '{Markup.Escape(catName)}' not found in source directory.[/]");
                }
            }

            if (glasses.Count == 0)
            {
                AnsiConsole.MarkupLine("[red]No glasses loaded.[/]");
                return;
            }

            // Apply filters
            var filtered = filter.Apply(glasses);
            AnsiConsole.MarkupLine($"Filtered: {filtered.Count} of {glasses.Count} glasses passed.");

            if (filtered.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No glasses match the filter criteria.[/]");
                return;
            }

            // Check for duplicates
            var duplicates = exporter.FindDuplicateNames(filtered);
            if (duplicates.Count > 0)
            {
                AnsiConsole.MarkupLine("[yellow]Warning: duplicate glass names found:[/]");
                foreach (var dup in duplicates)
                    AnsiConsole.MarkupLine($"  {Markup.Escape(dup)}");
            }

            // Export
            string catalogName = Path.GetFileNameWithoutExtension(outputPath);
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            exporter.Export(filtered, outputPath, catalogName);
            AnsiConsole.MarkupLine($"[green]Saved {filtered.Count} glasses to: {Markup.Escape(outputPath)}[/]");
        }

        private static bool TryParseRange(string val, out double min, out double max)
        {
            min = 0;
            max = 0;
            var parts = val.Split('-');

            // Handle negative min: e.g. "-0.2-0.2" splits to ["", "0.2", "0.2"]
            if (parts.Length == 3 && parts[0] == "")
            {
                return double.TryParse("-" + parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out min) &&
                       double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out max);
            }

            if (parts.Length == 2)
            {
                return double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out min) &&
                       double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out max);
            }

            return false;
        }

        private void ShowIndex(Session session, string[] args)
        {
            if (args.Length < 3)
            {
                AnsiConsole.MarkupLine("[red]Usage: glass index <name> <wavelength_um>[/]");
                return;
            }

            var glassMgr = session.EnsureGlassCatalog();
            var glass = glassMgr.GetGlass(args[1]);

            if (glass == null)
            {
                AnsiConsole.MarkupLine($"[red]Glass '{Markup.Escape(args[1])}' not found.[/]");
                return;
            }

            if (!double.TryParse(args[2], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double wl))
            {
                AnsiConsole.MarkupLine("[red]Invalid wavelength.[/]");
                return;
            }

            double index = glass.GetIndex(wl);
            AnsiConsole.MarkupLine($"n({wl:F4} um) = {index:F6}");
        }
    }
}
