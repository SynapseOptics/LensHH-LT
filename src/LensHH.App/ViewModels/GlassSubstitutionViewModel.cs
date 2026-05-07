using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using LensHH.App.Session;
using LensHH.Core.Models;

namespace LensHH.App.ViewModels;

public partial class GlassSubstitutionRowViewModel : ObservableObject
{
    private readonly GlassSubstitutionSetting _setting;

    public GlassSubstitutionRowViewModel(int glassNumber, int surfaceIndex, string material,
        GlassSubstitutionSetting setting, List<string> catalogOptions)
    {
        GlassNumber = glassNumber;
        SurfaceIndex = surfaceIndex;
        Material = material;
        _setting = setting;
        CatalogOptions = catalogOptions;
    }

    public int GlassNumber { get; }
    public int SurfaceIndex { get; }
    public string Material { get; }
    public List<string> CatalogOptions { get; }

    public bool Substitute
    {
        get => _setting.Substitute;
        set { _setting.Substitute = value; OnPropertyChanged(); }
    }

    public int CatalogIndex
    {
        get
        {
            int idx = CatalogOptions.IndexOf(_setting.CatalogName);
            return idx >= 0 ? idx : 0;
        }
        set
        {
            if (value >= 0 && value < CatalogOptions.Count)
            {
                _setting.CatalogName = CatalogOptions[value];
                OnPropertyChanged();
            }
        }
    }
}

public partial class GlassSubstitutionViewModel : ObservableObject
{
    private readonly GuiSession _session;

    public ObservableCollection<GlassSubstitutionRowViewModel> Rows { get; } = new();
    public bool HasGlasses => Rows.Count > 0;
    public string NoGlassesMessage => HasGlasses ? "" : "No glass surfaces in the system.";

    public GlassSubstitutionViewModel(GuiSession session)
    {
        _session = session;
        Refresh();
    }

    public void Refresh()
    {
        Rows.Clear();

        var catalogOptions = GetFilteredCatalogNames();
        int glassNum = 1;

        for (int i = 0; i < _session.System.Surfaces.Count; i++)
        {
            var surf = _session.System.Surfaces[i];
            if (string.IsNullOrEmpty(surf.Material)) continue;
            if (surf.Material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase)) continue;

            // Find or create a setting for this surface
            var setting = _session.System.GlassSubstitutions
                .FirstOrDefault(gs => gs.SurfaceIndex == i);
            if (setting == null)
            {
                setting = new GlassSubstitutionSetting { SurfaceIndex = i };
                _session.System.GlassSubstitutions.Add(setting);
            }

            // Default catalog to first available if not set
            if (string.IsNullOrEmpty(setting.CatalogName) && catalogOptions.Count > 0)
                setting.CatalogName = catalogOptions[0];

            Rows.Add(new GlassSubstitutionRowViewModel(
                glassNum++, i, surf.Material, setting, catalogOptions));
        }

        OnPropertyChanged(nameof(HasGlasses));
        OnPropertyChanged(nameof(NoGlassesMessage));
    }

    private List<string> GetFilteredCatalogNames()
    {
        var names = new List<string>();
        var dir = FindFilteredCatalogFolder();

        if (dir != null)
        {
            foreach (var file in Directory.GetFiles(dir, "*.agf"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
                    names.Add(name);
            }
        }

        return names;
    }

    /// <summary>
    /// Find the FilteredGlassCatalogues folder. Returns the first existing path, or null.
    /// </summary>
    public static string? FindFilteredCatalogFolder()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var searchPaths = new[]
        {
            Path.Combine(baseDir, "catalogs", "FilteredGlassCatalogues"),
            Path.Combine(baseDir, "..", "catalogs", "FilteredGlassCatalogues"),
            // Dev layout: bin/Debug/net8.0 → src/LensHH.App → src → LensHH-LT → catalogs
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "catalogs", "FilteredGlassCatalogues"),
        };

        foreach (var path in searchPaths)
        {
            var full = Path.GetFullPath(path);
            if (Directory.Exists(full))
                return full;
        }
        return null;
    }

    public GuiSession Session => _session;
}
