using System;
using System.Collections.ObjectModel;
using System.Globalization;
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

namespace LensHH.App.ViewModels;

public partial class SpotDiagramViewModel : ObservableObject
{
    private readonly GuiSession _session;

    [ObservableProperty] private Bitmap? _spotImage;
    [ObservableProperty] private int _numRings = 6;
    [ObservableProperty] private int _numArms = 12;
    [ObservableProperty] private int _gridSize = 32;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _useGrid;

    /// <summary>
    /// Wavelength dropdown. Index 0 = "Polychromatic" (maps to engine -1);
    /// indices 1..N = individual wavelengths 0..N-1.
    /// </summary>
    public ObservableCollection<string> WavelengthOptions { get; } = new();
    [ObservableProperty] private int _selectedWavelengthOption;  // 0 = Polychromatic

    partial void OnSelectedWavelengthOptionChanged(int value)
    {
        if (IsVisible) _ = Compute();
    }

    private SpotDiagramResult[]? _lastResults;
    public SpotDiagramResult[]? LastResults => _lastResults;

    private static readonly SKColor[] WaveColors =
    {
        SKColor.Parse("#2060ff"), SKColor.Parse("#20aa20"), SKColor.Parse("#ff2020"),
        SKColor.Parse("#ff8800"), SKColor.Parse("#8800ff"), SKColor.Parse("#008888")
    };

    public SpotDiagramViewModel(GuiSession session)
    {
        _session = session;
        _session.SystemChanged += OnSystemChanged;
        RefreshWavelengthOptions();
    }

    private void OnSystemChanged(string sender)
    {
        RefreshWavelengthOptions();
    }

