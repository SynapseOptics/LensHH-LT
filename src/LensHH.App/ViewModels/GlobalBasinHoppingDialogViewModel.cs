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

/// <summary>One chain's row in the live Chains tab / final apply-gallery of the global run.</summary>
public partial class GlobalBasinHoppingChainRow : ObservableObject
{
    public int Chain { get; }
    [ObservableProperty] private string _hops = "0";
    [ObservableProperty] private string _best = "—";
    [ObservableProperty] private string _restarts = "0";
    [ObservableProperty] private bool _leads;            // currently holds the global best
    public string LeadMarker => Leads ? "◄ best" : "";
    partial void OnLeadsChanged(bool value) => OnPropertyChanged(nameof(LeadMarker));
    public GlobalBasinHoppingChainRow(int chain) => Chain = chain;
}

public partial class GlobalBasinHoppingDialogViewModel : ObservableObject
{
    private readonly GuiSession _session;
    private CancellationTokenSource? _cts;
    private readonly Stopwatch _stopwatch = new();
    private GlobalBasinHoppingResult? _lastResult;   // for "apply selected chain"

    /// <summary>Cancel a running operation when the dialog closes.</summary>
    public void CancelRun() => _cts?.Cancel();

    // ── Editable per-chain HJ-LM settings ──
    [ObservableProperty] private int _maxHops = 2000;
    [ObservableProperty] private int _lmIterationsPerHop = 4000;
    [ObservableProperty] private int _hjStepsPerHop = 30;
    [ObservableProperty] private double _initialPerturbSigma = 0.001;
    [ObservableProperty] private bool _constrainedOnly = false;
    [ObservableProperty] private bool _useBroydenUpdate = true;
    [ObservableProperty] private bool _glassSubstitution = false;
    [ObservableProperty] private bool _rescaleOnGlassSwap = false;
    [ObservableProperty] private int _seed = 1234;

    // ── Mandatory no-improvement watchdog (toggle is locked ON; only the timeout is editable) ──
    [ObservableProperty] private double _noImprovementTimeoutSeconds = GlobalBasinHoppingSettings.DefaultChainTimeoutSeconds;
    // ── Global wall-clock budget (minutes) ──
    [ObservableProperty] private double _globalTimeoutMinutes = GlobalBasinHoppingSettings.DefaultGlobalTimeoutMinutes;

    /// <summary>Chains are fixed to physical cores — shown read-only.</summary>
    public string ChainsHint => $"auto → {LensHH.Core.Optimization.CpuInfo.PhysicalCoreCount()} physical cores (fixed)";

    // ── Advanced — engine + derivative (defaults: C++ Native + Analytic) ──
    [ObservableProperty] private int _engineModeIndex = 1;
    [ObservableProperty] private int _derivativeModeIndex = 1;
    public IReadOnlyList<string> EngineModeOptions { get; } = new[] { "C# (CSharp)", "C++ (Native)" };
    public IReadOnlyList<string> DerivativeModeOptions { get; } = new[] { "Finite Difference", "Analytic" };

    // ── Glass catalogs (same pattern as the basin dialog) ──
    public ObservableCollection<CatalogCheckItem> Catalogs { get; } = new();
    public ObservableCollection<string> GlassSourceOptions { get; } = new();
    [ObservableProperty] private int _selectedGlassSourceIndex;
    [ObservableProperty] private bool _onlyPreferred = true;
    private readonly List<string> _filteredCatalogNames = new();
    public bool ShowLoadedCatalogs => SelectedGlassSourceIndex >= _filteredCatalogNames.Count;
    partial void OnSelectedGlassSourceIndexChanged(int value) => OnPropertyChanged(nameof(ShowLoadedCatalogs));

    // ── Status ──
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isComplete;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _phaseText = "";
    [ObservableProperty] private string _initialMeritText = "";
    [ObservableProperty] private string _bestMeritText = "";
    [ObservableProperty] private string _elapsedText = "0.0 s";
    [ObservableProperty] private string _hopText = "";
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private double _progressPercent;

    [ObservableProperty] private string _saveChainsFolder = "";

