using System;
using System.Collections.Generic;
using System.IO;
using LensHH.Core.Enums;
using LensHH.Core.IO;
using LensHH.Core.Models;
using Spectre.Console;

namespace LensHH.CLI.Commands
{
    public class FileCommand : ICommand
    {
        public string Name => "file";
        public string Description => "File operations: new, open, save, save-as, import, export";

        public string Help => @"[bold]file[/] - File operations
  [green]file new[/]                  Create a new empty optical system
  [green]file open <path>[/]          Open a .lhlt file
  [green]file save [[name]][/]          Save as .lhlt (uses name if given, else current path)
  [green]file save-as <path>[/]       Save to a specific .lhlt path
  [green]file import zemax <path>[/]  Import a Zemax .zmx file
  [green]file import codev <path>[/]  Import a Code V .seq file
  [green]file import oslo <path>[/]   Import an OSLO .len file
  [green]file import optalix <path>[/] Import an Optalix .otx file
  [green]file import optiland <path>[/] Import an Optiland .json file
  [green]file export zemax <path>[/]  Export as Zemax .zmx file
  [green]file export codev <path>[/]  Export as Code V .seq file
  [green]file export oslo <path>[/]   Export as OSLO .len file
  [green]file export optalix <path>[/] Export as Optalix .otx file
  [green]file export optiland <path>[/] Export as Optiland .json file";

        public void Execute(Session session, string[] args)
        {
            if (args.Length == 0)
            {
                AnsiConsole.MarkupLine(Help);
                return;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "new":
                    ExecuteNew(session);
                    break;
                case "open":
                    ExecuteOpen(session, args);
                    break;
                case "save":
                    ExecuteSave(session, args);
                    break;
                case "save-as":
                    ExecuteSaveAs(session, args);
                    break;
                case "import":
                    ExecuteImport(session, args);
                    break;
                case "export":
                    ExecuteExport(session, args);
                    break;
                case "export-zmx":
                    // Legacy alias: treat as "export zemax <rest>"
                    if (args.Length >= 2)
                    {
                        var legacyArgs = new string[args.Length + 1];
                        legacyArgs[0] = "export";
                        legacyArgs[1] = "zemax";
                        Array.Copy(args, 1, legacyArgs, 2, args.Length - 1);
                        ExecuteExport(session, legacyArgs);
                    }
                    break;
                default:
                    AnsiConsole.MarkupLine($"[yellow]Unknown subcommand: {Markup.Escape(args[0])}[/]");
                    break;
            }
        }

        private void ExecuteNew(Session session)
        {
            var system = new OpticalSystem
            {
                Title = "New System",
                Aperture = new Aperture(ApertureType.EPD, 10.0),
                FieldType = FieldType.ObjectAngle
            };

            // Object surface
            system.Surfaces.Add(new Surface { Index = 0, Thickness = double.PositiveInfinity });
            // Stop surface
            system.Surfaces.Add(new Surface { Index = 1, IsStop = true, Thickness = 10.0 });
            // Image surface
            system.Surfaces.Add(new Surface { Index = 2 });

            system.Wavelengths.Add(new Wavelength(0.587, 1.0, true));
            system.Fields.Add(new Field(0, 1.0));

            session.CurrentSystem = system;
            session.CurrentFilePath = null;
            session.CurrentMeritFunction = null;
            session.ConfigEditor = null;

            AnsiConsole.MarkupLine("[green]New optical system created.[/]");
        }

        private void ExecuteOpen(Session session, string[] args)
        {
            if (args.Length < 2)
            {
                AnsiConsole.MarkupLine("[red]Usage: file open <path>[/]");
                return;
            }

            var path = string.Join(" ", args, 1, args.Length - 1);

            // Append .lhlt extension if no extension given
            if (string.IsNullOrEmpty(Path.GetExtension(path)))
                path += ".lhlt";

            var ext = Path.GetExtension(path).ToLowerInvariant();

            if (ext == ".zmx")
            {
                AnsiConsole.MarkupLine("[yellow]Use 'file import <path>' to import ZMX files.[/]");
                return;
            }

            if (ext != ".lhlt")
            {
                AnsiConsole.MarkupLine($"[red]Unsupported file format: {Markup.Escape(ext)}. Use .lhlt files.[/]");
                return;
            }

            var result = LhltReader.Read(path);
            session.CurrentSystem = result.System;
            session.CurrentFilePath = path;
            session.CurrentMeritFunction = result.MeritFunction;
            session.ConfigEditor = result.ConfigEditor;

            var system = result.System;

            // Paraxial ray aiming removed from enum; ZMX reader already maps it to Off

            AnsiConsole.MarkupLine($"[green]Loaded: {Markup.Escape(system.Title)}[/]");
            AnsiConsole.MarkupLine($"  Surfaces: {system.Surfaces.Count}");
            AnsiConsole.MarkupLine($"  Wavelengths: {system.Wavelengths.Count}");
            AnsiConsole.MarkupLine($"  Fields: {system.Fields.Count}");
            if (result.MeritFunction != null)
                AnsiConsole.MarkupLine($"  Merit function operands: {result.MeritFunction.Operands.Count}");
            if (result.ConfigEditor != null)
                AnsiConsole.MarkupLine($"  Configurations: {result.ConfigEditor.ConfigurationCount}, Operands: {result.ConfigEditor.OperandCount}");
            PrintValidationWarnings(session);
        }

