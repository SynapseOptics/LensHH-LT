using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LensHH.Core.MeritFunction;
using LensHH.Core.Optimization;
using ModelContextProtocol.Server;

namespace LensHH.Mcp.Tools
{
    [McpServerToolType]
    public class OptimizationTools
    {
        private readonly McpSession _session;
        public OptimizationTools(McpSession session) => _session = session;

        [McpServerTool, Description("Add a merit function operand. type is the operand type (e.g. EFL, BFL, CV, RX, RY, WAVEX, CTA, CT). target is the desired value. weight is the importance. Optional: surface (Surface1), surface2 (Surface2, for span boundary operands), wave, min, max, operationCode (NONE,SINE,COSINE,ACOS,ASIN,TANGENT,ATN,SQRT,ABSO). Boundary operands (CT, CTA, CTG, ET, EA, EG, CV, CVA, CVG, SD, DTRG, RI, RE) scan [surface, surface2] and use min/max. Surface sentinels: 0 = mirror the other endpoint (single-surface span); -1 = last refractive surface (count − 2); -2 = image; -3 = first surface after stop; -4 = stop surface; -5 = first surface after OBJ (position 1). When omitted, surface2 defaults to surface so single-surface operands work without an extra parameter.")]
        public string AddOperand(string type, double target = 0, double weight = 1,
            int surface = 0, int? surface2 = null, int wave = 0, double? min = null, double? max = null,
            string operationCode = "NONE")
        {
            if (!Enum.TryParse<OperandType>(type, true, out var opType))
                return $"Unknown operand type '{type}'. Use types like EFL, BFL, CV, RX, RY, WAVEX, SPOTM, SPOT, CTA, CT, etc.";

            if (!Enum.TryParse<Core.Enums.OperationCode>(operationCode, true, out var opCode))
                opCode = Core.Enums.OperationCode.None;

            var operand = new Operand
            {
                Type = opType,
                Target = target,
                Weight = weight,
                Surface1 = surface,
                // Default: single-surface span. The evaluator's sentinel resolver
                // treats Surface2=0 as "mirror Surface1", so an `add_operand(...,
                // surface=5)` call without surface2 evaluates as if the user had
                // typed surface2=5 — fixing the long-standing trap where
                // boundary operands authored via add_operand never scanned any
                // surfaces because Surface2 silently stayed at 0.
                Surface2 = surface2 ?? surface,
                WaveIndex = wave,
                OpCode = opCode
            };

            if (min.HasValue) operand.Minimum = min.Value;
            if (max.HasValue) operand.Maximum = max.Value;

            if (_session.MeritFunction == null)
                _session.MeritFunction = new MeritFunction();

            _session.MeritFunction.Operands.Add(operand);
            return $"Added {type} operand (target={target}, weight={weight}, " +
                   $"surface={surface}, surface2={operand.Surface2}). " +
                   $"Total operands: {_session.MeritFunction.Operands.Count}.";
        }

