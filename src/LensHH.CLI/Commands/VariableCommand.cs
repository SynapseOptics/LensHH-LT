using System;
using System.Collections.Generic;
using System.Globalization;
using LensHH.Core.Models;
using Spectre.Console;

namespace LensHH.CLI.Commands
{
    public class VariableCommand : ICommand
    {
        public string Name => "var";
        public string Description => "View and edit optimization variables and constraints";

        public string Help => @"[bold]var[/] - Variable table: view and edit optimization variables
  [green]var[/]                                     Show all variables with constraints
  [green]var <#> min=<v> max=<v>[/]                 Edit constraints on variable (1-based index)
  [green]var <#> min=none max=none[/]               Remove constraints
  [green]var clear[/]                               Remove all variables
  [green]var constraint <param> <s1>-<s2> [[glass|air]] min=<v> max=<v>[/]
    Set constraints on existing variables (does not toggle variable flag)
    param: curvature, thickness     s1-s2: surface range
    Example: var constraint thickness 1-8 glass min=2 max=20
    Example: var constraint curvature 1-6 min=-0.1 max=0.1";

        public void Execute(Session session, string[] args)
        {
            var system = session.EnsureSystem();

            if (args.Length == 0)
            {
                ShowVariableTable(system, session);
                return;
            }

            if (args[0].Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                ClearAllVariables(system, session);
                return;
            }

            if (args[0].Equals("constraint", StringComparison.OrdinalIgnoreCase))
            {
                BatchConstraint(system, args);
                return;
            }

            // Edit a variable by table index
            if (int.TryParse(args[0], out int varIndex))
            {
                EditVariable(system, session, varIndex, args);
                return;
            }

            AnsiConsole.MarkupLine(Help);
        }

        private void ShowVariableTable(OpticalSystem system, Session session)
        {
            var vars = CollectVariableEntries(system, session);

            if (vars.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No variables defined.[/]");
                // 2026-06-01 task #102: `system field-variable` (FieldY) and
                // `config variable` (multi-config) are out of scope for
                // LensHH-LT (planned for LensHH-Pro). Dropped from the hint.
                AnsiConsole.MarkupLine("Use [green]surface variable[/] to add variables.");
                return;
            }

            var table = new Table();
            table.AddColumn(new TableColumn("#").RightAligned());
            table.AddColumn("Source");
            table.AddColumn("Parameter");
            table.AddColumn(new TableColumn("Value").RightAligned());
            table.AddColumn(new TableColumn("Min").RightAligned());
            table.AddColumn(new TableColumn("Max").RightAligned());
            table.AddColumn("Bound Type");

            for (int i = 0; i < vars.Count; i++)
            {
                var v = vars[i];
                string boundType = GetBoundType(v.Min, v.Max);
                table.AddRow(
                    (i + 1).ToString(),
                    Markup.Escape(v.Source),
                    Markup.Escape(v.Parameter),
                    v.Value,
                    v.Min.HasValue ? v.Min.Value.ToString("G6") : "---",
                    v.Max.HasValue ? v.Max.Value.ToString("G6") : "---",
                    boundType
                );
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"Total: {vars.Count} variables");
        }

        private static string GetBoundType(double? min, double? max)
        {
            if (min.HasValue && max.HasValue) return "Bounded";
            if (min.HasValue) return "Lower";
            if (max.HasValue) return "Upper";
            return "Free";
        }

        private void EditVariable(OpticalSystem system, Session session, int varIndex, string[] args)
        {
            var vars = CollectVariableEntries(system, session);

            // varIndex is 1-based from user input
            int idx = varIndex - 1;
            if (idx < 0 || idx >= vars.Count)
            {
                AnsiConsole.MarkupLine($"[red]Variable {varIndex} out of range. Valid: 1-{vars.Count}[/]");
                return;
            }

            var entry = vars[idx];

            // Parse min=, max= from remaining args
            double? newMin = null, newMax = null;
            bool clearMin = false, clearMax = false;

            for (int i = 1; i < args.Length; i++)
            {
                if (args[i].StartsWith("min=", StringComparison.OrdinalIgnoreCase))
                {
                    var val = args[i].Substring(4);
                    if (val.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                        val.Equals("clear", StringComparison.OrdinalIgnoreCase))
                        clearMin = true;
                    else if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                        newMin = v;
                }
                else if (args[i].StartsWith("max=", StringComparison.OrdinalIgnoreCase))
                {
                    var val = args[i].Substring(4);
                    if (val.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                        val.Equals("clear", StringComparison.OrdinalIgnoreCase))
                        clearMax = true;
                    else if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                        newMax = v;
                }
            }

            if (!newMin.HasValue && !newMax.HasValue && !clearMin && !clearMax)
            {
                AnsiConsole.MarkupLine("[yellow]Nothing to change. Use min=<v> max=<v> or min=none max=none[/]");
                return;
            }

            // Apply changes
            entry.ApplyConstraints(newMin, newMax, clearMin, clearMax);

            AnsiConsole.MarkupLine($"[green]Updated variable #{varIndex}: {Markup.Escape(entry.Source)} {Markup.Escape(entry.Parameter)}[/]");
            ShowVariableTable(system, session);
        }

