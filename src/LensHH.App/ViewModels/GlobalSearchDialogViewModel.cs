using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LensHH.App.Session;
using LensHH.Core.Analysis;
using LensHH.Core.IO;
using LensHH.Core.MeritFunction;
using LensHH.Core.Models;
using LensHH.Core.NativeInterop;
using LensHH.Core.Optimization;
using LensHH.Rendering;
using Svg.Skia;

namespace LensHH.App.ViewModels
{
    /// <summary>
    /// View-model for the Global Search dialog (1.0.121). Runs
    /// <see cref="GlobalSearchService"/> off the UI thread and presents the
    /// resulting pool as a gallery of design cards (layout thumbnail + metrics).
    /// The user browses the variety and chooses one to apply, or saves them all.
    /// </summary>
    public partial class GlobalSearchDialogViewModel : ObservableObject
    {
        private readonly GuiSession _session;
        private CancellationTokenSource? _cts;
        private readonly System.Diagnostics.Stopwatch _stopwatch = new();
        private DispatcherTimer? _elapsedTimer;

        public GlobalSearchDialogViewModel(GuiSession session)
        {
            _session = session;
            Cards.CollectionChanged += OnCardsChanged;
        }

        // ── Global Search settings ──
        [ObservableProperty] private int _modelsToKeep = 16;
        [ObservableProperty] private int _maxRestarts = 48;
        [ObservableProperty] private int _maxTrialsPerRestart = 2000;
        [ObservableProperty] private int _lmIterationsPerTrial = 4000;
        [ObservableProperty] private int _stopAtCapStallBatches = 1;
        [ObservableProperty] private int _baseSeed = 1;

        // ── Inherited Multistart knobs exposed here ──
        [ObservableProperty] private double _glassSubstitutionProbability = 50.0; // percent
        [ObservableProperty] private bool _rescaleOnGlassSwap = true;
        [ObservableProperty] private int _initialLmPolish; // 0 = don't polish the starting design (default)
        [ObservableProperty] private double _initialSigma = 0.001;
        [ObservableProperty] private double _sigmaCap = 0.01;
        [ObservableProperty] private int _engineModeIndex = 1;     // 0 = C#, 1 = Native
        [ObservableProperty] private int _derivativeModeIndex = 1; // 0 = FD, 1 = Analytic
        [ObservableProperty] private bool _useGpuPreScreen;

        // ── Run state ──
        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private string _statusText = "Ready. Set the pool size and press Run.";
        [ObservableProperty] private string _progressText = string.Empty;
        [ObservableProperty] private double _progressFraction;
        [ObservableProperty] private string _initialMeritText = "—";
        [ObservableProperty] private string _bestMeritText = "—";
        [ObservableProperty] private string _elapsedText = "0.0 s";

        /// <summary>The gallery — one card per distinct pooled design, best first.</summary>
        public ObservableCollection<GlobalSearchCardViewModel> Cards { get; } = new();

        /// <summary>True after the user applied a design (consumed by the caller).</summary>
        public bool Accepted { get; private set; }

        /// <summary>Raised when the dialog should close (apply chosen).</summary>
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

        // ─────────────────────────────────────────────────────────────────────
        //  Run / Cancel
        // ─────────────────────────────────────────────────────────────────────

        [RelayCommand]
        private async Task Run()
        {
            if (IsRunning) return;
            IsRunning = true;
            Cards.Clear();
            ProgressFraction = 0;
            BestMeritText = "—";
            _cts = new CancellationTokenSource();
            StatusText = "Running global search…";

            _stopwatch.Restart();
            _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _elapsedTimer.Tick += (_, _) => ElapsedText = $"{_stopwatch.Elapsed.TotalSeconds:F1} s";
            _elapsedTimer.Start();

            // Initial merit for context.
            try
            {
                var ev = new MeritFunctionEvaluator(_session.System, _session.GlassCatalog, _session.ConfigEditor)
                { ParallelEvaluation = true };
                InitialMeritText = ev.Evaluate(_session.MeritFunction).ToString("E4");
            }
            catch { InitialMeritText = "—"; }

            var svc = new GlobalSearchService(
                _session.System, _session.MeritFunction, _session.GlassCatalog, _session.ConfigEditor)
            {
                Settings = new GlobalSearchSettings
                {
                    ModelsToKeep = Math.Max(1, ModelsToKeep),
                    MaxRestarts = Math.Max(1, MaxRestarts),
                    MaxTrialsPerRestart = Math.Max(1, MaxTrialsPerRestart),
                    StopAtCapStallBatches = Math.Max(1, StopAtCapStallBatches),
                    BaseSeed = BaseSeed,
                    Multistart = new MultistartSettings
                    {
                        InitialSigma = InitialSigma,
                        SigmaCap = SigmaCap,
                        GlassSubstitutionProbability = GlassSubstitutionProbability / 100.0,
                        RescaleCurvatureOnGlassSwap = RescaleOnGlassSwap,
                        UseGpuPreScreen = UseGpuPreScreen,
                        LmIterationsPerTrial = Math.Max(1, LmIterationsPerTrial),
                        // 0 = perturb the raw starting design (no pre-polish); the
                        // service skips Phase-1 entirely. >0 = polish the start once.
                        InitialLmIterations = Math.Max(0, InitialLmPolish),
                    },
                },
                EngineMode = EngineModeIndex == 1 ? EngineMode.Native : EngineMode.CSharp,
                NativeDerivativeMode = DerivativeModeIndex == 1
                    ? MeritDerivativeMode.Analytic
                    : MeritDerivativeMode.FiniteDifference,
                FilteredCatalogSearchPaths = GlassSubstitutionViewModel.FindFilteredCatalogFolder() is string dir
                    ? new[] { dir } : Array.Empty<string>(),
            };

            svc.OnProgress = p => Dispatcher.UIThread.Post(() =>
            {
                ProgressText = p.StatusMessage;
                ProgressFraction = Math.Min(1.0, (double)p.PoolCount / Math.Max(1, p.ModelsToKeep));
                if (!double.IsNaN(p.BestMerit)) BestMeritText = p.BestMerit.ToString("E4");
                StatusText = $"Pool {p.PoolCount}/{p.ModelsToKeep} · restart {p.RestartIndex + 1}/{p.MaxRestarts}";
            });

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

            // Build the gallery cards (layout thumbnails are rendered off-thread).
            var glass = _session.GlassCatalog;
            int rank = 1;
            foreach (var m in result.Models)
            {
                int r = rank++;
                var card = await Task.Run(() => GlobalSearchCardViewModel.Build(m, r, glass));
                if (r == 1) card.IsBest = true;
                Cards.Add(card);
            }

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
            // Same idea as Multistart's Stop: halt the run but keep every design
            // already collected (the gallery still populates from the partial pool,
            // including the restart that was running when you pressed Stop).
            _cts?.Cancel();
            StatusText = "Stopping — keeping the designs found so far…";
        }

