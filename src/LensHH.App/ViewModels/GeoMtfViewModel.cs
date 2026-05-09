using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LensHH.App.Session;
using LensHH.Core.Analysis;
using LensHH.Rendering;
using LensHH.Rendering.TextExport;
using Avalonia.Platform.Storage;
using Svg.Skia;

namespace LensHH.App.ViewModels;

public partial class GeoMtfVsFreqViewModel : ObservableObject
{
    private readonly GuiSession _session;

    [ObservableProperty] private Bitmap? _plotImage;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _selectedWavelengthIndex = 0;
    [ObservableProperty] private int _numRings = 15;
    [ObservableProperty] private int _numFreqPoints = 200;
    [ObservableProperty] private double _maxFrequency = 0; // 0 = auto

    public ObservableCollection<string> WavelengthOptions { get; } = new();
    private MtfResult[]? _lastResults;
    private string[]? _lastFieldLabels;
    public MtfResult[]? LastFieldResults => _lastResults;
    public string[]? LastFieldLabels => _lastFieldLabels;

    public GeoMtfVsFreqViewModel(GuiSession session) { _session = session; }

    private void RefreshWavelengthOptions()
    {
        var system = _session.System;
        int prev = SelectedWavelengthIndex;
        WavelengthOptions.Clear();
        WavelengthOptions.Add("All (Polychromatic)");
        for (int w = 0; w < system.Wavelengths.Count; w++)
            WavelengthOptions.Add($"W{w + 1}: {LabelFormat.Wavelength(system.Wavelengths[w].Value, system.Wavelengths)}");
        if (prev >= 0 && prev < WavelengthOptions.Count)
            SelectedWavelengthIndex = prev;
        else
            SelectedWavelengthIndex = 0;
    }

