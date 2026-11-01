using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
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
        // The back focus refocuses each lens until the user names other compensators.
        if (session.HasSystem) Compensators.Add(new CompensatorRowViewModel { Code = "COMP", Surfaces = BackFocusSurface.ToString(CultureInfo.InvariantCulture) });
        // Trials go beside the lens file, or to Documents for a lens not yet saved.
        string? file = session.FilePath;
        _saveDirectory = file != null
            ? Path.Combine(Path.GetDirectoryName(file) ?? "", Path.GetFileNameWithoutExtension(file) + " Monte Carlo")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LensHH Monte Carlo");
        // The set as it was when the window last closed on this lens.
        if (session.HasSystem && !string.IsNullOrWhiteSpace(session.ToleranceSet)) Fill(session.ToleranceSet);
    }

    /// <summary>Keeps the set in the session, for the next time the window opens and for the drawings.</summary>
    public void Remember()
    {
        if (Tolerances.Count > 0) _session.ToleranceSet = TolerancesText;
    }

    public static string[] Grades { get; } = { "Low cost", "Commercial", "Precision", "Extra precise" };
    public static string[] Criteria { get; } = { "RMS wavefront", "MTF" };
    public static string[] Orientations { get; } = { "Tangential", "Sagittal" };
    public static string[] DistributionNames { get; } = { "Uniform", "Normal (±2σ)", "End points" };

    /// <summary>The tolerance table, one row per tolerance.</summary>
    public ObservableCollection<ToleranceRowViewModel> Tolerances { get; } = new();
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(InsertToleranceCommand), nameof(DeleteToleranceCommand))]
    private ToleranceRowViewModel? _selectedTolerance;

    /// <summary>The compensators: what is adjusted after each lens is built. The back focus to begin with.</summary>
    public ObservableCollection<CompensatorRowViewModel> Compensators { get; } = new();
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(DeleteCompensatorCommand))]
    private CompensatorRowViewModel? _selectedCompensator;

    [ObservableProperty] private int _gradeIndex = 1;

    private int BackFocusSurface => _session.System.Surfaces.Count - 2;

    /// <summary>The table and the compensators as a tolerance set, in the .tol format.</summary>
    public string TolerancesText =>
        string.Concat(Tolerances.Select(r => r.ToLine() + "\n")) + string.Concat(Compensators.Select(r => r.ToLine() + "\n"));

    [RelayCommand]
    private void AddTolerance()
    {
        // A new row starts as a copy of the one selected, the usual way to add the next surface's.
        var row = SelectedTolerance is { } s
            ? new ToleranceRowViewModel { Code = s.Code, Surfaces = s.Surfaces, Min = s.Min, Max = s.Max, SpreadIndex = s.SpreadIndex, Pivot = s.Pivot }
            : new ToleranceRowViewModel();
        Tolerances.Add(row);
        SelectedTolerance = row;
    }

    private bool HasSelectedTolerance() => SelectedTolerance != null;

    [RelayCommand(CanExecute = nameof(HasSelectedTolerance))]
    private void InsertTolerance()
    {
        int at = SelectedTolerance is { } s ? Tolerances.IndexOf(s) : 0;
        var row = new ToleranceRowViewModel();
        Tolerances.Insert(Math.Max(0, at), row);
        SelectedTolerance = row;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedTolerance))]
    private void DeleteTolerance()
    {
        if (SelectedTolerance is not { } s) return;
        int at = Tolerances.IndexOf(s);
        Tolerances.Remove(s);
        SelectedTolerance = Tolerances.Count == 0 ? null : Tolerances[Math.Min(at, Tolerances.Count - 1)];
    }

    [RelayCommand]
    private void AddCompensator()
    {
        var row = new CompensatorRowViewModel { Code = "COMP", Surfaces = BackFocusSurface.ToString(CultureInfo.InvariantCulture) };
        Compensators.Add(row);
        SelectedCompensator = row;
    }

    private bool HasSelectedCompensator() => SelectedCompensator != null;

    [RelayCommand(CanExecute = nameof(HasSelectedCompensator))]
    private void DeleteCompensator()
    {
        if (SelectedCompensator is { } s) Compensators.Remove(s);
    }

    /// <summary>Fills both tables from a tolerance set; false, with the reason in the status, when it does not read.</summary>
    private bool Fill(string text)
    {
        try
        {
            var items = ToleranceSpec.ParseSet(text);
            var compensators = ToleranceSpec.ParseCompensators(text);
            Tolerances.Clear();
            foreach (var item in items) Tolerances.Add(ToleranceRowViewModel.From(item));
            if (compensators.Count > 0)
            {
                Compensators.Clear();
                foreach (var a in compensators) Compensators.Add(CompensatorRowViewModel.From(a));
            }
            SelectedTolerance = Tolerances.FirstOrDefault();
            return true;
        }
        catch (FormatException e)
        {
            Status = e.Message;
            return false;
        }
    }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsMtfCriterion))] private int _criterionIndex;
    public bool IsMtfCriterion => CriterionIndex == 1;
    [ObservableProperty] private int _mtfField = 1;
    [ObservableProperty] private double _mtfFrequency = 30;
    [ObservableProperty] private int _orientationIndex;

    [ObservableProperty] private bool _polychromatic = true;
    /// <summary>The directions each lens is judged in, as <see cref="FieldSymmetry"/> orders them, most first.</summary>
    public string[] FieldDirections { get; } = { "+y only", "+y and −y", "±x and ±y" };
    [ObservableProperty] private int _fieldDirectionsIndex = (int)FieldSymmetry.XY;
    [ObservableProperty] private bool _compensate = true;

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
    // The window's tabs: Tolerances, Compensators, Settings, then the results.
    private const int ReportTab = 3, MtfTab = 4;
    [ObservableProperty] private int _selectedTab;

    public ObservableCollection<string> MtfFields { get; } = new();
    [ObservableProperty] private int _mtfFieldIndex = -1;
    [ObservableProperty] private Bitmap? _mtfImage;

    public ObservableCollection<string> TrialFiles { get; } = new();
    [ObservableProperty] private string? _selectedTrial;


    public int FieldCount => _session.HasSystem ? _session.System.Fields.Count : 0;

    [RelayCommand]
    private void Generate()
    {
        try
        {
            var grade = (ShopGrade)GradeIndex;
            if (Fill(ToleranceRun.Defaults(_session.System, _session.GlassCatalog, grade, Settings())))
                Status = $"{Grades[GradeIndex]} tolerances generated: {Tolerances.Count} rows.";
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Status = e.Message;
        }
    }

    public void LoadTolerances(string path)
    {
        if (Fill(File.ReadAllText(path))) Status = $"Read {Tolerances.Count} tolerances and {Compensators.Count} compensators from {Path.GetFileName(path)}.";
    }

    public void SaveTolerances(string path) => File.WriteAllText(path, TolerancesText);

    private ToleranceSettings Settings()
    {

        string stats = DistributionIndex switch { 1 => "normal", 2 => "endpoint", _ => "uniform" } + (PairsAsOne ? "+disc" : "");
        string criterion = IsMtfCriterion
            ? string.Create(CultureInfo.InvariantCulture, $"mtf:{MtfField}:{MtfFrequency}:{(OrientationIndex == 0 ? "t" : "s")}")
            : "rms";
        return new ToleranceSettings
        {
            Criterion = criterion,
            Polychromatic = Polychromatic,
            Compensate = Compensate,

            Fields = (FieldSymmetry)Math.Clamp(FieldDirectionsIndex, 0, FieldDirections.Length - 1),
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
        if (Tolerances.Count == 0) { Status = "The table is empty: add tolerances, or press Generate for a starting set."; return; }
        int total;
        try { total = 2 * ToleranceRun.Parse(text, settings).Count; }
        catch (FormatException e) { Status = e.Message; return; }
        var system = _session.System.DeepClone();
        await Run($"Sensitivity of {total / 2} tolerances", total, (progress, cancel) =>
        {
            var table = ToleranceRun.Sensitivity(system, _session.GlassCatalog, text, settings, progress, cancel);
            return () => { Report = ToleranceRun.Report(table); SelectedTab = ReportTab; };
        }, "trial lenses");
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunMonteCarlo()
    {
        var settings = Settings();
        string text = TolerancesText;
        if (Tolerances.Count == 0) { Status = "The table is empty: add tolerances, or press Generate for a starting set."; return; }
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
                SelectedTab = MtfFields.Count > 0 ? MtfTab : ReportTab;
            };
        }, "lenses");
    }

    /// <summary>
    /// Runs <paramref name="work"/> away from the window, on the Report tab, with the count done and
    /// the time so far in the status line each second. Stop ends it early; what was finished is
    /// reported as a run would be.
    /// </summary>
    private async Task Run(string what, int total, Func<IProgress<int>, CancellationToken, Action> work, string unit)
    {
        IsRunning = true;
        Progress = 0;
        SelectedTab = ReportTab;
        Report = $"{what}: running.\n\nStop ends it and reports the {unit} finished by then.";
        _cts = new CancellationTokenSource();
        int done = 0;
        var started = DateTime.Now;
        void Show() => Status = $"{what}: {done} of {total} {unit}, {Elapsed(DateTime.Now - started)}" +
                                (_cts?.IsCancellationRequested == true ? " - stopping…" : "");
        var progress = new Progress<int>(d => { done = Math.Max(done, d); Progress = 100.0 * done / Math.Max(1, total); Show(); });
        var timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Show());
        Show();
        timer.Start();
        try
        {
            var stop = _cts.Token;
            var show = await Task.Run(() => work(progress, stop));
            show();
            Status = stop.IsCancellationRequested
                ? $"{what}: stopped after {done} of {total} {unit}, {Elapsed(DateTime.Now - started)}; the report covers those."
                : $"{what}: done in {Elapsed(DateTime.Now - started)}.";
        }
        catch (OperationCanceledException)
        {
            Report = "";
            Status = $"{what}: stopped before any was finished.";
        }
        catch (Exception e) when (e is ArgumentException or FormatException or InvalidOperationException
                                   or NotSupportedException or IOException or AggregateException)
        {
            Report = "";
            Status = $"{what}: {(e is AggregateException a ? a.InnerException?.Message : e.Message)}";
        }
        finally
        {
            timer.Stop();
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private static string Elapsed(TimeSpan t) =>
        t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min {t.Seconds} s" : $"{t.Seconds} s";

    private bool CanCancel() => IsRunning;

    /// <summary>Stop: ends the run within a second or so; the lenses finished are kept and reported.</summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    public void CancelRun() => _cts?.Cancel();

    partial void OnMtfFieldIndexChanged(int value)
    {
        if (_monteCarlo == null || value < 0 || value >= _monteCarlo.Fields.Count) return;
        MtfImage = Render(MonteCarloPlot.Svg(_monteCarlo, value));
    }

    /// <summary>
    /// Opens the selected trial lens in a LensHH-LT window of its own - a second copy of the program -
    /// so the design open here, and this window, are left as they are.
    /// </summary>
    [RelayCommand]
    private void OpenTrial()
    {
        if (SelectedTrial == null) return;
        if (!File.Exists(SelectedTrial)) { Status = $"{SelectedTrial} is no longer there."; return; }
        // In an AppImage the program's own path is inside the mounted image; the image itself is what starts it.
        string? program = Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } image ? image : Environment.ProcessPath;
        if (program == null) { Status = "Could not find the program to open the lens with."; return; }
        try
        {
            var start = new ProcessStartInfo(program) { UseShellExecute = false };
            start.ArgumentList.Add(SelectedTrial);
            Process.Start(start);
            Status = $"Opening {Path.GetFileName(SelectedTrial)} in a new window.";
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            Status = $"Could not open {Path.GetFileName(SelectedTrial)}: {e.Message}";
        }
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
