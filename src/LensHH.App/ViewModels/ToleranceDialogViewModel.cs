using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LensHH.App.Session;
using LensHH.Tolerancing;
using Svg.Skia;

namespace LensHH.App.ViewModels;

/// <summary>
/// The Tolerancing window: a tolerance set as .tol text, the run settings, and the sensitivity or
/// Monte Carlo report, its MTF plots and the trial lenses it saved. Runs on a copy of the lens;
/// the design itself is never changed.
/// </summary>
public partial class ToleranceDialogViewModel : ObservableObject
{
    private readonly GuiSession _session;
    private CancellationTokenSource? _cts;
    private MonteCarloResult? _monteCarlo;

    public ToleranceDialogViewModel(GuiSession session)
    {
        _session = session;
        _tolerancesText = "# Tolerances, one per line: CODE surfaces ±t, or CODE surfaces min max\n"
                        + "# Choose a shop grade and press Generate for a starting set.\n";
        // Trials go beside the lens file, or to Documents for a lens not yet saved.
        string? file = session.FilePath;
        _saveDirectory = file != null
            ? Path.Combine(Path.GetDirectoryName(file) ?? "", Path.GetFileNameWithoutExtension(file) + " Monte Carlo")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LensHH Monte Carlo");
    }

    public static string[] Grades { get; } = { "Low cost", "Commercial", "Precision", "Extra precise" };
    public static string[] Criteria { get; } = { "RMS wavefront", "MTF" };
    public static string[] Orientations { get; } = { "Tangential", "Sagittal" };
    public static string[] DistributionNames { get; } = { "Uniform", "Normal (±2σ)", "End points" };

