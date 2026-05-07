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
    private readonly Func<bool> _getVar;
    private readonly Action<bool> _setVar;

    public ParameterStateViewModel(string label, Surface surface, GuiSession session,
        PickupParameter pickupParam, Func<bool> getVar, Action<bool> setVar)
    {
        Label = label;
        _surface = surface;
        _session = session;
        _pickupParam = pickupParam;
        _getVar = getVar;
        _setVar = setVar;

        // Find existing pickup
        var pickup = session.System.Pickups.FirstOrDefault(
            p => p.TargetSurfaceIndex == surface.Index && p.Parameter == pickupParam);
        if (pickup != null)
        {
            _isPickup = true;
            _pickupSourceSurface = pickup.SourceSurfaceIndex;
            _pickupScale = pickup.ScaleFactor;
            _pickupOffset = pickup.Offset;
        }
    }

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

    public bool IsVariable
    {
        get => _getVar() && !IsPickup;
        set
        {
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

    // Preceding surfaces for pickup source
    public int[] AvailablePickupSurfaces =>
        Enumerable.Range(1, Math.Max(0, _surface.Index - 1)).ToArray();

    [ObservableProperty] private int _pickupSourceSurface = 1;
    [ObservableProperty] private double _pickupScale = 1.0;
    [ObservableProperty] private double _pickupOffset = 0.0;

    private void EnsurePickup()
    {
        var existing = _session.System.Pickups.FirstOrDefault(
            p => p.TargetSurfaceIndex == _surface.Index && p.Parameter == _pickupParam);
        if (existing == null)
        {
            _session.System.Pickups.Add(new Pickup
            {
                TargetSurfaceIndex = _surface.Index,
                Parameter = _pickupParam,
                SourceSurfaceIndex = PickupSourceSurface > 0 ? PickupSourceSurface : 1,
                ScaleFactor = PickupScale,
                Offset = PickupOffset
            });
        }
    }

    private void RemovePickup()
    {
        _session.System.Pickups.RemoveAll(
            p => p.TargetSurfaceIndex == _surface.Index && p.Parameter == _pickupParam);
    }

    public void SavePickup()
    {
        if (!IsPickup) { RemovePickup(); return; }
        RemovePickup();
        _session.System.Pickups.Add(new Pickup
        {
            TargetSurfaceIndex = _surface.Index,
            Parameter = _pickupParam,
            SourceSurfaceIndex = PickupSourceSurface,
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

    // Variable/Pickup tab
    public ParameterStateViewModel CurvatureState { get; }
    public ParameterStateViewModel ThicknessState { get; }
    public ParameterStateViewModel ConicState { get; }

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

    }

    public void Apply()
    {
        CurvatureState.SavePickup();
        ThicknessState.SavePickup();
        ConicState.SavePickup();
        _session.NotifySystemChanged("properties");
    }
}
