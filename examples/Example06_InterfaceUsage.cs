// Example 06: Using segregated interfaces
//
// Demonstrates how to depend only on the interface you need,
// enabling mocking for tests and clean dependency injection.

using System;
using System.IO;
using LensHH.API;
using LensHH.Core.Analysis;

class Example06_InterfaceUsage
{
    static void Main()
    {
        // Create session — implements all interfaces
        var session = new LensHHSession();
        session.ImportZemax("samples/CookeTriplet.zmx");

        // Pass only the interface needed to each function
        RunAnalysis(session);
        ExportResults(session);
    }

    // This function only needs IAnalysis — doesn't care about file I/O or optimization
    static void RunAnalysis(IAnalysis analysis)
    {
        var spot = analysis.SpotDiagram(0);
        Console.WriteLine($"Spot RMS: {spot.RmsRadius:E3} mm");

        var parax = analysis.ParaxialData();
        Console.WriteLine($"EFL: {parax.Efl:F2} mm");

        var seidel = analysis.Seidel();
        Console.WriteLine($"Spherical: {seidel.TotalS1:F4}");
    }

    // This function only needs IRendering and ITextExport
    static void ExportResults(LensHHSession session)
    {
        IAnalysis analysis = session;
        IRendering render = session;
        ITextExport text = session;

        // Compute, render, and export
        var spot = analysis.SpotDiagram(0);
        File.WriteAllText("spot.html",
            render.RenderSpotDiagram(new[] { spot }, "Spot Diagram"));

        var opd = analysis.OpdFan(0);
        File.WriteAllText("opd.txt",
            text.ExportOpdFanText(opd, "OPD Fan"));

        Console.WriteLine("Exported spot.html and opd.txt");
    }
}
