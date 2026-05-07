using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
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

public partial class FftPsfViewModel : ObservableObject
{
    private readonly GuiSession _session;

    [ObservableProperty] private Bitmap? _psfImage;
    [ObservableProperty] private int _gridSize = 64;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private int _selectedWavelengthIndex = 0;
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<string> WavelengthOptions { get; } = new();

    private PsfResult[]? _lastResults;
    public PsfResult[]? LastResults => _lastResults;

    public FftPsfViewModel(GuiSession session)
    {
        _session = session;
    }

    private void RefreshWavelengthOptions()
    {
        var system = _session.System;
        int prevIndex = SelectedWavelengthIndex;
        WavelengthOptions.Clear();
        for (int w = 0; w < system.Wavelengths.Count; w++)
            WavelengthOptions.Add($"W{w + 1}: {system.Wavelengths[w].Value:F4} \u00b5m");

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
            int waveIdx = Math.Max(0, SelectedWavelengthIndex);
            int gridSize = GridSize;

            string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";

            var titles = new string[numFields];
            for (int f = 0; f < numFields; f++)
                titles[f] = $"F{f + 1}: {system.Fields[f].Y:F1} {fieldUnit}, {system.Wavelengths[waveIdx].Value:F4} \u00b5m";

            var bitmap = await Task.Run(() =>
            {
                var results = new PsfResult[numFields];
                for (int f = 0; f < numFields; f++)
                    results[f] = FftPsfCalculator.Compute(system, glassMgr, f, waveIdx, gridSize);

                _lastResults = results;

                return RenderToBitmap(results, titles);
            });

            PsfImage = bitmap;
        }
        catch
        {
            _lastResults = null;
            PsfImage = null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static Bitmap RenderToBitmap(PsfResult[] results, string[] titles)
    {
        int imageSize = 300;
        int margin = 40;
        int cbWidth = 55; // must match FftPsfRenderer
        int cellW = imageSize + 2 * margin + cbWidth;
        int cellH = imageSize + 2 * margin;

        // Layout: one row, all fields side by side
        int cols = results.Length;
        int headerH = 30;
        int totalW = cols * cellW + 10;
        int totalH = headerH + cellH + 10;

        const int scale = 2;
        using var skBitmap = new SKBitmap(totalW * scale, totalH * scale);
        using var canvas = new SKCanvas(skBitmap);
        canvas.Scale(scale);
        canvas.Clear(SKColors.White);

        using var titlePaint = new SKPaint
        {
            Color = SKColors.Black, TextSize = 14, IsAntialias = true,
            Typeface = SKTypeface.Default, FakeBoldText = true
        };

        string pageTitle = "FFT PSF";
        float tw = titlePaint.MeasureText(pageTitle);
        canvas.DrawText(pageTitle, totalW / 2f - tw / 2f, 18, titlePaint);

        for (int i = 0; i < results.Length; i++)
        {
            string t = i < titles.Length ? titles[i] : "";
            string svg = FftPsfRenderer.Render(results[i], t, imageSize);

            float x = i * cellW + 5;
            float y = headerH;

            using var skSvg = new SKSvg();
            skSvg.FromSvg(svg);
            if (skSvg.Picture != null)
            {
                canvas.Save();
                canvas.Translate(x, y);
                canvas.DrawPicture(skSvg.Picture);
                canvas.Restore();
            }
        }

        using var image = SKImage.FromBitmap(skBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }

    [RelayCommand]
    public async Task ExportText()
    {
        if (_lastResults == null) return;

        var system = _session.System;
        string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";

        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Export FFT PSF Data",
                DefaultExtension = "txt",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Text Files") { Patterns = new[] { "*.txt" } }
                }
            });

        if (file == null) return;
        var path = file.TryGetLocalPath();
        if (path == null) return;

        var sb = new StringBuilder();
        foreach (var r in _lastResults)
        {
            double fieldY = r.FieldIndex < system.Fields.Count ? system.Fields[r.FieldIndex].Y : 0;
            double wlUm = r.WavelengthIndex < system.Wavelengths.Count
                ? system.Wavelengths[r.WavelengthIndex].Value : double.NaN;

            sb.AppendLine(FftPsfTextExport.Export(r,
                $"FFT PSF \u2014 Field {r.FieldIndex + 1}: {fieldY:F1} {fieldUnit}, W{r.WavelengthIndex + 1}: {wlUm:F4} \u00b5m",
                fieldY, wlUm, fieldUnit));
            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }
}
