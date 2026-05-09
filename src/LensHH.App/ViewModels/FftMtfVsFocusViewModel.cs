using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LensHH.App.Session;
using LensHH.Core.Analysis;
using LensHH.Rendering;
using LensHH.Rendering.TextExport;
using Avalonia.Platform.Storage;

namespace LensHH.App.ViewModels;

public partial class FftMtfVsFocusViewModel : ObservableObject
{
    private readonly GuiSession _session;

    [ObservableProperty] private Bitmap? _plotImage;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isBusy;
    /// <summary>True when the loaded system is afocal — the tab shows a
    /// "disabled" overlay in that case (semantics of focus shift in diopters
    /// are still being settled).</summary>
    [ObservableProperty] private bool _isAfocal;
    [ObservableProperty] private int _selectedWavelengthIndex = 0; // 0=Polychromatic
    [ObservableProperty] private int _selectedFieldIndex = 0; // 0=All Fields
    [ObservableProperty] private int _gridSize = 64;
    [ObservableProperty] private double _spatialFrequency = 50;
    [ObservableProperty] private double _focusRange = 0.1;
    [ObservableProperty] private int _numFocusSteps = 41;

    /// <summary>Toolbar label text that flips with afocal mode.</summary>
    [ObservableProperty] private string _frequencyUnitLabel = "Freq (cy/mm):";
    [ObservableProperty] private string _focusRangeUnitLabel = "Focus Range (mm):";

    public ObservableCollection<string> WavelengthOptions { get; } = new();
    public ObservableCollection<string> FieldOptions { get; } = new();
    private MtfThroughFocusResult? _lastResult;
    private MtfThroughFocusResult[]? _lastResults;
    public MtfThroughFocusResult? LastResult => _lastResult;
    public MtfThroughFocusResult[]? LastResults => _lastResults;

    // Defaults for afocal MTF-vs-Focus plots.
    private const double AfocalDefaultFrequency = 0.25; // cy/arc-min
    private const double AfocalDefaultFocusRange = 1.0; // diopters (±1 dpt)
    private const double FocalDefaultFrequency = 50.0;  // cy/mm
    private const double FocalDefaultFocusRange = 0.1;  // mm

    public FftMtfVsFocusViewModel(GuiSession session)
    {
        _session = session;
        ApplyDefaultsForMode();
        // Re-apply defaults whenever the session's system is swapped out or
        // the user toggles afocal mode — user asked for this: loading a new
        // file or changing afocal mode should immediately push the toolbar
        // back to mode-appropriate defaults + labels.
        _session.SystemChanged += OnSessionSystemChanged;
    }

    private void OnSessionSystemChanged(string sender)
    {
        ApplyDefaultsForMode();
    }

    /// <summary>
    /// Reset SpatialFrequency, FocusRange, and the unit labels so they
    /// track the session's current focal/afocal mode. Safe to call at any
    /// time; purely reactive to the live system state.
    /// </summary>
    public void ApplyDefaultsForMode()
    {
        bool afocal = _session.System != null && _session.System.IsAfocal;
        IsAfocal = afocal;
        if (afocal)
        {
            SpatialFrequency = AfocalDefaultFrequency;
            FocusRange = AfocalDefaultFocusRange;
            FrequencyUnitLabel = "Freq (cy/arc-min):";
            FocusRangeUnitLabel = "Focus Range (diopters):";
        }
        else
        {
            SpatialFrequency = FocalDefaultFrequency;
            FocusRange = FocalDefaultFocusRange;
            FrequencyUnitLabel = "Freq (cy/mm):";
            FocusRangeUnitLabel = "Focus Range (mm):";
        }
    }

    private void RefreshOptions()
    {
        var system = _session.System;
        int prevW = SelectedWavelengthIndex;
        WavelengthOptions.Clear();
        WavelengthOptions.Add("All (Polychromatic)");
        for (int w = 0; w < system.Wavelengths.Count; w++)
            WavelengthOptions.Add($"W{w + 1}: {LabelFormat.Wavelength(system.Wavelengths[w].Value, system.Wavelengths)}");
        SelectedWavelengthIndex = prevW >= 0 && prevW < WavelengthOptions.Count ? prevW : 0;

        string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
        int prevF = SelectedFieldIndex;
        FieldOptions.Clear();
        FieldOptions.Add("All Fields");
        for (int f = 0; f < system.Fields.Count; f++)
            FieldOptions.Add($"F{f + 1}: {system.Fields[f].Y:F1} {fieldUnit}");
        SelectedFieldIndex = prevF >= 0 && prevF < FieldOptions.Count ? prevF : 0;
    }

