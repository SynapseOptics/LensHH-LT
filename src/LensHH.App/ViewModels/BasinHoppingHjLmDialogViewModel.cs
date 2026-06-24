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

public partial class BasinHoppingVariableRow : ObservableObject
{
    public string Description { get; }
    public string StartValue { get; }

    [ObservableProperty] private string _currentValue;
    [ObservableProperty] private string _delta;

    public BasinHoppingVariableRow(string description, double startValue)
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

public partial class BasinHoppingGlassRow : ObservableObject
{
    public int SurfaceIndex { get; }
    public string StartGlass { get; }
    [ObservableProperty] private string _currentGlass;

    public BasinHoppingGlassRow(int surfaceIndex, string startGlass)
    {
        SurfaceIndex = surfaceIndex;
        StartGlass = startGlass;
        _currentGlass = startGlass;
    }
}

public partial class BasinHoppingChainRow : ObservableObject
{
    public int Chain { get; }
    [ObservableProperty] private string _hops = "0";
    [ObservableProperty] private string _best = "—";
    [ObservableProperty] private string _acceptedRejected = "0 / 0";
    [ObservableProperty] private bool _leads;   // currently holds the global best

    public string LeadMarker => Leads ? "◄ best" : "";
    partial void OnLeadsChanged(bool value) => OnPropertyChanged(nameof(LeadMarker));

    public BasinHoppingChainRow(int chain) => Chain = chain;
}

public partial class BasinHoppingHjLmDialogViewModel : ObservableObject
{
    private readonly GuiSession _session;
    private CancellationTokenSource? _cts;
    private BasinHoppingOptimizerBatch? _batch;   // set for multi-chain runs; BestChain feeds the completion table
    
    /// <summary>Cancel a running operation when the dialog closes (else worker threads run to completion).</summary>
    public void CancelRun() => _cts?.Cancel();
    private readonly Stopwatch _stopwatch = new();

    // ── Settings ──
    [ObservableProperty] private int _maxHops = 2000;
    [ObservableProperty] private int _lmIterationsPerHop = 4000;
    [ObservableProperty] private int _hjStepsPerHop = 30;
    [ObservableProperty] private double _initialPerturbSigma = 0.001;
    [ObservableProperty] private bool _constrainedOnly = false;
    [ObservableProperty] private bool _useBroydenUpdate = true;
    [ObservableProperty] private bool _glassSubstitution = false;
    // On a glass swap, rescale the element's curvatures by (n_old-1)/(n_new-1) to
    // preserve power. Default OFF for basin hopping — testing showed the per-swap
    // curvature jump over-perturbs its small-step trajectory and hurts results.
    [ObservableProperty] private bool _rescaleOnGlassSwap = false;
    [ObservableProperty] private int _seed = 1234;

    // Parallel independent chains: each a full hop walk from its own perturbation seed;
    // the single global best is returned. 0 = auto (physical core count). 1 = the classic
    // single chain (full live per-variable trace). >1 fills the cores and explores N basins
    // at once — much better global search, the whole point of the parallel batch.
    [ObservableProperty] private int _parallelChains = 0;

    /// <summary>The chain count that will actually run (auto-resolves 0 → physical cores).</summary>
    public int ResolvedChains => ParallelChains <= 0
        ? LensHH.Core.Optimization.CpuInfo.PhysicalCoreCount() : ParallelChains;

    /// <summary>Hint shown next to the chains box, e.g. "auto → 8 physical cores".</summary>
    public string ParallelChainsHint => ParallelChains <= 0
        ? $"auto → {LensHH.Core.Optimization.CpuInfo.PhysicalCoreCount()} physical cores"
        : (ParallelChains == 1 ? "single chain (live per-variable trace)" : $"{ParallelChains} parallel chains");

    partial void OnParallelChainsChanged(int value)
    {
        OnPropertyChanged(nameof(ResolvedChains));
        OnPropertyChanged(nameof(ParallelChainsHint));
    }

