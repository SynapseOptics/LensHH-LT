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

public partial class LateralColorViewModel : ObservableObject
{
    private readonly GuiSession _session;

    [ObservableProperty] private Bitmap? _plotImage;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isOnAxisOnly;
    [ObservableProperty] private bool _isDisabled;

    private LateralColorResult? _lastResult;
    public LateralColorResult? LastResult => _lastResult;

    public LateralColorViewModel(GuiSession session)
    {
        _session = session;
        // Reset IsDisabled whenever the system changes so loading a new file
        // with different wavelength/field structure immediately removes (or
        // applies) the "Requires multiple wavelengths and off-axis fields"
        // overlay. Without this, the disabled flag set by the previous
        // file's Compute() would persist until the user explicitly re-ran
        // the analysis.
        _session.SystemChanged += _ => RefreshDisabledFromSystem();
    }

    /// <summary>
    /// Recompute IsDisabled / IsOnAxisOnly from the current session's system
    /// without doing the heavy ray-trace work. Called on system load/replace.
    /// </summary>
    private void RefreshDisabledFromSystem()
    {
        try
        {
            if (!_session.HasSystem) { IsDisabled = true; return; }
            var system = _session.System;

            double maxFieldY = 0;
            foreach (var f in system.Fields)
                if (Math.Abs(f.Y) > maxFieldY) maxFieldY = Math.Abs(f.Y);

            IsOnAxisOnly = maxFieldY < 1e-10;
            IsDisabled = IsOnAxisOnly || system.Wavelengths.Count < 2;

            // Any plot rendered from the previous system is stale the instant
            // the system changes — clear it so the overlay isn't sitting on
            // top of wrong data when the tab is revisited.
            PlotImage = null;
            _lastResult = null;
        }
        catch
        {
            IsDisabled = true;
        }
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

            bool onAxisOnly = maxFieldY < 1e-10;
            bool singleWl = system.Wavelengths.Count < 2;
            IsOnAxisOnly = onAxisOnly;
            IsDisabled = onAxisOnly || singleWl;

            if (IsDisabled)
            {
                PlotImage = null;
                _lastResult = null;
                IsBusy = false;
                return;
            }

            string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
            var waveLabels = new string[system.Wavelengths.Count];
            for (int w = 0; w < system.Wavelengths.Count; w++)
                waveLabels[w] = $"{LabelFormat.Wavelength(system.Wavelengths[w].Value, system.Wavelengths)}";

            var result = await Task.Run(() =>
                LateralColorCalculator.Compute(system, glassMgr, numFieldPoints: 50));

            _lastResult = result;

            // Mirror the renderer's unit branching so title and Y axis agree.
            double titleScale = result.IsAfocal ? 1.0 : 1000.0;
            string titleUnit = result.IsAfocal ? "arcmin" : "\u00b5m";
            string title = $"Lateral Color \u2014 Max = {result.MaxLateralColor * titleScale:F3} {titleUnit}";
            string refLabel = LabelFormat.Wavelength(
                system.Wavelengths[system.PrimaryWavelengthIndex].Value, system.Wavelengths);
            string svg = LateralColorRenderer.Render(result, title, maxFieldY,
                system.Wavelengths.Count, waveLabels, fieldUnit: fieldUnit,
                referenceWavelengthLabel: refLabel);
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

    /// <summary>Copy the lateral color table to the clipboard as tab-delimited text.</summary>
    [RelayCommand]
    public async Task CopyTableToClipboard()
    {
        if (_lastResult == null) return;
        var system = _session.System;
        string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
        var wavelengths = new double[system.Wavelengths.Count];
        for (int w = 0; w < system.Wavelengths.Count; w++)
            wavelengths[w] = system.Wavelengths[w].Value;
        string text = LateralColorTextExport.Export(_lastResult, "Lateral Color", wavelengths, fieldUnit);

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
        var wavelengths = new double[system.Wavelengths.Count];
        for (int w = 0; w < system.Wavelengths.Count; w++)
            wavelengths[w] = system.Wavelengths[w].Value;

        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Export Lateral Color Data",
                DefaultExtension = "txt",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Text Files") { Patterns = new[] { "*.txt" } }
                }
            });

        if (file == null) return;
        var path = file.TryGetLocalPath();
        if (path == null) return;

        string text = LateralColorTextExport.Export(_lastResult, "Lateral Color", wavelengths, fieldUnit);
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
