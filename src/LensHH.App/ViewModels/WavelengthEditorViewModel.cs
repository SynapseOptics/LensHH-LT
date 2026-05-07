using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LensHH.App.Session;
using LensHH.Core.Models;

namespace LensHH.App.ViewModels;

public partial class WavelengthRowViewModel : ObservableObject
{
    private readonly Wavelength _wl;
    private readonly GuiSession _session;
    private readonly int _index;
    /// <summary>Resolved (material, min, max) ranges from the parent VM.
    /// MIRROR / unresolved / range-less materials are excluded.</summary>
    private readonly IReadOnlyList<(string name, double min, double max)> _materialRanges;

    public WavelengthRowViewModel(Wavelength wl, int index, GuiSession session,
        IReadOnlyList<(string name, double min, double max)> materialRanges)
    {
        _wl = wl; _index = index; _session = session;
        _materialRanges = materialRanges;
        RecomputeRangeStatus();
    }

    public int Number => _index + 1;

    public string ValueText
    {
        get => _wl.Value.ToString("F4", CultureInfo.InvariantCulture);
        set
        {
            if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out double v))
            {
                _wl.Value = v;
                OnPropertyChanged();
                RecomputeRangeStatus();
            }
        }
    }

    public string WeightText
    {
        get => _wl.Weight.ToString("F2", CultureInfo.InvariantCulture);
        set { if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out double v)) { _wl.Weight = v; OnPropertyChanged(); } }
    }

    public bool IsPrimary
    {
        get => _wl.IsPrimary;
        set
        {
            if (value)
                foreach (var w in _session.System.Wavelengths) w.IsPrimary = false;
            _wl.IsPrimary = value;
            OnPropertyChanged();
        }
    }

    [ObservableProperty] private bool _isOutOfRange;
    [ObservableProperty] private string _rangeWarning = "";

    private void RecomputeRangeStatus()
    {
        var bad = _materialRanges
            .Where(r => _wl.Value < r.min || _wl.Value > r.max)
            .ToList();
        IsOutOfRange = bad.Count > 0;
        RangeWarning = bad.Count > 0
            ? "Outside range of: " + string.Join(", ",
                bad.Select(r => $"{r.name} [{r.min:G4}\u2013{r.max:G4} \u00b5m]"))
            : "";
    }
}

public partial class WavelengthEditorViewModel : ObservableObject
{
    private readonly GuiSession _session;
    public ObservableCollection<WavelengthRowViewModel> Wavelengths { get; } = new();

    [ObservableProperty] private WavelengthRowViewModel? _selectedWavelength;

    public WavelengthEditorViewModel(GuiSession session)
    {
        _session = session;
        Refresh();
    }

    public void Refresh()
    {
        // Resolve each material's wavelength range once, then hand the
        // list to every row so per-cell validation is cheap.
        var ranges = new List<(string name, double min, double max)>();
        var preferred = _session.System.GlassCatalogs.Count > 0 ? _session.System.GlassCatalogs : null;
        for (int i = 0; i < _session.System.Surfaces.Count; i++)
        {
            var mat = _session.System.Surfaces[i].Material;
            if (string.IsNullOrEmpty(mat)) continue;
            if (mat.Equals("MIRROR", System.StringComparison.OrdinalIgnoreCase)) continue;
            var glass = _session.GlassCatalog.GetGlass(mat, preferred);
            if (glass == null) continue;
            if (glass.WavelengthMin <= 0 || glass.WavelengthMax <= 0) continue;
            ranges.Add((glass.Name, glass.WavelengthMin, glass.WavelengthMax));
        }

        Wavelengths.Clear();
        for (int i = 0; i < _session.System.Wavelengths.Count; i++)
            Wavelengths.Add(new WavelengthRowViewModel(_session.System.Wavelengths[i], i, _session, ranges));
    }

    [RelayCommand]
    public void Add()
    {
        _session.System.Wavelengths.Add(new Wavelength(0.55, 1.0));
        Refresh();
    }

    [RelayCommand]
    public void Insert()
    {
        int idx = SelectedWavelength != null ? Wavelengths.IndexOf(SelectedWavelength) : 0;
        if (idx < 0) idx = 0;
        _session.System.Wavelengths.Insert(idx, new Wavelength(0.55, 1.0));
        Refresh();
    }

    [RelayCommand]
    public void Remove()
    {
        if (SelectedWavelength == null || _session.System.Wavelengths.Count <= 1) return;
        int idx = Wavelengths.IndexOf(SelectedWavelength);
        if (idx >= 0 && idx < _session.System.Wavelengths.Count)
        {
            _session.System.Wavelengths.RemoveAt(idx);
            Refresh();
        }
    }

    public void Apply() => _session.NotifySystemChanged("wavelength");
}
