using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LensHH.App.Session;
using LensHH.Core.Optimization;

namespace LensHH.App.ViewModels;

public partial class SpcSynthesisDialogViewModel : ObservableObject
{
    private readonly GuiSession _session;
    private CancellationTokenSource? _stopCts;
    private CancellationTokenSource? _skipPhaseCts;
    private readonly Stopwatch _stopwatch = new();

    // ── Settings ──
    [ObservableProperty] private int _maxElements = 2;
    [ObservableProperty] private int _topN = 5;
    [ObservableProperty] private double _scanMin = -0.1;
    [ObservableProperty] private double _scanMax = 0.1;
    [ObservableProperty] private int _scanSteps = 100;
    [ObservableProperty] private double _epsilon = 1e-3;
    [ObservableProperty] private int _glassTrials = 50;
    [ObservableProperty] private int _lmPerTrial = 4000;
    [ObservableProperty] private int _postSplitLm = 4000;
    [ObservableProperty] private double _minGlassThickness = 1.0;
    [ObservableProperty] private double _maxGlassThickness = 25.0;
    [ObservableProperty] private double _minAirGap = 0.1;
    [ObservableProperty] private double _maxAirGap = 50.0;
    [ObservableProperty] private double _minEdgeThickness = 1.0;
    [ObservableProperty] private double _constraintWeight = 10.0;
    [ObservableProperty] private bool _onlyPreferred = true;
    [ObservableProperty] private bool _runInitialLm = false;
    [ObservableProperty] private bool _archiveIntermediate = true;
    [ObservableProperty] private string _archiveDirectory = string.Empty;
    [ObservableProperty] private int _maxDop = Environment.ProcessorCount;
    [ObservableProperty] private string _nullElementGlass = "N-BK7";
    [ObservableProperty] private string _nullElementGlassFlint = "SF5";

    /// <summary>
    /// 0 = Both (default), 1 = Pre-stop only, 2 = Post-stop only.
    /// Restricts which side of the stop SPC may insert null elements on.
    /// </summary>
    [ObservableProperty] private int _insertionSideIndex;
    public string[] InsertionSideOptions { get; } =
    {
        "Both sides",
        "Pre-stop only",
        "Post-stop only",
    };

    /// <summary>
    /// 0 = Single (default), 1 = Cemented Doublet, 2 = Single + Cemented Doublet.
    /// Topology of each newly inserted null element. Modes 1 and 2 also
    /// drop Top N to 3 since each doublet candidate is much more
    /// expensive than a single-element candidate.
    /// </summary>
    [ObservableProperty] private int _elementTypeIndex;
    public string[] ElementTypeOptions { get; } =
    {
        "Single",
        "Cemented Doublet",
        "Single + Cemented Doublet",
    };

    partial void OnElementTypeIndexChanged(int value)
    {
        // Any mode that includes the doublet topology is far more expensive
        // per candidate (3 surfaces, 2-glass enumeration in glass trials,
        // and "Both" runs the per-position scan twice). Drop Top N from 5
        // to 3 so total compute stays bounded; user can override afterward.
        bool wantsDoublet = value == 1 || value == 2;
        if (wantsDoublet && TopN == 5) TopN = 3;
        else if (!wantsDoublet && TopN == 3) TopN = 5;
    }

    // ── Glass catalog picker (shared UX with SplitElement) ──
    public ObservableCollection<CatalogCheckItem> Catalogs { get; } = new();
    public ObservableCollection<string> GlassSourceOptions { get; } = new();
    [ObservableProperty] private int _selectedGlassSourceIndex;
    private readonly List<string> _filteredCatalogNames = new();
    public bool ShowLoadedCatalogs => SelectedGlassSourceIndex >= _filteredCatalogNames.Count;

    partial void OnSelectedGlassSourceIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ShowLoadedCatalogs));
    }

    // ── Status ──
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isComplete;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _phaseText = "";
    [ObservableProperty] private string _initialMeritText = "";
    [ObservableProperty] private string _currentMeritText = "";
    [ObservableProperty] private string _elapsedText = "0.0 s";
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private string _branchText = "";
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private bool _progressIndeterminate;

    /// <summary>Running minimum merit across the whole run — never goes back up.</summary>
    [ObservableProperty] private string _bestMeritText = "";
    private double _bestMeritSoFar = double.MaxValue;

    // Log buffering. SPC emits hundreds of progress lines per second
    // (per-candidate, per-trial, per-LM-iteration). The previous
    // implementation appended directly to the bound LogText string,
    // which is O(n^2) in total log length and re-renders the bound
    // TextBox on every single line — saturating the UI thread.
    // Now we append to a StringBuilder and flush its content into
    // LogText on a timer (~250 ms). Buffer is capped so neither memory
    // nor TextBox re-render cost grow without bound.
    private const int LogBufferMaxChars = 200_000;
    private const int LogBufferTrimToChars = 150_000;
    private readonly StringBuilder _logBuffer = new();
    private DispatcherTimer? _logFlushTimer;
    private bool _logDirty;

    // ── Help strip ──
    private const string DefaultHelpText = "Hover over a setting for a description.";
    [ObservableProperty] private string _helpText = DefaultHelpText;

    public void SetHelp(string? text)
        => HelpText = string.IsNullOrEmpty(text) ? DefaultHelpText : text!;

    public bool Accepted { get; private set; }

    public SpcSynthesisDialogViewModel(GuiSession session)
    {
        _session = session;

        foreach (var cat in session.GlassCatalog.LoadedCatalogs)
        {
            bool check = session.System.GlassCatalogs.Count == 0 ||
                         session.System.GlassCatalogs.Contains(cat);
            Catalogs.Add(new CatalogCheckItem(cat, check));
        }

        var filteredDir = GlassSubstitutionViewModel.FindFilteredCatalogFolder();
        if (filteredDir != null)
        {
            foreach (var file in System.IO.Directory.GetFiles(filteredDir, "*.agf"))
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(file);
                _filteredCatalogNames.Add(name);
                GlassSourceOptions.Add(name);
            }
        }
        GlassSourceOptions.Add("Loaded catalogs (select below)");
        SelectedGlassSourceIndex = 0;
    }

    [RelayCommand]
    public async Task Start()
    {
        if (IsRunning) return;
        IsRunning = true;
        IsComplete = false;
        LogText = "";
        _logBuffer.Clear();
        _logDirty = false;
        BestMeritText = "";
        _bestMeritSoFar = double.MaxValue;
        _stopwatch.Restart();
        _stopCts = new CancellationTokenSource();
        _skipPhaseCts = new CancellationTokenSource();

        var timer = new System.Timers.Timer(500);
        timer.Elapsed += (_, _) =>
            Dispatcher.UIThread.Post(() => ElapsedText = $"{_stopwatch.Elapsed.TotalSeconds:F1} s");
        timer.Start();

        _logFlushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _logFlushTimer.Tick += (_, _) => FlushLog();
        _logFlushTimer.Start();

        // Determine glass source
        var glassCatalogs = new List<string>();
        if (SelectedGlassSourceIndex < _filteredCatalogNames.Count)
        {
            string catName = _filteredCatalogNames[SelectedGlassSourceIndex];
            var filteredDir = GlassSubstitutionViewModel.FindFilteredCatalogFolder();
            if (filteredDir != null)
            {
                var agfPath = System.IO.Path.Combine(filteredDir, catName + ".agf");
                if (!System.IO.File.Exists(agfPath))
                    agfPath = System.IO.Path.Combine(filteredDir, catName + ".AGF");
                if (System.IO.File.Exists(agfPath))
                    _session.GlassCatalog.LoadCatalog(agfPath);
            }
            glassCatalogs.Add(catName);
        }
        else
        {
            glassCatalogs = Catalogs.Where(c => c.IsChecked).Select(c => c.Name).ToList();
        }

        bool usePreferred = ShowLoadedCatalogs && OnlyPreferred;
        int totalGlasses = 0;
        foreach (var cat in glassCatalogs)
        {
            var glasses = _session.GlassCatalog.GetGlassesInCatalog(cat);
            int count = usePreferred ? glasses.Count(g => g.Status <= 1) : glasses.Count;
            totalGlasses += count;
        }
        if (totalGlasses == 0)
        {
            AppendLog($"ERROR: No glasses found in catalog(s): {string.Join(", ", glassCatalogs)}");
            StatusText = "No glasses found — aborted";
            IsRunning = false;
            timer.Stop(); timer.Dispose(); _stopwatch.Stop();
            return;
        }
        AppendLog($"Glass source: {string.Join(", ", glassCatalogs)} ({totalGlasses} glasses)");
        PhaseText = "Starting...";
        StatusText = $"Parallelism: {MaxDop} threads. Archive: {ArchiveIntermediate}";

        var settings = new SpcSynthesisSettings
        {
            MaxElements = MaxElements,
            TopN = TopN,
            ScanMin = ScanMin,
            ScanMax = ScanMax,
            ScanSteps = ScanSteps,
            Epsilon = Epsilon,
            GlassTrials = GlassTrials,
            LmIterationsPerTrial = LmPerTrial,
            PostSplitLmIterations = PostSplitLm,
            MinGlassThickness = MinGlassThickness,
            MaxGlassThickness = MaxGlassThickness,
            MinAirGap = MinAirGap,
            MaxAirGap = MaxAirGap,
            MinEdgeThickness = MinEdgeThickness,
            ConstraintWeight = ConstraintWeight,
            OnlyPreferred = usePreferred,
            GlassCatalogs = glassCatalogs,
            RunInitialLm = RunInitialLm,
            MaxDegreeOfParallelism = MaxDop,
            ArchiveIntermediateDesigns = ArchiveIntermediate,
            ArchiveDirectory = string.IsNullOrWhiteSpace(ArchiveDirectory) ? null : ArchiveDirectory,
            NullElementGlass = NullElementGlass,
            NullElementGlassFlint = NullElementGlassFlint,
            InsertionSide = InsertionSideIndex switch
            {
                1 => SpcInsertionSide.PreStop,
                2 => SpcInsertionSide.PostStop,
                _ => SpcInsertionSide.Both,
            },
            ElementType = ElementTypeIndex switch
            {
                1 => SpcElementType.CementedDoublet,
                2 => SpcElementType.Both,
                _ => SpcElementType.Single,
            }
        };

        if (settings.ArchiveIntermediateDesigns)
        {
            settings.ArchiveWriter = (path, sys, mf) =>
                LensHH.Core.IO.LhltWriter.Write(sys, path, mf, _session.ConfigEditor);
        }

        var stopToken = _stopCts.Token;
        var skipToken = _skipPhaseCts.Token;

        SpcSynthesisResult? result = null;
        try
        {
            result = await Task.Run(() =>
            {
                var service = new SpcSynthesisService(
                    _session.System, _session.MeritFunction,
                    _session.GlassCatalog, _session.ConfigEditor)
                {
                    Settings = settings,
                    OnProgress = p => Dispatcher.UIThread.Post(() => UpdateProgress(p))
                };
                return service.Execute(stopToken, skipToken);
            }, stopToken);
        }
        catch (OperationCanceledException)
        {
            AppendLog("Cancelled by user.");
        }
        catch (Exception ex)
        {
            AppendLog($"Error: {ex.Message}");
        }

        timer.Stop(); timer.Dispose(); _stopwatch.Stop();
        _logFlushTimer?.Stop();
        _logFlushTimer = null;
        IsRunning = false;
        IsComplete = true;
        // Final flush so the user sees the last batch of log lines
        // and the completion banner together.
        FlushLog();

        if (result != null)
        {
            InitialMeritText = result.InitialMerit.ToString("G8");
            CurrentMeritText = result.FinalMerit.ToString("G8");
            StatusText = result.Cancelled ? "Cancelled" : "Complete";
            AppendLog($"SPC complete. Merit: {result.InitialMerit:G6} -> {result.FinalMerit:G6} in {_stopwatch.Elapsed.TotalSeconds:F1}s");
            foreach (var lvl in result.Levels)
            {
                AppendLog($"Level {lvl.Level + 1}: {lvl.CandidatesEvaluated} candidate(s), kept top {lvl.Survivors}. Best: {lvl.BestMerit:G6}");
                for (int i = 0; i < lvl.TopCandidates.Count && i < 3; i++)
                {
                    var c = lvl.TopCandidates[i];
                    string sign = c.BranchSign > 0 ? "+" : "-";
                    AppendLog($"  #{i + 1}: surf {c.InsertionSurface}, c={c.SaddleCurvature:F4}, {sign}, {c.Glass}, merit={c.Merit:G6}");
                }
            }
        }
    }

    [RelayCommand]
    public void Stop()
    {
        _stopCts?.Cancel();
    }

    [RelayCommand]
    public void SkipPhase()
    {
        _skipPhaseCts?.Cancel();
        // Replace the token so subsequent phases aren't pre-cancelled
        _skipPhaseCts = new CancellationTokenSource();
    }

    public void Accept() => Accepted = true;

    [RelayCommand]
    public async Task CopyLog()
    {
        if (!string.IsNullOrEmpty(LogText))
        {
            var clipboard = Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow?.Clipboard : null;
            if (clipboard != null)
                await clipboard.SetTextAsync(LogText);
        }
    }

    [RelayCommand]
    public void ClearLog()
    {
        _logBuffer.Clear();
        _logDirty = false;
        LogText = "";
    }

    private void UpdateProgress(SpcSynthesisProgress p)
    {
        ElapsedText = $"{_stopwatch.Elapsed.TotalSeconds:F1} s";

        PhaseText = $"Level {p.Level + 1}/{p.MaxLevels} — {p.Phase}";

        if (p.CandidateCount > 0)
        {
            BranchText = $"Branch {p.CandidateIndex} / {p.CandidateCount}";
            ProgressPercent = 100.0 * p.CandidateIndex / p.CandidateCount;
            ProgressIndeterminate = false;
        }
        else
        {
            BranchText = "";
            ProgressIndeterminate = p.Phase != SpcPhase.Complete;
        }

        if (p.BestMerit > 0 && p.BestMerit < double.MaxValue)
            CurrentMeritText = p.BestMerit.ToString("G8");
        else if (p.CurrentMerit > 0 && p.CurrentMerit < double.MaxValue)
            CurrentMeritText = p.CurrentMerit.ToString("G8");

        // Track the running best as a monotonic minimum: the "Best merit" label
        // should only ever drop, never swing upward when a new branch starts.
        double? candidate = null;
        if (p.BestMerit > 0 && p.BestMerit < double.MaxValue) candidate = p.BestMerit;
        else if (p.CurrentMerit > 0 && p.CurrentMerit < double.MaxValue) candidate = p.CurrentMerit;
        if (candidate.HasValue && candidate.Value < _bestMeritSoFar)
        {
            _bestMeritSoFar = candidate.Value;
            BestMeritText = candidate.Value.ToString("G8");
        }

        AppendLog(p.StatusMessage);
        StatusText = p.StatusMessage;
    }

    private void AppendLog(string line)
    {
        _logBuffer.Append(line).Append('\n');
        _logDirty = true;
        // Bound the buffer so the eventual TextBox re-render and
        // memory don't grow forever on long runs.
        if (_logBuffer.Length > LogBufferMaxChars)
            _logBuffer.Remove(0, _logBuffer.Length - LogBufferTrimToChars);
    }

    private void FlushLog()
    {
        if (!_logDirty) return;
        _logDirty = false;
        LogText = _logBuffer.ToString();
    }
}