        /// <summary>
        /// Cancel a running search when the dialog is closed. Without this, closing the
        /// window leaves the worker threads running until the app exits. No-op if idle.
        /// </summary>
        public void CancelRun() => _cts?.Cancel();

        // ─────────────────────────────────────────────────────────────────────
        //  Apply / Save
        // ─────────────────────────────────────────────────────────────────────

        [RelayCommand]
        private void ApplyCard(GlobalSearchCardViewModel? card)
        {
            if (card == null || IsRunning) return;
            _session.System.CopyFrom(card.Model.System);
            Accepted = true;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Write every pooled design to <paramref name="folder"/> as .lhlt.
        /// Returns the number successfully written. Called by the dialog code-behind
        /// after the folder picker resolves.</summary>
        public int SaveAllTo(string folder)
        {
            int n = 0;
            string title = string.IsNullOrWhiteSpace(_session.System.Title) ? "design" : _session.System.Title;
            foreach (var card in Cards)
            {
                var m = card.Model;
                string glassTag = m.GlassSet.Count > 0 ? string.Join("-", m.GlassSet) : "noglass";
                string name = Sanitize($"{title}_global_rank{card.Rank}_seed{m.Seed}_merit{m.Merit:G4}_{glassTag}");
                string path = Path.Combine(folder, name + ".lhlt");
                try
                {
                    LhltWriter.Write(m.System, path, _session.MeritFunction, _session.ConfigEditor);
                    m.ArchivePath = path;
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
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '_');
            return sb.ToString();
        }
    }

    /// <summary>One gallery card — a layout thumbnail plus the design's metrics.</summary>
    public partial class GlobalSearchCardViewModel : ObservableObject
    {
        public GlobalSearchModel Model { get; }
        public int Rank { get; }
        public Bitmap? Thumbnail { get; }
        public string Title { get; }
        public string MeritText { get; }
        public string GlassText { get; }
        public string FormText { get; }
        public string InfoText { get; }

        [ObservableProperty] private bool _isBest;

        private GlobalSearchCardViewModel(GlobalSearchModel m, int rank, Bitmap? thumb)
        {
            Model = m;
            Rank = rank;
            Thumbnail = thumb;
            Title = $"#{rank}";
            MeritText = m.Merit.ToString("E4");
            GlassText = m.GlassSet.Count > 0 ? string.Join(", ", m.GlassSet) : "(no glass elements)";
            FormText = string.IsNullOrEmpty(m.PowerSignSignature) ? "—" : m.PowerSignSignature;
            string track = m.TrackLength.HasValue ? m.TrackLength.Value.ToString("F1") + " mm" : "—";
            InfoText = $"form {FormText} · track {track} · seed {m.Seed}" + (m.Converged ? "" : " · cap");
        }

        public static GlobalSearchCardViewModel Build(GlobalSearchModel m, int rank, LensHH.Core.Glass.GlassCatalogManager glass)
        {
            Bitmap? thumb = null;
            try { thumb = RenderThumbnail(m.System, glass); }
            catch { /* a card with no thumbnail is still useful */ }
            return new GlobalSearchCardViewModel(m, rank, thumb);
        }

        private static Bitmap? RenderThumbnail(OpticalSystem sys, LensHH.Core.Glass.GlassCatalogManager glass)
        {
            var layout = SystemLayout.ComputeLayout(sys, glass,
                numRays: 3, startFromSurface1: true, wavelengthIndex: -1);
            var fieldYs = sys.Fields.Select(f => f.Y).ToList();
            string fieldUnit = sys.FieldType == LensHH.Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
            string svg = SystemLayoutRenderer.Render(layout, width: 480, height: 240,
                fieldYs: fieldYs, fieldUnit: fieldUnit);

            using var skSvg = new SKSvg();
            skSvg.FromSvg(svg);
            if (skSvg.Picture == null) return null;

            var bounds = skSvg.Picture.CullRect;
            int w = (int)bounds.Width;
            int h = (int)bounds.Height;
            if (w < 50) w = 480;
            if (h < 50) h = 240;

            const int scale = 2;
            using var bitmap = new SkiaSharp.SKBitmap(w * scale, h * scale);
            using var canvas = new SkiaSharp.SKCanvas(bitmap);
            canvas.Clear(SkiaSharp.SKColors.White);
            canvas.Scale(scale);
            canvas.DrawPicture(skSvg.Picture);

            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
            using var stream = new MemoryStream(data.ToArray());
            return new Bitmap(stream);
        }
    }
}
