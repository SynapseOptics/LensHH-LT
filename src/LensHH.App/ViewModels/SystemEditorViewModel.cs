using System;
using CommunityToolkit.Mvvm.ComponentModel;
using LensHH.App.Session;
using LensHH.Core.Enums;

namespace LensHH.App.ViewModels;

public partial class SystemEditorViewModel : ObservableObject
{
    private readonly GuiSession _session;

    [ObservableProperty] private string _validationMessage = "";

    public SystemEditorViewModel(GuiSession session)
    {
        _session = session;
        Validate();
    }

    public string Title
    {
        get => _session.System.Title;
        set { _session.System.Title = value ?? string.Empty; OnPropertyChanged(); }
    }

    public ApertureType ApertureType
    {
        get => _session.System.Aperture.Type;
        set
        {
            _session.System.Aperture = new Core.Models.Aperture(value, ApertureValue);
            // Telecentric object space is only meaningful with Object-Space NA — clear it otherwise.
            if (value != ApertureType.ObjectSpaceNA && _session.System.TelecentricObjectSpace)
                _session.System.TelecentricObjectSpace = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TelecentricObjectSpace));
            OnPropertyChanged(nameof(IsTelecentricEnabled));
            Validate();
        }
    }

    public double ApertureValue
    {
        get => _session.System.Aperture.Value;
        set
        {
            if (IsApertureOwned) return;   // MCE-owned: value varies per configuration, reject edits
            _session.System.Aperture = new Core.Models.Aperture(ApertureType, value);
            OnPropertyChanged();
        }
    }

    /// <summary>True when the aperture value is varied across configurations by the multi-configuration
    /// editor. The value field is then disabled (its value belongs to the active config). The aperture
    /// TYPE stays shared across configs, so it remains editable.</summary>
    public bool IsApertureOwned =>
        AppExtensions.CellOwnership?.IsSystemCellVaried(SystemConfigCell.ApertureValue, 0) ?? false;

    public FieldType FieldType
    {
        get => _session.System.FieldType;
        set
        {
            if (value == FieldType.ObjectHeight && IsInfiniteConjugate)
            {
                ValidationMessage = "Object Height is not valid for infinite conjugate systems.";
                OnPropertyChanged(); // refresh ComboBox to revert
                return;
            }
            _session.System.FieldType = value;
            OnPropertyChanged();
            Validate();
        }
    }

    public bool IsAfocal
    {
        get => _session.System.IsAfocal;
        set { _session.System.IsAfocal = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Object-space telecentric: entrance pupil at infinity, chief ray parallel to axis in object
    /// space. Only available (checkbox enabled) when the aperture is Object-Space NA.
    /// </summary>
    public bool TelecentricObjectSpace
    {
        get => _session.System.TelecentricObjectSpace;
        set
        {
            _session.System.TelecentricObjectSpace = value && IsTelecentricEnabled;
            OnPropertyChanged();
            Validate();
        }
    }

    /// <summary>The Telecentric Object Space checkbox is enabled only for the Object-Space NA aperture
    /// AND with ray aiming Off — telecentric launch sets the chief-ray direction directly and cannot
    /// coexist with real ray aiming.</summary>
    public bool IsTelecentricEnabled =>
        ApertureType == ApertureType.ObjectSpaceNA && _session.System.RayAiming == RayAimingMode.Off;

    public bool PenalizeVignetting
    {
        get => _session.System.PenalizeVignetting;
        set { _session.System.PenalizeVignetting = value; OnPropertyChanged(); }
    }

    public bool UseAutomaticVignettingFactors
    {
        get => _session.System.UseAutomaticVignettingFactors;
        set { _session.System.UseAutomaticVignettingFactors = value; OnPropertyChanged(); }
    }

    public bool RayAiming
    {
        get => _session.System.RayAiming != RayAimingMode.Off;
        set
        {
            if (!value)
                _session.System.RayAiming = RayAimingMode.Off;
            else if (RobustRayAiming)
                _session.System.RayAiming = RayAimingMode.Robust;
            else
                _session.System.RayAiming = RayAimingMode.Real;
            // Telecentric Object Space is only supported with ray aiming Off — clear it when aiming turns on.
            if (_session.System.RayAiming != RayAimingMode.Off && _session.System.TelecentricObjectSpace)
                _session.System.TelecentricObjectSpace = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RobustRayAiming));
            OnPropertyChanged(nameof(TelecentricObjectSpace));
            OnPropertyChanged(nameof(IsTelecentricEnabled));
            Validate();
        }
    }

    public bool RobustRayAiming
    {
        get => _session.System.RayAiming == RayAimingMode.Robust;
        set
        {
            if (value)
                _session.System.RayAiming = RayAimingMode.Robust;
            else if (RayAiming)
                _session.System.RayAiming = RayAimingMode.Real;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RayAiming));
        }
    }

    /// <summary>How bounded optimization variables are mapped between physical and
    /// optimizer space. Sigmoid is the historical default; Reflect keeps a constant
    /// gradient at the bounds. Applies to every optimizer and is saved with the design.</summary>
    public BoundHandlingMode BoundHandling
    {
        get => _session.System.BoundHandling;
        set { _session.System.BoundHandling = value; OnPropertyChanged(); }
    }

    public ApertureType[] ApertureTypes => new[] { ApertureType.EPD, ApertureType.FNumber, ApertureType.ObjectSpaceNA };
    public BoundHandlingMode[] BoundHandlingModes => new[] { BoundHandlingMode.Sigmoid, BoundHandlingMode.Reflect };
    public FieldType[] FieldTypes => new[] { FieldType.ObjectAngle, FieldType.ObjectHeight };

    private bool IsInfiniteConjugate
    {
        get
        {
            if (_session.System.Surfaces.Count == 0) return true;
            double t = _session.System.Surfaces[0].Thickness;
            return double.IsInfinity(t) || double.IsNaN(t);
        }
    }

    private void Validate()
    {
        if (_session.System.FieldType == FieldType.ObjectHeight && IsInfiniteConjugate)
            ValidationMessage = "Object Height is not valid for infinite conjugate systems.";
        else if (ApertureType == ApertureType.ObjectSpaceNA && IsInfiniteConjugate)
            ValidationMessage = "Object Space NA requires a finite-conjugate system (a finite object distance). "
                              + "Analyses and optimization are disabled until the object is at a finite distance.";
        else if (ApertureType == ApertureType.ObjectSpaceNA && _session.System.FieldType != FieldType.ObjectHeight)
            ValidationMessage = "Object Space NA requires Object Height fields.";
        else if (_session.System.TelecentricObjectSpace && _session.System.RayAiming != RayAimingMode.Off)
            ValidationMessage = "Telecentric Object Space requires Ray Aiming to be Off.";
        else
            ValidationMessage = "";
    }

    public void Apply() => _session.NotifySystemChanged("system");
}
