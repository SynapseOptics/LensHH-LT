using System;
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

public partial class FieldCurvatureViewModel : ObservableObject
{
    private readonly GuiSession _session;

    [ObservableProperty] private Bitmap? _plotImage;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isOnAxisOnly;

    private MultiWavelengthFieldCurvatureResult? _lastMwResult;
    public MultiWavelengthFieldCurvatureResult? LastMwResult => _lastMwResult;

    public FieldCurvatureViewModel(GuiSession session)
    {
        _session = session;
        // Flip IsOnAxisOnly to match the incoming system so loading a new
        // file with off-axis fields clears the disabled overlay immediately.
        _session.SystemChanged += _ => RefreshOnAxisFromSystem();
    }

    private void RefreshOnAxisFromSystem()
    {
        try
        {
            if (!_session.HasSystem) { IsOnAxisOnly = true; PlotImage = null; _lastMwResult = null; return; }
            double maxFieldY = 0;
            foreach (var f in _session.System.Fields)
                if (Math.Abs(f.Y) > maxFieldY) maxFieldY = Math.Abs(f.Y);
            IsOnAxisOnly = maxFieldY < 1e-10;
            PlotImage = null;
            _lastMwResult = null;
        }
        catch { IsOnAxisOnly = true; }
    }

    [RelayCommand]
    public async Task Compute()
    {
        if (_session.CannotCompute) return;
        IsBusy = true;
        try
        {
            var system = _session.System;
            var glassMgr = _session.GlassCatalog;

            double maxFieldY = 0;
            foreach (var f in system.Fields)
                if (Math.Abs(f.Y) > maxFieldY) maxFieldY = Math.Abs(f.Y);
            if (maxFieldY < 1e-10)
            {
                IsOnAxisOnly = true;
                PlotImage = null;
                _lastMwResult = null;
                IsBusy = false;
                return;
            }
            IsOnAxisOnly = false;

            string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
            var waveLabels = new string[system.Wavelengths.Count];
            for (int w = 0; w < system.Wavelengths.Count; w++)
                waveLabels[w] = $"{system.Wavelengths[w].Value:F6} \u00b5m";

            var mwResult = await Task.Run(() =>
                FieldCurvatureCalculator.ComputeAllWavelengths(system, glassMgr, numPoints: 100));

            _lastMwResult = mwResult;

            string svg = FieldCurvatureRenderer.RenderMultiWavelength(mwResult,
                "Field Curvature", waveLabels, fieldUnit: fieldUnit);
            RenderSvgToBitmap(svg);
        }
        catch
        {
            _lastMwResult = null;
            PlotImage = null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Copy the field-curvature table to the clipboard as tab-delimited text.</summary>
    [RelayCommand]
    public async Task CopyTableToClipboard()
    {
        if (_lastMwResult == null) return;
        var system = _session.System;
        string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";

        int wlDigits = LensHH.Rendering.LabelFormat.WavelengthDigits(_lastMwResult.Wavelengths);
        string wlFmt = "F" + wlDigits;
        var sb = new System.Text.StringBuilder();
        for (int w = 0; w < _lastMwResult.PerWavelength.Count; w++)
        {
            string wlLabel = $"Wavelength {w + 1}: {_lastMwResult.Wavelengths[w].ToString(wlFmt, System.Globalization.CultureInfo.InvariantCulture)} \u00b5m";
            sb.AppendLine(FieldCurvatureTextExport.Export(_lastMwResult.PerWavelength[w], wlLabel, fieldUnit));
            sb.AppendLine();
        }

        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (topLevel?.Clipboard is { } clip)
            await clip.SetTextAsync(sb.ToString());
    }

    [RelayCommand]
    public async Task ExportText()
    {
        if (_lastMwResult == null) return;

        var system = _session.System;
        string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";

        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Export Field Curvature Data",
                DefaultExtension = "txt",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Text Files") { Patterns = new[] { "*.txt" } }
                }
            });

        if (file == null) return;
        var path = file.TryGetLocalPath();
        if (path == null) return;

        // Export each wavelength's data
        int wlDigits = LensHH.Rendering.LabelFormat.WavelengthDigits(_lastMwResult.Wavelengths);
        string wlFmt = "F" + wlDigits;
        var sb = new System.Text.StringBuilder();
        for (int w = 0; w < _lastMwResult.PerWavelength.Count; w++)
        {
            string wlLabel = $"Wavelength {w + 1}: {_lastMwResult.Wavelengths[w].ToString(wlFmt, System.Globalization.CultureInfo.InvariantCulture)} \u00b5m";
            sb.AppendLine(FieldCurvatureTextExport.Export(_lastMwResult.PerWavelength[w], wlLabel, fieldUnit));
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

            const int scale = 2;
            using var bitmap = new SkiaSharp.SKBitmap(w * scale, h * scale);
            using var canvas = new SkiaSharp.SKCanvas(bitmap);
            canvas.Clear(SkiaSharp.SKColors.White);
            canvas.Scale(scale);
            canvas.DrawPicture(skSvg.Picture);

            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream(data.ToArray());
            PlotImage = new Bitmap(stream);
        }
    }
}
