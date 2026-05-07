using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using LensHH.Core.Analysis;
using LensHH.Core.Glass;
using LensHH.Core.Models;
using LensHH.Rendering;
using SkiaSharp;

namespace LensHH.RenderApp;

public class AnalysisDispatcher
{
    private readonly GlassCatalogManager _glass;

    public AnalysisDispatcher(GlassCatalogManager glass) => _glass = glass;

    public (string title, Bitmap bitmap) Execute(
        OpticalSystem system,
        string analysis,
        Dictionary<string, JsonElement>? parms)
    {
        string sysTitle = system.Title ?? "";
        string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";

        switch (analysis)
        {
            case "SystemLayout":
            {
                int numRays = ParamHelper.GetInt(parms, "NumRays", 15);
                bool fromSurf1 = ParamHelper.GetBool(parms, "StartFromSurface1", true);
                int wIdx = ParamHelper.GetInt(parms, "WavelengthIndex", -1);
                var layout = SystemLayout.ComputeLayout(system, _glass, numRays,
                    startFromSurface1: fromSurf1, wavelengthIndex: wIdx);
                var layoutFieldYs = system.Fields.Select(f => f.Y).ToList();
                string layoutFieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
                string svg = SystemLayoutRenderer.Render(layout, width: 900, height: 450,
                    fieldYs: layoutFieldYs, fieldUnit: layoutFieldUnit);
                return ($"{sysTitle} \u2014 System Layout", SvgBitmapHelper.SvgToBitmap(svg, 1, 900, 450));
            }

            case "SpotDiagram":
            {
                int spotWIdx = ParamHelper.GetInt(parms, "WavelengthIndex", -1);
                return ($"{sysTitle} \u2014 Spot Diagram", RenderSpotDirect(system, spotWIdx));
            }

            case "RayFan":
            {
                int n = system.Fields.Count;
                var results = new RayFanResult[n];
                for (int i = 0; i < n; i++)
                    results[i] = TransverseRayFan.Compute(system, _glass, i);
                var titles = results.Select((r, i) => $"F{i + 1} ({system.Fields[i].Y})").ToArray();
                string html = RayFanRenderer.RenderPage(results, titles, sysTitle);
                return ($"{sysTitle} \u2014 Ray Fan", SvgBitmapHelper.HtmlPageToBitmap(html, 1));
            }

            case "PupilAberrationFan":
            {
                int nf = system.Fields.Count;
                var pResults = new PupilAberrationResult[nf];
                for (int i = 0; i < nf; i++)
                    pResults[i] = PupilAberrationFan.Compute(system, _glass, i);
                double maxAberr = pResults.Max(r => r.MaxAberration);
                if (maxAberr < 0.1) maxAberr = 0.1;
                string fUnit = system.FieldType == LensHH.Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
                var pTitles = pResults.Select((r, i) =>
                    $"{system.Fields[i].Y:F1} {fUnit}").ToArray();
                string pHtml = PupilAberrationFanRenderer.RenderPage(pResults, pTitles, sysTitle,
                    system.Wavelengths.Count, maxAberr);
                return ($"{sysTitle} \u2014 Pupil Aberration Fan", SvgBitmapHelper.HtmlPageToBitmap(pHtml, 1));
            }

            case "OpdFan":
            {
                int n = system.Fields.Count;
                var results = new OpdFanResult[n];
                for (int i = 0; i < n; i++)
                    results[i] = OpdFan.Compute(system, _glass, i);
                var titles = results.Select((r, i) => $"F{i + 1} ({system.Fields[i].Y})").ToArray();
                string html = OpdFanRenderer.RenderPage(results, titles, sysTitle);
                return ($"{sysTitle} \u2014 OPD Fan", SvgBitmapHelper.HtmlPageToBitmap(html, 1));
            }

            case "FftMtf":
            {
                int fi = ParamHelper.GetInt(parms, "FieldIndex", -1);
                int wIdx = ParamHelper.GetInt(parms, "WavelengthIndex", -1);
                if (wIdx < 0) wIdx = system.PrimaryWavelengthIndex;
                // 0 (default) = use diffraction cutoff per result, matching GUI.
                double maxFreq = ParamHelper.GetDouble(parms, "MaxFrequency", 0);

                if (fi >= 0)
                {
                    // Single field
                    var result = FftMtfCalculator.ComputeVsFrequency(system, _glass, fi, wIdx);
                    string html = FftMtfRenderer.RenderPage(
                        new[] { new[] { result } },
                        new[] { $"F{fi + 1}" },
                        sysTitle, maxFrequency: maxFreq);
                    return ($"{sysTitle} \u2014 FFT MTF F{fi + 1}", SvgBitmapHelper.HtmlPageToBitmap(html, 1));
                }
                else
                {
                    // All fields on one plot
                    int n = system.Fields.Count;
                    var fieldResults = new MtfResult[n];
                    var labels = new string[n];
                    for (int f = 0; f < n; f++)
                    {
                        fieldResults[f] = FftMtfCalculator.ComputeVsFrequency(system, _glass, f, wIdx);
                        labels[f] = $"F{f + 1} ({system.Fields[f].Y} {fieldUnit})";
                    }
                    double onAxisCutoff = fieldResults.Length > 0 ? fieldResults[0].MaxFrequency : 0;
                    string svg = FftMtfRenderer.RenderAllFields(fieldResults, labels,
                        $"{sysTitle} \u2014 FFT MTF",
                        maxFrequency: maxFreq, onAxisCutoff: onAxisCutoff);
                    return ($"{sysTitle} \u2014 FFT MTF", SvgBitmapHelper.SvgToBitmap(svg, 1));
                }
            }

            case "FftMtfVsField":
            {
                int wIdx = ParamHelper.GetInt(parms, "WavelengthIndex", -1);
                if (wIdx < 0) wIdx = system.PrimaryWavelengthIndex;
                bool poly = ParamHelper.GetBool(parms, "Polychromatic", false);
                string freqStr = ParamHelper.GetString(parms, "Frequencies", "10,20,30,40");
                var freqs = freqStr.Split(',').Select(s => double.Parse(s.Trim())).ToArray();
                var result = FftMtfCalculator.ComputeVsFieldMultiFreq(system, _glass,
                    freqs, wIdx, 256, 200, poly);
                string html = MtfVsFieldRenderer.RenderPage(result, sysTitle);
                return ($"{sysTitle} \u2014 FFT MTF vs Field", SvgBitmapHelper.HtmlPageToBitmap(html, 1));
            }

            case "FftMtfThroughFocus":
            {
                int fi = ParamHelper.GetInt(parms, "FieldIndex", 0);
                double freq = ParamHelper.GetDouble(parms, "SpatialFrequency", 50);
                double range = ParamHelper.GetDouble(parms, "FocusRange", 0.1);
                var result = FftMtfCalculator.ComputeThroughFocusPolychromatic(system, _glass,
                    fi, freq, range, 21, 64);
                string html = MtfThroughFocusRenderer.RenderPage(result,
                    $"{sysTitle} \u2014 FFT MTF Through Focus F{fi + 1} {freq:F0} cy/mm");
                return ($"{sysTitle} \u2014 FFT MTF Through Focus", SvgBitmapHelper.HtmlPageToBitmap(html, 1));
            }

            case "FftPsf":
            {
                int fi = ParamHelper.GetInt(parms, "FieldIndex", 0);
                int wIdx = ParamHelper.GetInt(parms, "WavelengthIndex", -1);
                if (wIdx < 0) wIdx = system.PrimaryWavelengthIndex;
                var result = FftPsfCalculator.Compute(system, _glass, fi, wIdx, 64);
                string html = FftPsfRenderer.RenderPage(
                    new[] { result }, new[] { $"F{fi + 1} W{wIdx + 1}" }, sysTitle);
                return ($"{sysTitle} \u2014 FFT PSF", SvgBitmapHelper.HtmlPageToBitmap(html, 1));
            }

            case "GeoMtf":
            {
                int fi = ParamHelper.GetInt(parms, "FieldIndex", 0);
                int wIdx = ParamHelper.GetInt(parms, "WavelengthIndex", -1);
                if (wIdx < 0) wIdx = system.PrimaryWavelengthIndex;
                int numRings = ParamHelper.GetInt(parms, "NumRings", 30);
                var result = GeometricMtfKidger.Compute(system, _glass, fi, wIdx, numRings);
                string html = FftMtfRenderer.RenderPage(
                    new[] { new[] { result } },
                    new[] { $"F{fi + 1}" },
                    sysTitle);
                return ($"{sysTitle} \u2014 Geometric MTF", SvgBitmapHelper.HtmlPageToBitmap(html, 1));
            }

            case "GeoMtfVsField":
            {
                int wIdx = ParamHelper.GetInt(parms, "WavelengthIndex", -1);
                if (wIdx < 0) wIdx = system.PrimaryWavelengthIndex;
                int numRings = ParamHelper.GetInt(parms, "NumRings", 30);
                string freqStr = ParamHelper.GetString(parms, "Frequencies", "10,20,30,40");
                var freqs = freqStr.Split(',').Select(s => double.Parse(s.Trim())).ToArray();
                var result = GeometricMtfKidger.ComputeVsFieldMultiFreq(system, _glass,
                    freqs, wIdx, numRings);
                string html = MtfVsFieldRenderer.RenderPage(result, sysTitle);
                return ($"{sysTitle} \u2014 Geometric MTF vs Field", SvgBitmapHelper.HtmlPageToBitmap(html, 1));
            }

            case "GeoMtfThroughFocus":
            {
                int fi = ParamHelper.GetInt(parms, "FieldIndex", 0);
                double freq = ParamHelper.GetDouble(parms, "SpatialFrequency", 30);
                int wIdx = ParamHelper.GetInt(parms, "WavelengthIndex", -1);
                if (wIdx < 0) wIdx = system.PrimaryWavelengthIndex;
                double range = ParamHelper.GetDouble(parms, "FocusRange", 0.1);
                int numRings = ParamHelper.GetInt(parms, "NumRings", 30);
                var result = GeometricMtfKidger.ComputeThroughFocus(system, _glass,
                    fi, freq, wIdx, range, 21, numRings);
                string html = MtfThroughFocusRenderer.RenderPage(result,
                    $"{sysTitle} \u2014 Through Focus F{fi + 1} {freq:F0} cy/mm");
                return ($"{sysTitle} \u2014 Geometric MTF Through Focus", SvgBitmapHelper.HtmlPageToBitmap(html, 1));
            }

            case "FieldCurvature":
            {
                var waveLabels = system.Wavelengths
                    .Select(w => $"{w.Value:F4} \u00b5m").ToArray();
                var mw = FieldCurvatureCalculator.ComputeAllWavelengths(system, _glass, numPoints: 100);
                string html = FieldCurvatureRenderer.RenderMultiWavelengthPage(mw, sysTitle, waveLabels, fieldUnit: fieldUnit);
                return ($"{sysTitle} \u2014 Field Curvature", SvgBitmapHelper.HtmlPageToBitmap(html, 1));
            }

            case "Distortion":
            {
                var result = DistortionCalculator.Compute(system, _glass);
                string html = DistortionRenderer.RenderPage(result, sysTitle);
                return ($"{sysTitle} \u2014 Distortion", SvgBitmapHelper.HtmlPageToBitmap(html, 1));
            }

            case "SystemData":
            {
                var result = SystemDataCalculator.Calculate(system, _glass);
                string html = SystemDataRenderer.RenderPage(result, sysTitle);
                return ($"{sysTitle} \u2014 System Data", SvgBitmapHelper.HtmlPageToBitmap(html, 1));
            }

            case "Seidel":
            {
                var result = SeidelCalculator.Calculate(system, _glass);
                string html = SeidelRenderer.RenderPage(result, sysTitle);
                return ($"{sysTitle} \u2014 Seidel Coefficients", SvgBitmapHelper.HtmlPageToBitmap(html, 1));
            }

            case "LateralColor":
            {
                var result = LateralColorCalculator.Compute(system, _glass);
                double maxField = system.Fields.Max(f => Math.Abs(f.Y));
                var wlLabels = system.Wavelengths.Select(w => $"{w.Value:F4}um").ToArray();
                string html = LateralColorRenderer.RenderPage(result, sysTitle,
                    maxField, system.Wavelengths.Count, wlLabels, fieldUnit: fieldUnit);
                return ($"{sysTitle} \u2014 Lateral Color", SvgBitmapHelper.HtmlPageToBitmap(html, 1));
            }

            case "RelativeIllumination":
            {
                int numFieldPts = ParamHelper.GetInt(parms, "NumFieldPoints", 50);
                int numPupilRays = ParamHelper.GetInt(parms, "NumPupilRays", 36);
                var result = RelativeIlluminationCalculator.Compute(system, _glass,
                    numFieldPoints: numFieldPts, numPupilRays: numPupilRays);
                string html = RelativeIlluminationRenderer.RenderPage(result, sysTitle);
                return ($"{sysTitle} \u2014 Relative Illumination", SvgBitmapHelper.HtmlPageToBitmap(html, 1));
            }

            case "WavefrontMap":
            {
                int fi = ParamHelper.GetInt(parms, "FieldIndex", 0);
                int wIdx = ParamHelper.GetInt(parms, "WavelengthIndex", -1);
                if (wIdx < 0) wIdx = system.PrimaryWavelengthIndex;
                int gridSize = ParamHelper.GetInt(parms, "GridSize", 64);
                var result = WavefrontMapCalculator.Compute(system, _glass, fi, wIdx, gridSize);
                string html = WavefrontMapRenderer.RenderPage(
                    new[] { result }, new[] { $"F{fi + 1} W{wIdx + 1}" }, sysTitle);
                return ($"{sysTitle} \u2014 Wavefront Map", SvgBitmapHelper.HtmlPageToBitmap(html, 1));
            }

            case "ChromaticFocalShift":
            {
                var result = ChromaticFocalShift.Compute(system, _glass);
                string html = ChromaticFocalShiftRenderer.RenderPage(result, sysTitle);
                return ($"{sysTitle} \u2014 Chromatic Focal Shift", SvgBitmapHelper.HtmlPageToBitmap(html, 1));
            }

            default:
                throw new ArgumentException($"Unknown analysis: {analysis}");
        }
    }

