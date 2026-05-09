using System;
using System.Globalization;
using System.Linq;
using LensHH.Core.Enums;
using LensHH.Core.Models;
using Spectre.Console;

namespace LensHH.CLI.Commands
{
    public class SystemCommand : ICommand
    {
        public string Name => "system";
        public string Description => "System properties: info, set-aperture, set-fields, set-wavelengths, set-afocal, set-catalogs";

        public string Help => @"[bold]system[/] - System properties
  [green]system info[/]                          Show system summary
  [green]system set-aperture epd <value>[/]      Set entrance pupil diameter
  [green]system set-aperture fno <value>[/]      Set F-number
  [green]system set-fields angle <y1> <y2> ...[/]  Set fields (object angle)
  [green]system set-fields height <y1> <y2> ...[/]  Set fields (object height)
  [green]system set-wavelengths <w1> <w2> ...[/] Set wavelengths in micrometers
  [green]system set-primary <index>[/]           Set primary wavelength (1-indexed)
  [green]system edit-wavelength <index> [[wv=<v>]] [[weight=<v>]][/]  Edit wavelength value/weight
  [green]system edit-field <index> [[y=<v>]] [[weight=<v>]][/]        Edit field Y value/weight
  [green]system set-afocal on|off[/]                 Set afocal mode
  [green]system set-ray-aiming off|real|robust[/]   Set ray aiming mode
  [green]system set-catalogs <cat1> <cat2> ...[/]   Set preferred glass catalogs (e.g. SCHOTT OHARA)";

        public void Execute(Session session, string[] args)
        {
            if (args.Length == 0)
            {
                AnsiConsole.MarkupLine(Help);
                return;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "info":
                    ShowInfo(session);
                    break;
                case "set-aperture":
                    SetAperture(session, args);
                    break;
                case "set-fields":
                    SetFields(session, args);
                    break;
                case "set-wavelengths":
                    SetWavelengths(session, args);
                    break;
                case "set-primary":
                    SetPrimary(session, args);
                    break;
                case "edit-wavelength":
                    EditWavelength(session, args);
                    break;
                case "edit-field":
                    EditField(session, args);
                    break;
                case "set-afocal":
                    SetAfocal(session, args);
                    break;
                case "set-ray-aiming":
                    SetRayAiming(session, args);
                    break;
                case "set-catalogs":
                    SetCatalogs(session, args);
                    break;
                default:
                    AnsiConsole.MarkupLine($"[yellow]Unknown subcommand: {Markup.Escape(args[0])}[/]");
                    break;
            }
        }

        private void ShowInfo(Session session)
        {
            var sys = session.EnsureSystem();

            AnsiConsole.MarkupLine($"[bold]Title:[/] {Markup.Escape(sys.Title)}");
            AnsiConsole.MarkupLine($"[bold]Aperture:[/] {sys.Aperture.Type} = {sys.Aperture.Value}");
            AnsiConsole.MarkupLine($"[bold]Field Type:[/] {sys.FieldType}");
            AnsiConsole.MarkupLine($"[bold]Afocal:[/] {(sys.IsAfocal ? "On" : "Off")}");
            AnsiConsole.MarkupLine($"[bold]Ray Aiming:[/] {sys.RayAiming}");
            AnsiConsole.MarkupLine($"[bold]Glass Catalogs:[/] {(sys.GlassCatalogs.Count > 0 ? string.Join(", ", sys.GlassCatalogs) : "(none)")}");

            var wlTable = new Table();
            wlTable.AddColumn("#");
            wlTable.AddColumn("Wavelength (um)");
            wlTable.AddColumn("Weight");
            wlTable.AddColumn("Primary");
            for (int i = 0; i < sys.Wavelengths.Count; i++)
            {
                var wl = sys.Wavelengths[i];
                wlTable.AddRow((i + 1).ToString(), wl.Value.ToString("F6"), wl.Weight.ToString("F2"), wl.IsPrimary ? "*" : "");
            }
            AnsiConsole.Write(wlTable);

            var fTable = new Table();
            fTable.AddColumn("#");
            fTable.AddColumn("Y");
            fTable.AddColumn("Weight");
            fTable.AddColumn("Variable");
            for (int i = 0; i < sys.Fields.Count; i++)
            {
                var f = sys.Fields[i];
                string varStr = f.Variable ? "V" : "";
                if (f.Variable && (f.Min.HasValue || f.Max.HasValue))
                    varStr += $" [{f.Min?.ToString("G4") ?? "---"}, {f.Max?.ToString("G4") ?? "---"}]";
                fTable.AddRow((i + 1).ToString(), f.Y.ToString("F4"), f.Weight.ToString("F2"), varStr);
            }
            AnsiConsole.Write(fTable);
        }

