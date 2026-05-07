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

public partial class GlassCatalogGeneratorViewModel : ObservableObject
{
    private readonly AgfFileParser _parser = new();
    private readonly GlassFilterService _filter = new();
    private readonly CatalogExportService _exporter = new();

    public GlassCatalogGeneratorViewModel()
    {
        AvailableCatalogs = new ObservableCollection<string>();
        SelectedCatalogs = new ObservableCollection<string>();
        GeneratedGlasses = new ObservableCollection<GlassEntry>();

        var (catDir, filtDir) = ResolveDefaultDirectories();
        CatalogDirectory = catDir;
        FilteredCatalogDirectory = filtDir;
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

        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (Directory.Exists(full))
            {
                var catalogsRoot = full.EndsWith("Glass", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetDirectoryName(full)!
                    : full;
                return (full, Path.Combine(catalogsRoot, "FilteredGlassCatalogues"));
            }
        }

        return (Path.Combine(baseDir, "catalogs"), Path.Combine(baseDir, "FilteredGlassCatalogues"));
    }

    // --- Directory Paths ---

    [ObservableProperty]
    private string _catalogDirectory = "";

    partial void OnCatalogDirectoryChanged(string value)
    {
        SelectedCatalogs.Clear();
        GeneratedGlasses.Clear();
        GlassCount = "0 glasses";
        LoadAvailableCatalogs();
    }

    [ObservableProperty]
    private string _filteredCatalogDirectory = "";

    // --- Busy / Status ---

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Ready";

    // --- Catalog Selection ---

    public ObservableCollection<string> AvailableCatalogs { get; }
    public ObservableCollection<string> SelectedCatalogs { get; }

    [ObservableProperty]
    private string? _selectedAvailableCatalog;

    [ObservableProperty]
    private string? _selectedActiveCatalog;

    // --- Filter Properties ---

    [ObservableProperty] private bool _preferredEnabled;

    [ObservableProperty] private bool _distanceEnabled;
    [ObservableProperty] private double _distanceThreshold = 0.05;
    [ObservableProperty] private double _wn = 1.0;
    [ObservableProperty] private double _wa = 1E-04;
    [ObservableProperty] private double _wp = 1E+02;
    [ObservableProperty] private double _ndTarget = 1.5168;
    [ObservableProperty] private double _vdTarget = 64.17;
    [ObservableProperty] private double _dPgFTarget = 0.0;

    [ObservableProperty] private bool _costEnabled;
    [ObservableProperty] private double _costLimit = 5.0;

    [ObservableProperty] private bool _ndRangeEnabled;
    [ObservableProperty] private double _ndMin = 1.4;
    [ObservableProperty] private double _ndMax = 2.1;

    [ObservableProperty] private bool _vdRangeEnabled;
    [ObservableProperty] private double _vdMin = 0;
    [ObservableProperty] private double _vdMax = 100;

    [ObservableProperty] private bool _dPgFRangeEnabled;
    [ObservableProperty] private double _dPgFMin = -0.2;
    [ObservableProperty] private double _dPgFMax = 0.2;

    [ObservableProperty] private bool _tCERangeEnabled;
    [ObservableProperty] private double _tCEMin = 0;
    [ObservableProperty] private double _tCEMax = 20;

    [ObservableProperty] private bool _minWavelengthEnabled;
    [ObservableProperty] private double _minWavelengthValue = 0.42;

    [ObservableProperty] private bool _maxWavelengthEnabled;
    [ObservableProperty] private double _maxWavelengthValue = 2.0;

    [ObservableProperty] private bool _meltFrequencyEnabled;
    [ObservableProperty] private int _meltFrequencyLimit = 3;

    // --- Generated Glasses ---

    public ObservableCollection<GlassEntry> GeneratedGlasses { get; }

    [ObservableProperty]
    private GlassEntry? _selectedGlass;

    [ObservableProperty]
    private string _glassCount = "0 glasses";

    // --- Sort ---

    public string[] SortOptions { get; } = { "Name", "Nd", "Vd", "DPgF", "Relative Cost", "TCE" };

    [ObservableProperty]
    private string _selectedSort = "Name";

    partial void OnSelectedSortChanged(string value) => ApplySort();

    // --- Export ---

    [ObservableProperty]
    private string _catalogName = "";

    // --- Dialog callbacks (set by the View) ---
    public Func<string, string, Task>? ShowMessage { get; set; }
    public Func<string, Task<string?>>? RequestFolderPath { get; set; }

    // --- Commands ---

    [RelayCommand]
    private void AddCatalog()
    {
        if (SelectedAvailableCatalog == null) return;
        string cat = SelectedAvailableCatalog;
        AvailableCatalogs.Remove(cat);
        SelectedCatalogs.Add(cat);
    }

    [RelayCommand]
    private void RemoveCatalog()
    {
        if (SelectedActiveCatalog == null) return;
        string cat = SelectedActiveCatalog;
        SelectedCatalogs.Remove(cat);
        AvailableCatalogs.Add(cat);
        var sorted = AvailableCatalogs.OrderBy(c => c).ToList();
        AvailableCatalogs.Clear();
        foreach (var c in sorted) AvailableCatalogs.Add(c);
    }

    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (SelectedCatalogs.Count == 0 || IsBusy) return;

