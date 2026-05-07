// Example 01: Load a lens and run basic analyses
//
// Demonstrates: ImportZemax, SpotDiagram, OpdFan, RayFan, ParaxialData
// Output: console text + HTML files

using System;
using System.IO;
using LensHH.API;

class Example01_BasicAnalysis
{
    static void Main()
    {
        var session = new LensHHSession();

        // Load a Zemax file
        session.ImportZemax("samples/CookeTriplet.zmx");
        Console.WriteLine($"Loaded: {session.System.Title}");
        Console.WriteLine($"Surfaces: {session.System.Surfaces.Count}");
        Console.WriteLine($"Fields: {session.System.Fields.Count}");
        Console.WriteLine($"Wavelengths: {session.System.Wavelengths.Count}");

        // Paraxial data
        var parax = session.ParaxialData();
        Console.WriteLine($"\nParaxial Data:");
        Console.WriteLine($"  EFL: {parax.Efl:F3} mm");
        Console.WriteLine($"  BFL: {parax.Bfl:F3} mm");
        Console.WriteLine($"  Exit Pupil Position: {parax.ExitPupilPosition:F3} mm");

        // Spot diagram for each field
        Console.WriteLine($"\nSpot Diagrams:");
        var spotResults = new LensHH.Core.Analysis.SpotDiagramResult[session.System.Fields.Count];
        for (int i = 0; i < session.System.Fields.Count; i++)
        {
            spotResults[i] = session.SpotDiagram(i);
            Console.WriteLine($"  F{i + 1} ({session.System.Fields[i].Y} deg): " +
                $"RMS={spotResults[i].RmsRadius:E3} mm, GEO={spotResults[i].GeoRadius:E3} mm");
        }

        // Save spot diagram HTML
        string html = session.RenderSpotDiagram(spotResults, "Cooke Triplet — Spot Diagrams");
        File.WriteAllText("spot_diagram.html", html);
        Console.WriteLine("\nSaved: spot_diagram.html");

        // OPD fan for on-axis field
        var opd = session.OpdFan(0);
        Console.WriteLine($"\nOPD Fan F1: Max OPD = {opd.MaxOpd:F4} waves");
        string opdText = session.ExportOpdFanText(opd, "OPD Fan — On Axis");
        File.WriteAllText("opd_fan_F1.txt", opdText);
        Console.WriteLine("Saved: opd_fan_F1.txt");

        // System layout
        var layout = session.SystemLayout(numRays: 7);
        string layoutHtml = session.RenderSystemLayout(layout, "Cooke Triplet — Layout");
        File.WriteAllText("system_layout.html", layoutHtml);
        Console.WriteLine("Saved: system_layout.html");
    }
}
