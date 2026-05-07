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

public partial class OpdFanViewModel : ObservableObject
{
    private readonly GuiSession _session;

    [ObservableProperty] private Bitmap? _opdFanImage;
    [ObservableProperty] private int _numPoints = 64;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isBusy;

    private OpdFanResult[]? _lastResults;

    public OpdFanViewModel(GuiSession session)
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
            int numPoints = NumPoints;

            string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
            var fieldLabels = new string[numFields];
            for (int f = 0; f < numFields; f++)
                fieldLabels[f] = $"{system.Fields[f].Y:F1} {fieldUnit}";

            var waveLabels = new string[system.Wavelengths.Count];
            for (int w = 0; w < system.Wavelengths.Count; w++)
                waveLabels[w] = $"{system.Wavelengths[w].Value:F4} \u00b5m";

            var bitmap = await Task.Run(() =>
            {
                var results = new OpdFanResult[numFields];
                for (int f = 0; f < numFields; f++)
                    results[f] = OpdFan.Compute(system, glassMgr, f, numPoints);

                _lastResults = results;

                return RenderToBitmap(results, fieldLabels, waveLabels, system.Wavelengths.Count);
            });

            OpdFanImage = bitmap;
        }
        catch
        {
            _lastResults = null;
            OpdFanImage = null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static Bitmap RenderToBitmap(OpdFanResult[] results, string[] fieldLabels,
        string[] waveLabels, int numWavelengths)
    {
        double maxOpd = 0;
        foreach (var r in results)
            if (r.MaxOpd > maxOpd)
                maxOpd = r.MaxOpd;

        var options = new RenderingOptions();

        int fanW = 400, fanH = 300;
        int headerH = 30;
        int legendH = 30;
        int totalW = fanW * 2 + 30;
        int totalH = headerH + results.Length * (fanH + 10) + legendH;

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
        using var scaleLabelPaint = new SKPaint
        {
            Color = SKColor.Parse("#666"), TextSize = 10, IsAntialias = true
        };
        using var legendPaint = new SKPaint
        {
            Color = SKColor.Parse("#333"), TextSize = 10, IsAntialias = true
        };

        string title = "OPD Fan";
        float tw = titlePaint.MeasureText(title);
        canvas.DrawText(title, totalW / 2f - tw / 2f, 18, titlePaint);

        string scaleText = $"Max OPD scale: \u00b1{maxOpd:F2} waves";
        canvas.DrawText(scaleText, 10, 28, scaleLabelPaint);

        for (int fi = 0; fi < results.Length; fi++)
        {
            string label = fi < fieldLabels.Length ? fieldLabels[fi] : $"Field {fi + 1}";
            int yOff = headerH + fi * (fanH + 10);

            string tanSvg = OpdFanRenderer.RenderFan(results[fi].TangentialFan,
                label + " \u2014 Tangential",
                maxOpd, numWavelengths, options);
            DrawSvgOnCanvas(canvas, tanSvg, 5, yOff);

            string sagSvg = OpdFanRenderer.RenderFan(results[fi].SagittalFan,
                label + " \u2014 Sagittal",
                maxOpd, numWavelengths, options);
            DrawSvgOnCanvas(canvas, sagSvg, fanW + 15, yOff);
        }

        float ly = headerH + results.Length * (fanH + 10) + 15;
        float lx = 10;
        for (int w = 0; w < waveLabels.Length; w++)
        {
            string color = options.GetWavelengthColor(w);
            using var lPaint = new SKPaint { Color = SKColor.Parse(color), IsAntialias = true };
            canvas.DrawCircle(lx, ly, 4, lPaint);
            canvas.DrawText(waveLabels[w], lx + 8, ly + 4, legendPaint);
            lx += 100;
        }

        using var image = SKImage.FromBitmap(skBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }

    private static void DrawSvgOnCanvas(SKCanvas canvas, string svgString, float x, float y)
    {
        using var skSvg = new SKSvg();
        skSvg.FromSvg(svgString);
        if (skSvg.Picture != null)
        {
            canvas.Save();
            canvas.Translate(x, y);
            canvas.DrawPicture(skSvg.Picture);
            canvas.Restore();
        }
    }

    [RelayCommand]
    public async Task ExportText()
    {
        if (_lastResults == null) return;

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
                Title = "Export OPD Fan Data",
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
        for (int f = 0; f < _lastResults.Length; f++)
        {
            double fieldY = system.Fields[f].Y;
            sb.AppendLine(OpdFanTextExport.Export(_lastResults[f],
                $"Field {f + 1}: {fieldY:F1} {fieldUnit}",
                wavelengths, fieldY, fieldUnit));
            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }
}
