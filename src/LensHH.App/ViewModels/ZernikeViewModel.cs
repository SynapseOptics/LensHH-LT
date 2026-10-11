using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LensHH.App.Session;
using LensHH.Core.Analysis;
using LensHH.Rendering;
using LensHH.Rendering.TextExport;
using Avalonia.Platform.Storage;

namespace LensHH.App.ViewModels;

public class ZernikeRow
{
    public string Term { get; set; } = "";
    public string Coefficient { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>Zernike coefficients (Standard or Fringe) of one field and wavelength, fitted to the
/// wavefront map.</summary>
public partial class ZernikeViewModel : ObservableObject
{
    private readonly GuiSession _session;

    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _summaryText = "";

    [ObservableProperty] private int _selectedFieldIndex;
    [ObservableProperty] private int _selectedWaveIndex = -1;
    /// <summary>0 = Standard (Noll), 1 = Fringe.</summary>
    [ObservableProperty] private int _typeIndex;
    [ObservableProperty] private int _numTerms = 37;
    [ObservableProperty] private int _gridSize = 64;

    public ObservableCollection<string> FieldOptions { get; } = new();
    public ObservableCollection<string> WaveOptions { get; } = new();
    public int[] GridSizes { get; } = { 32, 64, 128, 256, 512 };

    public ObservableCollection<ZernikeRow> Rows { get; } = new();

    public bool IsFringe => TypeIndex == 1;
    public int MaxTerms => IsFringe ? ZernikeCalculator.MaxFringeTerms : 231;

    private ZernikeResult? _lastResult;
    private string _lastTitle = "";
    private double _lastFieldY = double.NaN, _lastWaveUm = double.NaN;

    public ZernikeViewModel(GuiSession session)
    {
        _session = session;
        _session.SystemChanged += _ => RefreshOptions();
    }

    partial void OnTypeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsFringe));
        OnPropertyChanged(nameof(MaxTerms));
        if (NumTerms > MaxTerms) NumTerms = MaxTerms;
    }

    public void RefreshOptions()
    {
        var system = _session.System;
        int field = SelectedFieldIndex, wave = SelectedWaveIndex;
        FieldOptions.Clear();
        WaveOptions.Clear();

        string unit = system.FieldType == Core.Enums.FieldType.ObjectAngle ? "°" : " mm";
        for (int i = 0; i < system.Fields.Count; i++)
            FieldOptions.Add($"{i + 1}: {system.Fields[i].Y}{unit}");
        for (int i = 0; i < system.Wavelengths.Count; i++)
        {
            string primary = i == system.PrimaryWavelengthIndex ? " *" : "";
            WaveOptions.Add($"{i + 1}: {LabelFormat.Wavelength(system.Wavelengths[i].Value, system.Wavelengths)}{primary}");
        }

        SelectedFieldIndex = field >= 0 && field < FieldOptions.Count ? field : 0;
        SelectedWaveIndex = wave >= 0 && wave < WaveOptions.Count ? wave : system.PrimaryWavelengthIndex;
    }

    [RelayCommand]
    public async Task Compute()
    {
        if (_session.CannotCompute) return;
        IsBusy = true;
        StatusText = "";
        try
        {
            var system = _session.System;
            var glassMgr = _session.GlassCatalog;
            if (FieldOptions.Count == 0 || SelectedWaveIndex < 0) RefreshOptions();

            int field = Math.Clamp(SelectedFieldIndex, 0, system.Fields.Count - 1);
            int wave = Math.Clamp(SelectedWaveIndex, 0, system.Wavelengths.Count - 1);
            bool fringe = IsFringe;
            int terms = Math.Clamp(NumTerms, 1, MaxTerms);
            int grid = GridSize;

            var result = await Task.Run(() => fringe
                ? ZernikeCalculator.ComputeFringe(system, glassMgr, field, wave, terms, grid)
                : ZernikeCalculator.ComputeStandard(system, glassMgr, field, wave, terms, grid));

            string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectAngle ? "deg" : "mm";
            _lastResult = result;
            _lastFieldY = system.Fields[field].Y;
            _lastWaveUm = system.Wavelengths[wave].Value;
            _lastTitle = $"{(fringe ? "Fringe" : "Standard")} Zernike Coefficients";

            Rows.Clear();
            for (int k = 0; k < result.Coefficients.Length; k++)
                Rows.Add(new ZernikeRow
                {
                    Term = $"Z{k + 1}",
                    Coefficient = result.Coefficients[k].ToString("F8", CultureInfo.InvariantCulture),
                    Name = fringe ? ZernikeCalculator.GetFringeTermName(k + 1) : ZernikeCalculator.GetStandardTermName(k + 1),
                });

            string wlFmt = "F" + LabelFormat.WavelengthDigits(system.Wavelengths);
            SummaryText = $"{(fringe ? "Fringe" : "Standard (Noll)")}, field {field + 1}: {_lastFieldY.ToString("G6", CultureInfo.InvariantCulture)} {fieldUnit}, "
                + $"wavelength {wave + 1}: {_lastWaveUm.ToString(wlFmt, CultureInfo.InvariantCulture)} µm, grid {grid} × {grid}.   "
                + $"P-V {result.PeakToValley:F6} waves,  RMS {result.RmsWavefront:F6} waves,  RMS fit residual {result.RmsFit:E3} waves";
            if (result.Error != null)
                StatusText = "The fit failed: " + result.Error;
        }
        catch (Exception ex)
        {
            _lastResult = null;
            Rows.Clear();
            SummaryText = "";
            StatusText = "Error: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Drop the shown result so a stale one is not mistaken for the current lens's.</summary>
    public void Clear()
    {
        _lastResult = null;
        Rows.Clear();
        SummaryText = "";
    }

    private string? Text()
    {
        if (_lastResult == null) return null;
        string fieldUnit = _session.System.FieldType == Core.Enums.FieldType.ObjectAngle ? "deg" : "mm";
        return ZernikeTextExport.Export(_lastResult, _lastTitle, _lastFieldY, _lastWaveUm, fieldUnit);
    }

    [RelayCommand]
    public async Task CopyTableToClipboard()
    {
        var text = Text();
        if (text == null) return;
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (topLevel?.Clipboard is { } clip)
            await clip.SetTextAsync(text);
    }

    [RelayCommand]
    public async Task ExportText()
    {
        var text = Text();
        if (text == null) return;
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Export Zernike Coefficients",
                DefaultExtension = "txt",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Text Files") { Patterns = new[] { "*.txt" } }
                }
            });
        if (file == null) return;
        var path = file.TryGetLocalPath();
        if (path == null) return;
        File.WriteAllText(path, text);
    }
}
