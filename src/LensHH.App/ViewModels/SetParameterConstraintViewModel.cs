using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LensHH.App.Session;
using LensHH.Core.Enums;

namespace LensHH.App.ViewModels;

/// <summary>The surface-parameter families this bulk tool can vary/bound. Each maps to a distinct
/// storage on <see cref="LensHH.Core.Models.Surface"/>: Even Asphere → AsphericVariable/Min/Max[k];
/// Paraxial → FocalLengthVariable + FocalPowerMin/Max (diopters); ABCD → ParameterVariable/Min/Max[k].</summary>
public enum SurfaceParamKind { EvenAsphere, Paraxial, Abcd }

/// <summary>
/// Bulk "Surface Parameter Constraints" dialog (Variable Editor). Sets a chosen surface-type
/// parameter (Even Asphere A2..A16, Paraxial diopters, or ABCD A/B/C/D) as an optimization
/// variable — with an optional Min/Max bound — across a surface range, applying only to surfaces
/// of the selected type. Mirrors the Thickness/Curvature Constraints dialogs, and fills the gap
/// that Set/Clear Variables (thickness/curvature only) left for surface parameters — so a lens
/// with many ABCD surfaces can have, say, all four A/B/C/D set variable in one action.
/// </summary>
public partial class SetParameterConstraintViewModel : ObservableObject
{
    private readonly GuiSession _session;

    public string Title => "Surface Parameter Constraints";

    // Surface type combo — which parameter family to edit.
    public List<string> SurfaceTypeOptions { get; } = new() { "Even Asphere", "Paraxial", "ABCD" };
    [ObservableProperty] private int _surfaceTypeIndex = 2;  // default ABCD

    // Parameter combo — repopulated when the surface type changes.
    public ObservableCollection<string> ParameterOptions { get; } = new();
    [ObservableProperty] private int _parameterIndex;

    [ObservableProperty] private int _surface1;
    [ObservableProperty] private int _surface2;

    // Constraint type — bounds to apply (or Clear = make fixed / remove the variable).
    public List<string> ConstraintOptions { get; } = new()
        { "Variable (no bounds)", "Min/Max", "Max Only", "Min Only", "Clear (make fixed)" };
    [ObservableProperty] private int _constraintTypeIndex = 1;  // default Min/Max

    [ObservableProperty] private string _minText = "";
    [ObservableProperty] private string _maxText = "";

    public bool IsMinEnabled => ConstraintTypeIndex == 1 || ConstraintTypeIndex == 3;
    public bool IsMaxEnabled => ConstraintTypeIndex == 1 || ConstraintTypeIndex == 2;

    // The parameter combo is meaningful for Even Asphere / ABCD (multiple params); Paraxial has a
    // single choice (Diopters), so the combo is disabled but still shows it for clarity.
    public bool IsParameterSelectable => (SurfaceParamKind)SurfaceTypeIndex != SurfaceParamKind.Paraxial;

    public SetParameterConstraintViewModel(GuiSession session)
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

    partial void OnConstraintTypeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsMinEnabled));
        OnPropertyChanged(nameof(IsMaxEnabled));
    }

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
        bool clear = ConstraintTypeIndex == 4;
        bool setVar = !clear;

        double? min = null, max = null;
        if (!clear)
        {
            if (IsMinEnabled && double.TryParse(MinText, out double mv)) min = mv;
            if (IsMaxEnabled && double.TryParse(MaxText, out double xv)) max = xv;
        }

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
                    if (p < surf.AsphericVariable.Length)
                    {
                        surf.AsphericVariable[p] = setVar;
                        surf.AsphericMin[p] = min;
                        surf.AsphericMax[p] = max;
                    }
                    break;
                case SurfaceParamKind.Paraxial:
                    surf.FocalLengthVariable = setVar;
                    surf.FocalPowerMin = min;
                    surf.FocalPowerMax = max;
                    break;
                case SurfaceParamKind.Abcd:
                    if (p < surf.ParameterVariable.Length)
                    {
                        surf.ParameterVariable[p] = setVar;
                        surf.ParameterMin[p] = min;
                        surf.ParameterMax[p] = max;
                    }
                    break;
            }
        }

        _session.NotifySystemChanged("properties");
    }
}