    [RelayCommand]
    public async Task Compute()
    {
        if (_session.CannotCompute) return;
        if (_session.System.IsAfocal) { _lastResult = null; _lastResults = null; PlotImage = null; return; }
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
            double freq = SpatialFrequency;
            double range = FocusRange;
            int steps = NumFocusSteps;
            int grid = GridSize;

            if (allFields)
            {
                int numFields = system.Fields.Count;
                var results = await Task.Run(() =>
                {
                    var res = new MtfThroughFocusResult[numFields];
                    for (int f = 0; f < numFields; f++)
                    {
                        if (polychromatic)
                            res[f] = FftMtfCalculator.ComputeThroughFocusPolychromatic(
                                system, glassMgr, f, freq, range, steps, grid);
                        else
                            res[f] = FftMtfCalculator.ComputeThroughFocus(
                                system, glassMgr, f, freq, waveIdx, range, steps, grid);
                    }
                    return res;
                });

                _lastResults = results;
                _lastResult = null;

                var fieldLabels = new string[numFields];
                for (int f = 0; f < numFields; f++)
                    fieldLabels[f] = $"{system.Fields[f].Y:F1} {fieldUnit}";

                string freqUnit = system.IsAfocal ? "cy/arc-min" : "cy/mm";
                string title = $"FFT MTF vs Focus \u2014 {freq:G} {freqUnit}, {wlLabel}";
                string svg = MtfThroughFocusRenderer.RenderAllFields(results, fieldLabels, title);
                PlotImage = SvgHelper.ToBitmap(svg);
            }
            else
            {
                var result = await Task.Run(() =>
                {
                    if (polychromatic)
                        return FftMtfCalculator.ComputeThroughFocusPolychromatic(
                            system, glassMgr, fieldIdx, freq, range, steps, grid);
                    else
                        return FftMtfCalculator.ComputeThroughFocus(
                            system, glassMgr, fieldIdx, freq, waveIdx, range, steps, grid);
                });

                _lastResult = result;
                _lastResults = null;

                string freqUnit = system.IsAfocal ? "cy/arc-min" : "cy/mm";
                string title = $"FFT MTF vs Focus \u2014 {freq:G} {freqUnit}, {system.Fields[fieldIdx].Y:F1} {fieldUnit}, {wlLabel}";
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
        var path = await SvgHelper.PickSavePath("Export FFT MTF vs Focus");
        if (path == null) return;
        var system = _session.System;
        string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
        bool polychromatic = SelectedWavelengthIndex == 0;
        int waveIdx = SelectedWavelengthIndex - 1;
        double wlUm = polychromatic ? double.NaN
            : (waveIdx < system.Wavelengths.Count ? system.Wavelengths[waveIdx].Value : double.NaN);

        var sb = new StringBuilder();
        if (_lastResults != null)
        {
            for (int f = 0; f < _lastResults.Length; f++)
            {
                double fieldY = f < system.Fields.Count ? system.Fields[f].Y : double.NaN;
                sb.AppendLine(MtfThroughFocusTextExport.Export(_lastResults[f],
                    $"FFT MTF vs Focus - Field {f + 1}", fieldY, fieldUnit, wlUm));
                sb.AppendLine();
            }
        }
        else if (_lastResult != null)
        {
            int fieldIdx = SelectedFieldIndex - 1;
            double fieldY = fieldIdx >= 0 && fieldIdx < system.Fields.Count ? system.Fields[fieldIdx].Y : double.NaN;
            sb.AppendLine(MtfThroughFocusTextExport.Export(_lastResult,
                "FFT MTF vs Focus", fieldY, fieldUnit, wlUm));
        }

        File.WriteAllText(path, sb.ToString());
    }
}
