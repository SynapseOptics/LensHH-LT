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

public partial class ChromaticFocalShiftViewModel : ObservableObject
{
    private readonly GuiSession _session;

    [ObservableProperty] private Bitmap? _plotImage;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isDisabled;

    private ChromaticFocalShiftResult? _lastResult;
    public ChromaticFocalShiftResult? LastResult => _lastResult;

    public ChromaticFocalShiftViewModel(GuiSession session)
    {
        _session = session;
        // Refresh IsDisabled every time the session's system is replaced so
        // loading a multi-wavelength file after a single-wavelength one
        // (or vice versa) flips the disabled overlay immediately, instead
        // of leaving the stale flag from the previous Compute() run.
        _session.SystemChanged += _ => RefreshDisabledFromSystem();
    }

    private void RefreshDisabledFromSystem()
    {
        try
        {
            if (!_session.HasSystem) { IsDisabled = true; PlotImage = null; _lastResult = null; return; }
            IsDisabled = _session.System.Wavelengths.Count < 2;
            PlotImage = null;
            _lastResult = null;
        }
        catch { IsDisabled = true; }
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

            bool singleWl = system.Wavelengths.Count < 2;
            IsDisabled = singleWl;

            if (IsDisabled)
            {
                PlotImage = null;
                _lastResult = null;
                IsBusy = false;
                return;
            }

            var result = await Task.Run(() =>
                ChromaticFocalShift.Compute(system, glassMgr, numPoints: 50));

            _lastResult = result;

            string unit = result.IsAfocal ? "diopters" : "mm";
            string title = $"Chromatic Focal Shift \u2014 Range = {result.MaxShift:F4} {unit}";
            string svg = ChromaticFocalShiftRenderer.Render(result, title);
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
                Title = "Export Chromatic Focal Shift Data",
                DefaultExtension = "txt",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Text Files") { Patterns = new[] { "*.txt" } }
                }
            });

        if (file == null) return;
        var path = file.TryGetLocalPath();
        if (path == null) return;

        string text = ChromaticFocalShiftTextExport.Export(_lastResult, "Chromatic Focal Shift");
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
