using System;
using System.Collections.Generic;
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
    /// View-model for the Random Start Optimization dialog. Runs <see cref="RandomStartService"/>
    /// off the UI thread - copies of the starting design with their variables randomized, screened
    /// by a short local optimization, the best polished by a full one - and shows the distinct best
    /// as a gallery of cards (<see cref="GlobalSearchCardViewModel"/>).
    /// </summary>
    public partial class RandomStartDialogViewModel : ObservableObject
    {
        private readonly GuiSession _session;
        private CancellationTokenSource? _cts;
        private readonly System.Diagnostics.Stopwatch _stopwatch = new();
        private DispatcherTimer? _elapsedTimer;
        private readonly string? _catalogFolder;

        public RandomStartDialogViewModel(GuiSession session)
        {
            _session = session;
            Cards.CollectionChanged += OnCardsChanged;

            _catalogFolder = GlassSubstitutionViewModel.FindFilteredCatalogFolder();
            var names = new List<string>();
            if (_catalogFolder != null && Directory.Exists(_catalogFolder))
                names.AddRange(Directory.GetFiles(_catalogFolder, "*.agf", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Select(n => n!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            if (names.Count == 0) names.Add("CoreSet28");
            GlassCatalogs = names;
            _selectedGlassCatalog = names.FirstOrDefault(n => n.Equals("CoreSet28", StringComparison.OrdinalIgnoreCase))
                                    ?? names[0];

            // The automatic sigmas for this lens, shown so they can be seen and changed.
            try
            {
                var (c, t) = new RandomStartService(_session.System, _session.MeritFunction, _session.GlassCatalog).ResolvedSigmas();
                _curvatureSigma = Math.Round(c, 6);
                _thicknessSigma = Math.Round(t, 4);
            }
            catch { }
            _variablesText = DescribeVariables();
        }

        // ── Step method (as Global Search) ──
        public static IReadOnlyList<GlobalSearchDialogViewModel.StepOption> StepOptions =>
            GlobalSearchDialogViewModel.StepOptions;
        [ObservableProperty] private GlobalSearchDialogViewModel.StepOption _selectedStep = GlobalSearchDialogViewModel.StepOptions[2];   // PSD III
        [ObservableProperty] private bool _useBroydenUpdate = false;   // PSD's default: it needs fresh Jacobians

        partial void OnSelectedStepChanged(GlobalSearchDialogViewModel.StepOption value)
        {
            if (value != null) UseBroydenUpdate = value.Value == StepMethod.LevenbergMarquardt;
        }

        // ── Designs and optimization ──
        [ObservableProperty] private int _designs = 50000;
        [ObservableProperty] private int _screenIterations = 30;
        [ObservableProperty] private double _polishPercent = 10.0;
        [ObservableProperty] private int _lmIterations = 6000;
        [ObservableProperty] private int _modelsToKeep = 24;
        [ObservableProperty] private int _seed = 1;
        [ObservableProperty] private int _engineModeIndex = 1;     // 0 = C#, 1 = Native
        [ObservableProperty] private int _derivativeModeIndex = 1; // 0 = FD, 1 = Analytic

        // ── Randomization: bounded variables ──
        public IReadOnlyList<string> BoundedOptions { get; } = new[] { "Uniform within min and max", "Around the start, ± % of the range" };
        [ObservableProperty] private int _boundedIndex = 0;
        [ObservableProperty] private double _boundedPercent = 25.0;
        public bool IsAroundStart => BoundedIndex == 1;
        partial void OnBoundedIndexChanged(int value) => OnPropertyChanged(nameof(IsAroundStart));

        // ── Randomization: other variables, a sigma per type ──
        [ObservableProperty] private bool _randomizeCurvature = true;
        [ObservableProperty] private double _curvatureSigma = 0.008;
        [ObservableProperty] private bool _randomizeThickness = true;
        [ObservableProperty] private double _thicknessSigma = 1.0;
        [ObservableProperty] private bool _randomizeConic = true;
        [ObservableProperty] private double _conicSigma = 0.5;
        [ObservableProperty] private bool _randomizeModelGlass = true;
        [ObservableProperty] private double _ndSigma = 0.05;
        [ObservableProperty] private double _vdSigma = 5.0;

        // ── Glass ──
        [ObservableProperty] private bool _substituteGlass = true;
        [ObservableProperty] private string _selectedGlassCatalog;
        public IReadOnlyList<string> GlassCatalogs { get; }

        // ── Run state ──
        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private string _variablesText = string.Empty;
        [ObservableProperty] private string _statusText = "Ready. Make the parameters to explore variables (with min and max where you want them kept), then press Run.";
        [ObservableProperty] private string _progressText = string.Empty;
        [ObservableProperty] private double _progressFraction;
        [ObservableProperty] private string _initialMeritText = "—";
        [ObservableProperty] private string _bestMeritText = "—";
        [ObservableProperty] private string _elapsedText = "0.0 s";

        public ObservableCollection<GlobalSearchCardViewModel> Cards { get; } = new();
        public bool Accepted { get; private set; }
        public event EventHandler? CloseRequested;

        public bool CanRun => !IsRunning;
        public bool CanSaveAll => !IsRunning && Cards.Count > 0;

        partial void OnIsRunningChanged(bool value)
        {
            OnPropertyChanged(nameof(CanRun));
            OnPropertyChanged(nameof(CanSaveAll));
        }

        private void OnCardsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
            OnPropertyChanged(nameof(CanSaveAll));

        /// <summary>How many variables of each kind the lens has, and how many are bounded on both sides.</summary>
        private string DescribeVariables()
        {
            try
            {
                var lo = new LocalOptimizer(_session.System.DeepClone(), _session.MeritFunction.Clone(), _session.GlassCatalog);
                lo.CollectVariables();
                var vars = lo.Variables;
                if (vars.Count == 0) return "The lens has no variables.";
                var groups = vars.GroupBy(v => v.Type).OrderBy(g => g.Key)
                    .Select(g => $"{g.Count()} {g.Key} ({g.Count(v => v.Transform.Min.HasValue && v.Transform.Max.HasValue)} with min and max)");
                return $"{vars.Count} variables: " + string.Join(", ", groups) + ". The first design is the starting design itself.";
            }
            catch { return string.Empty; }
        }

        private RandomStartSettings BuildSettings() => new RandomStartSettings
        {
            Designs = Math.Max(1, Designs),
            ScreenIterations = Math.Max(0, ScreenIterations),
            PolishFraction = Math.Max(0.001, Math.Min(1.0, PolishPercent / 100.0)),
            LmIterations = Math.Max(1, LmIterations),
            Bounded = BoundedIndex == 1 ? BoundedRandomization.AroundStart : BoundedRandomization.UniformInRange,
            BoundedFraction = Math.Max(0.0, BoundedPercent / 100.0),
            RandomizeCurvature = RandomizeCurvature,
            CurvatureSigma = CurvatureSigma > 0 ? CurvatureSigma : null,
            RandomizeThickness = RandomizeThickness,
            ThicknessSigma = ThicknessSigma > 0 ? ThicknessSigma : null,
            RandomizeConic = RandomizeConic,
            ConicSigma = Math.Max(0.0, ConicSigma),
            RandomizeModelGlass = RandomizeModelGlass,
            NdSigma = Math.Max(0.0, NdSigma),
            VdSigma = Math.Max(0.0, VdSigma),
            SubstituteGlass = SubstituteGlass,
            GlassCatalog = string.IsNullOrWhiteSpace(SelectedGlassCatalog) ? "CoreSet28" : SelectedGlassCatalog,
            ModelsToKeep = Math.Max(1, ModelsToKeep),
            Seed = Seed,
            Step = SelectedStep?.Value ?? StepMethod.PsdIII,
            UseBroydenUpdate = UseBroydenUpdate,
        };

        public string BuildComputePathPreview()
            => LensHH.App.Views.ComputePathPreview.Build(
                _session,
                "Random Start - " + (SelectedStep?.Label ?? "LM") + " per design",
                (EngineModeIndex == 1) ? EngineMode.Native : EngineMode.CSharp,
                (DerivativeModeIndex == 1) ? MeritDerivativeMode.Analytic : MeritDerivativeMode.FiniteDifference,
                UseBroydenUpdate,
                gpuImageOperands: false,
                "The designs are optimized in parallel, one per processor; each design's own merit evaluation is serial.");

        [RelayCommand]
        private async Task Run()
        {
            if (IsRunning) return;
            IsRunning = true;
            Cards.Clear();
            ProgressFraction = 0;
            BestMeritText = "—";
            _cts = new CancellationTokenSource();
            StatusText = "Running random start optimization…";

            _stopwatch.Restart();
            _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _elapsedTimer.Tick += (_, _) => ElapsedText = $"{_stopwatch.Elapsed.TotalSeconds:F1} s";
            _elapsedTimer.Start();

            try
            {
                var ev = AppExtensions.CreateMeritEvaluator(_session.System, _session.GlassCatalog);
                ev.ParallelEvaluation = true;
                InitialMeritText = ev.Evaluate(_session.MeritFunction).ToString("E4");
            }
            catch { InitialMeritText = "—"; }

            var svc = new RandomStartService(_session.System, _session.MeritFunction, _session.GlassCatalog)
            {
                Settings = BuildSettings(),
                EngineMode = EngineModeIndex == 1 ? EngineMode.Native : EngineMode.CSharp,
                NativeDerivativeMode = DerivativeModeIndex == 1
                    ? MeritDerivativeMode.Analytic
                    : MeritDerivativeMode.FiniteDifference,
                FilteredCatalogSearchPaths = _catalogFolder != null ? new[] { _catalogFolder } : Array.Empty<string>(),
            };

            long lastPost = 0;
            svc.OnProgress = p =>
            {
                long now = Environment.TickCount64;
                bool last = p.RestartIndex >= p.MaxRestarts;
                if (!last && now - Interlocked.Read(ref lastPost) < 100) return;
                Interlocked.Exchange(ref lastPost, now);
                Dispatcher.UIThread.Post(() =>
                {
                    ProgressText = p.StatusMessage;
                    ProgressFraction = Math.Min(1.0, (double)p.RestartIndex / Math.Max(1, p.MaxRestarts));
                    if (!double.IsNaN(p.BestMerit)) BestMeritText = p.BestMerit.ToString("E4");
                    StatusText = $"Designs {p.RestartIndex}/{p.MaxRestarts} · {p.PoolCount} distinct kept";
                });
            };

            GlobalSearchResult result;
            try
            {
                var token = _cts.Token;
                result = await Task.Run(() => svc.Run(token));
            }
            catch (Exception ex)
            {
                StatusText = "Error: " + ex.Message;
                StopElapsed();
                IsRunning = false;
                return;
            }

            var glass = _session.GlassCatalog;
            int rank = 1;
            foreach (var m in result.Models)
            {
                int r = rank++;
                var card = await Task.Run(() => GlobalSearchCardViewModel.Build(m, r, glass));
                if (r == 1) card.IsBest = true;
                Cards.Add(card);
            }
            if (result.Best != null) BestMeritText = result.Best.Merit.ToString("E4");

            ProgressFraction = 1.0;
            StatusText = result.Message;
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

        public void CancelRun() => _cts?.Cancel();

        [RelayCommand]
        private void ApplyCard(GlobalSearchCardViewModel? card)
        {
            if (card == null || IsRunning) return;
            _session.System.CopyFrom(card.Model.System);
            Accepted = true;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        public int SaveAllTo(string folder)
        {
            int n = 0;
            string title = string.IsNullOrWhiteSpace(_session.System.Title) ? "design" : _session.System.Title;
            foreach (var card in Cards)
            {
                var m = card.Model;
                string glassTag = m.GlassSet.Count > 0 ? string.Join("-", m.GlassSet) : "noglass";
                string name = Sanitize($"{title}_randomstart_rank{card.Rank}_merit{m.Merit:G4}_{glassTag}");
                try
                {
                    LhltWriter.Write(m.System, Path.Combine(folder, name + ".lhlt"), _session.MeritFunction);
                    n++;
                }
                catch { /* skip a single failed write, keep going */ }
            }
            StatusText = $"Saved {n} of {Cards.Count} design(s) to {folder}";
            return n;
        }

        private static string Sanitize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' || c == '+' ? c : '_');
            return sb.ToString();
        }
    }
}
