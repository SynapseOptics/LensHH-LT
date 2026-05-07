// Example 03: Comprehensive MTF analysis
//
// Demonstrates: FftMtf, FftMtfPolychromatic, FftMtfVsField, FftPsf
//               Rendering and text export for each

using System;
using System.IO;
using System.Linq;
using LensHH.API;
using LensHH.Core.Analysis;

class Example03_MTFAnalysis
{
    static void Main()
    {
        var session = new LensHHSession();
        session.ImportZemax("samples/CookeTriplet.zmx");
        Console.WriteLine($"Loaded: {session.System.Title}");

        int numFields = session.System.Fields.Count;
        int numWaves = session.System.Wavelengths.Count;
        int priWave = session.System.PrimaryWavelengthIndex;

        // --- FFT MTF per field per wavelength ---
        Console.WriteLine("\n=== FFT MTF vs Spatial Frequency ===");
        var mtfByField = new MtfResult[numFields][];
        for (int f = 0; f < numFields; f++)
        {
            mtfByField[f] = new MtfResult[numWaves];
            for (int w = 0; w < numWaves; w++)
            {
                mtfByField[f][w] = session.FftMtf(f, w, gridSize: 128);
                Console.WriteLine($"  F{f + 1} W{w + 1}: cutoff={mtfByField[f][w].MaxFrequency:F1} cy/mm");
            }
        }

        // Render all MTF curves
        string mtfHtml = session.RenderFftMtf(mtfByField, "FFT MTF — All Fields");
        File.WriteAllText("fft_mtf.html", mtfHtml);
        Console.WriteLine("Saved: fft_mtf.html");

        // Text export for primary wavelength, on-axis
        string mtfText = session.ExportFftMtfText(mtfByField[0][priWave], "MTF F1 primary");
        File.WriteAllText("fft_mtf_F1.txt", mtfText);
        Console.WriteLine("Saved: fft_mtf_F1.txt");

        // --- Polychromatic MTF ---
        Console.WriteLine("\n=== Polychromatic MTF ===");
        for (int f = 0; f < numFields; f++)
        {
            var poly = session.FftMtfPolychromatic(f, gridSize: 128);
            Console.WriteLine($"  F{f + 1}: cutoff={poly.MaxFrequency:F1} cy/mm, " +
                $"points={poly.Points.Count}");
        }

        // --- MTF vs Field ---
        Console.WriteLine("\n=== MTF vs Field ===");
        double[] freqs = { 10, 20, 30, 40, 50 };
        var mtfVsField = session.FftMtfVsField(freqs, polychromatic: true);
        Console.WriteLine($"  Evaluated at {freqs.Length} frequencies across {mtfVsField.Points.Count} field points");

        string vsFieldHtml = session.RenderFftMtfVsField(mtfVsField, "MTF vs Field — Polychromatic");
        File.WriteAllText("mtf_vs_field.html", vsFieldHtml);
        Console.WriteLine("Saved: mtf_vs_field.html");

        string vsFieldText = session.ExportFftMtfVsFieldText(mtfVsField, "MTF vs Field");
        File.WriteAllText("mtf_vs_field.txt", vsFieldText);
        Console.WriteLine("Saved: mtf_vs_field.txt");

        // --- FFT PSF ---
        Console.WriteLine("\n=== FFT PSF ===");
        var psfResults = new PsfResult[numFields];
        for (int f = 0; f < numFields; f++)
        {
            psfResults[f] = session.FftPsf(f, priWave, gridSize: 64);
            Console.WriteLine($"  F{f + 1}: Strehl={psfResults[f].StrehlRatio:F4}, " +
                $"peak={psfResults[f].PeakIntensity:E3}");
        }

        string psfHtml = session.RenderFftPsf(psfResults, "FFT PSF — Primary Wavelength");
        File.WriteAllText("fft_psf.html", psfHtml);
        Console.WriteLine("Saved: fft_psf.html");
    }
}
