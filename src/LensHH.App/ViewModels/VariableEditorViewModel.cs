using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using LensHH.App.Session;
using LensHH.Core.Enums;
using LensHH.Core.Models;

namespace LensHH.App.ViewModels;

public enum ConstraintType { Unconstrained, MinMax, MaxOnly, MinOnly }

public partial class VariableRowViewModel : ObservableObject
{
    private readonly Func<double?> _getMin;
    private readonly Func<double?> _getMax;
    private readonly Action<double?> _setMin;
    private readonly Action<double?> _setMax;

    // Bound accessors are delegates so a row can represent a base surface variable OR a
    // config-specific variable (whose bounds live in the multi-configuration editor) uniformly.
    public VariableRowViewModel(int number, string description, int surfaceIndex,
        Func<double?> getMin, Func<double?> getMax, Action<double?> setMin, Action<double?> setMax)
    {
        Number = number;
        Description = description;
        SurfaceIndex = surfaceIndex;
        _getMin = getMin; _getMax = getMax; _setMin = setMin; _setMax = setMax;
    }

    public int Number { get; }
    public string Description { get; }
    public int SurfaceIndex { get; }

    /// <summary>Surface column text: the surface index, or "-" for variables not tied to a surface
    /// (e.g. field-value variables, whose 1-based field index is already shown in the description).
    /// A negative <see cref="SurfaceIndex"/> is the sentinel for "not surface-scoped".</summary>
    public string SurfaceDisplay => SurfaceIndex < 0
        ? "-"
        : SurfaceIndex.ToString(CultureInfo.InvariantCulture);

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

    private double? GetMin() => _getMin();
    private double? GetMax() => _getMax();
    private void SetMin(double? v) => _setMin(v);
    private void SetMax(double? v) => _setMax(v);
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
            var s = surf;   // per-iteration capture for the bound delegates
            // Skip base variables that are owned by the multi-configuration editor: their
            // variability + bounds live per configuration (shown as separate config rows below).
            if (s.CurvatureVariable && !Owned(s.Index, SurfaceConfigParam.Curvature))
                Variables.Add(new VariableRowViewModel(num++, "Curvature", s.Index,
                    () => s.CurvatureMin, () => s.CurvatureMax,
                    v => s.CurvatureMin = v, v => s.CurvatureMax = v));
            if (s.ThicknessVariable && !Owned(s.Index, SurfaceConfigParam.Thickness))
                Variables.Add(new VariableRowViewModel(num++, "Thickness", s.Index,
                    () => s.ThicknessMin, () => s.ThicknessMax,
                    v => s.ThicknessMin = v, v => s.ThicknessMax = v));
            if (s.ConicVariable && !Owned(s.Index, SurfaceConfigParam.Conic))
                Variables.Add(new VariableRowViewModel(num++, "Conic", s.Index,
                    () => s.ConicMin, () => s.ConicMax,
                    v => s.ConicMin = v, v => s.ConicMax = v));
            for (int j = 0; j < s.AsphericVariable.Length; j++)
            {
                if (s.AsphericVariable[j])
                {
                    int jj = j;
                    Variables.Add(new VariableRowViewModel(num++, $"Asphere A{(jj + 1) * 2}", s.Index,
                        () => s.AsphericMin[jj], () => s.AsphericMax[jj],
                        v => s.AsphericMin[jj] = v, v => s.AsphericMax[jj] = v));
                }
            }

            // Aperture solve variables (mode-gated, matching the optimizer): Fixed -> Semi-Diameter,
            // Auto -> Clear Aperture %. Bounds are editable here. The stop is excluded — its
            // semi-diameter IS the aperture, so it can't be an optimization variable (matches
            // LocalOptimizer's collection gate).
            if (!s.IsStop && s.SemiDiameterMode == SemiDiameterMode.Fixed && s.SemiDiameterVariable)
                Variables.Add(new VariableRowViewModel(num++, "Semi-Diameter", s.Index,
                    () => s.SemiDiameterMin, () => s.SemiDiameterMax,
                    v => s.SemiDiameterMin = v, v => s.SemiDiameterMax = v));
            if (!s.IsStop && s.SemiDiameterMode == SemiDiameterMode.Auto && s.ClearAperturePercentVariable)
                Variables.Add(new VariableRowViewModel(num++, "Clear Aperture %", s.Index,
                    () => s.ClearAperturePercentMin, () => s.ClearAperturePercentMax,
                    v => s.ClearAperturePercentMin = v, v => s.ClearAperturePercentMax = v));

