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

public partial class DistortionViewModel : ObservableObject
{
    private readonly GuiSession _session;

    [ObservableProperty] private Bitmap? _plotImage;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isOnAxisOnly;
    [ObservableProperty] private int _selectedTypeIndex = 0; // 0=F-Tan(Theta), 1=F-Theta

    private DistortionResult? _lastResult;
    public DistortionResult? LastResult => _lastResult;

    public DistortionViewModel(GuiSession session)
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
            if (!_session.HasSystem) { IsOnAxisOnly = true; PlotImage = null; _lastResult = null; return; }
            double maxFieldY = 0;
            foreach (var f in _session.System.Fields)
                if (Math.Abs(f.Y) > maxFieldY) maxFieldY = Math.Abs(f.Y);
            IsOnAxisOnly = maxFieldY < 1e-10;
            PlotImage = null;
            _lastResult = null;
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

            // Check if all fields are on-axis
            double maxFieldY = 0;
            foreach (var f in system.Fields)
                if (Math.Abs(f.Y) > maxFieldY) maxFieldY = Math.Abs(f.Y);
            if (maxFieldY < 1e-10)
            {
                IsOnAxisOnly = true;
                PlotImage = null;
                _lastResult = null;
                IsBusy = false;
                return;
            }
            IsOnAxisOnly = false;

            var distType = SelectedTypeIndex == 0 ? DistortionType.FTanTheta : DistortionType.FTheta;

            var result = await Task.Run(() =>
                DistortionCalculator.Compute(system, glassMgr, distType, numPoints: 100));

            _lastResult = result;

            string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
            string typeLabel = distType == DistortionType.FTanTheta ? "F-Tan(\u03b8)" : "F-\u03b8";
            string title = $"Distortion \u2014 {typeLabel}, Max = {result.MaxDistortion:F3}%";

            string svg = DistortionRenderer.Render(result, title, fieldUnit: fieldUnit);
            RenderSvgToBitmap(svg);
        }
        catch
        {
            _lastResult = null;
            PlotImage = null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Copy the distortion table to the clipboard as tab-delimited text.</summary>
    [RelayCommand]
    public async Task CopyTableToClipboard()
    {
        if (_lastResult == null) return;
        var system = _session.System;
        string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
        string text = DistortionTextExport.Export(_lastResult, "Distortion", fieldUnit);

        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (topLevel?.Clipboard is { } clip)
            await clip.SetTextAsync(text);
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
                Title = "Export Distortion Data",
                DefaultExtension = "txt",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Text Files") { Patterns = new[] { "*.txt" } }
                }
            });

        if (file == null) return;
        var path = file.TryGetLocalPath();
        if (path == null) return;

        string text = DistortionTextExport.Export(_lastResult, "Distortion", fieldUnit);
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
