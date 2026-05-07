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
        set { _session.System.Aperture = new Core.Models.Aperture(value, ApertureValue); OnPropertyChanged(); }
    }

    public double ApertureValue
    {
        get => _session.System.Aperture.Value;
        set { _session.System.Aperture = new Core.Models.Aperture(ApertureType, value); OnPropertyChanged(); }
    }

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
            ValidationMessage = "";
            OnPropertyChanged();
        }
    }

    public bool IsAfocal
    {
        get => _session.System.IsAfocal;
        set { _session.System.IsAfocal = value; OnPropertyChanged(); }
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
            OnPropertyChanged();
            OnPropertyChanged(nameof(RobustRayAiming));
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

    public ApertureType[] ApertureTypes => new[] { ApertureType.EPD, ApertureType.FNumber };
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
        else
            ValidationMessage = "";
    }

    public void Apply() => _session.NotifySystemChanged("system");
}
