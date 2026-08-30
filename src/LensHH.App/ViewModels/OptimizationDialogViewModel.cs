using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LensHH.App.Session;
using LensHH.Core.MeritFunction;
using LensHH.Core.Optimization;

namespace LensHH.App.ViewModels;

/// <summary>Row in the variable comparison table shown during optimization.</summary>
public partial class VariableProgressRow : ObservableObject
{
    public string Description { get; }
    public string StartValue { get; }

    [ObservableProperty] private string _currentValue;
    [ObservableProperty] private string _delta;

    public VariableProgressRow(string description, double startValue)
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

/// <summary>ViewModel for the local optimization modal dialog.</summary>
public partial class OptimizationDialogViewModel : ObservableObject
{
    private readonly GuiSession _session;
    private CancellationTokenSource? _cts;
    
    /// <summary>Cancel a running operation when the dialog closes (else worker threads run to completion).</summary>
    public void CancelRun() => _cts?.Cancel();
    private readonly Stopwatch _stopwatch = new();

    // ── Settings ──
    [ObservableProperty] private int _maxIterations = OptimizationDefaults.LmIterations;
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


    // Local Optimization never uses the GPU — it runs ParallelEvaluation=true (all
    // CPU cores on the operands), which serves a single LM chain well. The GPU is
    // reserved for the multi-trial optimizers (Preferences ▸ GPU acceleration).

    /// <summary>
    /// Levenberg–Marquardt initial damping. 1e-3 (default) is the across-
    /// the-board default — robust on aspheric mixes and well-conditioned
    /// problems alike. Drop to 1e-6 for gauss-newton-like behavior on very
    /// smooth, well-scaled designs; raise to 1e-2 if the optimizer keeps
    /// rejecting steps early. Same control as the Multistart dialog.
    /// </summary>
    [ObservableProperty] private double _initialDamping = 1e-3;

    // ── Status ──
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isComplete;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _meritText = "";
    [ObservableProperty] private string _engineText = "";
    [ObservableProperty] private string _elapsedText = "0.0 s";
    [ObservableProperty] private int _iterationCount;
    [ObservableProperty] private string _initialMeritText = "";
    [ObservableProperty] private string _bestMeritText = "";

    // ── Variable table ──
    public ObservableCollection<VariableProgressRow> VariableRows { get; } = new();

    /// <summary>Set to true when the user clicks OK to accept results.</summary>
    public bool Accepted { get; set; }

