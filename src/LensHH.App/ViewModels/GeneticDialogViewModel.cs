using System;
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

public partial class GeneticVariableRow : ObservableObject
{
    public string Description { get; }
    public string StartValue { get; }
    [ObservableProperty] private string _currentValue;
    [ObservableProperty] private string _delta;

    public GeneticVariableRow(string description, double startValue)
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

public partial class GeneticDialogViewModel : ObservableObject
{
    private readonly GuiSession _session;
    private CancellationTokenSource _cts;
    private readonly Stopwatch _stopwatch = new();

    // Settings
    [ObservableProperty] private int _populationSize = 200;
    [ObservableProperty] private int _generations = 100;
    [ObservableProperty] private double _mutationRate = 15;  // display as %
    [ObservableProperty] private double _crossoverRate = 70; // display as %
    [ObservableProperty] private int _eliteCount = 10;
    [ObservableProperty] private int _lmTopCount = 100;
    [ObservableProperty] private int _lmIterations = 50;

    // Status
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isComplete;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _meritText = "";
    [ObservableProperty] private string _elapsedText = "0.0 s";
    [ObservableProperty] private string _initialMeritText = "";
    [ObservableProperty] private string _geneticMeritText = "";
    [ObservableProperty] private string _bestMeritText = "";

    public ObservableCollection<GeneticVariableRow> VariableRows { get; } = new();

    public bool Accepted { get; set; }

    public GeneticDialogViewModel(GuiSession session) { _session = session; }

    [RelayCommand]
    public async Task StartOptimization()
    {
        IsRunning = true;
        IsComplete = false;
        StatusText = "Initializing population...";
        VariableRows.Clear();

        _cts = new CancellationTokenSource();
        _stopwatch.Restart();

        var timer = new System.Timers.Timer(200);
        timer.Elapsed += (_, _) =>
            Dispatcher.UIThread.Post(() => ElapsedText = $"{_stopwatch.Elapsed.TotalSeconds:F1} s");
        timer.Start();

        try
        {
            var optimizer = new GeneticOptimizer(
                _session.System, _session.MeritFunction,
                _session.GlassCatalog, _session.ConfigEditor)
            {
                Settings = new GeneticSettings
                {
                    PopulationSize = PopulationSize,
                    Generations = Generations,
                    MutationRate = MutationRate / 100.0,
                    CrossoverRate = CrossoverRate / 100.0,
                    EliteCount = EliteCount,
                    LmTopCount = LmTopCount,
                    LmIterations = LmIterations
                }
            };

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

                    if (!variableRowsPopulated && optimizer.Variables.Count > 0)
                    {
                        variableRowsPopulated = true;
                        for (int i = 0; i < optimizer.Variables.Count; i++)
                            VariableRows.Add(new GeneticVariableRow(
                                optimizer.Variables[i].Description,
                                optimizer.StartingValues[i]));
                    }

                    if (progress.Phase == "genetic")
                    {
                        StatusText = $"Generation {progress.Generation}/{progress.MaxGenerations}" +
                            $" — Best: {progress.BestMerit:E4}, Avg: {progress.AvgMerit:E4}";
                        BestMeritText = progress.BestMerit.ToString("E6");
                        MeritText = $"Merit: {progress.BestMerit:E6} (gen {progress.Generation})";

                        if (progress.BestValues.Length == VariableRows.Count)
                        {
                            for (int i = 0; i < VariableRows.Count; i++)
                                VariableRows[i].Update(progress.BestValues[i], optimizer.StartingValues[i]);
                        }
                    }
                    else if (progress.Phase == "lm")
                    {
                        StatusText = $"LM refinement {progress.LmCompleted}/{progress.LmTotal}";
                        MeritText = $"Refining top {progress.LmTotal} with LM...";
                    }
                });
            };

            GeneticResult result;
            try
            {
                result = await Task.Run(() => optimizer.Optimize(_cts.Token));
            }
            catch (OperationCanceledException)
            {
                // User pressed Stop — Parallel.For propagates the cancellation
                // as OperationCanceledException. Treat as a clean exit: report
                // "Cancelled" and skip the final-merit / best-values rollup
                // since the optimizer may have been mid-generation.
                await Dispatcher.UIThread.InvokeAsync(() => { optimizationDone = true; });
                _stopwatch.Stop();
                timer.Stop();
                ElapsedText = $"{_stopwatch.Elapsed.TotalSeconds:F1} s";
                StatusText = "Cancelled";
                MeritText = string.Empty;
                IsRunning = false;
                IsComplete = true;
                return;
            }
            catch (AggregateException ae) when (ae.InnerExceptions.All(e => e is OperationCanceledException))
            {
                // Parallel.For can wrap cancellation in AggregateException if
                // multiple workers observe the token simultaneously.
                await Dispatcher.UIThread.InvokeAsync(() => { optimizationDone = true; });
                _stopwatch.Stop();
                timer.Stop();
                ElapsedText = $"{_stopwatch.Elapsed.TotalSeconds:F1} s";
                StatusText = "Cancelled";
                MeritText = string.Empty;
                IsRunning = false;
                IsComplete = true;
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() => { optimizationDone = true; });

            _stopwatch.Stop();
            timer.Stop();
            ElapsedText = $"{_stopwatch.Elapsed.TotalSeconds:F1} s";

            // Re-evaluate with fresh evaluator
            LensHH.Core.Analysis.SemiDiameterSolver.Solve(_session.System, _session.GlassCatalog);
            var freshEval = new MeritFunctionEvaluator(
                _session.System, _session.GlassCatalog, configEditor: _session.ConfigEditor);
            double finalMerit = freshEval.Evaluate(_session.MeritFunction);

            // Update variable rows
            for (int i = 0; i < optimizer.Variables.Count && i < VariableRows.Count; i++)
            {
                double val = GetVariableValue(optimizer.Variables[i]);
                VariableRows[i].Update(val, optimizer.StartingValues[i]);
            }

            InitialMeritText = result.InitialMerit.ToString("E6");
            GeneticMeritText = result.BestGeneticMerit.ToString("E6");
            BestMeritText = finalMerit.ToString("E6");

            string status = result.Cancelled ? "Cancelled" : "Completed";
            StatusText = $"{status} — {result.Message}";
            MeritText = $"Merit: {result.InitialMerit:E4} → {result.BestGeneticMerit:E4} (genetic) → {finalMerit:E4} (LM)";
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

    private double GetVariableValue(OptimizationVariable v)
    {
        if (v.Type == VariableType.FieldY)
            return _session.System.Fields[v.FieldIndex].Y;
        var surface = _session.System.Surfaces[v.SurfaceIndex];
        switch (v.Type)
        {
            case VariableType.Curvature: return surface.Curvature;
            case VariableType.Thickness: return surface.Thickness;
            case VariableType.Conic: return surface.Conic;
            case VariableType.AsphericCoefficient: return surface.AsphericCoefficients[v.AsphericTermIndex];
            default: return 0;
        }
    }
}