    private const string DefaultHelpText = "Hover over a setting for a description.";
    [ObservableProperty] private string _helpText = DefaultHelpText;
    public void SetHelp(string? text) => HelpText = string.IsNullOrEmpty(text) ? DefaultHelpText : text!;

    // ── Chains tab (live) doubles as the completion apply-gallery ──
    public ObservableCollection<GlobalBasinHoppingChainRow> ChainRows { get; } = new();
    [ObservableProperty] private GlobalBasinHoppingChainRow? _selectedChain;
    private double _bestEverLogged = double.MaxValue;

    public bool Accepted { get; private set; }

    public GlobalBasinHoppingDialogViewModel(GuiSession session)
    {
        _session = session;
        foreach (var cat in session.GlassCatalog.LoadedCatalogs)
        {
            bool check = session.System.GlassCatalogs.Count == 0 || session.System.GlassCatalogs.Contains(cat);
            Catalogs.Add(new CatalogCheckItem(cat, check));
        }
        var filteredDir = GlassSubstitutionViewModel.FindFilteredCatalogFolder();
        if (filteredDir != null)
            foreach (var file in System.IO.Directory.GetFiles(filteredDir, "*.agf"))
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(file);
                _filteredCatalogNames.Add(name);
                GlassSourceOptions.Add(name);
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
        ChainRows.Clear();
        SelectedChain = null;
        _lastResult = null;
        Accepted = false;
        _bestEverLogged = double.MaxValue;
        _stopwatch.Restart();
        _cts = new CancellationTokenSource();

        double budgetSec = GlobalTimeoutMinutes > 0 ? GlobalTimeoutMinutes * 60.0 : 0.0;
        var timer = new System.Timers.Timer(500);
        timer.Elapsed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            ElapsedText = $"{_stopwatch.Elapsed.TotalSeconds:F1} s";
            if (budgetSec > 0) ProgressPercent = Math.Min(100.0, 100.0 * _stopwatch.Elapsed.TotalSeconds / budgetSec);
        });
        timer.Start();

        // Resolve the glass source (mirrors the basin dialog).
        var glassCatalogs = new List<string>();
        bool useFiltered = SelectedGlassSourceIndex < _filteredCatalogNames.Count;
        var filteredDir = GlassSubstitutionViewModel.FindFilteredCatalogFolder();
        if (GlassSubstitution)
        {
            if (useFiltered)
            {
                string catName = _filteredCatalogNames[SelectedGlassSourceIndex];
                if (filteredDir != null)
                {
                    var agfPath = System.IO.Path.Combine(filteredDir, catName + ".agf");
                    if (!System.IO.File.Exists(agfPath)) agfPath = System.IO.Path.Combine(filteredDir, catName + ".AGF");
                    if (System.IO.File.Exists(agfPath)) _session.GlassCatalog.LoadCatalog(agfPath);
                }
                glassCatalogs.Add(catName);
            }
            else glassCatalogs = Catalogs.Where(c => c.IsChecked).Select(c => c.Name).ToList();

            bool usePreferred = !useFiltered && OnlyPreferred;
            int totalGlasses = 0;
            foreach (var cat in glassCatalogs)
            {
                var glasses = _session.GlassCatalog.GetGlassesInCatalog(cat);
                totalGlasses += usePreferred ? glasses.Count(g => g.Status <= 1) : glasses.Count;
            }
            if (totalGlasses == 0)
            {
                AppendLog($"ERROR: No glasses found in catalog(s): {string.Join(", ", glassCatalogs)}");
                StatusText = "No glasses found — aborted";
                IsRunning = false; timer.Stop(); timer.Dispose(); _stopwatch.Stop();
                return;
            }
            AppendLog($"Glass source: {string.Join(", ", glassCatalogs)} ({totalGlasses} glasses)");
        }

        var gs = new GlobalBasinHoppingSettings
        {
            GlobalTimeoutMinutes = GlobalTimeoutMinutes,
            Chain = new BasinHoppingSettings
            {
                MaxHops = MaxHops,
                LmIterationsPerHop = LmIterationsPerHop,
                HjStepsPerHop = HjStepsPerHop,
                InitialPerturbSigma = InitialPerturbSigma,
                UseBroydenUpdate = UseBroydenUpdate,
                ConstrainedOnly = ConstrainedOnly,
                GlassSubstitution = GlassSubstitution,
                RescaleCurvatureOnGlassSwap = RescaleOnGlassSwap,
                GlassCatalogs = glassCatalogs,
                OnlyPreferred = !useFiltered && OnlyPreferred,
                Seed = Seed,
                NoImprovementTimeoutSeconds = NoImprovementTimeoutSeconds,   // mandatory watchdog
            },
        };

        var ct = _cts.Token;
        GlobalBasinHoppingResult? result = null;
        var tableThrottle = Stopwatch.StartNew();

        try
        {
            var evaluator = new MeritFunctionEvaluator(_session.System, _session.GlassCatalog, configEditor: _session.ConfigEditor);
            double initialMerit = evaluator.Evaluate(_session.MeritFunction);
            InitialMeritText = initialMerit.ToString("E6");
            BestMeritText = InitialMeritText;
            int cores = LensHH.Core.Optimization.CpuInfo.PhysicalCoreCount();
            PhaseText = $"{cores} chains running";
            StatusText = $"Initial merit: {initialMerit:E6}";
            AppendLog($"{cores} chains (physical cores). No-improvement timeout {NoImprovementTimeoutSeconds:F0}s, global limit {GlobalTimeoutMinutes:F0} min.");
            AppendLog("Chains restart from the best of the OTHER chains when they stall — cooperative deep dive.");

            var optimizer = new GlobalBasinHoppingOptimizer(
                _session.System, _session.MeritFunction, _session.GlassCatalog, _session.ConfigEditor)
            {
                Settings = gs,
                EngineMode = (EngineModeIndex == 1) ? EngineMode.Native : EngineMode.CSharp,
                NativeDerivativeMode = (DerivativeModeIndex == 1)
                    ? LensHH.Core.NativeInterop.MeritDerivativeMode.Analytic
                    : LensHH.Core.NativeInterop.MeritDerivativeMode.FiniteDifference,
                FilteredCatalogSearchPaths = filteredDir != null ? new[] { filteredDir } : Array.Empty<string>(),
                OnChainsProgress = snap => Dispatcher.UIThread.Post(() =>
                {
                    if (ChainRows.Count == 0)
                        for (int k = 0; k < snap.Length; k++) ChainRows.Add(new GlobalBasinHoppingChainRow(k));

                    double gBest = double.NaN; long totHops = 0; int totRestarts = 0;
                    foreach (var c in snap)
                    {
                        totHops += c.Hops; totRestarts += c.Restarts;
                        if (!double.IsNaN(c.Best) && (double.IsNaN(gBest) || c.Best < gBest)) gBest = c.Best;
                    }
                    if (!double.IsNaN(gBest) && gBest < _bestEverLogged - Math.Abs(_bestEverLogged) * 1e-9 - 1e-15)
                    {
                        _bestEverLogged = gBest;
                        BestMeritText = gBest.ToString("E6");
                        AppendLog($"new global best  {gBest:E6}   ({totHops} hops, {totRestarts} restarts)");
                    }
                    if (tableThrottle.ElapsedMilliseconds < 200) return;
                    tableThrottle.Restart();
                    HopText = $"{totHops} hops / {snap.Length} chains / {totRestarts} restarts";
                    StatusText = double.IsNaN(gBest) ? "running…" : $"global best {gBest:E5}";
                    for (int k = 0; k < snap.Length && k < ChainRows.Count; k++)
                    {
                        var r = ChainRows[k]; var s = snap[k];
                        r.Hops = s.Hops.ToString();
                        r.Best = double.IsNaN(s.Best) ? "—" : s.Best.ToString("E6");
                        r.Restarts = s.Restarts.ToString();
                        r.Leads = s.Leads;
                    }
                }),
            };

            result = await Task.Run(() => optimizer.Optimize(ct), ct);
        }
        catch (OperationCanceledException) { AppendLog("Cancelled by user."); }
        catch (Exception ex) { AppendLog($"Error: {ex.Message}"); StatusText = $"Error: {ex.Message}"; }

        timer.Stop(); timer.Dispose(); _stopwatch.Stop();
        IsRunning = false;
        IsComplete = true;
        ProgressPercent = 100;

        if (result != null)
        {
            _lastResult = result;
            LensHH.Core.Analysis.SemiDiameterSolver.Solve(_session.System, _session.GlassCatalog);
            var freshEval = new MeritFunctionEvaluator(_session.System, _session.GlassCatalog, configEditor: _session.ConfigEditor);
            double finalMerit = freshEval.Evaluate(_session.MeritFunction);
            BestMeritText = finalMerit.ToString("E6");
            StatusText = result.TimedOut ? "Global time limit reached" : result.Cancelled ? "Stopped by user" : "Complete";
            string why = result.TimedOut ? "global time limit" : result.Cancelled ? "stopped" : "completed";
            AppendLog($"Merit {result.InitialMerit:E6} -> {finalMerit:E6} in {_stopwatch.Elapsed.TotalSeconds:F1}s " +
                $"({result.ChainsRun} chains, {result.TotalRestarts} restarts, {result.TotalHops} hops, {why}).");

            // Final per-chain bests → gallery (sorted best-first; global best leads + auto-selected).
            ChainRows.Clear();
            foreach (var c in result.ChainResults.OrderBy(c => c.Merit))
            {
                var row = new GlobalBasinHoppingChainRow(c.ChainIndex)
                {
                    Best = c.Merit.ToString("E6"),
                    Leads = c.IsBest,
                };
                ChainRows.Add(row);
                if (c.IsBest) SelectedChain = row;
            }
            AppendLog("Select a chain row and click 'Apply Selected' to load a different design; OK keeps the global best.");

            SaveChainsIfRequested(finalMerit);
        }
    }

    /// <summary>Apply the selected chain's design to the session system (default is the global best).</summary>
    [RelayCommand]
    public void ApplySelected()
    {
        if (_lastResult == null || SelectedChain == null) return;
        var pick = _lastResult.ChainResults.FirstOrDefault(c => c.ChainIndex == SelectedChain.Chain);
        if (pick.System == null) return;
        _session.System.CopyFrom(pick.System);
        LensHH.Core.Analysis.SemiDiameterSolver.Solve(_session.System, _session.GlassCatalog);
        var eval = new MeritFunctionEvaluator(_session.System, _session.GlassCatalog, configEditor: _session.ConfigEditor);
        double m = eval.Evaluate(_session.MeritFunction);
        BestMeritText = m.ToString("E6");
        AppendLog($"Applied chain {SelectedChain.Chain}'s design (merit {m:E6}).");
    }

    private void SaveChainsIfRequested(double fallbackMerit)
    {
        if (string.IsNullOrWhiteSpace(SaveChainsFolder) || _lastResult == null) return;
        try
        {
            string baseName = string.IsNullOrWhiteSpace(_session.System.Title) ? "global_basin" : _session.System.Title;
            var paths = LensHH.Core.IO.ChainResultWriter.SaveChains(
                _lastResult.ChainResults, SaveChainsFolder, baseName, _session.MeritFunction, _session.ConfigEditor);
            AppendLog($"Saved {paths.Count} chain design(s) to {SaveChainsFolder}");
        }
        catch (Exception ex) { AppendLog($"Chain save failed: {ex.Message}"); }
    }

    [RelayCommand] public void Stop() => _cts?.Cancel();
    public void Accept() => Accepted = true;

    [RelayCommand]
    public async Task CopyLog()
    {
        if (string.IsNullOrEmpty(LogText)) return;
        var clipboard = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow?.Clipboard : null;
        if (clipboard != null) await clipboard.SetTextAsync(LogText);
    }

    [RelayCommand] public void ClearLog() => LogText = "";
    private void AppendLog(string line) => LogText += line + "\n";
}
