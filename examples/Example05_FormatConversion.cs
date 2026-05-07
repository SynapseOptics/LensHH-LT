// Example 05: Convert between optical design file formats
//
// Demonstrates: Import and export across Zemax, Code V, OSLO, Optalix

using System;
using LensHH.API;

class Example05_FormatConversion
{
    static void Main()
    {
        var session = new LensHHSession();

        // Import from Zemax
        session.ImportZemax("samples/CookeTriplet.zmx");
        PrintSystem(session, "Imported from Zemax");

        // Export to all formats
        session.ExportCodeV("CookeTriplet.seq");
        session.ExportOslo("CookeTriplet.len");
        session.ExportOptalix("CookeTriplet.otx");
        Console.WriteLine("Exported to: .seq, .len, .otx\n");

        // Re-import each and verify
        session.ImportCodeV("CookeTriplet.seq");
        PrintSystem(session, "Re-imported from Code V");

        session.ImportOslo("CookeTriplet.len");
        PrintSystem(session, "Re-imported from OSLO");

        session.ImportOptalix("CookeTriplet.otx");
        PrintSystem(session, "Re-imported from Optalix");

        // Save as native format (preserves merit function, variables, config)
        session.SaveAs("CookeTriplet.lhlt");
        Console.WriteLine("Saved native format: CookeTriplet.lhlt");
    }

    static void PrintSystem(LensHHSession session, string label)
    {
        var sys = session.System;
        Console.WriteLine($"{label}:");
        Console.WriteLine($"  Title: {sys.Title}");
        Console.WriteLine($"  Surfaces: {sys.Surfaces.Count}");
        Console.WriteLine($"  Aperture: {sys.Aperture.Type} = {sys.Aperture.Value}");
        Console.WriteLine($"  Wavelengths: {sys.Wavelengths.Count}");
        Console.WriteLine($"  Fields: {sys.Fields.Count}");
        Console.WriteLine($"  Stop: surface {sys.StopSurfaceIndex}");
        Console.WriteLine();
    }
}