    private void RefreshWavelengthOptions()
    {
        int prior = SelectedWavelengthOption;
        WavelengthOptions.Clear();
        WavelengthOptions.Add("Polychromatic");
        if (_session.System != null)
        {
            for (int i = 0; i < _session.System.Wavelengths.Count; i++)
            {
                double wl = _session.System.Wavelengths[i].Value;
                bool isPrimary = i == _session.System.PrimaryWavelengthIndex;
                string label = $"W{i + 1}: {wl.ToString("0.###", CultureInfo.InvariantCulture)} µm"
                               + (isPrimary ? " (primary)" : "");
                WavelengthOptions.Add(label);
            }
        }
        SelectedWavelengthOption = prior < WavelengthOptions.Count ? prior : 0;
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
            int rings = NumRings;
            int arms = NumArms;
            int grid = GridSize;
            bool useGrid = UseGrid;
            // 0 = Polychromatic → -1 to engine; n ≥ 1 → wavelength n-1.
            int wIdx = SelectedWavelengthOption <= 0 ? -1 : SelectedWavelengthOption - 1;

            string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
            var titles = new string[numFields];
            for (int f = 0; f < numFields; f++)
                titles[f] = $"{system.Fields[f].Y:F1} {fieldUnit}";

            var waveLabels = new string[system.Wavelengths.Count];
            for (int w = 0; w < system.Wavelengths.Count; w++)
                waveLabels[w] = $"{system.Wavelengths[w].Value:F6} \u00b5m";

            var bitmap = await Task.Run(() =>
            {
                var res = new SpotDiagramResult[numFields];
                for (int f = 0; f < numFields; f++)
                {
                    res[f] = useGrid
                        ? SpotDiagram.ComputeGrid(system, glassMgr, f, grid, wavelengthIndex: wIdx)
                        : SpotDiagram.Compute(system, glassMgr, f, rings, arms, wavelengthIndex: wIdx);
                }
                _lastResults = res;

                // Common scale
                double maxGeo = 0;
                foreach (var r in res)
                    if (r.GeoRadius > maxGeo) maxGeo = r.GeoRadius;
                double extent = maxGeo > 1e-10 ? maxGeo * 1.15 : 0.01;

                return RenderDirect(res, titles, waveLabels, extent);
            });

            SpotImage = bitmap;
        }
        catch
        {
            _lastResults = null;
            SpotImage = null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static Bitmap RenderDirect(SpotDiagramResult[] results, string[] titles,
        string[] waveLabels, double extent)
    {
        int cellSize = 400;
        int margin = 40;
        int plotSize = cellSize - 2 * margin;
        int n = results.Length;
        int totalW = cellSize * n;
        int totalH = cellSize + 30;

        using var skBitmap = new SKBitmap(totalW * 2, totalH * 2);
        using var canvas = new SKCanvas(skBitmap);
        canvas.Scale(2);
        canvas.Clear(SKColors.White);

        using var titlePaint = new SKPaint { Color = SKColors.Black, TextSize = 12, IsAntialias = true, Typeface = SKTypeface.Default };
        using var subtitlePaint = new SKPaint { Color = SKColor.Parse("#666"), TextSize = 10, IsAntialias = true };
        using var gridPaint = new SKPaint { Color = SKColor.Parse("#ddd"), StrokeWidth = 0.5f, IsAntialias = true, Style = SKPaintStyle.Stroke };
        using var borderPaint = new SKPaint { Color = SKColor.Parse("#ccc"), StrokeWidth = 1f, IsAntialias = true, Style = SKPaintStyle.Stroke };
        using var rmsPaint = new SKPaint { Color = SKColor.Parse("#aaa"), StrokeWidth = 0.8f, IsAntialias = true, Style = SKPaintStyle.Stroke, PathEffect = SKPathEffect.CreateDash(new[] { 3f, 3f }, 0) };
        using var scalePaint = new SKPaint { Color = SKColor.Parse("#999"), TextSize = 8, IsAntialias = true };
        using var legendPaint = new SKPaint { Color = SKColor.Parse("#333"), TextSize = 10, IsAntialias = true };

        for (int fi = 0; fi < n; fi++)
        {
            var result = results[fi];
            float ox = fi * cellSize;
            float cx = ox + margin + plotSize / 2f;
            float cy = margin + plotSize / 2f;

            // Title
            string title = fi < titles.Length ? titles[fi] : $"Field {fi + 1}";
            float tw = titlePaint.MeasureText(title);
            canvas.DrawText(title, ox + cellSize / 2f - tw / 2, 16, titlePaint);

            // Subtitle: pick a unit based on whether the engine produced
            // angular (afocal) or linear (focal) spot coordinates.
            //   focal  → engine values are in mm; display as µm (×1000)
            //   afocal → engine values are in arcmin; display as arcmin (no scale)
            double dispScale = result.IsAfocal ? 1.0 : 1000.0;
            string unitLabel = result.IsAfocal ? "arcmin" : "\u00b5m";
            double rmsDisp = result.RmsRadius * dispScale;
            double geoDisp = result.GeoRadius * dispScale;
            string sub = $"RMS={rmsDisp:F2} {unitLabel}, GEO={geoDisp:F2} {unitLabel}";
            float sw = subtitlePaint.MeasureText(sub);
            canvas.DrawText(sub, ox + cellSize / 2f - sw / 2, 30, subtitlePaint);

            // Crosshair
            canvas.DrawLine(ox + margin, cy, ox + margin + plotSize, cy, gridPaint);
            canvas.DrawLine(cx, margin, cx, margin + plotSize, gridPaint);

            // Scale labels (axis extents) — same unit as the subtitle.
            double extentDisp = extent * dispScale;
            canvas.DrawText($"{extentDisp:F0} {unitLabel}", ox + margin + plotSize - 5, margin + plotSize + 12, scalePaint);
            canvas.DrawText($"-{extentDisp:F0}", ox + margin, margin + plotSize + 12, scalePaint);

            // RMS circle
            float rmsPixels = (float)(result.RmsRadius / extent * (plotSize / 2.0));
            if (rmsPixels > 1)
                canvas.DrawCircle(cx, cy, rmsPixels, rmsPaint);

            // Border
            canvas.DrawRect(ox + margin, margin, plotSize, plotSize, borderPaint);

            // Ray dots — draw directly, no SVG overhead
            float scale = (float)(plotSize / 2.0 / extent);
            float dotR = 1.2f;

            // Pre-create paints per wavelength
            var dotPaints = new SKPaint[WaveColors.Length];
            for (int w = 0; w < dotPaints.Length; w++)
                dotPaints[w] = new SKPaint { Color = WaveColors[w].WithAlpha(180), IsAntialias = true, Style = SKPaintStyle.Fill };

            foreach (var pt in result.Points)
            {
                float dx = (float)(pt.X - result.ChiefRayX);
                float dy = (float)(pt.Y - result.ChiefRayY);
                float px = cx + dx * scale;
                float py = cy - dy * scale;
                canvas.DrawCircle(px, py, dotR, dotPaints[pt.WavelengthIndex % dotPaints.Length]);
            }

            for (int w = 0; w < dotPaints.Length; w++)
                dotPaints[w].Dispose();
        }

        // Wavelength legend
        float ly = cellSize + 15;
        float lx = 10;
        for (int w = 0; w < waveLabels.Length; w++)
        {
            var color = WaveColors[w % WaveColors.Length];
            using var lPaint = new SKPaint { Color = color, IsAntialias = true };
            canvas.DrawCircle(lx, ly, 4, lPaint);
            canvas.DrawText(waveLabels[w], lx + 8, ly + 4, legendPaint);
            lx += 100;
        }

        // Convert to Avalonia Bitmap
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
                Title = "Export Spot Diagram Data",
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
            sb.AppendLine(SpotDiagramTextExport.Export(_lastResults[f],
                $"Field {f + 1}: {fieldY:F1} {fieldUnit}",
                wavelengths, fieldY, fieldUnit));
            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }
}
