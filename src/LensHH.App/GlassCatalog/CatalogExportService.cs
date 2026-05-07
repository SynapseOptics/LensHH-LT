using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LensHH.App.GlassCatalog;

public class CatalogExportService
{
    public List<string> FindDuplicateNames(IEnumerable<GlassEntry> glasses)
    {
        return glasses
            .GroupBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Where(grp => grp.Count() > 1)
            .Select(grp => $"{grp.Key} ({string.Join(", ", grp.Select(g => g.CatalogName))})")
            .ToList();
    }

    public void Export(IEnumerable<GlassEntry> glasses, string outputPath, string catalogName)
    {
        using var writer = new StreamWriter(outputPath, false, System.Text.Encoding.ASCII);
        writer.WriteLine($"CC {catalogName} - Generated {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        foreach (var glass in glasses)
        {
            foreach (var line in glass.RawLines)
            {
                writer.WriteLine(line);
            }
        }
    }
}
