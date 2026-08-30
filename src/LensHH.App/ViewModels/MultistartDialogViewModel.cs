using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LensHH.App.Session;
using LensHH.Core.MeritFunction;
using LensHH.Core.Optimization;

namespace LensHH.App.ViewModels;

public partial class MultistartVariableRow : ObservableObject
{
    public string Description { get; }
    public string StartValue { get; }

    [ObservableProperty] private string _currentValue;
    [ObservableProperty] private string _delta;

    public MultistartVariableRow(string description, double startValue)
    {
        Description = description;
        StartValue = startValue.ToString("G8", CultureInfo.InvariantCulture);
        _currentValue = StartValue;
        _delta = "0";
    }

    public void Update(double current, double start)
    {
        CurrentValue = current.ToString("G8", CultureInfo.InvariantCulture);
        Delta = (current - start).ToString("E4", CultureInfo.InvariantCulture);
    }
}

public partial class MultistartGlassRow : ObservableObject
{
    public int SurfaceIndex { get; }
    public string StartGlass { get; }
    [ObservableProperty] private string _currentGlass;

    public MultistartGlassRow(int surfaceIndex, string startGlass)
    {
        SurfaceIndex = surfaceIndex;
        StartGlass = startGlass;
        _currentGlass = startGlass;
    }
}

public partial class MultistartDialogViewModel : ObservableObject
{
    private readonly GuiSession _session;
    private CancellationTokenSource? _cts;
    
    /// <summary>Cancel a running operation when the dialog closes (else worker threads run to completion).</summary>
    public void CancelRun() => _cts?.Cancel();
    private readonly Stopwatch _stopwatch = new();

    // ── Settings ──
    [ObservableProperty] private int _maxTrials = OptimizationDefaults.MultistartTrials;
    // LmIterationsPerTrial: per-trial LM cap. Uses the shared OptimizationDefaults so LM
    // and Multistart stay in lock-step across GUI/CLI/MCP. The per-trial LM must be allowed
    // to converge cleanly — a small cap here is deadly (unconverged trials), which is why
    // the default is intentionally generous.
    [ObservableProperty] private int _lmIterationsPerTrial = OptimizationDefaults.LmIterations;
    // InitialLmIterations: Phase-1 cap. Kept small (200): Phase 1 is a SINGLE-THREADED LM, while the
    // real optimization is Phase 2's parallel per-trial LMs across all cores. A big value here just
    // stalls the run in a long low-CPU phase before the parallel trials start. The seed still gets
    // fully optimized in Phase 2. Raise it only if you specifically want a thorough (single-threaded)
    // pre-polish; skippable entirely via the Skip Init LM box.
    [ObservableProperty] private int _initialLmIterations = 200;
    // Optional one-time Hooke-Jeeves pre-polish of the seed before the Phase-1 LM.
    // 0 = off. Set > 0 (e.g. 50) for constrained designs where a pure-LM seed polish
    // stalls (LM's transformed Jacobian collapses at the bounds; HJ is gradient-free).
    [ObservableProperty] private int _initialHjSteps = 0;
    // For CONSTRAINED designs: run a physical-space HJ pre-step on EVERY trial (like BH's
    // per-hop HJ) so the search keeps improving past where a pure LM stalls at the bounds.
    // Costs time per trial — default off.
    [ObservableProperty] private bool _physicalTrialHj = false;
    /// <summary>When true, MultistartOptimizer skips Phase 1 entirely (the
    /// one-time LM pass on the seed before any randomization). Use when the
    /// seed design is already converged and Phase 1 is pure waste — LM can't
    /// escape the basin Phase 2 is trying to randomize out of. Forces
    /// Settings.InitialLmIterations = 0 in StartOptimization.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InitialLmInputEnabled))]
    private bool _skipInitialLm = false;

