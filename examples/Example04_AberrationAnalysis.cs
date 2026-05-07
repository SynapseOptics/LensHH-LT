// Example 04: Aberration analysis suite
//
// Demonstrates: Seidel, FieldCurvature, Distortion, LateralColor,
//               ChromaticFocalShift, RelativeIllumination, WavefrontMap

using System;
using System.IO;
using LensHH.API;

class Example04_AberrationAnalysis
{
    static void Main()
    {
        var session = new LensHHSession();
        session.ImportZemax("samples/CookeTriplet.zmx");
        Console.WriteLine($"Loaded: {session.System.Title}\n");

        // --- Seidel Coefficients ---
        var seidel = session.Seidel();
        Console.WriteLine("Seidel (3rd order) Coefficients:");
        Console.WriteLine($"  Spherical (S1): {seidel.TotalS1:F6}");
        Console.WriteLine($"  Coma (S2):      {seidel.TotalS2:F6}");
        Console.WriteLine($"  Astigmatism (S3): {seidel.TotalS3:F6}");
        Console.WriteLine($"  Field Curv (S4):  {seidel.TotalS4:F6}");
        Console.WriteLine($"  Distortion (S5):  {seidel.TotalS5:F6}");
        File.WriteAllText("seidel.html", session.RenderSeidel(seidel, "Seidel Coefficients"));
        File.WriteAllText("seidel.txt", session.ExportSeidelText(seidel));
        Console.WriteLine("  Saved: seidel.html, seidel.txt");

        // --- Field Curvature ---
        var fc = session.FieldCurvature();
        Console.WriteLine($"\nField Curvature: {fc.Points.Count} points");
        File.WriteAllText("field_curvature.html", session.RenderFieldCurvature(fc, "Field Curvature"));
        File.WriteAllText("field_curvature.txt", session.ExportFieldCurvatureText(fc));
        Console.WriteLine("  Saved: field_curvature.html, field_curvature.txt");

        // --- Distortion ---
        var dist = session.Distortion();
        Console.WriteLine($"\nDistortion: {dist.Points.Count} points");
        if (dist.Points.Count > 0)
        {
            var maxDist = dist.Points[dist.Points.Count - 1];
            Console.WriteLine($"  Max field distortion: {maxDist.DistortionPercent:F2}%");
        }
        File.WriteAllText("distortion.html", session.RenderDistortion(dist, "Distortion"));
        File.WriteAllText("distortion.txt", session.ExportDistortionText(dist));
        Console.WriteLine("  Saved: distortion.html, distortion.txt");

        // --- Lateral Color ---
        var lc = session.LateralColor();
        Console.WriteLine($"\nLateral Color: max={lc.MaxLateralColor:E3} mm");
        File.WriteAllText("lateral_color.html", session.RenderLateralColor(lc, "Lateral Color"));
        File.WriteAllText("lateral_color.txt", session.ExportLateralColorText(lc));
        Console.WriteLine("  Saved: lateral_color.html, lateral_color.txt");

        // --- Chromatic Focal Shift ---
        var cfs = session.ChromaticFocalShift();
        Console.WriteLine($"\nChromatic Focal Shift: max={cfs.MaxShift:E3} mm");
        File.WriteAllText("chromatic_focal_shift.html",
            session.RenderChromaticFocalShift(cfs, "Chromatic Focal Shift"));
        File.WriteAllText("chromatic_focal_shift.txt",
            session.ExportChromaticFocalShiftText(cfs));
        Console.WriteLine("  Saved: chromatic_focal_shift.html, chromatic_focal_shift.txt");

        // --- Relative Illumination ---
        var ri = session.RelativeIllumination();
        Console.WriteLine($"\nRelative Illumination: {ri.Points.Count} points");
        File.WriteAllText("relative_illumination.html",
            session.RenderRelativeIllumination(ri, "Relative Illumination"));
        File.WriteAllText("relative_illumination.txt",
            session.ExportRelativeIlluminationText(ri));
        Console.WriteLine("  Saved: relative_illumination.html, relative_illumination.txt");

        // --- Wavefront Maps ---
        Console.WriteLine($"\nWavefront Maps:");
        int priWave = session.System.PrimaryWavelengthIndex;
        for (int f = 0; f < session.System.Fields.Count; f++)
        {
            var wf = session.WavefrontMap(f, priWave, gridSize: 64);
            Console.WriteLine($"  F{f + 1}: PV={wf.PeakToValley:F4} waves, RMS={wf.RmsWavefront:F4} waves");

            File.WriteAllText($"wavefront_F{f + 1}.txt",
                session.ExportWavefrontMapText(wf, $"Wavefront F{f + 1}"));
        }
    }
}