    [RelayCommand]
    public async Task Compute()
    {
        if (_session.CannotCompute) return;
        IsBusy = true;
        try
        {
            RefreshWavelengthOptions();
            var system = _session.System;
            var glassMgr = _session.GlassCatalog;
            bool polychromatic = SelectedWavelengthIndex == 0;
            int waveIdx = SelectedWavelengthIndex - 1;
            int rings = NumRings;
            int numFreqPts = NumFreqPoints;
            double maxFreqSetting = MaxFrequency;
            int numFields = system.Fields.Count;

            var results = await Task.Run(() =>
            {
                var res = new MtfResult[numFields];
                for (int f = 0; f < numFields; f++)
                {
                    if (polychromatic)
                        res[f] = GeometricMtfKidger.ComputePolychromatic(system, glassMgr, f, rings,
                            maxFrequency: maxFreqSetting, numFreqPoints: numFreqPts);
                    else
                        res[f] = GeometricMtfKidger.Compute(system, glassMgr, f, waveIdx, rings,
                            maxFrequency: maxFreqSetting, numFreqPoints: numFreqPts);
                }
                return res;
            });

            _lastResults = results;
            string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
            var fieldLabels = new string[numFields];
            for (int f = 0; f < numFields; f++)
                fieldLabels[f] = $"{system.Fields[f].Y:F1} {fieldUnit}";
            _lastFieldLabels = fieldLabels;

            double maxFreq = maxFreqSetting;
            if (maxFreq <= 0)
            {
                maxFreq = 0;
                foreach (var r in results) if (r.MaxFrequency > maxFreq) maxFreq = r.MaxFrequency;
            }
            double onAxisCutoff = results.Length > 0 ? results[0].MaxFrequency : 0;

            string title = polychromatic
                ? "Geometric MTF \u2014 Polychromatic"
                : $"Geometric MTF \u2014 {LabelFormat.Wavelength(system.Wavelengths[waveIdx].Value, system.Wavelengths)}";
            string svg = FftMtfRenderer.RenderAllFields(results, fieldLabels, title,
                maxFrequency: maxFreq, onAxisCutoff: onAxisCutoff);
            RenderSvg(svg);
        }
        catch { _lastResults = null; PlotImage = null; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task ExportText()
    {
        if (_lastResults == null) return;
        var system = _session.System;
        string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
        var path = await PickSavePath("Export Geometric MTF Data");
        if (path == null) return;

        var sb = new System.Text.StringBuilder();
        for (int f = 0; f < _lastResults.Length; f++)
        {
            bool poly = SelectedWavelengthIndex == 0;
            int wIdx = SelectedWavelengthIndex - 1;
            string wlFmt = "F" + LabelFormat.WavelengthDigits(system.Wavelengths);
            string label = poly
                ? $"Field {f + 1}: {system.Fields[f].Y:F1} {fieldUnit}, Polychromatic"
                : $"Field {f + 1}: {system.Fields[f].Y:F1} {fieldUnit}, {system.Wavelengths[wIdx].Value.ToString(wlFmt, System.Globalization.CultureInfo.InvariantCulture)} um";
            sb.AppendLine(FftMtfTextExport.Export(_lastResults[f], label,
                _lastResults[f].CutoffT, _lastResults[f].CutoffS,
                system.Fields[f].Y,
                poly ? double.NaN : system.Wavelengths[wIdx].Value,
                fieldUnit, system.IsAfocal));
            sb.AppendLine();
        }
        File.WriteAllText(path, sb.ToString());
    }

    private void RenderSvg(string svg) { PlotImage = SvgHelper.ToBitmap(svg); }
    private async Task<string?> PickSavePath(string title) => await SvgHelper.PickSavePath(title);
}

public partial class GeoMtfVsFieldViewModel : ObservableObject
{
    private readonly GuiSession _session;

    [ObservableProperty] private Bitmap? _plotImage;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _selectedWavelengthIndex = 0;
    [ObservableProperty] private int _numRings = 30;
    [ObservableProperty] private int _numFieldPoints = 20;
    [ObservableProperty] private string _frequencyLabel = "50, 100 cy/mm";

    public ObservableCollection<string> WavelengthOptions { get; } = new();
    private double[] _frequencies = { 50, 100 };
    private MtfVsFieldMultiFreqResult? _lastResult;
    public MtfVsFieldMultiFreqResult? LastResult => _lastResult;

    public GeoMtfVsFieldViewModel(GuiSession session) { _session = session; }

    public double[] Frequencies
    {
        get => _frequencies;
        set { _frequencies = value; FrequencyLabel = string.Join(", ", Array.ConvertAll(value, f => $"{f:G}")) + " cy/mm"; }
    }

    private void RefreshWavelengthOptions()
    {
        var system = _session.System;
        int prev = SelectedWavelengthIndex;
        WavelengthOptions.Clear();
        WavelengthOptions.Add("All (Polychromatic)");
        for (int w = 0; w < system.Wavelengths.Count; w++)
            WavelengthOptions.Add($"W{w + 1}: {LabelFormat.Wavelength(system.Wavelengths[w].Value, system.Wavelengths)}");
        if (prev >= 0 && prev < WavelengthOptions.Count)
            SelectedWavelengthIndex = prev;
        else
            SelectedWavelengthIndex = 0;
    }

    [RelayCommand]
    public async Task Compute()
    {
        if (_session.CannotCompute) return;
        if (_frequencies.Length == 0) return;
        IsBusy = true;
        try
        {
            RefreshWavelengthOptions();
            var system = _session.System;
            var glassMgr = _session.GlassCatalog;
            bool polychromatic = SelectedWavelengthIndex == 0;
            int waveIdx = SelectedWavelengthIndex - 1;

            var result = await Task.Run(() =>
                GeometricMtfKidger.ComputeVsFieldMultiFreq(
                    system, glassMgr, _frequencies, polychromatic ? 0 : waveIdx,
                    NumRings, NumFieldPoints, polychromatic: polychromatic));

            _lastResult = result;
            string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
            string title = polychromatic
                ? "Geometric MTF vs Field \u2014 Polychromatic"
                : $"Geometric MTF vs Field \u2014 {LabelFormat.Wavelength(system.Wavelengths[waveIdx].Value, system.Wavelengths)}";
            string svg = MtfVsFieldRenderer.Render(result, title, fieldUnit: fieldUnit);
            PlotImage = SvgHelper.ToBitmap(svg);
        }
        catch { _lastResult = null; PlotImage = null; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task ExportText()
    {
        if (_lastResult == null) return;
        var system = _session.System;
        string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
        var path = await SvgHelper.PickSavePath("Export Geometric MTF vs Field");
        if (path == null) return;
        string text = MtfVsFieldTextExport.Export(_lastResult, "Geometric MTF vs Field", fieldUnit, system.IsAfocal);
        File.WriteAllText(path, text);
    }
}

public partial class GeoMtfVsFocusViewModel : ObservableObject
{
    private readonly GuiSession _session;

    [ObservableProperty] private Bitmap? _plotImage;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isBusy;
    /// <summary>True when the loaded system is afocal — the tab shows a
    /// "disabled" overlay in that case (no defined focus axis for an
    /// afocal output).</summary>
    [ObservableProperty] private bool _isAfocal;
    [ObservableProperty] private int _selectedWavelengthIndex = 0;
    [ObservableProperty] private int _numRings = 30;
    [ObservableProperty] private int _selectedFieldIndex = 0;
    [ObservableProperty] private double _spatialFrequency = 50;
    [ObservableProperty] private double _focusRange = 0.1;
    [ObservableProperty] private int _numFocusSteps = 21;

    public ObservableCollection<string> WavelengthOptions { get; } = new();
    public ObservableCollection<string> FieldOptions { get; } = new();
    private MtfThroughFocusResult? _lastResult;
    private MtfThroughFocusResult[]? _lastResults;
    public MtfThroughFocusResult? LastResult => _lastResult;
    public MtfThroughFocusResult[]? LastResults => _lastResults;

    public GeoMtfVsFocusViewModel(GuiSession session)
    {
        _session = session;
        IsAfocal = _session.System != null && _session.System.IsAfocal;
        _session.SystemChanged += _ => IsAfocal = _session.System != null && _session.System.IsAfocal;
    }

    private void RefreshOptions()
    {
        var system = _session.System;
        int prevW = SelectedWavelengthIndex;
        WavelengthOptions.Clear();
        WavelengthOptions.Add("All (Polychromatic)");
        for (int w = 0; w < system.Wavelengths.Count; w++)
            WavelengthOptions.Add($"W{w + 1}: {LabelFormat.Wavelength(system.Wavelengths[w].Value, system.Wavelengths)}");
        if (prevW >= 0 && prevW < WavelengthOptions.Count)
            SelectedWavelengthIndex = prevW;
        else
            SelectedWavelengthIndex = 0;

        string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
        int prevF = SelectedFieldIndex;
        FieldOptions.Clear();
        FieldOptions.Add("All Fields");
        for (int f = 0; f < system.Fields.Count; f++)
            FieldOptions.Add($"F{f + 1}: {system.Fields[f].Y:F1} {fieldUnit}");
        if (prevF >= 0 && prevF < FieldOptions.Count)
            SelectedFieldIndex = prevF;
        else
            SelectedFieldIndex = 0;
    }

    [RelayCommand]
    public async Task Compute()
    {
        if (_session.CannotCompute) return;
        IsAfocal = _session.System != null && _session.System.IsAfocal;
        if (IsAfocal) { _lastResult = null; _lastResults = null; PlotImage = null; return; }
        IsBusy = true;
        try
        {
            RefreshOptions();
            var system = _session.System;
            var glassMgr = _session.GlassCatalog;
            bool polychromatic = SelectedWavelengthIndex == 0;
            int waveIdx = SelectedWavelengthIndex - 1;
            bool allFields = SelectedFieldIndex == 0;
            int fieldIdx = SelectedFieldIndex - 1;
            string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
            string wlLabel = polychromatic ? "Polychromatic" : $"{LabelFormat.Wavelength(system.Wavelengths[waveIdx].Value, system.Wavelengths)}";

            if (allFields)
            {
                int numFields = system.Fields.Count;
                var results = await Task.Run(() =>
                {
                    var res = new MtfThroughFocusResult[numFields];
                    for (int f = 0; f < numFields; f++)
                        res[f] = GeometricMtfKidger.ComputeThroughFocus(
                            system, glassMgr, f, SpatialFrequency,
                            polychromatic ? 0 : waveIdx,
                            FocusRange, NumFocusSteps, NumRings,
                            polychromatic: polychromatic);
                    return res;
                });

                _lastResults = results;
                _lastResult = null;

                var fieldLabels = new string[numFields];
                for (int f = 0; f < numFields; f++)
                    fieldLabels[f] = $"{system.Fields[f].Y:F1} {fieldUnit}";

                string title = $"Geometric MTF vs Focus \u2014 {SpatialFrequency:G} cy/mm, {wlLabel}";
                string svg = MtfThroughFocusRenderer.RenderAllFields(results, fieldLabels, title);
                PlotImage = SvgHelper.ToBitmap(svg);
            }
            else
            {
                var result = await Task.Run(() =>
                    GeometricMtfKidger.ComputeThroughFocus(
                        system, glassMgr, fieldIdx, SpatialFrequency,
                        polychromatic ? 0 : waveIdx,
                        FocusRange, NumFocusSteps, NumRings,
                        polychromatic: polychromatic));

                _lastResult = result;
                _lastResults = null;

                string title = $"Geometric MTF vs Focus \u2014 {SpatialFrequency:G} cy/mm, {system.Fields[fieldIdx].Y:F1} {fieldUnit}, {wlLabel}";
                string svg = MtfThroughFocusRenderer.Render(result, title);
                PlotImage = SvgHelper.ToBitmap(svg);
            }
        }
        catch { _lastResult = null; _lastResults = null; PlotImage = null; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task ExportText()
    {
        if (_lastResult == null && _lastResults == null) return;
        var path = await SvgHelper.PickSavePath("Export Geometric MTF vs Focus");
        if (path == null) return;
        var system = _session.System;
        string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
        bool polychromatic = SelectedWavelengthIndex == 0;
        int waveIdx = SelectedWavelengthIndex - 1;
        double wlUm = polychromatic ? double.NaN
            : (waveIdx < system.Wavelengths.Count ? system.Wavelengths[waveIdx].Value : double.NaN);

        var sb = new System.Text.StringBuilder();
        if (_lastResults != null)
        {
            for (int f = 0; f < _lastResults.Length; f++)
            {
                double fieldY = f < system.Fields.Count ? system.Fields[f].Y : double.NaN;
                sb.AppendLine(MtfThroughFocusTextExport.Export(_lastResults[f],
                    $"Geometric MTF vs Focus - Field {f + 1}",
                    fieldY, fieldUnit, wlUm));
                sb.AppendLine();
            }
        }
        else if (_lastResult != null)
        {
            int fieldIdx = SelectedFieldIndex - 1;
            double fieldY = fieldIdx >= 0 && fieldIdx < system.Fields.Count ? system.Fields[fieldIdx].Y : double.NaN;
            sb.AppendLine(MtfThroughFocusTextExport.Export(_lastResult,
                "Geometric MTF vs Focus", fieldY, fieldUnit, wlUm));
        }

        File.WriteAllText(path, sb.ToString());
    }
}

/// <summary>Shared SVG-to-bitmap and file picker helpers.</summary>
internal static class SvgHelper
{
    public static Bitmap? ToBitmap(string svg)
    {
        using var skSvg = new SKSvg();
        skSvg.FromSvg(svg);
        if (skSvg.Picture == null) return null;

        var bounds = skSvg.Picture.CullRect;
        int w = (int)bounds.Width; if (w < 100) w = 800;
        int h = (int)bounds.Height; if (h < 100) h = 600;

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

    public static async Task<string?> PickSavePath(string title)
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (topLevel == null) return null;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = title,
                DefaultExtension = "txt",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Text Files") { Patterns = new[] { "*.txt" } }
                }
            });

        return file?.TryGetLocalPath();
    }
}