    public OptimizationDialogViewModel(GuiSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Text for the Preview button: what THIS dialog's current settings will actually run.
    /// Delegates to ComputePathPlanner — the same code the optimizer uses to pick its path —
    /// so the preview cannot disagree with the run.
    /// </summary>
    public string BuildComputePathPreview()
        => LensHH.App.Views.ComputePathPreview.Build(
            _session,
            "Local optimization (" + (SelectedStep?.Label ?? "LM") + ")",
            // This dialog does not expose an engine choice — it always asks for the native
            // analytic path and relies on the planner's fallbacks. Keep these two literals in
            // step with StartOptimization below.
            EngineMode.Native,
            LensHH.Core.NativeInterop.MeritDerivativeMode.Analytic,
            UseBroydenUpdate,
            gpuImageOperands: false);

    [RelayCommand]
    public async Task StartOptimization()
    {
        IsRunning = true;
        IsComplete = false;
        StatusText = "Optimizing...";
        VariableRows.Clear();

        _cts = new CancellationTokenSource();
        _stopwatch.Restart();

        // Timer to update elapsed display
        var timer = new System.Timers.Timer(200);
        timer.Elapsed += (_, _) =>
            Dispatcher.UIThread.Post(() => ElapsedText = $"{_stopwatch.Elapsed.TotalSeconds:F1} s");
        timer.Start();

        try
        {
            // Build via the neutral factory so an advanced-edition host can inject its
            // multi-configuration editor at construction (null in the standard build).
            var optimizer = AppExtensions.CreateLocalOptimizer(
                _session.System, _session.MeritFunction, _session.GlassCatalog);
            optimizer.MaxIterations = MaxIterations;
            optimizer.UseBroydenUpdate = UseBroydenUpdate;
            optimizer.Step = SelectedStep?.Value ?? StepMethod.LevenbergMarquardt;
            optimizer.InitialDamping = InitialDamping;
            optimizer.ParallelEvaluation = true;

            // Use the native C++ ANALYTIC Jacobian — the bedrock-validated path, same default the
            // Basin Hopping / DE dialogs use. The optimizer auto-falls-back to C# only for variable
            // types native can't handle (semi-diameter, clear-aperture, model-glass, multi-config);
            // an ordinary curvature/thickness design runs native analytic (fast + exact gradients).
            optimizer.EngineMode = EngineMode.Native;
            optimizer.NativeDerivativeMode = LensHH.Core.NativeInterop.MeritDerivativeMode.Analytic;

            optimizer.CollectVariables();

            if (optimizer.Variables.Count == 0)
            {
                StatusText = "No variables defined.";
                IsRunning = false;
                IsComplete = true;
                timer.Stop();
                return;
            }

            // Populate variable table with starting values
            for (int i = 0; i < optimizer.Variables.Count; i++)
            {
                double startVal = optimizer.GetCurrentValue(optimizer.Variables[i]);
                VariableRows.Add(new VariableProgressRow(
                    optimizer.Variables[i].Description, startVal));
            }

            // Evaluate once to get initial merit (config-aware in PRO via the factory)
            var evaluator = AppExtensions.CreateMeritEvaluator(
                _session.System, _session.GlassCatalog);
            double initialMerit = evaluator.Evaluate(_session.MeritFunction);
            InitialMeritText = initialMerit.ToString("E6");
            BestMeritText = InitialMeritText;
            MeritText = $"Merit: {initialMerit:E6}";

            bool optimizationDone = false;

            optimizer.OnProgress = progress =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (optimizationDone) return; // Don't overwrite final results

                    IterationCount = progress.Iteration + 1;
                    BestMeritText = progress.MeritValue.ToString("E6");
                    MeritText = $"Merit: {progress.MeritValue:E6}  (iter {IterationCount})";

                    // Update variable rows
                    if (progress.CurrentValues.Length == VariableRows.Count)
                    {
                        for (int i = 0; i < VariableRows.Count; i++)
                            VariableRows[i].Update(progress.CurrentValues[i], optimizer.StartingValues[i]);
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
            // (config-aware in PRO via the factory).
            LensHH.Core.Analysis.SemiDiameterSolver.Solve(_session.System, _session.GlassCatalog, accurateApertures: true);
            var freshEval = AppExtensions.CreateMeritEvaluator(
                _session.System, _session.GlassCatalog);
            double finalMerit = freshEval.Evaluate(_session.MeritFunction);

            for (int i = 0; i < optimizer.Variables.Count; i++)
            {
                double finalVal = optimizer.GetCurrentValue(optimizer.Variables[i]);
                if (i < VariableRows.Count)
                    VariableRows[i].Update(finalVal, optimizer.StartingValues[i]);
            }

            InitialMeritText = result.InitialMerit.ToString("E6");
            BestMeritText = finalMerit.ToString("E6");
            IterationCount = result.Iterations;

            string status = result.Converged ? "Converged" :
                            result.Cancelled ? "Cancelled" : "Completed";
            StatusText = $"{status} — {result.Message}";
            MeritText = $"Merit: {result.InitialMerit:E6} → {finalMerit:E6}";
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

    private double GetCurrentVariableValue(OptimizationVariable v)
    {
        // Single source of truth: OptimizationVariable.GetValue defines the
        // convention for EVERY variable (curvature not radius, focal POWER not
        // focal length, etc.). Delegate so this display can never diverge.
        return v.GetValue(_session.System);
    }
}