        private static void PrintValidationWarnings(Session session)
        {
            var errors = session.Validate();
            if (errors.Count == 0) return;
            AnsiConsole.MarkupLine("[yellow]Validation warnings (analyses will refuse to run until fixed):[/]");
            foreach (var e in errors)
                AnsiConsole.MarkupLine($"  [yellow]{Markup.Escape(e.Message)}[/]");
        }

        private void ExecuteSave(Session session, string[] args)
        {
            var system = session.EnsureSystem();

            if (args.Length >= 2)
            {
                // file save <name> — save as .lhlt with the given name
                var name = string.Join(" ", args, 1, args.Length - 1);

                // Ensure .lhlt extension
                if (!name.EndsWith(".lhlt", StringComparison.OrdinalIgnoreCase))
                    name += ".lhlt";

                LhltWriter.Write(system, name,
                    session.CurrentMeritFunction,
                    session.ConfigEditor);

                session.CurrentFilePath = name;
                AnsiConsole.MarkupLine($"[green]Saved: {Markup.Escape(name)}[/]");
            }
            else
            {
                // file save (no name) — save to current .lhlt path
                if (session.CurrentFilePath == null)
                {
                    AnsiConsole.MarkupLine("[red]No file path set. Use 'file save <name>' or 'file save-as <path>'.[/]");
                    return;
                }

                var ext = Path.GetExtension(session.CurrentFilePath).ToLowerInvariant();
                if (ext == ".zmx")
                {
                    AnsiConsole.MarkupLine("[red]Current file is a ZMX import. Use 'file save <name>' to save as .lhlt, or 'file export <path>' to export ZMX.[/]");
                    return;
                }

                LhltWriter.Write(system, session.CurrentFilePath,
                    session.CurrentMeritFunction,
                    session.ConfigEditor);

                AnsiConsole.MarkupLine($"[green]Saved: {Markup.Escape(session.CurrentFilePath)}[/]");
            }
        }

        private void ExecuteSaveAs(Session session, string[] args)
        {
            var system = session.EnsureSystem();
            if (args.Length < 2)
            {
                AnsiConsole.MarkupLine("[red]Usage: file save-as <path>[/]");
                return;
            }

            var path = string.Join(" ", args, 1, args.Length - 1);

            // Ensure .lhlt extension
            if (!path.EndsWith(".lhlt", StringComparison.OrdinalIgnoreCase))
                path += ".lhlt";

            LhltWriter.Write(system, path,
                session.CurrentMeritFunction,
                session.ConfigEditor);

            session.CurrentFilePath = path;
            AnsiConsole.MarkupLine($"[green]Saved: {Markup.Escape(path)}[/]");
        }

        private void ExecuteImport(Session session, string[] args)
        {
            if (args.Length < 3)
            {
                AnsiConsole.MarkupLine("[red]Usage: file import <format> <path>[/]");
                AnsiConsole.MarkupLine("  Formats: zemax, codev, oslo, optalix, optiland");
                return;
            }

            string format = args[1].ToLowerInvariant();
            var path = string.Join(" ", args, 2, args.Length - 2);

            OpticalSystem system;
            switch (format)
            {
                case "zemax":
                case "zmx":
                    system = ZmxReader.Read(path);
                    break;
                case "codev":
                case "seq":
                    system = CodeVReader.Read(path);
                    break;
                case "oslo":
                case "len":
                    system = OsloReader.Read(path);
                    break;
                case "optalix":
                case "otx":
                    system = OptalixReader.Read(path);
                    break;
                case "optiland":
                case "json":
                    system = OptilandReader.Read(path);
                    break;
                default:
                    AnsiConsole.MarkupLine($"[red]Unknown format: {Markup.Escape(format)}. Use zemax, codev, oslo, optalix, or optiland.[/]");
                    return;
            }

            session.CurrentSystem = system;
            session.CurrentFilePath = null;
            session.CurrentMeritFunction = null;
            session.ConfigEditor = null;

            AnsiConsole.MarkupLine($"[green]Imported ({format}): {Markup.Escape(system.Title)}[/]");
            AnsiConsole.MarkupLine($"  Surfaces: {system.Surfaces.Count}");
            AnsiConsole.MarkupLine($"  Wavelengths: {system.Wavelengths.Count}");
            AnsiConsole.MarkupLine($"  Fields: {system.Fields.Count}");
        }

        private void ExecuteExport(Session session, string[] args)
        {
            var system = session.EnsureSystem();
            if (args.Length < 3)
            {
                AnsiConsole.MarkupLine("[red]Usage: file export <format> <path>[/]");
                AnsiConsole.MarkupLine("  Formats: zemax, codev, oslo, optalix, optiland");
                return;
            }

            string format = args[1].ToLowerInvariant();
            var path = string.Join(" ", args, 2, args.Length - 2);

            switch (format)
            {
                case "zemax":
                case "zmx":
                    ZmxWriter.Write(system, path);
                    break;
                case "codev":
                case "seq":
                    CodeVWriter.Write(system, path);
                    break;
                case "oslo":
                case "len":
                    OsloWriter.Write(system, path);
                    break;
                case "optalix":
                case "otx":
                    OptalixWriter.Write(system, path);
                    break;
                case "optiland":
                case "json":
                    OptilandWriter.Write(system, path);
                    break;
                default:
                    AnsiConsole.MarkupLine($"[red]Unknown format: {Markup.Escape(format)}. Use zemax, codev, oslo, optalix, or optiland.[/]");
                    return;
            }

            AnsiConsole.MarkupLine($"[green]Exported ({format}): {Markup.Escape(path)}[/]");
        }
    }
}
