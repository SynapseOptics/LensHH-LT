using System;
using System.Globalization;
using System.IO;
using System.Linq;
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

public partial class LongitudinalAberrationViewModel : ObservableObject
{
    private readonly GuiSession _session;

    [ObservableProperty] private Bitmap? _plotImage;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isDisabled;
    [ObservableProperty] private int _numZones = 32;

    private LongitudinalAberrationResult? _lastResult;
    public LongitudinalAberrationResult? LastResult => _lastResult;

    public LongitudinalAberrationViewModel(GuiSession session)
    {
        _session = session;
        _session.SystemChanged += _ => RefreshDisabledFromSystem();
    }

    private void RefreshDisabledFromSystem()
    {
        try
        {
            if (!_session.HasSystem) { IsDisabled = true; PlotImage = null; _lastResult = null; return; }
            // LSA itself is meaningful for any focal system; disable only for afocal.
            IsDisabled = _session.System.IsAfocal;
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

            IsDisabled = system.IsAfocal;
            if (IsDisabled)
            {
                PlotImage = null;
                _lastResult = null;
                IsBusy = false;
                return;
            }

            int zones = NumZones;
            var result = await Task.Run(() =>
                LensHH.Core.Analysis.LongitudinalAberration.Compute(system, glassMgr, numZones: zones));

            _lastResult = result;

            int wlDigits = LabelFormat.WavelengthDigits(result.WavelengthsUm);
            string wlFmt = "F" + wlDigits;
            var waveLabels = system.Wavelengths
                .Select(w => w.Value.ToString(wlFmt, CultureInfo.InvariantCulture) + " µm")
                .ToArray();

            string title = "Longitudinal Aberration";
            string svg = LongitudinalAberrationRenderer.Render(result, title, null, waveLabels);
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
                Title = "Export Longitudinal Aberration Data",
                DefaultExtension = "txt",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Text Files") { Patterns = new[] { "*.txt" } }
                }
            });

        if (file == null) return;
        var path = file.TryGetLocalPath();
        if (path == null) return;

        string text = LongitudinalAberrationTextExport.Export(_lastResult, "Longitudinal Aberration");
        File.WriteAllText(path, text);
    }

    [RelayCommand]
    public async Task CopyTableToClipboard()
    {
        if (_lastResult == null) return;
        string text = LongitudinalAberrationTextExport.Export(_lastResult, "Longitudinal Aberration");
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (topLevel?.Clipboard is { } clip)
            await clip.SetTextAsync(text);
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
