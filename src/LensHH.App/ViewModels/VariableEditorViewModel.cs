using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using LensHH.App.Session;
using LensHH.Core.Models;

namespace LensHH.App.ViewModels;

public enum ConstraintType { Unconstrained, MinMax, MaxOnly, MinOnly }

public partial class VariableRowViewModel : ObservableObject
{
    private readonly Surface _surface;
    private readonly string _param; // "Curvature", "Thickness", "Conic", "Aspheric"
    private readonly int _asphericIndex; // -1 if not aspheric

    public VariableRowViewModel(int number, Surface surface, string param, int asphericIndex = -1)
    {
        Number = number;
        _surface = surface;
        _param = param;
        _asphericIndex = asphericIndex;
    }

    public int Number { get; }

    public string Description => _param == "Aspheric"
        ? $"Asphere A{(_asphericIndex + 1) * 2}" // A2, A4, A6, ... A16
        : _param;

    public int SurfaceIndex => _surface.Index;

    // ── Constraint Type ──

    public static List<string> ConstraintOptions { get; } = new()
        { "Unconstrained", "Min/Max", "Max Only", "Min Only" };

    public int ConstraintTypeIndex
    {
        get
        {
            bool hasMin = GetMin().HasValue;
            bool hasMax = GetMax().HasValue;
            if (hasMin && hasMax) return 1; // Min/Max
            if (hasMax) return 2; // Max Only
            if (hasMin) return 3; // Min Only
            return 0; // Unconstrained
        }
        set
        {
            switch (value)
            {
                case 0: // Unconstrained
                    SetMin(null); SetMax(null); break;
                case 1: // Min/Max
                    if (!GetMin().HasValue) SetMin(0);
                    if (!GetMax().HasValue) SetMax(0);
                    break;
                case 2: // Max Only
                    SetMin(null);
                    if (!GetMax().HasValue) SetMax(0);
                    break;
                case 3: // Min Only
                    if (!GetMin().HasValue) SetMin(0);
                    SetMax(null);
                    break;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(MinText));
            OnPropertyChanged(nameof(MaxText));
            OnPropertyChanged(nameof(IsMinEnabled));
            OnPropertyChanged(nameof(IsMaxEnabled));
        }
    }

    // ── Min / Max ──

    public bool IsMinEnabled => ConstraintTypeIndex == 1 || ConstraintTypeIndex == 3;
    public bool IsMaxEnabled => ConstraintTypeIndex == 1 || ConstraintTypeIndex == 2;

    public string MinText
    {
        get => GetMin().HasValue ? GetMin()!.Value.ToString("G8", CultureInfo.InvariantCulture) : "";
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                SetMin(null);
            else if (TryParseNumber(value, out double v))
                SetMin(v);
            // Unparseable input is silently ignored — the OnPropertyChanged
            // below will refresh the textbox to whatever the value
            // actually is, so the user sees their input was rejected.
            OnPropertyChanged();
        }
    }

    public string MaxText
    {
        get => GetMax().HasValue ? GetMax()!.Value.ToString("G8", CultureInfo.InvariantCulture) : "";
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                SetMax(null);
            else if (TryParseNumber(value, out double v))
                SetMax(v);
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Permissive number parser for the Min/Max text fields. Accepts
    /// scientific notation ("1e-6", "1.5E-12"), plain decimals ("0.001"),
    /// integer ("100"), and tolerates a Unicode minus sign (U+2212) which
    /// some fonts/locales paste in instead of an ASCII hyphen-minus.
    /// Always uses InvariantCulture so the decimal separator is ".".
    /// </summary>
    private static bool TryParseNumber(string text, out double value)
    {
        // Normalize: strip surrounding whitespace, replace Unicode
        // minus sign (U+2212) with ASCII hyphen-minus.
        string s = text.Trim().Replace('−', '-');
        return double.TryParse(s,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out value);
    }

    // ── Accessors ──

    private double? GetMin() => _param switch
    {
        "Curvature" => _surface.CurvatureMin,
        "Thickness" => _surface.ThicknessMin,
        "Conic" => _surface.ConicMin,
        "Aspheric" => _surface.AsphericMin[_asphericIndex],
        _ => null
    };

    private double? GetMax() => _param switch
    {
        "Curvature" => _surface.CurvatureMax,
        "Thickness" => _surface.ThicknessMax,
        "Conic" => _surface.ConicMax,
        "Aspheric" => _surface.AsphericMax[_asphericIndex],
        _ => null
    };

    private void SetMin(double? v)
    {
        switch (_param)
        {
            case "Curvature": _surface.CurvatureMin = v; break;
            case "Thickness": _surface.ThicknessMin = v; break;
            case "Conic": _surface.ConicMin = v; break;
            case "Aspheric": _surface.AsphericMin[_asphericIndex] = v; break;
        }
    }

    private void SetMax(double? v)
    {
        switch (_param)
        {
            case "Curvature": _surface.CurvatureMax = v; break;
            case "Thickness": _surface.ThicknessMax = v; break;
            case "Conic": _surface.ConicMax = v; break;
            case "Aspheric": _surface.AsphericMax[_asphericIndex] = v; break;
        }
    }
}

public partial class VariableEditorViewModel : ObservableObject
{
    private readonly GuiSession _session;

    public ObservableCollection<VariableRowViewModel> Variables { get; } = new();
    public bool HasVariables => Variables.Count > 0;
    public string NoVariablesMessage => HasVariables ? "" : "No variables defined. Use Set/Clear Variable buttons in the Lens Editor to define variables.";

    public VariableEditorViewModel(GuiSession session)
    {
        _session = session;
        Refresh();
    }

    public void Refresh()
    {
        Variables.Clear();
        int num = 1;
        foreach (var surf in _session.System.Surfaces)
        {
            if (surf.CurvatureVariable)
                Variables.Add(new VariableRowViewModel(num++, surf, "Curvature"));
            if (surf.ThicknessVariable)
                Variables.Add(new VariableRowViewModel(num++, surf, "Thickness"));
            if (surf.ConicVariable)
                Variables.Add(new VariableRowViewModel(num++, surf, "Conic"));
            for (int j = 0; j < surf.AsphericVariable.Length; j++)
            {
                if (surf.AsphericVariable[j])
                    Variables.Add(new VariableRowViewModel(num++, surf, "Aspheric", j));
            }
        }
        OnPropertyChanged(nameof(HasVariables));
        OnPropertyChanged(nameof(NoVariablesMessage));
    }

    public GuiSession Session => _session;
}
