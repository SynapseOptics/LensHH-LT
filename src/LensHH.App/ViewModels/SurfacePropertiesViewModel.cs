using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using LensHH.App.Session;
using LensHH.Core.Enums;
using LensHH.Core.Models;

namespace LensHH.App.ViewModels;

/// <summary>
/// Fixed/Variable/Pickup state for a single optical parameter.
/// Supports pickup with source surface, scale factor, and offset.
/// </summary>
public partial class ParameterStateViewModel : ObservableObject
{
    public string Label { get; }
    private readonly Surface _surface;
    private readonly GuiSession _session;
    private readonly PickupParameter _pickupParam;
    // 0-based generic-parameter slot; only meaningful for a SurfaceParameter pickup
    // (PRO Coordinate Break), where one surface carries several distinct params under
    // the same PickupParameter. -1 for every ordinary single-value parameter.
    private readonly int _paramIndex;
    private readonly Func<bool> _getVar;
    private readonly Action<bool> _setVar;
    private readonly bool _canVary;

    public ParameterStateViewModel(string label, Surface surface, GuiSession session,
        PickupParameter pickupParam, Func<bool> getVar, Action<bool> setVar,
        bool canVary = true, int paramIndex = -1)
    {
        Label = label;
        _surface = surface;
        _session = session;
        _pickupParam = pickupParam;
        _paramIndex = paramIndex;
        _getVar = getVar;
        _setVar = setVar;
        _canVary = canVary;

        // Find existing pickup
        var pickup = session.System.Pickups.FirstOrDefault(Matches);
        if (pickup != null)
        {
            _isPickup = true;
            _pickupSourceSurface = pickup.SourceSurfaceIndex;
            _pickupScale = pickup.ScaleFactor;
            _pickupOffset = pickup.Offset;
        }
    }

    /// <summary>Matches a pickup to THIS parameter: same target surface and parameter,
    /// and — for a slot-indexed SurfaceParameter pickup — the same slot.</summary>
    private bool Matches(Pickup p) =>
        p.TargetSurfaceIndex == _surface.Index
        && p.Parameter == _pickupParam
        && (_pickupParam != PickupParameter.SurfaceParameter || p.ParameterIndex == _paramIndex);

    [ObservableProperty] private bool _isPickup;

    public bool IsFixed
    {
        get => !_getVar() && !IsPickup;
        set
        {
            if (value) { _setVar(false); IsPickup = false; RemovePickup(); }
            OnPropertyChanged(); OnPropertyChanged(nameof(IsVariable)); OnPropertyChanged(nameof(IsPickup));
        }
    }

    /// <summary>
    /// False disables the Variable option — used for the semi-diameter / CA %
    /// on the STOP surface, whose aperture IS the system aperture and therefore
    /// can't be an optimization variable.
    /// </summary>
    public bool CanVary => _canVary;

    public bool IsVariable
    {
        get => _getVar() && !IsPickup;
        set
        {
            // Guard: the stop's aperture can't be a variable (matches the
            // optimizer's collection gate). Snap the radio back to Fixed.
            if (value && !_canVary) { OnPropertyChanged(); return; }
            if (value) { _setVar(true); IsPickup = false; RemovePickup(); }
            else _setVar(false);
            OnPropertyChanged(); OnPropertyChanged(nameof(IsFixed)); OnPropertyChanged(nameof(IsPickup));
        }
    }

    public bool IsPickupSelected
    {
        get => IsPickup;
        set
        {
            IsPickup = value;
            if (value) { _setVar(false); EnsurePickup(); }
            else RemovePickup();
            OnPropertyChanged(nameof(IsFixed)); OnPropertyChanged(nameof(IsVariable));
            OnPropertyChanged(nameof(IsPickup)); OnPropertyChanged(nameof(IsPickupSelected));
        }
    }

    private bool IsModelParam =>
        _pickupParam == PickupParameter.ModelNd
        || _pickupParam == PickupParameter.ModelVd
        || _pickupParam == PickupParameter.ModelDPgF;

