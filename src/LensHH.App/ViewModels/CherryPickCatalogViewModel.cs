using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LensHH.App.GlassCatalog;

namespace LensHH.App.ViewModels;

/// <summary>
/// Single source-catalog row with a checkbox. When the checkbox toggles,
/// the parent ViewModel rebuilds the flattened source-glass list.
/// </summary>
public partial class CatalogPickRow : ObservableObject
{
    public string Name { get; }
    public string Path { get; }
    private readonly Action _onChanged;

    [ObservableProperty] private bool _isChecked;

    partial void OnIsCheckedChanged(bool value) => _onChanged();

    public CatalogPickRow(string name, string path, Action onChanged)
    {
        Name = name;
        Path = path;
        _onChanged = onChanged;
    }
}

public partial class CherryPickCatalogViewModel : ObservableObject
{
    private readonly AgfFileParser _parser = new();
    private readonly CatalogExportService _exporter = new();

    // All glasses loaded from checked source catalogs, before search filter
    private readonly List<GlassEntry> _allSourceGlasses = new();

    public ObservableCollection<CatalogPickRow> AvailableCatalogs { get; } = new();

    /// <summary>Source glasses visible in the left list (after search filter).</summary>
    public ObservableCollection<GlassEntry> SourceGlasses { get; } = new();

    /// <summary>Glasses chosen for the new catalog.</summary>
    public ObservableCollection<GlassEntry> ChosenGlasses { get; } = new();

    [ObservableProperty] private string _catalogDirectory = "";
    [ObservableProperty] private string _filteredCatalogDirectory = "";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _newCatalogName = "";
    [ObservableProperty] private string _statusText = "";

    [ObservableProperty] private GlassEntry? _selectedSourceGlass;
    [ObservableProperty] private GlassEntry? _selectedChosenGlass;

    public string SourceCount => $"{SourceGlasses.Count} glass(es)";
    public string ChosenCount => $"{ChosenGlasses.Count} chosen";

    /// <summary>Optional folder picker provided by the dialog. (path-in, path-out)</summary>
    public Func<string, Task<string?>>? RequestFolderPath { get; set; }
    /// <summary>Optional file picker for "Load Existing" — returns chosen .agf path.</summary>
    public Func<string, Task<string?>>? RequestOpenFilePath { get; set; }
    /// <summary>Optional info/error display.</summary>
    public Func<string, string, Task>? ShowMessage { get; set; }

    public CherryPickCatalogViewModel()
    {
        var (catDir, filtDir) = ResolveDefaultDirectories();
        CatalogDirectory = catDir;
        FilteredCatalogDirectory = filtDir;
        LoadAvailableCatalogs();
    }

