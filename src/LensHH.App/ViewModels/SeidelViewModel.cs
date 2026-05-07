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

public partial class SeidelViewModel : ObservableObject
{
    private readonly GuiSession _session;

    [ObservableProperty] private Bitmap? _plotImage;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isBusy;

    private SeidelResult? _lastResult;
    public SeidelResult? LastResult => _lastResult;

    public SeidelViewModel(GuiSession session)
    {
        _session = session;
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

            var result = await Task.Run(() =>
                SeidelCalculator.Calculate(system, glassMgr));

            _lastResult = result;

            // The bar chart already includes the SUM column, so a separate
            // totals plot would duplicate information.
            string svg = SeidelRenderer.Render(result, "Seidel Aberration Coefficients");
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

    /// <summary>Copy the Seidel coefficient table to the clipboard as tab-delimited text.</summary>
    [RelayCommand]
    public async Task CopyTableToClipboard()
    {
        if (_lastResult == null) return;
        string text = SeidelTextExport.Export(_lastResult, "Seidel Aberration Coefficients");

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

        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Export Seidel Coefficients",
                DefaultExtension = "txt",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Text Files") { Patterns = new[] { "*.txt" } }
                }
            });

        if (file == null) return;
        var path = file.TryGetLocalPath();
        if (path == null) return;

        string text = SeidelTextExport.Export(_lastResult, "Seidel Aberration Coefficients");
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
