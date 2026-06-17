using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LensHH.App.Session;
using LensHH.Core.IO;
using LensHH.Core.MeritFunction;
using LensHH.Core.NativeInterop;
using LensHH.Core.Optimization;

namespace LensHH.App.ViewModels
{
    /// <summary>
    /// View-model for the DE starting-design pipeline dialog: Differential-Evolution seed search
    /// (GPU-resident when a CUDA device is present — population fills the device — else CPU) with
    /// the focus+EFL conditioner, then Local-LM or Multistart-LM polish of the best candidates.
    /// ALL pre-polish seeds are saved automatically; the polished designs are shown as a gallery.
    /// </summary>
    public partial class DePipelineDialogViewModel : ObservableObject
    {
        private readonly GuiSession _session;
        private CancellationTokenSource? _cts;
        private readonly System.Diagnostics.Stopwatch _stopwatch = new();
        private DispatcherTimer? _elapsedTimer;
        private DePipelineResult? _lastResult;

        public DePipelineDialogViewModel(GuiSession session)
        {
            _session = session;
            Cards.CollectionChanged += OnCardsChanged;
            // Default output under the system title so the auto-save is discoverable.
            OutputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "LensHH-LT", "de_pipeline");
            GpuAvailable = GpuResidentDe.IsAvailable;
            UseGpu = GpuAvailable;
        }

        // ── Pipeline settings ──
        [ObservableProperty] private bool _gpuAvailable;
        [ObservableProperty] private bool _useGpu;
        [ObservableProperty] private int _generations = 10000;
        [ObservableProperty] private int _populationSize = 256;   // CPU; GPU auto-fills
        [ObservableProperty] private int _seedsToEmit = 16;
        [ObservableProperty] private int _baseSeed = 1;
        [ObservableProperty] private double _glassSubstitutionProbability = 50.0; // percent

        // §8.5 conditioner inputs.
        [ObservableProperty] private bool _conditionFocusEfl = true;
        [ObservableProperty] private bool _adjustCurvatureForEfl = true;
        [ObservableProperty] private double _eflAdjustTolerance = 0.05; // only adjust EFL within tol×|target| (default 0.05 = ±5%); <=0 = always
        [ObservableProperty] private int _focusSurface = -1;  // -1 = auto (image − 1)
        [ObservableProperty] private int _eflSurface = -1;    // -1 = auto (last powered)

        // Polish.
        [ObservableProperty] private int _polishMethodIndex = 2;  // 0 = none, 1 = LM, 2 = Multistart (default)
        [ObservableProperty] private int _polishCount = 16;
        [ObservableProperty] private int _lmIterations = 4000;

        // Multistart-LM polish tuning (used when PolishMethodIndex == 2). The polish exits on
        // cap-stall; MsTrials is a high backstop so the trial budget isn't the limiter.
        [ObservableProperty] private int _msTrials = 20000;
        [ObservableProperty] private int _msStopAtCapStallBatches = 1;
        [ObservableProperty] private double _msInitialSigma = 0.001;
        [ObservableProperty] private double _msSigmaCap = 0.01;
        [ObservableProperty] private int _msLmIterationsPerTrial = 4000;

        /// <summary>Polish = Multistart LM (index 2) — enables the Multistart-tuning controls.</summary>
        public bool PolishIsMultistart => PolishMethodIndex == 2;
        /// <summary>Polish = Local LM (index 1) — enables the LM-iters control.</summary>
        public bool PolishIsLocalLm => PolishMethodIndex == 1;

        // Re-raise the enable/disable flags whenever the Polish combo changes so the
        // LM-iters control and the Multistart-tuning controls grey out per method.
        partial void OnPolishMethodIndexChanged(int value)
        {
            OnPropertyChanged(nameof(PolishIsMultistart));
            OnPropertyChanged(nameof(PolishIsLocalLm));
        }

        // Polish a previously-saved DE result set instead of running a new search.
        [ObservableProperty] private bool _polishSavedResults;
        [ObservableProperty] private string _polishFolder = "";

        [ObservableProperty] private string _outputDir = "";

        // ── Run state ──
        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private string _statusText = "Ready. Configure and press Run.";
        [ObservableProperty] private string _progressText = string.Empty;
        [ObservableProperty] private double _progressFraction;
        [ObservableProperty] private string _initialMeritText = "—";
        [ObservableProperty] private string _bestMeritText = "—";
        [ObservableProperty] private string _elapsedText = "0.0 s";
        [ObservableProperty] private string _deTimingText = "—";

        /// <summary>Gallery — one card per polished candidate, best-first.</summary>
        public ObservableCollection<GlobalSearchCardViewModel> Cards { get; } = new();

        public bool Accepted { get; private set; }
        public event EventHandler? CloseRequested;

        public bool CanRun => !IsRunning;
        public bool CanSaveAll => !IsRunning && Cards.Count > 0;
        public bool HasResults => Cards.Count > 0;

