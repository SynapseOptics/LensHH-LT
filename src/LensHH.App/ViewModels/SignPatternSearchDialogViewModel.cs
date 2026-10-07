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
    /// View-model for the Sign-Pattern Search dialog. Runs <see cref="SignPatternSearchService"/>
    /// off the UI thread - a population of designs from a lens of nearly parallel plates, each with
    /// its own curvature-sign pattern and random glasses, each locally optimized - and shows the
    /// distinct best as a gallery of cards (<see cref="GlobalSearchCardViewModel"/>).
    /// </summary>
    public partial class SignPatternSearchDialogViewModel : ObservableObject
    {
        private readonly GuiSession _session;
        private CancellationTokenSource? _cts;
        private readonly System.Diagnostics.Stopwatch _stopwatch = new();
        private DispatcherTimer? _elapsedTimer;
        private readonly string? _catalogFolder;

        public SignPatternSearchDialogViewModel(GuiSession session)
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
            UpdatePopulationText();
        }

        // ── Step method (as Global Search) ──
        public static IReadOnlyList<GlobalSearchDialogViewModel.StepOption> StepOptions =>
            GlobalSearchDialogViewModel.StepOptions;
        [ObservableProperty] private GlobalSearchDialogViewModel.StepOption _selectedStep = GlobalSearchDialogViewModel.StepOptions[0];
        [ObservableProperty] private bool _useBroydenUpdate = true;

        /// <summary>Changing the step method resets Broyden to that method's default: PSD differences
        /// two successive FRESH Jacobians, which a rank-1 Broyden update is not.</summary>
        partial void OnSelectedStepChanged(GlobalSearchDialogViewModel.StepOption value)
        {
            if (value != null) UseBroydenUpdate = value.Value == StepMethod.LevenbergMarquardt;
        }

        // ── Settings ──
        [ObservableProperty] private int _designsPerSurfaceSquared = 50;
        [ObservableProperty] private bool _substituteGlass = true;
        [ObservableProperty] private string _selectedGlassCatalog;
        [ObservableProperty] private double _weakRadiusFactor = 10.0;
        [ObservableProperty] private int _lmIterations = 300;
        [ObservableProperty] private int _modelsToKeep = 24;
        [ObservableProperty] private int _seed = 1;
        [ObservableProperty] private int _engineModeIndex = 0;     // 0 = C# (faster here: designs run in parallel), 1 = Native
        [ObservableProperty] private int _derivativeModeIndex = 1; // 0 = FD, 1 = Analytic

        /// <summary>The .agf catalogs in the filtered-catalog folder.</summary>
        public IReadOnlyList<string> GlassCatalogs { get; }

        // ── Run state ──
        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private string _populationText = string.Empty;
        [ObservableProperty] private string _statusText = "Ready. Start from a lens of nearly parallel plates with its curvatures as variables, then press Run.";
        [ObservableProperty] private string _progressText = string.Empty;
        [ObservableProperty] private double _progressFraction;
        [ObservableProperty] private string _initialMeritText = "—";
        [ObservableProperty] private string _bestMeritText = "—";
        [ObservableProperty] private string _elapsedText = "0.0 s";

        public ObservableCollection<GlobalSearchCardViewModel> Cards { get; } = new();
        public bool Accepted { get; private set; }
        public event EventHandler? CloseRequested;

        public bool CanRun => !IsRunning && PopulationSize > 0;
        public bool CanSaveAll => !IsRunning && Cards.Count > 0;

        private int PopulationSize => SignPatternSearchService.PopulationSize(_session.System, BuildSettings());

        partial void OnIsRunningChanged(bool value)
        {
            OnPropertyChanged(nameof(CanRun));
            OnPropertyChanged(nameof(CanSaveAll));
        }

        partial void OnDesignsPerSurfaceSquaredChanged(int value) => UpdatePopulationText();

        private void OnCardsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
            OnPropertyChanged(nameof(CanSaveAll));

        private void UpdatePopulationText()
        {
            int n = SignPatternSearchService.SignSurfaces(_session.System).Count;
            int pop = PopulationSize;
            PopulationText = n == 0
                ? "No surface has a curvature variable that may take either sign: make the curvatures variables first."
                : $"{n} sign surfaces · {Math.Pow(2, n):G6} sign patterns · {pop} designs"
                  + (pop >= Math.Pow(2, n) ? " (every pattern an equal share)" : " (each a distinct random pattern)");
            OnPropertyChanged(nameof(CanRun));
        }

        private SignPatternSearchSettings BuildSettings() => new SignPatternSearchSettings
        {
            DesignsPerSurfaceSquared = Math.Max(1, DesignsPerSurfaceSquared),
            SubstituteGlass = SubstituteGlass,
            GlassCatalog = string.IsNullOrWhiteSpace(SelectedGlassCatalog) ? "CoreSet28" : SelectedGlassCatalog,
            WeakRadiusFactor = Math.Max(0.1, WeakRadiusFactor),
            LmIterations = Math.Max(1, LmIterations),
            ModelsToKeep = Math.Max(1, ModelsToKeep),
            Seed = Seed,
            Step = SelectedStep?.Value ?? StepMethod.LevenbergMarquardt,
            UseBroydenUpdate = UseBroydenUpdate,
        };

        public string BuildComputePathPreview()
            => LensHH.App.Views.ComputePathPreview.Build(
                _session,
                "Sign-Pattern Search - " + (SelectedStep?.Label ?? "LM") + " per design",
                (EngineModeIndex == 1) ? EngineMode.Native : EngineMode.CSharp,
                (DerivativeModeIndex == 1) ? MeritDerivativeMode.Analytic : MeritDerivativeMode.FiniteDifference,
                UseBroydenUpdate,
                gpuImageOperands: false,
                "The designs are optimized in parallel, one per processor; each design's own merit evaluation is serial.");

        [RelayCommand]
        private async Task Run()
        {
            if (IsRunning) return;
            UpdatePopulationText();
            if (PopulationSize == 0) { StatusText = PopulationText; return; }

            IsRunning = true;
            Cards.Clear();
            ProgressFraction = 0;
            BestMeritText = "—";
            _cts = new CancellationTokenSource();
            StatusText = "Running sign-pattern search…";

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

            var svc = new SignPatternSearchService(_session.System, _session.MeritFunction, _session.GlassCatalog)
            {
                Settings = BuildSettings(),
                EngineMode = EngineModeIndex == 1 ? EngineMode.Native : EngineMode.CSharp,
                NativeDerivativeMode = DerivativeModeIndex == 1
                    ? MeritDerivativeMode.Analytic
                    : MeritDerivativeMode.FiniteDifference,
                FilteredCatalogSearchPaths = _catalogFolder != null ? new[] { _catalogFolder } : Array.Empty<string>(),
            };

            // Progress arrives from every worker thread; post only the latest few per second.
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
                string name = Sanitize($"{title}_signpattern_rank{card.Rank}_{m.SignPattern}_merit{m.Merit:G4}_{glassTag}");
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
