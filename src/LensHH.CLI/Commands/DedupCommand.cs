using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LensHH.Core.IO;
using LensHH.Core.MeritFunction;
using LensHH.Core.Models;
using Spectre.Console;

namespace LensHH.CLI.Commands
{
    /// <summary>
    /// CLI-only utility: read every .lhlt design in an input folder and write the
    /// structurally-UNIQUE designs to an output folder (dropping near-duplicate results,
    /// e.g. several basin-hopping chains that converged to the same minimum). Within each
    /// duplicate cluster the best-merit design is kept; output files are named so the best
    /// sorts first.
    /// </summary>
    public class DedupCommand : ICommand
    {
        public string Name => "dedup";
        public string Description => "Deduplicate a folder of .lhlt designs into unique results.";
        public string Help =>
            "dedup <inputFolder> <outputFolder> [digits=N]\n" +
            "  Reads every *.lhlt in <inputFolder>, groups designs that agree to N significant\n" +
            "  figures (default 5) on every surface's curvature/thickness/conic/material, keeps\n" +
            "  the best-merit design per group, and writes the unique designs to <outputFolder>\n" +
            "  (created if needed). Best-merit design is rank 1.\n" +
            "    digits=N   significance for the duplicate test (default 5; lower = more merging)";

        public void Execute(Session session, string[] args)
        {
            string? input = null, output = null;
            int digits = 5;
            foreach (var a in args)
            {
                var kv = a.Split('=', 2);
                if (kv.Length == 2)
                {
                    if (kv[0].Equals("digits", StringComparison.OrdinalIgnoreCase) && int.TryParse(kv[1], out int d))
                        digits = Math.Clamp(d, 2, 12);
                    continue;
                }
                if (input == null) input = a;
                else if (output == null) output = a;
            }

            if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output))
            {
                AnsiConsole.MarkupLine("[red]Usage:[/] dedup <inputFolder> <outputFolder> [digits=N]");
                return;
            }
            if (!Directory.Exists(input))
            {
                AnsiConsole.MarkupLine($"[red]Input folder not found:[/] {Markup.Escape(input)}");
                return;
            }

            var files = Directory.GetFiles(input, "*.lhlt").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
            if (files.Length == 0)
            {
                AnsiConsole.MarkupLine($"[yellow]No .lhlt files in[/] {Markup.Escape(input)}");
                return;
            }

            var glassMgr = session.EnsureGlassCatalog();
            var entries = new List<Entry>();
            int skipped = 0;
            foreach (var f in files)
            {
                try
                {
                    var r = LhltReader.Read(f);
                    double merit = double.NaN;
                    if (r.MeritFunction != null)
                    {
                        try { merit = new MeritFunctionEvaluator(r.System, glassMgr, r.ConfigEditor).Evaluate(r.MeritFunction); }
                        catch { /* unresolved glass / bad merit → rank last, still deduped structurally */ }
                    }
                    entries.Add(new Entry(f, r.System, r.MeritFunction, r.ConfigEditor, merit, Signature(r.System, digits)));
                }
                catch (Exception ex)
                {
                    skipped++;
                    AnsiConsole.MarkupLine($"[yellow]Skipped[/] {Markup.Escape(Path.GetFileName(f))}: {Markup.Escape(ex.Message)}");
                }
            }

            // Group by structural signature; keep the best (lowest) merit per group.
            static double Rank(double m) => double.IsNaN(m) ? double.MaxValue : m;
            var unique = entries
                .GroupBy(e => e.Signature)
                .Select(g => g.OrderBy(e => Rank(e.Merit)).First())
                .OrderBy(e => Rank(e.Merit))
                .ToList();

            Directory.CreateDirectory(output);
            int rank = 1;
            foreach (var u in unique)
            {
                string meritStr = double.IsNaN(u.Merit) ? "na" : u.Merit.ToString("G6", CultureInfo.InvariantCulture);
                string name = Sanitize($"unique_rank{rank:D2}_m{meritStr}.lhlt");
                LhltWriter.Write(u.System, Path.Combine(output, name), u.MeritFunction, u.ConfigEditor);
                rank++;
            }

            int dups = entries.Count - unique.Count;
            AnsiConsole.MarkupLine("");
            AnsiConsole.MarkupLine($"[bold]Dedup complete[/] (signature digits = {digits})");
            AnsiConsole.MarkupLine($"  Read:      {files.Length} file(s){(skipped > 0 ? $" ({skipped} skipped)" : "")}");
            AnsiConsole.MarkupLine($"  Unique:    [green]{unique.Count}[/]   Duplicates dropped: {dups}");
            AnsiConsole.MarkupLine($"  Written to {Markup.Escape(output)}");
        }

        private readonly struct Entry
        {
            public Entry(string path, OpticalSystem system, MeritFunction? mf,
                LensHH.Core.Configuration.ConfigurationEditor? cfg, double merit, string sig)
            { Path = path; System = system; MeritFunction = mf; ConfigEditor = cfg; Merit = merit; Signature = sig; }
            public string Path { get; }
            public OpticalSystem System { get; }
            public MeritFunction? MeritFunction { get; }
            public LensHH.Core.Configuration.ConfigurationEditor? ConfigEditor { get; }
            public double Merit { get; }
            public string Signature { get; }
        }

        // Structural fingerprint: every surface's curvature/thickness/conic rounded to `digits`
        // significant figures plus its material. Designs with the same fingerprint are the
        // same local minimum (the basin-hopping duplicate case).
        private static string Signature(OpticalSystem sys, int digits)
        {
            string fmt = "G" + digits;
            var sb = new StringBuilder();
            foreach (var s in sys.Surfaces)
            {
                sb.Append(s.Curvature.ToString(fmt, CultureInfo.InvariantCulture)).Append('|');
                sb.Append(s.Thickness.ToString(fmt, CultureInfo.InvariantCulture)).Append('|');
                sb.Append(s.Conic.ToString(fmt, CultureInfo.InvariantCulture)).Append('|');
                sb.Append(s.Material ?? "").Append(';');
            }
            return sb.ToString();
        }

        private static string Sanitize(string s)
        {
            foreach (char ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
            return s;
        }
    }
}
