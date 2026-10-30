using System;
using System.Globalization;
using System.IO;
using System.Linq;
using LensHH.Tolerancing;
using Spectre.Console;

namespace LensHH.CLI.Commands
{
    public class ToleranceCommand : ICommand
    {
        public string Name => "tolerance";
        public string Description => "Tolerancing: default tolerances, sensitivity, Monte Carlo with MTF";

        public string Help => @"[bold]tolerance[/] - Tolerance the current lens
  [green]tolerance defaults [[low|commercial|precision|extra]] [[file.tol]][/]
    A starting tolerance set at a shop grade (commercial by default); printed, or written to file.tol
  [green]tolerance sensitivity <file.tol> [[options]][/]   Each tolerance at both ends of its range
  [green]tolerance montecarlo <file.tol> [[options]][/]    Random lenses drawn within the tolerances
  A .tol file has one tolerance per line: CODE surfaces ±t, or CODE surfaces min max,
    then optionally uniform | normal[[:k]] | endpoint, and pivot:d on an element tilt. # comments.
    Codes: TRAD TFRN TIRR TRSI TTHI TIND TABB TCON TSDX TSDY TSTX TSTY, and for an element
    (surfaces first-last) TEDX TEDY TETX TETY.
    Compensators, also one per line: COMP surface [[±travel]] (the thickness after it), and
    CEDX CEDY CETX CETY first-last [[±travel]] (an element decentred or tilted in its cell).
    Without them each lens is refocused by its back focus.
  Options:
    criterion=rms | mtf:field:frequency:t|s   What the lens is judged by (RMS wavefront by default)
    mono              Primary wavelength only
    nofocus           No compensator at all;  travel=<mm> limits the back focus when the set names none
    fields=y | file   Mirror each field to -y as well (default), or the lens's fields only
    wave=<um>         Test wavelength for fringes (0.6328)
    show=<n>          Sensitivity: show the n worst only
    trials=<n> seed=<n>                      Monte Carlo lenses and seed (200, 1)
    stats=uniform | normal[[:k]] | endpoint[[+disc|+ring]]   How errors spread (uniform)
    mtf=<cy/mm> step=<cy/mm>                 Keep each lens's MTF up to this frequency
    save=<folder> savecount=<n>              Save trial lenses (.lhlt), trials.csv and MTF plots";

        public void Execute(Session session, string[] args)
        {
            if (args.Length == 0)
            {
                AnsiConsole.MarkupLine(Help);
                return;
            }

            var system = session.EnsureValidSystem();
            var glass = session.EnsureGlassCatalog();
            try
            {
                switch (args[0].ToLowerInvariant())
                {
                    case "defaults":
                        Defaults(system, glass, args);
                        break;
                    case "sensitivity":
                    case "montecarlo":
                    case "monte-carlo":
                        Run(system, glass, args);
                        break;
                    default:
                        AnsiConsole.MarkupLine($"[red]Unknown subcommand: {Markup.Escape(args[0])}[/]");
                        AnsiConsole.MarkupLine(Help);
                        break;
                }
            }
            catch (Exception e) when (e is ArgumentException or FormatException or InvalidOperationException
                                       or NotSupportedException or IOException)
            {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(e.Message)}[/]");
            }
        }

        private static void Defaults(Core.Models.OpticalSystem system, Core.Glass.GlassCatalogManager glass, string[] args)
        {
            var grade = ShopGrade.Commercial;
            string? file = null;
            foreach (var a in args.Skip(1))
            {
                switch (a.ToLowerInvariant())
                {
                    case "low": case "lowcost": grade = ShopGrade.LowCost; break;
                    case "commercial": grade = ShopGrade.Commercial; break;
                    case "precision": grade = ShopGrade.Precision; break;
                    case "extra": case "extraprecise": grade = ShopGrade.ExtraPrecise; break;
                    default: file = a; break;
                }
            }
            string text = ToleranceRun.Defaults(system, glass, grade, new ToleranceSettings());
            if (file == null) { Console.Write(text); return; }
            File.WriteAllText(file, text);
            AnsiConsole.MarkupLine($"[green]Wrote {text.Split('\n').Count(l => l.Length > 0 && !l.StartsWith("#"))} tolerances to {Markup.Escape(file)}[/]");
        }

        private static void Run(Core.Models.OpticalSystem system, Core.Glass.GlassCatalogManager glass, string[] args)
        {
            if (args.Length < 2 || !File.Exists(args[1]))
            {
                AnsiConsole.MarkupLine($"[red]Usage: tolerance {args[0]} <file.tol> [[options]] — the tolerance file was not found.[/]");
                return;
            }
            string tolerances = File.ReadAllText(args[1]);
            var settings = new ToleranceSettings();
            int show = 0;
            var c = CultureInfo.InvariantCulture;
            foreach (var a in args.Skip(2))
            {
                var kv = a.Split('=', 2);
                string key = kv[0].ToLowerInvariant();
                string value = kv.Length > 1 ? kv[1] : "";
                settings = key switch
                {
                    "criterion" => settings with { Criterion = value },
                    "mono" => settings with { Polychromatic = false },
                    "nofocus" => settings with { Compensate = false },
                    "travel" => settings with { Travel = double.Parse(value, c) },
                    "fields" => settings with { Fields = value.Equals("file", StringComparison.OrdinalIgnoreCase) ? FieldSymmetry.File : FieldSymmetry.Y },
                    "wave" => settings with { TestWavelengthUm = double.Parse(value, c) },
                    "trials" => settings with { Trials = int.Parse(value, c) },
                    "seed" => settings with { Seed = int.Parse(value, c) },
                    "stats" => settings with { Statistics = value },
                    "mtf" => settings with { MtfMaxFrequency = double.Parse(value, c) },
                    "step" => settings with { MtfStep = double.Parse(value, c) },
                    "save" => settings with { SaveDirectory = value },
                    "savecount" => settings with { SaveTrials = int.Parse(value, c) },
                    "show" => settings,
                    _ => throw new ArgumentException($"unknown option {a}"),
                };
                if (key == "show") show = int.Parse(value, c);
            }

            bool sensitivity = args[0].Equals("sensitivity", StringComparison.OrdinalIgnoreCase);
            int total = sensitivity ? 2 * ToleranceRun.Parse(tolerances, settings).Count : settings.Trials;
            int lastShown = -1;
            var progress = new Progress<int>(done =>
            {
                int pct = (int)(100.0 * done / Math.Max(1, total));
                if (pct / 10 != lastShown / 10) { lastShown = pct; Console.Error.Write($"\r  {pct,3}%"); }
            });

            if (sensitivity)
            {
                var table = ToleranceRun.Sensitivity(system, glass, tolerances, settings, progress);
                Console.Error.WriteLine();
                Console.Write(ToleranceRun.Report(table, show));
            }
            else
            {
                var result = ToleranceRun.MonteCarlo(system, glass, tolerances, settings, progress);
                Console.Error.WriteLine();
                Console.Write(ToleranceRun.Report(result));
            }
        }
    }
}
