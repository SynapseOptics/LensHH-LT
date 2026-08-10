using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using LensHH.App.Session;
using LensHH.Core.Enums;

namespace LensHH.App.ViewModels;

/// <summary>
/// Bulk "Set/Clear Surface Parameter Variables" (LDE). Sets or clears the optimization-VARIABLE
/// flag on a chosen surface-type parameter — Even Asphere A2..A16, Paraxial diopters, or ABCD
/// A/B/C/D — across a surface range, applying only to surfaces of the selected type. This is the
/// exact analogue of Set/Clear Thickness/Curvature Variables (flag only, NO Min/Max bounds); it
/// borrows only the Surface-Type + Parameter selectors from the Surface Parameter Constraints
/// dialog. E.g. select ABCD + A, Set → every ABCD surface in the range gets A as a variable.
/// </summary>
public partial class SetClearParameterVariablesViewModel : ObservableObject
{
    private readonly GuiSession _session;

    public string Title => "Set/Clear Surface Parameter Variables";

    // Surface type combo — which parameter family to edit.
    public List<string> SurfaceTypeOptions { get; } = new() { "Even Asphere", "Paraxial", "ABCD" };
    [ObservableProperty] private int _surfaceTypeIndex = 2;  // default ABCD

    // Parameter combo — repopulated when the surface type changes.
    public ObservableCollection<string> ParameterOptions { get; } = new();
    [ObservableProperty] private int _parameterIndex;

    [ObservableProperty] private int _surface1;
    [ObservableProperty] private int _surface2;

    [ObservableProperty] private bool _isSet = true;
    [ObservableProperty] private bool _isClear;

    // Paraxial has a single parameter (Diopters); the combo is disabled but still shown for clarity.
    public bool IsParameterSelectable => (SurfaceParamKind)SurfaceTypeIndex != SurfaceParamKind.Paraxial;

    public SetClearParameterVariablesViewModel(GuiSession session)
    {
        _session = session;
        _surface1 = 1;
        _surface2 = Math.Max(1, session.System.Surfaces.Count - 2); // last before image
        RebuildParameterOptions();
    }

    partial void OnSurfaceTypeIndexChanged(int value)
    {
        RebuildParameterOptions();
        OnPropertyChanged(nameof(IsParameterSelectable));
    }

    partial void OnIsSetChanged(bool value) { if (value) IsClear = false; }
    partial void OnIsClearChanged(bool value) { if (value) IsSet = false; }

    private void RebuildParameterOptions()
    {
        ParameterOptions.Clear();
        switch ((SurfaceParamKind)SurfaceTypeIndex)
        {
            case SurfaceParamKind.EvenAsphere:
                foreach (var n in new[] { "A2", "A4", "A6", "A8", "A10", "A12", "A14", "A16" })
                    ParameterOptions.Add(n);
                break;
            case SurfaceParamKind.Paraxial:
                ParameterOptions.Add("Diopters");
                break;
            case SurfaceParamKind.Abcd:
                foreach (var n in new[] { "A", "B", "C", "D" })
                    ParameterOptions.Add(n);
                break;
        }
        ParameterIndex = 0;
    }

    public void Apply()
    {
        int s1 = Math.Max(0, Surface1);
        int s2 = Math.Min(_session.System.Surfaces.Count - 1, Surface2);
        var kind = (SurfaceParamKind)SurfaceTypeIndex;
        int p = Math.Max(0, ParameterIndex);
        bool set = IsSet;   // false = Clear

        SurfaceType targetType = kind switch
        {
            SurfaceParamKind.EvenAsphere => SurfaceType.EvenAsphere,
            SurfaceParamKind.Paraxial => SurfaceType.Paraxial,
            _ => SurfaceType.Abcd
        };

        for (int i = s1; i <= s2; i++)
        {
            var surf = _session.System.Surfaces[i];
            if (surf.Type != targetType) continue;

            switch (kind)
            {
                case SurfaceParamKind.EvenAsphere:
                    if (p < surf.AsphericVariable.Length) surf.AsphericVariable[p] = set;
                    break;
                case SurfaceParamKind.Paraxial:
                    surf.FocalLengthVariable = set;
                    break;
                case SurfaceParamKind.Abcd:
                    if (p < surf.ParameterVariable.Length) surf.ParameterVariable[p] = set;
                    break;
            }
        }

        _session.NotifySystemChanged("properties");
    }
}