            // Model-glass parameters (Nd / Vd / dPgF) — only for model-index surfaces.
            if (s.ModelIndexEnabled)
            {
                if (s.ModelNdVariable)
                    Variables.Add(new VariableRowViewModel(num++, "Model Nd", s.Index,
                        () => s.ModelNdMin, () => s.ModelNdMax,
                        v => s.ModelNdMin = v, v => s.ModelNdMax = v));
                if (s.ModelVdVariable)
                    Variables.Add(new VariableRowViewModel(num++, "Model Vd", s.Index,
                        () => s.ModelVdMin, () => s.ModelVdMax,
                        v => s.ModelVdMin = v, v => s.ModelVdMax = v));
                if (s.ModelDPgFVariable)
                    Variables.Add(new VariableRowViewModel(num++, "Model dPgF", s.Index,
                        () => s.ModelDPgFMin, () => s.ModelDPgFMax,
                        v => s.ModelDPgFMin = v, v => s.ModelDPgFMax = v));
            }

            // Paraxial (ideal thin lens): the optimizer varies POWER in diopters
            // (1000/f) — continuous through afocal and sign-symmetric — so the
            // min/max bounds are power. Only meaningful on a Paraxial surface.
            if (s.Type == SurfaceType.Paraxial && s.FocalLengthVariable)
                Variables.Add(new VariableRowViewModel(num++, "Focal Power (D)", s.Index,
                    () => s.FocalPowerMin, () => s.FocalPowerMax,
                    v => s.FocalPowerMin = v, v => s.FocalPowerMax = v));

            // Coordinate Break (PRO): each free decenter/tilt param varies with its own
            // min/max bounds in Surface.ParameterMin/Max[slot] (slots 0..4).
            if (s.Type == SurfaceType.CoordinateBreak)
            {
                string[] cbNames = { "Decenter X", "Decenter Y", "Tilt X", "Tilt Y", "Tilt Z" };
                for (int k = 0; k < cbNames.Length; k++)
                {
                    int slot = k; // capture per iteration
                    if (s.ParameterVariable[slot])
                        Variables.Add(new VariableRowViewModel(num++, "CB " + cbNames[slot], s.Index,
                            () => s.ParameterMin[slot], () => s.ParameterMax[slot],
                            v => s.ParameterMin[slot] = v, v => s.ParameterMax[slot] = v));
                }
            }
        }

        // Config-specific variables (advanced edition): shown here with editable bounds. The Solve
        // dialog only sets the solve type; bounds are owned by this editor. Null seam → nothing added.
        var cfgVars = AppExtensions.ConfigVariables?.GetConfigVariables();
        if (cfgVars != null)
            foreach (var cv in cfgVars)
                Variables.Add(new VariableRowViewModel(num++, cv.Description, cv.SurfaceIndex,
                    cv.GetMin, cv.GetMax, cv.SetMin, cv.SetMax));

        // Field-value variables (advanced edition): Field Y / X marked Variable in the field solve
        // dialog show here with editable bounds. Same seam contract as config variables.
        var fieldVars = AppExtensions.FieldVariables?.GetConfigVariables();
        if (fieldVars != null)
            foreach (var fv in fieldVars)
                Variables.Add(new VariableRowViewModel(num++, fv.Description, fv.SurfaceIndex,
                    fv.GetMin, fv.GetMax, fv.SetMin, fv.SetMax));

        OnPropertyChanged(nameof(HasVariables));
        OnPropertyChanged(nameof(NoVariablesMessage));
    }

    private static bool Owned(int surfaceIndex, SurfaceConfigParam param)
        => AppExtensions.CellOwnership?.IsVariedAcrossConfigs(surfaceIndex, param) ?? false;

    public GuiSession Session => _session;
}