        private void BatchConstraint(OpticalSystem system, string[] args)
        {
            // var constraint <param> <s1>-<s2> [glass|air] min=V max=V
            if (args.Length < 3)
            {
                AnsiConsole.MarkupLine("[red]Usage: var constraint <curvature|thickness> <s1>-<s2> [[glass|air]] min=<v> max=<v>[/]");
                return;
            }

            var param = args[1].ToLowerInvariant();
            if (param != "curvature" && param != "thickness")
            {
                AnsiConsole.MarkupLine("[yellow]Parameter must be 'curvature' or 'thickness'.[/]");
                return;
            }

            // Parse surface range
            int argIdx = 2;
            int s1 = 1, s2 = system.Surfaces.Count - 2;
            if (argIdx < args.Length && args[argIdx].Contains("-"))
            {
                var parts = args[argIdx].Split('-');
                if (parts.Length == 2 && int.TryParse(parts[0], out int a) && int.TryParse(parts[1], out int b))
                { s1 = a; s2 = b; }
                argIdx++;
            }
            else if (argIdx < args.Length && int.TryParse(args[argIdx], out int single))
            {
                s1 = s2 = single;
                argIdx++;
            }

            // Parse optional filter and min/max
            string filter = "all";
            double? min = null, max = null;
            for (int i = argIdx; i < args.Length; i++)
            {
                if (args[i].Equals("glass", StringComparison.OrdinalIgnoreCase))
                    filter = "glass";
                else if (args[i].Equals("air", StringComparison.OrdinalIgnoreCase))
                    filter = "air";
                else if (args[i].StartsWith("min=", StringComparison.OrdinalIgnoreCase))
                {
                    var val = args[i].Substring(4);
                    if (val.Equals("none", StringComparison.OrdinalIgnoreCase))
                        min = null;
                    else if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                        min = v;
                }
                else if (args[i].StartsWith("max=", StringComparison.OrdinalIgnoreCase))
                {
                    var val = args[i].Substring(4);
                    if (val.Equals("none", StringComparison.OrdinalIgnoreCase))
                        max = null;
                    else if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                        max = v;
                }
            }

            s1 = Math.Max(0, s1);
            s2 = Math.Min(system.Surfaces.Count - 1, s2);
            int count = 0;

            for (int i = s1; i <= s2; i++)
            {
                var surf = system.Surfaces[i];
                if (filter == "glass" && string.IsNullOrEmpty(surf.Material)) continue;
                if (filter == "air" && !string.IsNullOrEmpty(surf.Material)) continue;

                if (param == "thickness" && surf.ThicknessVariable)
                {
                    surf.ThicknessMin = min;
                    surf.ThicknessMax = max;
                    count++;
                }
                else if (param == "curvature" && surf.CurvatureVariable)
                {
                    surf.CurvatureMin = min;
                    surf.CurvatureMax = max;
                    count++;
                }
            }

            string desc = min.HasValue && max.HasValue ? $"min={min:G4} max={max:G4}" :
                          min.HasValue ? $"min={min:G4}" :
                          max.HasValue ? $"max={max:G4}" : "unconstrained";
            AnsiConsole.MarkupLine($"[green]Set {param} constraints on {count} variables ({s1}-{s2}, {filter}): {desc}[/]");
        }

        private void ClearAllVariables(OpticalSystem system, Session session)
        {
            int count = 0;
            foreach (var s in system.Surfaces)
            {
                if (s.CurvatureVariable) { s.CurvatureVariable = false; s.CurvatureMin = null; s.CurvatureMax = null; count++; }
                if (s.ThicknessVariable) { s.ThicknessVariable = false; s.ThicknessMin = null; s.ThicknessMax = null; count++; }
                if (s.ConicVariable) { s.ConicVariable = false; s.ConicMin = null; s.ConicMax = null; count++; }
                for (int j = 0; j < s.AsphericVariable.Length; j++)
                {
                    if (s.AsphericVariable[j])
                    {
                        s.AsphericVariable[j] = false;
                        s.AsphericMin[j] = null;
                        s.AsphericMax[j] = null;
                        count++;
                    }
                }
            }
            foreach (var f in system.Fields)
            {
                if (f.Variable) { f.Variable = false; f.Min = null; f.Max = null; count++; }
            }
            if (session.ConfigEditor != null)
            {
                var ce = session.ConfigEditor;
                for (int c = 0; c < ce.ConfigurationCount; c++)
                    for (int o = 0; o < ce.OperandCount; o++)
                        if (ce.IsVariable(c, o)) { ce.SetVariable(c, o, false); count++; }
            }

            AnsiConsole.MarkupLine($"[green]Cleared {count} variables.[/]");
        }

        private class VariableEntry
        {
            public string Source { get; set; } = "";
            public string Parameter { get; set; } = "";
            public string Value { get; set; } = "";
            public double? Min { get; set; }
            public double? Max { get; set; }
            public Action<double?, double?, bool, bool> ApplyConstraints { get; set; } = (_, _, _, _) => { };
        }