    private static (string catalogDir, string filteredDir) ResolveDefaultDirectories()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "catalogs", "Glass"),
            Path.Combine(baseDir, "..", "catalogs", "Glass"),
            Path.Combine(baseDir, "catalogs"),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "catalogs", "Glass"),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "catalogs"),
            Path.Combine(baseDir, "..", "..", "..", "catalogs", "Glass"),
            Path.Combine(baseDir, "..", "..", "..", "catalogs"),
        };
        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (Directory.Exists(full))
            {
                var catalogsRoot = full.EndsWith("Glass", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetDirectoryName(full)!
                    : full;
                return (full, Path.Combine(catalogsRoot, "FilteredGlassCatalogues"));
            }
        }
        return (Path.Combine(baseDir, "catalogs"),
                Path.Combine(baseDir, "FilteredGlassCatalogues"));
    }

    partial void OnCatalogDirectoryChanged(string value)
    {
        ChosenGlasses.Clear();
        _allSourceGlasses.Clear();
        SourceGlasses.Clear();
        OnPropertyChanged(nameof(SourceCount));
        OnPropertyChanged(nameof(ChosenCount));
        LoadAvailableCatalogs();
    }

    partial void OnSearchTextChanged(string value) => RefreshSourceGlasses();

    private void LoadAvailableCatalogs()
    {
        AvailableCatalogs.Clear();
        if (!Directory.Exists(CatalogDirectory))
        {
            StatusText = $"No catalogs found at {CatalogDirectory}";
            return;
        }
        var found = _parser.DiscoverCatalogs(CatalogDirectory);
        foreach (var kv in found.OrderBy(k => k.Key))
        {
            AvailableCatalogs.Add(new CatalogPickRow(kv.Key, kv.Value, OnAnyCatalogToggled));
        }
        StatusText = $"{AvailableCatalogs.Count} source catalog(s) available";
    }

    private void OnAnyCatalogToggled()
    {
        // Rebuild the flattened source-glass list from currently checked
        // catalogs. Keep ChosenGlasses intact; the user's selections aren't
        // tied to which catalogs are checked.
        _allSourceGlasses.Clear();
        foreach (var c in AvailableCatalogs.Where(c => c.IsChecked))
        {
            try { _allSourceGlasses.AddRange(_parser.ParseCatalog(c.Path, c.Name)); }
            catch { /* skip a catalog that fails to parse */ }
        }
        RefreshSourceGlasses();
    }

    /// <summary>Rebuilds SourceGlasses from _allSourceGlasses applying SearchText filter.</summary>
    private void RefreshSourceGlasses()
    {
        SourceGlasses.Clear();
        IEnumerable<GlassEntry> q = _allSourceGlasses;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string s = SearchText.Trim();
            q = q.Where(g => g.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        foreach (var g in q.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            SourceGlasses.Add(g);
        OnPropertyChanged(nameof(SourceCount));
    }

    [RelayCommand]
    private void AddSelected()
    {
        if (SelectedSourceGlass == null) return;
        AddGlass(SelectedSourceGlass);
    }

    /// <summary>Public so the dialog can wire up double-click on the source row.</summary>
    public void AddGlass(GlassEntry g)
    {
        if (ChosenGlasses.Any(x => string.Equals(x.Name, g.Name, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText = $"{g.Name} is already in the chosen list";
            return;
        }
        ChosenGlasses.Add(g);
        OnPropertyChanged(nameof(ChosenCount));
        StatusText = $"Added {g.Name}";
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (SelectedChosenGlass == null) return;
        var g = SelectedChosenGlass;
        ChosenGlasses.Remove(g);
        OnPropertyChanged(nameof(ChosenCount));
        StatusText = $"Removed {g.Name}";
    }

    [RelayCommand]
    private async Task LoadExistingAsync()
    {
        if (RequestOpenFilePath == null) return;
        string startDir = Directory.Exists(FilteredCatalogDirectory)
            ? FilteredCatalogDirectory : CatalogDirectory;
        string? path = await RequestOpenFilePath(startDir);
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        try
        {
            string name = Path.GetFileNameWithoutExtension(path);
            var entries = _parser.ParseCatalog(path, name);
            ChosenGlasses.Clear();
            foreach (var g in entries) ChosenGlasses.Add(g);
            NewCatalogName = name; // seed the save name
            OnPropertyChanged(nameof(ChosenCount));
            StatusText = $"Loaded {entries.Count} glasses from {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load: {ex.Message}";
            if (ShowMessage != null) await ShowMessage("Load failed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (ChosenGlasses.Count == 0)
        {
            StatusText = "No glasses chosen — pick some before saving";
            return;
        }
        if (string.IsNullOrWhiteSpace(NewCatalogName))
        {
            StatusText = "Catalog name required";
            return;
        }
        try
        {
            string name = NewCatalogName.Trim();
            if (!Directory.Exists(FilteredCatalogDirectory))
                Directory.CreateDirectory(FilteredCatalogDirectory);

            // Dedupe by name (keep first occurrence). The Add button already
            // refuses dupes, but Load Existing or hand-loaded data could
            // contain them — silent dedupe per the design.
            var deduped = ChosenGlasses
                .GroupBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .Select(grp => grp.First())
                .ToList();

            string path = Path.Combine(FilteredCatalogDirectory, name + ".agf");
            await Task.Run(() => _exporter.Export(deduped, path, name));
            StatusText = $"Saved {deduped.Count} glasses to {path}";
            if (ShowMessage != null)
                await ShowMessage("Catalog saved", $"{deduped.Count} glasses written to:\n{path}");
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
            if (ShowMessage != null) await ShowMessage("Save failed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task BrowseSourceDirAsync()
    {
        if (RequestFolderPath == null) return;
        var picked = await RequestFolderPath(CatalogDirectory);
        if (!string.IsNullOrEmpty(picked)) CatalogDirectory = picked;
    }

    [RelayCommand]
    private async Task BrowseFilteredDirAsync()
    {
        if (RequestFolderPath == null) return;
        var picked = await RequestFolderPath(FilteredCatalogDirectory);
        if (!string.IsNullOrEmpty(picked)) FilteredCatalogDirectory = picked;
    }

    [RelayCommand]
    private void ClearChosen()
    {
        ChosenGlasses.Clear();
        OnPropertyChanged(nameof(ChosenCount));
        StatusText = "Cleared";
    }
}