    // Preceding surfaces for pickup source. Model-index pickups (Nd/Vd/dPgF) may ONLY source from
    // a preceding surface that is itself a model-glass surface — a catalog/air source has no model
    // coefficients, so picking it up would fill the target with junk (0). Filter those out so the
    // user cannot select an invalid source.
    public int[] AvailablePickupSurfaces
    {
        get
        {
            var pre = Enumerable.Range(1, Math.Max(0, _surface.Index - 1));
            if (IsModelParam)
            {
                var surfs = _session.System.Surfaces;
                pre = pre.Where(i => i < surfs.Count && surfs[i].ModelIndexEnabled);
            }
            return pre.ToArray();
        }
    }

    // Resolve a valid source-surface index, snapping the selection into the allowed set (a model
    // pickup requires a model-glass source). Returns 0 when no valid source exists.
    private int ResolvePickupSource()
    {
        int src = PickupSourceSurface > 0 ? PickupSourceSurface : 1;
        if (IsModelParam)
        {
            var avail = AvailablePickupSurfaces;
            if (avail.Length == 0) return 0;
            if (System.Array.IndexOf(avail, src) < 0) { src = avail[0]; PickupSourceSurface = src; }
        }
        return src;
    }

    [ObservableProperty] private int _pickupSourceSurface = 1;
    [ObservableProperty] private double _pickupScale = 1.0;
    [ObservableProperty] private double _pickupOffset = 0.0;

    private void EnsurePickup()
    {
        var existing = _session.System.Pickups.FirstOrDefault(Matches);
        if (existing == null)
        {
            int src = ResolvePickupSource();
            if (src <= 0) { IsPickup = false; return; }   // model pickup with no valid model source
            _session.System.Pickups.Add(new Pickup
            {
                TargetSurfaceIndex = _surface.Index,
                Parameter = _pickupParam,
                ParameterIndex = _paramIndex < 0 ? 0 : _paramIndex,
                SourceSurfaceIndex = src,
                ScaleFactor = PickupScale,
                Offset = PickupOffset
            });
        }
    }

    private void RemovePickup()
    {
        _session.System.Pickups.RemoveAll(Matches);
    }

    public void SavePickup()
    {
        if (!IsPickup) { RemovePickup(); return; }
        RemovePickup();
        int src = ResolvePickupSource();
        if (src <= 0) { IsPickup = false; return; }   // model pickup with no valid model source — revert to Fixed
        _session.System.Pickups.Add(new Pickup
        {
            TargetSurfaceIndex = _surface.Index,
            Parameter = _pickupParam,
            ParameterIndex = _paramIndex < 0 ? 0 : _paramIndex,
            SourceSurfaceIndex = src,
            ScaleFactor = PickupScale,
            Offset = PickupOffset
        });
    }
}

/// <summary>
/// ViewModel for the Surface Properties modal dialog (tabbed).
/// </summary>
public partial class SurfacePropertiesViewModel : ObservableObject
{
    private readonly Surface _surface;
    private readonly GuiSession _session;

    public int SurfaceIndex => _surface.Index;
    public string Title => $"Surface {_surface.Index} Properties";
    public bool IsEvenAsphere => _surface.Type == SurfaceType.EvenAsphere;
    /// <summary>True for a Paraxial (ideal thin lens) surface — drives the
    /// Focal Length tab's visibility (its only shape parameter).</summary>
    public bool IsParaxial => _surface.Type == SurfaceType.Paraxial;
    /// <summary>True for a Coordinate Break (PRO) surface — drives the Coordinate Break
    /// tab's visibility. Its shape is five generic params (decenter X/Y, tilt X/Y/Z) + Order.</summary>
    public bool IsCoordinateBreak => _surface.Type == SurfaceType.CoordinateBreak;
    /// <summary>True for an ABCD ray-transfer-matrix surface (BASE — both editions) —
    /// drives the ABCD tab's visibility. Its shape is the 4 matrix params A/B/C/D.</summary>
    public bool IsAbcd => _surface.Type == SurfaceType.Abcd;

    // Variable/Pickup tab
    public ParameterStateViewModel CurvatureState { get; }
    public ParameterStateViewModel ThicknessState { get; }
    public ParameterStateViewModel ConicState { get; }

    // Aperture solve: which quantity is the free parameter depends on the aperture mode.
    // Auto -> Clear Aperture %; Fixed -> Semi-Diameter. Only the relevant group is shown.
    public ParameterStateViewModel SemiDiameterState { get; }
    public ParameterStateViewModel ClearApertureState { get; }
    public bool IsAutoAperture  => _surface.SemiDiameterMode == SemiDiameterMode.Auto;
    public bool IsFixedAperture => _surface.SemiDiameterMode == SemiDiameterMode.Fixed;

