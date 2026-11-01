using LensHH.Core.Enums;
using LensHH.Core.Glass;
using LensHH.Core.Models;

namespace LensHH.Tolerancing.Tests;

/// <summary>The lenses the tests tolerance: a model-glass singlet, and the repository's Cooke triplet.</summary>
internal static class Lenses
{
    public static string? RepoRoot { get; } = FindRoot();

    private static string? FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LensHH-LT.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>An f/5 singlet at infinite conjugates, model glass so it needs no catalog.</summary>
    public static OpticalSystem Singlet()
    {
        var sys = new OpticalSystem { Aperture = new Aperture(ApertureType.EPD, 10.0) };
        sys.Wavelengths.Add(new Wavelength(0.5876, 1.0, true));
        sys.Fields.Add(new Field { Y = 0 });
        sys.Fields.Add(new Field { Y = 3 });
        sys.Surfaces.Add(new Surface { Index = 0, Thickness = double.PositiveInfinity });
        sys.Surfaces.Add(new Surface
        {
            Index = 1, Radius = 50, Thickness = 5, IsStop = true,
            ModelIndexEnabled = true, ModelNd = 1.5168, ModelVd = 64.17,
        });
        sys.Surfaces.Add(new Surface { Index = 2, Radius = -50, Thickness = 47 });
        sys.Surfaces.Add(new Surface { Index = 3 });
        return sys;
    }

    public static GlassCatalogManager Glass()
    {
        var mgr = new GlassCatalogManager();
        if (RepoRoot != null)
        {
            string folder = Path.Combine(RepoRoot, "catalogs", "Glass");
            if (Directory.Exists(folder)) mgr.LoadCatalogsFromFolder(folder);
        }
        return mgr;
    }

    /// <summary>
    /// A Cooke triplet, f/5, its glasses SCHOTT's and its semi-diameters fixed (7, 5 and 7 mm): the
    /// lens the drawing tests take their values from.
    /// </summary>
    public static OpticalSystem Triplet()
    {
        var sys = Lens("COOKE TRIPLET F/5", 10.0, 0.0, 10.0, 14.0);
        sys.Surfaces.Add(Glass(1, 1.0 / 0.04706, 2.5, "N-SK16", 7));
        sys.Surfaces.Add(Air(2, 1.0 / -0.00630, 5.0, 7));
        sys.Surfaces.Add(Glass(3, 1.0 / -0.04938, 1.0, "F2", 5));
        sys.Surfaces.Add(Air(4, 1.0 / 0.05102, 2.0, 5));
        sys.Surfaces.Add(new Surface { Index = 5, Thickness = 2.0, IsStop = true });
        sys.Surfaces.Add(Glass(6, 1.0 / 0.00708, 2.5, "N-SK16", 7));
        sys.Surfaces.Add(Air(7, 1.0 / -0.05988, 43.55, 7));
        sys.Surfaces.Add(new Surface { Index = 8 });
        return sys;
    }

    /// <summary>A cemented achromat, f/5: N-BK7 and N-SF5, semi-diameters fixed at 11 mm.</summary>
    public static OpticalSystem Doublet()
    {
        var sys = Lens("CEMENTED DOUBLET F/5", 20.0, 0.0, 1.5);
        sys.Surfaces.Add(Glass(1, 1.0 / 0.016375, 6.0, "N-BK7", 11));
        sys.Surfaces[1].IsStop = true;
        sys.Surfaces.Add(Glass(2, 1.0 / -0.022640, 2.5, "N-SF5", 11));
        sys.Surfaces.Add(Air(3, 1.0 / -0.007758, 95.4, 11));
        sys.Surfaces.Add(new Surface { Index = 4 });
        return sys;
    }

    /// <summary>The lens, edited first if asked, as the tolerancing takes it.</summary>
    public static NominalLens Nominal(OpticalSystem system, Action<OpticalSystem>? edit = null)
    {
        edit?.Invoke(system);
        return new NominalLens(system, Glass(), FieldSymmetry.File);
    }

    private static OpticalSystem Lens(string title, double epd, params double[] fields)
    {
        var sys = new OpticalSystem { Title = title, Aperture = new Aperture(ApertureType.EPD, epd) };
        sys.GlassCatalogs.Add("SCHOTT");
        sys.Wavelengths.Add(new Wavelength(0.4861327, 1.0));
        sys.Wavelengths.Add(new Wavelength(0.5875618, 1.0, true));
        sys.Wavelengths.Add(new Wavelength(0.6562725, 1.0));
        foreach (double y in fields) sys.Fields.Add(new Field(y));
        sys.Surfaces.Add(new Surface { Index = 0, Thickness = double.PositiveInfinity });
        return sys;
    }

    private static Surface Glass(int index, double radius, double thickness, string glass, double semiDiameter) =>
        new()
        {
            Index = index, Radius = radius, Thickness = thickness, Material = glass,
            SemiDiameter = semiDiameter, SemiDiameterMode = SemiDiameterMode.Fixed,
        };

    private static Surface Air(int index, double radius, double thickness, double semiDiameter) =>
        new() { Index = index, Radius = radius, Thickness = thickness, SemiDiameter = semiDiameter, SemiDiameterMode = SemiDiameterMode.Fixed };

    public static string CookeTriplet =>
        Path.Combine(RepoRoot ?? throw new InvalidOperationException("no repository root"), "samples", "CookeTriplet.lhlt");
}
