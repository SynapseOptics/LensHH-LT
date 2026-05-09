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
using SkiaSharp;
using Svg.Skia;

namespace LensHH.App.ViewModels;

public partial class SystemDataViewModel : ObservableObject
{
    private readonly GuiSession _session;

    [ObservableProperty] private Bitmap? _plotImage;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isBusy;

    private SystemDataResult? _lastResult;
    public SystemDataResult? LastResult => _lastResult;

    public SystemDataViewModel(GuiSession session)
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

            var (result, bitmap) = await Task.Run(() =>
            {
                var r = SystemDataCalculator.Calculate(system, glassMgr);
                string svg = SystemDataRenderer.Render(r, system.Title ?? "System Data");
                var bmp = RenderSvgToBitmap(svg);
                return (r, bmp);
            });

            _lastResult = result;
            PlotImage = bitmap;
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

    /// <summary>Copy the system-data table to the clipboard as tab-delimited text.</summary>
    [RelayCommand]
    public async Task CopyTableToClipboard()
    {
        if (_lastResult == null) return;
        string text = SystemDataTextExport.Export(_lastResult, _session.System, _session.GlassCatalog, "System Data");

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
                Title = "Export System Data",
                DefaultExtension = "txt",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Text Files") { Patterns = new[] { "*.txt" } }
                }
            });

        if (file == null) return;
        var path = file.TryGetLocalPath();
        if (path == null) return;

        string text = SystemDataTextExport.Export(_lastResult, _session.System, _session.GlassCatalog, "System Data");
        File.WriteAllText(path, text);
    }

    private static Bitmap RenderSvgToBitmap(string svg)
    {
        using var skSvg = new SKSvg();
        skSvg.FromSvg(svg);

        if (skSvg.Picture != null)
        {
            var bounds = skSvg.Picture.CullRect;
            int w = (int)bounds.Width;
            int h = (int)bounds.Height;
            if (w < 100) w = 500;
            if (h < 100) h = 600;

            const int scale = 2;
            using var bitmap = new SKBitmap(w * scale, h * scale);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White);
            canvas.Scale(scale);
            canvas.DrawPicture(skSvg.Picture);

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream(data.ToArray());
            return new Bitmap(stream);
        }

        return null!;
    }
}
