using System;
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

public partial class WavefrontMapViewModel : ObservableObject
{
    private readonly GuiSession _session;

    [ObservableProperty] private Bitmap? _wavefrontImage;
    [ObservableProperty] private int _gridSize = 64;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isBusy;

    private WavefrontResult[]? _lastResults;

    public WavefrontMapViewModel(GuiSession session)
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
            int numFields = system.Fields.Count;
            int numWaves = system.Wavelengths.Count;
            int gridSize = GridSize;

            string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";

            var titles = new string[numFields * numWaves];
            for (int f = 0; f < numFields; f++)
                for (int w = 0; w < numWaves; w++)
                    titles[f * numWaves + w] =
                        $"F{f + 1}: {system.Fields[f].Y:F1} {fieldUnit}, {LabelFormat.Wavelength(system.Wavelengths[w].Value, system.Wavelengths)}";

            var bitmap = await Task.Run(() =>
            {
                var results = new WavefrontResult[numFields * numWaves];
                for (int f = 0; f < numFields; f++)
                    for (int w = 0; w < numWaves; w++)
                        results[f * numWaves + w] =
                            WavefrontMapCalculator.Compute(system, glassMgr, f, w, gridSize);

                _lastResults = results;

                return RenderToBitmap(results, titles, numWaves);
            });

            WavefrontImage = bitmap;
        }
        catch
        {
            _lastResults = null;
            WavefrontImage = null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static Bitmap RenderToBitmap(WavefrontResult[] results, string[] titles, int numWavesPerField)
    {
        // Find global max OPD for shared color scale
        double globalMax = 0;
        foreach (var r in results)
        {
            for (int i = 0; i < r.GridSize; i++)
                for (int j = 0; j < r.GridSize; j++)
                    if (r.Valid[i, j] && Math.Abs(r.Opd[i, j]) > globalMax)
                        globalMax = Math.Abs(r.Opd[i, j]);
        }
        if (globalMax < 1e-10) globalMax = 1.0;

        int imageSize = 300;
        var options = new RenderingOptions();

        // Render each wavefront map as SVG
        var svgs = new string[results.Length];
        for (int i = 0; i < results.Length; i++)
        {
            string t = i < titles.Length ? titles[i] : "";
            svgs[i] = WavefrontMapRenderer.Render(results[i], t, imageSize, globalMax, options);
        }

        // Layout: grid with numWavesPerField columns
        int cols = numWavesPerField;
        int rows = (results.Length + cols - 1) / cols;
        int cellW = imageSize + 45 + 50; // margin + colorbar
        int cellH = imageSize + 2 * 45;
        int headerH = 30;
        int totalW = cols * cellW + 10;
        int totalH = headerH + rows * cellH + 10;

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
        using var scalePaint = new SKPaint
        {
            Color = SKColor.Parse("#666"), TextSize = 10, IsAntialias = true
        };

        string title = "Wavefront Map";
        float tw = titlePaint.MeasureText(title);
        canvas.DrawText(title, totalW / 2f - tw / 2f, 18, titlePaint);

        string scaleText = $"Shared color scale: \u00b1{globalMax:F2} waves";
        canvas.DrawText(scaleText, 10, 28, scalePaint);

        for (int i = 0; i < svgs.Length; i++)
        {
            int col = i % cols;
            int row = i / cols;
            float x = col * cellW + 5;
            float y = headerH + row * cellH;

            using var skSvg = new SKSvg();
            skSvg.FromSvg(svgs[i]);
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
                Title = "Export Wavefront Map Data",
                DefaultExtension = "txt",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Text Files") { Patterns = new[] { "*.txt" } }
                }
            });

        if (file == null) return;
        var path = file.TryGetLocalPath();
        if (path == null) return;

        string wlFmt = "F" + LabelFormat.WavelengthDigits(system.Wavelengths);
        var sb = new StringBuilder();
        foreach (var r in _lastResults)
        {
            double fieldY = r.FieldIndex < system.Fields.Count ? system.Fields[r.FieldIndex].Y : 0;
            double wlUm = r.WavelengthIndex < system.Wavelengths.Count
                ? system.Wavelengths[r.WavelengthIndex].Value : double.NaN;

            string wlStr = double.IsNaN(wlUm) ? "?" : wlUm.ToString(wlFmt, System.Globalization.CultureInfo.InvariantCulture);
            sb.AppendLine(WavefrontMapTextExport.Export(r,
                $"Field {r.FieldIndex + 1}: {fieldY:F1} {fieldUnit}, W{r.WavelengthIndex + 1}: {wlStr} \u00b5m",
                fieldY, wlUm, fieldUnit));
            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }
}
