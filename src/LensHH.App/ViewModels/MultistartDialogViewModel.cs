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
    private readonly Stopwatch _stopwatch = new();

    // ── Settings ──
    [ObservableProperty] private int _maxTrials = 2000;
    [ObservableProperty] private int _lmIterationsPerTrial = 4000;
    [ObservableProperty] private int _initialLmIterations = 4000;
    [ObservableProperty] private double _initialSigma = 0.001;
    [ObservableProperty] private double _sigmaCap = 0.5;
    [ObservableProperty] private bool _enableMetropolis = true;
    [ObservableProperty] private int _hjStepsPerTrial = 50;
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
                    InitialLmIterations = InitialLmIterations,
                    InitialSigma = InitialSigma,
                    SigmaCap = SigmaCap,
                    EnableMetropolis = EnableMetropolis,
                    HjStepsPerTrial = HjStepsPerTrial,
                    GlassSwapLmMultiplier = GlassSwapLmMultiplier,
                    ConstrainedOnly = ConstrainedOnly,
                    GlassSubstitutionProbability = GlassSubstitutionProbability / 100.0,
                    UseBroydenUpdate = UseBroydenUpdate,
                    InitialDamping = InitialDamping,
                },
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