    // Glass Model tab — when enabled the refractive index is computed from the
    // three model-glass parameters (Nd/Vd/dPgF) instead of a catalog material.
    // Each parameter can be Fixed / Variable / Pickup like curvature or thickness.
    public ParameterStateViewModel ModelNdState { get; }
    public ParameterStateViewModel ModelVdState { get; }
    public ParameterStateViewModel ModelDPgFState { get; }

    // Paraxial (ideal thin lens) focal length — the surface's single shape
    // parameter, Fixed / Variable / Pickup like any other.
    public ParameterStateViewModel FocalLengthState { get; }

    // Coordinate Break (PRO): five generic params, each Fixed / Variable / Pickup.
    // Slots 0..4 = Decenter X, Decenter Y, Tilt X, Tilt Y, Tilt Z (0-based storage;
    // "Parameter 1..5" to the user). Order lives in Settings[0].
    public ParameterStateViewModel DecenterXState { get; }
    public ParameterStateViewModel DecenterYState { get; }
    public ParameterStateViewModel TiltXState { get; }
    public ParameterStateViewModel TiltYState { get; }
    public ParameterStateViewModel TiltZState { get; }

    // ABCD (base): four matrix params, each Fixed / Variable / Pickup.
    // Slots 0..3 = A, B, C, D ("Parameter 1..4" to the user).
    public ParameterStateViewModel AState { get; }
    public ParameterStateViewModel BState { get; }
    public ParameterStateViewModel CState { get; }
    public ParameterStateViewModel DState { get; }

    /// <summary>
    /// Enable/disable model-glass mode. Toggling is where Glass ⇄ Model
    /// conversion happens (per the design): enabling loads Nd/Vd/dPgF from the
    /// currently-specified catalog glass; disabling finds the closest catalog
    /// glass to the (possibly optimizer-modified) model parameters and writes
    /// it back to the Material column.
    /// </summary>
    public bool ModelIndexEnabled
    {
        get => _surface.ModelIndexEnabled;
        set
        {
            if (_surface.ModelIndexEnabled == value) return;

            // Preferred catalogs drive both the extract and the closest-match
            // search; an empty list means "search all loaded catalogs".
            var preferred = _session.System.GlassCatalogs != null && _session.System.GlassCatalogs.Count > 0
                ? _session.System.GlassCatalogs
                : null;

            if (value)
            {
                // Glass -> Model: seed the three parameters from the current glass.
                var p = _session.GlassCatalog.ExtractModelParams(_surface.Material, preferred);
                if (p.HasValue)
                {
                    _surface.ModelNd = p.Value.Nd;
                    _surface.ModelVd = p.Value.Vd;
                    _surface.ModelDPgF = p.Value.DPgF;
                }
                _surface.ModelIndexEnabled = true;
            }
            else
            {
                // Model -> Glass: snap to the closest real catalog glass.
                // FindClosestGlass returns a "CATALOG:GLASS" key; Material stores
                // the bare glass name, so strip the catalog prefix.
                var g = _session.GlassCatalog.FindClosestGlass(
                    _surface.ModelNd, _surface.ModelVd, _surface.ModelDPgF, preferred);
                if (!string.IsNullOrEmpty(g))
                {
                    int ci = g.IndexOf(':');
                    _surface.Material = ci >= 0 ? g.Substring(ci + 1) : g;
                }
                _surface.ModelIndexEnabled = false;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(ModelNdValue));
            OnPropertyChanged(nameof(ModelVdValue));
            OnPropertyChanged(nameof(ModelDPgFValue));
            OnPropertyChanged(nameof(GlassDisplay));
        }
    }

    public double ModelNdValue
    {
        get => _surface.ModelNd;
        set { _surface.ModelNd = value; OnPropertyChanged(); }
    }

    public double ModelVdValue
    {
        get => _surface.ModelVd;
        set { _surface.ModelVd = value; OnPropertyChanged(); }
    }

    public double ModelDPgFValue
    {
        get => _surface.ModelDPgF;
        set { _surface.ModelDPgF = value; OnPropertyChanged(); }
    }

