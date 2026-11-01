using System;
using System.Globalization;
using System.IO;
using System.Linq;
using LensHH.Tolerancing;
using LensHH.Tolerancing.Drawings;
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
    fields=xy | y | file   Judge each lens at +y, +x, -y and -x (default: the lens turned about its axis),
                      at +y and -y, or at the lens's fields only
    wave=<um>         Test wavelength for fringes (0.6328)
    show=<n>          Sensitivity: show the n worst only
    trials=<n> seed=<n>                      Monte Carlo lenses and seed (200, 1)
    stats=uniform | normal[[:k]] | endpoint[[+disc|+ring]]   How errors spread (uniform)
    mtf=<cy/mm> step=<cy/mm>                 Keep each lens's MTF up to this frequency
    save=<folder> savecount=<n>              Save trial lenses (.lhlt), trials.csv and MTF plots
  [green]tolerance drawings <file.tol> out=<folder> [[settings=<drawing-settings.json>]] [[format=pdf|svg|dxf|all]] [[standard=iso|asme]] [[wave=<um>]][/]
    A drawing of each element from the tolerance set: drawings.pdf with every sheet (format=pdf, the default),
    and an .svg or .dxf per element; prints each drawing's warnings. The settings file holds the shop's
    classes, coatings, title block and per-element overrides (docs/tolerancing.md).
  [green]tolerance drawings settings <file.json>[/]   Write the default drawing settings, to edit";

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
                    case "drawings":
                        Drawings(system, glass, args);
                        break;
                    default:
                        AnsiConsole.MarkupLine($"[red]Unknown subcommand: {Markup.Escape(args[0])}[/]");
                        AnsiConsole.MarkupLine(Help);
                        break;
                }
            }
            catch (Exception e) when (e is ArgumentException or FormatException or InvalidOperationException
                                       or NotSupportedException or IOException or System.Text.Json.JsonException
                                       or UnauthorizedAccessException)
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

        private static void Drawings(Core.Models.OpticalSystem system, Core.Glass.GlassCatalogManager glass, string[] args)
        {
            if (args.Length == 3 && args[1].Equals("settings", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(args[2], new DrawingSettings().ToJson());
                AnsiConsole.MarkupLine($"[green]Wrote the default drawing settings to {Markup.Escape(args[2])}[/]");
                return;
            }
            if (args.Length < 2 || !File.Exists(args[1]))
            {
                AnsiConsole.MarkupLine("[red]Usage: tolerance drawings <file.tol> out=<folder> [[settings=<file.json>]] [[format=pdf|svg|dxf|all]] " +
                                       "[[standard=iso|asme]] — the tolerance file was not found.[/]");
                return;
            }
            string? settingsFile = null, folder = null;
            var formats = DrawingFormats.Pdf;
            DrawingStandard? standard = null;
            double wave = PowerFringes.HeNe;
            foreach (var a in args.Skip(2))
            {
                var kv = a.Split('=', 2);
                string value = kv.Length > 1 ? kv[1] : "";
                switch (kv[0].ToLowerInvariant())
                {
                    case "settings": settingsFile = value; break;
                    case "out": folder = value; break;
                    case "format": formats = DrawingRun.ParseFormat(value); break;
                    case "standard": standard = DrawingRun.ParseStandard(value); break;
                    case "wave": wave = double.Parse(value, CultureInfo.InvariantCulture); break;
                    default: throw new ArgumentException($"unknown option {a}");
                }
            }
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("tolerance drawings needs out=<folder>");
            var settings = DrawingRun.ReadSettings(settingsFile);
            if (standard is DrawingStandard st) settings = settings with { Standard = st };
            var result = DrawingRun.Write(system, glass, File.ReadAllText(args[1]), settings, folder, formats, wave);
            Console.Write(DrawingRun.Report(result));
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
                    "fields" => settings with { Fields = ToleranceRun.ParseFields(value) },
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
