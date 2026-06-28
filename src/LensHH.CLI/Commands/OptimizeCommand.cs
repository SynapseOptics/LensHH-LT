using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using LensHH.Core.Configuration;
using LensHH.Core.Models;
using LensHH.Core.NativeInterop;
using LensHH.Core.Optimization;
using Spectre.Console;

namespace LensHH.CLI.Commands
{
    public class OptimizeCommand : ICommand
    {
        private CancellationTokenSource? _cts;

        public string Name => "optimize";
        public string Description => "Optimization: run, cancel, status";

        public string Help => @"[bold]optimize[/] - Optimization operations
  [green]optimize run [[maxiter=N]] [[tol=V]] [[damping=V]] [[broyden=true|false]] [[refresh=N]][/]  Run local optimization (auto-applies result to the system)
  [green]optimize try [[maxiter=N]] [[tol=V]] [[damping=V]] [[broyden=true|false]] [[refresh=N]][/]  Run local optimization and prompt to keep or revert
  [green]optimize multistart [[trials=N]] [[lm=N]] [[initlm=N]] [[sigma=V]] [[cap=V]] [[growth=V]] [[glass=V]] [[constrained]] [[tol=V]] [[damping=V]] [[broyden=true|false]] [[refresh=N]] [[gpu]] [[mincurvchange=V]][/]  Multistart optimization. gpu = sieve candidates on the GPU. mincurvchange (default 2) = GPU difference gate: only feed designs that differ from the running best by a glass swap or this % refractive-surface curvature change (0 = off; stops the GPU sieve acting as a pure refiner).
  [green]optimize basin [[hops=N]] [[lm=N]] [[hj=N]] [[sigma=V]] [[hjstep=V]] [[hjmin=V]] [[tol=V]] [[damping=V]] [[broyden=true|false]] [[constrained]] [[glasssub=true|false]] [[onlypreferred=true|false]] [[catalog=NAME]] [[seed=N]][/]  Basin hopping (Hooke-Jeeves + LM with random kicks between hops)
  [green]optimize global-basin [[hops=N]] [[lm=N]] [[hj=N]] [[sigma=V]] [[broyden=true|false]] [[glasssub=true|false]] [[rescale=true|false]] [[constrained]] [[onlypreferred=true|false]] [[catalog=NAME]] [[seed=N]] [[timeout=SEC]] [[globalmin=MIN]] [[savechains=DIR]] [[apply=N]][/]  Global Basin Hopping HJ+LM: chains=physical cores (fixed); each chain restarts from the best of the OTHER chains when its no-improvement watchdog (timeout, default 600s) fires or hops are exhausted, until the global limit (globalmin, default 120) elapses or you cancel. savechains: write every chain's best design; apply=N: apply chain N's design instead of the global best.
  [green]optimize split [[splits=N]] [[trials=N]] [[lm=N]] [[postlm=N]] [[preglass=N]] [[postglass=N]] [[sigma=V]] [[constrained]] [[onlypreferred=true|false]] [[minglass=V]] [[maxglass=V]] [[minair=V]] [[maxair=V]] [[minedge=V]] [[skipsec=V]] [[tol=V]] [[damping=V]] [[broyden=true|false]] [[refresh=N]] [[catalog=NAME]] [[noglass]][/]  Split element synthesis. catalog: AGF name (e.g. catalog=S1_GLASS); resolved against catalogs\FilteredGlassCatalogues. noglass: skip the glass-trials phase entirely (split + LM polish only).
  [green]optimize spc [[elements=N]] [[topn=N]] [[scanmin=V]] [[scanmax=V]] [[steps=N]] [[epsilon=V]] [[glass=N]] [[lm=N]] [[postlm=N]] [[catalog=NAME]] [[archive=true|false]] [[archivedir=PATH]] [[dop=N]] [[nullglass=NAME]] [[runinitlm=true|false]] [[initlm=N]] [[onlypreferred=true|false]] [[minglass=V]] [[maxglass=V]] [[minair=V]] [[maxair=V]] [[minedge=V]] [[constraintweight=V]][/]  Synthesis by SPC. catalog is mandatory (single AGF name or comma-separated list).
  [green]optimize global [[models=N]] [[restarts=N]] [[trials=N]] [[lm=N]] [[stall=N]] [[seed=N]] [[prepolish=N]] [[sigma=V]] [[cap=V]] [[glass=V]] [[native]] [[analytic]] [[out=DIR]][/]  Global Search: many seeded restarts from the start design; writes a pool of distinct .lhlt designs to DIR (default global_search_results). seed: base seed (run 1, then 2, … for independent batches). prepolish=0 (default) perturbs the raw start.
  [green]optimize deseed [[pop=N]] [[gens=N]] [[stall=N]] [[f=V]] [[cr=V]] [[glass=V]] [[curvlimit=V]] [[gpu]] [[seed=N]] [[emit=N]] [[refine=N]] [[out=DIR]][/]  Differential-Evolution seed generator: evolve a population from ranges (geometry + glass), then LM-refine each seed; writes refined seeds to DIR. glass = per-candidate glass-swap probability (%); curvlimit = curvature seed limit (0=auto); gpu = run the per-generation merit eval on the GPU (host DE loop). Prints a CPU/GPU timing breakdown.
  [green]optimize memetic [[rounds=N]] [[gens=N]] [[polish-count=N]] [[polish=lm|multistart]] [[pop=N]] [[f=V]] [[cr=V]] [[clones=N]] [[sigma=V]] [[niche=V]] [[lm-iters=N]] [[seed=N]] [[gpu]] [[out=DIR]] [[resume=DIR]][/]  EXPERIMENTAL memetic DE: interleaves DE bursts (gens) with niched-best polish + reseed for `rounds`, returning `polish-count` diverse designs. gpu = population resident on the device. out: writes best/*.lhlt + population.json (restart with resume=DIR).
  [green]optimize cancel[/]                                     Cancel running optimization
  [green]optimize variables[/]                                  List current variables";

        public void Execute(Session session, string[] args)
        {
            if (args.Length == 0)
            {
                AnsiConsole.MarkupLine(Help);
                return;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "run":
                    RunOptimization(session, args, autoApply: true);
                    break;
                case "try":
                    RunOptimization(session, args, autoApply: false);
                    break;
                case "multistart":
                    RunMultistart(session, args);
                    break;
                case "basin":
                case "basinhop":
                case "basin-hopping":
                    RunBasinHopping(session, args);
                    break;
                case "global-basin":
                case "globalbasin":
                case "gbasin":
                    RunGlobalBasinHopping(session, args);
                    break;
                case "split":
                    RunSplitElement(session, args);
                    break;
                case "spc":
                    RunSpcSynthesis(session, args);
                    break;
                case "global":
                case "global-search":
                case "globalsearch":
                    RunGlobalSearch(session, args);
                    break;
                case "deseed":
                case "de":
                    RunDeSeed(session, args);
                    break;
                case "memetic":
                    RunMemetic(session, args);
                    break;
                case "cancel":
                    CancelOptimization();
                    break;
                case "variables":
                    ListVariables(session);
                    break;
                default:
                    AnsiConsole.MarkupLine($"[yellow]Unknown subcommand: {Markup.Escape(args[0])}[/]");
                    break;
            }
        }

        private void RunOptimization(Session session, string[] args, bool autoApply)
        {
            var system = session.EnsureValidSystem();
            var mf = session.EnsureMeritFunction();
            var glassMgr = session.EnsureGlassCatalog();
            session.ValidateGlass();

            // Defaults match LocalOptimizer's own defaults so the CLI is
            // the most-faithful surface to the engine. maxiter lifted
            // 200 -> 4000 to match the GUI dialog and the engine's
            // expectation that LM converges well before that.
            int maxIter = 4000;
            double tol = 1e-10;
            double damping = 0.001;
            bool useBroyden = true;
            int broydenRefresh = 5;

            // 'optimize try' captures a snapshot up-front so we can revert
            // if the user doesn't like the result. 'optimize run' skips
            // this — it's the auto-commit path, same as before.
            string? snapshot = autoApply ? null : session.SnapshotSystem();

            for (int i = 1; i < args.Length; i++)
            {
                var kv = args[i].Split(new[] { '=' }, 2);
                if (kv.Length != 2) continue;
                switch (kv[0].ToLowerInvariant())
                {
                    case "maxiter":
                        int.TryParse(kv[1], out maxIter);
                        break;
                    case "tol":
                        double.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out tol);
                        break;
                    case "damping":
                        double.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out damping);
                        break;
                    case "broyden":
                        // Accept true/false/1/0/yes/no for ergonomics.
                        var b = kv[1].Trim().ToLowerInvariant();
                        useBroyden = b == "true" || b == "1" || b == "yes" || b == "y";
                        break;
                    case "refresh":
                        int.TryParse(kv[1], out broydenRefresh);
                        break;
                }
            }

            var optimizer = new LocalOptimizer(system, mf, glassMgr, session.ConfigEditor)
            {
                MaxIterations = maxIter,
                Tolerance = tol,
                InitialDamping = damping,
                UseBroydenUpdate = useBroyden,
                BroydenRefreshInterval = broydenRefresh,
                ParallelEvaluation = true
            };

            optimizer.CollectVariables();

            // Set up cancellation
            _cts = new CancellationTokenSource();

            // Handle Ctrl+C
            Console.CancelKeyPress += OnCancelKeyPress;

            optimizer.OnProgress = progress =>
            {
                if (progress.Iteration % 10 == 0 || progress.Iteration < 5)
                {
                    AnsiConsole.MarkupLine(
                        $"  Iter {progress.Iteration,4}: Merit = {progress.MeritValue:E6}  Lambda = {progress.DampingFactor:E2}");
                }
            };

            AnsiConsole.MarkupLine($"[bold]Starting optimization[/]");
            AnsiConsole.MarkupLine($"  Variables: {CountVariables(system, session.ConfigEditor)}");
            AnsiConsole.MarkupLine($"  Operands: {mf.Operands.Count}");
            AnsiConsole.MarkupLine($"  Max Iterations: {maxIter}");
            AnsiConsole.MarkupLine("");

            try
            {
                var result = optimizer.Optimize(_cts.Token);

                AnsiConsole.MarkupLine("");
                AnsiConsole.MarkupLine($"[bold]Optimization Complete[/]");
                AnsiConsole.MarkupLine($"  Initial Merit: {result.InitialMerit:E6}");
                AnsiConsole.MarkupLine($"  Final Merit:   {result.FinalMerit:E6}");
                AnsiConsole.MarkupLine($"  Iterations:    {result.Iterations}");
                AnsiConsole.MarkupLine($"  {Markup.Escape(result.Message)}");

                if (result.Converged)
                    AnsiConsole.MarkupLine("[green]Converged.[/]");
                else if (result.Cancelled)
                    AnsiConsole.MarkupLine("[yellow]Cancelled by user.[/]");

                // 'optimize try' — prompt to keep or revert. The optimizer
                // has already mutated the live system in place, so revert
                // means restoring from the pre-run snapshot we captured
                // above.
                if (!autoApply && snapshot != null)
                {
                    AnsiConsole.Markup("\n[bold]Keep optimized result? (k=keep / r=revert) [k]:[/] ");
                    var input = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
                    bool revert = input == "r" || input == "revert" || input == "n" || input == "no";
                    if (revert)
                    {
                        session.RestoreSystemSnapshot(snapshot);
                        AnsiConsole.MarkupLine("[yellow]Reverted to pre-optimization state.[/]");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[green]Kept optimized result.[/]");
                    }
                }
            }
            finally
            {
                Console.CancelKeyPress -= OnCancelKeyPress;
                _cts = null;
            }
        }

        private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            _cts?.Cancel();
            AnsiConsole.MarkupLine("\n[yellow]Cancelling optimization...[/]");
        }

        private void RunMultistart(Session session, string[] args)
        {
            var system = session.EnsureValidSystem();
            var mf = session.EnsureMeritFunction();
            var glassMgr = session.EnsureGlassCatalog();
            session.ValidateGlass();

            var settings = new MultistartSettings();

            // Parse args: trials=N, lm=N, initlm=N, sigma=V, cap=V, growth=V, glass=V, constrained
            for (int i = 1; i < args.Length; i++)
            {
                var parts = args[i].Split('=');
                var key = parts[0].ToLowerInvariant();
                var val = parts.Length > 1 ? parts[1] : "";

                switch (key)
                {
                    case "trials": if (int.TryParse(val, out int t)) settings.MaxTrials = t; break;
                    case "lm": if (int.TryParse(val, out int l)) settings.LmIterationsPerTrial = l; break;
                    case "initlm": if (int.TryParse(val, out int il)) settings.InitialLmIterations = il; break;
                    case "sigma": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double sg)) settings.InitialSigma = sg; break;
                    case "growth": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double gr)) settings.SigmaGrowth = gr; break;
                    case "cap": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double cp)) settings.SigmaCap = cp; break;
                    case "metropolis":
                        var m = val.Trim().ToLowerInvariant();
                        settings.EnableMetropolis = m == "true" || m == "1" || m == "yes" || m == "y";
                        break;
                    case "temp": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double tp)) settings.MetropolisTemperature = tp; break;
                    case "hjsteps": if (int.TryParse(val, out int hjs)) settings.HjStepsPerTrial = hjs; break;
                    case "hjstep": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double hjst)) settings.HjInitialStep = hjst; break;
                    case "glasslmmult": if (int.TryParse(val, out int gm)) settings.GlassSwapLmMultiplier = gm; break;
                    case "glass": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double g)) settings.GlassSubstitutionProbability = g / 100.0; break;
                    case "constrained": settings.ConstrainedOnly = true; break;
                    case "tol": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double tol)) settings.Tolerance = tol; break;
                    case "damping": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double damp)) settings.InitialDamping = damp; break;
                    case "broyden":
                        var b = val.Trim().ToLowerInvariant();
                        settings.UseBroydenUpdate = b == "true" || b == "1" || b == "yes" || b == "y";
                        break;
                    case "refresh": if (int.TryParse(val, out int rf)) settings.BroydenRefreshInterval = rf; break;
                    // GPU pre-screen (sieve candidates on the GPU) + 1.0.128 difference gate.
                    case "gpu": settings.UseGpuPreScreen = true; break;
                    case "mincurvchange":
                        if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double mcc))
                            settings.GpuPreScreenMinCurvatureChangePercent = mcc;
                        break;
                }
            }

            _cts = new CancellationTokenSource();
            Console.CancelKeyPress += OnCancelKeyPress;

            var filteredPaths = new System.Collections.Generic.List<string>(FindFilteredCatalogPaths());

            var optimizer = new MultistartOptimizer(system, mf, glassMgr, session.ConfigEditor)
            {
                Settings = settings,
                FilteredCatalogSearchPaths = filteredPaths.ToArray()
            };
            if (settings.UseGpuPreScreen)
            {
                string gateTxt = settings.GpuPreScreenMinCurvatureChangePercent > 0
                    ? settings.GpuPreScreenMinCurvatureChangePercent.ToString("G3") + "%"
                    : "off";
                AnsiConsole.MarkupLine($"  GPU pre-screen: ON  (difference gate {gateTxt})");
            }

            optimizer.OnProgress = p =>
            {
                if (p.IsInitialLm)
                {
                    if (p.InitialLmIteration % 10 == 0 || p.InitialLmIteration < 5)
                        AnsiConsole.MarkupLine($"  [grey]Init LM iter {p.InitialLmIteration}: Merit = {p.BestMerit:E6}[/]");
                }
                else if (p.Trial % 10 == 0 || p.Trial <= 5 || p.TrialsAccepted > 0 && p.Trial == p.MaxTrials)
                {
                    AnsiConsole.MarkupLine($"  Trial {p.Trial}/{p.MaxTrials}: Best = {p.BestMerit:E6} ({p.TrialsAccepted} accepted)");
                }
            };

            AnsiConsole.MarkupLine($"[bold]Starting multistart optimization[/]");
            AnsiConsole.MarkupLine($"  Trials: {settings.MaxTrials}, LM/trial: {settings.LmIterationsPerTrial}");
            AnsiConsole.MarkupLine($"  Sigma: start {settings.InitialSigma:G3} → cap {settings.SigmaCap:G3} (grow ×{settings.SigmaGrowth:G3} on reject), Metropolis: {(settings.EnableMetropolis ? "on" : "off")}");
            AnsiConsole.MarkupLine($"  HJ steps/trial: {settings.HjStepsPerTrial}, Glass-swap LM ×{settings.GlassSwapLmMultiplier}, Glass sub: {settings.GlassSubstitutionProbability * 100:F0}%");
            AnsiConsole.MarkupLine("");

            try
            {
                var result = optimizer.Optimize(_cts.Token);

                AnsiConsole.MarkupLine("");
                AnsiConsole.MarkupLine($"[bold]Multistart Complete[/]");
                AnsiConsole.MarkupLine($"  Initial Merit:  {result.InitialMerit:E6}");
                AnsiConsole.MarkupLine($"  Post-LM Merit:  {result.PostInitialLmMerit:E6}");
                AnsiConsole.MarkupLine($"  Final Merit:    {result.FinalMerit:E6}");
                AnsiConsole.MarkupLine($"  Trials: {result.TrialsRun}, Accepted: {result.TrialsAccepted}");
                AnsiConsole.MarkupLine($"  {Markup.Escape(result.Message)}");
            }
            finally
            {
                Console.CancelKeyPress -= OnCancelKeyPress;
                _cts = null;
            }
        }

        private void RunDeSeed(Session session, string[] args)
        {
            var system = session.EnsureValidSystem();
            var mf = session.EnsureMeritFunction();
            var glassMgr = session.EnsureGlassCatalog();
            session.ValidateGlass();

            var pset = new DePipelineSettings();
            pset.De.FilteredCatalogSearchPaths = FindFilteredCatalogPaths();
            string outDir = "deseed_results";
            string? polishFolder = null;   // when set: polish a saved DE result set (skip the search)

            for (int i = 1; i < args.Length; i++)
            {
                var parts = args[i].Split('=');
                var key = parts[0].ToLowerInvariant();
                var val = parts.Length > 1 ? parts[1] : "";
                switch (key)
                {
                    case "pop": if (int.TryParse(val, out int p)) pset.De.PopulationSize = p; break;
                    case "gens": if (int.TryParse(val, out int g)) pset.De.MaxGenerations = g; break;
                    case "stall": if (int.TryParse(val, out int st)) pset.De.StallGenerations = st; break;
                    case "f": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double fv)) pset.De.F = fv; break;
                    case "cr": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double cr)) pset.De.CR = cr; break;
                    case "glass": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double gp)) pset.De.GlassMutationProbability = gp / 100.0; break;
                    case "curvlimit": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double cl)) pset.De.CurvatureSeedLimit = cl; break;
                    case "gpu": pset.UseGpu = true; break;
                    case "seed": if (int.TryParse(val, out int sd)) pset.De.BaseSeed = sd; break;
                    case "emit": if (int.TryParse(val, out int em)) pset.De.SeedsToEmit = em; break;
                    // §8.5 conditioner surface inputs.
                    case "focus-surface": if (int.TryParse(val, out int fsf)) pset.De.FocusCompensatorSurface = fsf; break;
                    case "efl-surface": if (int.TryParse(val, out int esf)) pset.De.EflControlSurface = esf; break;
                    case "no-efl-adjust": pset.De.AdjustCurvatureForEfl = false; break;
                    case "efl-tol": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double et)) pset.De.EflAdjustTolerance = et; break;
                    case "no-condition": pset.De.ConditionFocusEfl = false; break;
                    // Post-DE polish.
                    case "polish":
                        pset.PolishMethod = val.ToLowerInvariant() switch
                        {
                            "none" => DePolishMethod.None,
                            "multistart" or "ms" => DePolishMethod.MultistartLm,
                            _ => DePolishMethod.LocalLm,
                        };
                        break;
                    case "polish-count": if (int.TryParse(val, out int pc)) pset.PolishCandidateCount = pc; break;
                    case "lm-iters": if (int.TryParse(val, out int li)) pset.LmIterations = li; break;
                    // Diverse output: polish the best of each distinct form + crowding (default on).
                    case "no-niche": pset.NichedOutput = false; break;
                    case "out": if (!string.IsNullOrEmpty(val)) outDir = val; break;
                    // Polish a previously-saved DE result set (folder of *.lhlt) — skips the search.
                    case "polish-folder": if (!string.IsNullOrEmpty(val)) polishFolder = val; break;
                }
            }

            _cts = new CancellationTokenSource();
            Console.CancelKeyPress += OnCancelKeyPress;

            // Time-throttle the high-frequency per-generation / per-trial updates so the console
            // shows live status (DE gens with elapsed + s/gen, "Polishing X of N" with the
            // candidate's running best and the pool best) without flooding. Phase-boundary
            // events (RestartIndex == 0, i.e. DE-complete) always print.
            var printWatch = Stopwatch.StartNew();
            Action<GlobalSearchProgress> onProg = p =>
            {
                if (p.RestartIndex == 0 || printWatch.ElapsedMilliseconds >= 500)
                {
                    AnsiConsole.MarkupLine($"  [grey]{Markup.Escape(p.StatusMessage)}[/]");
                    printWatch.Restart();
                }
            };

            DeOptimizationPipeline pipeline;
            List<OpticalSystem>? savedSystems = null;
            List<string>? savedLabels = null;

            if (!string.IsNullOrEmpty(polishFolder))
            {
                // ── Polish a previously-saved DE seed set (no DE search) ──
                if (!System.IO.Directory.Exists(polishFolder))
                { AnsiConsole.MarkupLine($"[red]Folder not found: {Markup.Escape(polishFolder)}[/]"); return; }
                var files = System.IO.Directory.GetFiles(polishFolder, "*.lhlt");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                if (files.Length == 0)
                { AnsiConsole.MarkupLine($"[red]No .lhlt files in {Markup.Escape(polishFolder)}[/]"); return; }

                savedSystems = new List<OpticalSystem>(files.Length);
                savedLabels = new List<string>(files.Length);
                LensHH.Core.MeritFunction.MeritFunction refMerit = mf;
                OpticalSystem refSys = system;
                for (int fi = 0; fi < files.Length; fi++)
                {
                    var read = LensHH.Core.IO.LhltReader.Read(files[fi]);
                    if (fi == 0) { refSys = read.System; refMerit = read.MeritFunction ?? mf; }
                    savedSystems.Add(read.System);
                    savedLabels.Add(System.IO.Path.GetFileName(files[fi]));
                }
                // The first file is the reference structure; the rest are validated against it.
                pipeline = new DeOptimizationPipeline(refSys, refMerit, glassMgr, session.ConfigEditor)
                { Settings = pset, OnProgress = onProg };
                mf = refMerit;   // save polished output with the folder's own merit function

                AnsiConsole.MarkupLine("[bold]DE polish — previously-saved results[/]");
                AnsiConsole.MarkupLine($"  Folder: {Markup.Escape(System.IO.Path.GetFullPath(polishFolder))}  ({files.Length} file(s))");
                AnsiConsole.MarkupLine($"  Polish: {pset.PolishMethod} on best {pset.PolishCandidateCount} (LM iters {pset.LmIterations})");
                AnsiConsole.MarkupLine("");
            }
            else
            {
                pipeline = new DeOptimizationPipeline(system, mf, glassMgr, session.ConfigEditor)
                { Settings = pset, OnProgress = onProg };

                bool gpuReq = pset.UseGpu;
                bool gpuAvail = GpuResidentDe.IsAvailable;
                AnsiConsole.MarkupLine("[bold]DE starting-design pipeline[/]");
                AnsiConsole.MarkupLine($"  Engine: {(gpuReq && gpuAvail ? "GPU-resident (population fills the device)" : gpuReq ? "CPU (GPU requested but unavailable)" : "CPU")}"
                    + $", generations: {pset.De.MaxGenerations}, conditioner: {(pset.De.ConditionFocusEfl ? "on" : "off")}"
                    + $" (focus surf {SurfLabel(pset.De.FocusCompensatorSurface)}, EFL surf {SurfLabel(pset.De.EflControlSurface)})");
                AnsiConsole.MarkupLine($"  Polish: {pset.PolishMethod} on best {pset.PolishCandidateCount} (LM iters {pset.LmIterations}); seeds emitted: {pset.De.SeedsToEmit}");
                AnsiConsole.MarkupLine("");
            }

            try
            {
                var result = !string.IsNullOrEmpty(polishFolder)
                    ? pipeline.PolishExisting(savedSystems!, savedLabels, _cts.Token)
                    : pipeline.Run(_cts.Token);
                AnsiConsole.MarkupLine("");
                if (result.DeElapsed > TimeSpan.Zero)
                    AnsiConsole.MarkupLine($"  [grey]DE: {result.Generations} gens in {result.DeElapsed.TotalSeconds:F1}s ({result.DeSecondsPerGeneration:F3} s/gen)[/]");
                AnsiConsole.MarkupLine($"[bold]Pipeline complete[/]  {Markup.Escape(result.Message)}");

                // Run log OUTSIDE the lens-file folders (seeds_pre_polish/ + polished/ stay pure) —
                // settings header + per-candidate source/before/after/elapsed, for easy run comparison.
                try
                {
                    System.IO.Directory.CreateDirectory(outDir);
                    string logStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string logMode = !string.IsNullOrEmpty(polishFolder) ? "Polish previously-saved DE results" : "DE search + polish";
                    string logSrc = !string.IsNullOrEmpty(polishFolder) ? polishFolder! : "(DE search from loaded design)";
                    string logPath = System.IO.Path.Combine(outDir, $"de_run_{logStamp}.log");
                    System.IO.File.WriteAllText(logPath, DeRunLog.Build(pset, result, logMode, logSrc, DateTime.Now));
                    AnsiConsole.MarkupLine($"  [grey]Run log: {Markup.Escape(System.IO.Path.GetFullPath(logPath))}[/]");
                }
                catch (Exception logEx) { AnsiConsole.MarkupLine($"  [yellow]Run log not written: {Markup.Escape(logEx.Message)}[/]"); }

                string title = string.IsNullOrWhiteSpace(system.Title) ? "design" : system.Title;

                // ── Save ALL pre-polish seeds by default (skip when polishing a saved folder —
                //    that folder already IS the pre-polish set) ──
                if (string.IsNullOrEmpty(polishFolder))
                {
                string preDir = System.IO.Path.Combine(outDir, "seeds_pre_polish");
                System.IO.Directory.CreateDirectory(preDir);
                int nPre = 0;
                for (int r = 0; r < result.SeedPool.Models.Count; r++)
                {
                    var m = result.SeedPool.Models[r];
                    string glassTag = m.GlassSet.Count > 0 ? string.Join("-", m.GlassSet) : "noglass";
                    string name = SanitizeFileName($"{title}_seed{r + 1:00}_merit{m.Merit:G4}_{glassTag}");
                    try { LensHH.Core.IO.LhltWriter.Write(m.System, System.IO.Path.Combine(preDir, name + ".lhlt"), mf, session.ConfigEditor); nPre++; } catch { }
                }
                AnsiConsole.MarkupLine($"  [grey]Saved {nPre} pre-polish seed(s) to {Markup.Escape(System.IO.Path.GetFullPath(preDir))}[/]");
                }

                // ── Polished candidates (best-first) + before/after ──
                if (result.Polished.Count > 0)
                {
                    string postDir = System.IO.Path.Combine(outDir, "polished");
                    System.IO.Directory.CreateDirectory(postDir);
                    AnsiConsole.MarkupLine($"[bold]Polished ({pset.PolishMethod}) — before → after[/]");
                    int n = 0;
                    for (int r = 0; r < result.Polished.Count; r++)
                    {
                        var c = result.Polished[r];
                        string glassTag = c.GlassSet.Count > 0 ? string.Join("-", c.GlassSet) : "noglass";
                        string name = SanitizeFileName($"{title}_polished{r + 1:00}_merit{c.MeritAfter:G4}_{glassTag}");
                        try { LensHH.Core.IO.LhltWriter.Write(c.System, System.IO.Path.Combine(postDir, name + ".lhlt"), mf, session.ConfigEditor); n++; } catch { }
                        double x = c.MeritAfter > 1e-30 ? c.MeritBefore / c.MeritAfter : 0;
                        AnsiConsole.MarkupLine($"  #{r + 1,2}  {c.MeritBefore:E4} → {c.MeritAfter:E4} ({x:F1}×)  [[{Markup.Escape(glassTag)}]]");
                    }
                    AnsiConsole.MarkupLine($"  [bold]Wrote {n} polished design(s) to {Markup.Escape(System.IO.Path.GetFullPath(postDir))}[/]");
                }
            }
            finally
            {
                Console.CancelKeyPress -= OnCancelKeyPress;
                _cts = null;
            }
        }

        // Surface label for the conditioner inputs (-1 = auto).
        private static string SurfLabel(int s) => s < 0 ? "auto" : s.ToString();

        private void RunGlobalSearch(Session session, string[] args)
        {
            var system = session.EnsureValidSystem();
            var mf = session.EnsureMeritFunction();
            var glassMgr = session.EnsureGlassCatalog();
            session.ValidateGlass();

            // Defaults mirror the GUI dialog: don't pre-polish the start, escape
            // fast (sigma cap 0.01, stall-at-cap 1), 50% glass swaps, deep per-trial LM.
            var gs = new GlobalSearchSettings { StopAtCapStallBatches = 1, MaxTrialsPerRestart = 2000 };
            gs.Multistart.InitialLmIterations = 0;        // 0 = perturb the raw start (no pre-polish)
            gs.Multistart.SigmaCap = 0.01;
            gs.Multistart.GlassSubstitutionProbability = 0.5;
            gs.Multistart.LmIterationsPerTrial = 4000;
            bool useNative = false, analytic = false;
            string outDir = "global_search_results";

            for (int i = 1; i < args.Length; i++)
            {
                var parts = args[i].Split('=');
                var key = parts[0].ToLowerInvariant();
                var val = parts.Length > 1 ? parts[1] : "";
                switch (key)
                {
                    case "models": if (int.TryParse(val, out int mk)) gs.ModelsToKeep = mk; break;
                    case "restarts": if (int.TryParse(val, out int mr)) gs.MaxRestarts = mr; break;
                    case "trials": if (int.TryParse(val, out int t)) gs.MaxTrialsPerRestart = t; break;
                    case "lm": if (int.TryParse(val, out int l)) gs.Multistart.LmIterationsPerTrial = l; break;
                    case "stall": if (int.TryParse(val, out int st)) gs.StopAtCapStallBatches = st; break;
                    case "seed": if (int.TryParse(val, out int sd)) gs.BaseSeed = sd; break;
                    case "prepolish": if (int.TryParse(val, out int pp)) gs.Multistart.InitialLmIterations = pp; break;
                    case "sigma": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double sgv)) gs.Multistart.InitialSigma = sgv; break;
                    case "cap": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double cpv)) gs.Multistart.SigmaCap = cpv; break;
                    case "glass": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double gv)) gs.Multistart.GlassSubstitutionProbability = gv / 100.0; break;
                    case "native": useNative = true; break;
                    case "analytic": analytic = true; break;
                    case "out": if (!string.IsNullOrEmpty(val)) outDir = val; break;
                }
            }

            _cts = new CancellationTokenSource();
            Console.CancelKeyPress += OnCancelKeyPress;

            var svc = new GlobalSearchService(system, mf, glassMgr, session.ConfigEditor)
            {
                Settings = gs,
                EngineMode = useNative
                    ? LensHH.Core.MeritFunction.EngineMode.Native
                    : LensHH.Core.MeritFunction.EngineMode.CSharp,
                NativeDerivativeMode = analytic
                    ? LensHH.Core.NativeInterop.MeritDerivativeMode.Analytic
                    : LensHH.Core.NativeInterop.MeritDerivativeMode.FiniteDifference,
                FilteredCatalogSearchPaths = FindFilteredCatalogPaths(),
                OnProgress = p => AnsiConsole.MarkupLine($"  [grey]{Markup.Escape(p.StatusMessage)}[/]"),
            };

            AnsiConsole.MarkupLine("[bold]Starting Global Search[/]");
            AnsiConsole.MarkupLine($"  Models: {gs.ModelsToKeep}, max restarts: {gs.MaxRestarts}, trials/restart: {gs.MaxTrialsPerRestart}, LM/trial: {gs.Multistart.LmIterationsPerTrial}");
            AnsiConsole.MarkupLine($"  Base seed: {gs.BaseSeed}, stall-at-cap: {gs.StopAtCapStallBatches}, pre-polish LM: {gs.Multistart.InitialLmIterations}, glass sub: {gs.Multistart.GlassSubstitutionProbability * 100:F0}%");
            AnsiConsole.MarkupLine("");

            try
            {
                var result = svc.Run(_cts.Token);

                AnsiConsole.MarkupLine("");
                AnsiConsole.MarkupLine($"[bold]Global Search Complete[/]  {Markup.Escape(result.Message)}");

                System.IO.Directory.CreateDirectory(outDir);
                string title = string.IsNullOrWhiteSpace(system.Title) ? "design" : system.Title;
                int n = 0;
                for (int r = 0; r < result.Models.Count; r++)
                {
                    var mdl = result.Models[r];
                    string glassTag = mdl.GlassSet.Count > 0 ? string.Join("-", mdl.GlassSet) : "noglass";
                    string form = string.IsNullOrEmpty(mdl.PowerSignSignature) ? "-" : mdl.PowerSignSignature;
                    string name = SanitizeFileName($"{title}_global_rank{r + 1}_seed{mdl.Seed}_merit{mdl.Merit:G4}_{glassTag}");
                    string path = System.IO.Path.Combine(outDir, name + ".lhlt");
                    try { LensHH.Core.IO.LhltWriter.Write(mdl.System, path, mf, session.ConfigEditor); n++; }
                    catch (Exception ex) { AnsiConsole.MarkupLine($"  [yellow]write failed: {Markup.Escape(ex.Message)}[/]"); }
                    AnsiConsole.MarkupLine($"  #{r + 1}  merit {mdl.Merit:E4}  form {form}  [[{Markup.Escape(glassTag)}]]  seed {mdl.Seed}");
                }
                AnsiConsole.MarkupLine($"  Wrote {n} design(s) to {Markup.Escape(System.IO.Path.GetFullPath(outDir))}");
            }
            finally
            {
                Console.CancelKeyPress -= OnCancelKeyPress;
                _cts = null;
            }
        }

        private static string SanitizeFileName(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '_');
            return sb.ToString();
        }

        // EXPERIMENTAL: memetic DE — interleave DE bursts with niched-best polish + reseed.
        private sealed class MemeticPopulationFile
        {
            public int PopulationSize { get; set; }
            public int VarCount { get; set; }
            public int Seed { get; set; }
            public double[][] Genes { get; set; } = Array.Empty<double[]>();
        }

        private void RunMemetic(Session session, string[] args)
        {
            var system = session.EnsureValidSystem();
            var mf = session.EnsureMeritFunction();
            var glassMgr = session.EnsureGlassCatalog();
            session.ValidateGlass();

            var mset = new MemeticDeSettings();
            bool useGpu = false;
            string outDir = "memetic_results";
            string? resumeDir = null;

            for (int i = 1; i < args.Length; i++)
            {
                var parts = args[i].Split('=');
                var key = parts[0].ToLowerInvariant();
                var val = parts.Length > 1 ? parts[1] : "";
                switch (key)
                {
                    case "rounds": if (int.TryParse(val, out int rr)) mset.Rounds = rr; break;
                    case "gens": if (int.TryParse(val, out int gg)) mset.GenerationsPerRound = gg; break;
                    case "polish-count": case "zzz": if (int.TryParse(val, out int zz)) mset.PolishCount = zz; break;
                    case "pop": if (int.TryParse(val, out int pp)) mset.PopulationSize = pp; break;
                    case "f": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double fv)) mset.F = fv; break;
                    case "cr": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double cv)) mset.CR = cv; break;
                    case "clones": if (int.TryParse(val, out int cl)) mset.ClonesPerElite = cl; break;
                    case "sigma": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double sg)) mset.InitialPerturbSigma = sg; break;
                    case "niche": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double nd)) mset.NicheMinDistance = nd; break;
                    case "lm-iters": if (int.TryParse(val, out int li)) mset.LmIterations = li; break;
                    case "seed": if (int.TryParse(val, out int sd)) mset.Seed = sd; break;
                    case "polish":
                        mset.Polish = val.ToLowerInvariant() is "multistart" or "ms"
                            ? MemeticPolishMethod.Multistart : MemeticPolishMethod.Lm;
                        break;
                    case "gpu": useGpu = true; break;
                    case "out": if (!string.IsNullOrEmpty(val)) outDir = val; break;
                    case "resume": if (!string.IsNullOrEmpty(val)) resumeDir = val; break;
                }
            }

            // ── Resume: load a saved population (unbounded genes) to continue evolution. ──
            double[][]? resumeGenes = null;
            if (!string.IsNullOrEmpty(resumeDir))
            {
                string popPath = System.IO.Path.Combine(resumeDir, "population.json");
                if (!System.IO.File.Exists(popPath))
                { AnsiConsole.MarkupLine($"[red]No population.json in {Markup.Escape(resumeDir)}[/]"); return; }
                try
                {
                    var pf = JsonSerializer.Deserialize<MemeticPopulationFile>(System.IO.File.ReadAllText(popPath));
                    resumeGenes = pf?.Genes;
                    if (resumeGenes != null)
                        AnsiConsole.MarkupLine($"  [grey]Resuming from {resumeGenes.Length} saved member(s).[/]");
                }
                catch (Exception ex)
                { AnsiConsole.MarkupLine($"[red]Could not read population.json: {Markup.Escape(ex.Message)}[/]"); return; }
            }

            bool gpu = useGpu && GpuMemeticDeOptimizer.IsAvailable;
            MemeticDeOptimizer opt = gpu
                ? new GpuMemeticDeOptimizer(system, mf, glassMgr, session.ConfigEditor) { Settings = mset, InitialPopulation = resumeGenes }
                : new MemeticDeOptimizer(system, mf, glassMgr, session.ConfigEditor) { Settings = mset, InitialPopulation = resumeGenes };

            _cts = new CancellationTokenSource();
            Console.CancelKeyPress += OnCancelKeyPress;

            var printWatch = Stopwatch.StartNew();
            opt.OnProgress = pr =>
            {
                if (printWatch.ElapsedMilliseconds < 400) return;
                printWatch.Restart();
                AnsiConsole.MarkupLine($"  [grey]round {pr.Round + 1}/{pr.MaxRounds}  {pr.Phase,-8} gen {pr.Generation}  best={pr.BestMerit:E5}[/]");
            };

            AnsiConsole.MarkupLine("[bold]Memetic DE (experimental)[/]");
            AnsiConsole.MarkupLine($"  Engine: {(useGpu && gpu ? "GPU-resident (population fills the device)" : useGpu ? "CPU (GPU requested but unavailable)" : "CPU")}");
            AnsiConsole.MarkupLine($"  Rounds: {mset.Rounds}, gens/round: {mset.GenerationsPerRound}, population: {mset.PopulationSize}");
            AnsiConsole.MarkupLine($"  Polish: {mset.Polish} on niched-best {mset.PolishCount} (LM iters {mset.LmIterations}); F={mset.F:G3}, CR={mset.CR:G3}");
            AnsiConsole.MarkupLine("");

            MemeticDeResult result;
            try { result = opt.Run(_cts.Token); }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Memetic DE failed: {Markup.Escape(ex.Message)}[/]");
                (opt as IDisposable)?.Dispose();
                Console.CancelKeyPress -= OnCancelKeyPress;
                return;
            }
            (opt as IDisposable)?.Dispose();
            Console.CancelKeyPress -= OnCancelKeyPress;

            AnsiConsole.MarkupLine("");
            AnsiConsole.MarkupLine($"[bold]Memetic DE complete[/]  {Markup.Escape(result.Message)}");
            AnsiConsole.MarkupLine($"  {result.EvaluationCount:N0} candidate evals in {result.Elapsed.TotalSeconds:F1}s"
                + (result.Cancelled ? "  [yellow](stopped)[/]" : ""));

            // ── Save the diverse best designs + the population for restart. ──
            try
            {
                System.IO.Directory.CreateDirectory(outDir);
                string title = string.IsNullOrWhiteSpace(system.Title) ? "design" : system.Title;
                string bestDir = System.IO.Path.Combine(outDir, "best");
                System.IO.Directory.CreateDirectory(bestDir);

                AnsiConsole.MarkupLine($"[bold]Diverse best ({result.Best.Count})[/]");
                for (int r = 0; r < result.Best.Count; r++)
                {
                    var d = result.Best[r];
                    AnsiConsole.MarkupLine($"  #{r + 1}  merit={d.Merit:E6}  form={Markup.Escape(d.FormSignature)}");
                    string name = SanitizeFileName($"{title}_memetic{r + 1:00}_merit{d.Merit:G4}");
                    try { LensHH.Core.IO.LhltWriter.Write(d.System, System.IO.Path.Combine(bestDir, name + ".lhlt"), mf, session.ConfigEditor); } catch { }
                }
                AnsiConsole.MarkupLine($"  [grey]Saved {result.Best.Count} design(s) to {Markup.Escape(System.IO.Path.GetFullPath(bestDir))}[/]");

                if (result.FinalPopulation != null)
                {
                    var pf = new MemeticPopulationFile
                    {
                        PopulationSize = result.FinalPopulation.Length,
                        VarCount = result.FinalPopulation.Length > 0 ? result.FinalPopulation[0].Length : 0,
                        Seed = mset.Seed,
                        Genes = result.FinalPopulation,
                    };
                    string popPath = System.IO.Path.Combine(outDir, "population.json");
                    System.IO.File.WriteAllText(popPath, JsonSerializer.Serialize(pf));
                    AnsiConsole.MarkupLine($"  [grey]Population saved to {Markup.Escape(System.IO.Path.GetFullPath(popPath))} (restart with resume={Markup.Escape(outDir)})[/]");
                }
            }
            catch (Exception ex) { AnsiConsole.MarkupLine($"  [yellow]Save failed: {Markup.Escape(ex.Message)}[/]"); }
        }

        private void RunBasinHopping(Session session, string[] args)
        {
            var system = session.EnsureValidSystem();
            var mf = session.EnsureMeritFunction();
            var glassMgr = session.EnsureGlassCatalog();
            session.ValidateGlass();

            var settings = new BasinHoppingSettings();
            string? saveChainsFolder = null;

            for (int i = 1; i < args.Length; i++)
            {
                if (args[i].Equals("constrained", StringComparison.OrdinalIgnoreCase))
                {
                    settings.ConstrainedOnly = true;
                    continue;
                }
                var parts = args[i].Split('=', 2);
                if (parts.Length != 2) continue;
                var key = parts[0].ToLowerInvariant();
                var val = parts[1];
                switch (key)
                {
                    case "hops": if (int.TryParse(val, out int h)) settings.MaxHops = h; break;
                    case "lm": if (int.TryParse(val, out int l)) settings.LmIterationsPerHop = l; break;
                    case "hj": if (int.TryParse(val, out int hj)) settings.HjStepsPerHop = hj; break;
                    case "sigma": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double sg)) settings.InitialPerturbSigma = sg; break;
                    case "hjstep": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double hs)) settings.HjInitialStep = hs; break;
                    case "hjmin": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double hm)) settings.HjMinStep = hm; break;
                    case "tol": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double tol)) settings.LmTolerance = tol; break;
                    case "damping": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double dmp)) settings.LmInitialDamping = dmp; break;
                    case "broyden":
                        var b = val.Trim().ToLowerInvariant();
                        settings.UseBroydenUpdate = b == "true" || b == "1" || b == "yes" || b == "y";
                        break;
                    case "glasssub":
                        var gs = val.Trim().ToLowerInvariant();
                        settings.GlassSubstitution = gs == "true" || gs == "1" || gs == "yes" || gs == "y";
                        break;
                    case "onlypreferred":
                        var op = val.Trim().ToLowerInvariant();
                        settings.OnlyPreferred = op == "true" || op == "1" || op == "yes" || op == "y";
                        break;
                    case "catalog":
                    case "catalogs":
                        settings.GlassCatalogs = new System.Collections.Generic.List<string>(
                            val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                        break;
                    case "seed": if (int.TryParse(val, out int sd)) settings.Seed = sd; break;
                    // Parallel independent chains (each from its own perturbation seed); the
                    // single global best is returned. 1 = classic single chain; 0 = auto (physical cores).
                    case "chains": if (int.TryParse(val, out int ch)) settings.ParallelChains = ch; break;
                    // Persist EVERY chain's final design as a separate .lhlt in this folder.
                    case "savechains":
                    case "savechainsfolder": saveChainsFolder = val; break;
                }
            }

            _cts = new CancellationTokenSource();
            Console.CancelKeyPress += OnCancelKeyPress;

            // Always use the batch orchestrator: chains==1 routes to the single-chain
            // optimizer (identical to before), chains>1 runs N parallel walks.
            var optimizer = new BasinHoppingOptimizerBatch(system, mf, glassMgr, session.ConfigEditor)
            {
                Settings = settings,
                FilteredCatalogSearchPaths = FindFilteredCatalogPaths(),
            };

            // Throttle progress: with N chains, OnProgress fires from N threads on every
            // hop — print at most ~3×/s so the console isn't flooded (and timing is clean).
            var progressWatch = System.Diagnostics.Stopwatch.StartNew();
            object progressLock = new object();
            optimizer.OnProgress = p =>
            {
                if (p.Hop <= 0) return;
                lock (progressLock)
                {
                    if (progressWatch.ElapsedMilliseconds < 333) return;
                    progressWatch.Restart();
                    AnsiConsole.MarkupLine(
                        $"  Hop {p.Hop}/{p.MaxHops}: {Markup.Escape(p.Phase),-18} best={p.BestMerit:E6} ({p.Accepted} acc / {p.Rejected} rej{(settings.GlassSubstitution ? $", {p.GlassSwaps} swaps" : "")})");
                }
            };

            int resolvedChains = settings.ParallelChains <= 0
                ? LensHH.Core.Optimization.CpuInfo.PhysicalCoreCount() : settings.ParallelChains;
            AnsiConsole.MarkupLine("[bold]Starting basin-hopping optimization[/]");
            AnsiConsole.MarkupLine($"  Parallel chains: {resolvedChains}{(settings.ParallelChains <= 0 ? " (auto = physical cores)" : "")}");
            AnsiConsole.MarkupLine($"  Hops/chain: {settings.MaxHops}, LM/hop: {settings.LmIterationsPerHop}, HJ/hop: {settings.HjStepsPerHop}");
            AnsiConsole.MarkupLine($"  Initial sigma: {settings.InitialPerturbSigma:G3}, HJ step: {settings.HjInitialStep:G3}");
            AnsiConsole.MarkupLine($"  Glass substitution: {(settings.GlassSubstitution ? "ON (" + string.Join(", ", settings.GlassCatalogs) + ")" : "OFF")}");
            AnsiConsole.MarkupLine("");

            try
            {
                var result = optimizer.Optimize(_cts.Token);

                AnsiConsole.MarkupLine("");
                AnsiConsole.MarkupLine("[bold]Basin Hopping Complete[/]");
                AnsiConsole.MarkupLine($"  Initial Merit: {result.InitialMerit:E6}");
                AnsiConsole.MarkupLine($"  Final Merit:   {result.FinalMerit:E6}");
                AnsiConsole.MarkupLine($"  Chains: {optimizer.ChainsRun}, total hops: {result.Hops}, Accepted: {result.Accepted}, Rejected: {result.Rejected}, Glass Swaps: {result.GlassSwaps}");
                AnsiConsole.MarkupLine($"  Wall time: {result.Elapsed.TotalSeconds:F2} s");
                if (!string.IsNullOrEmpty(result.Message))
                    AnsiConsole.MarkupLine($"  {Markup.Escape(result.Message)}");
                if (result.Cancelled)
                    AnsiConsole.MarkupLine("[yellow]Cancelled by user.[/]");

                if (!string.IsNullOrWhiteSpace(saveChainsFolder))
                {
                    string baseName = string.IsNullOrWhiteSpace(system.Title) ? "basin" : system.Title;
                    var paths = LensHH.Core.IO.ChainResultWriter.SaveChains(
                        optimizer.ChainResults, saveChainsFolder!, baseName, mf, session.ConfigEditor);
                    AnsiConsole.MarkupLine("");
                    AnsiConsole.MarkupLine($"[green]Saved {paths.Count} chain design(s)[/] to {Markup.Escape(saveChainsFolder!)}");
                }
            }
            finally
            {
                Console.CancelKeyPress -= OnCancelKeyPress;
                _cts = null;
            }
        }

        private void RunGlobalBasinHopping(Session session, string[] args)
        {
            var system = session.EnsureValidSystem();
            var mf = session.EnsureMeritFunction();
            var glassMgr = session.EnsureGlassCatalog();
            session.ValidateGlass();

            var gs = new GlobalBasinHoppingSettings();   // Chain defaults + 600s watchdog + 120min global
            var s = gs.Chain;
            string? saveChainsFolder = null;
            int applyChain = -1;

            for (int i = 1; i < args.Length; i++)
            {
                if (args[i].Equals("constrained", StringComparison.OrdinalIgnoreCase))
                {
                    s.ConstrainedOnly = true;   // "Only randomize constrained variables"
                    continue;
                }
                var parts = args[i].Split('=', 2);
                if (parts.Length != 2) continue;
                var key = parts[0].ToLowerInvariant();
                var val = parts[1];
                switch (key)
                {
                    case "hops": if (int.TryParse(val, out int h)) s.MaxHops = h; break;
                    case "lm": if (int.TryParse(val, out int l)) s.LmIterationsPerHop = l; break;
                    case "hj": if (int.TryParse(val, out int hj)) s.HjStepsPerHop = hj; break;
                    case "sigma": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double sg)) s.InitialPerturbSigma = sg; break;
                    case "seed": if (int.TryParse(val, out int sd)) s.Seed = sd; break;
                    case "broyden":
                        var b = val.Trim().ToLowerInvariant();
                        s.UseBroydenUpdate = b == "true" || b == "1" || b == "yes" || b == "y";
                        break;
                    case "glasssub":
                        var g = val.Trim().ToLowerInvariant();
                        s.GlassSubstitution = g == "true" || g == "1" || g == "yes" || g == "y";
                        break;
                    case "rescale":   // rescale curvature on glass swap
                        var rs = val.Trim().ToLowerInvariant();
                        s.RescaleCurvatureOnGlassSwap = rs == "true" || rs == "1" || rs == "yes" || rs == "y";
                        break;
                    case "onlypreferred":
                        var op = val.Trim().ToLowerInvariant();
                        s.OnlyPreferred = op == "true" || op == "1" || op == "yes" || op == "y";
                        break;
                    case "catalog":
                    case "catalogs":
                        s.GlassCatalogs = new System.Collections.Generic.List<string>(
                            val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                        break;
                    // No-improvement watchdog (mandatory ON) in seconds; ≤0 → engine default 600.
                    case "timeout":
                    case "noimprovement": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double to)) s.NoImprovementTimeoutSeconds = to; break;
                    // Global wall-clock budget in minutes.
                    case "globalmin":
                    case "globaltimeout": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double gm)) gs.GlobalTimeoutMinutes = gm; break;
                    case "savechains":
                    case "savechainsfolder": saveChainsFolder = val; break;
                    case "apply": int.TryParse(val, out applyChain); break;
                }
            }

            _cts = new CancellationTokenSource();
            Console.CancelKeyPress += OnCancelKeyPress;

            var optimizer = new GlobalBasinHoppingOptimizer(system, mf, glassMgr, session.ConfigEditor)
            {
                Settings = gs,
                FilteredCatalogSearchPaths = FindFilteredCatalogPaths(),
            };

            // Throttle the per-chain snapshot to ~3×/s (it fires per hop from N threads).
            var progressWatch = System.Diagnostics.Stopwatch.StartNew();
            object progressLock = new object();
            optimizer.OnChainsProgress = snap =>
            {
                lock (progressLock)
                {
                    if (progressWatch.ElapsedMilliseconds < 333) return;
                    progressWatch.Restart();
                    double best = double.NaN; int bestChain = -1; long hops = 0; int restarts = 0;
                    foreach (var c in snap)
                    {
                        hops += c.Hops; restarts += c.Restarts;
                        if (!double.IsNaN(c.Best) && (double.IsNaN(best) || c.Best < best)) { best = c.Best; bestChain = c.Chain; }
                    }
                    AnsiConsole.MarkupLine($"  best={best:E6} (chain {bestChain}) | {snap.Length} chains | {restarts} restarts | {hops} hops");
                }
            };

            int cores = LensHH.Core.Optimization.CpuInfo.PhysicalCoreCount();
            double watchdog = s.NoImprovementTimeoutSeconds > 0 ? s.NoImprovementTimeoutSeconds : GlobalBasinHoppingSettings.DefaultChainTimeoutSeconds;
            AnsiConsole.MarkupLine("[bold]Starting GLOBAL Basin-Hopping HJ+LM[/]");
            AnsiConsole.MarkupLine($"  Chains: {cores} (physical cores, fixed)");
            AnsiConsole.MarkupLine($"  Hops/episode: {s.MaxHops}, LM/hop: {s.LmIterationsPerHop}, HJ/hop: {s.HjStepsPerHop}, sigma: {s.InitialPerturbSigma:G3}");
            AnsiConsole.MarkupLine($"  No-improvement timeout: {watchdog:F0} s (mandatory)   Global limit: {gs.GlobalTimeoutMinutes:F0} min");
            AnsiConsole.MarkupLine($"  Glass substitution: {(s.GlassSubstitution ? "ON" : "OFF")}{(s.GlassSubstitution && s.RescaleCurvatureOnGlassSwap ? " (rescale on swap)" : "")}");
            AnsiConsole.MarkupLine("[grey]Press Ctrl+C to stop early.[/]");
            AnsiConsole.MarkupLine("");

            try
            {
                var result = optimizer.Optimize(_cts.Token);

                AnsiConsole.MarkupLine("");
                AnsiConsole.MarkupLine("[bold]Global Basin-Hopping Complete[/]");
                AnsiConsole.MarkupLine($"  Initial Merit: {result.InitialMerit:E6}");
                AnsiConsole.MarkupLine($"  Final Merit:   {result.FinalMerit:E6}");
                AnsiConsole.MarkupLine($"  Chains: {result.ChainsRun}, restarts: {result.TotalRestarts}, total hops: {result.TotalHops}");
                AnsiConsole.MarkupLine($"  Wall time: {result.Elapsed.TotalSeconds:F1} s  ({(result.TimedOut ? "global time limit reached" : result.Cancelled ? "stopped by user" : "completed")})");
                if (!string.IsNullOrEmpty(result.Message))
                    AnsiConsole.MarkupLine($"  {Markup.Escape(result.Message)}");

                if (result.ChainResults.Count > 0)
                {
                    AnsiConsole.MarkupLine("");
                    AnsiConsole.MarkupLine("[bold]Per-chain best designs:[/]");
                    foreach (var c in result.ChainResults.OrderBy(c => c.Merit))
                        AnsiConsole.MarkupLine($"  chain {c.ChainIndex,2}: merit {c.Merit:E6}{(c.IsBest ? "   [green]<- global best[/]" : "")}");
                }

                // The engine already applied the global best to `system`. apply=N overrides
                // with a specific chain's design (e.g. to take a runner-up form).
                if (applyChain >= 0)
                {
                    var pick = result.ChainResults.FirstOrDefault(c => c.ChainIndex == applyChain);
                    if (pick.System != null) { system.CopyFrom(pick.System); AnsiConsole.MarkupLine($"[green]Applied chain {applyChain}'s design to the system.[/]"); }
                    else AnsiConsole.MarkupLine($"[yellow]apply={applyChain}: no such chain result; kept the global best.[/]");
                }

                if (!string.IsNullOrWhiteSpace(saveChainsFolder))
                {
                    string baseName = string.IsNullOrWhiteSpace(system.Title) ? "global_basin" : system.Title;
                    var paths = LensHH.Core.IO.ChainResultWriter.SaveChains(
                        result.ChainResults, saveChainsFolder!, baseName, mf, session.ConfigEditor);
                    AnsiConsole.MarkupLine("");
                    AnsiConsole.MarkupLine($"[green]Saved {paths.Count} chain design(s)[/] to {Markup.Escape(saveChainsFolder!)}");
                }
            }
            finally
            {
                Console.CancelKeyPress -= OnCancelKeyPress;
                _cts = null;
            }
        }

        private void RunSplitElement(Session session, string[] args)
        {
            session.EnsureGlassCatalog();
            session.ValidateGlass();
            if (session.CurrentMeritFunction == null || session.CurrentMeritFunction.Operands.Count == 0)
            {
                AnsiConsole.MarkupLine("[red]No merit function defined. Add operands first.[/]");
                return;
            }

            var settings = new SplitElementSettings();

            for (int i = 1; i < args.Length; i++)
            {
                // Flags (no =value); allow either bare form.
                if (args[i].Equals("constrained", StringComparison.OrdinalIgnoreCase))
                {
                    settings.ConstrainedOnly = true;
                    continue;
                }
                if (args[i].Equals("noglass", StringComparison.OrdinalIgnoreCase))
                {
                    settings.SkipGlassTrials = true;
                    continue;
                }
                var parts = args[i].Split('=', 2);
                if (parts.Length != 2) continue;
                switch (parts[0].ToLowerInvariant())
                {
                    case "splits": if (int.TryParse(parts[1], out int s)) settings.MaxSplits = s; break;
                    case "trials": if (int.TryParse(parts[1], out int t)) settings.GlassTrials = t; break;
                    case "lm": if (int.TryParse(parts[1], out int l)) settings.LmIterationsPerTrial = l; break;
                    case "postlm": if (int.TryParse(parts[1], out int pl)) settings.PostSplitLmIterations = pl; break;
                    case "preglass": if (int.TryParse(parts[1], out int pg)) settings.PreGlassMultistartTrials = pg; break;
                    case "postglass": if (int.TryParse(parts[1], out int pog)) settings.PostGlassMultistartTrials = pog; break;
                    case "sigma": if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double r)) settings.MultistartInitialSigma = r; break;
                    case "freeglasses":
                        var fg = parts[1].Trim().ToLowerInvariant();
                        settings.FreeAllGlasses = fg == "true" || fg == "1" || fg == "yes" || fg == "y";
                        break;
                    case "rejectifworse":
                        var rw = parts[1].Trim().ToLowerInvariant();
                        settings.AcceptOnlyIfBetter = rw == "true" || rw == "1" || rw == "yes" || rw == "y";
                        break;
                    case "onlypreferred":
                        var op = parts[1].Trim().ToLowerInvariant();
                        settings.OnlyPreferred = op == "true" || op == "1" || op == "yes" || op == "y";
                        break;
                    case "minglass": if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double mig)) settings.MinGlassThickness = mig; break;
                    case "maxglass": if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double mxg)) settings.MaxGlassThickness = mxg; break;
                    case "minair": if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double mia)) settings.MinAirGap = mia; break;
                    case "maxair": if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double mxa)) settings.MaxAirGap = mxa; break;
                    case "minedge": if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double mie)) settings.MinEdgeThickness = mie; break;
                    case "skipsec": if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double sk)) settings.SkipPhaseAfterNoImprovementSeconds = sk; break;
                    case "tol": if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double tol)) settings.Tolerance = tol; break;
                    case "damping": if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double dmp)) settings.InitialDamping = dmp; break;
                    case "broyden":
                        var b = parts[1].Trim().ToLowerInvariant();
                        settings.UseBroydenUpdate = b == "true" || b == "1" || b == "yes" || b == "y";
                        break;
                    case "refresh": if (int.TryParse(parts[1], out int rf)) settings.BroydenRefreshInterval = rf; break;
                    case "catalog":
                    case "catalogs":
                        // Single name or comma-separated multi-name. The
                        // GUI shows a single-select dropdown but the engine
                        // accepts a list — keep both names as aliases so
                        // the user can use whichever form feels natural.
                        settings.GlassCatalogs = new System.Collections.Generic.List<string>(
                            parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                        break;
                    case "skipglass":
                    case "noglass":
                        var sg = parts[1].Trim().ToLowerInvariant();
                        settings.SkipGlassTrials = sg == "true" || sg == "1" || sg == "yes" || sg == "y";
                        break;
                }
            }

            // Point the engine at the filtered-catalogs folder so it can
            // resolve a name like 'S1_GLASS' to the .agf even if the
            // user hasn't pre-loaded it.
            settings.FilteredCatalogSearchPaths = FindFilteredCatalogPaths();

            _cts = new CancellationTokenSource();
            void OnCancelKeyPress(object? s, ConsoleCancelEventArgs e) { e.Cancel = true; _cts.Cancel(); }
            Console.CancelKeyPress += OnCancelKeyPress;

            try
            {
                var service = new SplitElementService(
                    session.EnsureValidSystem(), session.CurrentMeritFunction!,
                    session.EnsureGlassCatalog(), session.ConfigEditor)
                {
                    Settings = settings,
                    OnProgress = p =>
                    {
                        AnsiConsole.MarkupLine($"  [grey]Split {p.SplitIteration + 1}/{p.MaxSplits}[/] {Markup.Escape(p.StatusMessage)}");
                    }
                };

                AnsiConsole.MarkupLine($"[bold]Split Element:[/] max splits={settings.MaxSplits}, glass trials={settings.GlassTrials}");
                var result = service.Execute(_cts.Token);

                foreach (var iter in result.Iterations)
                {
                    AnsiConsole.MarkupLine($"  Split {iter.SplitIndex + 1}: surface {iter.SelectedSurfaceIndex} ({Markup.Escape(iter.SelectedMaterial)})");
                    AnsiConsole.MarkupLine($"    Best glass: {Markup.Escape(iter.BestGlass1)} + {Markup.Escape(iter.BestGlass2)} ({iter.GlassTrialsRun} trials)");
                    AnsiConsole.MarkupLine($"    Merit: {iter.PostGlassTrialMerit:G6}");
                }
                AnsiConsole.MarkupLine($"[green]Merit: {result.InitialMerit:G6} -> {result.FinalMerit:G6}[/]");
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
            }
            finally
            {
                Console.CancelKeyPress -= OnCancelKeyPress;
                _cts = null;
            }
        }

        private void RunSpcSynthesis(Session session, string[] args)
        {
            session.EnsureGlassCatalog();
            session.ValidateGlass();
            if (session.CurrentMeritFunction == null || session.CurrentMeritFunction.Operands.Count == 0)
            {
                AnsiConsole.MarkupLine("[red]No merit function defined. Add operands first.[/]");
                return;
            }

            var settings = new SpcSynthesisSettings();

            for (int i = 1; i < args.Length; i++)
            {
                var parts = args[i].Split('=', 2);
                if (parts.Length != 2) continue;
                string val = parts[1];
                switch (parts[0].ToLowerInvariant())
                {
                    case "elements": if (int.TryParse(val, out int e)) settings.MaxElements = e; break;
                    case "topn": if (int.TryParse(val, out int tn)) settings.TopN = tn; break;
                    case "scanmin": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double sa)) settings.ScanMin = sa; break;
                    case "scanmax": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double sb)) settings.ScanMax = sb; break;
                    case "steps": if (int.TryParse(val, out int st)) settings.ScanSteps = st; break;
                    case "epsilon": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double ep)) settings.Epsilon = ep; break;
                    case "glass": if (int.TryParse(val, out int gt)) settings.GlassTrials = gt; break;
                    case "lm": if (int.TryParse(val, out int lm)) settings.LmIterationsPerTrial = lm; break;
                    case "postlm": if (int.TryParse(val, out int pl)) settings.PostSplitLmIterations = pl; break;
                    case "catalog":
                    case "catalogs":
                        settings.GlassCatalogs = new System.Collections.Generic.List<string>(
                            val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                        break;
                    case "archive": settings.ArchiveIntermediateDesigns = bool.TryParse(val, out bool b) ? b : true; break;
                    case "archivedir": settings.ArchiveDirectory = val; break;
                    case "dop": if (int.TryParse(val, out int d)) settings.MaxDegreeOfParallelism = d; break;
                    case "nullglass": settings.NullElementGlass = val; break;
                    case "runinitlm":
                        var rl = val.Trim().ToLowerInvariant();
                        settings.RunInitialLm = rl == "true" || rl == "1" || rl == "yes" || rl == "y";
                        break;
                    case "initlm": if (int.TryParse(val, out int il)) settings.InitialLmIterations = il; break;
                    case "onlypreferred":
                        var op = val.Trim().ToLowerInvariant();
                        settings.OnlyPreferred = op == "true" || op == "1" || op == "yes" || op == "y";
                        break;
                    case "minglass": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double mig)) settings.MinGlassThickness = mig; break;
                    case "maxglass": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double mxg)) settings.MaxGlassThickness = mxg; break;
                    case "minair": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double mia)) settings.MinAirGap = mia; break;
                    case "maxair": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double mxa)) settings.MaxAirGap = mxa; break;
                    case "minedge": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double mie)) settings.MinEdgeThickness = mie; break;
                    case "constraintweight": if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double cw)) settings.ConstraintWeight = cw; break;
                }
            }

            // Auto-resolve filtered catalogs.
            settings.FilteredCatalogSearchPaths = FindFilteredCatalogPaths();

            if (settings.ArchiveIntermediateDesigns)
            {
                settings.ArchiveWriter = (path, sys, mf) =>
                    LensHH.Core.IO.LhltWriter.Write(sys, path, mf, session.ConfigEditor);
            }

            _cts = new CancellationTokenSource();
            var skipCts = new CancellationTokenSource();
            void OnCancelKeyPress(object? s, ConsoleCancelEventArgs e) { e.Cancel = true; _cts.Cancel(); }
            Console.CancelKeyPress += OnCancelKeyPress;

            try
            {
                var service = new SpcSynthesisService(
                    session.EnsureValidSystem(), session.CurrentMeritFunction!,
                    session.EnsureGlassCatalog(), session.ConfigEditor)
                {
                    Settings = settings,
                    OnProgress = p =>
                    {
                        AnsiConsole.MarkupLine($"  [grey]L{p.Level + 1}/{p.MaxLevels} {p.Phase}[/] {Markup.Escape(p.StatusMessage)}");
                    }
                };

                AnsiConsole.MarkupLine($"[bold]Synthesis by SPC:[/] elements={settings.MaxElements}, topN={settings.TopN}, scan=[{settings.ScanMin},{settings.ScanMax}] x {settings.ScanSteps}, glassTrials={settings.GlassTrials}");
                AnsiConsole.MarkupLine($"  Parallelism: {settings.MaxDegreeOfParallelism?.ToString() ?? Environment.ProcessorCount.ToString()} threads. Archive: {settings.ArchiveIntermediateDesigns}");

                var result = service.Execute(_cts.Token, skipCts.Token);

                AnsiConsole.MarkupLine("");
                AnsiConsole.MarkupLine($"[bold]SPC Complete:[/] {result.ElementsAdded} element(s) added");
                AnsiConsole.MarkupLine($"  Merit: {result.InitialMerit:E6} -> {result.FinalMerit:E6}");
                foreach (var lvl in result.Levels)
                {
                    AnsiConsole.MarkupLine($"  Level {lvl.Level + 1}: {lvl.CandidatesEvaluated} evaluated, {lvl.Survivors} kept. Best: {lvl.BestMerit:E6}");
                    for (int i = 0; i < lvl.TopCandidates.Count && i < 3; i++)
                    {
                        var c = lvl.TopCandidates[i];
                        string sign = c.BranchSign > 0 ? "+" : "-";
                        AnsiConsole.MarkupLine($"    [grey]#{i + 1}[/] surf {c.InsertionSurface}, c={c.SaddleCurvature:F4}, {sign}, {Markup.Escape(c.Glass)}, merit={c.Merit:E6}");
                    }
                }
                if (result.Cancelled) AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
                if (!string.IsNullOrEmpty(result.Message)) AnsiConsole.MarkupLine($"  {Markup.Escape(result.Message)}");
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
            }
            finally
            {
                Console.CancelKeyPress -= OnCancelKeyPress;
                _cts = null;
            }
        }

        private void CancelOptimization()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                AnsiConsole.MarkupLine("[yellow]Cancel requested.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]No optimization running.[/]");
            }
        }

        private void ListVariables(Session session)
        {
            var system = session.EnsureValidSystem();

            var table = new Table();
            table.AddColumn("Surface");
            table.AddColumn("Parameter");
            table.AddColumn("Value");
            table.AddColumn("Min");
            table.AddColumn("Max");

            int count = 0;
            for (int i = 0; i < system.Surfaces.Count; i++)
            {
                var s = system.Surfaces[i];

                if (s.CurvatureVariable)
                {
                    table.AddRow(i.ToString(), "Curvature",
                        s.Curvature.ToString("G6"),
                        s.CurvatureMin?.ToString("G6") ?? "---",
                        s.CurvatureMax?.ToString("G6") ?? "---");
                    count++;
                }
                if (s.ThicknessVariable)
                {
                    table.AddRow(i.ToString(), "Thickness", s.Thickness.ToString("G6"),
                        s.ThicknessMin?.ToString("G6") ?? "---",
                        s.ThicknessMax?.ToString("G6") ?? "---");
                    count++;
                }
                if (s.ConicVariable)
                {
                    table.AddRow(i.ToString(), "Conic", s.Conic.ToString("G6"),
                        s.ConicMin?.ToString("G6") ?? "---",
                        s.ConicMax?.ToString("G6") ?? "---");
                    count++;
                }
                for (int j = 0; j < s.AsphericVariable.Length; j++)
                {
                    if (s.AsphericVariable[j])
                    {
                        table.AddRow(i.ToString(), $"Asph[{j}]",
                            s.AsphericCoefficients[j].ToString("E4"),
                            s.AsphericMin[j]?.ToString("G6") ?? "---",
                            s.AsphericMax[j]?.ToString("G6") ?? "---");
                        count++;
                    }
                }
            }

            // Field variables
            for (int i = 0; i < system.Fields.Count; i++)
            {
                var f = system.Fields[i];
                if (f.Variable)
                {
                    table.AddRow($"F{i}", "Field Y",
                        f.Y.ToString("G6"),
                        f.Min?.ToString("G6") ?? "---",
                        f.Max?.ToString("G6") ?? "---");
                    count++;
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
                            string val = op.Type == Core.Configuration.ConfigOperandType.Glass
                                ? (ce.GetGlass(c, o) ?? "---")
                                : ce.GetValue(c, o).ToString("G6");
                            table.AddRow($"C{c}:Op{o}", $"Config {op.Type}",
                                val,
                                ce.GetMin(c, o)?.ToString("G6") ?? "---",
                                ce.GetMax(c, o)?.ToString("G6") ?? "---");
                            count++;
                        }
                    }
                }
            }

            if (count == 0)
            {
                // 2026-06-01 task #102: `system field-variable` (FieldY var) and
                // `config variable` (multi-config) are both out of scope for
                // LensHH-LT (planned for LensHH-Pro). Dropped from the hint.
                AnsiConsole.MarkupLine("[yellow]No variables defined. Use 'surface variable' to set variables.[/]");
            }
            else
            {
                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine($"Total variables: {count}");
            }
        }

        /// <summary>
        /// Locate catalogs\FilteredGlassCatalogues across install / dev
        /// layouts. Returns an empty array if the folder isn't found
        /// (the engine then falls back to whatever's already in the
        /// GlassCatalogManager).
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

        private static int CountVariables(LensHH.Core.Models.OpticalSystem system,
            LensHH.Core.Configuration.ConfigurationEditor? configEditor = null)
        {
            int count = 0;
            foreach (var s in system.Surfaces)
            {
                if (s.CurvatureVariable) count++;
                if (s.ThicknessVariable) count++;
                if (s.ConicVariable) count++;
                foreach (var v in s.AsphericVariable)
                    if (v) count++;
            }
            foreach (var f in system.Fields)
                if (f.Variable) count++;
            if (configEditor != null)
            {
                for (int c = 0; c < configEditor.ConfigurationCount; c++)
                    for (int o = 0; o < configEditor.OperandCount; o++)
                        if (configEditor.IsVariable(c, o)) count++;
            }
            return count;
        }
    }
}