    // ── Advanced — engine + derivative selection (was orange DEV banner in
    //    ≤1.0.114; moved into a collapsed Advanced expander for 1.0.115).
    //    Index 0 = C# (CSharp), 1 = C++ (Native). Default flipped to C++
    //    Native on 1.0.115 — the bedrock-validated path.
    [ObservableProperty] private int _engineModeIndex = 1;
    //    Index 0 = Finite Difference, 1 = Analytic (forward-mode Dual<W> AD).
    //    Only effective when EngineModeIndex == 1. Default flipped to Analytic.
    [ObservableProperty] private int _derivativeModeIndex = 1;
    public IReadOnlyList<string> EngineModeOptions { get; } =
        new[] { "C# (CSharp)", "C++ (Native)" };
    public IReadOnlyList<string> DerivativeModeOptions { get; } =
        new[] { "Finite Difference", "Analytic" };

    // No-improvement watchdog: terminate the run if best merit hasn't improved
    // within this many seconds since the last improvement. 0 = disabled. When
    // ON, MaxHops effectively becomes a safety cap and the watchdog is the
    // practical termination criterion. UI toggle pairs with the value box —
    // toggling Off forces the value to 0 so the engine sees it as disabled.
    [ObservableProperty] private bool _noImprovementEnabled = false;
    [ObservableProperty] private double _noImprovementTimeoutSeconds = 600.0;

    // ── Glass catalogs (mirrors SplitElementDialogViewModel pattern) ──
    public ObservableCollection<CatalogCheckItem> Catalogs { get; } = new();
    public ObservableCollection<string> GlassSourceOptions { get; } = new();
    [ObservableProperty] private int _selectedGlassSourceIndex;
    [ObservableProperty] private bool _onlyPreferred = true;
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
    [ObservableProperty] private string _bestMeritText = "";
    [ObservableProperty] private string _elapsedText = "0.0 s";
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private string _hopText = "";
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private int _acceptedCount;
    [ObservableProperty] private int _rejectedCount;
    [ObservableProperty] private int _glassSwapsTotal;

    /// <summary>Folder to write each chain's final design (.lhlt) into on completion.
    /// Empty = don't export. Set via the "Browse…" folder picker in the dialog.</summary>
    [ObservableProperty] private string _saveChainsFolder = "";

    private const string DefaultHelpText = "Hover over a setting for a description.";
    [ObservableProperty] private string _helpText = DefaultHelpText;

    public void SetHelp(string? text)
        => HelpText = string.IsNullOrEmpty(text) ? DefaultHelpText : text!;

    // ── Tables ──
    public ObservableCollection<BasinHoppingVariableRow> VariableRows { get; } = new();
    public ObservableCollection<BasinHoppingGlassRow> GlassRows { get; } = new();

    // Live per-chain status for a parallel run (empty for single-chain). Drives the "Chains" tab.
    public ObservableCollection<BasinHoppingChainRow> ChainRows { get; } = new();
    [ObservableProperty] private bool _isMultiChain;
    private double _bestEverLogged = double.MaxValue;   // for "new global best" log lines

    public bool Accepted { get; private set; }

    public BasinHoppingHjLmDialogViewModel(GuiSession session)
    {
        _session = session;

        // Loaded-catalog checkboxes (preselect what the system already references).
        foreach (var cat in session.GlassCatalog.LoadedCatalogs)
        {
            bool check = session.System.GlassCatalogs.Count == 0 ||
                         session.System.GlassCatalogs.Contains(cat);
            Catalogs.Add(new CatalogCheckItem(cat, check));
        }

        // Filtered catalogs first, then "Loaded catalogs (select below)" — same as Split.
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
        VariableRows.Clear();
        GlassRows.Clear();
        Accepted = false;
        _batch = null;
        ChainRows.Clear();
        IsMultiChain = false;
        _bestEverLogged = double.MaxValue;
        _stopwatch.Restart();
        _cts = new CancellationTokenSource();

        var timer = new System.Timers.Timer(500);
        timer.Elapsed += (_, _) =>
            Dispatcher.UIThread.Post(() => ElapsedText = $"{_stopwatch.Elapsed.TotalSeconds:F1} s");
        timer.Start();

        // Determine glass source (mirrors SplitElement)
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

            // Sanity check the pool size before launching
            bool usePreferred = !useFiltered && OnlyPreferred;
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
                AppendLog("Disable glass substitution or select a different source.");
                StatusText = "No glasses found — aborted";
                IsRunning = false;
                timer.Stop(); timer.Dispose(); _stopwatch.Stop();
                return;
            }
            AppendLog($"Glass source: {string.Join(", ", glassCatalogs)} ({totalGlasses} glasses)");
        }

