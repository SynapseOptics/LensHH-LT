using System;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LensHH.App.Session;
using LensHH.Core.Enums;
using LensHH.Core.Models;

namespace LensHH.App.ViewModels;

public partial class FieldRowViewModel : ObservableObject
{
    private readonly Field _field;
    private readonly int _index;
    private readonly Func<FieldType> _getFieldType;
    private readonly Action<string>? _onError;

    public FieldRowViewModel(Field field, int index, bool isYLockedToZero,
        Func<FieldType>? getFieldType = null, Action<string>? onError = null)
    {
        _field = field; _index = index;
        _getFieldType = getFieldType ?? (() => FieldType.ObjectAngle);
        _onError = onError;
        IsYLockedToZero = isYLockedToZero;
        // When Y is locked to zero (single-field design), force the
        // stored value to 0 so display and storage agree.
        if (IsYLockedToZero) _field.Y = 0;
    }

    public int Number => _index + 1;

    /// <summary>
    /// When true, Y is rendered read-only and clamped to 0. Set when
    /// the system has exactly one field — a single off-axis field has
    /// no on-axis baseline for the merit function, which is almost
    /// always a configuration mistake.
    /// </summary>
    [ObservableProperty] private bool _isYLockedToZero;

    public string YText
    {
        get => _field.Y.ToString("F4", CultureInfo.InvariantCulture);
        set
        {
            if (IsYLockedToZero) return; // read-only path
            if (!double.TryParse(value, NumberStyles.Float | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out double v))
                return;
            // Reject NaN/Inf field values; surface the error to the
            // editor's status bar (if wired) and leave the stored value
            // untouched so the previous valid value remains.
            if (!FieldValidation.IsValid(v, _getFieldType(), out string? error))
            {
                _onError?.Invoke(error!);
                OnPropertyChanged(); // refresh display back to stored value
                return;
            }
            _field.Y = v;
            OnPropertyChanged();
        }
    }

    public string WeightText
    {
        get => _field.Weight.ToString("F2", CultureInfo.InvariantCulture);
        set { if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out double v)) { _field.Weight = v; OnPropertyChanged(); } }
    }
}

public partial class FieldEditorViewModel : ObservableObject
{
    private readonly GuiSession _session;
    public ObservableCollection<FieldRowViewModel> Fields { get; } = new();

    [ObservableProperty] private FieldRowViewModel? _selectedField;

    /// <summary>
    /// Most recent validation error from a row edit. Cleared on successful
    /// edits and on Refresh. Bind to a status TextBlock in the dialog if
    /// you want to surface invalid-input messages.
    /// </summary>
    [ObservableProperty] private string _statusText = string.Empty;

    public string FieldUnit => _session.System.FieldType == Core.Enums.FieldType.ObjectAngle ? "(deg)" : "(mm)";

    public FieldEditorViewModel(GuiSession session)
    {
        _session = session;
        Refresh();
    }

    public void Refresh()
    {
        Fields.Clear();
        StatusText = string.Empty;
        bool single = _session.System.Fields.Count == 1;
        for (int i = 0; i < _session.System.Fields.Count; i++)
            Fields.Add(new FieldRowViewModel(
                _session.System.Fields[i], i, isYLockedToZero: single,
                getFieldType: () => _session.System.FieldType,
                onError: msg => StatusText = msg));
    }

    [RelayCommand]
    public void Add()
    {
        _session.System.Fields.Add(new Field(0, 1.0));
        Refresh();
    }

    [RelayCommand]
    public void Insert()
    {
        int idx = SelectedField != null ? Fields.IndexOf(SelectedField) : 0;
        if (idx < 0) idx = 0;
        _session.System.Fields.Insert(idx, new Field(0, 1.0));
        Refresh();
    }

    [RelayCommand]
    public void Remove()
    {
        if (SelectedField == null || _session.System.Fields.Count <= 1) return;
        int idx = Fields.IndexOf(SelectedField);
        if (idx >= 0 && idx < _session.System.Fields.Count)
        {
            _session.System.Fields.RemoveAt(idx);
            Refresh();
        }
    }

    public void Apply() => _session.NotifySystemChanged("field");
}
