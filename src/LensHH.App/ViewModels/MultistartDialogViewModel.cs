using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
    [ObservableProperty] private int _maxTrials = 2000;
    // LmIterationsPerTrial: per-trial LM cap. Restored to 4000 — the previous
    // default — at user request 2026-05-31. The per-trial LM should be allowed
    // to converge cleanly; user explicitly objected to a small cap here.
    [ObservableProperty] private int _lmIterationsPerTrial = 4000;
    // InitialLmIterations: Phase-1 cap. Dropped to 200 (engine default) per
    // user agreement — Phase 1 LM mostly polishes an already-converged seed
    // and 4000 here was overkill. Skippable entirely via the Skip Init LM box.
    [ObservableProperty] private int _initialLmIterations = 200;
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
    // Default lowered from 50 → 10 on 2026-05-31. HJ pre-step now also
    // runs only on glass-swap trials (MultistartSettings.HjOnGlassSwapOnly
    // default = true). Previously HJ ran every trial × 50 outer iters ×
    // (2N+1) inner evals, dominating wall-clock on Tanabe-class designs.
    [ObservableProperty] private int _hjStepsPerTrial = 10;
    [ObservableProperty] private int _glassSwapLmMultiplier = 4;
    [ObservableProperty] private bool _constrainedOnly = false;
    [ObservableProperty] private double _glassSubstitutionProbability = 50; // display as %
    [ObservableProperty] private bool _useBroydenUpdate = true;
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

    // ── GPU pre-screen (Beta, G2 / 1.0.115) ──
    // Bound to the checkbox in the "Hardware acceleration" strip at top.
    // 1.0.115 ships this as a detection-only Beta: the toggle verifies the
    // user's GPU is reachable and the dispatcher is wired, but the Multistart
    // Phase-2 inner-loop hook lands in 1.0.116. When the kernel inner-loop
    // integration arrives, this property already feeds MultistartSettings.
    // UseGpuPreScreen — no UI rewiring needed.
    [ObservableProperty] private bool _useGpuPreScreen;
    [ObservableProperty] private string _gpuStatusText = "Detecting GPU...";
    [ObservableProperty] private bool _isGpuToggleEnabled;

    // GPU difference gate (1.0.128): only feed the GPU sieve designs that are
    // STRUCTURALLY different from the running best — a glass change (|Δn_d|>0.001) or a
    // refractive surface whose curvature moved by more than this percent. Stops the
    // value-only sieve from collapsing into a pure refiner. 0 disables the gate.
    [ObservableProperty] private double _gpuMinCurvatureChangePercent = 2.0;

    // GPU population multiplier (1.0.128): candidates sieved per batch = this × the GPU's
    // device-fill count. The GPU is otherwise idle, so a bigger pool is a near-free way to give
    // the value-only sieve more shots at a good (post-LM) design. Scales GPU work/scratch ~linearly.
    [ObservableProperty] private double _gpuPopulationMultiplier = 1.0;

    // ── Status ──
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isComplete;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _meritText = "";
    [ObservableProperty] private string _elapsedText = "0.0 s";
    [ObservableProperty] private string _trialText = "";
    [ObservableProperty] private string _initialMeritText = "";
    [ObservableProperty] private string _postLmMeritText = "";
    [ObservableProperty] private string _bestMeritText = "";
    [ObservableProperty] private string _currentMeritText = "";
    [ObservableProperty] private int _trialsAccepted;

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
        DetectGpu();
    }

    // ── G2 / 1.0.115: GPU detection ──
    // Runs once at dialog construction. Sets IsGpuToggleEnabled so the
    // checkbox is interactable only when a CUDA device is reachable, and
    // populates GpuStatusText with what the user needs to know:
    //   - GPU detected → name + 1.7-1.9× speedup expectation + Beta note
    //   - No GPU       → reason the toggle is disabled
    // The first call lazy-loads the CUDA driver inside the native DLL; ~50 ms.
    private void DetectGpu()
    {
        try
        {
            bool available = LensHH.Core.NativeInterop.GpuPreScreener.IsAvailable;
            if (available)
            {
                // Check whether the CURRENT design has variable types the GPU
                // kernel can't accept. If so, surface this in the status text
                // upfront and disable the toggle — better than letting the
                // user enable it then quietly running CPU and only discovering
                // via the post-run telemetry.
                string? blocker = CheckGpuVariableCompatibility();
                if (blocker != null)
                {
                    IsGpuToggleEnabled = false;
                    UseGpuPreScreen = false;
                    GpuStatusText =
                        $"CUDA device detected, but this design has {blocker}. " +
                        "GPU pre-screen requires only curvature / thickness / conic " +
                        "variables — falling back to CPU. Remove the unsupported " +
                        "variable types to enable.";
                }
                else
                {
                    IsGpuToggleEnabled = true;
                    UseGpuPreScreen = false; // Off by default — user opts in.
                    GpuStatusText =
                        "CUDA device detected. When enabled, each Phase-2 batch generates ~16× more " +
                        "candidate designs than CPU cores, evaluates all on GPU in one launch " +
                        "(~60 µs/design on 4060), and runs HJ-LM only on the top survivors. " +
                        "Continuous variables AND glass-swap candidates are sieved together.";
                }
            }
            else
            {
                IsGpuToggleEnabled = false;
                UseGpuPreScreen = false;
                GpuStatusText =
                    "No compatible NVIDIA GPU detected. Pre-screen disabled. " +
                    "(Requires CUDA-capable card and recent NVIDIA driver.)";
            }
        }
        catch (System.Exception ex)
        {
            IsGpuToggleEnabled = false;
            UseGpuPreScreen = false;
            GpuStatusText = $"GPU detection failed: {ex.Message.Replace("\n", " ")}. Pre-screen disabled.";
        }
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
                _session.GlassCatalog, _session.ConfigEditor);
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

    [RelayCommand]
    public async Task StartOptimization()
    {
        IsRunning = true;
        IsComplete = false;
        StatusText = "Starting initial optimization...";
        VariableRows.Clear();
        GlassRows.Clear();

        _cts = new CancellationTokenSource();
        _stopwatch.Restart();

        var timer = new System.Timers.Timer(200);
        timer.Elapsed += (_, _) =>
            Dispatcher.UIThread.Post(() => ElapsedText = $"{_stopwatch.Elapsed.TotalSeconds:F1} s");
        timer.Start();

        try
        {
            var optimizer = new MultistartOptimizer(
                _session.System, _session.MeritFunction,
                _session.GlassCatalog, configEditor: _session.ConfigEditor)
            {
                Settings = new MultistartSettings
                {
                    MaxTrials = MaxTrials,
                    LmIterationsPerTrial = LmIterationsPerTrial,
                    InitialLmIterations = SkipInitialLm ? 0 : InitialLmIterations,
                    InitialSigma = InitialSigma,
                    SigmaCap = SigmaCap,
                    EnableMetropolis = EnableMetropolis,
                    HjStepsPerTrial = HjStepsPerTrial,
                    GlassSwapLmMultiplier = GlassSwapLmMultiplier,
                    ConstrainedOnly = ConstrainedOnly,
                    GlassSubstitutionProbability = GlassSubstitutionProbability / 100.0,
                    UseBroydenUpdate = UseBroydenUpdate,
                    InitialDamping = InitialDamping,
                    // G2 / 1.0.115 Beta: plumb the dialog checkbox into the
                    // optimizer settings. 1.0.115 ships infrastructure only —
                    // MultistartOptimizer Phase-2 hook lands in 1.0.116, at
                    // which point this flag will start gating the GPU sieve.
                    UseGpuPreScreen = UseGpuPreScreen,
                    // 1.0.128: difference gate threshold (only effective with the GPU sieve on).
                    GpuPreScreenMinCurvatureChangePercent = GpuMinCurvatureChangePercent,
                    // 1.0.128: evaluate this × the GPU device-fill candidates per batch.
                    GpuPreScreenFill = GpuPopulationMultiplier,
                    // Experimental (1.0.120): rescale an element's curvatures by
                    // (n_old-1)/(n_new-1) on a glass swap so its power is preserved.
                    RescaleCurvatureOnGlassSwap = RescaleOnGlassSwap,
                },
                // Phase 10a — DEV engine selection from the dialog.
                EngineMode = (EngineModeIndex == 1) ? EngineMode.Native : EngineMode.CSharp,
                NativeDerivativeMode = (DerivativeModeIndex == 1)
                    ? LensHH.Core.NativeInterop.MeritDerivativeMode.Analytic
                    : LensHH.Core.NativeInterop.MeritDerivativeMode.FiniteDifference,
                FilteredCatalogSearchPaths = GlassSubstitutionViewModel.FindFilteredCatalogFolder() is string dir
                    ? new[] { dir } : Array.Empty<string>()
            };

            // Get initial merit for display
            var evaluator = new MeritFunctionEvaluator(
                _session.System, _session.GlassCatalog, configEditor: _session.ConfigEditor);
            double initialMerit = evaluator.Evaluate(_session.MeritFunction);
            InitialMeritText = initialMerit.ToString("E6");
            BestMeritText = InitialMeritText;
            MeritText = $"Merit: {initialMerit:E6}";

            bool variableRowsPopulated = false;
            bool optimizationDone = false;

            optimizer.OnProgress = progress =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (optimizationDone) return;

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

            // Re-evaluate with a fresh evaluator to match what Evaluate button shows
            LensHH.Core.Analysis.SemiDiameterSolver.Solve(_session.System, _session.GlassCatalog);
            var freshEval = new MeritFunctionEvaluator(
                _session.System, _session.GlassCatalog, configEditor: _session.ConfigEditor);
            double finalMerit = freshEval.Evaluate(_session.MeritFunction);

            InitialMeritText = result.InitialMerit.ToString("E6");
            PostLmMeritText = result.PostInitialLmMerit.ToString("E6");
            BestMeritText = finalMerit.ToString("E6");
            TrialsAccepted = result.TrialsAccepted;

            string status = result.Cancelled ? "Cancelled" : "Completed";
            StatusText = $"{status} — {result.TrialsAccepted}/{result.TrialsRun} trials accepted";
            MeritText = $"Merit: {result.InitialMerit:E4} → {result.PostInitialLmMerit:E4} → {finalMerit:E4}";
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
}
