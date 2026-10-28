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

    public static string CookeTriplet =>
        Path.Combine(RepoRoot ?? throw new InvalidOperationException("no repository root"), "samples", "CookeTriplet.lhlt");
}
