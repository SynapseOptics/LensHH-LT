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
    /// View-model for the Replace Model Glass dialog. Runs <see cref="ModelGlassReplacementService"/>
    /// off the UI thread - the lens's model glasses replaced by real glasses from one catalog, one
    /// element at a time, highest power first - and shows the finished designs as a gallery of
    /// cards (<see cref="GlobalSearchCardViewModel"/>).
    /// </summary>
    public partial class ReplaceModelGlassDialogViewModel : ObservableObject
    {
        private readonly GuiSession _session;
        private CancellationTokenSource? _cts;
        private readonly System.Diagnostics.Stopwatch _stopwatch = new();
        private DispatcherTimer? _elapsedTimer;
        private readonly string? _catalogFolder;

        public ReplaceModelGlassDialogViewModel(GuiSession session)
        {
            _session = session;
            Cards.CollectionChanged += OnCardsChanged;

            // The filtered catalogs (the user's own, e.g. preferred glasses) and the loaded vendor catalogs.
            _catalogFolder = GlassSubstitutionViewModel.FindFilteredCatalogFolder();
            var names = new List<string>();
            if (_catalogFolder != null && Directory.Exists(_catalogFolder))
                names.AddRange(Directory.GetFiles(_catalogFolder, "*.agf", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Select(n => n!));
            names.AddRange(_session.GlassCatalog.LoadedCatalogs);
            names = names.Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            if (names.Count == 0) names.Add("CoreSet28");
            GlassCatalogs = names;
            // Start with the lens's glass map catalog (Editors → System), else CoreSet28.
            string mapCatalog = _session.System.GlassMapCatalog;
            _selectedGlassCatalog = names.FirstOrDefault(n => !string.IsNullOrWhiteSpace(mapCatalog) && n.Equals(mapCatalog, StringComparison.OrdinalIgnoreCase))
                                    ?? names.FirstOrDefault(n => n.Equals("CoreSet28", StringComparison.OrdinalIgnoreCase))
                                    ?? names[0];
            UpdateGroupsText();
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

        // ── Settings ──
        [ObservableProperty] private string _selectedGlassCatalog;
        [ObservableProperty] private int _candidatesPerGlass = 20;
        [ObservableProperty] private int _beamWidth = 10;
        [ObservableProperty] private int _screenIterations = 30;
        [ObservableProperty] private double _polishPercent = 25.0;
        [ObservableProperty] private int _lmIterations = 6000;
        [ObservableProperty] private bool _rescaleCurvature = true;
        [ObservableProperty] private int _modelsToKeep = 24;
        [ObservableProperty] private int _engineModeIndex = 1;     // 0 = C#, 1 = Native
        [ObservableProperty] private int _derivativeModeIndex = 1; // 0 = FD, 1 = Analytic

        public IReadOnlyList<string> GlassCatalogs { get; }

        // ── Run state ──
        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private string _groupsText = string.Empty;
        [ObservableProperty] private string _statusText = "Ready. Start from an optimized lens with model glasses, then press Run.";
        [ObservableProperty] private string _progressText = string.Empty;
        [ObservableProperty] private double _progressFraction;
        [ObservableProperty] private string _initialMeritText = "—";
        [ObservableProperty] private string _bestMeritText = "—";
        [ObservableProperty] private string _elapsedText = "0.0 s";

        public ObservableCollection<GlobalSearchCardViewModel> Cards { get; } = new();
        public bool Accepted { get; private set; }
        public event EventHandler? CloseRequested;

        private int GroupCount => ModelGlassReplacementService.ModelGlassGroups(_session.System).Count;
        public bool CanRun => !IsRunning && GroupCount > 0;
        public bool CanSaveAll => !IsRunning && Cards.Count > 0;

        partial void OnIsRunningChanged(bool value)
        {
            OnPropertyChanged(nameof(CanRun));
            OnPropertyChanged(nameof(CanSaveAll));
        }

        private void OnCardsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
            OnPropertyChanged(nameof(CanSaveAll));

        private void UpdateGroupsText()
        {
            var sys = _session.System;
            var groups = ModelGlassReplacementService.ModelGlassGroups(sys);
            GroupsText = groups.Count == 0
                ? "The lens has no model glass to replace."
                : $"{groups.Count} model glass(es), replaced in this order (highest element power first): "
                  + string.Join(" → ", groups.Select(g =>
                  {
                      var s = sys.Surfaces[g.Surface];
                      string who = "surface " + g.Surface + (g.Followers.Count > 0 ? " (+" + string.Join(", ", g.Followers) + ")" : "");
                      return $"{who} Nd {s.ModelNd:F4} Vd {s.ModelVd:F2}";
                  }));
            OnPropertyChanged(nameof(CanRun));
        }

        private ModelGlassReplacementSettings BuildSettings() => new ModelGlassReplacementSettings
        {
            GlassCatalog = string.IsNullOrWhiteSpace(SelectedGlassCatalog) ? "CoreSet28" : SelectedGlassCatalog,
            CandidatesPerGlass = Math.Max(1, CandidatesPerGlass),
            BeamWidth = Math.Max(1, BeamWidth),
            ScreenIterations = Math.Max(0, ScreenIterations),
            PolishFraction = Math.Max(0.001, Math.Min(1.0, PolishPercent / 100.0)),
            LmIterations = Math.Max(1, LmIterations),
            RescaleCurvature = RescaleCurvature,
            ModelsToKeep = Math.Max(1, ModelsToKeep),
            Step = SelectedStep?.Value ?? StepMethod.PsdIII,
            UseBroydenUpdate = UseBroydenUpdate,
        };

        public string BuildComputePathPreview()
            => LensHH.App.Views.ComputePathPreview.Build(
                _session,
                "Replace Model Glass - " + (SelectedStep?.Label ?? "LM") + " per candidate",
                (EngineModeIndex == 1) ? EngineMode.Native : EngineMode.CSharp,
                (DerivativeModeIndex == 1) ? MeritDerivativeMode.Analytic : MeritDerivativeMode.FiniteDifference,
                UseBroydenUpdate,
                gpuImageOperands: false,
                "The candidates are optimized in parallel, one per processor; each candidate's own merit evaluation is serial.");

        [RelayCommand]
        private async Task Run()
        {
            if (IsRunning) return;
            UpdateGroupsText();
            if (GroupCount == 0) { StatusText = GroupsText; return; }

            IsRunning = true;
            Cards.Clear();
            ProgressFraction = 0;
            BestMeritText = "—";
            _cts = new CancellationTokenSource();
            StatusText = "Replacing model glasses…";

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

            var svc = new ModelGlassReplacementService(_session.System, _session.MeritFunction, _session.GlassCatalog)
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
                    StatusText = $"Optimizations {p.RestartIndex}/{p.MaxRestarts}";
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
            StatusText = "Stopping…";
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
                string name = Sanitize($"{title}_realglass_rank{card.Rank}_merit{m.Merit:G4}_{glassTag}");
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