    /// <summary>Derived: Init LM textbox enabled when NOT running AND NOT skipping.</summary>
    public bool InitialLmInputEnabled => !IsRunning && !SkipInitialLm;

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(InitialLmInputEnabled));
    // Starting/reset perturbation sigma: the search starts here and returns here
    // on every acceptance; rejection grows sigma toward SigmaCap to escape.
    [ObservableProperty] private double _initialSigma = 0.001;
    [ObservableProperty] private double _sigmaCap = 0.1;
    [ObservableProperty] private bool _enableMetropolis = true;
    // On a glass swap, rescale the element's curvatures by (n_old-1)/(n_new-1) to
    // preserve its optical power — keeps swaps feasible. Default ON since 1.0.121
    // (validated); no-op when glass substitution is off.
    [ObservableProperty] private bool _rescaleOnGlassSwap = true;

    // Convergence levers (2026-08-17, default ON) — reach deeper basins.
    // #7 reduced-dim: perturb a random subset of variables on a fraction of trials.
    // #6 basin memory: archive visited minima and restart far from them (with fresh
    //    glasses) when the walk stalls at the sigma cap.
    [ObservableProperty] private bool _reducedDimPerturbation = true;
    [ObservableProperty] private bool _basinMemoryRestart = true;
    // RNG seed — exposed so a run is reproducible for studying settings. Same seed +
    // same settings => bit-identical run; change it (1, 2, 3, …) for an independent run.
    [ObservableProperty] private int _seed = 1;
    // Default lowered from 50 → 10 on 2026-05-31. HJ pre-step now also
    // runs only on glass-swap trials (MultistartSettings.HjOnGlassSwapOnly
    // default = true). Previously HJ ran every trial × 50 outer iters ×
    // (2N+1) inner evals, dominating wall-clock on Tanabe-class designs.
    [ObservableProperty] private int _hjStepsPerTrial = 10;
    [ObservableProperty] private int _glassSwapLmMultiplier = 4;
    [ObservableProperty] private bool _constrainedOnly = false;
    [ObservableProperty] private double _glassSubstitutionProbability = 50; // display as %
    [ObservableProperty] private bool _useBroydenUpdate = true;

    /// <summary>Step-method choices for the dropdown. ToString is the label, so the ComboBox
    /// needs no template or converter.</summary>
    public sealed record StepOption(string Label, StepMethod Value)
    {
        public override string ToString() => Label;
    }

    public static IReadOnlyList<StepOption> StepOptions { get; } = new[]
    {
        new StepOption("LM (Marquardt damping)", StepMethod.LevenbergMarquardt),
        new StepOption("PSD II",  StepMethod.PsdII),
        new StepOption("PSD III", StepMethod.PsdIII),
    };

    // Seeded to LM so the dropdown never shows blank and the default is explicit.
    [ObservableProperty] private StepOption _selectedStep = StepOptions[0];

    /// <summary>
    /// Changing the step method resets the Broyden checkbox to that method's default, so the
    /// rule is visible in the UI instead of being applied invisibly at run time. PSD estimates
    /// curvature by differencing two successive FRESH Jacobians, which a rank-1 Broyden update
    /// is not. The user can re-tick the box afterwards and that choice sticks until the step
    /// method changes again.
    /// </summary>
    partial void OnSelectedStepChanged(StepOption value)
    {
        if (value != null) UseBroydenUpdate = value.Value == StepMethod.LevenbergMarquardt;
    }

    /// <summary>LM Marquardt damping starting value. 1e-3 (default) is the
    /// across-the-board default — robust on aspheric mixes and well-
    /// conditioned problems alike. Drop to 1e-6 for gauss-newton-like
    /// behavior on very smooth, well-scaled designs; raise to 1e-2 if the
    /// optimizer keeps rejecting steps early.</summary>
    [ObservableProperty] private double _initialDamping = 1e-3;

    // ── Advanced — engine + derivative selection (was orange DEV banner in
    //    ≤1.0.114; moved into a collapsed Advanced expander for 1.0.115).
    //    Index 0 = C# (CSharp), 1 = C++ (Native). Default flipped to C++
    //    Native on 1.0.115 — the bedrock-validated path that the GPU pre-
    //    screen layers on top of. C# remains available via the dropdown.
    [ObservableProperty] private int _engineModeIndex = 1;
    //    Index 0 = Finite Difference, 1 = Analytic (forward-mode Dual<W> AD).
    //    Only effective when EngineModeIndex == 1. Default flipped to Analytic
    //    on 1.0.115 (faster Jacobian, bit-equal on 134/134 bedrock tests).
    [ObservableProperty] private int _derivativeModeIndex = 1;
    public IReadOnlyList<string> EngineModeOptions { get; } =
        new[] { "C# (CSharp)", "C++ (Native)" };
    public IReadOnlyList<string> DerivativeModeOptions { get; } =
        new[] { "Finite Difference", "Analytic" };

    // ── GPU pre-screen tuning (advanced; the on/off lives in Preferences ▸ GPU) ──
    // Difference gate: only feed the sieve designs STRUCTURALLY different from the
    // running best (a glass swap or a curvature moved by more than this percent) —
    // stops the value-only sieve collapsing into a pure refiner. Population multiplier:
    // candidates sieved per batch = this × the device-fill count. Kept at defaults;
    // whether the pre-screen runs at all is the global AppPreferences.GpuPreScreen.
    [ObservableProperty] private double _gpuMinCurvatureChangePercent = 2.0;
    [ObservableProperty] private double _gpuPopulationMultiplier = 1.0;

    // ── Status ──
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isComplete;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _meritText = "";
    [ObservableProperty] private string _engineText = "";
    [ObservableProperty] private string _elapsedText = "0.0 s";
    [ObservableProperty] private string _trialText = "";
    [ObservableProperty] private string _initialMeritText = "";
    [ObservableProperty] private string _postLmMeritText = "";
    [ObservableProperty] private string _bestMeritText = "";
    [ObservableProperty] private string _currentMeritText = "";
    [ObservableProperty] private int _trialsAccepted;

    // ── Improvement log (written to a text file, NOT shown in the GUI) ── one line per new
    // best (Trial #, RMSE, wall-clock time), to compare runs when tuning default settings.
    private double _bestEverLogged = double.MaxValue;
    private string? _logFilePath;

    // ── Tables ──
    public ObservableCollection<MultistartVariableRow> VariableRows { get; } = new();
    public ObservableCollection<MultistartGlassRow> GlassRows { get; } = new();

    // ── Help strip ──
    private const string DefaultHelpText = "Hover over a setting for a description.";
    [ObservableProperty] private string _helpText = DefaultHelpText;

    public void SetHelp(string? text)
        => HelpText = string.IsNullOrEmpty(text) ? DefaultHelpText : text!;

    public bool Accepted { get; set; }

    public MultistartDialogViewModel(GuiSession session)
    {
        _session = session;
    }


    /// <summary>
    /// Inspect the current design's optimization variables. Returns a
    /// human-readable description of any variable types the GPU pre-screen
    /// kernel can't accept (aspheric coefficients, FieldY, ConfigValue), or
    /// null if every variable is curvature/thickness/conic-compatible.
    /// </summary>
    /// <remarks>
    /// Variables are enumerated via a throwaway <see cref="LocalOptimizer"/>
    /// — the same path MultistartOptimizer takes at Optimize() entry. If
    /// enumeration throws (rare — usually means the merit function is in a
    /// bad state) we return null so the toggle stays usable; the engine
    /// will report any real eligibility blocker in result.Message after the
    /// run.
    /// </remarks>
    private string? CheckGpuVariableCompatibility()
    {
        try
        {
            var probe = new LensHH.Core.Optimization.LocalOptimizer(
                _session.System, _session.MeritFunction,
                _session.GlassCatalog);
            probe.CollectVariables();

            bool hasAspheric = false, hasField = false, hasConfig = false;
            foreach (var v in probe.Variables)
            {
                switch (v.Type)
                {
                    case LensHH.Core.Optimization.VariableType.AsphericCoefficient:
                        hasAspheric = true; break;
                    case LensHH.Core.Optimization.VariableType.FieldY:
                        hasField = true; break;
                    case LensHH.Core.Optimization.VariableType.ConfigValue:
                        hasConfig = true; break;
                }
            }

            if (!hasAspheric && !hasField && !hasConfig) return null;

            var parts = new List<string>();
            if (hasAspheric) parts.Add("aspheric");
            if (hasField)    parts.Add("Field-Y");
            if (hasConfig)   parts.Add("ConfigValue");
            return string.Join(" + ", parts) + " variable(s)";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Text for the Preview button: what THIS dialog's current settings will actually run.
    /// Delegates to ComputePathPlanner — the same code the optimizer uses to pick its path —
    /// so the preview cannot disagree with the run.
    /// </summary>
    public string BuildComputePathPreview()
        => LensHH.App.Views.ComputePathPreview.Build(
            _session,
            "Multistart (" + (SelectedStep?.Label ?? "LM") + ")",
            (EngineModeIndex == 1) ? EngineMode.Native : EngineMode.CSharp,
            (DerivativeModeIndex == 1)
                ? LensHH.Core.NativeInterop.MeritDerivativeMode.Analytic
                : LensHH.Core.NativeInterop.MeritDerivativeMode.FiniteDifference,
            UseBroydenUpdate,
            AppPreferences.GpuImageQuality,
            "GPU candidate pre-screen (Preferences > GPU): "
          + (AppPreferences.GpuPreScreen ? "ON" : "off")
          + ". This is a separate stage from the merit engine above — it ranks perturbed "
          + "candidates on the device; the local optimizer that follows still uses the path above.");

    [RelayCommand]
    public async Task StartOptimization()
    {
        IsRunning = true;
        IsComplete = false;
        StatusText = "Starting initial optimization...";
        VariableRows.Clear();
        GlassRows.Clear();
        LensHH.Core.NativeInterop.GpuActivity.Reset();   // reset the live GPU-activity chip counters

        _cts = new CancellationTokenSource();
        _stopwatch.Restart();

        var timer = new System.Timers.Timer(200);
        timer.Elapsed += (_, _) =>
            Dispatcher.UIThread.Post(() => ElapsedText = $"{_stopwatch.Elapsed.TotalSeconds:F1} s");
        timer.Start();

        try
        {
            var optimizer = AppExtensions.CreateMultistartOptimizer(
                _session.System, _session.MeritFunction, _session.GlassCatalog);
            optimizer.Settings = new MultistartSettings
                {
                    MaxTrials = MaxTrials,
                    LmIterationsPerTrial = LmIterationsPerTrial,
                    InitialLmIterations = SkipInitialLm ? 0 : InitialLmIterations,
                    InitialHjSteps = InitialHjSteps,
                    PhysicalTrialHj = PhysicalTrialHj,
                    InitialSigma = InitialSigma,
                    SigmaCap = SigmaCap,
                    EnableMetropolis = EnableMetropolis,
                    HjStepsPerTrial = HjStepsPerTrial,
                    GlassSwapLmMultiplier = GlassSwapLmMultiplier,
                    ConstrainedOnly = ConstrainedOnly,
                    GlassSubstitutionProbability = GlassSubstitutionProbability / 100.0,
                    UseBroydenUpdate = UseBroydenUpdate,
                    Step = SelectedStep?.Value ?? StepMethod.LevenbergMarquardt,
                    InitialDamping = InitialDamping,
                    // Pre-screen on/off comes from the global Preferences ▸ GPU setting.
                    UseGpuPreScreen = AppPreferences.GpuPreScreen,
                    // 1.0.128: difference gate threshold (only effective with the GPU sieve on).
                    GpuPreScreenMinCurvatureChangePercent = GpuMinCurvatureChangePercent,
                    // 1.0.128: evaluate this × the GPU device-fill candidates per batch.
                    GpuPreScreenFill = GpuPopulationMultiplier,
                    // Experimental (1.0.120): rescale an element's curvatures by
                    // (n_old-1)/(n_new-1) on a glass swap so its power is preserved.
                    RescaleCurvatureOnGlassSwap = RescaleOnGlassSwap,
                    ReducedDimPerturbation = ReducedDimPerturbation,
                    BasinMemoryRestart = BasinMemoryRestart,
                    Seed = Seed,
                };
            // Phase 10a — DEV engine selection from the dialog.
            optimizer.EngineMode = (EngineModeIndex == 1) ? EngineMode.Native : EngineMode.CSharp;
            optimizer.NativeDerivativeMode = (DerivativeModeIndex == 1)
                ? LensHH.Core.NativeInterop.MeritDerivativeMode.Analytic
                : LensHH.Core.NativeInterop.MeritDerivativeMode.FiniteDifference;
            // Global GPU image-quality setting (Preferences ▸ GPU). No-op without a CUDA device.
            optimizer.UseGpuGridTrace = AppPreferences.GpuImageQuality;
            optimizer.FilteredCatalogSearchPaths = GlassSubstitutionViewModel.FindFilteredCatalogFolder() is string dir
                ? new[] { dir } : Array.Empty<string>();

            // Get initial merit for display. EnsureSolved first so this matches the
            // Evaluate-merit button exactly (both read one canonical accurate-SD +
            // vignetting state; neither depends on what a prior in-place fast re-solve
            // left behind). Route through the factory so PRO's config-aware evaluator
            // sums ALL configurations — a plain 2-arg MeritFunctionEvaluator is
            // config-blind and collapses a multi-config merit to a single config.
            _session.EnsureSolved();
            var evaluator = AppExtensions.CreateMeritEvaluator(
                _session.System, _session.GlassCatalog);
            double initialMerit = evaluator.Evaluate(_session.MeritFunction);
            InitialMeritText = initialMerit.ToString("E6");
            BestMeritText = InitialMeritText;
            MeritText = $"Merit: {initialMerit:E6}";

            // Open a per-run improvement log file (not shown in the GUI). Best-effort:
            // a logging failure must never abort the optimization.
            _bestEverLogged = double.MaxValue;
            _logFilePath = null;
            try
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SynapseOptics", "LensHH-LT", "logs");
                Directory.CreateDirectory(logDir);
                _logFilePath = Path.Combine(logDir, $"multistart-{DateTime.Now:yyyyMMdd-HHmmss}.log");
                File.WriteAllText(_logFilePath,
                    $"# Multistart run {DateTime.Now:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine +
                    $"# maxTrials={MaxTrials} lmPerTrial={LmIterationsPerTrial} hjSteps={HjStepsPerTrial} initialRMSE={initialMerit:E6}" + Environment.NewLine);
            }
            catch { _logFilePath = null; }

            bool variableRowsPopulated = false;
            bool optimizationDone = false;

            optimizer.OnProgress = progress =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (optimizationDone) return;

                    // Per-batch parallel-timing diagnostic line (eff% = core utilization;
                    // straggler = slowest/mean trial; serial = between-batch walk update).
                    if (progress.IsBatchTiming)
                    {
                        double straggler = progress.TrialMaxMs / Math.Max(0.001, progress.TrialMeanMs);
                        WriteLogLine(
                            $"  batch {progress.BatchIndex}  wall={progress.BatchWallMs:F0}ms  " +
                            $"trials={progress.BatchTrials} thr={progress.BatchThreads}  " +
                            $"trial[mean/max]={progress.TrialMeanMs:F0}/{progress.TrialMaxMs:F0}ms  " +
                            $"eff={progress.ParallelEfficiencyPct:F0}%  straggler={straggler:F1}x  " +
                            $"serial={progress.SerialMs:F1}ms");
                        return;
                    }

                    // Populate tables on first callback
                    if (!variableRowsPopulated && optimizer.Variables.Count > 0)
                    {
                        variableRowsPopulated = true;
                        for (int i = 0; i < optimizer.Variables.Count; i++)
                            VariableRows.Add(new MultistartVariableRow(
                                optimizer.Variables[i].Description,
                                optimizer.StartingValues[i]));

                        foreach (var kvp in optimizer.StartingGlasses)
                            GlassRows.Add(new MultistartGlassRow(kvp.Key, kvp.Value));
                    }

                    BestMeritText = progress.BestMerit.ToString("E6");
                    if (progress.CurrentMerit > 0)
                        CurrentMeritText = progress.CurrentMerit.ToString("E6");

                    // Track improvements across both phases; emit a log line only for
                    // trial-phase gains (Trial #, New RMSE, wall-clock time).
                    bool improved = progress.BestMerit < _bestEverLogged - Math.Abs(_bestEverLogged) * 1e-9 - 1e-15;
                    if (improved) _bestEverLogged = progress.BestMerit;

                    if (progress.IsInitialLm)
                    {
                        StatusText = $"Initial LM — iteration {progress.InitialLmIteration + 1}";
                        MeritText = $"Merit: {progress.BestMerit:E6} (initial LM)";
                    }
                    else
                    {
                        TrialsAccepted = progress.TrialsAccepted;
                        StatusText = $"Trial {progress.Trial}/{progress.MaxTrials} — {progress.TrialsAccepted} accepted — σ {progress.Sigma:G3}";
                        TrailText = $"Trial {progress.Trial}/{progress.MaxTrials}";
                        MeritText = $"Best: {progress.BestMerit:E6}  ·  Current: {progress.CurrentMerit:E6}  ({progress.TrialsAccepted} accepted)";
                        if (improved)
                            WriteLogLine($"trial {progress.Trial}   RMSE={progress.BestMerit:E6}   {DateTime.Now:HH:mm:ss}");
                    }

                    // Update variable values
                    if (progress.CurrentValues.Length == VariableRows.Count)
                    {
                        for (int i = 0; i < VariableRows.Count; i++)
                            VariableRows[i].Update(progress.CurrentValues[i], optimizer.StartingValues[i]);
                    }

                    // Update glass assignments
                    foreach (var row in GlassRows)
                    {
                        if (progress.CurrentGlasses.TryGetValue(row.SurfaceIndex, out var glass))
                            row.CurrentGlass = glass;
                    }
                });
            };

            var result = await Task.Run(() => optimizer.Optimize(_cts.Token));

            // Drain pending UI posts before setting final values
            await Dispatcher.UIThread.InvokeAsync(() => { optimizationDone = true; });

            _stopwatch.Stop();
            timer.Stop();
            ElapsedText = $"{_stopwatch.Elapsed.TotalSeconds:F1} s";

            // Re-evaluate with a fresh evaluator to match what Evaluate button shows.
            // Route through the factory so PRO stays config-aware (a plain 2-arg
            // evaluator collapses a multi-config merit to a single configuration).
            LensHH.Core.Analysis.SemiDiameterSolver.Solve(_session.System, _session.GlassCatalog, accurateApertures: true);
            var freshEval = AppExtensions.CreateMeritEvaluator(
                _session.System, _session.GlassCatalog);
            double finalMerit = freshEval.Evaluate(_session.MeritFunction);

            InitialMeritText = result.InitialMerit.ToString("E6");
            PostLmMeritText = result.PostInitialLmMerit.ToString("E6");
            BestMeritText = finalMerit.ToString("E6");
            TrialsAccepted = result.TrialsAccepted;

            string status = result.Cancelled ? "Cancelled" : "Completed";
            WriteLogLine($"# {status} — {result.TrialsAccepted}/{result.TrialsRun} accepted — " +
                         $"final RMSE={finalMerit:E6} — {_stopwatch.Elapsed.TotalSeconds:F1}s");
            StatusText = $"{status} — {result.TrialsAccepted}/{result.TrialsRun} trials accepted" +
                         (_logFilePath != null ? $"  ·  log: {_logFilePath}" : "");
            MeritText = $"Merit: {result.InitialMerit:E4} → {result.PostInitialLmMerit:E4} → {finalMerit:E4}";
            // Transparency: show the engine/module that actually ran (incl. any fallback).
            EngineText = $"Engine: {result.ComputePathDescription}";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            timer.Stop();
        }

        IsRunning = false;
        IsComplete = true;
    }

    [RelayCommand]
    public void StopOptimization()
    {
        _cts?.Cancel();
        StatusText = "Stopping...";
    }

    // Workaround: CommunityToolkit generates TrailText from _trialText
    private string TrailText { set => TrialText = value; }

    /// <summary>Append one line to the per-run improvement log file. Best-effort: never throws
    /// (a logging failure must not disturb the optimization). No-op if the file couldn't be opened.</summary>
    private void WriteLogLine(string line)
    {
        if (_logFilePath == null) return;
        try { File.AppendAllText(_logFilePath, line + Environment.NewLine); }
        catch { /* ignore — logging is best-effort */ }
    }
}