    // ─── Direct SkiaSharp rendering for spot diagram (no SVG) ──────────

    private static readonly SKColor[] WaveColors =
    {
        SKColor.Parse("#2060ff"), SKColor.Parse("#20aa20"), SKColor.Parse("#ff2020"),
        SKColor.Parse("#ff8800"), SKColor.Parse("#8800ff"), SKColor.Parse("#008888")
    };

    private Bitmap RenderSpotDirect(OpticalSystem system, int wavelengthIndex = -1)
    {
        int numFields = system.Fields.Count;
        string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";

        var titles = new string[numFields];
        for (int f = 0; f < numFields; f++)
            titles[f] = $"{system.Fields[f].Y:F1} {fieldUnit}";

        var waveLabels = new string[system.Wavelengths.Count];
        for (int w = 0; w < system.Wavelengths.Count; w++)
            waveLabels[w] = $"{system.Wavelengths[w].Value:F4} \u00b5m";

        var results = new SpotDiagramResult[numFields];
        for (int f = 0; f < numFields; f++)
            results[f] = SpotDiagram.Compute(system, _glass, f,
                numRings: 6, numArms: 12, wavelengthIndex: wavelengthIndex);

        // Common scale
        double maxGeo = 0;
        foreach (var r in results)
            if (r.GeoRadius > maxGeo) maxGeo = r.GeoRadius;
        double extent = maxGeo > 1e-10 ? maxGeo * 1.15 : 0.01;

        int cellSize = 300;
        int margin = 30;
        int plotSize = cellSize - 2 * margin;
        int totalW = cellSize * numFields;
        int totalH = cellSize + 25;

        using var skBitmap = new SKBitmap(totalW, totalH);
        using var canvas = new SKCanvas(skBitmap);
        canvas.Clear(SKColors.White);

        using var titlePaint = new SKPaint { Color = SKColors.Black, TextSize = 12, IsAntialias = true, Typeface = SKTypeface.Default };
        using var subtitlePaint = new SKPaint { Color = SKColor.Parse("#666"), TextSize = 10, IsAntialias = true };
        using var gridPaint = new SKPaint { Color = SKColor.Parse("#ddd"), StrokeWidth = 0.5f, IsAntialias = true, Style = SKPaintStyle.Stroke };
        using var borderPaint = new SKPaint { Color = SKColor.Parse("#ccc"), StrokeWidth = 1f, IsAntialias = true, Style = SKPaintStyle.Stroke };
        using var rmsPaint = new SKPaint { Color = SKColor.Parse("#aaa"), StrokeWidth = 0.8f, IsAntialias = true, Style = SKPaintStyle.Stroke, PathEffect = SKPathEffect.CreateDash(new[] { 3f, 3f }, 0) };
        using var scalePaint = new SKPaint { Color = SKColor.Parse("#999"), TextSize = 8, IsAntialias = true };
        using var legendPaint = new SKPaint { Color = SKColor.Parse("#333"), TextSize = 10, IsAntialias = true };

        for (int fi = 0; fi < numFields; fi++)
        {
            var result = results[fi];
            float ox = fi * cellSize;
            float cx = ox + margin + plotSize / 2f;
            float cy = margin + plotSize / 2f;

            string title = fi < titles.Length ? titles[fi] : $"Field {fi + 1}";
            float tw = titlePaint.MeasureText(title);
            canvas.DrawText(title, ox + cellSize / 2f - tw / 2, 16, titlePaint);

            double rmsUm = result.RmsRadius * 1000;
            double geoUm = result.GeoRadius * 1000;
            string sub = $"RMS={rmsUm:F2} \u00b5m, GEO={geoUm:F2} \u00b5m";
            float sw = subtitlePaint.MeasureText(sub);
            canvas.DrawText(sub, ox + cellSize / 2f - sw / 2, 30, subtitlePaint);

            canvas.DrawLine(ox + margin, cy, ox + margin + plotSize, cy, gridPaint);
            canvas.DrawLine(cx, margin, cx, margin + plotSize, gridPaint);

            double extentUm = extent * 1000;
            canvas.DrawText($"{extentUm:F0} \u00b5m", ox + margin + plotSize - 5, margin + plotSize + 12, scalePaint);
            canvas.DrawText($"-{extentUm:F0}", ox + margin, margin + plotSize + 12, scalePaint);

            float rmsPixels = (float)(result.RmsRadius / extent * (plotSize / 2.0));
            if (rmsPixels > 1)
                canvas.DrawCircle(cx, cy, rmsPixels, rmsPaint);

            canvas.DrawRect(ox + margin, margin, plotSize, plotSize, borderPaint);

            float scale = (float)(plotSize / 2.0 / extent);
            float dotR = 1.2f;

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

        using var image = SKImage.FromBitmap(skBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }
}
