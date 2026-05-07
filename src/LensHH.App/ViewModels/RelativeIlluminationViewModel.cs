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

public partial class RelativeIlluminationViewModel : ObservableObject
{
    private readonly GuiSession _session;

    [ObservableProperty] private Bitmap? _plotImage;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isOnAxisOnly;

    // Resolution knobs surfaced in the Rel. Illumination toolbar so users can
    // smooth jagged plots by sampling more pupil directions / field points.
    // Defaults match the engine's defaults; clamped on use.
    [ObservableProperty] private int _numFieldPoints = 50;
    [ObservableProperty] private int _numPupilRays = 36;

    private RelativeIlluminationResult? _lastResult;
    public RelativeIlluminationResult? LastResult => _lastResult;

    public RelativeIlluminationViewModel(GuiSession session)
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

            int fieldPts = Math.Max(2, NumFieldPoints);
            int pupilRays = Math.Max(8, NumPupilRays);
            var result = await Task.Run(() =>
                RelativeIlluminationCalculator.Compute(system, glassMgr,
                    numFieldPoints: fieldPts, numPupilRays: pupilRays));

            _lastResult = result;

            string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
            string svg = RelativeIlluminationRenderer.Render(result,
                "Relative Illumination", fieldUnit: fieldUnit);
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

        var system = _session.System;
        string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";

        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Export Relative Illumination Data",
                DefaultExtension = "txt",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Text Files") { Patterns = new[] { "*.txt" } }
                }
            });

        if (file == null) return;
        var path = file.TryGetLocalPath();
        if (path == null) return;

        string text = RelativeIlluminationTextExport.Export(_lastResult,
            "Relative Illumination", fieldUnit);
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
