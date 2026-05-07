using System;
using CommunityToolkit.Mvvm.ComponentModel;
using LensHH.App.Session;

namespace LensHH.App.ViewModels;

public enum VariableMode { Thickness, Curvature }

public partial class SetClearVariablesViewModel : ObservableObject
{
    private readonly GuiSession _session;

    public VariableMode Mode { get; }
    public string Title => Mode == VariableMode.Thickness
        ? "Set/Clear Thickness Variables"
        : "Set/Clear Curvature Variables";

    [ObservableProperty] private int _surface1;
    [ObservableProperty] private int _surface2;
    [ObservableProperty] private bool _isSet = true;
    [ObservableProperty] private bool _isClear;
    [ObservableProperty] private bool _ignoreInfRadius = true;

    public bool ShowIgnoreInfRadius => Mode == VariableMode.Curvature;

    public SetClearVariablesViewModel(GuiSession session, VariableMode mode)
    {
        _session = session;
        Mode = mode;
        _surface1 = 1;
        _surface2 = session.System.Surfaces.Count - 2; // last before image
    }

    partial void OnIsSetChanged(bool value)
    {
        if (value) IsClear = false;
    }

    partial void OnIsClearChanged(bool value)
    {
        if (value) IsSet = false;
    }

    public void Apply()
    {
        int s1 = Math.Max(0, Surface1);
        int s2 = Math.Min(_session.System.Surfaces.Count - 1, Surface2);

        for (int i = s1; i <= s2; i++)
        {
            var surf = _session.System.Surfaces[i];

            if (Mode == VariableMode.Thickness)
            {
                surf.ThicknessVariable = IsSet;
            }
            else // Curvature
            {
                if (IsSet && IgnoreInfRadius && double.IsInfinity(surf.Radius))
                    continue;
                surf.CurvatureVariable = IsSet;
            }
        }

        _session.NotifySystemChanged("properties");
    }
}
