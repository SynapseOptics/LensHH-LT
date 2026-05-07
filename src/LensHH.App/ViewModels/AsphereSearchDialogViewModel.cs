using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LensHH.App.Session;
using LensHH.Core.Optimization;

namespace LensHH.App.ViewModels;

public partial class AsphereTrialRow : ObservableObject
{
    public int Rank { get; }
    public int SurfaceIndex { get; }
    public string PostMerit { get; }
    public string ImprovementPercent { get; }
    public string Status { get; }

    public AsphereTrialRow(int rank, AsphereTrialResult t)
    {
        Rank = rank;
        SurfaceIndex = t.SurfaceIndex;
        PostMerit = t.PostTrialMerit < 1e6 ? t.PostTrialMerit.ToString("G6") : "FAIL";
        ImprovementPercent = t.TrialFailed ? "—" : t.ImprovementPercent.ToString("+0.00;-0.00;0") + " %";
        Status = t.TrialFailed ? "FAILED" : "OK";
    }
}

public partial class AsphereSearchDialogViewModel : ObservableObject
{
    private readonly GuiSession _session;
    private CancellationTokenSource? _cts;
    private readonly Stopwatch _stopwatch = new();

    // ── Settings ──
    [ObservableProperty] private bool _enableA4 = true;
    [ObservableProperty] private bool _enableA6 = true;
    [ObservableProperty] private bool _enableA8 = false;
    [ObservableProperty] private int _lmIterationsPerTrial = 500;
    [ObservableProperty] private int _finalLmIterations = 4000;
    [ObservableProperty] private int _topN = 1;
    [ObservableProperty] private bool _skipAlreadyAspheric = true;
    [ObservableProperty] private bool _acceptOnlyIfBetter = true;
    [ObservableProperty] private double _minImprovementPercent = 1.0;

    // ── Status ──
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isComplete;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _elapsedText = "0.0 s";
    [ObservableProperty] private string _initialMeritText = "";
    [ObservableProperty] private string _finalMeritText = "";
    [ObservableProperty] private string _logText = "";

    public ObservableCollection<AsphereTrialRow> TrialRows { get; } = new();

    // Help strip
    private const string DefaultHelpText = "Hover over a setting for a description.";
    [ObservableProperty] private string _helpText = DefaultHelpText;
    public void SetHelp(string? text)
        => HelpText = string.IsNullOrEmpty(text) ? DefaultHelpText : text!;

    public bool Accepted { get; set; }

    public AsphereSearchDialogViewModel(GuiSession session)
    {
        _session = session;
    }

    [RelayCommand]
    public async Task Start()
    {
        if (IsRunning) return;
        IsRunning = true;
        IsComplete = false;
        LogText = "";
        TrialRows.Clear();
        _stopwatch.Restart();
        _cts = new CancellationTokenSource();

        var timer = new System.Timers.Timer(500);
        timer.Elapsed += (_, _) =>
            Dispatcher.UIThread.Post(() => ElapsedText = $"{_stopwatch.Elapsed.TotalSeconds:F1} s");
        timer.Start();

        var settings = new AsphereSurfaceSearchSettings
        {
            EnableA4 = EnableA4,
            EnableA6 = EnableA6,
            EnableA8 = EnableA8,
            LmIterationsPerTrial = LmIterationsPerTrial,
            FinalLmIterations = FinalLmIterations,
            TopN = TopN,
            SkipAlreadyAspheric = SkipAlreadyAspheric,
            AcceptOnlyIfBetter = AcceptOnlyIfBetter,
            MinImprovementPercent = MinImprovementPercent,
        };

        AppendLog($"Starting asphere surface search. Initial merit: {(_session.MeritFunction != null ? "computing..." : "no merit fn")}");

        AsphereSurfaceSearchResult? result = null;
        try
        {
            result = await Task.Run(() =>
            {
                var service = new AsphereSurfaceSearchService(
                    _session.System, _session.MeritFunction,
                    _session.GlassCatalog, _session.ConfigEditor)
                {
                    Settings = settings,
                    OnProgress = p => Dispatcher.UIThread.Post(() => UpdateProgress(p))
                };
                return service.Execute(_cts.Token);
            }, _cts.Token);
        }
        catch (OperationCanceledException) { AppendLog("Cancelled by user."); }
        catch (Exception ex) { AppendLog($"Error: {ex.Message}"); }

        timer.Stop(); timer.Dispose(); _stopwatch.Stop();

        if (result != null)
        {
            InitialMeritText = result.InitialMerit.ToString("G8");
            FinalMeritText = result.FinalMerit.ToString("G8");
            StatusText = result.Cancelled ? "Cancelled"
                : result.ResultRejected ? "Rejected (no improvement)"
                : "Complete";

            // Populate the trials grid
            for (int i = 0; i < result.Trials.Count; i++)
                TrialRows.Add(new AsphereTrialRow(i + 1, result.Trials[i]));

            AppendLog($"Search complete in {_stopwatch.Elapsed.TotalSeconds:F1}s");
            AppendLog($"  Merit: {result.InitialMerit:G6} -> {result.FinalMerit:G6}");
            AppendLog($"  {result.Message}");
        }

        IsRunning = false;
        IsComplete = true;
    }

    [RelayCommand]
    public void Stop() => _cts?.Cancel();

    private void UpdateProgress(AsphereSurfaceSearchProgress p)
    {
        StatusText = p.Phase + (p.TrialTotal > 0 ? $" — trial {p.TrialIndex}/{p.TrialTotal}" : "");
        if (!string.IsNullOrEmpty(p.StatusMessage))
            AppendLog(p.StatusMessage);
    }

    private void AppendLog(string line) => LogText += line + "\n";
}
