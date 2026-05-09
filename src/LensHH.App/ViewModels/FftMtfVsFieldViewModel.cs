using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LensHH.App.Session;
using LensHH.Core.Analysis;
using LensHH.Rendering;
using LensHH.Rendering.TextExport;
using Avalonia.Platform.Storage;
using Svg.Skia;

namespace LensHH.App.ViewModels;

public partial class FftMtfVsFieldViewModel : ObservableObject
{
    private readonly GuiSession _session;

    [ObservableProperty] private Bitmap? _mtfImage;
    [ObservableProperty] private int _gridSize = 64;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private int _selectedWavelengthIndex = 0;
    [ObservableProperty] private string _frequencyLabel = "50, 100 cy/mm";
    [ObservableProperty] private int _numFieldPoints = 20;
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<string> WavelengthOptions { get; } = new();

    private double[] _frequencies = { 50, 100 };
    private MtfVsFieldMultiFreqResult? _lastResult;
    public MtfVsFieldMultiFreqResult? LastResult => _lastResult;

    /// <summary>Default frequencies for afocal systems (cy/arc-min).</summary>
    public static readonly double[] AfocalDefaultFrequencies = { 0.25, 0.5 };
    /// <summary>Default frequencies for focal systems (cy/mm).</summary>
    public static readonly double[] FocalDefaultFrequencies = { 50, 100 };

    public FftMtfVsFieldViewModel(GuiSession session)
    {
        _session = session;
        ApplyDefaultsForMode();
        // Track the session's afocal state — reload/lens-type-swap updates
        // the toolbar defaults and label units without the user clicking in.
        _session.SystemChanged += _ => ApplyDefaultsForMode();
    }

    /// <summary>
    /// Reset frequencies to the mode-appropriate defaults. Called from the
    /// constructor and whenever the MTF-vs-Field view is opened so a user
    /// switching between focal and afocal designs gets sensible starting
    /// frequencies in the right units.
    /// </summary>
    public void ApplyDefaultsForMode()
    {
        _frequencies = _session.System != null && _session.System.IsAfocal
            ? (double[])AfocalDefaultFrequencies.Clone()
            : (double[])FocalDefaultFrequencies.Clone();
        UpdateFrequencyLabel();
    }

    public double[] Frequencies
    {
        get => _frequencies;
        set
        {
            _frequencies = value;
            UpdateFrequencyLabel();
        }
    }

    private void UpdateFrequencyLabel()
    {
        string unit = _session.System != null && _session.System.IsAfocal ? "cy/arc-min" : "cy/mm";
        FrequencyLabel = string.Join(", ", Array.ConvertAll(_frequencies, f => $"{f:G}")) + " " + unit;
    }

    private void RefreshWavelengthOptions()
    {
        var system = _session.System;
        int prevIndex = SelectedWavelengthIndex;
        WavelengthOptions.Clear();
        WavelengthOptions.Add("All (Polychromatic)");
        for (int w = 0; w < system.Wavelengths.Count; w++)
            WavelengthOptions.Add($"W{w + 1}: {LabelFormat.Wavelength(system.Wavelengths[w].Value, system.Wavelengths)}");

        if (prevIndex >= 0 && prevIndex < WavelengthOptions.Count)
            SelectedWavelengthIndex = prevIndex;
        else
            SelectedWavelengthIndex = 0;
    }

    [RelayCommand]
    public async Task Compute()
    {
        if (_session.CannotCompute) return;
        if (_frequencies.Length == 0) return;

        RefreshWavelengthOptions();
        IsBusy = true;

        try
        {
            var system = _session.System;
            var glassMgr = _session.GlassCatalog;
            bool polychromatic = SelectedWavelengthIndex == 0;
            int waveIdx = SelectedWavelengthIndex - 1;
            var freqs = _frequencies;
            int gridSize = GridSize;
            int numPts = NumFieldPoints;

            var result = await Task.Run(() =>
                FftMtfCalculator.ComputeVsFieldMultiFreq(
                    system, glassMgr, freqs,
                    polychromatic ? 0 : waveIdx,
                    gridSize, numFieldPoints: numPts,
                    polychromatic: polychromatic));

            _lastResult = result;

            string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
            string title = polychromatic
                ? "FFT MTF vs Field \u2014 Polychromatic"
                : $"FFT MTF vs Field \u2014 {LabelFormat.Wavelength(system.Wavelengths[waveIdx].Value, system.Wavelengths)}";

            string svg = MtfVsFieldRenderer.Render(result, title, fieldUnit: fieldUnit);
            RenderSvgToBitmap(svg);
        }
        catch
        {
            _lastResult = null;
            MtfImage = null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ExportText()
    {
        if (_lastResult == null) return;

        var system = _session.System;
        string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";

        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Export FFT MTF vs Field Data",
                DefaultExtension = "txt",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Text Files") { Patterns = new[] { "*.txt" } }
                }
            });

        if (file == null) return;
        var path = file.TryGetLocalPath();
        if (path == null) return;

        bool polychromatic = SelectedWavelengthIndex == 0;
        int waveIdx = SelectedWavelengthIndex - 1;

        string wlFmt = "F" + LabelFormat.WavelengthDigits(system.Wavelengths);
        string title = polychromatic
            ? "FFT MTF vs Field - Polychromatic"
            : $"FFT MTF vs Field - {system.Wavelengths[waveIdx].Value.ToString(wlFmt, System.Globalization.CultureInfo.InvariantCulture)} um";

        string text = MtfVsFieldTextExport.Export(_lastResult, title, fieldUnit, system.IsAfocal);
        File.WriteAllText(path, text);
    }

    private void RenderSvgToBitmap(string svg)
    {
        using var skSvg = new SKSvg();
        skSvg.FromSvg(svg);

        if (skSvg.Picture != null)
        {
            var bounds = skSvg.Picture.CullRect;
            int w = (int)bounds.Width;
            int h = (int)bounds.Height;
            if (w < 100) w = 800;
            if (h < 100) h = 600;

            const int scale = 3;
            using var bitmap = new SkiaSharp.SKBitmap(w * scale, h * scale);
            using var canvas = new SkiaSharp.SKCanvas(bitmap);
            canvas.Clear(SkiaSharp.SKColors.White);
            canvas.Scale(scale);
            canvas.DrawPicture(skSvg.Picture);

            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream(data.ToArray());
            MtfImage = new Bitmap(stream);
        }
    }
}
