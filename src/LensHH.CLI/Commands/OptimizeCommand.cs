using System;
using System.Globalization;
using System.Threading;
using LensHH.Core.Configuration;
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
  [green]optimize multistart [[trials=N]] [[lm=N]] [[initlm=N]] [[sigma=V]] [[cap=V]] [[growth=V]] [[glass=V]] [[constrained]] [[tol=V]] [[damping=V]] [[broyden=true|false]] [[refresh=N]][/]  Multistart optimization
  [green]optimize basin [[hops=N]] [[lm=N]] [[hj=N]] [[sigma=V]] [[hjstep=V]] [[hjmin=V]] [[tol=V]] [[damping=V]] [[broyden=true|false]] [[constrained]] [[glasssub=true|false]] [[onlypreferred=true|false]] [[catalog=NAME]] [[seed=N]][/]  Basin hopping (Hooke-Jeeves + LM with random kicks between hops)
  [green]optimize split [[splits=N]] [[trials=N]] [[lm=N]] [[postlm=N]] [[preglass=N]] [[postglass=N]] [[sigma=V]] [[constrained]] [[onlypreferred=true|false]] [[minglass=V]] [[maxglass=V]] [[minair=V]] [[maxair=V]] [[minedge=V]] [[skipsec=V]] [[tol=V]] [[damping=V]] [[broyden=true|false]] [[refresh=N]] [[catalog=NAME]] [[noglass]][/]  Split element synthesis. catalog: AGF name (e.g. catalog=S1_GLASS); resolved against catalogs\FilteredGlassCatalogues. noglass: skip the glass-trials phase entirely (split + LM polish only).
  [green]optimize spc [[elements=N]] [[topn=N]] [[scanmin=V]] [[scanmax=V]] [[steps=N]] [[epsilon=V]] [[glass=N]] [[lm=N]] [[postlm=N]] [[catalog=NAME]] [[archive=true|false]] [[archivedir=PATH]] [[dop=N]] [[nullglass=NAME]] [[runinitlm=true|false]] [[initlm=N]] [[onlypreferred=true|false]] [[minglass=V]] [[maxglass=V]] [[minair=V]] [[maxair=V]] [[minedge=V]] [[constraintweight=V]][/]  Synthesis by SPC. catalog is mandatory (single AGF name or comma-separated list).
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
                case "split":
                    RunSplitElement(session, args);
                    break;
                case "spc":
                    RunSpcSynthesis(session, args);
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
            AnsiConsole.MarkupLine($"  Sigma: start {settings.InitialSigma:G3}, triangle floor {settings.SigmaMin:G3} ↔ cap {settings.SigmaCap:G3} (×{settings.SigmaGrowth:G3}), Metropolis: {(settings.EnableMetropolis ? "on" : "off")}");
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

        private void RunBasinHopping(Session session, string[] args)
        {
            var system = session.EnsureValidSystem();
            var mf = session.EnsureMeritFunction();
            var glassMgr = session.EnsureGlassCatalog();
            session.ValidateGlass();

            var settings = new BasinHoppingSettings();

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
                }
            }

            _cts = new CancellationTokenSource();
            Console.CancelKeyPress += OnCancelKeyPress;

            var optimizer = new BasinHoppingOptimizer(system, mf, glassMgr, session.ConfigEditor)
            {
                Settings = settings,
                FilteredCatalogSearchPaths = FindFilteredCatalogPaths(),
            };

            optimizer.OnProgress = p =>
            {
                if (p.Hop > 0)
                    AnsiConsole.MarkupLine(
                        $"  Hop {p.Hop}/{p.MaxHops}: {Markup.Escape(p.Phase),-15} merit={p.CurrentMerit:E6} best={p.BestMerit:E6} ({p.Accepted} acc / {p.Rejected} rej{(settings.GlassSubstitution ? $", {p.GlassSwaps} swaps" : "")})");
            };

            AnsiConsole.MarkupLine("[bold]Starting basin-hopping optimization[/]");
            AnsiConsole.MarkupLine($"  Hops: {settings.MaxHops}, LM/hop: {settings.LmIterationsPerHop}, HJ/hop: {settings.HjStepsPerHop}");
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
                AnsiConsole.MarkupLine($"  Hops: {result.Hops}, Accepted: {result.Accepted}, Rejected: {result.Rejected}, Glass Swaps: {result.GlassSwaps}");
                if (!string.IsNullOrEmpty(result.Message))
                    AnsiConsole.MarkupLine($"  {Markup.Escape(result.Message)}");
                if (result.Cancelled)
                    AnsiConsole.MarkupLine("[yellow]Cancelled by user.[/]");
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
