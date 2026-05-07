using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using TPL = System.Threading.Tasks;
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

public partial class FftMtfViewModel : ObservableObject
{
    private readonly GuiSession _session;

    [ObservableProperty] private Bitmap? _mtfImage;
    [ObservableProperty] private double _maxFrequency = 0; // 0 = auto
    [ObservableProperty] private int _gridSize = 256;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private int _selectedWavelengthIndex = 0; // 0 = All (Polychromatic)
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<string> WavelengthOptions { get; } = new();

    private MtfResult[]? _lastFieldResults; // one per field (current wavelength selection)
    private string[]? _lastFieldLabels;
    public MtfResult[]? LastFieldResults => _lastFieldResults;
    public string[]? LastFieldLabels => _lastFieldLabels;

    public FftMtfViewModel(GuiSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Rebuild wavelength combo options from current system.
    /// </summary>
    private void RefreshWavelengthOptions()
    {
        var system = _session.System;
        int prevIndex = SelectedWavelengthIndex;
        WavelengthOptions.Clear();
        WavelengthOptions.Add("All (Polychromatic)");
        for (int w = 0; w < system.Wavelengths.Count; w++)
            WavelengthOptions.Add($"W{w + 1}: {system.Wavelengths[w].Value:F4} \u00b5m");

        // Restore previous selection if still valid
        if (prevIndex >= 0 && prevIndex < WavelengthOptions.Count)
            SelectedWavelengthIndex = prevIndex;
        else
            SelectedWavelengthIndex = 0;
    }

    [RelayCommand]
    public async Task Compute()
    {
        if (_session.CannotCompute) return;
        RefreshWavelengthOptions();
        IsBusy = true;

        try
        {
            var system = _session.System;
            var glassMgr = _session.GlassCatalog;
            int numFields = system.Fields.Count;
            // With only 1 wavelength, use monochromatic path (polychromatic is identical but has more overhead)
            bool polychromatic = SelectedWavelengthIndex == 0 && system.Wavelengths.Count > 1;
            int waveIdx = polychromatic ? 0 : Math.Max(0, SelectedWavelengthIndex - 1);
            int gridSize = GridSize;
            double maxFreqSetting = MaxFrequency;

            string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
            var fieldLabels = new string[numFields];
            for (int f = 0; f < numFields; f++)
                fieldLabels[f] = $"{system.Fields[f].Y:F1} {fieldUnit}";

            var fieldResults = await Task.Run(() =>
            {
                var results = new MtfResult[numFields];
                double freqStep = 1.0;

                if (polychromatic)
                    results[0] = FftMtfCalculator.ComputePolychromatic(
                        system, glassMgr, 0, gridSize, freqStep);
                else
                    results[0] = FftMtfCalculator.ComputeVsFrequency(
                        system, glassMgr, 0, waveIdx, gridSize, freqStep);

                if (results[0].MaxFrequency > 0)
                    freqStep = results[0].MaxFrequency / 200.0;

                if (numFields > 1)
                {
                    double fs = freqStep;
                    TPL.Parallel.For(1, numFields, f =>
                    {
                        try
                        {
                            if (polychromatic)
                                results[f] = FftMtfCalculator.ComputePolychromatic(
                                    system, glassMgr, f, gridSize, fs);
                            else
                                results[f] = FftMtfCalculator.ComputeVsFrequency(
                                    system, glassMgr, f, waveIdx, gridSize, fs);
                        }
                        catch
                        {
                            // Return empty result for fields that fail (e.g. extreme wide-angle)
                            results[f] = new MtfResult { FieldIndex = f, MaxFrequency = results[0]?.MaxFrequency ?? 100 };
                        }
                    });
                }
                return results;
            });

            _lastFieldResults = fieldResults;
            _lastFieldLabels = fieldLabels;

            double maxFreq = maxFreqSetting;
            if (maxFreq <= 0)
            {
                maxFreq = 0;
                foreach (var r in fieldResults)
                    if (r.MaxFrequency > maxFreq) maxFreq = r.MaxFrequency;
            }

            double onAxisCutoff = fieldResults.Length > 0 ? fieldResults[0].MaxFrequency : 0;

            string title = polychromatic
                ? "FFT MTF \u2014 Polychromatic"
                : $"FFT MTF \u2014 {system.Wavelengths[waveIdx].Value:F4} \u00b5m";

            string svg = FftMtfRenderer.RenderAllFields(
                fieldResults, fieldLabels, title,
                maxFrequency: maxFreq, onAxisCutoff: onAxisCutoff);

            RenderSvgToBitmap(svg);
        }
        catch
        {
            _lastFieldResults = null;
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
        if (_lastFieldResults == null || _lastFieldLabels == null) return;

        var system = _session.System;
        string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";

        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Export FFT MTF Data",
                DefaultExtension = "txt",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Text Files") { Patterns = new[] { "*.txt" } }
                }
            });

        if (file == null) return;
        var path = file.TryGetLocalPath();
        if (path == null) return;

        bool polychromatic = SelectedWavelengthIndex == 0;
        int waveIdx = SelectedWavelengthIndex - 1;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("FFT MTF vs Spatial Frequency");
        sb.AppendLine($"Grid Size: {GridSize}");
        if (polychromatic)
            sb.AppendLine("Wavelength: Polychromatic");
        else
            sb.AppendLine($"Wavelength: {system.Wavelengths[waveIdx].Value:F4} um");
        sb.AppendLine();

        for (int f = 0; f < _lastFieldResults.Length; f++)
        {
            var result = _lastFieldResults[f];
            double fieldY = system.Fields[f].Y;
            double cutT = result.CutoffT > 0 ? result.CutoffT : result.MaxFrequency;
            double cutS = result.CutoffS > 0 ? result.CutoffS : result.MaxFrequency;

            string label = polychromatic
                ? $"Field {f + 1}: {fieldY:F1} {fieldUnit}, Polychromatic"
                : $"Field {f + 1}: {fieldY:F1} {fieldUnit}, Wave {waveIdx + 1}: {system.Wavelengths[waveIdx].Value:F4} um";

            sb.AppendLine(FftMtfTextExport.Export(result,
                label, cutT, cutS, fieldY,
                polychromatic ? double.NaN : system.Wavelengths[waveIdx].Value,
                fieldUnit, system.IsAfocal));
            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
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
