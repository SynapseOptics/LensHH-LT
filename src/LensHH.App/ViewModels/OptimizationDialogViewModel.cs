using System;
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
    [ObservableProperty] private int _maxIterations = 4000;
    [ObservableProperty] private bool _useBroydenUpdate = true;

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
            var optimizer = new LocalOptimizer(
                _session.System, _session.MeritFunction,
                _session.GlassCatalog, configEditor: _session.ConfigEditor)
            {
                MaxIterations = MaxIterations,
                UseBroydenUpdate = UseBroydenUpdate,
                InitialDamping = InitialDamping,
                ParallelEvaluation = true
            };

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
                double startVal = GetCurrentVariableValue(optimizer.Variables[i]);
                VariableRows.Add(new VariableProgressRow(
                    optimizer.Variables[i].Description, startVal));
            }

            // Evaluate once to get initial merit
            var evaluator = new MeritFunctionEvaluator(
                _session.System, _session.GlassCatalog, configEditor: _session.ConfigEditor);
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
            LensHH.Core.Analysis.SemiDiameterSolver.Solve(_session.System, _session.GlassCatalog);
            var freshEval = new MeritFunctionEvaluator(
                _session.System, _session.GlassCatalog, configEditor: _session.ConfigEditor);
            double finalMerit = freshEval.Evaluate(_session.MeritFunction);

            for (int i = 0; i < optimizer.Variables.Count; i++)
            {
                double finalVal = GetCurrentVariableValue(optimizer.Variables[i]);
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
        if (v.Type == VariableType.FieldY)
            return _session.System.Fields[v.FieldIndex].Y;

        var surface = _session.System.Surfaces[v.SurfaceIndex];
        switch (v.Type)
        {
            case VariableType.Curvature: return surface.Curvature;
            case VariableType.Thickness: return surface.Thickness;
            case VariableType.Conic: return surface.Conic;
            case VariableType.AsphericCoefficient:
                return surface.AsphericCoefficients[v.AsphericTermIndex];
            default: return 0;
        }
    }
}
