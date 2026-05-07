using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LensHH.App.Session;

namespace LensHH.App.ViewModels;

public partial class CatalogPreferenceViewModel : ObservableObject
{
    private readonly GuiSession _session;

    public ObservableCollection<string> AvailableCatalogs { get; } = new();
    public ObservableCollection<string> PreferredCatalogs { get; } = new();

    [ObservableProperty] private string? _selectedAvailable;
    [ObservableProperty] private string? _selectedPreferred;

    public bool Accepted { get; set; }

    public CatalogPreferenceViewModel(GuiSession session)
    {
        _session = session;

        // Load current preferred catalogs
        foreach (var cat in session.System.GlassCatalogs)
            PreferredCatalogs.Add(cat);

        // Available = loaded catalogs minus already-preferred
        foreach (var cat in session.GlassCatalog.LoadedCatalogs.OrderBy(c => c))
        {
            if (!PreferredCatalogs.Contains(cat))
                AvailableCatalogs.Add(cat);
        }
    }

    [RelayCommand]
    private void Add()
    {
        if (SelectedAvailable == null) return;
        string cat = SelectedAvailable;
        AvailableCatalogs.Remove(cat);
        PreferredCatalogs.Add(cat);
    }

    [RelayCommand]
    private void Remove()
    {
        if (SelectedPreferred == null) return;
        string cat = SelectedPreferred;
        PreferredCatalogs.Remove(cat);
        // Re-insert in sorted position
        int idx = 0;
        while (idx < AvailableCatalogs.Count && string.Compare(AvailableCatalogs[idx], cat, StringComparison.OrdinalIgnoreCase) < 0)
            idx++;
        AvailableCatalogs.Insert(idx, cat);
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedPreferred == null) return;
        int idx = PreferredCatalogs.IndexOf(SelectedPreferred);
        if (idx > 0)
            PreferredCatalogs.Move(idx, idx - 1);
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedPreferred == null) return;
        int idx = PreferredCatalogs.IndexOf(SelectedPreferred);
        if (idx >= 0 && idx < PreferredCatalogs.Count - 1)
            PreferredCatalogs.Move(idx, idx + 1);
    }

    public void Apply()
    {
        _session.System.GlassCatalogs.Clear();
        foreach (var cat in PreferredCatalogs)
            _session.System.GlassCatalogs.Add(cat);
        _session.NotifySystemChanged("catalogs");
    }
}