    [ObservableProperty] private string _tolerancesText;
    [ObservableProperty] private int _gradeIndex = 1;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsMtfCriterion))] private int _criterionIndex;
    public bool IsMtfCriterion => CriterionIndex == 1;
    [ObservableProperty] private int _mtfField = 1;
    [ObservableProperty] private double _mtfFrequency = 30;
    [ObservableProperty] private int _orientationIndex;

    [ObservableProperty] private bool _polychromatic = true;
    [ObservableProperty] private bool _mirrorFields = true;
    [ObservableProperty] private bool _compensate = true;
    [ObservableProperty] private string _travelText = "";

    [ObservableProperty] private int _trials = 200;
    [ObservableProperty] private int _seed = 1;
    [ObservableProperty] private int _distributionIndex;
    [ObservableProperty] private bool _pairsAsOne;
    [ObservableProperty] private double _mtfMaxFrequency = 60;
    [ObservableProperty] private double _mtfStep = 5;

    [ObservableProperty] private bool _saveTrials = true;
    [ObservableProperty] private string _saveDirectory = "";
    [ObservableProperty] private int _saveCount = 20;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(RunSensitivityCommand), nameof(RunMonteCarloCommand), nameof(CancelCommand))]
    private bool _isRunning;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _report = "";
    [ObservableProperty] private int _selectedTab;

    public ObservableCollection<string> MtfFields { get; } = new();
    [ObservableProperty] private int _mtfFieldIndex = -1;
    [ObservableProperty] private Bitmap? _mtfImage;

    public ObservableCollection<string> TrialFiles { get; } = new();
    [ObservableProperty] private string? _selectedTrial;

    /// <summary>A trial lens to open in the main window once this one closes.</summary>
    public string? OpenPath { get; private set; }
    public event EventHandler? CloseRequested;

    public int FieldCount => _session.HasSystem ? _session.System.Fields.Count : 0;

    [RelayCommand]
    private void Generate()
    {
        try
        {
            var grade = (ShopGrade)GradeIndex;
            TolerancesText = ToleranceRun.Defaults(_session.System, _session.GlassCatalog, grade, Settings());
            Status = $"{Grades[GradeIndex]} tolerances generated.";
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Status = e.Message;
        }
    }

    public void LoadTolerances(string path) => TolerancesText = File.ReadAllText(path);
    public void SaveTolerances(string path) => File.WriteAllText(path, TolerancesText);

    private ToleranceSettings Settings()
    {
        double? travel = double.TryParse(TravelText, NumberStyles.Float, CultureInfo.CurrentCulture, out double t) && t > 0 ? t : null;
        string stats = DistributionIndex switch { 1 => "normal", 2 => "endpoint", _ => "uniform" } + (PairsAsOne ? "+disc" : "");
        string criterion = IsMtfCriterion
            ? string.Create(CultureInfo.InvariantCulture, $"mtf:{MtfField}:{MtfFrequency}:{(OrientationIndex == 0 ? "t" : "s")}")
            : "rms";
        return new ToleranceSettings
        {
            Criterion = criterion,
            Polychromatic = Polychromatic,
            Compensate = Compensate,
            Travel = travel,
            Fields = MirrorFields ? FieldSymmetry.Y : FieldSymmetry.File,
            Trials = Math.Max(1, Trials),
            Seed = Seed,
            Statistics = stats,
            MtfMaxFrequency = MtfMaxFrequency,
            MtfStep = MtfStep,
            SaveDirectory = SaveTrials && !string.IsNullOrWhiteSpace(SaveDirectory) ? SaveDirectory : null,
            SaveTrials = SaveCount,
        };
    }

    private bool CanRun() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunSensitivity()
    {
        var settings = Settings();
        string text = TolerancesText;
        int total;
        try { total = 2 * ToleranceRun.Parse(text, settings).Count; }
        catch (FormatException e) { Status = e.Message; return; }
        var system = _session.System.DeepClone();
        await Run($"Sensitivity of {total / 2} tolerances", total, (progress, cancel) =>
        {
            var table = ToleranceRun.Sensitivity(system, _session.GlassCatalog, text, settings, progress, cancel);
            return () => { Report = ToleranceRun.Report(table); SelectedTab = 0; };
        });
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunMonteCarlo()
    {
        var settings = Settings();
        string text = TolerancesText;
        try { ToleranceRun.Parse(text, settings); }
        catch (FormatException e) { Status = e.Message; return; }
        var system = _session.System.DeepClone();
        await Run($"Monte Carlo of {settings.Trials} lenses", settings.Trials, (progress, cancel) =>
        {
            var result = ToleranceRun.MonteCarlo(system, _session.GlassCatalog, text, settings, progress, cancel);
            return () =>
            {
                _monteCarlo = result;
                Report = ToleranceRun.Report(result);
                MtfFields.Clear();
                foreach (int f in result.Fields)
                    MtfFields.Add($"Field {f + 1} ({system.Fields[f].Y.ToString("G4", CultureInfo.CurrentCulture)})");
                MtfFieldIndex = MtfFields.Count > 0 ? 0 : -1;
                if (MtfFields.Count == 0) MtfImage = null;
                TrialFiles.Clear();
                foreach (var t in result.Trials.Where(t => t.File != null))
                    TrialFiles.Add(t.File!);
                SelectedTab = MtfFields.Count > 0 ? 1 : 0;
            };
        });
    }

    private async Task Run(string what, int total, Func<IProgress<int>, CancellationToken, Action> work)
    {
        IsRunning = true;
        Progress = 0;
        Status = what + "…";
        _cts = new CancellationTokenSource();
        var progress = new Progress<int>(done => Progress = 100.0 * done / Math.Max(1, total));
        var started = DateTime.Now;
        try
        {
            var show = await Task.Run(() => work(progress, _cts.Token));
            show();
            Status = $"{what}: done in {(DateTime.Now - started).TotalSeconds:F0} s.";
        }
        catch (OperationCanceledException)
        {
            Status = $"{what}: cancelled.";
        }
        catch (Exception e) when (e is ArgumentException or FormatException or InvalidOperationException
                                   or NotSupportedException or IOException or AggregateException)
        {
            Status = $"{what}: {(e is AggregateException a ? a.InnerException?.Message : e.Message)}";
        }
        finally
        {
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private bool CanCancel() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    public void CancelRun() => _cts?.Cancel();

    partial void OnMtfFieldIndexChanged(int value)
    {
        if (_monteCarlo == null || value < 0 || value >= _monteCarlo.Fields.Count) return;
        MtfImage = Render(MonteCarloPlot.Svg(_monteCarlo, value));
    }

    [RelayCommand]
    private void OpenTrial()
    {
        if (SelectedTrial == null) return;
        OpenPath = SelectedTrial;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private static Bitmap? Render(string svg)
    {
        using var skSvg = new SKSvg();
        skSvg.FromSvg(svg);
        if (skSvg.Picture == null) return null;
        var bounds = skSvg.Picture.CullRect;
        int w = Math.Max(100, (int)bounds.Width), h = Math.Max(100, (int)bounds.Height);
        const int scale = 2;
        using var bitmap = new SkiaSharp.SKBitmap(w * scale, h * scale);
        using var canvas = new SkiaSharp.SKCanvas(bitmap);
        canvas.Clear(SkiaSharp.SKColors.White);
        canvas.Scale(scale);
        canvas.DrawPicture(skSvg.Picture);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }
}