        private void SetAperture(Session session, string[] args)
        {
            var sys = session.EnsureSystem();
            if (args.Length < 3)
            {
                AnsiConsole.MarkupLine("[red]Usage: system set-aperture epd|fno <value>[/]");
                return;
            }

            if (!double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
            {
                AnsiConsole.MarkupLine("[red]Invalid value.[/]");
                return;
            }

            switch (args[1].ToLowerInvariant())
            {
                case "epd":
                    sys.Aperture = new Aperture(ApertureType.EPD, val);
                    break;
                case "fno":
                    sys.Aperture = new Aperture(ApertureType.FNumber, val);
                    break;
                default:
                    AnsiConsole.MarkupLine("[red]Use 'epd' or 'fno'.[/]");
                    return;
            }

            AnsiConsole.MarkupLine($"[green]Aperture set to {sys.Aperture.Type} = {sys.Aperture.Value}[/]");
        }

        private void SetFields(Session session, string[] args)
        {
            var sys = session.EnsureSystem();
            if (args.Length < 3)
            {
                AnsiConsole.MarkupLine("[red]Usage: system set-fields angle|height <y1> <y2> ...[/]");
                return;
            }

            switch (args[1].ToLowerInvariant())
            {
                case "angle":
                    sys.FieldType = FieldType.ObjectAngle;
                    break;
                case "height":
                    sys.FieldType = FieldType.ObjectHeight;
                    break;
                default:
                    AnsiConsole.MarkupLine("[red]Use 'angle' or 'height'.[/]");
                    return;
            }

            // Parse and validate all values BEFORE mutating system state, so
            // a bad value doesn't leave the field list half-rewritten.
            var parsed = new System.Collections.Generic.List<double>();
            for (int i = 2; i < args.Length; i++)
            {
                if (!double.TryParse(args[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
                {
                    AnsiConsole.MarkupLine($"[red]Could not parse field value '{args[i]}'.[/]");
                    return;
                }
                if (!FieldValidation.IsValid(y, sys.FieldType, out string? error))
                {
                    AnsiConsole.MarkupLine($"[red]{Markup.Escape(error!)}[/]");
                    return;
                }
                parsed.Add(y);
            }

            sys.Fields.Clear();
            foreach (double y in parsed)
                sys.Fields.Add(new Field(y, 1.0));

            AnsiConsole.MarkupLine($"[green]Fields set: {sys.Fields.Count} field points ({sys.FieldType})[/]");
        }

        private void SetWavelengths(Session session, string[] args)
        {
            var sys = session.EnsureSystem();
            if (args.Length < 2)
            {
                AnsiConsole.MarkupLine("[red]Usage: system set-wavelengths <w1> <w2> ...[/]");
                return;
            }

            sys.Wavelengths.Clear();
            for (int i = 1; i < args.Length; i++)
            {
                if (double.TryParse(args[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double wl))
                    sys.Wavelengths.Add(new Wavelength(wl));
            }

            if (sys.Wavelengths.Count > 0)
                sys.Wavelengths[0].IsPrimary = true;

            AnsiConsole.MarkupLine($"[green]Wavelengths set: {sys.Wavelengths.Count}[/]");
        }

        private void SetPrimary(Session session, string[] args)
        {
            var sys = session.EnsureSystem();
            if (args.Length < 2 || !int.TryParse(args[1], out int idx))
            {
                AnsiConsole.MarkupLine("[red]Usage: system set-primary <index> (1-indexed)[/]");
                return;
            }

            idx--; // convert to 0-indexed
            if (idx < 0 || idx >= sys.Wavelengths.Count)
            {
                AnsiConsole.MarkupLine("[red]Index out of range.[/]");
                return;
            }

            foreach (var wl in sys.Wavelengths) wl.IsPrimary = false;
            sys.Wavelengths[idx].IsPrimary = true;

            AnsiConsole.MarkupLine($"[green]Primary wavelength set to {sys.Wavelengths[idx].Value} um[/]");
        }

        private void EditWavelength(Session session, string[] args)
        {
            var sys = session.EnsureSystem();
            if (args.Length < 3 || !int.TryParse(args[1], out int idx))
            {
                AnsiConsole.MarkupLine("[red]Usage: system edit-wavelength <index> [wv=<v>] [weight=<v>][/]");
                return;
            }

            idx--;
            if (idx < 0 || idx >= sys.Wavelengths.Count)
            {
                AnsiConsole.MarkupLine("[red]Wavelength index out of range.[/]");
                return;
            }

            var wl = sys.Wavelengths[idx];
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i].StartsWith("wv=", StringComparison.OrdinalIgnoreCase))
                {
                    if (double.TryParse(args[i].Substring(3), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                        wl.Value = v;
                }
                else if (args[i].StartsWith("weight=", StringComparison.OrdinalIgnoreCase))
                {
                    if (double.TryParse(args[i].Substring(7), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                        wl.Weight = v;
                }
            }

            string wlFmt = "F" + LensHH.Rendering.LabelFormat.WavelengthDigits(sys.Wavelengths);
            AnsiConsole.MarkupLine($"[green]Wavelength {idx + 1}: {wl.Value.ToString(wlFmt, System.Globalization.CultureInfo.InvariantCulture)} um, weight={wl.Weight:F2}[/]");
        }

        private void EditField(Session session, string[] args)
        {
            var sys = session.EnsureSystem();
            if (args.Length < 3 || !int.TryParse(args[1], out int idx))
            {
                AnsiConsole.MarkupLine("[red]Usage: system edit-field <index> [y=<v>] [weight=<v>][/]");
                return;
            }

            idx--;
            if (idx < 0 || idx >= sys.Fields.Count)
            {
                AnsiConsole.MarkupLine("[red]Field index out of range.[/]");
                return;
            }

            var field = sys.Fields[idx];
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i].StartsWith("y=", StringComparison.OrdinalIgnoreCase))
                {
                    if (double.TryParse(args[i].Substring(2), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                        field.Y = v;
                }
                else if (args[i].StartsWith("weight=", StringComparison.OrdinalIgnoreCase))
                {
                    if (double.TryParse(args[i].Substring(7), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                        field.Weight = v;
                }
            }

            AnsiConsole.MarkupLine($"[green]Field {idx + 1}: Y={field.Y:F4}, weight={field.Weight:F2}[/]");
        }

        private void SetRayAiming(Session session, string[] args)
        {
            var sys = session.EnsureSystem();
            if (args.Length < 2)
            {
                AnsiConsole.MarkupLine($"[bold]Ray Aiming:[/] {sys.RayAiming}");
                AnsiConsole.MarkupLine("[dim]Usage: system set-ray-aiming off|real|robust[/]");
                return;
            }

            switch (args[1].ToLowerInvariant())
            {
                case "off":
                    sys.RayAiming = RayAimingMode.Off;
                    break;
                case "real":
                    sys.RayAiming = RayAimingMode.Real;
                    break;
                case "robust":
                    sys.RayAiming = RayAimingMode.Robust;
                    break;
                default:
                    AnsiConsole.MarkupLine("[red]Use 'off', 'real', or 'robust'.[/]");
                    return;
            }

            AnsiConsole.MarkupLine($"[green]Ray aiming set to {sys.RayAiming}[/]");
        }

        private void SetAfocal(Session session, string[] args)
        {
            var sys = session.EnsureSystem();
            if (args.Length < 2)
            {
                AnsiConsole.MarkupLine($"[bold]Afocal:[/] {(sys.IsAfocal ? "On" : "Off")}");
                AnsiConsole.MarkupLine("[dim]Usage: system set-afocal on|off[/]");
                return;
            }

            switch (args[1].ToLowerInvariant())
            {
                case "on":
                case "true":
                case "1":
                    sys.IsAfocal = true;
                    break;
                case "off":
                case "false":
                case "0":
                    sys.IsAfocal = false;
                    break;
                default:
                    AnsiConsole.MarkupLine("[red]Use 'on' or 'off'.[/]");
                    return;
            }

            AnsiConsole.MarkupLine($"[green]Afocal mode set to {(sys.IsAfocal ? "On" : "Off")}[/]");
        }

        private void SetCatalogs(Session session, string[] args)
        {
            var sys = session.EnsureSystem();
            if (args.Length < 2)
            {
                AnsiConsole.MarkupLine($"[bold]Glass Catalogs:[/] {(sys.GlassCatalogs.Count > 0 ? string.Join(", ", sys.GlassCatalogs) : "(none)")}");
                AnsiConsole.MarkupLine("[dim]Usage: system set-catalogs <cat1> <cat2> ...[/]");
                return;
            }

            sys.GlassCatalogs.Clear();
            for (int i = 1; i < args.Length; i++)
                sys.GlassCatalogs.Add(args[i].ToUpperInvariant());

            AnsiConsole.MarkupLine($"[green]Glass catalogs set to: {string.Join(", ", sys.GlassCatalogs)}[/]");
        }
    }
}
