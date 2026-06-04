using System;
using System.Globalization;
using LensHH.Core.Enums;
using LensHH.Core.Models;
using Spectre.Console;

namespace LensHH.CLI.Commands
{
    public class PickupCommand : ICommand
    {
        public string Name => "pickup";
        public string Description => "Pickup operations: list, add, remove";

        public string Help => @"[bold]pickup[/] - Pickup operations
  [green]pickup list[/]                                          List all pickups
  [green]pickup add <target_surf> <param> <source_surf>[/]       Add pickup
    [green][[scale=V]] [[offset=V]][/]
  [green]pickup remove <index>[/]                                Remove pickup
  [green]pickup apply[/]                                         Apply all pickups

  Parameters: radius, thickness, glass, semi-diameter, conic";

        public void Execute(Session session, string[] args)
        {
            if (args.Length == 0)
            {
                AnsiConsole.MarkupLine(Help);
                return;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "list":
                    ListPickups(session);
                    break;
                case "add":
                    AddPickup(session, args);
                    break;
                case "remove":
                    RemovePickup(session, args);
                    break;
                case "apply":
                    ApplyPickups(session);
                    break;
                default:
                    AnsiConsole.MarkupLine($"[yellow]Unknown subcommand: {Markup.Escape(args[0])}[/]");
                    break;
            }
        }

        private void ListPickups(Session session)
        {
            var system = session.EnsureSystem();

            if (system.Pickups.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No pickups defined.[/]");
                return;
            }

            var table = new Table();
            table.AddColumn("#");
            table.AddColumn("Target Surf");
            table.AddColumn("Parameter");
            table.AddColumn("Source Surf");
            table.AddColumn("Scale");
            table.AddColumn("Offset");
            table.AddColumn("Config");

            for (int i = 0; i < system.Pickups.Count; i++)
            {
                var p = system.Pickups[i];
                table.AddRow(
                    i.ToString(),
                    p.TargetSurfaceIndex.ToString(),
                    p.Parameter.ToString(),
                    p.SourceSurfaceIndex.ToString(),
                    p.ScaleFactor.ToString("G6"),
                    p.Offset.ToString("G6"),
                    p.SourceConfigurationIndex >= 0 ? p.SourceConfigurationIndex.ToString() : "same"
                );
            }

            AnsiConsole.Write(table);
        }

        private void AddPickup(Session session, string[] args)
        {
            if (args.Length < 4)
            {
                AnsiConsole.MarkupLine("[red]Usage: pickup add <target_surf> <param> <source_surf> [scale=V] [offset=V][/]");
                return;
            }

            if (!int.TryParse(args[1], out int targetSurf) ||
                !Enum.TryParse<PickupParameter>(args[2], true, out var param) ||
                !int.TryParse(args[3], out int sourceSurf))
            {
                AnsiConsole.MarkupLine("[red]Invalid parameters.[/]");
                return;
            }

            var pickup = new Pickup
            {
                TargetSurfaceIndex = targetSurf,
                Parameter = param,
                SourceSurfaceIndex = sourceSurf
            };

            for (int i = 4; i < args.Length; i++)
            {
                var kv = args[i].Split(new[] { '=' }, 2);
                if (kv.Length != 2) continue;
                switch (kv[0].ToLowerInvariant())
                {
                    case "scale":
                        if (double.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double s))
                            pickup.ScaleFactor = s;
                        break;
                    case "offset":
                        if (double.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double o))
                            pickup.Offset = o;
                        break;
                    // 2026-06-01 task #102: `config=N` removed (multi-config
                    // is out of scope for LensHH-LT; planned for LensHH-Pro).
                    // SourceConfigurationIndex stays in the data model so
                    // Pro-authored .lhlt files round-trip cleanly.
                }
            }

            session.EnsureSystem().Pickups.Add(pickup);
            AnsiConsole.MarkupLine($"[green]Pickup added: surface {targetSurf} {param} from surface {sourceSurf}[/]");
        }

        private void RemovePickup(Session session, string[] args)
        {
            if (args.Length < 2 || !int.TryParse(args[1], out int idx))
            {
                AnsiConsole.MarkupLine("[red]Usage: pickup remove <index>[/]");
                return;
            }

            var system = session.EnsureSystem();
            if (idx < 0 || idx >= system.Pickups.Count)
            {
                AnsiConsole.MarkupLine("[red]Index out of range.[/]");
                return;
            }

            system.Pickups.RemoveAt(idx);
            AnsiConsole.MarkupLine($"[green]Pickup {idx} removed.[/]");
        }

        private void ApplyPickups(Session session)
        {
            var system = session.EnsureSystem();
            PickupEvaluator.Apply(system);
            AnsiConsole.MarkupLine($"[green]Applied {system.Pickups.Count} pickups.[/]");
        }
    }

    internal static class PickupEvaluator
    {
        public static void Apply(OpticalSystem system)
        {
            foreach (var pickup in system.Pickups)
            {
                if (pickup.SourceSurfaceIndex < 0 || pickup.SourceSurfaceIndex >= system.Surfaces.Count)
                    continue;
                if (pickup.TargetSurfaceIndex < 0 || pickup.TargetSurfaceIndex >= system.Surfaces.Count)
                    continue;

                var source = system.Surfaces[pickup.SourceSurfaceIndex];
                var target = system.Surfaces[pickup.TargetSurfaceIndex];

                double sourceValue;
                switch (pickup.Parameter)
                {
                    case PickupParameter.Radius:
                        sourceValue = source.Radius;
                        target.Radius = sourceValue * pickup.ScaleFactor + pickup.Offset;
                        break;
                    case PickupParameter.Thickness:
                        sourceValue = source.Thickness;
                        target.Thickness = sourceValue * pickup.ScaleFactor + pickup.Offset;
                        break;
                    case PickupParameter.Glass:
                        target.Material = source.Material;
                        break;
                    case PickupParameter.SemiDiameter:
                        sourceValue = source.SemiDiameter;
                        target.SemiDiameter = sourceValue * pickup.ScaleFactor + pickup.Offset;
                        break;
                    case PickupParameter.Conic:
                        sourceValue = source.Conic;
                        target.Conic = sourceValue * pickup.ScaleFactor + pickup.Offset;
                        break;
                }
            }
        }
    }
}