    /// <summary>Paraxial focal length f (mm). PositiveInfinity = zero power.</summary>
    public double FocalLengthValue
    {
        get => _surface.FocalLength;
        set { _surface.FocalLength = value; OnPropertyChanged(); OnPropertyChanged(nameof(FocalPowerText)); }
    }

    // ── Coordinate Break parameters (mm / degrees). Stored 0-based in Surface.Parameters;
    //    exposed here as named values. Order is Settings[0] (0 = decenter then tilt,
    //    the ZEMAX default; 1 = tilt then decenter). ─────────────────────────────────────
    public double DecenterXValue { get => _surface.Parameters[0]; set { _surface.Parameters[0] = value; OnPropertyChanged(); } }
    public double DecenterYValue { get => _surface.Parameters[1]; set { _surface.Parameters[1] = value; OnPropertyChanged(); } }
    public double TiltXValue     { get => _surface.Parameters[2]; set { _surface.Parameters[2] = value; OnPropertyChanged(); } }
    public double TiltYValue     { get => _surface.Parameters[3]; set { _surface.Parameters[3] = value; OnPropertyChanged(); } }
    public double TiltZValue     { get => _surface.Parameters[4]; set { _surface.Parameters[4] = value; OnPropertyChanged(); } }

    /// <summary>Coordinate Break Order: 0 = decenter then tilt (ZEMAX default), 1 = tilt
    /// then decenter. Bound to a two-item ComboBox (SelectedIndex).</summary>
    public int OrderIndex
    {
        get => _surface.Settings[0] == 1 ? 1 : 0;
        set { _surface.Settings[0] = value == 1 ? 1 : 0; OnPropertyChanged(); }
    }

    // ── ABCD ray-transfer-matrix params (dimensionless). Stored 0-based in
    //    Surface.Parameters[0..3] = A,B,C,D; the matrix acts on ray height + slope. ────
    public double AValue { get => _surface.Parameters[0]; set { _surface.Parameters[0] = value; OnPropertyChanged(); } }
    public double BValue { get => _surface.Parameters[1]; set { _surface.Parameters[1] = value; OnPropertyChanged(); } }
    public double CValue { get => _surface.Parameters[2]; set { _surface.Parameters[2] = value; OnPropertyChanged(); } }
    public double DValue { get => _surface.Parameters[3]; set { _surface.Parameters[3] = value; OnPropertyChanged(); } }

    /// <summary>Read-only optical power in diopters — the quantity the optimizer
    /// varies (set its min/max in the Variable Editor). 0 D = afocal.</summary>
    public string FocalPowerText => double.IsInfinity(_surface.FocalLength)
        ? "0 D (afocal)"
        : _surface.FocalPower.ToString("G6", CultureInfo.InvariantCulture) + " D";

    /// <summary>What the read-only "Glass" reference field shows on the tab:
    /// "Model" while model-index is enabled, otherwise the catalog material.</summary>
    public string GlassDisplay =>
        _surface.ModelIndexEnabled ? "Model" : (_surface.Material ?? string.Empty);

    public double SemiDiameterValue
    {
        get => _surface.SemiDiameter;
        set { _surface.SemiDiameter = value; OnPropertyChanged(); }
    }

    public double ClearAperturePercentValue
    {
        get => _surface.ClearAperturePercent;
        set { _surface.ClearAperturePercent = value; OnPropertyChanged(); }
    }

    public double Curvature
    {
        get => _surface.Curvature;
        set { _surface.Curvature = value; OnPropertyChanged(); }
    }

    public double Thickness
    {
        get => _surface.Thickness;
        set { _surface.Thickness = value; OnPropertyChanged(); }
    }

    public double Conic
    {
        get => _surface.Conic;
        set { _surface.Conic = value; OnPropertyChanged(); }
    }

    // Aperture tab
    public double InnerRadius
    {
        get => _surface.InnerRadius;
        set { _surface.InnerRadius = value; OnPropertyChanged(); }
    }

    public double ObscurationRadius
    {
        get => _surface.ObscurationRadius;
        set { _surface.ObscurationRadius = value; OnPropertyChanged(); }
    }

    // Even Asphere tab — bound as strings with explicit invariant-culture
    // parsing so values like "1e-6" / "1.5E-12" are always accepted
    // regardless of the user's regional decimal separator. The previous
    // direct double bindings went through Avalonia's culture-aware
    // converter, which rejected scientific notation on some locales —
    // very cumbersome for aspheric coefficients that span 1e-6 to 1e-15.
    private string FormatCoeff(double v) =>
        v.ToString("G8", CultureInfo.InvariantCulture);