        private List<VariableEntry> CollectVariableEntries(OpticalSystem system, Session session)
        {
            var list = new List<VariableEntry>();

            for (int i = 0; i < system.Surfaces.Count; i++)
            {
                var s = system.Surfaces[i];
                int surfIdx = i; // capture for lambda

                if (s.CurvatureVariable)
                {
                    list.Add(new VariableEntry
                    {
                        Source = $"Surf {surfIdx}",
                        Parameter = "Curvature",
                        Value = s.Curvature.ToString("G6"),
                        Min = s.CurvatureMin,
                        Max = s.CurvatureMax,
                        ApplyConstraints = (min, max, clrMin, clrMax) =>
                        {
                            if (min.HasValue) s.CurvatureMin = min;
                            if (max.HasValue) s.CurvatureMax = max;
                            if (clrMin) s.CurvatureMin = null;
                            if (clrMax) s.CurvatureMax = null;
                        }
                    });
                }
                if (s.ThicknessVariable)
                {
                    list.Add(new VariableEntry
                    {
                        Source = $"Surf {surfIdx}",
                        Parameter = "Thickness",
                        Value = s.Thickness.ToString("G6"),
                        Min = s.ThicknessMin,
                        Max = s.ThicknessMax,
                        ApplyConstraints = (min, max, clrMin, clrMax) =>
                        {
                            if (min.HasValue) s.ThicknessMin = min;
                            if (max.HasValue) s.ThicknessMax = max;
                            if (clrMin) s.ThicknessMin = null;
                            if (clrMax) s.ThicknessMax = null;
                        }
                    });
                }
                if (s.ConicVariable)
                {
                    list.Add(new VariableEntry
                    {
                        Source = $"Surf {surfIdx}",
                        Parameter = "Conic",
                        Value = s.Conic.ToString("G6"),
                        Min = s.ConicMin,
                        Max = s.ConicMax,
                        ApplyConstraints = (min, max, clrMin, clrMax) =>
                        {
                            if (min.HasValue) s.ConicMin = min;
                            if (max.HasValue) s.ConicMax = max;
                            if (clrMin) s.ConicMin = null;
                            if (clrMax) s.ConicMax = null;
                        }
                    });
                }
                for (int j = 0; j < s.AsphericVariable.Length; j++)
                {
                    if (s.AsphericVariable[j])
                    {
                        int termIdx = j; // capture for lambda
                        list.Add(new VariableEntry
                        {
                            Source = $"Surf {surfIdx}",
                            Parameter = $"Asph[{termIdx}]",
                            Value = s.AsphericCoefficients[termIdx].ToString("E4"),
                            Min = s.AsphericMin[termIdx],
                            Max = s.AsphericMax[termIdx],
                            ApplyConstraints = (min, max, clrMin, clrMax) =>
                            {
                                if (min.HasValue) s.AsphericMin[termIdx] = min;
                                if (max.HasValue) s.AsphericMax[termIdx] = max;
                                if (clrMin) s.AsphericMin[termIdx] = null;
                                if (clrMax) s.AsphericMax[termIdx] = null;
                            }
                        });
                    }
                }
            }

            // Field variables
            for (int i = 0; i < system.Fields.Count; i++)
            {
                var f = system.Fields[i];
                int fIdx = i;
                if (f.Variable)
                {
                    list.Add(new VariableEntry
                    {
                        Source = $"Field {fIdx + 1}",
                        Parameter = "Field Y",
                        Value = f.Y.ToString("G6"),
                        Min = f.Min,
                        Max = f.Max,
                        ApplyConstraints = (min, max, clrMin, clrMax) =>
                        {
                            if (min.HasValue) f.Min = min;
                            if (max.HasValue) f.Max = max;
                            if (clrMin) f.Min = null;
                            if (clrMax) f.Max = null;
                        }
                    });
                }
            }

            // Config editor variables
            if (session.ConfigEditor != null)
            {
                var ce = session.ConfigEditor;
                for (int c = 0; c < ce.ConfigurationCount; c++)
                {
                    for (int o = 0; o < ce.OperandCount; o++)
                    {
                        if (ce.IsVariable(c, o))
                        {
                            var op = ce.Operands[o];
                            int cc = c, oo = o;
                            string val = op.Type == Core.Configuration.ConfigOperandType.Glass
                                ? (ce.GetGlass(cc, oo) ?? "---")
                                : ce.GetValue(cc, oo).ToString("G6");
                            list.Add(new VariableEntry
                            {
                                Source = $"Config {cc}:Op{oo}",
                                Parameter = $"{op.Type}",
                                Value = val,
                                Min = ce.GetMin(cc, oo),
                                Max = ce.GetMax(cc, oo),
                                ApplyConstraints = (min, max, clrMin, clrMax) =>
                                {
                                    double? newMin = clrMin ? null : (min ?? ce.GetMin(cc, oo));
                                    double? newMax = clrMax ? null : (max ?? ce.GetMax(cc, oo));
                                    ce.SetVariable(cc, oo, true, newMin, newMax);
                                }
                            });
                        }
                    }
                }
            }

            return list;
        }
    }
}
