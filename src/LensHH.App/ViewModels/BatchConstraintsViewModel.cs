using System;
using CommunityToolkit.Mvvm.ComponentModel;
using LensHH.App.Session;

namespace LensHH.App.ViewModels;

public enum BatchConstraintParam { Thickness, Curvature }
public enum SurfaceFilter { All, Glass, Air }

public partial class BatchConstraintsViewModel : ObservableObject
{
    private readonly GuiSession _session;

    public BatchConstraintParam Param { get; }
    public string Title => Param == BatchConstraintParam.Thickness
        ? "Thickness Constraints" : "Curvature Constraints";

    [ObservableProperty] private int _surface1;
    [ObservableProperty] private int _surface2;

    // Surface filter radio buttons
    [ObservableProperty] private bool _filterAll = true;
    [ObservableProperty] private bool _filterGlass;
    [ObservableProperty] private bool _filterAir;

    // Constraint type
    public static System.Collections.Generic.List<string> ConstraintOptions { get; } = new()
        { "Unconstrained", "Min/Max", "Max Only", "Min Only" };

    [ObservableProperty] private int _constraintTypeIndex = 1; // default Min/Max

    [ObservableProperty] private string _minText = "";
    [ObservableProperty] private string _maxText = "";

    public bool IsMinEnabled => ConstraintTypeIndex == 1 || ConstraintTypeIndex == 3;
    public bool IsMaxEnabled => ConstraintTypeIndex == 1 || ConstraintTypeIndex == 2;

    public BatchConstraintsViewModel(GuiSession session, BatchConstraintParam param)
    {
        _session = session;
        Param = param;
        _surface1 = 1;
        _surface2 = session.System.Surfaces.Count - 2;
    }

    partial void OnFilterAllChanged(bool value) { if (value) { FilterGlass = false; FilterAir = false; } }
    partial void OnFilterGlassChanged(bool value) { if (value) { FilterAll = false; FilterAir = false; } }
    partial void OnFilterAirChanged(bool value) { if (value) { FilterAll = false; FilterGlass = false; } }

    partial void OnConstraintTypeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsMinEnabled));
        OnPropertyChanged(nameof(IsMaxEnabled));
    }

    public void Apply()
    {
        int s1 = Math.Max(0, Surface1);
        int s2 = Math.Min(_session.System.Surfaces.Count - 1, Surface2);

        double? min = null, max = null;
        if (IsMinEnabled && double.TryParse(MinText, out double mv)) min = mv;
        if (IsMaxEnabled && double.TryParse(MaxText, out double xv)) max = xv;

        // Unconstrained clears both
        if (ConstraintTypeIndex == 0) { min = null; max = null; }

        SurfaceFilter filter = FilterGlass ? SurfaceFilter.Glass :
                               FilterAir ? SurfaceFilter.Air : SurfaceFilter.All;

        for (int i = s1; i <= s2; i++)
        {
            var surf = _session.System.Surfaces[i];

            // Apply filter
            if (filter != SurfaceFilter.All)
            {
                bool hasGlass = !string.IsNullOrEmpty(surf.Material);
                if (filter == SurfaceFilter.Glass && !hasGlass) continue;
                if (filter == SurfaceFilter.Air && hasGlass) continue;
            }

            // Only apply to surfaces that are already variable
            if (Param == BatchConstraintParam.Thickness)
            {
                if (!surf.ThicknessVariable) continue;
                surf.ThicknessMin = min;
                surf.ThicknessMax = max;
            }
            else
            {
                if (!surf.CurvatureVariable) continue;
                surf.CurvatureMin = min;
                surf.CurvatureMax = max;
            }
        }
    }
}