        IsBusy = true;
        StatusText = "Loading and filtering glasses...";
        try
        {
            var allGlasses = await Task.Run(() =>
            {
                var catalogs = _parser.DiscoverCatalogs(CatalogDirectory);
                var glasses = new List<GlassEntry>();

                foreach (var catName in SelectedCatalogs)
                {
                    if (catalogs.TryGetValue(catName, out string? path))
                    {
                        var parsed = _parser.ParseCatalog(path, catName);
                        glasses.AddRange(parsed);
                    }
                }

                // Sync filter settings
                _filter.PreferredEnabled = PreferredEnabled;
                _filter.DistanceEnabled = DistanceEnabled;
                _filter.DistanceThreshold = DistanceThreshold;
                _filter.Wn = Wn;
                _filter.Wa = Wa;
                _filter.Wp = Wp;
                _filter.NdTarget = NdTarget;
                _filter.VdTarget = VdTarget;
                _filter.DPgFTarget = DPgFTarget;
                _filter.CostEnabled = CostEnabled;
                _filter.CostLimit = CostLimit;
                _filter.NdRangeEnabled = NdRangeEnabled;
                _filter.NdMin = NdMin;
                _filter.NdMax = NdMax;
                _filter.VdRangeEnabled = VdRangeEnabled;
                _filter.VdMin = VdMin;
                _filter.VdMax = VdMax;
                _filter.DPgFRangeEnabled = DPgFRangeEnabled;
                _filter.DPgFMin = DPgFMin;
                _filter.DPgFMax = DPgFMax;
                _filter.TCERangeEnabled = TCERangeEnabled;
                _filter.TCEMin = TCEMin;
                _filter.TCEMax = TCEMax;
                _filter.MinWavelengthEnabled = MinWavelengthEnabled;
                _filter.MinWavelengthValue = MinWavelengthValue;
                _filter.MaxWavelengthEnabled = MaxWavelengthEnabled;
                _filter.MaxWavelengthValue = MaxWavelengthValue;
                _filter.MeltFrequencyEnabled = MeltFrequencyEnabled;
                _filter.MeltFrequencyLimit = MeltFrequencyLimit;

                return _filter.Apply(glasses);
            });

            GeneratedGlasses.Clear();
            foreach (var g in allGlasses)
                GeneratedGlasses.Add(g);

            ApplySort();

            GlassCount = $"{GeneratedGlasses.Count} glasses";
            StatusText = $"Generated {GeneratedGlasses.Count} glasses from {SelectedCatalogs.Count} catalog(s).";
        }
        catch (Exception ex)
        {
            StatusText = "Error: " + ex.Message;
            if (ShowMessage != null) await ShowMessage("Generate Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void RemoveGlass()
    {
        if (SelectedGlass == null) return;
        GeneratedGlasses.Remove(SelectedGlass);
        GlassCount = $"{GeneratedGlasses.Count} glasses";
    }

    [RelayCommand]
    private async Task SaveCatalogAsync()
    {
        if (GeneratedGlasses.Count == 0 || string.IsNullOrWhiteSpace(CatalogName) || IsBusy)
            return;

        IsBusy = true;
        try
        {
            string name = CatalogName.Trim();

            var duplicates = _exporter.FindDuplicateNames(GeneratedGlasses);
            if (duplicates.Count > 0)
            {
                string msg = "Duplicate glass names found:\n" + string.Join("\n", duplicates);
                if (ShowMessage != null) await ShowMessage("Duplicate Names", msg);
                return;
            }

            if (!Directory.Exists(FilteredCatalogDirectory))
                Directory.CreateDirectory(FilteredCatalogDirectory);

            string outputPath = Path.Combine(FilteredCatalogDirectory, name + ".agf");

            await Task.Run(() => _exporter.Export(GeneratedGlasses, outputPath, name));

            StatusText = $"Saved {GeneratedGlasses.Count} glasses to {outputPath}";
            if (ShowMessage != null) await ShowMessage("Success", $"Catalog saved to:\n{outputPath}");
        }
        catch (Exception ex)
        {
            StatusText = "Save error: " + ex.Message;
            if (ShowMessage != null) await ShowMessage("Save Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task BrowseCatalogDirAsync()
    {
        if (RequestFolderPath == null) return;
        var path = await RequestFolderPath(CatalogDirectory);
        if (!string.IsNullOrEmpty(path))
            CatalogDirectory = path;
    }

    [RelayCommand]
    private async Task BrowseFilteredDirAsync()
    {
        if (RequestFolderPath == null) return;
        var path = await RequestFolderPath(FilteredCatalogDirectory);
        if (!string.IsNullOrEmpty(path))
            FilteredCatalogDirectory = path;
    }

    // --- Helpers ---

    private void LoadAvailableCatalogs()
    {
        AvailableCatalogs.Clear();
        var catalogs = _parser.DiscoverCatalogs(CatalogDirectory);
        foreach (var name in catalogs.Keys.OrderBy(k => k))
        {
            if (!SelectedCatalogs.Contains(name))
                AvailableCatalogs.Add(name);
        }

        if (catalogs.Count > 0)
            StatusText = $"Found {catalogs.Count} catalogs in {CatalogDirectory}";
        else
            StatusText = $"No catalogs found. Place .agf files in: {CatalogDirectory}";
    }

    private void ApplySort()
    {
        if (GeneratedGlasses.Count == 0) return;

        List<GlassEntry> sorted = SelectedSort switch
        {
            "Nd" => GeneratedGlasses.OrderBy(g => g.Nd).ToList(),
            "Vd" => GeneratedGlasses.OrderBy(g => g.Vd).ToList(),
            "DPgF" => GeneratedGlasses.OrderBy(g => g.DPgF).ToList(),
            "Relative Cost" => GeneratedGlasses.OrderBy(g => g.RelativeCost).ToList(),
            "TCE" => GeneratedGlasses.OrderBy(g => g.TCE).ToList(),
            _ => GeneratedGlasses.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList()
        };

        GeneratedGlasses.Clear();
        foreach (var g in sorted)
            GeneratedGlasses.Add(g);
    }
}
