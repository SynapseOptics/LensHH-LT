using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LensHH.Core.Enums;
using LensHH.Core.Models;
using Spectre.Console;

namespace LensHH.CLI.Commands
{
    public class SurfaceCommand : ICommand
    {
        public string Name => "surface";
        public string Description => "Surface operations: list, add, remove, edit, set-asphere, scale";

        public string Help => @"[bold]surface[/] - Surface operations
  [green]surface list[/]                        List all surfaces
  [green]surface add <after_index>[/]           Add a surface after the given index
  [green]surface remove <index>[/]              Remove surface at index
  [green]surface edit <index> <param>=<value>[/] Edit surface parameters
    Parameters: radius, thickness, material, semi-diameter, semi-diameter-mode=auto|fixed, conic, stop,
               inner-radius, obscuration
  [green]surface set-asphere <index> <a1> <a2> ...[/] Set aspheric coefficients
  [green]surface clear-fixed-diameters[/]   Reset all semi-diameters to auto mode
  [green]surface set-ca <s1> <s2> <percent>[/]  Set clear aperture % for surface range (Auto only)
  [green]surface variable <param> <surfaces> [[on|off]] [[noinf]] [[min=<v>]] [[max=<v>]][/]
    Parameters: curvature, thickness, conic, aspheric [[terms]]
    Surfaces: 3, 1-5, all, glass, air, glass 1-5, air 1-5
    noinf: skip infinite-radius surfaces (curvature only)
    Aspheric terms: omit=all, 3=single, 0,2,4=list, 0-3=range (0-7 maps to A2-A16)
  Examples:
    surface variable curvature all on noinf
    surface variable thickness glass 1-8 on min=1 max=20
    surface variable thickness air 1-8 on min=0.5 max=100
  [green]surface scale <factor>[/]              Scale entire lens system by a factor";

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
                    ListSurfaces(session);
                    break;
                case "add":
                    AddSurface(session, args);
                    break;
                case "remove":
                    RemoveSurface(session, args);
                    break;
                case "edit":
                    EditSurface(session, args);
                    break;
                case "set-asphere":
                    SetAsphere(session, args);
                    break;
                case "variable":
                    SetVariable(session, args);
                    break;
                case "clear-fixed-diameters":
                    ClearFixedDiameters(session);
                    break;
                case "set-ca":
                    SetClearAperture(session, args);
                    break;
                case "scale":
                    ScaleLens(session, args);
                    break;
                default:
                    AnsiConsole.MarkupLine($"[yellow]Unknown subcommand: {Markup.Escape(args[0])}[/]");
                    break;
            }
        }

        private void ListSurfaces(Session session)
        {
            var sys = session.EnsureSystem();

            var table = new Table();
            table.AddColumn("#");
            table.AddColumn("Type");
            table.AddColumn("Radius");
            table.AddColumn("Thickness");
            table.AddColumn("Material");
            table.AddColumn("Semi-Dia");
            table.AddColumn("Conic");
            table.AddColumn("Stop");

            foreach (var s in sys.Surfaces)
            {
                string radiusStr = double.IsInfinity(s.Radius) ? "Infinity" : s.Radius.ToString("G6");
                if (s.CurvatureVariable) radiusStr += " V";
                string thickStr = double.IsInfinity(s.Thickness) ? "Infinity" : s.Thickness.ToString("G6");
                if (s.ThicknessVariable) thickStr += " V";
                string conicStr = s.Conic.ToString("G6");
                if (s.ConicVariable) conicStr += " V";

                table.AddRow(
                    s.Index.ToString(),
                    s.Type.ToString(),
                    radiusStr,
                    thickStr,
                    Markup.Escape(s.Material),
                    s.SemiDiameter.ToString("G6") + (s.SemiDiameterMode == SemiDiameterMode.Fixed ? " F" : ""),
                    conicStr,
                    s.IsStop ? "*" : ""
                );
            }

            AnsiConsole.Write(table);
        }

        private void AddSurface(Session session, string[] args)
        {
            var sys = session.EnsureSystem();

            int afterIndex = sys.Surfaces.Count - 2; // before image by default
            if (args.Length > 1 && int.TryParse(args[1], out int idx))
                afterIndex = idx;

            if (afterIndex < 0) afterIndex = 0;
            if (afterIndex >= sys.Surfaces.Count) afterIndex = sys.Surfaces.Count - 1;

            int insertedIndex = afterIndex + 1;
            var newSurface = new Surface { Thickness = 0 };

            // If inserting inside a glass element, copy the exit surface shape
            // (standard sequential-design convention). Glass-to-air refraction
            // stays at correct curvature.
            var prevSurf = sys.Surfaces[insertedIndex - 1];
            if (!string.IsNullOrEmpty(prevSurf.Material) && insertedIndex < sys.Surfaces.Count)
            {
                var exitSurf = sys.Surfaces[insertedIndex];
                newSurface.Radius = exitSurf.Radius;
                newSurface.Conic = exitSurf.Conic;
                newSurface.Type = exitSurf.Type;
                if (exitSurf.AsphericCoefficients != null)
                    newSurface.AsphericCoefficients = (double[])exitSurf.AsphericCoefficients.Clone();
            }

            sys.Surfaces.Insert(insertedIndex, newSurface);

            // Reindex
            for (int i = 0; i < sys.Surfaces.Count; i++)
                sys.Surfaces[i].Index = i;

            // Update surface references in merit function, config editor, pickups
            SurfaceIndexUpdater.OnSurfaceInserted(insertedIndex, sys,
                session.CurrentMeritFunction, session.ConfigEditor);

            AnsiConsole.MarkupLine($"[green]Surface added at index {insertedIndex}[/]");
        }

        private void RemoveSurface(Session session, string[] args)
        {
            var sys = session.EnsureSystem();
            if (args.Length < 2 || !int.TryParse(args[1], out int idx))
            {
                AnsiConsole.MarkupLine("[red]Usage: surface remove <index>[/]");
                return;
            }

            if (idx <= 0 || idx >= sys.Surfaces.Count - 1)
            {
                AnsiConsole.MarkupLine("[red]Cannot remove object or image surface.[/]");
                return;
            }

            sys.Surfaces.RemoveAt(idx);
            for (int i = 0; i < sys.Surfaces.Count; i++)
                sys.Surfaces[i].Index = i;

            // Update surface references in merit function, config editor, pickups
            SurfaceIndexUpdater.OnSurfaceRemoved(idx, sys,
                session.CurrentMeritFunction, session.ConfigEditor);

            AnsiConsole.MarkupLine($"[green]Surface {idx} removed.[/]");
        }

        private void EditSurface(Session session, string[] args)
        {
            var sys = session.EnsureSystem();
            if (args.Length < 3 || !int.TryParse(args[1], out int idx))
            {
                AnsiConsole.MarkupLine("[red]Usage: surface edit <index> <param>=<value> ...[/]");
                return;
            }

            if (idx < 0 || idx >= sys.Surfaces.Count)
            {
                AnsiConsole.MarkupLine("[red]Surface index out of range.[/]");
                return;
            }

            var surface = sys.Surfaces[idx];

            for (int i = 2; i < args.Length; i++)
            {
                var kv = args[i].Split(new[] { '=' }, 2);
                if (kv.Length != 2) continue;

                switch (kv[0].ToLowerInvariant())
                {
                    case "radius":
                        if (kv[1].Equals("infinity", StringComparison.OrdinalIgnoreCase))
                            surface.Radius = double.PositiveInfinity;
                        else if (double.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double r))
                            surface.Radius = r;
                        break;
                    case "thickness":
                        if (kv[1].Equals("infinity", StringComparison.OrdinalIgnoreCase))
                            surface.Thickness = double.PositiveInfinity;
                        else if (double.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double t))
                            surface.Thickness = t;
                        break;
                    case "material":
                        surface.Material = kv[1];
                        break;
                    case "semi-diameter":
                        if (double.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double sd))
                        {
                            surface.SemiDiameter = sd;
                            surface.SemiDiameterMode = SemiDiameterMode.Fixed;
                        }
                        break;
                    case "semi-diameter-mode":
                        if (kv[1].Equals("auto", StringComparison.OrdinalIgnoreCase))
                            surface.SemiDiameterMode = SemiDiameterMode.Auto;
                        else if (kv[1].Equals("fixed", StringComparison.OrdinalIgnoreCase))
                            surface.SemiDiameterMode = SemiDiameterMode.Fixed;
                        break;
                    case "conic":
                        if (double.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double c))
                            surface.Conic = c;
                        break;
                    case "stop":
                        bool isStop = kv[1].Equals("true", StringComparison.OrdinalIgnoreCase) || kv[1] == "1";
                        if (isStop)
                        {
                            foreach (var s in sys.Surfaces) s.IsStop = false;
                        }
                        surface.IsStop = isStop;
                        break;
                    case "inner-radius":
                        if (double.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double ir))
                            surface.InnerRadius = ir;
                        break;
                    case "obscuration":
                        if (double.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double ob))
                            surface.ObscurationRadius = ob;
                        break;
                }
            }

            AnsiConsole.MarkupLine($"[green]Surface {idx} updated.[/]");
        }

        private void SetAsphere(Session session, string[] args)
        {
            var sys = session.EnsureSystem();
            if (args.Length < 3 || !int.TryParse(args[1], out int idx))
            {
                AnsiConsole.MarkupLine("[red]Usage: surface set-asphere <index> <a1> <a2> ...[/]");
                return;
            }

            if (idx < 0 || idx >= sys.Surfaces.Count)
            {
                AnsiConsole.MarkupLine("[red]Surface index out of range.[/]");
                return;
            }

            var surface = sys.Surfaces[idx];
            surface.Type = SurfaceType.EvenAsphere;

            for (int i = 2; i < args.Length && (i - 2) < surface.AsphericCoefficients.Length; i++)
            {
                if (double.TryParse(args[i], NumberStyles.Float | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out double val))
                    surface.AsphericCoefficients[i - 2] = val;
            }

            AnsiConsole.MarkupLine($"[green]Aspheric coefficients set for surface {idx}.[/]");
        }

        private void SetVariable(Session session, string[] args)
        {
            var sys = session.EnsureSystem();

            // Syntax: surface variable <param> <surfaces> [on|off] [min=V] [max=V]
            // <surfaces>: 3, 1-5, all, glass, air, glass 1-5, air 1-5
            if (args.Length < 3)
            {
                AnsiConsole.MarkupLine("[red]Usage: surface variable <param> <surfaces> [[on|off]] [[min=<v>]] [[max=<v>]][/]");
                return;
            }

            var param = args[1].ToLowerInvariant();
            if (param != "curvature" && param != "thickness" && param != "conic" && param != "aspheric")
            {
                AnsiConsole.MarkupLine($"[yellow]Unknown parameter: {Markup.Escape(param)}. Use curvature, thickness, conic, or aspheric.[/]");
                return;
            }

            // Parse surface specifier: qualifier + range
            int argIdx = 2;
            string? qualifier = null;
            if (args[argIdx].Equals("glass", StringComparison.OrdinalIgnoreCase) ||
                args[argIdx].Equals("air", StringComparison.OrdinalIgnoreCase))
            {
                qualifier = args[argIdx].ToLowerInvariant();
                argIdx++;
            }

            int rangeStart, rangeEnd;
            if (argIdx >= args.Length || args[argIdx].Equals("on", StringComparison.OrdinalIgnoreCase) ||
                args[argIdx].Equals("off", StringComparison.OrdinalIgnoreCase) ||
                args[argIdx].StartsWith("min=", StringComparison.OrdinalIgnoreCase) ||
                args[argIdx].StartsWith("max=", StringComparison.OrdinalIgnoreCase))
            {
                // No range given after qualifier — use all surfaces (exclude object and image)
                rangeStart = 1;
                rangeEnd = sys.Surfaces.Count - 2;
            }
            else if (args[argIdx].Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                rangeStart = 1;
                rangeEnd = sys.Surfaces.Count - 2;
                argIdx++;
            }
            else if (!TryParseSurfaceRange(args[argIdx], out rangeStart, out rangeEnd))
            {
                AnsiConsole.MarkupLine("[red]Invalid surface specifier. Use: 3, 1-5, all, glass, air, glass 1-5[/]");
                return;
            }
            else
            {
                argIdx++;
            }

            // Parse on/off, min, max, noinf from remaining args
            bool enable = true;
            bool noInf = false;
            double? min = null, max = null;
            for (int i = argIdx; i < args.Length; i++)
            {
                if (args[i].Equals("off", StringComparison.OrdinalIgnoreCase))
                    enable = false;
                else if (args[i].Equals("on", StringComparison.OrdinalIgnoreCase))
                    enable = true;
                else if (args[i].Equals("noinf", StringComparison.OrdinalIgnoreCase))
                    noInf = true;
                else if (args[i].StartsWith("min=", StringComparison.OrdinalIgnoreCase))
                {
                    if (double.TryParse(args[i].Substring(4), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                        min = v;
                }
                else if (args[i].StartsWith("max=", StringComparison.OrdinalIgnoreCase))
                {
                    if (double.TryParse(args[i].Substring(4), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                        max = v;
                }
            }

            // Clamp range to valid surface indices
            rangeStart = Math.Max(0, rangeStart);
            rangeEnd = Math.Min(sys.Surfaces.Count - 1, rangeEnd);

            int count = 0;
            for (int s = rangeStart; s <= rangeEnd; s++)
            {
                var surface = sys.Surfaces[s];

                // Apply air/glass filter
                if (qualifier != null)
                {
                    bool hasGlass = !string.IsNullOrEmpty(surface.Material);
                    if (qualifier == "glass" && !hasGlass) continue;
                    if (qualifier == "air" && hasGlass) continue;
                }

                switch (param)
                {
                    case "curvature":
                        if (noInf && enable && double.IsInfinity(surface.Radius))
                            continue;
                        surface.CurvatureVariable = enable;
                        if (min.HasValue) surface.CurvatureMin = min;
                        if (max.HasValue) surface.CurvatureMax = max;
                        break;
                    case "thickness":
                        surface.ThicknessVariable = enable;
                        if (min.HasValue) surface.ThicknessMin = min;
                        if (max.HasValue) surface.ThicknessMax = max;
                        break;
                    case "conic":
                        surface.ConicVariable = enable;
                        if (min.HasValue) surface.ConicMin = min;
                        if (max.HasValue) surface.ConicMax = max;
                        break;
                    case "aspheric":
                        var terms = ParseAsphericTerms(args);
                        foreach (int t in terms)
                        {
                            surface.AsphericVariable[t] = enable;
                            if (min.HasValue) surface.AsphericMin[t] = min;
                            if (max.HasValue) surface.AsphericMax[t] = max;
                        }
                        break;
                }
                count++;
            }

            string rangeStr = rangeStart == rangeEnd ? rangeStart.ToString() : $"{rangeStart}-{rangeEnd}";
            string qualStr = qualifier != null ? $" ({qualifier})" : "";
            AnsiConsole.MarkupLine($"[green]Variable {param} on {count} surface(s) {rangeStr}{qualStr}: {(enable ? "ON" : "OFF")}[/]");
        }

        private void ClearFixedDiameters(Session session)
        {
            var sys = session.EnsureSystem();
            int count = 0;
            for (int i = 0; i < sys.Surfaces.Count; i++)
            {
                if (sys.Surfaces[i].SemiDiameterMode == SemiDiameterMode.Fixed)
                {
                    sys.Surfaces[i].SemiDiameterMode = SemiDiameterMode.Auto;
                    count++;
                }
            }
            AnsiConsole.MarkupLine($"[green]Reset {count} surfaces from Fixed to Auto semi-diameter mode.[/]");
        }

        private void SetClearAperture(Session session, string[] args)
        {
            if (args.Length < 4)
            {
                AnsiConsole.MarkupLine("[red]Usage: surface set-ca <start> <end> <percent>[/]");
                return;
            }

            var sys = session.EnsureSystem();
            if (!int.TryParse(args[1], out int s1) || !int.TryParse(args[2], out int s2) ||
                !double.TryParse(args[3], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double caPercent))
            {
                AnsiConsole.MarkupLine("[red]Invalid arguments.[/]");
                return;
            }

            s1 = Math.Max(0, s1);
            s2 = Math.Min(sys.Surfaces.Count - 1, s2);
            int count = 0;
            for (int i = s1; i <= s2; i++)
            {
                var surf = sys.Surfaces[i];
                if (surf.SemiDiameterMode == SemiDiameterMode.Auto && !surf.IsStop)
                {
                    surf.ClearAperturePercent = caPercent;
                    count++;
                }
            }
            AnsiConsole.MarkupLine($"[green]Set CA% = {caPercent:F1} on {count} Auto surfaces (range {s1}-{s2}).[/]");
        }

        private static bool TryParseSurfaceRange(string input, out int start, out int end)
        {
            start = end = 0;
            var dashIndex = input.IndexOf('-');

            if (dashIndex < 0)
            {
                if (int.TryParse(input, out int single))
                {
                    start = end = single;
                    return true;
                }
                return false;
            }

            var left = input.Substring(0, dashIndex);
            var right = input.Substring(dashIndex + 1);
            if (int.TryParse(left, out start) && int.TryParse(right, out end) && start <= end)
                return true;

            start = end = 0;
            return false;
        }

        /// <summary>
        /// Parse aspheric term specifier from args after "aspheric".
        /// No specifier = all (0-7). "0,2,4" = specific. "0-3" = range.
        /// Skips "on", "off", and "min=/max=" tokens.
        /// </summary>
        private static List<int> ParseAsphericTerms(string[] args)
        {
            // Look for term specifier starting at args[3] (args[0]=subcommand, [1]=index, [2]=param)
            for (int i = 3; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg.Equals("on", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("off", StringComparison.OrdinalIgnoreCase) ||
                    arg.StartsWith("min=", StringComparison.OrdinalIgnoreCase) ||
                    arg.StartsWith("max=", StringComparison.OrdinalIgnoreCase))
                    continue;

                var result = new List<int>();

                // Range: "0-3"
                if (arg.Contains("-"))
                {
                    var rangeParts = arg.Split('-');
                    if (rangeParts.Length == 2 &&
                        int.TryParse(rangeParts[0], out int lo) &&
                        int.TryParse(rangeParts[1], out int hi))
                    {
                        lo = Math.Max(0, lo);
                        hi = Math.Min(7, hi);
                        for (int t = lo; t <= hi; t++)
                            result.Add(t);
                        return result;
                    }
                }

                // Comma-separated: "0,2,4"
                if (arg.Contains(","))
                {
                    foreach (var part in arg.Split(','))
                    {
                        if (int.TryParse(part.Trim(), out int t) && t >= 0 && t < 8)
                            result.Add(t);
                    }
                    if (result.Count > 0)
                        return result;
                }

                // Single index: "3"
                if (int.TryParse(arg, out int single) && single >= 0 && single < 8)
                {
                    result.Add(single);
                    return result;
                }
            }

            // No specifier found — all terms
            return new List<int> { 0, 1, 2, 3, 4, 5, 6, 7 };
        }

        private void ScaleLens(Session session, string[] args)
        {
            var system = session.EnsureSystem();
            if (args.Length < 2 || !double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double s) || s <= 0)
            {
                AnsiConsole.MarkupLine("[red]Usage: surface scale <factor>  (positive number)[/]");
                return;
            }

            if (s == 1.0)
            {
                AnsiConsole.MarkupLine("[yellow]Scale factor is 1.0 — no change.[/]");
                return;
            }

            for (int i = 0; i < system.Surfaces.Count; i++)
            {
                var surf = system.Surfaces[i];

                if (!double.IsInfinity(surf.Radius) && surf.Radius != 0)
                    surf.Radius *= s;

                if (!double.IsInfinity(surf.Thickness) && !double.IsNaN(surf.Thickness))
                    surf.Thickness *= s;

                if (surf.SemiDiameterMode == SemiDiameterMode.Fixed && surf.SemiDiameter > 0)
                    surf.SemiDiameter *= s;

                if (surf.InnerRadius > 0) surf.InnerRadius *= s;
                if (surf.ClapOuterRadius > 0) surf.ClapOuterRadius *= s;
                if (surf.ObscurationRadius > 0) surf.ObscurationRadius *= s;
                if (surf.FloatingApertureRadius > 0) surf.FloatingApertureRadius *= s;

                if (surf.AsphericCoefficients != null)
                {
                    for (int j = 0; j < surf.AsphericCoefficients.Length; j++)
                    {
                        if (surf.AsphericCoefficients[j] != 0)
                        {
                            int twoN = (j + 1) * 2;
                            surf.AsphericCoefficients[j] *= Math.Pow(s, 1.0 - twoN);
                        }
                    }
                }

                if (surf.ThicknessMin.HasValue) surf.ThicknessMin *= s;
                if (surf.ThicknessMax.HasValue) surf.ThicknessMax *= s;
                if (surf.CurvatureMin.HasValue) surf.CurvatureMin /= s;
                if (surf.CurvatureMax.HasValue) surf.CurvatureMax /= s;
            }

            if (system.Aperture.Type == ApertureType.EPD)
                system.Aperture.Value *= s;

            if (system.FieldType == FieldType.ObjectHeight)
            {
                foreach (var field in system.Fields)
                    field.Y *= s;
            }

            AnsiConsole.MarkupLine($"[green]System scaled by factor {s}.[/]");
        }
    }
}
