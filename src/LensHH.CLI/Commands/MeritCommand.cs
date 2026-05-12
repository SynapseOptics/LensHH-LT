using System;
using System.Collections.Generic;
using System.Globalization;
using LensHH.Core.Enums;
using LensHH.Core.MeritFunction;
using Spectre.Console;

namespace LensHH.CLI.Commands
{
    public class MeritCommand : ICommand
    {
        public string Name => "merit";
        public string Description => "Merit function: list, add, remove, edit, clear, evaluate";

        public string Help => @"[bold]merit[/] - Merit function operations
  [green]merit list[/]                           List all operands
  [green]merit types[/]                          List all available operand types
  [green]merit add <type> [[params...]][/]          Add an operand
  [green]merit remove <index>[/]                  Remove operand at index (1-indexed)
  [green]merit edit <index> <param>=<value>[/]    Edit operand parameters (1-indexed)
  [green]merit clear[/]                           Clear all operands
  [green]merit evaluate[/]                        Evaluate merit function
  [green]merit save <path>[/]                     Save merit table to .mft file (settings only)
  [green]merit open <path>[/]                     Load merit table from .mft file (clamps surface refs)

  Example: merit add EFL target=100 weight=1
  Example: merit add CTA surface1=1 surface2=6 min=0.5
  Example: merit add SPOTM rings=6 arms=12 weight=1
  Example: merit add WAVEX rings=6 arms=8
  Example: merit add ET surface1=1 surface2=8 min=0.5      (min edge thickness across surfaces 1-8)
  Example: merit add CTA surface1=1 surface2=8 min=0.5     (min center thickness, air gaps only)
  Example: merit add CTG surface1=1 surface2=8 min=2.0     (min center thickness, glass only)
  Example: merit add ET surface1=1 surface2=8 max=25       (max edge thickness across surfaces 1-8)
  Example: merit add CT surface1=1 surface2=8 min=2 max=25 (both min and max in one operand)";

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
                    ListOperands(session);
                    break;
                case "types":
                    ListTypes();
                    break;
                case "add":
                    AddOperand(session, args);
                    break;
                case "remove":
                    RemoveOperand(session, args);
                    break;
                case "edit":
                    EditOperand(session, args);
                    break;
                case "clear":
                    ClearOperands(session);
                    break;
                case "evaluate":
                    EvaluateMerit(session);
                    break;
                case "save":
                    SaveMeritTable(session, args);
                    break;
                case "open":
                case "load":
                    OpenMeritTable(session, args);
                    break;
                // DEBUG ONLY — Remove before release
                case "debug-expand":
                    DebugExpandedOperands(session);
                    break;
                default:
                    AnsiConsole.MarkupLine($"[yellow]Unknown subcommand: {Markup.Escape(args[0])}[/]");
                    break;
            }
        }

        private void ListOperands(Session session)
        {
            var mf = session.EnsureMeritFunction();

            if (mf.Operands.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No operands defined.[/]");
                return;
            }

            // Evaluate before listing so values are up to date
            if (session.CurrentSystem != null)
            {
                try
                {
                    var glassMgr = session.EnsureGlassCatalog();
                    var evaluator = new MeritFunctionEvaluator(session.CurrentSystem, glassMgr, session.ConfigEditor);
                    evaluator.Evaluate(mf);
                }
                catch { /* show stale values if evaluation fails */ }
            }

            var table = new Table();
            table.AddColumn("#");
            table.AddColumn("Type");
            table.AddColumn("Wave");
            table.AddColumn("Surf");
            table.AddColumn("Sampling");
            table.AddColumn("Target");
            table.AddColumn("Weight");
            table.AddColumn("Min");
            table.AddColumn("Max");
            table.AddColumn("Value");
            table.AddColumn("Error");

            // Resolve primary wavelength for display
            int primaryWave = session.CurrentSystem?.PrimaryWavelengthIndex ?? 0;

            for (int i = 0; i < mf.Operands.Count; i++)
            {
                var op = mf.Operands[i];
                bool isMacro = IsMacroType(op.Type);
                bool isBoundary = IsBoundaryType(op.Type);
                string waveStr = isMacro ? "All" :
                    isBoundary ? "" :
                    (op.WaveIndex < 0 ? (primaryWave + 1).ToString() : (op.WaveIndex + 1).ToString());
                string surfStr;
                if (IsBoundaryType(op.Type) && op.Surface2 > 0)
                    surfStr = $"{op.Surface1}-{op.Surface2}";
                else if (op.SurfaceIndex > 0)
                    surfStr = op.SurfaceIndex.ToString();
                else if (op.Surface1 > 0)
                    surfStr = op.Surface1.ToString();
                else
                    surfStr = "";

                // Macro sampling info (show effective values with defaults applied)
                string samplingStr = "";
                if (isMacro)
                {
                    bool isRect = op.Type == OperandType.WAVEXR || op.Type == OperandType.WAVEMR
                        || op.Type == OperandType.WAVECR || op.Type == OperandType.SPOTMR
                        || op.Type == OperandType.SPOTR;
                    if (isRect)
                        samplingStr = $"grid={op.EffectiveGridSize}";
                    else
                        samplingStr = $"R={op.EffectiveRings} A={op.EffectiveArms}";
                }

                // Compute error contribution
                double error = 0;
                if (op.IsTargetActive)
                {
                    error = (op.Value - op.Target) * op.Weight;
                }
                else
                {
                    if (op.Minimum.HasValue && op.Value < op.Minimum.Value)
                        error = (op.Value - op.Minimum.Value) * op.Weight;
                    else if (op.Maximum.HasValue && op.Value > op.Maximum.Value)
                        error = (op.Value - op.Maximum.Value) * op.Weight;
                }

                table.AddRow(
                    (i + 1).ToString(),
                    op.Type.ToString(),
                    waveStr,
                    surfStr,
                    samplingStr,
                    op.IsTargetActive ? op.Target.ToString("G6") : "---",
                    op.Weight.ToString("G4"),
                    op.Minimum.HasValue ? op.Minimum.Value.ToString("G6") : "---",
                    op.Maximum.HasValue ? op.Maximum.Value.ToString("G6") : "---",
                    op.Value.ToString("E4"),
                    error != 0 ? error.ToString("E4") : "0"
                );
            }

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"Merit Value: {mf.MeritValue:E6}");
        }

        private void ListTypes()
        {
            var table = new Table();
            table.AddColumn("Type");
            table.AddColumn("Description");
            table.AddColumn("Parameters");

            // Ray intercept operands
            table.AddRow("RX", "Real ray X intercept", "surface, wave, hx, hy, px, py");
            table.AddRow("RY", "Real ray Y intercept", "surface, wave, hx, hy, px, py");
            table.AddRow("RZ", "Real ray Z intercept", "surface, wave, hx, hy, px, py");
            table.AddRow("RL", "Real ray L direction cosine", "surface, wave, hx, hy, px, py");
            table.AddRow("RM", "Real ray M direction cosine", "surface, wave, hx, hy, px, py");
            table.AddRow("RN", "Real ray N direction cosine", "surface, wave, hx, hy, px, py");
            table.AddRow("RI", "Angle of incidence (degrees)", "surf, surf2 (boundary range)");
            table.AddRow("RE", "Angle of exitance (degrees)", "surf, surf2 (boundary range)");
            table.AddRow("AOID", "Angle of incidence (degrees)", "surface, wave, hx, hy, px, py");
            table.AddRow("AOED", "Angle of exitance (degrees)", "surface, wave, hx, hy, px, py");
            table.AddRow("AOER", "Angle of exitance (radians)", "surface, wave, hx, hy, px, py");
            table.AddRow("AOIR", "Angle of incidence (radians)", "surface, wave, hx, hy, px, py");

            // Sensitivity macro (expands like WAVEX)
            table.AddRow("SENS", "Sensitivity Composite", "rings, arms");

            // Paraxial operands
            table.AddRow("PL", "Paraxial ray L direction cosine", "surface, wave, hx, hy, px, py");
            table.AddRow("PM", "Paraxial ray M direction cosine", "surface, wave, hx, hy, px, py");
            table.AddRow("PN", "Paraxial ray N direction cosine", "surface, wave, hx, hy, px, py");
            table.AddRow("PX", "Paraxial ray X", "surface, wave, hx, hy, px, py");
            table.AddRow("PY", "Paraxial ray Y", "surface, wave, hx, hy, px, py");
            table.AddRow("PZ", "Paraxial ray Z", "surface, wave, hx, hy, px, py");

            // Wavefront macro operands
            table.AddRow("WAVEX", "Wave Error Composite (Chief Ray Ref, Tilt Removed)", "rings, arms");
            table.AddRow("WAVEM", "Wave Error Composite (Centroid Reference)", "rings, arms");
            table.AddRow("WAVEC", "Wave Error Composite (Chief Ray Reference)", "rings, arms");
            table.AddRow("WAVEXR", "Wave Error Composite (Chief Ray Ref, Tilt Removed, Grid)", "gridsize");
            table.AddRow("WAVEMR", "Wave Error Composite (Centroid Reference, Grid)", "gridsize");
            table.AddRow("WAVECR", "Wave Error Composite (Chief Ray Reference, Grid)", "gridsize");

            // Spot macro operands
            table.AddRow("SPOTM", "RMS spot size (centroid reference)", "rings, arms");
            table.AddRow("SPOT", "RMS spot size (chief ray reference)", "rings, arms");
            table.AddRow("SPOTMR", "RMS spot size (centroid reference, grid)", "gridsize");
            table.AddRow("SPOTR", "RMS spot size (chief ray reference, grid)", "gridsize");

            // System operands
            table.AddRow("EFL", "Effective focal length", "wave");
            table.AddRow("MAG", "Paraxial magnification", "wave");
            table.AddRow("AMAG", "Angular magnification", "wave");
            table.AddRow("EXPZ", "Exit pupil position", "wave");
            table.AddRow("ENPZ", "Entrance pupil position", "wave");
            table.AddRow("ENPD", "Entrance pupil diameter", "wave");
            table.AddRow("EXPD", "Exit pupil diameter", "wave");
            table.AddRow("TTRACK", "Total track length", "wave");
            table.AddRow("CFS", "Chromatic focal shift (max range across wavelengths)", "none");
            table.AddRow("ILL", "Relative illumination (F/#_0 / F/#_field)²", "hy");
            table.AddRow("DITAN", "Max F-tan(θ) distortion (%)", "none");
            table.AddRow("DITHETA", "Max F-θ distortion (%)", "none");


            // Boundary operands — scan surface range, use min/max to constrain
            table.AddRow("CV", "Curvature (all surfaces)", "surface1, surface2, min, max");
            table.AddRow("CVA", "Curvature (air only)", "surface1, surface2, min, max");
            table.AddRow("CVG", "Curvature (glass only)", "surface1, surface2, min, max");
            table.AddRow("CT", "Center thickness (all)", "surface1, surface2, min, max");
            table.AddRow("CTA", "Center thickness (air only)", "surface1, surface2, min, max");
            table.AddRow("CTG", "Center thickness (glass only)", "surface1, surface2, min, max");
            table.AddRow("ET", "Edge thickness (all)", "surface1, surface2, min, max");
            table.AddRow("EA", "Edge thickness (air only)", "surface1, surface2, min, max");
            table.AddRow("EG", "Edge thickness (glass only)", "surface1, surface2, min, max");
            table.AddRow("SD", "Semi-diameter", "surface1, surface2, min, max");

            // Surface property operands
            table.AddRow("DM", "Surface diameter", "surface1");

            // Arithmetic operands
            table.AddRow("MULTC", "Multiply operand by constant", "operand, factor");
            table.AddRow("SUMR", "Sum of operand values Op1..Op2", "operand, operand2");
            table.AddRow("SUM", "Op1 + Op2", "operand, operand2");
            table.AddRow("DIV", "Op1 / Op2", "operand, operand2");
            table.AddRow("MULT", "Op1 * Op2", "operand, operand2");
            table.AddRow("DIFF", "Op1 - Op2", "operand, operand2");
            table.AddRow("DEV", "Deviation across range Op1..Op2", "operand, operand2");
            table.AddRow("QSUMR", "RSS of operand values Op1..Op2", "operand, operand2");

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine("\n[dim]Common params for all: target, weight, min, max, config, opcode[/]");
        }

        private void AddOperand(Session session, string[] args)
        {
            if (args.Length < 2)
            {
                AnsiConsole.MarkupLine("[red]Usage: merit add <type> [params...][/]");
                return;
            }

            if (!Enum.TryParse<OperandType>(args[1], true, out var type))
            {
                AnsiConsole.MarkupLine($"[red]Unknown operand type: {Markup.Escape(args[1])}[/]");
                return;
            }

            var op = new Operand { Type = type };

            // Parse key=value params (handle spaces around '=')
            var merged = MergeKeyValueArgs(args, 2);
            foreach (var kv in merged)
            {
                ParseOperandParam(op, kv);
            }

            var mf = session.EnsureMeritFunction();
            mf.AddOperand(op);
            AnsiConsole.MarkupLine($"[green]Added {type} operand (index {mf.Operands.Count})[/]");
        }

        private void RemoveOperand(Session session, string[] args)
        {
            if (args.Length < 2 || !int.TryParse(args[1], out int idx))
            {
                AnsiConsole.MarkupLine("[red]Usage: merit remove <index> (1-indexed)[/]");
                return;
            }

            idx--; // convert to 0-indexed
            var mf = session.EnsureMeritFunction();
            if (idx < 0 || idx >= mf.Operands.Count)
            {
                AnsiConsole.MarkupLine("[red]Index out of range.[/]");
                return;
            }

            mf.RemoveOperand(idx);
            AnsiConsole.MarkupLine($"[green]Removed operand {idx + 1}.[/]");
        }

        private void EditOperand(Session session, string[] args)
        {
            if (args.Length < 3 || !int.TryParse(args[1], out int idx))
            {
                AnsiConsole.MarkupLine("[red]Usage: merit edit <index> <param>=<value> ... (1-indexed)[/]");
                return;
            }

            idx--; // convert to 0-indexed
            var mf = session.EnsureMeritFunction();
            if (idx < 0 || idx >= mf.Operands.Count)
            {
                AnsiConsole.MarkupLine("[red]Index out of range.[/]");
                return;
            }

            var op = mf.Operands[idx];
            var merged = MergeKeyValueArgs(args, 2);
            foreach (var kv in merged)
            {
                ParseOperandParam(op, kv);
            }

            AnsiConsole.MarkupLine($"[green]Operand {idx + 1} updated.[/]");
        }

        private void ClearOperands(Session session)
        {
            session.EnsureMeritFunction().Clear();
            AnsiConsole.MarkupLine("[green]Merit function cleared.[/]");
        }

        private void SaveMeritTable(Session session, string[] args)
        {
            if (args.Length < 2)
            {
                AnsiConsole.MarkupLine("[red]Usage: merit save <path.mft>[/]");
                return;
            }
            var mf = session.EnsureMeritFunction();
            string path = args[1];
            if (!path.EndsWith(MeritFunctionTableIO.FileExtension, StringComparison.OrdinalIgnoreCase))
                path += MeritFunctionTableIO.FileExtension;
            MeritFunctionTableIO.Save(mf, path);
            AnsiConsole.MarkupLine($"[green]Saved {mf.Operands.Count} operands to {Markup.Escape(path)}[/]");
        }

        private void OpenMeritTable(Session session, string[] args)
        {
            if (args.Length < 2)
            {
                AnsiConsole.MarkupLine("[red]Usage: merit open <path.mft>[/]");
                return;
            }
            int surfaceCount = session.CurrentSystem?.Surfaces.Count ?? 0;
            var mf = MeritFunctionTableIO.Load(args[1], surfaceCount);
            session.CurrentMeritFunction = mf;
            AnsiConsole.MarkupLine($"[green]Loaded {mf.Operands.Count} operands from {Markup.Escape(args[1])}[/]");
        }

        private void EvaluateMerit(Session session)
        {
            var system = session.EnsureSystem();
            var mf = session.EnsureMeritFunction();
            var glassMgr = session.EnsureGlassCatalog();

            var evaluator = new MeritFunctionEvaluator(system, glassMgr, session.ConfigEditor)
                { ParallelEvaluation = true };
            double merit = evaluator.Evaluate(mf);

            AnsiConsole.MarkupLine($"[bold]Merit Value: {merit:E6}[/]");
            ListOperands(session);
        }

        // DEBUG ONLY — Remove before release
        private void DebugExpandedOperands(Session session)
        {
            var system = session.EnsureSystem();
            var mf = session.EnsureMeritFunction();
            var glassMgr = session.EnsureGlassCatalog();

            var evaluator = new MeritFunctionEvaluator(system, glassMgr, session.ConfigEditor);
            var expanded = evaluator.DebugExpandAndEvaluate(mf);

            string outPath = System.IO.Path.Combine(
                Environment.CurrentDirectory,
                "merit_debug_expand.txt");

            using (var w = new System.IO.StreamWriter(outPath, false, System.Text.Encoding.UTF8))
            {
                w.WriteLine($"Merit Debug Expand — {expanded.Count} operands");
                w.WriteLine($"Fields: {system.Fields.Count}, Wavelengths: {system.Wavelengths.Count}");
                w.WriteLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                w.WriteLine();

                // Column headers
                w.WriteLine($"{"#",6}\t{"Type",-6}\t{"Wave",4}\t{"Hy",10}\t{"Hx",10}\t{"Px",10}\t{"Py",10}\t{"Weight",12}\t{"Value",12}\t{"Weighted",12}\t{"Residual",12}");

                for (int i = 0; i < expanded.Count; i++)
                {
                    var op = expanded[i];
                    double weighted = op.Value * op.Weight;
                    double residual = (op.Value - op.Target) * op.Weight;
                    w.WriteLine($"{i + 1,6}\t{op.Type,-6}\t{op.WaveIndex + 1,4}\t{op.Hy,10:F6}\t{op.Hx,10:F6}\t{op.Px,10:F6}\t{op.Py,10:F6}\t{op.Weight,12:E6}\t{op.Value,12:E6}\t{weighted,12:E6}\t{residual,12:E6}");
                }

                // Summary by field/wave
                w.WriteLine();
                w.WriteLine("=== Summary by Field / Wavelength ===");
                w.WriteLine($"{"Field/Wave",-20}\t{"Pts",6}\t{"RMS Value",12}\t{"Sum Weight^2",14}");

                var summary = new Dictionary<string, (double sumSq, double sumW, int count)>();
                foreach (var op in expanded)
                {
                    if (op.Weight == 0) continue;
                    string key = $"Hy={op.Hy:F4} W{op.WaveIndex + 1}";
                    if (!summary.ContainsKey(key))
                        summary[key] = (0, 0, 0);
                    var (ss, sw, c) = summary[key];
                    summary[key] = (ss + op.Value * op.Value * op.Weight * op.Weight,
                                     sw + op.Weight * op.Weight, c + 1);
                }

                foreach (var kvp in summary)
                {
                    var (ss, sw, c) = kvp.Value;
                    double rms = sw > 0 ? Math.Sqrt(ss / sw) : 0;
                    w.WriteLine($"{kvp.Key,-20}\t{c,6}\t{rms,12:F6}\t{sw,14:E6}");
                }

                // Grand total
                double totalSumSq = 0, totalSumW = 0;
                int totalCount = 0;
                foreach (var (ss, sw, c) in summary.Values)
                {
                    totalSumSq += ss;
                    totalSumW += sw;
                    totalCount += c;
                }
                double grandRms = totalSumW > 0 ? Math.Sqrt(totalSumSq / totalSumW) : 0;
                w.WriteLine();
                w.WriteLine($"Total: {totalCount} operands, Grand RMS: {grandRms:F6}");
            }

            AnsiConsole.MarkupLine($"[green]Debug expand written to: {Markup.Escape(outPath)} ({expanded.Count} operands)[/]");
        }

        /// <summary>
        /// Merge args that were split around '='. E.g. ["weight", "=", "100"] -> ["weight=100"]
        /// Also handles "weight=" "100" and "weight" "=100".
        /// </summary>
        private static List<string> MergeKeyValueArgs(string[] args, int startIndex)
        {
            var result = new List<string>();
            int i = startIndex;
            while (i < args.Length)
            {
                var arg = args[i];
                if (arg == "=" && result.Count > 0 && i + 1 < args.Length)
                {
                    // "key" "=" "value" -> "key=value"
                    result[result.Count - 1] += "=" + args[i + 1];
                    i += 2;
                }
                else if (arg.EndsWith("=") && i + 1 < args.Length && !args[i + 1].Contains("="))
                {
                    // "key=" "value" -> "key=value"
                    result.Add(arg + args[i + 1]);
                    i += 2;
                }
                else if (arg.StartsWith("=") && result.Count > 0)
                {
                    // "key" "=value" -> "key=value"
                    result[result.Count - 1] += arg;
                    i++;
                }
                else
                {
                    result.Add(arg);
                    i++;
                }
            }
            return result;
        }

        private static void ParseOperandParam(Operand op, string param)
        {
            var kv = param.Split(new[] { '=' }, 2);
            if (kv.Length != 2) return;

            var key = kv[0].ToLowerInvariant();
            var val = kv[1];

            switch (key)
            {
                case "target":
                    if (TryParse(val, out double t)) op.Target = t;
                    break;
                case "weight":
                    if (TryParse(val, out double w)) op.Weight = w;
                    break;
                case "min":
                    if (TryParse(val, out double mn)) op.Minimum = mn;
                    break;
                case "max":
                    if (TryParse(val, out double mx)) op.Maximum = mx;
                    break;
                case "surface":
                    if (int.TryParse(val, out int s)) { op.SurfaceIndex = s; op.Surface1 = s; }
                    break;
                case "surface1":
                    if (int.TryParse(val, out int s1)) op.Surface1 = s1;
                    break;
                case "surface2":
                    if (int.TryParse(val, out int s2)) op.Surface2 = s2;
                    break;
                case "wave":
                    if (int.TryParse(val, out int wi)) op.WaveIndex = wi - 1; // CLI is 1-indexed
                    break;
                case "hx":
                    if (TryParse(val, out double hx)) op.Hx = hx;
                    break;
                case "hy":
                    if (TryParse(val, out double hy)) op.Hy = hy;
                    break;
                case "px":
                    if (TryParse(val, out double px)) op.Px = px;
                    break;
                case "py":
                    if (TryParse(val, out double py)) op.Py = py;
                    break;
                case "rings":
                    if (int.TryParse(val, out int rings)) op.Rings = rings;
                    break;
                case "arms":
                    if (int.TryParse(val, out int arms)) op.Arms = arms;
                    break;
                case "gridsize":
                    if (int.TryParse(val, out int gs)) op.GridSize = gs;
                    break;
                case "config":
                    if (int.TryParse(val, out int cfg)) op.ConfigurationNo = cfg;
                    break;
                case "opcode":
                    if (Enum.TryParse<OperationCode>(val, true, out var oc)) op.OpCode = oc;
                    break;
                case "operand":
                    if (int.TryParse(val, out int opn)) op.OperandNo = opn - 1; // CLI is 1-indexed
                    break;
                case "operand2":
                    if (int.TryParse(val, out int opn2)) op.OperandNo2 = opn2 - 1; // CLI is 1-indexed
                    break;
                case "factor":
                    if (TryParse(val, out double f)) op.Factor = f;
                    break;
            }
        }

        private static bool TryParse(string s, out double value)
        {
            return double.TryParse(s, NumberStyles.Float | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture, out value);
        }

        private static bool IsMacroType(OperandType type)
        {
            switch (type)
            {
                case OperandType.WAVEX:
                case OperandType.WAVEM:
                case OperandType.WAVEC:
                case OperandType.WAVEXR:
                case OperandType.WAVEMR:
                case OperandType.WAVECR:
                case OperandType.SPOTM:
                case OperandType.SPOT:
                case OperandType.SPOTMR:
                case OperandType.SPOTR:
                case OperandType.SENS:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsBoundaryType(OperandType type)
        {
            switch (type)
            {
                case OperandType.CV:
                case OperandType.CVA:
                case OperandType.CVG:
                case OperandType.CT:
                case OperandType.CTA:
                case OperandType.CTG:
                case OperandType.ET:
                case OperandType.EA:
                case OperandType.EG:
                case OperandType.SD:
                    return true;
                default:
                    return false;
            }
        }
    }
}