        partial void OnIsRunningChanged(bool value)
        {
            OnPropertyChanged(nameof(CanRun));
            OnPropertyChanged(nameof(CanSaveAll));
        }

        private void OnCardsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(CanSaveAll));
            OnPropertyChanged(nameof(HasResults));
        }

        [RelayCommand]
        private async Task Run()
        {
            if (IsRunning) return;
            IsRunning = true;
            Cards.Clear();
            ProgressFraction = 0;
            BestMeritText = "—";
            _cts = new CancellationTokenSource();
            StatusText = "Running DE pipeline…";

            _stopwatch.Restart();
            _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _elapsedTimer.Tick += (_, _) => ElapsedText = $"{_stopwatch.Elapsed.TotalSeconds:F1} s";
            _elapsedTimer.Start();

            try
            {
                var ev = new MeritFunctionEvaluator(_session.System, _session.GlassCatalog, _session.ConfigEditor)
                { ParallelEvaluation = true };
                InitialMeritText = ev.Evaluate(_session.MeritFunction).ToString("E4");
            }
            catch { InitialMeritText = "—"; }

            var pset = new DePipelineSettings
            {
                UseGpu = UseGpu,
                PolishCandidateCount = Math.Max(0, PolishCount),
                LmIterations = Math.Max(1, LmIterations),
                PolishMethod = PolishMethodIndex switch
                {
                    0 => DePolishMethod.None,
                    2 => DePolishMethod.MultistartLm,
                    _ => DePolishMethod.LocalLm,
                },
            };
            pset.De.MaxGenerations = Math.Max(1, Generations);
            pset.De.StallGenerations = 0;
            pset.De.PopulationSize = Math.Max(4, PopulationSize);
            pset.De.SeedsToEmit = Math.Max(1, SeedsToEmit);
            pset.De.BaseSeed = BaseSeed;
            pset.De.GlassMutationProbability = GlassSubstitutionProbability / 100.0;
            pset.De.ConditionFocusEfl = ConditionFocusEfl;
            pset.De.AdjustCurvatureForEfl = AdjustCurvatureForEfl;
            pset.De.EflAdjustTolerance = EflAdjustTolerance;
            pset.De.FocusCompensatorSurface = FocusSurface;
            pset.De.EflControlSurface = EflSurface;
            pset.De.FilteredCatalogSearchPaths = GlassSubstitutionViewModel.FindFilteredCatalogFolder() is string dir
                ? new[] { dir } : Array.Empty<string>();

            // Multistart-LM polish tuning from the dialog (used when PolishMethod = MultistartLm).
            // Exits on cap-stall; MsTrials is the high backstop. Other Multistart fields keep their
            // class defaults (LM/trial, glass-sub, Metropolis, …).
            pset.Multistart = new MultistartSettings
            {
                MaxTrials = Math.Max(1, MsTrials),
                StopAtCapStallBatches = Math.Max(0, MsStopAtCapStallBatches),
                InitialSigma = MsInitialSigma,
                SigmaCap = MsSigmaCap,
                LmIterationsPerTrial = Math.Max(1, MsLmIterationsPerTrial),
            };

            var pipeline = new DeOptimizationPipeline(
                _session.System, _session.MeritFunction, _session.GlassCatalog, _session.ConfigEditor)
            {
                Settings = pset,
                OnProgress = p => Dispatcher.UIThread.Post(() =>
                {
                    ProgressText = p.StatusMessage;
                    if (p.MaxRestarts > 0) ProgressFraction = Math.Min(1.0, (double)p.RestartIndex / p.MaxRestarts);
                    if (!double.IsNaN(p.BestMerit)) BestMeritText = p.BestMerit.ToString("E4");
                    if (p.Elapsed > TimeSpan.Zero) DeTimingText = $"{p.Elapsed.TotalSeconds:F1} s ({p.SecondsPerStep:F3} s/gen)";
                    StatusText = p.StatusMessage;
                }),
            };

            DePipelineResult result;
            bool polishingSaved = PolishSavedResults && !string.IsNullOrWhiteSpace(PolishFolder);
            try
            {
                var token = _cts.Token;
                if (polishingSaved)
                {
                    var files = Directory.Exists(PolishFolder) ? Directory.GetFiles(PolishFolder, "*.lhlt") : Array.Empty<string>();
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    if (files.Length == 0) throw new InvalidOperationException($"No .lhlt files in {PolishFolder}");
                    var systems = new System.Collections.Generic.List<LensHH.Core.Models.OpticalSystem>(files.Length);
                    var labels = new System.Collections.Generic.List<string>(files.Length);
                    foreach (var f in files) { systems.Add(LhltReader.Read(f).System); labels.Add(Path.GetFileName(f)); }
                    result = await Task.Run(() => pipeline.PolishExisting(systems, labels, token));
                }
                else
                {
                    result = await Task.Run(() => pipeline.Run(token));
                }
            }
            catch (Exception ex)
            {
                StatusText = "Error: " + ex.Message;
                StopElapsed();
                IsRunning = false;
                return;
            }
            _lastResult = result;
            if (result.DeElapsed > TimeSpan.Zero)
                DeTimingText = $"{result.DeElapsed.TotalSeconds:F1} s ({result.DeSecondsPerGeneration:F3} s/gen)";

            // Auto-save the pre-polish seed pool by default (skip when polishing a saved folder —
            // that folder already IS the pre-polish set).
            int nPre = 0;
            if (!polishingSaved)
            {
                try { nPre = SavePool(result.SeedPool.Models.Select(m => (m.System, m.Merit, m.GlassSet)), "seeds_pre_polish", "seed"); }
                catch { }
            }

            // Run log OUTSIDE the lens-file folders (output root, alongside the subfolders — not in
            // them) so the seed/polished folders stay pure lens-file folders and runs are comparable.
            string logPath = "";
            try
            {
                Directory.CreateDirectory(OutputDir);
                string logStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string logMode = polishingSaved ? "Polish previously-saved DE results" : "DE search + polish";
                string logSrc = polishingSaved ? PolishFolder : "(DE search from loaded design)";
                logPath = Path.Combine(OutputDir, $"de_run_{logStamp}.log");
                File.WriteAllText(logPath, DeRunLog.Build(pset, result, logMode, logSrc, DateTime.Now));
            }
            catch { logPath = ""; }

            // Gallery from the polished candidates (best-first).
            var glass = _session.GlassCatalog;
            int rank = 1;
            foreach (var c in result.Polished)
            {
                int r = rank++;
                var gm = new GlobalSearchModel
                {
                    System = c.System,
                    Merit = c.MeritAfter,
                    GlassSet = c.GlassSet,
                    Seed = BaseSeed,
                    Converged = true,
                };
                var card = await Task.Run(() => GlobalSearchCardViewModel.Build(gm, r, glass));
                if (r == 1) card.IsBest = true;
                Cards.Add(card);
            }

            ProgressFraction = 1.0;
            string savedMsg = polishingSaved ? "" : $"  ·  auto-saved {nPre} pre-polish seed(s)";
            string logMsg = string.IsNullOrEmpty(logPath) ? "" : $"  ·  log: {logPath}";
            StatusText = result.Message + savedMsg + logMsg;
            StopElapsed();
            IsRunning = false;
        }

        private void StopElapsed()
        {
            _stopwatch.Stop();
            _elapsedTimer?.Stop();
            _elapsedTimer = null;
            ElapsedText = $"{_stopwatch.Elapsed.TotalSeconds:F1} s";
        }

        [RelayCommand]
        private void Stop()
        {
            _cts?.Cancel();
            StatusText = "Stopping — keeping the designs found so far…";
        }

        /// <summary>
        /// Cancel a running pipeline when the dialog is closed. Without this, closing
        /// the window leaves the DE / polish worker threads running (full speed, since
        /// we opt out of power throttling) until the whole app exits. No-op if nothing
        /// is running.
        /// </summary>
        public void CancelRun() => _cts?.Cancel();

        [RelayCommand]
        private void ApplyCard(GlobalSearchCardViewModel? card)
        {
            if (card == null || IsRunning) return;
            _session.System.CopyFrom(card.Model.System);
            Accepted = true;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Save the polished gallery to <paramref name="folder"/>. Returns the count.</summary>
        public int SaveAllTo(string folder)
        {
            int n = 0;
            string title = string.IsNullOrWhiteSpace(_session.System.Title) ? "design" : _session.System.Title;
            foreach (var card in Cards)
            {
                var m = card.Model;
                string glassTag = m.GlassSet.Count > 0 ? string.Join("-", m.GlassSet) : "noglass";
                string name = Sanitize($"{title}_polished_rank{card.Rank}_merit{m.Merit:G4}_{glassTag}");
                try { LhltWriter.Write(m.System, Path.Combine(folder, name + ".lhlt"), _session.MeritFunction, _session.ConfigEditor); n++; }
                catch { }
            }
            StatusText = $"Saved {n} of {Cards.Count} polished design(s) to {folder}";
            return n;
        }

        private int SavePool(System.Collections.Generic.IEnumerable<(LensHH.Core.Models.OpticalSystem sys, double merit, System.Collections.Generic.List<string> glasses)> pool,
            string subfolder, string tag)
        {
            string dir = Path.Combine(OutputDir, subfolder);
            Directory.CreateDirectory(dir);
            string title = string.IsNullOrWhiteSpace(_session.System.Title) ? "design" : _session.System.Title;
            int n = 0, r = 1;
            foreach (var (sys, merit, glasses) in pool)
            {
                string glassTag = glasses.Count > 0 ? string.Join("-", glasses) : "noglass";
                string name = Sanitize($"{title}_{tag}{r++:00}_merit{merit:G4}_{glassTag}");
                try { LhltWriter.Write(sys, Path.Combine(dir, name + ".lhlt"), _session.MeritFunction, _session.ConfigEditor); n++; }
                catch { }
            }
            return n;
        }

        private static string Sanitize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '_');
            return sb.ToString();
        }
    }
}