        var settings = new BasinHoppingSettings
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
            ParallelChains = ParallelChains,
            NoImprovementTimeoutSeconds = NoImprovementEnabled ? NoImprovementTimeoutSeconds : 0.0,
        };

        var ct = _cts.Token;
        BasinHoppingResult? result = null;

        try
        {
            // Initial merit display
            var evaluator = new MeritFunctionEvaluator(
                _session.System, _session.GlassCatalog, configEditor: _session.ConfigEditor);
            double initialMerit = evaluator.Evaluate(_session.MeritFunction);
            InitialMeritText = initialMerit.ToString("E6");
            BestMeritText = InitialMeritText;
            PhaseText = "Starting...";
            StatusText = $"Initial merit: {initialMerit:E6}";

            int resolvedChains = ResolvedChains;
            AppendLog(resolvedChains == 1
                ? "Single chain (live per-variable trace)."
                : $"{resolvedChains} parallel chains" + (ParallelChains <= 0 ? " (auto = physical cores)" : "")
                  + " — exploring " + resolvedChains + " basins at once; best design loaded at completion.");

            if (resolvedChains == 1)
            {
                // ── Single chain: full live per-variable / per-glass trace (unchanged). ──
                bool variableRowsPopulated = false;
                bool eligibilityLogged = false;
                BasinHoppingOptimizer? optimizer = null;

                optimizer = new BasinHoppingOptimizer(
                    _session.System, _session.MeritFunction,
                    _session.GlassCatalog, _session.ConfigEditor)
                {
                    Settings = settings,
                    // Phase 10a — DEV engine selection from the dialog.
                    EngineMode = (EngineModeIndex == 1) ? EngineMode.Native : EngineMode.CSharp,
                    NativeDerivativeMode = (DerivativeModeIndex == 1)
                        ? LensHH.Core.NativeInterop.MeritDerivativeMode.Analytic
                        : LensHH.Core.NativeInterop.MeritDerivativeMode.FiniteDifference,
                    FilteredCatalogSearchPaths = filteredDir != null ? new[] { filteredDir } : Array.Empty<string>(),
                    OnProgress = p => Dispatcher.UIThread.Post(() =>
                    {
                        if (!variableRowsPopulated && optimizer!.Variables.Count > 0)
                        {
                            variableRowsPopulated = true;
                            for (int i = 0; i < optimizer.Variables.Count; i++)
                                VariableRows.Add(new BasinHoppingVariableRow(
                                    optimizer.Variables[i].Description,
                                    optimizer.StartingValues[i]));
                            foreach (var kv in optimizer.StartingGlasses)
                                GlassRows.Add(new BasinHoppingGlassRow(kv.Key, kv.Value));
                        }

                        if (!eligibilityLogged && GlassSubstitution)
                        {
                            eligibilityLogged = true;
                            LogGlassEligibility(optimizer!.ConfiguredSubstituteSurfaces, optimizer.FixedGlassSurfacesSkipped);
                        }

                        AcceptedCount = p.Accepted;
                        RejectedCount = p.Rejected;
                        GlassSwapsTotal += p.GlassSwaps;
                        BestMeritText = p.BestMerit.ToString("E6");
                        HopText = $"Hop {p.Hop + 1}/{p.MaxHops}";
                        PhaseText = $"{p.Phase} (acc {p.Accepted} / rej {p.Rejected})";
                        StatusText = $"merit={p.CurrentMerit:E5}  best={p.BestMerit:E5}";
                        ProgressPercent = 100.0 * (p.Hop + 1) / Math.Max(1, p.MaxHops);

                        if (p.CurrentValues.Length == VariableRows.Count)
                            for (int i = 0; i < VariableRows.Count; i++)
                                VariableRows[i].Update(p.CurrentValues[i], optimizer.StartingValues[i]);

                        foreach (var row in GlassRows)
                            if (p.CurrentGlasses.TryGetValue(row.SurfaceIndex, out var glass))
                                row.CurrentGlass = glass;

                        string tag = p.Phase == "Accept" ? "ACC" : "rej";
                        string extra = p.GlassSwaps > 0 ? $"  glass-swaps={p.GlassSwaps}" : "";
                        AppendLog($"Hop {p.Hop + 1,3} [{tag}] merit={p.CurrentMerit:E5}  best={p.BestMerit:E5}{extra}");
                    })
                };

                result = await Task.Run(() => optimizer.Optimize(ct), ct);
            }
            else
            {
                // ── N parallel chains. The useful live view is the per-chain table (each
                //    chain's hops + running best, leader highlighted) via OnChainsProgress;
                //    the headline shows the global best, which only ever decreases. Per-chain
                //    "Accept/Reject" flicker is intentionally NOT shown — across N chains it
                //    is noise. Per-variable + per-glass tables fill at completion from the
                //    winning chain (live per-variable tracking is ambiguous across N walks). ──
                IsMultiChain = true;
                PhaseText = $"{resolvedChains} chains running";
                var headThrottle = System.Diagnostics.Stopwatch.StartNew();
                var tableThrottle = System.Diagnostics.Stopwatch.StartNew();

                _batch = new BasinHoppingOptimizerBatch(
                    _session.System, _session.MeritFunction,
                    _session.GlassCatalog, _session.ConfigEditor)
                {
                    Settings = settings,
                    EngineMode = (EngineModeIndex == 1) ? EngineMode.Native : EngineMode.CSharp,
                    NativeDerivativeMode = (DerivativeModeIndex == 1)
                        ? LensHH.Core.NativeInterop.MeritDerivativeMode.Analytic
                        : LensHH.Core.NativeInterop.MeritDerivativeMode.FiniteDifference,
                    FilteredCatalogSearchPaths = filteredDir != null ? new[] { filteredDir } : Array.Empty<string>(),
                    OnProgress = p => Dispatcher.UIThread.Post(() =>
                    {
                        // Log every genuine global-best improvement (not throttled — these are
                        // the events the user cares about), and keep the headline merit current.
                        if (p.BestMerit < _bestEverLogged - Math.Abs(_bestEverLogged) * 1e-9 - 1e-15)
                        {
                            _bestEverLogged = p.BestMerit;
                            BestMeritText = p.BestMerit.ToString("E6");
                            AppendLog($"new global best  {p.BestMerit:E6}   (hop {p.Hop})");
                        }
                        if (headThrottle.ElapsedMilliseconds < 200) return;
                        headThrottle.Restart();
                        AcceptedCount = p.Accepted;
                        RejectedCount = p.Rejected;
                        GlassSwapsTotal = p.GlassSwaps;          // already aggregated across chains
                        HopText = $"{p.Hop} hops / {resolvedChains} chains";
                        StatusText = $"global best {p.BestMerit:E5}   ({p.Accepted} acc / {p.Rejected} rej)";
                        ProgressPercent = 100.0 * p.Hop / Math.Max(1, p.MaxHops);
                    }),
                    OnChainsProgress = snap => Dispatcher.UIThread.Post(() =>
                    {
                        if (ChainRows.Count == 0)
                            for (int k = 0; k < snap.Length; k++) ChainRows.Add(new BasinHoppingChainRow(k));
                        if (tableThrottle.ElapsedMilliseconds < 200) return;
                        tableThrottle.Restart();
                        for (int k = 0; k < snap.Length && k < ChainRows.Count; k++)
                        {
                            var r = ChainRows[k]; var s = snap[k];
                            r.Hops = s.Hops.ToString();
                            r.Best = double.IsNaN(s.Best) ? "—" : s.Best.ToString("E6");
                            r.AcceptedRejected = $"{s.Accepted} / {s.Rejected}";
                            r.Leads = s.Leads;
                        }
                    })
                };

                result = await Task.Run(() => _batch.Optimize(ct), ct);
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("Cancelled by user.");
        }
        catch (Exception ex)
        {
            AppendLog($"Error: {ex.Message}");
            StatusText = $"Error: {ex.Message}";
        }

        timer.Stop();
        timer.Dispose();
        _stopwatch.Stop();
        IsRunning = false;
        IsComplete = true;

        if (result != null)
        {
            // Re-evaluate to match what other panels show
            LensHH.Core.Analysis.SemiDiameterSolver.Solve(_session.System, _session.GlassCatalog);
            var freshEval = new MeritFunctionEvaluator(
                _session.System, _session.GlassCatalog, configEditor: _session.ConfigEditor);
            double finalMerit = freshEval.Evaluate(_session.MeritFunction);

            // Multi-chain: fill the variable / glass tables from the winning chain now
            // (they weren't tracked live). _session.System already holds the global best.
            var best = _batch?.BestChain;
            if (best != null && VariableRows.Count == 0)
            {
                if (GlassSubstitution)
                    LogGlassEligibility(best.ConfiguredSubstituteSurfaces, best.FixedGlassSurfacesSkipped);
                for (int i = 0; i < best.Variables.Count; i++)
                {
                    var row = new BasinHoppingVariableRow(best.Variables[i].Description, best.StartingValues[i]);
                    row.Update(best.Variables[i].GetValue(_session.System), best.StartingValues[i]);
                    VariableRows.Add(row);
                }
                foreach (var kv in best.StartingGlasses)
                {
                    var gr = new BasinHoppingGlassRow(kv.Key, kv.Value);
                    if (kv.Key >= 0 && kv.Key < _session.System.Surfaces.Count)
                        gr.CurrentGlass = _session.System.Surfaces[kv.Key].Material ?? kv.Value;
                    GlassRows.Add(gr);
                }
            }

            BestMeritText = finalMerit.ToString("E6");
            StatusText = result.Cancelled ? "Cancelled" : "Complete";
            string chainNote = _batch != null ? $"{_batch.ChainsRun} chains, " : "";
            AppendLog($"Merit: {result.InitialMerit:E6} -> {finalMerit:E6} in {_stopwatch.Elapsed.TotalSeconds:F1}s " +
                $"({chainNote}{result.Accepted} accepted / {result.Rejected} rejected, {result.GlassSwaps} glass swaps)");

            if (!string.IsNullOrWhiteSpace(SaveChainsFolder))
            {
                try
                {
                    string baseName = string.IsNullOrWhiteSpace(_session.System.Title) ? "basin" : _session.System.Title;
                    IReadOnlyList<BasinHoppingOptimizerBatch.ChainDesign> chains =
                        (_batch != null && _batch.ChainResults.Count > 0)
                            ? _batch.ChainResults
                            // Single chain: the one final design (already live in the session).
                            : new[] { new BasinHoppingOptimizerBatch.ChainDesign(0, _session.System.DeepClone(), finalMerit, true) };
                    var paths = LensHH.Core.IO.ChainResultWriter.SaveChains(
                        chains, SaveChainsFolder, baseName, _session.MeritFunction, _session.ConfigEditor);
                    AppendLog($"Saved {paths.Count} chain design(s) to {SaveChainsFolder}");
                }
                catch (Exception ex) { AppendLog($"Chain save failed: {ex.Message}"); }
            }
        }
    }

    private void LogGlassEligibility(int eligible, int skipped)
    {
        int total = eligible + skipped;
        if (total == 0)
            AppendLog("Substitution-eligible elements: 0 (no glass surfaces in system)");
        else if (skipped == 0)
            AppendLog($"Substitution-eligible elements: {eligible} of {total} (every glass has ≥1 active variable)");
        else
        {
            string verb = skipped == 1 ? "has" : "have";
            AppendLog($"Substitution-eligible elements: {eligible} of {total} ({skipped} fixed glass {verb} no active variable — not eligible)");
        }
    }

    [RelayCommand]
    public void Stop() => _cts?.Cancel();

    public void Accept() => Accepted = true;

    [RelayCommand]
    public async Task CopyLog()
    {
        if (string.IsNullOrEmpty(LogText)) return;
        var clipboard = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow?.Clipboard : null;
        if (clipboard != null)
            await clipboard.SetTextAsync(LogText);
    }

    [RelayCommand]
    public void ClearLog() => LogText = "";

    private void AppendLog(string line)
    {
        LogText += line + "\n";
    }
}
