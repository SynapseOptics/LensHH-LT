// Example 02: Build a lens from scratch and optimize
//
// Demonstrates: NewSystem, AddSurface, SetWavelengths, SetFields,
//               SetCurvatureVariable, AddOperand, Optimize

using System;
using LensHH.API;
using LensHH.Core.Enums;
using LensHH.Core.MeritFunction;

class Example02_BuildAndOptimize
{
    static void Main()
    {
        var session = new LensHHSession();

        // Create a new system
        session.NewSystem();
        session.SetTitle("Simple Singlet");
        session.SetAperture(ApertureType.EPD, 20.0);

        // Wavelengths: visible spectrum (F, d, C lines)
        session.SetWavelengths(new[] { 0.486, 0.587, 0.656 }, primaryIndex: 1);

        // Fields: on-axis + 10 degrees
        session.SetFields(new[] { 0.0, 10.0 });

        // Build a singlet lens: surface 0 is object (already exists)
        // Surface 1: front of lens (R=50mm, thickness=5mm, BK7 glass)
        session.SetSurface(1, radius: 50.0, thickness: 5.0, material: "BK7", isStop: true);

        // Surface 2: back of lens (R=-200mm, thickness to image)
        session.AddSurface(radius: -200.0, thickness: 95.0);

        // Surface 3 is image (already exists)
        Console.WriteLine($"System: {session.System.Title}");
        Console.WriteLine($"Surfaces: {session.System.Surfaces.Count}");

        // Check initial performance
        var parax = session.ParaxialData();
        Console.WriteLine($"Initial EFL: {parax.Efl:F2} mm");

        var spot0 = session.SpotDiagram(0);
        Console.WriteLine($"Initial spot F1: RMS={spot0.RmsRadius:E3} mm");

        // Set up optimization variables
        session.SetCurvatureVariable(1, true);  // front radius
        session.SetCurvatureVariable(2, true);  // back radius
        session.SetThicknessVariable(2, true, min: 80, max: 120);  // back focal distance

        // Build merit function
        session.NewMeritFunction();

        // Target EFL = 100mm
        session.AddOperand(OperandType.EFL, target: 100.0, weight: 1.0);

        // Minimize wavefront error (WAVEX = OPD with tilt removed)
        session.AddOperand(OperandType.WAVEX, target: 0, weight: 1.0, rings: 3, arms: 6);

        // Evaluate initial merit
        double initialMerit = session.EvaluateMerit();
        Console.WriteLine($"\nInitial merit: {initialMerit:F6}");

        // Optimize
        Console.WriteLine("Optimizing...");
        var result = session.Optimize();
        Console.WriteLine($"Final merit: {result.FinalMerit:F6}");
        Console.WriteLine($"Iterations: {result.Iterations}");
        Console.WriteLine($"Status: {result.Message}");

        // Check optimized performance
        var paraxOpt = session.ParaxialData();
        Console.WriteLine($"\nOptimized EFL: {paraxOpt.Efl:F2} mm");
        Console.WriteLine($"Front R: {session.System.Surfaces[1].Radius:F3} mm");
        Console.WriteLine($"Back R: {session.System.Surfaces[2].Radius:F3} mm");

        var spotOpt = session.SpotDiagram(0);
        Console.WriteLine($"Optimized spot F1: RMS={spotOpt.RmsRadius:E3} mm");

        // Save the optimized system
        session.SaveAs("optimized_singlet.lhlt");
        Console.WriteLine("\nSaved: optimized_singlet.lhlt");
    }
}