    private void SetCoeff(int index, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _surface.AsphericCoefficients[index] = 0;
            return;
        }
        if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture, out double v))
        {
            _surface.AsphericCoefficients[index] = v;
        }
        // Silently ignore unparseable input (TextBox keeps its prior value
        // until the user types something valid).
    }

    public string A2  { get => FormatCoeff(_surface.AsphericCoefficients[0]); set { SetCoeff(0, value); OnPropertyChanged(); } }
    public string A4  { get => FormatCoeff(_surface.AsphericCoefficients[1]); set { SetCoeff(1, value); OnPropertyChanged(); } }
    public string A6  { get => FormatCoeff(_surface.AsphericCoefficients[2]); set { SetCoeff(2, value); OnPropertyChanged(); } }
    public string A8  { get => FormatCoeff(_surface.AsphericCoefficients[3]); set { SetCoeff(3, value); OnPropertyChanged(); } }
    public string A10 { get => FormatCoeff(_surface.AsphericCoefficients[4]); set { SetCoeff(4, value); OnPropertyChanged(); } }
    public string A12 { get => FormatCoeff(_surface.AsphericCoefficients[5]); set { SetCoeff(5, value); OnPropertyChanged(); } }
    public string A14 { get => FormatCoeff(_surface.AsphericCoefficients[6]); set { SetCoeff(6, value); OnPropertyChanged(); } }
    public string A16 { get => FormatCoeff(_surface.AsphericCoefficients[7]); set { SetCoeff(7, value); OnPropertyChanged(); } }

    public bool A2Var { get => _surface.AsphericVariable[0]; set { _surface.AsphericVariable[0] = value; OnPropertyChanged(); } }
    public bool A4Var { get => _surface.AsphericVariable[1]; set { _surface.AsphericVariable[1] = value; OnPropertyChanged(); } }
    public bool A6Var { get => _surface.AsphericVariable[2]; set { _surface.AsphericVariable[2] = value; OnPropertyChanged(); } }
    public bool A8Var { get => _surface.AsphericVariable[3]; set { _surface.AsphericVariable[3] = value; OnPropertyChanged(); } }
    public bool A10Var { get => _surface.AsphericVariable[4]; set { _surface.AsphericVariable[4] = value; OnPropertyChanged(); } }
    public bool A12Var { get => _surface.AsphericVariable[5]; set { _surface.AsphericVariable[5] = value; OnPropertyChanged(); } }
    public bool A14Var { get => _surface.AsphericVariable[6]; set { _surface.AsphericVariable[6] = value; OnPropertyChanged(); } }
    public bool A16Var { get => _surface.AsphericVariable[7]; set { _surface.AsphericVariable[7] = value; OnPropertyChanged(); } }

    public SurfacePropertiesViewModel(Surface surface, GuiSession session)
    {
        _surface = surface;
        _session = session;

        CurvatureState = new ParameterStateViewModel("Curvature", surface, session,
            PickupParameter.Radius, () => surface.CurvatureVariable, v => surface.CurvatureVariable = v);
        ThicknessState = new ParameterStateViewModel("Thickness", surface, session,
            PickupParameter.Thickness, () => surface.ThicknessVariable, v => surface.ThicknessVariable = v);
        ConicState = new ParameterStateViewModel("Conic", surface, session,
            PickupParameter.Conic, () => surface.ConicVariable, v => surface.ConicVariable = v);

        // The stop surface's aperture IS the system aperture (EPD/F#/NA), so its
        // semi-diameter and CA % can never be optimization variables — disable that option.
        SemiDiameterState = new ParameterStateViewModel("Semi-Diameter", surface, session,
            PickupParameter.SemiDiameter, () => surface.SemiDiameterVariable, v => surface.SemiDiameterVariable = v,
            canVary: !surface.IsStop);
        ClearApertureState = new ParameterStateViewModel("Clear Aperture %", surface, session,
            PickupParameter.ClearAperturePercent, () => surface.ClearAperturePercentVariable, v => surface.ClearAperturePercentVariable = v,
            canVary: !surface.IsStop);

        ModelNdState = new ParameterStateViewModel("Model Nd", surface, session,
            PickupParameter.ModelNd, () => surface.ModelNdVariable, v => surface.ModelNdVariable = v);
        ModelVdState = new ParameterStateViewModel("Model Vd", surface, session,
            PickupParameter.ModelVd, () => surface.ModelVdVariable, v => surface.ModelVdVariable = v);
        ModelDPgFState = new ParameterStateViewModel("Model dPgF", surface, session,
            PickupParameter.ModelDPgF, () => surface.ModelDPgFVariable, v => surface.ModelDPgFVariable = v);

        FocalLengthState = new ParameterStateViewModel("Focal Length", surface, session,
            PickupParameter.FocalLength, () => surface.FocalLengthVariable, v => surface.FocalLengthVariable = v);

        // Coordinate Break params: each slot 0..4 is Fixed / Variable / Pickup, keyed by
        // the generic SurfaceParameter pickup + its slot. Variable flag lives in
        // Surface.ParameterVariable[slot]; bounds (for the Variable Editor) in ParameterMin/Max[slot].
        DecenterXState = new ParameterStateViewModel("Decenter X", surface, session,
            PickupParameter.SurfaceParameter, () => surface.ParameterVariable[0], v => surface.ParameterVariable[0] = v, paramIndex: 0);
        DecenterYState = new ParameterStateViewModel("Decenter Y", surface, session,
            PickupParameter.SurfaceParameter, () => surface.ParameterVariable[1], v => surface.ParameterVariable[1] = v, paramIndex: 1);
        TiltXState = new ParameterStateViewModel("Tilt X", surface, session,
            PickupParameter.SurfaceParameter, () => surface.ParameterVariable[2], v => surface.ParameterVariable[2] = v, paramIndex: 2);
        TiltYState = new ParameterStateViewModel("Tilt Y", surface, session,
            PickupParameter.SurfaceParameter, () => surface.ParameterVariable[3], v => surface.ParameterVariable[3] = v, paramIndex: 3);
        TiltZState = new ParameterStateViewModel("Tilt Z", surface, session,
            PickupParameter.SurfaceParameter, () => surface.ParameterVariable[4], v => surface.ParameterVariable[4] = v, paramIndex: 4);

        // ABCD params: slots 0..3 = A,B,C,D, same generic SurfaceParameter var/pickup path.
        AState = new ParameterStateViewModel("A", surface, session,
            PickupParameter.SurfaceParameter, () => surface.ParameterVariable[0], v => surface.ParameterVariable[0] = v, paramIndex: 0);
        BState = new ParameterStateViewModel("B", surface, session,
            PickupParameter.SurfaceParameter, () => surface.ParameterVariable[1], v => surface.ParameterVariable[1] = v, paramIndex: 1);
        CState = new ParameterStateViewModel("C", surface, session,
            PickupParameter.SurfaceParameter, () => surface.ParameterVariable[2], v => surface.ParameterVariable[2] = v, paramIndex: 2);
        DState = new ParameterStateViewModel("D", surface, session,
            PickupParameter.SurfaceParameter, () => surface.ParameterVariable[3], v => surface.ParameterVariable[3] = v, paramIndex: 3);
    }

    public void Apply()
    {
        CurvatureState.SavePickup();
        ThicknessState.SavePickup();
        ConicState.SavePickup();
        SemiDiameterState.SavePickup();
        ClearApertureState.SavePickup();
        ModelNdState.SavePickup();
        ModelVdState.SavePickup();
        ModelDPgFState.SavePickup();
        FocalLengthState.SavePickup();
        // Coordinate Break and ABCD both key their generic SurfaceParameter pickups on
        // Parameters slots (CB 0..4, ABCD 0..3), so save only the set that matches this
        // surface's type — otherwise the OTHER type's states (IsPickup=false) would remove
        // this type's slot pickups. A surface is exactly one of these types.
        if (IsCoordinateBreak)
        {
            DecenterXState.SavePickup();
            DecenterYState.SavePickup();
            TiltXState.SavePickup();
            TiltYState.SavePickup();
            TiltZState.SavePickup();
        }
        else if (IsAbcd)
        {
            AState.SavePickup();
            BState.SavePickup();
            CState.SavePickup();
            DState.SavePickup();
        }
        _session.NotifySystemChanged("properties");
    }
}
