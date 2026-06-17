using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LensHH.App.Session;
using LensHH.Core.Optimization;
using LensHH.Core.IO;

namespace LensHH.App.ViewModels;

public partial class CatalogCheckItem : ObservableObject
{
    public string Name { get; }
    [ObservableProperty] private bool _isChecked;

    public CatalogCheckItem(string name, bool isChecked = true)
    {
        Name = name;
        _isChecked = isChecked;
    }
}

public partial class SplitElementDialogViewModel : ObservableObject
{
    private readonly GuiSession _session;
    private CancellationTokenSource? _cts;
    
    /// <summary>Cancel a running operation when the dialog closes (else worker threads run to completion).</summary>
    public void CancelRun() => _cts?.Cancel();
    private readonly Stopwatch _stopwatch = new();

    // ── Settings ──
    [ObservableProperty] private int _maxSplits = 1;
    [ObservableProperty] private int _glassTrials = 300;
    [ObservableProperty] private int _lmPerTrial = 4000;
    [ObservableProperty] private int _postSplitLm = 4000;
    [ObservableProperty] private double _minGlassThickness = 1.0;
    [ObservableProperty] private double _maxGlassThickness = 25.0;
    [ObservableProperty] private double _minAirGap = 0.1;
    [ObservableProperty] private double _maxAirGap = 25.0;
    [ObservableProperty] private double _minEdgeThickness = 0.5;
    [ObservableProperty] private bool _onlyPreferred = true;
    [ObservableProperty] private int _preGlassTrials = 4000;
    [ObservableProperty] private int _postGlassTrials = 2500;
    [ObservableProperty] private double _msSigma = 0.001;
    [ObservableProperty] private bool _constrainedOnly = false;
    /// <summary>Free all glasses: skip the three-phase pre-glass / glass-pair / post-glass flow and run a single Multistart with glass substitution enabled on every interior glass surface, drawing from the selected catalog.</summary>
    [ObservableProperty] private bool _freeAllGlasses = false;
    /// <summary>Reject a split that produced a final merit worse than the starting merit; restore the original design.</summary>
    [ObservableProperty] private bool _acceptOnlyIfBetter = true;
    /// <summary>Auto-advance to the next phase if BestMerit hasn't improved in this many seconds. 0 = disabled.</summary>
    [ObservableProperty] private double _skipPhaseAfterNoImprovementSec = 180;

    // Set while a phase is running so the Next-Phase button can target it.
    private SplitElementService? _runningService;

    // ── Glass catalogs ──
    public ObservableCollection<CatalogCheckItem> Catalogs { get; } = new();
    public ObservableCollection<string> GlassSourceOptions { get; } = new();
    [ObservableProperty] private int _selectedGlassSourceIndex;
    private readonly List<string> _filteredCatalogNames = new();
    // Index 0 = "(None — skip glass trials)". Filtered catalogs occupy
    // indices 1..N; "Loaded catalogs (select below)" is at N+1.
    private const int SkipGlassSourceIndex = 0;
    public bool IsSkipGlassSelected => SelectedGlassSourceIndex == SkipGlassSourceIndex;
    public bool ShowLoadedCatalogs => SelectedGlassSourceIndex == _filteredCatalogNames.Count + 1;

    partial void OnSelectedGlassSourceIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ShowLoadedCatalogs));
        OnPropertyChanged(nameof(IsSkipGlassSelected));
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
    [ObservableProperty] private string _trialText = "";
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private bool _progressIndeterminate;
    /// <summary>
    /// "Stalled 12 s / 180 s" — seconds since the active phase last
    /// improved BestMerit, with the configured auto-skip threshold for
    /// reference. Empty between phases.
    /// </summary>
    [ObservableProperty] private string _stalledText = "";

    // ── Help strip ──
    private const string DefaultHelpText = "Hover over a setting for a description.";
    [ObservableProperty] private string _helpText = DefaultHelpText;

    public void SetHelp(string? text)
        => HelpText = string.IsNullOrEmpty(text) ? DefaultHelpText : text!;

    public bool Accepted { get; private set; }

    public SplitElementDialogViewModel(GuiSession session)
    {
        _session = session;

        foreach (var cat in session.GlassCatalog.LoadedCatalogs)
        {
            // Default: check catalogs that are in the system's GlassCatalogs list
            bool check = session.System.GlassCatalogs.Count == 0 ||
                         session.System.GlassCatalogs.Contains(cat);
            Catalogs.Add(new CatalogCheckItem(cat, check));
        }

        // Build glass source options. Index 0 is the explicit "skip"
        // option so the user can run split-element without a glass-pair
        // phase; filtered catalogs follow; then the loaded-catalogs
        // fallback.
        GlassSourceOptions.Add("(None — skip glass trials)");
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
        // Default to the first filtered catalog if any exist; otherwise
        // the loaded-catalogs fallback. Don't default to None — that's
        // an explicit opt-in.
        SelectedGlassSourceIndex = _filteredCatalogNames.Count > 0 ? 1 : _filteredCatalogNames.Count + 1;
    }

    [RelayCommand]
    public async Task Start()
    {
        if (IsRunning) return;
        IsRunning = true;
        IsComplete = false;
        LogText = "";
        _stopwatch.Restart();
        _cts = new CancellationTokenSource();

        // Timer to keep elapsed time and status alive independently of engine callbacks
        var timer = new System.Timers.Timer(500);
        timer.Elapsed += (_, _) =>
            Dispatcher.UIThread.Post(() => ElapsedText = $"{_stopwatch.Elapsed.TotalSeconds:F1} s");
        timer.Start();

        // Determine glass source
        var glassCatalogs = new List<string>();
        bool skipGlassTrials = IsSkipGlassSelected;
        if (skipGlassTrials)
        {
            // No glass-pair phase — split + LM polish only.
        }
        else if (SelectedGlassSourceIndex >= 1 && SelectedGlassSourceIndex <= _filteredCatalogNames.Count)
        {
            // Filtered catalog selected — load AGF if needed.
            // Index 0 is the skip option, so subtract 1.
            string catName = _filteredCatalogNames[SelectedGlassSourceIndex - 1];
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
            // "Loaded catalogs" selected — use checked catalogs
            glassCatalogs = Catalogs.Where(c => c.IsChecked).Select(c => c.Name).ToList();
        }

        // Verify glass catalogs have glasses before starting lengthy
        // optimization. Skipped when the user explicitly opted out of
        // the glass phase.
        bool usePreferred = ShowLoadedCatalogs && OnlyPreferred;
        int totalGlasses = 0;
        foreach (var cat in glassCatalogs)
        {
            var glasses = _session.GlassCatalog.GetGlassesInCatalog(cat);
            int count = usePreferred ? glasses.Count(g => g.Status <= 1) : glasses.Count;
            totalGlasses += count;
        }
        string glassSourceDesc = skipGlassTrials
            ? "(none — glass trials skipped)"
            : $"{string.Join(", ", glassCatalogs)} ({totalGlasses} glasses)";
        if (!skipGlassTrials && totalGlasses == 0)
        {
            AppendLog($"ERROR: No glasses found in catalog(s): {string.Join(", ", glassCatalogs)}");
            AppendLog("Check that the glass catalog is loaded or select a different source (or pick None to skip glass trials).");
            StatusText = "No glasses found — aborted";
            IsRunning = false;
            timer.Stop();
            timer.Dispose();
            _stopwatch.Stop();
            return;
        }
        AppendLog($"Glass source: {glassSourceDesc}");
        PhaseText = "Starting...";
        StatusText = $"Glass source: {glassSourceDesc}";

        var settings = new SplitElementSettings
        {
            MaxSplits = MaxSplits,
            GlassTrials = GlassTrials,
            LmIterationsPerTrial = LmPerTrial,
            PostSplitLmIterations = PostSplitLm,
            MinGlassThickness = MinGlassThickness,
            MaxGlassThickness = MaxGlassThickness,
            MinAirGap = MinAirGap,
            MaxAirGap = MaxAirGap,
            MinEdgeThickness = MinEdgeThickness,
            GlassCatalogs = glassCatalogs,
            // Filtered catalogs are already curated — don't apply preferred filter
            OnlyPreferred = ShowLoadedCatalogs && OnlyPreferred,
            PreGlassMultistartTrials = PreGlassTrials,
            PostGlassMultistartTrials = PostGlassTrials,
            MultistartInitialSigma = MsSigma,
            FreeAllGlasses = FreeAllGlasses,
            AcceptOnlyIfBetter = AcceptOnlyIfBetter,
            ConstrainedOnly = ConstrainedOnly,
            SkipPhaseAfterNoImprovementSeconds = SkipPhaseAfterNoImprovementSec,
            SkipGlassTrials = skipGlassTrials,
        };

        var ct = _cts.Token;

        SplitElementResult? result = null;
        try
        {
            result = await Task.Run(() =>
            {
                var service = new SplitElementService(
                    _session.System, _session.MeritFunction,
                    _session.GlassCatalog, _session.ConfigEditor)
                {
                    Settings = settings,
                    OnProgress = p => Dispatcher.UIThread.Post(() => UpdateProgress(p)),
                    OnSplitRejected = (initialMerit, finalMerit) =>
                        SaveRejectedSplit(initialMerit, finalMerit),
                    OnSplitIterationComplete = (splitIndex, iterMerit) =>
                        SaveSplitIteration(splitIndex, iterMerit)
                };
                _runningService = service;
                try { return service.Execute(ct); }
                finally { _runningService = null; }
            }, ct);
        }
        catch (OperationCanceledException)
        {
            AppendLog("Cancelled by user.");
        }
        catch (Exception ex)
        {
            AppendLog($"Error: {ex.Message}");
        }

        timer.Stop();
        timer.Dispose();
        _stopwatch.Stop();
        IsRunning = false;
        IsComplete = true;

        if (result != null)
        {
            InitialMeritText = result.InitialMerit.ToString("G8");
            CurrentMeritText = result.FinalMerit.ToString("G8");
            StatusText = result.Cancelled ? "Cancelled" : "Complete";

            foreach (var iter in result.Iterations)
            {
                AppendLog($"Split {iter.SplitIndex + 1}: surface {iter.SelectedSurfaceIndex} ({iter.SelectedMaterial}), " +
                    $"score={iter.AberrationScore:G4}");
                AppendLog($"  Post-split merit: {iter.PostSplitMerit:G6}");
                AppendLog($"  Best glass: {iter.BestGlass1} + {iter.BestGlass2} ({iter.GlassTrialsRun} trials)");
                AppendLog($"  Final merit: {iter.PostGlassTrialMerit:G6}");
            }
            AppendLog($"Merit: {result.InitialMerit:G6} -> {result.FinalMerit:G6} in {_stopwatch.Elapsed.TotalSeconds:F1}s");
        }
    }

    [RelayCommand]
    public void Stop()
    {
        _cts?.Cancel();
    }

    /// <summary>
    /// Tell the engine to abandon the in-flight phase and continue to the
    /// next one. Useful when pre-glass / post-glass multistart has
    /// plateaued on the current basin and the user wants to move on to
    /// the glass-trials phase without losing the run entirely.
    /// </summary>
    [RelayCommand]
    public void NextPhase()
    {
        _runningService?.RequestSkipCurrentPhase();
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
    public void ClearLog() => LogText = "";

    private void UpdateProgress(SplitElementProgress p)
    {
        ElapsedText = $"{_stopwatch.Elapsed.TotalSeconds:F1} s";

        // Phase header for the status line
        string phaseLabel = p.Phase switch
        {
            SplitPhase.SeidelAnalysis or SplitPhase.ElementSelection or SplitPhase.Splitting
                => "Splitting",
            SplitPhase.LocalOptimize => "Pre-Glass Multistart",
            SplitPhase.GlassTrials => "Glass Trials",
            SplitPhase.PostOptimize => "Post-Glass Multistart",
            SplitPhase.Complete => "Complete",
            _ => p.Phase.ToString()
        };
        PhaseText = $"Split {p.SplitIteration + 1}/{p.MaxSplits} — {phaseLabel}";

        // Trial counter and progress bar
        if (p.GlassTrialCurrent > 0 && p.GlassTrialTotal > 0)
        {
            TrialText = $"Trial {p.GlassTrialCurrent} / {p.GlassTrialTotal}";
            ProgressPercent = 100.0 * p.GlassTrialCurrent / p.GlassTrialTotal;
            ProgressIndeterminate = false;
        }
        else if (p.LmIteration > 0)
        {
            TrialText = $"LM iter {p.LmIteration}";
            ProgressIndeterminate = true;
        }
        else
        {
            TrialText = "";
            ProgressPercent = 0;
            ProgressIndeterminate = false;
        }

        // Update merit display
        if (p.BestMerit > 0 && p.BestMerit < double.MaxValue)
            CurrentMeritText = p.BestMerit.ToString("G8");

        // No-improvement watchdog readout. Only meaningful while a
        // phase is active (engine reports 0 between phases) and the
        // user has the auto-skip enabled.
        if (p.SecondsSinceLastImprovement > 0 && SkipPhaseAfterNoImprovementSec > 0)
            StalledText = $"Stalled {p.SecondsSinceLastImprovement:F0} s / {SkipPhaseAfterNoImprovementSec:F0} s";
        else
            StalledText = "";

        // Log only meaningful events — skip routine heartbeats and all mid-run LM iterations
        bool isRoutineHeartbeat = p.StatusMessage.StartsWith("  Trial ") && !p.StatusMessage.Contains("Improved");
        bool isLmLine = p.StatusMessage.StartsWith("  LM iter ");
        if (!isRoutineHeartbeat && !isLmLine)
        {
            AppendLog(p.StatusMessage);
        }

        // Always update the status text
        StatusText = p.StatusMessage;
    }

    private void AppendLog(string line)
    {
        LogText += line + "\n";
    }

    /// <summary>
    /// SplitElementService callback fired after each successful split
    /// iteration. Writes the post-multistart state of that iteration to
    /// disk alongside the source file so the user can recover any
    /// intermediate manually — important for multi-split runs where the
    /// best-intermediate restore picks an earlier iteration than the
    /// final one. Returns the path written, or null on failure.
    /// </summary>
    private string? SaveSplitIteration(int splitIndex, double iterMerit)
    {
        try
        {
            var (dir, baseName) = ResolveArchiveDestination();
            string meritStr = iterMerit.ToString("G4").Replace("+", "").Replace(".", "p");
            string fileName = $"{baseName}_split{splitIndex}_merit{meritStr}.lhlt";
            string fullPath = Path.Combine(dir, fileName);
            LhltWriter.Write(_session.System, fullPath, _session.MeritFunction, _session.ConfigEditor);
            return fullPath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Pick the directory + base filename for Split Element archives.
    /// Source-file directory if a file is open; otherwise a per-session
    /// folder under the user's Documents.
    /// </summary>
    private (string dir, string baseName) ResolveArchiveDestination()
    {
        if (!string.IsNullOrEmpty(_session.FilePath))
        {
            return (
                Path.GetDirectoryName(_session.FilePath!) ?? string.Empty,
                Path.GetFileNameWithoutExtension(_session.FilePath!));
        }
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string dir = Path.Combine(docs, "LensHH-LT", "SplitElement");
        Directory.CreateDirectory(dir);
        string baseName = string.IsNullOrEmpty(_session.CurrentFileName)
            ? "Untitled" : _session.CurrentFileName!;
        return (dir, baseName);
    }

    /// <summary>
    /// SplitElementService callback fired when the regression guard is
    /// about to roll back a worse-than-starting result. The rejected
    /// state is still live in _session.System and _session.MeritFunction
    /// — write it to disk so the user can inspect or build on it later.
    /// Returns the path written, or null if the save failed.
    ///
    /// Save location:
    ///   • If a current file is open, alongside it with a suffix like
    ///     "<base>_split_rejected_<yyyyMMdd_HHmmss>.lhlt".
    ///   • Otherwise, the user's Documents\LensHH-LT\RejectedSplits\
    ///     folder with a timestamped name.
    /// </summary>
    private string? SaveRejectedSplit(double initialMerit, double finalMerit)
    {
        try
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string baseName, dir;

            if (!string.IsNullOrEmpty(_session.FilePath))
            {
                dir = Path.GetDirectoryName(_session.FilePath!) ?? string.Empty;
                baseName = Path.GetFileNameWithoutExtension(_session.FilePath!);
            }
            else
            {
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                dir = Path.Combine(docs, "LensHH-LT", "RejectedSplits");
                Directory.CreateDirectory(dir);
                baseName = string.IsNullOrEmpty(_session.CurrentFileName)
                    ? "Untitled"
                    : _session.CurrentFileName!;
            }

            string fileName = $"{baseName}_split_rejected_{timestamp}.lhlt";
            string fullPath = Path.Combine(dir, fileName);

            LhltWriter.Write(_session.System, fullPath, _session.MeritFunction, _session.ConfigEditor);
            return fullPath;
        }
        catch
        {
            return null;
        }
    }
}