        [McpServerTool, Description("List all merit function operands.")]
        public string ListOperands()
        {
            if (_session.MeritFunction == null || _session.MeritFunction.Operands.Count == 0)
                return "No operands in merit function.";

            var sb = new StringBuilder();
            sb.AppendLine($"{"#",4} {"Type",-12} {"Target",10} {"Weight",8} {"Min",10} {"Max",10} {"Surf1",6} {"Surf2",6} {"Wave",5}");

            for (int i = 0; i < _session.MeritFunction.Operands.Count; i++)
            {
                var op = _session.MeritFunction.Operands[i];
                string minStr = op.Minimum.HasValue ? op.Minimum.Value.ToString("G4") : "---";
                string maxStr = op.Maximum.HasValue ? op.Maximum.Value.ToString("G4") : "---";
                sb.AppendLine($"{i,4} {op.Type,-12} {op.Target,10:G4} {op.Weight,8:F2} {minStr,10} {maxStr,10} {op.Surface1,6} {op.Surface2,6} {op.WaveIndex,5}");
            }
            return sb.ToString();
        }

        [McpServerTool, Description("Clear all merit function operands.")]
        public string ClearOperands()
        {
            if (_session.MeritFunction != null)
                _session.MeritFunction.Operands.Clear();
            return "Merit function cleared.";
        }

        [McpServerTool, Description("Remove a merit function operand by index.")]
        public string RemoveOperand(int index)
        {
            if (_session.MeritFunction == null || index < 0 || index >= _session.MeritFunction.Operands.Count)
                return "Invalid operand index.";

            _session.MeritFunction.Operands.RemoveAt(index);
            return $"Operand {index} removed. Total operands: {_session.MeritFunction.Operands.Count}.";
        }

        [McpServerTool, Description("Edit a merit function operand in place by index. Only provided parameters are changed; omit parameters to keep their current values. index is 0-based. surface sets Surface1, surface2 sets Surface2 (the span end for boundary operands). Surface sentinels: 0 = mirror the other endpoint; -1 = last refractive surface; -2 = image; -3 = first surface after stop; -4 = stop surface; -5 = first surface after OBJ (position 1).")]
        public string EditOperand(int index, double? target = null, double? weight = null,
            int? surface = null, int? surface2 = null, int? wave = null, double? min = null, double? max = null,
            string? operationCode = null)
        {
            if (_session.MeritFunction == null || index < 0 || index >= _session.MeritFunction.Operands.Count)
                return "Invalid operand index.";

            var op = _session.MeritFunction.Operands[index];
            if (target.HasValue) op.Target = target.Value;
            if (weight.HasValue) op.Weight = weight.Value;
            if (surface.HasValue) op.Surface1 = surface.Value;
            if (surface2.HasValue) op.Surface2 = surface2.Value;
            if (wave.HasValue) op.WaveIndex = wave.Value;
            if (min.HasValue) op.Minimum = min.Value;
            if (max.HasValue) op.Maximum = max.Value;
            if (operationCode != null && Enum.TryParse<Core.Enums.OperationCode>(operationCode, true, out var opCode))
                op.OpCode = opCode;

            return $"Operand {index} ({op.Type}) updated.";
        }

        [McpServerTool, Description("Evaluate the current merit function and return the merit value and individual operand values.")]
        public string EvaluateMerit()
        {
            { var ge = _session.ValidateGlass(); if (ge != null) return ge; }
            if (_session.MeritFunction == null || _session.MeritFunction.Operands.Count == 0)
                return "No operands in merit function.";

            var evaluator = new MeritFunctionEvaluator(_session.System, _session.GlassCatalog)
                { ParallelEvaluation = true };
            double merit = evaluator.Evaluate(_session.MeritFunction);

            var sb = new StringBuilder();
            sb.AppendLine($"Merit Function Value: {merit:E6}");
            sb.AppendLine();
            sb.AppendLine($"{"#",4} {"Type",-12} {"Value",12} {"Target",10} {"Residual",12}");

            for (int i = 0; i < _session.MeritFunction.Operands.Count; i++)
            {
                var op = _session.MeritFunction.Operands[i];
                // Use the residual the evaluator already computed (which correctly
                // handles Min/Max bounds for boundary operands like CTA / EA / CTG
                // / EG). Previously we recomputed here as
                //   op.IsTargetActive ? (Value-Target)*Weight : 0
                // which silently displayed 0 for every bounded operand even when
                // its Value was outside [Min, Max] — making boundary-constraint
                // violations invisible in the diagnostic table, though the
                // optimizer was correctly seeing the penalty.
                double residual = op.Residual;
                sb.AppendLine($"{i,4} {op.Type,-12} {op.Value,12:G6} {op.Target,10:G4} {residual,12:E4}");
            }
            return sb.ToString();
        }

        [McpServerTool, Description("Save the current merit function table to a .mft file. Only settings are written (no evaluated values). filePath may omit the .mft extension.")]
        public string SaveMeritFunctionTable(
            [Description("Output file path (.mft). Extension appended if missing.")] string filePath)
        {
            if (_session.MeritFunction == null || _session.MeritFunction.Operands.Count == 0)
                return "No operands in merit function.";
            if (!filePath.EndsWith(MeritFunctionTableIO.FileExtension, System.StringComparison.OrdinalIgnoreCase))
                filePath += MeritFunctionTableIO.FileExtension;
            MeritFunctionTableIO.Save(_session.MeritFunction, filePath);
            return $"Saved {_session.MeritFunction.Operands.Count} operands to {filePath}.";
        }

        [McpServerTool, Description("Load a merit function table from a .mft file, replacing the current merit function. Surface references past the last surface of the current system are clamped automatically.")]
        public string OpenMeritFunctionTable(
            [Description("Path to a .mft file.")] string filePath)
        {
            int surfaceCount = _session.System?.Surfaces.Count ?? 0;
            var mf = MeritFunctionTableIO.Load(filePath, surfaceCount);
            _session.MeritFunction = mf;
            return $"Loaded {mf.Operands.Count} operands from {filePath}.";
        }

        [McpServerTool, Description("Run local optimization (damped least squares). Auto-applies the result to the live system — caller will not get a chance to revert. Use optimize_try if you want a keep-or-revert decision after seeing the merit. maxIterations defaults to 4000 (LM normally converges well before this), tolerance to 1e-10, dampingFactor to 0.001. useBroydenUpdate (default true) uses a rank-1 Jacobian update between full rebuilds — disable to force a full finite-difference Jacobian every step (slower but more robust on stiff designs). broydenRefreshInterval (default 5) is the number of accepted steps between forced Jacobian rebuilds. Returns the final merit value.")]
        public string Optimize(int maxIterations = 4000, double tolerance = 1e-10, double dampingFactor = 0.001,
            bool useBroydenUpdate = true, int broydenRefreshInterval = 5)
        {
            { var ge = _session.ValidateGlass(); if (ge != null) return ge; }
            if (_session.MeritFunction == null || _session.MeritFunction.Operands.Count == 0)
                return "No operands in merit function. Add operands first.";

            var optimizer = new LocalOptimizer(_session.System, _session.MeritFunction, _session.GlassCatalog)
                { ParallelEvaluation = true };
            optimizer.MaxIterations = maxIterations;
            optimizer.Tolerance = tolerance;
            optimizer.InitialDamping = dampingFactor;
            optimizer.UseBroydenUpdate = useBroydenUpdate;
            optimizer.BroydenRefreshInterval = broydenRefreshInterval;
            optimizer.CollectVariables();

            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var optimResult = optimizer.Optimize(cts.Token);

            double initialMerit = optimResult.InitialMerit;
            double finalMerit = optimResult.FinalMerit;
            int iterations = optimResult.Iterations;

            var sb = new StringBuilder();
            sb.AppendLine("Optimization Complete");
            sb.AppendLine($"  Iterations:    {iterations}");
            sb.AppendLine($"  Initial Merit: {initialMerit:E6}");
            sb.AppendLine($"  Final Merit:   {finalMerit:E6}");
            sb.AppendLine($"  Improvement:   {(initialMerit > 0 ? (1 - finalMerit / initialMerit) * 100 : 0):F1}%");
            return sb.ToString();
        }

        [McpServerTool, Description("Run local optimization but stage the result for the user to keep or revert. Snapshots the system before running, mutates it in place during the run, then awaits a follow-up call to optimize_keep_result (commit) or optimize_revert_result (restore the pre-run snapshot). Returns the merit comparison and explicit instructions for the next call. Same parameters as optimize, including useBroydenUpdate / broydenRefreshInterval.")]
        public string OptimizeTry(int maxIterations = 4000, double tolerance = 1e-10, double dampingFactor = 0.001,
            bool useBroydenUpdate = true, int broydenRefreshInterval = 5)
        {
            { var ge = _session.ValidateGlass(); if (ge != null) return ge; }
            if (_session.MeritFunction == null || _session.MeritFunction.Operands.Count == 0)
                return "No operands in merit function. Add operands first.";

            string warning = _session.HasPendingOptimizeResult
                ? "WARNING: A previous optimize_try result was still pending; the prior snapshot has been overwritten. The earlier result is now permanent.\n\n"
                : "";

            _session.CaptureOptimizeSnapshot();

            var optimizer = new LocalOptimizer(_session.System, _session.MeritFunction, _session.GlassCatalog)
                { ParallelEvaluation = true };
            optimizer.MaxIterations = maxIterations;
            optimizer.Tolerance = tolerance;
            optimizer.InitialDamping = dampingFactor;
            optimizer.UseBroydenUpdate = useBroydenUpdate;
            optimizer.BroydenRefreshInterval = broydenRefreshInterval;
            optimizer.CollectVariables();

            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var optimResult = optimizer.Optimize(cts.Token);

            double initialMerit = optimResult.InitialMerit;
            double finalMerit = optimResult.FinalMerit;
            int iterations = optimResult.Iterations;
            double improvement = initialMerit > 0 ? (1 - finalMerit / initialMerit) * 100 : 0;

            var sb = new StringBuilder();
            sb.Append(warning);
            sb.AppendLine("Optimization Complete (PENDING — call optimize_keep_result or optimize_revert_result)");
            sb.AppendLine($"  Iterations:    {iterations}");
            sb.AppendLine($"  Initial Merit: {initialMerit:E6}");
            sb.AppendLine($"  Final Merit:   {finalMerit:E6}");
            sb.AppendLine($"  Improvement:   {improvement:F1}%");
            sb.AppendLine();
            sb.AppendLine("Next step:");
            sb.AppendLine("  optimize_keep_result   — commit the optimized values to the system");
            sb.AppendLine("  optimize_revert_result — restore the pre-optimization snapshot");
            return sb.ToString();
        }

        [McpServerTool, Description("Commit the most recent optimize_try result by discarding the snapshot. The system already holds the optimized values; this just clears the pending revert state.")]
        public string OptimizeKeepResult()
        {
            if (!_session.ClearOptimizeSnapshot())
                return "No pending optimize_try result to keep. Run optimize_try first.";
            return "Optimized result kept. The system now holds the post-optimization values.";
        }

        [McpServerTool, Description("Revert the most recent optimize_try result by restoring the pre-run snapshot of system + merit function + configuration editor. After this, the system is back to its state immediately before optimize_try was called.")]
        public string OptimizeRevertResult()
        {
            if (!_session.RevertOptimizeSnapshot())
                return "No pending optimize_try result to revert. Run optimize_try first.";
            return "Reverted. The system is back to its pre-optimize_try state.";
        }

        [McpServerTool, Description("List all optimization variables in the current system (surfaces with variable flags set).")]
        public string ListVariables()
        {
            var sys = _session.System;
            var sb = new StringBuilder();
            sb.AppendLine("Optimization Variables:");

            int count = 0;
            for (int i = 0; i < sys.Surfaces.Count; i++)
            {
                var s = sys.Surfaces[i];
                if (s.CurvatureVariable)
                {
                    sb.AppendLine($"  Surface {i} Curvature" +
                                  (s.CurvatureMin.HasValue || s.CurvatureMax.HasValue
                                      ? $" [{s.CurvatureMin?.ToString("G4") ?? "---"}, {s.CurvatureMax?.ToString("G4") ?? "---"}]" : ""));
                    count++;
                }
                if (s.ThicknessVariable)
                {
                    sb.AppendLine($"  Surface {i} Thickness" +
                                  (s.ThicknessMin != double.MinValue || s.ThicknessMax != double.MaxValue
                                      ? $" [{s.ThicknessMin:G4}, {s.ThicknessMax:G4}]" : ""));
                    count++;
                }
                if (s.ConicVariable)
                {
                    sb.AppendLine($"  Surface {i} Conic" +
                                  (s.ConicMin != double.MinValue || s.ConicMax != double.MaxValue
                                      ? $" [{s.ConicMin:G4}, {s.ConicMax:G4}]" : ""));
                    count++;
                }
                for (int j = 0; j < 8; j++)
                {
                    if (s.AsphericVariable[j])
                    {
                        sb.AppendLine($"  Surface {i} Aspheric[{j}]");
                        count++;
                    }
                }
            }

            if (count == 0)
                sb.AppendLine("  (none)");
            else
                sb.AppendLine($"\nTotal: {count} variables");

            return sb.ToString();
        }

        [McpServerTool, Description("Set or clear curvature variables for a range of surfaces. skipInfiniteRadius=true skips flat surfaces when setting.")]
        public string SetCurvatureVariables(int surface1, int surface2, bool set = true, bool skipInfiniteRadius = true)
        {
            var sys = _session.System;
            int s1 = Math.Max(0, surface1);
            int s2 = Math.Min(sys.Surfaces.Count - 1, surface2);
            int count = 0;
            for (int i = s1; i <= s2; i++)
            {
                var s = sys.Surfaces[i];
                if (set && skipInfiniteRadius && double.IsInfinity(s.Radius))
                    continue;
                s.CurvatureVariable = set;
                count++;
            }
            return $"Curvature variables {(set ? "set" : "cleared")} on {count} surfaces ({s1}-{s2}).";
        }

        [McpServerTool, Description("Set or clear thickness variables for a range of surfaces.")]
        public string SetThicknessVariables(int surface1, int surface2, bool set = true)
        {
            var sys = _session.System;
            int s1 = Math.Max(0, surface1);
            int s2 = Math.Min(sys.Surfaces.Count - 1, surface2);
            int count = 0;
            for (int i = s1; i <= s2; i++)
            {
                sys.Surfaces[i].ThicknessVariable = set;
                count++;
            }
            return $"Thickness variables {(set ? "set" : "cleared")} on {count} surfaces ({s1}-{s2}).";
        }

        [McpServerTool, Description("Set constraints on thickness variables in a surface range. filter: all, glass, air. Only applies to surfaces already marked as variable.")]
        public string SetThicknessConstraints(int surface1, int surface2, double? min = null, double? max = null, string filter = "all")
        {
            var sys = _session.System;
            int s1 = Math.Max(0, surface1);
            int s2 = Math.Min(sys.Surfaces.Count - 1, surface2);
            int count = 0;
            for (int i = s1; i <= s2; i++)
            {
                var surf = sys.Surfaces[i];
                if (filter == "glass" && string.IsNullOrEmpty(surf.Material)) continue;
                if (filter == "air" && !string.IsNullOrEmpty(surf.Material)) continue;
                if (!surf.ThicknessVariable) continue;
                surf.ThicknessMin = min;
                surf.ThicknessMax = max;
                count++;
            }
            string constraint = min.HasValue && max.HasValue ? $"min={min:G4} max={max:G4}" :
                                min.HasValue ? $"min={min:G4}" :
                                max.HasValue ? $"max={max:G4}" : "unconstrained";
            return $"Thickness constraints set on {count} variables ({s1}-{s2}, {filter}): {constraint}";
        }

        [McpServerTool, Description("Set constraints on curvature variables in a surface range. filter: all, glass, air. Only applies to surfaces already marked as variable.")]
        public string SetCurvatureConstraints(int surface1, int surface2, double? min = null, double? max = null, string filter = "all")
        {
            var sys = _session.System;
            int s1 = Math.Max(0, surface1);
            int s2 = Math.Min(sys.Surfaces.Count - 1, surface2);
            int count = 0;
            for (int i = s1; i <= s2; i++)
            {
                var surf = sys.Surfaces[i];
                if (filter == "glass" && string.IsNullOrEmpty(surf.Material)) continue;
                if (filter == "air" && !string.IsNullOrEmpty(surf.Material)) continue;
                if (!surf.CurvatureVariable) continue;
                surf.CurvatureMin = min;
                surf.CurvatureMax = max;
                count++;
            }
            string constraint = min.HasValue && max.HasValue ? $"min={min:G4} max={max:G4}" :
                                min.HasValue ? $"min={min:G4}" :
                                max.HasValue ? $"max={max:G4}" : "unconstrained";
            return $"Curvature constraints set on {count} variables ({s1}-{s2}, {filter}): {constraint}";
        }

        [McpServerTool, Description("Clear all optimization variables (curvature, thickness, conic, aspheric) on all surfaces.")]
        public string ClearAllVariables()
        {
            var sys = _session.System;
            foreach (var s in sys.Surfaces)
            {
                s.CurvatureVariable = false;
                s.ThicknessVariable = false;
                s.ConicVariable = false;
                for (int i = 0; i < s.AsphericVariable.Length; i++)
                    s.AsphericVariable[i] = false;
            }
            return "All variables cleared.";
        }

        // Blocking variant unregistered (2026-06-11): long-running optimizations
        // are non-blocking only over MCP — use multistart_optimize_start.
        [Description("Run adaptive multistart optimization. Each trial perturbs from the running center, runs Hooke-Jeeves pre-step (robust to merit-function discontinuities), then short LM. Sigma grows on rejection: starts at initialSigma, resets there on any accepted move, and GROWS (×sigmaGrowth) up to sigmaCap on each rejection to escape a stuck basin. Metropolis acceptance keeps a 'currentCenter' that may accept worse-than-best moves with probability exp(-dM/T) so the optimizer can walk out of local basins. Glass-swap trials get LmPerTrial × glassSwapLmMultiplier iterations to recover from the index discontinuity. Parameters: maxTrials (default 2000), lmPerTrial (default 50), initialLm (default 200), initialSigma (default 0.001), sigmaGrowth (default 1.5), sigmaCap (default 0.1), enableMetropolis (default true), metropolisTemperature (default 0 = autotune from first 10 |dM| samples), hjStepsPerTrial (default 50, 0=disable), hjInitialStep (default 0.1), glassSwapLmMultiplier (default 4), glassSubPercent (default 50), constrainedOnly (default false). Per-LocalOptimizer tunables: tolerance (default 1e-10), dampingFactor (default 1e-6), useBroyden (default true), broydenRefreshInterval (default 5). Result is auto-applied to the system.")]
        public string MultistartOptimize(int maxTrials = 2000, int lmPerTrial = 50, int initialLm = 200,
            double initialSigma = 0.001, double sigmaGrowth = 1.5, double sigmaCap = 0.1,
            bool enableMetropolis = true, double metropolisTemperature = 0.0,
            int hjStepsPerTrial = 50, double hjInitialStep = 0.1, int glassSwapLmMultiplier = 4,
            double glassSubPercent = 50, bool constrainedOnly = false,
            double tolerance = 1e-10, double dampingFactor = 1e-6,
            bool useBroyden = true, int broydenRefreshInterval = 5)
        {
            { var ge = _session.ValidateGlass(); if (ge != null) return ge; }
            if (_session.MeritFunction == null || _session.MeritFunction.Operands.Count == 0)
                return "No operands in merit function. Add operands first.";

            var settings = new MultistartSettings
            {
                MaxTrials = maxTrials,
                LmIterationsPerTrial = lmPerTrial,
                InitialLmIterations = initialLm,
                InitialSigma = initialSigma,
                SigmaGrowth = sigmaGrowth,
                SigmaCap = sigmaCap,
                EnableMetropolis = enableMetropolis,
                MetropolisTemperature = metropolisTemperature,
                HjStepsPerTrial = hjStepsPerTrial,
                HjInitialStep = hjInitialStep,
                GlassSwapLmMultiplier = glassSwapLmMultiplier,
                GlassSubstitutionProbability = glassSubPercent / 100.0,
                ConstrainedOnly = constrainedOnly,
                Tolerance = tolerance,
                InitialDamping = dampingFactor,
                UseBroydenUpdate = useBroyden,
                BroydenRefreshInterval = broydenRefreshInterval,
            };

            var optimizer = new MultistartOptimizer(_session.System, _session.MeritFunction, _session.GlassCatalog)
            {
                Settings = settings,
                FilteredCatalogSearchPaths = FindFilteredCatalogPaths()
            };

            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
            var result = optimizer.Optimize(cts.Token);

            var sb = new StringBuilder();
            sb.AppendLine("Multistart Optimization Complete");
            sb.AppendLine($"  Initial Merit:  {result.InitialMerit:E6}");
            sb.AppendLine($"  Post-LM Merit:  {result.PostInitialLmMerit:E6}");
            sb.AppendLine($"  Final Merit:    {result.FinalMerit:E6}");
            sb.AppendLine($"  Trials: {result.TrialsRun}, Accepted: {result.TrialsAccepted}");
            sb.AppendLine($"  {result.Message}");
            return sb.ToString();
        }

        // Blocking variant unregistered (2026-06-11): use split_element_start.
        [Description(
            "Split the highest-aberration lens element into two elements with equal power, then optimize glass selection via parallel trials. " +
            "Phases per split: (1) split + add boundary constraints, (2) pre-glass multistart with the original glass, (3) parallel glass-pair trials, (4) post-glass multistart with the winning pair. " +
            "Iteration / trial counts (defaults match the GUI): maxSplits (1), glassTrials (300), lmPerTrial (4000), postSplitLm (4000), preGlassTrials (4000), postGlassTrials (2500), multistartInitialSigma (0.001 — sawtooth perturbation magnitude for the embedded multistarts). " +
            "Boundaries: minGlassThickness (1.0), maxGlassThickness (25.0), minAirGap (0.1), maxAirGap (25.0), minEdgeThickness (0.5). " +
            "Glass selection: catalogs (comma-separated AGF names; empty = all loaded), onlyPreferred (true). " +
            "Watchdog: skipPhaseAfterNoImprovementSec (180; auto-skip pre-/post-glass multistart when stalled). " +
            "Per-LM tunables (apply to every internal LocalOptimizer): tolerance (1e-10), dampingFactor (1e-6), useBroyden (true), broydenRefreshInterval (5). " +
            "constrainedOnly (false) restricts multistart randomization to variables with min/max bounds. " +
            "skipGlassTrials (false) bypasses Phases 4 + 5 entirely — split + LM polish only, no glass swap. " +
            "If skipGlassTrials is false, the catalogs string must resolve to at least one glass; an empty string with no catalogs loaded throws an error rather than silently doing nothing. " +
            "Result is auto-applied to the system; use system_save to persist.")]
        public string SplitElement(int maxSplits = 1, int glassTrials = 300, int lmPerTrial = 4000,
            int postSplitLm = 4000, int preGlassTrials = 4000, int postGlassTrials = 2500,
            double multistartInitialSigma = 0.001, bool constrainedOnly = false, bool onlyPreferred = true,
            bool freeAllGlasses = false, bool acceptOnlyIfBetter = true,
            double minGlassThickness = 1.0, double maxGlassThickness = 25.0,
            double minAirGap = 0.1, double maxAirGap = 25.0, double minEdgeThickness = 0.5,
            double skipPhaseAfterNoImprovementSec = 180,
            double tolerance = 1e-10, double dampingFactor = 1e-6,
            bool useBroyden = true, int broydenRefreshInterval = 5,
            bool skipGlassTrials = false,
            string catalogs = "")
        {
            { var ge = _session.ValidateGlass(); if (ge != null) return ge; }
            if (_session.MeritFunction == null || _session.MeritFunction.Operands.Count == 0)
                return "No merit function defined. Add operands first.";

            var settings = new SplitElementSettings
            {
                MaxSplits = maxSplits,
                GlassTrials = glassTrials,
                LmIterationsPerTrial = lmPerTrial,
                PostSplitLmIterations = postSplitLm,
                PreGlassMultistartTrials = preGlassTrials,
                PostGlassMultistartTrials = postGlassTrials,
                MultistartInitialSigma = multistartInitialSigma,
                FreeAllGlasses = freeAllGlasses,
                AcceptOnlyIfBetter = acceptOnlyIfBetter,
                ConstrainedOnly = constrainedOnly,
                OnlyPreferred = onlyPreferred,
                MinGlassThickness = minGlassThickness,
                MaxGlassThickness = maxGlassThickness,
                MinAirGap = minAirGap,
                MaxAirGap = maxAirGap,
                MinEdgeThickness = minEdgeThickness,
                SkipPhaseAfterNoImprovementSeconds = skipPhaseAfterNoImprovementSec,
                Tolerance = tolerance,
                InitialDamping = dampingFactor,
                UseBroydenUpdate = useBroyden,
                BroydenRefreshInterval = broydenRefreshInterval,
                SkipGlassTrials = skipGlassTrials,
            };

            if (!string.IsNullOrWhiteSpace(catalogs))
                settings.GlassCatalogs = new System.Collections.Generic.List<string>(
                    catalogs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            // Point the engine at the filtered-catalogs folder so a name
            // like 'S1_GLASS' resolves to its .agf without the LLM
            // having to pre-load it.
            settings.FilteredCatalogSearchPaths = FindFilteredCatalogPaths();

            var service = new SplitElementService(
                _session.System, _session.MeritFunction,
                _session.GlassCatalog, _session.ConfigEditor)
            {
                Settings = settings
            };

            var result = service.Execute();

            var sb = new StringBuilder();
            sb.AppendLine($"Split Element: {result.SplitsPerformed} split(s) performed");
            sb.AppendLine($"  Initial Merit: {result.InitialMerit:E6}");
            sb.AppendLine($"  Final Merit:   {result.FinalMerit:E6}");
            foreach (var iter in result.Iterations)
            {
                sb.AppendLine($"  Split {iter.SplitIndex + 1}: surface {iter.SelectedSurfaceIndex} ({iter.SelectedMaterial})");
                sb.AppendLine($"    Best glass: {iter.BestGlass1} + {iter.BestGlass2} ({iter.GlassTrialsRun} trials)");
                sb.AppendLine($"    Merit: {iter.PostGlassTrialMerit:E6}");
            }
            if (!string.IsNullOrEmpty(result.Message))
                sb.AppendLine($"  {result.Message}");
            return sb.ToString();
        }

        // Blocking variant unregistered (2026-06-11): use basin_hopping_start.
        [Description(
            "Basin-hopping optimization: a global-ish search that combines a Hooke-Jeeves pattern step + a Levenberg-Marquardt local refinement, with a random Gaussian kick (sigma) between hops to break out of local minima. Optionally substitutes glasses between hops, drawing from filtered or loaded catalogs (same model as Split Element). " +
            "Hop budget: maxHops (default 2000). Per-hop LM iterations: lmIterationsPerHop (default 60). Per-hop Hooke-Jeeves steps: hjStepsPerHop (default 30). " +
            "Kick magnitude: initialPerturbSigma (default 0.001 = 0.1%). HJ step bounds: hjInitialStep (default 0.25), hjMinStep (default 1e-4). " +
            "LM tunables: lmTolerance (1e-10), lmInitialDamping (1e-3), useBroydenUpdate (true). " +
            "Variable filtering: constrainedOnly (false) — when true, only variables with min/max are perturbed. " +
            "Glass substitution: glassSubstitution (false). When true, the catalogs string must resolve to at least one glass; an empty string with no catalogs loaded throws. onlyPreferred (true) restricts loaded catalogs to Status<=1 glasses (filtered catalogs are already curated). " +
            "Determinism: seed (default 1234). " +
            "Parallelism: chains (default 0 = auto = one chain per physical core) runs that many independent hopping chains concurrently from different perturbation seeds and returns the single global best — much better global search and full CPU use. chains=1 is the classic single chain. " +
            "Chain export: saveChainsFolder (default empty) — when set, EVERY chain's final design is written as a separate .lhlt in that folder (best-merit first), not just the global best. " +
            "Result is auto-applied to the system; use system_save to persist.")]
        public string BasinHopping(
            int maxHops = 2000, int lmIterationsPerHop = 60, int hjStepsPerHop = 30,
            double initialPerturbSigma = 0.001, double hjInitialStep = 0.25, double hjMinStep = 1e-4,
            double lmTolerance = 1e-10, double lmInitialDamping = 1e-3, bool useBroydenUpdate = true,
            bool constrainedOnly = false,
            bool glassSubstitution = false, bool onlyPreferred = true, string catalogs = "",
            int seed = 1234, int chains = 0, string saveChainsFolder = "")
        {
            { var ge = _session.ValidateGlass(); if (ge != null) return ge; }
            if (_session.MeritFunction == null || _session.MeritFunction.Operands.Count == 0)
                return "No operands in merit function. Add operands first.";

            var settings = new BasinHoppingSettings
            {
                MaxHops = maxHops,
                LmIterationsPerHop = lmIterationsPerHop,
                HjStepsPerHop = hjStepsPerHop,
                InitialPerturbSigma = initialPerturbSigma,
                HjInitialStep = hjInitialStep,
                HjMinStep = hjMinStep,
                LmTolerance = lmTolerance,
                LmInitialDamping = lmInitialDamping,
                UseBroydenUpdate = useBroydenUpdate,
                ConstrainedOnly = constrainedOnly,
                GlassSubstitution = glassSubstitution,
                OnlyPreferred = onlyPreferred,
                Seed = seed,
            };

            if (!string.IsNullOrWhiteSpace(catalogs))
                settings.GlassCatalogs = new System.Collections.Generic.List<string>(
                    catalogs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            settings.ParallelChains = chains;   // 0 = auto (physical cores); 1 = single chain
            var optimizer = new BasinHoppingOptimizerBatch(_session.System, _session.MeritFunction, _session.GlassCatalog, _session.ConfigEditor)
            {
                Settings = settings,
                FilteredCatalogSearchPaths = FindFilteredCatalogPaths(),
            };

            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
            var result = optimizer.Optimize(cts.Token);

            var sb = new StringBuilder();
            sb.AppendLine("Basin Hopping Complete");
            sb.AppendLine($"  Chains:        {optimizer.ChainsRun}");
            sb.AppendLine($"  Initial Merit: {result.InitialMerit:E6}");
            sb.AppendLine($"  Final Merit:   {result.FinalMerit:E6}");
            sb.AppendLine($"  Hops:          {result.Hops}");
            sb.AppendLine($"  Accepted:      {result.Accepted}");
            sb.AppendLine($"  Rejected:      {result.Rejected}");
            sb.AppendLine($"  Glass Swaps:   {result.GlassSwaps}");
            if (!string.IsNullOrEmpty(result.Message))
                sb.AppendLine($"  {result.Message}");

            if (!string.IsNullOrWhiteSpace(saveChainsFolder))
            {
                string baseName = string.IsNullOrWhiteSpace(_session.System.Title) ? "basin" : _session.System.Title;
                var paths = LensHH.Core.IO.ChainResultWriter.SaveChains(
                    optimizer.ChainResults, saveChainsFolder, baseName, _session.MeritFunction, _session.ConfigEditor);
                sb.AppendLine($"  Saved {paths.Count} chain design(s) to {saveChainsFolder}");
            }
            return sb.ToString();
        }

        [McpServerTool, Description(
            "Start GLOBAL Basin-Hopping HJ+LM in the background and return a job-id immediately (non-blocking). " +
            "Runs N chains (N = physical cores, FIXED — not settable) of basin-hopping; whenever a chain's no-improvement watchdog fires or it exhausts its hops, that chain RESTARTS seeded with the best design found by the OTHER chains, until the global time limit elapses or you cancel — a cooperative deep-dive that pools the best basin across chains. " +
            "Poll optimize_status(jobId) for progress (global best merit, restarts, hops); call optimize_cancel(jobId) to stop early. When the job completes the global-best design is auto-applied to the system. " +
            "Per-chain HJ-LM settings: maxHops (2000), lmIterationsPerHop (60), hjStepsPerHop (30), initialPerturbSigma (0.001), useBroydenUpdate (true), constrainedOnly (false — when true, only randomize bounded variables), glassSubstitution (false), rescaleOnGlassSwap (false), onlyPreferred (true), catalogs (''), seed (1234). " +
            "Mandatory no-improvement watchdog: noImprovementTimeoutSeconds (default 600; values <=0 are forced to 600 — it cannot be disabled). Global wall-clock budget: globalTimeoutMinutes (default 120; <=0 = run until cancelled). " +
            "Chain export: saveChainsFolder ('') — when set, every chain's best design is written there as a separate .lhlt (best-merit first).")]
        public string GlobalBasinHoppingStart(
            int maxHops = 2000, int lmIterationsPerHop = 60, int hjStepsPerHop = 30,
            double initialPerturbSigma = 0.001, bool useBroydenUpdate = true,
            bool constrainedOnly = false, bool glassSubstitution = false,
            bool rescaleOnGlassSwap = false, bool onlyPreferred = true, string catalogs = "",
            int seed = 1234, double noImprovementTimeoutSeconds = 600, double globalTimeoutMinutes = 120,
            string saveChainsFolder = "")
        {
            { var ge = _session.ValidateGlass(); if (ge != null) return ge; }
            if (_session.MeritFunction == null || _session.MeritFunction.Operands.Count == 0)
                return "No operands in merit function. Add operands first.";

            var gs = new GlobalBasinHoppingSettings
            {
                GlobalTimeoutMinutes = globalTimeoutMinutes,
                Chain = new BasinHoppingSettings
                {
                    MaxHops = maxHops,
                    LmIterationsPerHop = lmIterationsPerHop,
                    HjStepsPerHop = hjStepsPerHop,
                    InitialPerturbSigma = initialPerturbSigma,
                    UseBroydenUpdate = useBroydenUpdate,
                    ConstrainedOnly = constrainedOnly,
                    GlassSubstitution = glassSubstitution,
                    RescaleCurvatureOnGlassSwap = rescaleOnGlassSwap,
                    OnlyPreferred = onlyPreferred,
                    Seed = seed,
                    NoImprovementTimeoutSeconds = noImprovementTimeoutSeconds,
                },
            };
            if (!string.IsNullOrWhiteSpace(catalogs))
                gs.Chain.GlassCatalogs = new System.Collections.Generic.List<string>(
                    catalogs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            var mf = _session.MeritFunction;
            var configEditor = _session.ConfigEditor;
            string folder = saveChainsFolder;

            var job = new RunningJob(kind: "global_basin");
            var optimizer = new GlobalBasinHoppingOptimizer(_session.System, mf, _session.GlassCatalog, configEditor)
            {
                Settings = gs,
                FilteredCatalogSearchPaths = FindFilteredCatalogPaths(),
                OnChainsProgress = snap =>
                {
                    double best = double.NaN; int bestChain = -1; long hops = 0; int restarts = 0;
                    foreach (var c in snap)
                    {
                        hops += c.Hops; restarts += c.Restarts;
                        if (!double.IsNaN(c.Best) && (double.IsNaN(best) || c.Best < best)) { best = c.Best; bestChain = c.Chain; }
                    }
                    if (!double.IsNaN(best)) job.BestMerit = best;
                    job.Trial = restarts;
                    job.MaxTrials = snap.Length;
                    job.Phase = $"{snap.Length} chains, {restarts} restarts, {hops} hops (best chain {bestChain})";
                },
            };

            job.Task = Task.Run(() =>
            {
                try
                {
                    var result = optimizer.Optimize(job.Cts.Token);
                    job.InitialMerit = result.InitialMerit;
                    job.CurrentMerit = result.FinalMerit;
                    job.BestMerit = result.FinalMerit;
                    job.Trial = result.TotalRestarts;

                    var sb = new StringBuilder();
                    sb.AppendLine($"Global Basin-Hopping {(result.TimedOut ? "Stopped (global time limit)" : result.Cancelled ? "Stopped (user)" : "Complete")}");
                    sb.AppendLine($"  Chains: {result.ChainsRun}, restarts: {result.TotalRestarts}, total hops: {result.TotalHops}");
                    sb.AppendLine($"  Initial Merit: {result.InitialMerit:E6}");
                    sb.AppendLine($"  Final Merit:   {result.FinalMerit:E6}");
                    sb.AppendLine($"  Wall time: {result.Elapsed.TotalSeconds:F1} s");
                    foreach (var c in result.ChainResults.OrderBy(c => c.Merit))
                        sb.AppendLine($"  chain {c.ChainIndex,2}: merit {c.Merit:E6}{(c.IsBest ? "  <- global best" : "")}");
                    if (!string.IsNullOrWhiteSpace(folder) && result.ChainResults.Count > 0)
                    {
                        string baseName = string.IsNullOrWhiteSpace(_session.System.Title) ? "global_basin" : _session.System.Title;
                        var paths = LensHH.Core.IO.ChainResultWriter.SaveChains(result.ChainResults, folder, baseName, mf, configEditor);
                        sb.AppendLine($"  Saved {paths.Count} chain design(s) to {folder}");
                    }
                    job.Complete(sb.ToString());
                }
                catch (OperationCanceledException) { job.Cancel(); }
                catch (Exception ex) { job.Fault(ex); }
            });

            _session.AddJob(job);
            return $"Started global basin-hopping job. jobId={job.JobId}\n" +
                   $"Chains = physical cores (fixed). Poll optimize_status(jobId=\"{job.JobId}\") every 10-30 s; " +
                   $"call optimize_cancel(jobId=\"{job.JobId}\") to stop early. The global-best design is auto-applied when the job completes.";
        }

        // Blocking variant unregistered (2026-06-11): use synthesis_by_spc_start.
        [Description(
            "Synthesis by Saddle Point Construction (SPC). Iteratively adds elements by locating saddle points in the merit function vs a null-element curvature, branching into two local minima, and optimizing each branch (including glass trials). Runs in parallel across branches. " +
            "Required: catalogs (comma-separated AGF names). SPC needs a glass pool and will throw if catalogs resolve to zero glasses; pass at least one catalog (e.g. 'S1_GLASS' or 'SCHOTT'). " +
            "Topology: maxElements (default 2 — number of elements to insert). topN (5 — survivors kept per level). " +
            "Saddle scan: scanMin/scanMax/scanSteps (default -0.1 / 0.1 / 100). epsilon (1e-3 — branch perturbation off the saddle). " +
            "Glass: glassTrials (50 — random glass picks per branch). nullElementGlass (default 'N-BK7' — material the inserted null element starts with before glass trials substitute). onlyPreferred (true — restrict catalogs to Status<=1; ignored for filtered catalogs which are already curated). " +
            "LM iterations: lmPerTrial (200 — per glass trial). postSplitLm (1000 — final polish per surviving branch). runInitialLm (false — set true to LM-polish the starting design once before insertion). initialLmIterations (50). " +
            "Boundaries: minGlassThickness (1.0), maxGlassThickness (25.0), minAirGap (0.1), maxAirGap (50.0), minEdgeThickness (1.0). constraintWeight (10.0 — boundary-penalty weight). " +
            "Archive: archiveIntermediate (true — write per-level designs to disk). archiveDirectory (default beside current file). " +
            "Parallelism: maxDop (default ProcessorCount). " +
            "Result is auto-applied to the system; use system_save to persist.")]
        public string SynthesisBySpc(
            int maxElements = 2,
            int topN = 5,
            double scanMin = -0.1,
            double scanMax = 0.1,
            int scanSteps = 100,
            double epsilon = 1e-3,
            int glassTrials = 50,
            int lmPerTrial = 200,
            int postSplitLm = 1000,
            string catalogs = "",
            bool onlyPreferred = true,
            double minGlassThickness = 1.0,
            double maxGlassThickness = 25.0,
            double minAirGap = 0.1,
            double maxAirGap = 50.0,
            double minEdgeThickness = 1.0,
            double constraintWeight = 10.0,
            string nullElementGlass = "N-BK7",
            bool runInitialLm = false,
            int initialLmIterations = 50,
            bool archiveIntermediate = true,
            string archiveDirectory = "",
            int? maxDop = null)
        {
            { var ge = _session.ValidateGlass(); if (ge != null) return ge; }
            if (_session.MeritFunction == null || _session.MeritFunction.Operands.Count == 0)
                return "No merit function defined. Add operands first.";

            var settings = new SpcSynthesisSettings
            {
                MaxElements = maxElements,
                TopN = topN,
                ScanMin = scanMin,
                ScanMax = scanMax,
                ScanSteps = scanSteps,
                Epsilon = epsilon,
                GlassTrials = glassTrials,
                LmIterationsPerTrial = lmPerTrial,
                PostSplitLmIterations = postSplitLm,
                OnlyPreferred = onlyPreferred,
                MinGlassThickness = minGlassThickness,
                MaxGlassThickness = maxGlassThickness,
                MinAirGap = minAirGap,
                MaxAirGap = maxAirGap,
                MinEdgeThickness = minEdgeThickness,
                ConstraintWeight = constraintWeight,
                NullElementGlass = nullElementGlass,
                RunInitialLm = runInitialLm,
                InitialLmIterations = initialLmIterations,
                ArchiveIntermediateDesigns = archiveIntermediate,
                ArchiveDirectory = string.IsNullOrWhiteSpace(archiveDirectory) ? null : archiveDirectory,
                MaxDegreeOfParallelism = maxDop,
                FilteredCatalogSearchPaths = FindFilteredCatalogPaths(),
            };

            if (!string.IsNullOrWhiteSpace(catalogs))
                settings.GlassCatalogs = new System.Collections.Generic.List<string>(
                    catalogs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            if (settings.ArchiveIntermediateDesigns)
            {
                settings.ArchiveWriter = (path, sys, mf) =>
                    LensHH.Core.IO.LhltWriter.Write(sys, path, mf, _session.ConfigEditor);
            }

            var service = new SpcSynthesisService(
                _session.System, _session.MeritFunction,
                _session.GlassCatalog, _session.ConfigEditor)
            {
                Settings = settings
            };

            var result = service.Execute();

            var sb = new StringBuilder();
            sb.AppendLine($"Synthesis by SPC: {result.ElementsAdded} element(s) added");
            sb.AppendLine($"  Initial Merit: {result.InitialMerit:E6}");
            sb.AppendLine($"  Final Merit:   {result.FinalMerit:E6}");
            if (result.Cancelled) sb.AppendLine("  (Cancelled)");
            foreach (var lvl in result.Levels)
            {
                sb.AppendLine($"  Level {lvl.Level + 1}: evaluated {lvl.CandidatesEvaluated} candidate(s), kept top {lvl.Survivors}. Best merit: {lvl.BestMerit:E6}");
                for (int i = 0; i < lvl.TopCandidates.Count && i < 3; i++)
                {
                    var c = lvl.TopCandidates[i];
                    sb.AppendLine($"    #{i + 1}: surface {c.InsertionSurface}, c={c.SaddleCurvature:F4}, sign={(c.BranchSign > 0 ? "+" : "-")}, glass={c.Glass}, merit={c.Merit:E6}");
                }
            }
            if (!string.IsNullOrEmpty(result.Message))
                sb.AppendLine($"  {result.Message}");
            return sb.ToString();
        }

        // ── Job pattern: long-running optimizations ──────────────────
        // The synchronous optimize_* tools above can take many minutes;
        // these *_start tools push the work onto a background Task and
        // return immediately with a job-id. The LLM polls via
        // optimize_status, optionally cancels via optimize_cancel, and
        // lists active jobs via optimize_jobs. The engine still mutates
        // _session.System in place when a job completes, so every other
        // tool sees the result without extra ceremony.

        [McpServerTool, Description(
            "Start a multistart optimization in the background and return a job-id immediately. " +
            "After calling this, poll optimize_status(jobId) every ~10-30 seconds to track progress (current trial, best merit, elapsed time) and report to the user. " +
            "Call optimize_cancel(jobId) to stop early. " +
            "When the job's status is Completed, the system already holds the optimized values (auto-applied) — there is no separate keep step. " +
            "Parameters mirror optimize_multistart: maxTrials (2000), lmPerTrial (50), initialLm (200), initialSigma (0.0003), sigmaGrowth (1.5), sigmaCap (0.1), enableMetropolis (true), metropolisTemperature (0 = autotune), hjStepsPerTrial (50), hjInitialStep (0.1), glassSwapLmMultiplier (4), glassSubPercent (50), constrainedOnly (false), tolerance (1e-10), dampingFactor (1e-6), useBroyden (true), broydenRefreshInterval (5).")]
        public string MultistartOptimizeStart(int maxTrials = 2000, int lmPerTrial = 50, int initialLm = 200,
            double initialSigma = 0.001, double sigmaGrowth = 1.5, double sigmaCap = 0.1,
            bool enableMetropolis = true, double metropolisTemperature = 0.0,
            int hjStepsPerTrial = 50, double hjInitialStep = 0.1, int glassSwapLmMultiplier = 4,
            double glassSubPercent = 50, bool constrainedOnly = false,
            double tolerance = 1e-10, double dampingFactor = 1e-6,
            bool useBroyden = true, int broydenRefreshInterval = 5)
        {
            { var ge = _session.ValidateGlass(); if (ge != null) return ge; }
            if (_session.MeritFunction == null || _session.MeritFunction.Operands.Count == 0)
                return "No operands in merit function. Add operands first.";

            var settings = new MultistartSettings
            {
                MaxTrials = maxTrials,
                LmIterationsPerTrial = lmPerTrial,
                InitialLmIterations = initialLm,
                InitialSigma = initialSigma,
                SigmaGrowth = sigmaGrowth,
                SigmaCap = sigmaCap,
                EnableMetropolis = enableMetropolis,
                MetropolisTemperature = metropolisTemperature,
                HjStepsPerTrial = hjStepsPerTrial,
                HjInitialStep = hjInitialStep,
                GlassSwapLmMultiplier = glassSwapLmMultiplier,
                GlassSubstitutionProbability = glassSubPercent / 100.0,
                ConstrainedOnly = constrainedOnly,
                Tolerance = tolerance,
                InitialDamping = dampingFactor,
                UseBroydenUpdate = useBroyden,
                BroydenRefreshInterval = broydenRefreshInterval,
            };

            var job = new RunningJob(kind: "multistart") { MaxTrials = maxTrials };
            var optimizer = new MultistartOptimizer(_session.System, _session.MeritFunction, _session.GlassCatalog, _session.ConfigEditor)
            {
                Settings = settings,
                FilteredCatalogSearchPaths = FindFilteredCatalogPaths(),
                OnProgress = p =>
                {
                    job.Phase = p.IsInitialLm ? $"Initial LM iter {p.InitialLmIteration}" : $"Trial {p.Trial}";
                    if (!double.IsNaN(p.BestMerit) && p.BestMerit > 0) job.BestMerit = p.BestMerit;
                    job.Trial = p.Trial;
                    job.Accepted = p.TrialsAccepted;
                },
            };

            job.Task = Task.Run(() =>
            {
                try
                {
                    var result = optimizer.Optimize(job.Cts.Token);
                    job.InitialMerit = result.InitialMerit;
                    job.CurrentMerit = result.FinalMerit;
                    job.BestMerit = result.FinalMerit;
                    job.Accepted = result.TrialsAccepted;
                    job.Trial = result.TrialsRun;
                    if (result.Cancelled) job.Cancel();
                    else job.Complete(
                        $"Multistart Complete\n" +
                        $"  Initial Merit: {result.InitialMerit:E6}\n" +
                        $"  Post-LM Merit: {result.PostInitialLmMerit:E6}\n" +
                        $"  Final Merit:   {result.FinalMerit:E6}\n" +
                        $"  Trials: {result.TrialsRun}, Accepted: {result.TrialsAccepted}");
                }
                catch (OperationCanceledException) { job.Cancel(); }
                catch (Exception ex) { job.Fault(ex); }
            });

            _session.AddJob(job);
            return $"Started multistart job. jobId={job.JobId}\n" +
                   $"Poll optimize_status(jobId=\"{job.JobId}\") every 10-30 seconds to track progress; " +
                   $"call optimize_cancel(jobId=\"{job.JobId}\") to stop early. " +
                   $"The system will be auto-updated when the job completes.";
        }

        [McpServerTool, Description(
            "Start a Global Search in the background and return a job-id immediately (non-blocking). " +
            "Global Search runs many seeded Multistart restarts from the starting design and collects a pool of structurally-DISTINCT locally-optimal designs — the point is variety, not a single answer. " +
            "Each pooled design is written as a .lhlt to outputFolder with its seed in the filename; the merit-best design is auto-applied to the current system when the job completes. " +
            "Poll optimize_status(jobId) for progress (pool count / models-to-keep, best merit); call optimize_cancel(jobId) to stop early (the pool found so far is still written). " +
            "Reproducibility: the same baseSeed reproduces the same pool exactly; run baseSeed=1, then 2, then 3 to accumulate independent, non-overlapping batches of designs. " +
            "Params: modelsToKeep (16), maxRestarts (48), maxTrialsPerRestart (2000), lmPerTrial (4000), stallAtCapBatches (1 — restart ends after this many no-improvement batches at the sigma cap), baseSeed (1), glassSubPercent (50), initialSigma (0.001), sigmaCap (0.01), prePolishLm (0 = perturb the raw start design; >0 LM-polishes it once first), rescaleOnGlassSwap (true), useNativeEngine (true), analyticDerivative (true), outputFolder ('global_search_results').")]
        public string GlobalSearchStart(
            int modelsToKeep = 16, int maxRestarts = 48, int maxTrialsPerRestart = 2000,
            int lmPerTrial = 4000, int stallAtCapBatches = 1, int baseSeed = 1,
            double glassSubPercent = 50, double initialSigma = 0.001, double sigmaCap = 0.01,
            int prePolishLm = 0, bool rescaleOnGlassSwap = true,
            bool useNativeEngine = true, bool analyticDerivative = true,
            string outputFolder = "global_search_results")
        {
            { var ge = _session.ValidateGlass(); if (ge != null) return ge; }
            if (_session.MeritFunction == null || _session.MeritFunction.Operands.Count == 0)
                return "No operands in merit function. Add operands first.";

            string folder = string.IsNullOrWhiteSpace(outputFolder) ? "global_search_results" : outputFolder;
            try { System.IO.Directory.CreateDirectory(folder); }
            catch (Exception ex) { return $"Cannot create output folder '{folder}': {ex.Message}"; }

            var configEditor = _session.ConfigEditor;
            var mf = _session.MeritFunction;

            var settings = new GlobalSearchSettings
            {
                ModelsToKeep = modelsToKeep,
                MaxRestarts = maxRestarts,
                MaxTrialsPerRestart = maxTrialsPerRestart,
                StopAtCapStallBatches = stallAtCapBatches,
                BaseSeed = baseSeed,
                Multistart = new MultistartSettings
                {
                    InitialSigma = initialSigma,
                    SigmaCap = sigmaCap,
                    GlassSubstitutionProbability = glassSubPercent / 100.0,
                    RescaleCurvatureOnGlassSwap = rescaleOnGlassSwap,
                    LmIterationsPerTrial = lmPerTrial,
                    InitialLmIterations = prePolishLm,
                },
                ArchiveWriter = (name, sys, m) =>
                {
                    try { LensHH.Core.IO.LhltWriter.Write(sys, System.IO.Path.Combine(folder, name + ".lhlt"), m, configEditor); }
                    catch { /* best-effort archive */ }
                },
            };

            var job = new RunningJob(kind: "global_search") { MaxTrials = modelsToKeep };
            var svc = new GlobalSearchService(_session.System, mf, _session.GlassCatalog, configEditor)
            {
                Settings = settings,
                EngineMode = useNativeEngine ? EngineMode.Native : EngineMode.CSharp,
                NativeDerivativeMode = analyticDerivative
                    ? LensHH.Core.NativeInterop.MeritDerivativeMode.Analytic
                    : LensHH.Core.NativeInterop.MeritDerivativeMode.FiniteDifference,
                FilteredCatalogSearchPaths = FindFilteredCatalogPaths(),
                OnProgress = p =>
                {
                    job.Phase = p.StatusMessage;
                    job.Trial = p.PoolCount;
                    job.MaxTrials = p.ModelsToKeep;
                    if (!double.IsNaN(p.BestMerit)) job.BestMerit = p.BestMerit;
                },
            };

            job.Task = Task.Run(() =>
            {
                try
                {
                    var result = svc.Run(job.Cts.Token);
                    job.BestMerit = result.Best?.Merit ?? double.NaN;
                    job.Trial = result.Models.Count;
                    // Apply the merit-best design so subsequent tools see it.
                    if (result.Best != null) _session.System.CopyFrom(result.Best.System);

                    if (result.Cancelled && result.Models.Count == 0) { job.Cancel(); return; }

                    var sb = new StringBuilder();
                    sb.AppendLine($"Global Search {(result.Cancelled ? "Stopped" : "Complete")} — {result.Models.Count} distinct design(s)");
                    sb.AppendLine($"  {result.Message}");
                    sb.AppendLine($"  Files written to: {System.IO.Path.GetFullPath(folder)}");
                    for (int r = 0; r < result.Models.Count; r++)
                    {
                        var mdl = result.Models[r];
                        string glassTag = mdl.GlassSet.Count > 0 ? string.Join("-", mdl.GlassSet) : "noglass";
                        string form = string.IsNullOrEmpty(mdl.PowerSignSignature) ? "-" : mdl.PowerSignSignature;
                        sb.AppendLine($"  #{r + 1}  merit {mdl.Merit:E4}  form {form}  [{glassTag}]  seed {mdl.Seed}");
                    }
                    sb.AppendLine("  Best (rank 1) applied to the current system.");
                    job.Complete(sb.ToString());
                }
                catch (OperationCanceledException) { job.Cancel(); }
                catch (Exception ex) { job.Fault(ex); }
            });

            _session.AddJob(job);
            return $"Started global search job. jobId={job.JobId}\n" +
                   $"Poll optimize_status(jobId=\"{job.JobId}\") every 10-30 seconds (shows pool count / best merit); " +
                   $"call optimize_cancel(jobId=\"{job.JobId}\") to stop early. " +
                   $"Designs are written to '{folder}'; the best is auto-applied when the job completes.";
        }

        [McpServerTool, Description(
            "Start basin-hopping optimization in the background and return a job-id immediately. " +
            "Poll optimize_status(jobId) for progress (current hop, best merit, accepted/rejected, glass swaps); call optimize_cancel to stop. " +
            "Result auto-applies to the system when the job completes. " +
            "chains (default 0 = auto = one chain per physical core) runs that many independent hopping chains concurrently and returns the single global best (chains=1 = classic single chain). " +
            "Parameters mirror optimize_basin_hopping.")]
        public string BasinHoppingStart(
            int maxHops = 2000, int lmIterationsPerHop = 60, int hjStepsPerHop = 30,
            double initialPerturbSigma = 0.001, double hjInitialStep = 0.25, double hjMinStep = 1e-4,
            double lmTolerance = 1e-10, double lmInitialDamping = 1e-3, bool useBroydenUpdate = true,
            bool constrainedOnly = false,
            bool glassSubstitution = false, bool onlyPreferred = true, string catalogs = "",
            int seed = 1234, int chains = 0, string saveChainsFolder = "")
        {
            { var ge = _session.ValidateGlass(); if (ge != null) return ge; }
            if (_session.MeritFunction == null || _session.MeritFunction.Operands.Count == 0)
                return "No operands in merit function. Add operands first.";

            var settings = new BasinHoppingSettings
            {
                MaxHops = maxHops,
                LmIterationsPerHop = lmIterationsPerHop,
                HjStepsPerHop = hjStepsPerHop,
                InitialPerturbSigma = initialPerturbSigma,
                HjInitialStep = hjInitialStep,
                HjMinStep = hjMinStep,
                LmTolerance = lmTolerance,
                LmInitialDamping = lmInitialDamping,
                UseBroydenUpdate = useBroydenUpdate,
                ConstrainedOnly = constrainedOnly,
                GlassSubstitution = glassSubstitution,
                OnlyPreferred = onlyPreferred,
                Seed = seed,
            };
            if (!string.IsNullOrWhiteSpace(catalogs))
                settings.GlassCatalogs = new System.Collections.Generic.List<string>(
                    catalogs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            settings.ParallelChains = chains;   // 0 = auto (physical cores); 1 = single chain
            int resolvedChains = chains <= 0 ? LensHH.Core.Optimization.CpuInfo.PhysicalCoreCount() : chains;
            var job = new RunningJob(kind: "basin") { MaxTrials = maxHops * Math.Max(1, resolvedChains) };
            var optimizer = new BasinHoppingOptimizerBatch(_session.System, _session.MeritFunction, _session.GlassCatalog, _session.ConfigEditor)
            {
                Settings = settings,
                FilteredCatalogSearchPaths = FindFilteredCatalogPaths(),
                OnProgress = p =>
                {
                    job.Phase = $"Hop {p.Hop}/{p.MaxHops} {p.Phase}";
                    if (!double.IsNaN(p.BestMerit) && p.BestMerit > 0) job.BestMerit = p.BestMerit;
                    if (!double.IsNaN(p.CurrentMerit)) job.CurrentMerit = p.CurrentMerit;
                    job.Trial = p.Hop;
                    job.Accepted = p.Accepted;
                    job.Rejected = p.Rejected;
                    job.GlassSwaps = p.GlassSwaps;
                },
            };

            job.Task = Task.Run(() =>
            {
                try
                {
                    var result = optimizer.Optimize(job.Cts.Token);
                    job.InitialMerit = result.InitialMerit;
                    job.CurrentMerit = result.FinalMerit;
                    job.BestMerit = result.FinalMerit;
                    job.Accepted = result.Accepted;
                    job.Rejected = result.Rejected;
                    job.GlassSwaps = result.GlassSwaps;
                    string chainsMsg = "";
                    if (!result.Cancelled && !string.IsNullOrWhiteSpace(saveChainsFolder))
                    {
                        string baseName = string.IsNullOrWhiteSpace(_session.System.Title) ? "basin" : _session.System.Title;
                        var paths = LensHH.Core.IO.ChainResultWriter.SaveChains(
                            optimizer.ChainResults, saveChainsFolder, baseName, _session.MeritFunction, _session.ConfigEditor);
                        chainsMsg = $"\n  Saved {paths.Count} chain design(s) to {saveChainsFolder}";
                    }
                    if (result.Cancelled) job.Cancel();
                    else job.Complete(
                        $"Basin Hopping Complete\n" +
                        $"  Chains: {optimizer.ChainsRun}\n" +
                        $"  Initial Merit: {result.InitialMerit:E6}\n" +
                        $"  Final Merit:   {result.FinalMerit:E6}\n" +
                        $"  Hops: {result.Hops}, Accepted: {result.Accepted}, Rejected: {result.Rejected}, Glass Swaps: {result.GlassSwaps}" +
                        chainsMsg);
                }
                catch (OperationCanceledException) { job.Cancel(); }
                catch (Exception ex) { job.Fault(ex); }
            });

            _session.AddJob(job);
            return $"Started basin-hopping job ({resolvedChains} parallel chain{(resolvedChains == 1 ? "" : "s")}). jobId={job.JobId}\n" +
                   $"Poll optimize_status(jobId=\"{job.JobId}\") every 10-30 seconds; " +
                   $"call optimize_cancel(jobId=\"{job.JobId}\") to stop early.";
        }

        [McpServerTool, Description(
            "Start split-element synthesis in the background and return a job-id. " +
            "Poll optimize_status for the active phase (pre-glass multistart / glass trials / post-glass multistart), current best merit, and elapsed time. " +
            "Result auto-applies. Parameters mirror optimize_split.")]
        public string SplitElementStart(
            int maxSplits = 1, int glassTrials = 300, int lmPerTrial = 4000,
            int postSplitLm = 4000, int preGlassTrials = 4000, int postGlassTrials = 2500,
            double multistartInitialSigma = 0.001, bool constrainedOnly = false, bool onlyPreferred = true,
            bool freeAllGlasses = false, bool acceptOnlyIfBetter = true,
            double minGlassThickness = 1.0, double maxGlassThickness = 25.0,
            double minAirGap = 0.1, double maxAirGap = 25.0, double minEdgeThickness = 0.5,
            double skipPhaseAfterNoImprovementSec = 180,
            double tolerance = 1e-10, double dampingFactor = 1e-6,
            bool useBroyden = true, int broydenRefreshInterval = 5,
            bool skipGlassTrials = false,
            string catalogs = "")
        {
            { var ge = _session.ValidateGlass(); if (ge != null) return ge; }
            if (_session.MeritFunction == null || _session.MeritFunction.Operands.Count == 0)
                return "No merit function defined. Add operands first.";

            var settings = new SplitElementSettings
            {
                MaxSplits = maxSplits,
                GlassTrials = glassTrials,
                LmIterationsPerTrial = lmPerTrial,
                PostSplitLmIterations = postSplitLm,
                PreGlassMultistartTrials = preGlassTrials,
                PostGlassMultistartTrials = postGlassTrials,
                MultistartInitialSigma = multistartInitialSigma,
                FreeAllGlasses = freeAllGlasses,
                AcceptOnlyIfBetter = acceptOnlyIfBetter,
                ConstrainedOnly = constrainedOnly,
                OnlyPreferred = onlyPreferred,
                MinGlassThickness = minGlassThickness,
                MaxGlassThickness = maxGlassThickness,
                MinAirGap = minAirGap,
                MaxAirGap = maxAirGap,
                MinEdgeThickness = minEdgeThickness,
                SkipPhaseAfterNoImprovementSeconds = skipPhaseAfterNoImprovementSec,
                Tolerance = tolerance,
                InitialDamping = dampingFactor,
                UseBroydenUpdate = useBroyden,
                BroydenRefreshInterval = broydenRefreshInterval,
                SkipGlassTrials = skipGlassTrials,
                FilteredCatalogSearchPaths = FindFilteredCatalogPaths(),
            };
            if (!string.IsNullOrWhiteSpace(catalogs))
                settings.GlassCatalogs = new System.Collections.Generic.List<string>(
                    catalogs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            var job = new RunningJob(kind: "split") { MaxTrials = maxSplits };
            var service = new SplitElementService(_session.System, _session.MeritFunction, _session.GlassCatalog, _session.ConfigEditor)
            {
                Settings = settings,
                OnProgress = p =>
                {
                    job.Phase = $"Split {p.SplitIteration + 1}/{p.MaxSplits} {p.Phase}: {p.StatusMessage}";
                    if (!double.IsNaN(p.BestMerit) && p.BestMerit > 0) job.BestMerit = p.BestMerit;
                    if (!double.IsNaN(p.CurrentMerit)) job.CurrentMerit = p.CurrentMerit;
                    job.Trial = p.GlassTrialCurrent;
                    if (p.GlassTrialTotal > 0) job.MaxTrials = p.GlassTrialTotal;
                },
            };

            job.Task = Task.Run(() =>
            {
                try
                {
                    var result = service.Execute(job.Cts.Token);
                    job.InitialMerit = result.InitialMerit;
                    job.CurrentMerit = result.FinalMerit;
                    job.BestMerit = result.FinalMerit;
                    if (result.Cancelled) job.Cancel();
                    else
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine($"Split Element Complete: {result.SplitsPerformed} split(s)");
                        sb.AppendLine($"  Initial Merit: {result.InitialMerit:E6}");
                        sb.AppendLine($"  Final Merit:   {result.FinalMerit:E6}");
                        foreach (var iter in result.Iterations)
                        {
                            sb.AppendLine($"  Split {iter.SplitIndex + 1}: surface {iter.SelectedSurfaceIndex} ({iter.SelectedMaterial})");
                            sb.AppendLine($"    Best glass: {iter.BestGlass1} + {iter.BestGlass2} ({iter.GlassTrialsRun} trials)");
                            sb.AppendLine($"    Merit: {iter.PostGlassTrialMerit:E6}");
                        }
                        job.Complete(sb.ToString());
                    }
                }
                catch (OperationCanceledException) { job.Cancel(); }
                catch (Exception ex) { job.Fault(ex); }
            });

            _session.AddJob(job);
            return $"Started split-element job. jobId={job.JobId}\n" +
                   $"Poll optimize_status(jobId=\"{job.JobId}\"). Phases progress through pre-glass multistart, glass trials, post-glass multistart per split.";
        }

        [McpServerTool, Description(
            "Start synthesis-by-SPC in the background and return a job-id. " +
            "Poll optimize_status for level/phase/best-merit. Glass catalog is mandatory (the tool throws if it resolves to zero glasses). Result auto-applies. " +
            "Parameters mirror optimize_synthesis_by_spc.")]
        public string SynthesisBySpcStart(
            int maxElements = 2, int topN = 5,
            double scanMin = -0.1, double scanMax = 0.1, int scanSteps = 100,
            double epsilon = 1e-3,
            int glassTrials = 50, int lmPerTrial = 200, int postSplitLm = 1000,
            string catalogs = "",
            bool onlyPreferred = true,
            double minGlassThickness = 1.0, double maxGlassThickness = 25.0,
            double minAirGap = 0.1, double maxAirGap = 50.0, double minEdgeThickness = 1.0,
            double constraintWeight = 10.0,
            string nullElementGlass = "N-BK7",
            bool runInitialLm = false, int initialLmIterations = 50,
            bool archiveIntermediate = true, string archiveDirectory = "",
            int? maxDop = null)
        {
            { var ge = _session.ValidateGlass(); if (ge != null) return ge; }
            if (_session.MeritFunction == null || _session.MeritFunction.Operands.Count == 0)
                return "No merit function defined. Add operands first.";

            var settings = new SpcSynthesisSettings
            {
                MaxElements = maxElements,
                TopN = topN,
                ScanMin = scanMin,
                ScanMax = scanMax,
                ScanSteps = scanSteps,
                Epsilon = epsilon,
                GlassTrials = glassTrials,
                LmIterationsPerTrial = lmPerTrial,
                PostSplitLmIterations = postSplitLm,
                OnlyPreferred = onlyPreferred,
                MinGlassThickness = minGlassThickness,
                MaxGlassThickness = maxGlassThickness,
                MinAirGap = minAirGap,
                MaxAirGap = maxAirGap,
                MinEdgeThickness = minEdgeThickness,
                ConstraintWeight = constraintWeight,
                NullElementGlass = nullElementGlass,
                RunInitialLm = runInitialLm,
                InitialLmIterations = initialLmIterations,
                ArchiveIntermediateDesigns = archiveIntermediate,
                ArchiveDirectory = string.IsNullOrWhiteSpace(archiveDirectory) ? null : archiveDirectory,
                MaxDegreeOfParallelism = maxDop,
                FilteredCatalogSearchPaths = FindFilteredCatalogPaths(),
            };
            if (!string.IsNullOrWhiteSpace(catalogs))
                settings.GlassCatalogs = new System.Collections.Generic.List<string>(
                    catalogs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            if (settings.ArchiveIntermediateDesigns)
                settings.ArchiveWriter = (path, sys, mf) =>
                    LensHH.Core.IO.LhltWriter.Write(sys, path, mf, _session.ConfigEditor);

            var job = new RunningJob(kind: "spc") { MaxTrials = maxElements };
            var service = new SpcSynthesisService(_session.System, _session.MeritFunction, _session.GlassCatalog, _session.ConfigEditor)
            {
                Settings = settings,
                OnProgress = p =>
                {
                    job.Phase = $"L{p.Level + 1}/{p.MaxLevels} {p.Phase}: {p.StatusMessage}";
                    if (!double.IsNaN(p.BestMerit) && p.BestMerit > 0) job.BestMerit = p.BestMerit;
                    if (!double.IsNaN(p.CurrentMerit)) job.CurrentMerit = p.CurrentMerit;
                    job.Trial = p.Level + 1;
                },
            };

            job.Task = Task.Run(() =>
            {
                try
                {
                    var result = service.Execute(job.Cts.Token);
                    job.InitialMerit = result.InitialMerit;
                    job.CurrentMerit = result.FinalMerit;
                    job.BestMerit = result.FinalMerit;
                    if (result.Cancelled) job.Cancel();
                    else
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine($"Synthesis by SPC Complete: {result.ElementsAdded} element(s) added");
                        sb.AppendLine($"  Initial Merit: {result.InitialMerit:E6}");
                        sb.AppendLine($"  Final Merit:   {result.FinalMerit:E6}");
                        foreach (var lvl in result.Levels)
                            sb.AppendLine($"  Level {lvl.Level + 1}: {lvl.CandidatesEvaluated} evaluated, {lvl.Survivors} kept. Best: {lvl.BestMerit:E6}");
                        job.Complete(sb.ToString());
                    }
                }
                catch (OperationCanceledException) { job.Cancel(); }
                catch (Exception ex) { job.Fault(ex); }
            });

            _session.AddJob(job);
            return $"Started synthesis-by-SPC job. jobId={job.JobId}\n" +
                   $"Poll optimize_status(jobId=\"{job.JobId}\"). Each level runs a saddle-scan, branch-LM, and glass-trials phase.";
        }

        [McpServerTool, Description(
            "Poll the status of a job started by an optimize_*_start tool. " +
            "Returns one of Running / Completed / Cancelled / Faulted plus the current phase, trial counter, merit values (initial / current / best), accepted/rejected counts, and elapsed time. " +
            "When status is Completed the system already reflects the optimized values; the Result field has the final summary.")]
        public string OptimizeStatus(string jobId)
        {
            var job = _session.GetJob(jobId);
            if (job == null) return $"No job found with id '{jobId}'. Use optimize_jobs to list active jobs.";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Job: {job.JobId}  ({job.Kind})");
            sb.AppendLine($"Status: {job.Status}");
            sb.AppendLine($"Phase: {job.Phase}");
            sb.AppendLine($"Elapsed: {job.Elapsed.TotalSeconds:F1} s");
            if (!double.IsNaN(job.InitialMerit)) sb.AppendLine($"Initial Merit: {job.InitialMerit:E6}");
            if (!double.IsNaN(job.BestMerit)) sb.AppendLine($"Best Merit:    {job.BestMerit:E6}");
            if (job.MaxTrials > 0) sb.AppendLine($"Trial: {job.Trial}/{job.MaxTrials}");
            if (job.Accepted > 0 || job.Rejected > 0) sb.AppendLine($"Accepted: {job.Accepted}  Rejected: {job.Rejected}");
            if (job.GlassSwaps > 0) sb.AppendLine($"Glass swaps: {job.GlassSwaps}");
            if (job.Status == JobStatus.Completed && !string.IsNullOrEmpty(job.Result))
            {
                sb.AppendLine();
                sb.AppendLine(job.Result);
            }
            if (job.Status == JobStatus.Faulted && !string.IsNullOrEmpty(job.Error))
            {
                sb.AppendLine();
                sb.AppendLine($"Error: {job.Error}");
            }
            return sb.ToString();
        }

        [McpServerTool, Description("Cancel a running optimization job started by an optimize_*_start tool. The job's status moves to Cancelled; the system retains whatever progress had been accumulated (multistart commits accepted trials in place).")]
        public string OptimizeCancel(string jobId)
        {
            var job = _session.GetJob(jobId);
            if (job == null) return $"No job found with id '{jobId}'.";
            if (job.Status != JobStatus.Running)
                return $"Job {jobId} is already {job.Status}; nothing to cancel.";
            try { job.Cts.Cancel(); } catch { }
            return $"Cancellation requested for job {jobId}. Poll optimize_status to confirm transition to Cancelled.";
        }

        [McpServerTool, Description("List every optimization job tracked by this session, including completed and cancelled ones. Returns one row per job with id, kind, status, phase, elapsed time, and best merit.")]
        public string OptimizeJobs()
        {
            var jobs = _session.Jobs;
            if (jobs.Count == 0) return "No jobs.";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{"jobId",-10} {"kind",-12} {"status",-10} {"elapsed",-10} {"best merit"}");
            foreach (var j in jobs)
            {
                string best = double.IsNaN(j.BestMerit) ? "—" : j.BestMerit.ToString("E4");
                sb.AppendLine($"{j.JobId,-10} {j.Kind,-12} {j.Status,-10} {j.Elapsed.TotalSeconds,8:F1}s  {best}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Locate catalogs\FilteredGlassCatalogues across install / dev
        /// layouts. Returns an empty array if the folder isn't found
        /// (the engine then falls back to whatever's already loaded
        /// in the GlassCatalogManager).
        /// </summary>
        private static string[] FindFilteredCatalogPaths()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                System.IO.Path.Combine(baseDir, "catalogs", "FilteredGlassCatalogues"),
                System.IO.Path.Combine(baseDir, "..", "catalogs", "FilteredGlassCatalogues"),
                System.IO.Path.Combine(baseDir, "..", "..", "..", "..", "..", "catalogs", "FilteredGlassCatalogues"),
            };
            foreach (var d in candidates)
            {
                if (System.IO.Directory.Exists(d))
                    return new[] { System.IO.Path.GetFullPath(d) };
            }
            return Array.Empty<string>();
        }
    }
}
