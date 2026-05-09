using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LensHH.App.Session;
using LensHH.Core.Enums;
using LensHH.Core.Models;

namespace LensHH.App.ViewModels;

/// <summary>
/// Row view model wrapping a Surface for display in the DataGrid.
/// Notifies the session when values change.
/// </summary>
public partial class SurfaceRowViewModel : ObservableObject
{
    private readonly Surface _surface;
    private readonly GuiSession _session;

    public SurfaceRowViewModel(Surface surface, GuiSession session)
    {
        _surface = surface;
        _session = session;
    }

    public int Index => _surface.Index;
    public string SurfaceLabel => _surface.Index == 0 ? "OBJ" :
        (_surface.Index == _session.System.Surfaces.Count - 1 ? "IMG" : _surface.Index.ToString());

    // Type as string-based combo (Avalonia ComboBox works better with string/index)
    public static string[] TypeOptions { get; } = new[] { "Standard", "Even Asphere" };

    public string TypeDisplay => _surface.Type == SurfaceType.EvenAsphere ? "Even Asphere" : "Standard";

    public int TypeIndex
    {
        get => _surface.Type == SurfaceType.EvenAsphere ? 1 : 0;
        set
        {
            var newType = value == 1 ? SurfaceType.EvenAsphere : SurfaceType.Standard;
            if (_surface.Type != newType)
            {
                _surface.Type = newType;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TypeDisplay));
                _session.NotifySystemChanged("surface");
            }
        }
    }

    public bool IsStop
    {
        get => _surface.IsStop;
        set
        {
            // The system always needs exactly one stop, so unchecking the
            // current stop is not allowed — to move the stop, the user
            // checks a different surface, and that path clears the old one.
            // Reject value=false here, but raise PropertyChanged so the
            // bound checkbox snaps back to the underlying true.
            if (!value)
            {
                OnPropertyChanged();
                return;
            }
            if (_surface.IsStop) return; // already the stop, nothing to do

            // [DIAG] Snapshot before stop change.
            SurfaceDiagnostics.Log("IsStop.set BEFORE", _session.System,
                $"target row Index={_surface.Index}");

            // Move the stop: clear it on every other surface, then set it
            // here. Notify SystemChanged afterward — the editor's
            // OnSystemChanged handler will call RefreshDisplayProperties on
            // every row, which now broadcasts IsStop, so the previously-
            // stop row repaints unchecked.
            foreach (var s in _session.System.Surfaces) s.IsStop = false;
            _surface.IsStop = true;

            // [DIAG] After the loop+set; before notification fires.
            SurfaceDiagnostics.Log("IsStop.set AFTER", _session.System,
                $"target row Index={_surface.Index}");

            OnPropertyChanged();
            _session.NotifySystemChanged("surface");
        }
    }

    public double Radius
    {
        get => _surface.Radius;
        set { if (_surface.Radius != value) { _surface.Radius = value; OnPropertyChanged(); OnPropertyChanged(nameof(RadiusDisplay)); _session.NotifySystemChanged("surface"); } }
    }

    public string RadiusDisplay
    {
        get
        {
            string val = double.IsPositiveInfinity(_surface.Radius) ? "Infinity" : _surface.Radius.ToString("G8", CultureInfo.InvariantCulture);
            if (_surface.CurvatureVariable) val += " V";
            else if (HasPickup(_surface.Index, PickupParameter.Radius)) val += " P";
            return val;
        }
    }

    public double Thickness
    {
        get => _surface.Thickness;
        set
        {
            if (_surface.Thickness != value)
            {
                _surface.Thickness = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ThicknessDisplay));
                // OBJ row: SD cell is blanked when thickness is infinite, so
                // toggling between Infinity and a finite value must re-render.
                if (_surface.Index == 0)
                    OnPropertyChanged(nameof(SemiDiameterDisplay));
                _session.NotifySystemChanged("surface");
            }
        }
    }

    public string ThicknessDisplay
    {
        get
        {
            string val = double.IsPositiveInfinity(_surface.Thickness) ? "Infinity" : _surface.Thickness.ToString("G8", CultureInfo.InvariantCulture);
            if (_surface.ThicknessVariable) val += " V";
            else if (HasPickup(_surface.Index, PickupParameter.Thickness)) val += " P";
            return val;
        }
    }

    public double Conic
    {
        get => _surface.Conic;
        set { if (_surface.Conic != value) { _surface.Conic = value; OnPropertyChanged(); OnPropertyChanged(nameof(ConicDisplay)); _session.NotifySystemChanged("surface"); } }
    }

    public string ConicDisplay
    {
        get
        {
            string val = _surface.Conic.ToString("G8", CultureInfo.InvariantCulture);
            if (_surface.ConicVariable) val += " V";
            else if (HasPickup(_surface.Index, PickupParameter.Conic)) val += " P";
            return val;
        }
    }

    public string Material
    {
        get => _surface.Material;
        set { if (_surface.Material != value) { _surface.Material = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(IsGlassUnresolved)); _session.NotifySystemChanged("surface"); } }
    }

    public bool IsGlassUnresolved
    {
        get
        {
            var mat = _surface.Material;
            if (string.IsNullOrEmpty(mat) || mat.Equals("MIRROR", StringComparison.OrdinalIgnoreCase))
                return false;
            return _session.GlassCatalog.GetGlass(mat, _session.System.GlassCatalogs.Count > 0 ? _session.System.GlassCatalogs : null) == null;
        }
    }

    // NB: the Glass column foreground is driven by Classes.unresolved-glass in
    // MainWindow.axaml (toggled by IsGlassUnresolved). Do NOT re-add a Foreground
    // property here — binding Foreground to a nullable brush makes the resolved
    // case render with a null brush (invisible), regardless of theme.

    public double SemiDiameter
    {
        get => _surface.SemiDiameter;
        set { if (_surface.SemiDiameter != value) { _surface.SemiDiameter = value; OnPropertyChanged(); OnPropertyChanged(nameof(SemiDiameterDisplay)); _session.NotifySystemChanged("surface"); } }
    }

    public string SemiDiameterDisplay
    {
        get
        {
            // Object surface at infinite conjugate has no physical
            // ray-launch height — the auto-solver produces a meaningless
            // huge number from rays projected back from infinity. Blank
            // the cell so users don't think 3.6e14 is a real value.
            if (_surface.Index == 0 &&
                (double.IsInfinity(_surface.Thickness) || double.IsNaN(_surface.Thickness)))
                return string.Empty;
            return _surface.SemiDiameter > 0
                ? _surface.SemiDiameter.ToString("F4", CultureInfo.InvariantCulture)
                : "0";
        }
        set
        {
            // Only allow editing in Fixed mode, not on stop surface
            if (_surface.SemiDiameterMode != SemiDiameterMode.Fixed || _surface.IsStop) return;
            if (!double.TryParse(value, System.Globalization.NumberStyles.Float,
                CultureInfo.InvariantCulture, out double newSD) || newSD <= 0) return;

            double oldSD = _surface.SemiDiameter;
            if (Math.Abs(oldSD) > 1e-10 && Math.Abs(newSD - oldSD) > 1e-10)
            {
                // Recalculate CA% to reflect the new SD
                _surface.ClearAperturePercent *= newSD / oldSD;
                OnPropertyChanged(nameof(ClearAperturePercentText));
            }
            _surface.SemiDiameter = newSD;
            OnPropertyChanged();
            _session.NotifySystemChanged("surface");
        }
    }

    public string ClearAperturePercentText
    {
        get => _surface.IsStop ? "100.0" : _surface.ClearAperturePercent.ToString("F1", CultureInfo.InvariantCulture);
        set
        {
            if (_surface.IsStop) return;
            if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                CultureInfo.InvariantCulture, out double v) && v > 0)
            {
                _surface.ClearAperturePercent = v;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SemiDiameterDisplay));
                _session.NotifySystemChanged("surface");
            }
        }
    }

    /// <summary>True iff this row represents the image surface (last surface).</summary>
    private bool IsImage => _surface.Index == _session.System.Surfaces.Count - 1;

    /// <summary>
    /// CA% is editable only when Auto mode AND not the stop / object / image
    /// surface. OBJ and IMG are conceptual surfaces, not physical apertures —
    /// scaling their SD via CA% changes a stored number that drives nothing.
    /// </summary>
    public bool IsCaEditable =>
        !_surface.IsStop
        && _surface.SemiDiameterMode != SemiDiameterMode.Fixed
        && _surface.Index != 0
        && !IsImage;

    /// <summary>
    /// True if semi-diameter is editable: Fixed mode AND not stop / object /
    /// image. The engine never reads OBJ.SemiDiameter for ray launching, and
    /// IMG.SemiDiameter is auto-computed from ray heights — neither has a
    /// user-meaningful clear-aperture interpretation.
    /// </summary>
    public bool IsSemiDiameterEditable =>
        _surface.SemiDiameterMode == SemiDiameterMode.Fixed
        && !_surface.IsStop
        && _surface.Index != 0
        && !IsImage;

    public bool IsFixedSemiDiameter
    {
        get => _surface.SemiDiameterMode == SemiDiameterMode.Fixed;
        set
        {
            if (_surface.IsStop) return; // stop surface cannot be set to Fixed
            var mode = value ? SemiDiameterMode.Fixed : SemiDiameterMode.Auto;
            if (_surface.SemiDiameterMode != mode)
            {
                _surface.SemiDiameterMode = mode;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SemiDiameterDisplay));
                OnPropertyChanged(nameof(IsCaEditable));
                OnPropertyChanged(nameof(IsSemiDiameterEditable));
                OnPropertyChanged(nameof(CanSetFixed));
                _session.NotifySystemChanged("surface");
            }
        }
    }

    /// <summary>False for stop surface — prevents checking the Fixed SD checkbox.</summary>
    public bool CanSetFixed => !_surface.IsStop;

    /// <summary>
    /// Hide the Fixed SD checkbox on the object and image surfaces. The
    /// engine never reads OBJ.SemiDiameter or OBJ.SemiDiameterMode for ray
    /// launching (infinite conjugate uses field angle, finite uses fieldY);
    /// the IMG semi-diameter is auto-derived from ray heights and the
    /// layout sizes the image-plane line from the actual ray fan, so a
    /// Fixed value there just freezes a number nothing reads.
    /// </summary>
    public bool ShowFixedSd => _surface.Index != 0 && !IsImage;

    public Surface UnderlyingSurface => _surface;

    /// <summary>
    /// Refresh display-only properties (V/P indicators) without rebuilding the collection.
    /// Called after Properties dialog changes variable/pickup state, or after
    /// any external editor (e.g. Set CA % dialog) flips SemiDiameterMode on the
    /// underlying surface — without this, the Fixed checkbox would stay
    /// stale because the row's setter wasn't the one that mutated the source.
    /// </summary>
    public void RefreshDisplayProperties()
    {
        OnPropertyChanged(nameof(RadiusDisplay));
        OnPropertyChanged(nameof(ThicknessDisplay));
        OnPropertyChanged(nameof(ConicDisplay));
        OnPropertyChanged(nameof(SemiDiameterDisplay));
        OnPropertyChanged(nameof(ClearAperturePercentText));
        OnPropertyChanged(nameof(IsGlassUnresolved));
        OnPropertyChanged(nameof(IsFixedSemiDiameter));
        OnPropertyChanged(nameof(IsCaEditable));
        OnPropertyChanged(nameof(IsSemiDiameterEditable));
        OnPropertyChanged(nameof(CanSetFixed));
        // Stop checkbox: when the stop moves to another surface, the
        // previously-stop row's underlying _surface.IsStop has been
        // flipped false outside this VM. Without this notification the
        // old checkbox stays visually checked.
        OnPropertyChanged(nameof(IsStop));
    }

    private bool HasPickup(int surfIndex, PickupParameter param) =>
        _session.System.Pickups.Any(p => p.TargetSurfaceIndex == surfIndex && p.Parameter == param);
}

/// <summary>
/// ViewModel for the Surface Editor DataGrid (non-modal, main window).
/// </summary>
public partial class SurfaceEditorViewModel : ObservableObject
{
    public static SurfaceType[] SurfaceTypes { get; } = new[] { SurfaceType.Standard, SurfaceType.EvenAsphere };

    private readonly GuiSession _session;

    [ObservableProperty] private SurfaceRowViewModel? _selectedSurface;

    public ObservableCollection<SurfaceRowViewModel> Surfaces { get; } = new();

    public SurfaceEditorViewModel(GuiSession session)
    {
        _session = session;
        _session.SystemChanged += OnSystemChanged;
        Refresh();
    }

    private bool _isRefreshing;

    private void OnSystemChanged(string sender)
    {
        SurfaceDiagnostics.Log("SurfaceEditor.OnSystemChanged ENTER", _session.System,
            $"sender={sender}  rowCount={Surfaces.Count}");

        // For cell edits from this editor: don't rebuild the collection (avoids crash),
        // but DO refresh display properties so semi-diameters etc. update immediately.
        if (sender == "surface" || sender == "properties")
        {
            foreach (var s in Surfaces)
                s.RefreshDisplayProperties();
            SurfaceDiagnostics.Log("SurfaceEditor.OnSystemChanged EXIT (props-only)", _session.System,
                $"sender={sender}  rowCount={Surfaces.Count}");
            return;
        }

        // Structural changes (insert/delete/open/new): full rebuild.
        Refresh();
        SurfaceDiagnostics.Log("SurfaceEditor.OnSystemChanged EXIT (refreshed)", _session.System,
            $"sender={sender}  rowCount={Surfaces.Count}");
    }

    public void Refresh()
    {
        if (_isRefreshing) return;
        _isRefreshing = true;
        try
        {
            SurfaceDiagnostics.Log("SurfaceEditor.Refresh ENTER", _session.System,
                $"rowCountBefore={Surfaces.Count}");
            Surfaces.Clear();
            if (_session.System == null) return;
            foreach (var s in _session.System.Surfaces)
                Surfaces.Add(new SurfaceRowViewModel(s, _session));
            SurfaceDiagnostics.Log("SurfaceEditor.Refresh EXIT", _session.System,
                $"rowCountAfter={Surfaces.Count}");
        }
        finally { _isRefreshing = false; }
    }

    [RelayCommand]
    public void InsertSurface()
    {
        SurfaceDiagnostics.Log("InsertSurface ENTER", _session.System,
            $"selectedIndex={SelectedSurface?.Index.ToString() ?? "null"}");
        int idx = SelectedSurface != null ? SelectedSurface.Index + 1 : _session.System.Surfaces.Count - 1;
        if (idx < 1) idx = 1;
        if (idx >= _session.System.Surfaces.Count) idx = _session.System.Surfaces.Count - 1;

        var newSurf = new Surface { Index = idx, Thickness = 0 };

        // If inserting inside a glass element, copy the shape of the exit
        // surface onto the new surface (standard sequential-design convention).
        // The glass-to-air refraction stays at the correct curvature; the
        // original exit surface becomes air-to-air (optically transparent).
        var prevSurf = _session.System.Surfaces[idx - 1];
        if (!string.IsNullOrEmpty(prevSurf.Material))
        {
            var exitSurf = _session.System.Surfaces[idx];
            newSurf.Radius = exitSurf.Radius;
            newSurf.Conic = exitSurf.Conic;
            newSurf.Type = exitSurf.Type;
            if (exitSurf.AsphericCoefficients != null)
                newSurf.AsphericCoefficients = (double[])exitSurf.AsphericCoefficients.Clone();
        }

        _session.System.Surfaces.Insert(idx, newSurf);

        // Reindex
        for (int i = 0; i < _session.System.Surfaces.Count; i++)
            _session.System.Surfaces[i].Index = i;

        // Update surface references in merit function, pickups, etc.
        SurfaceIndexUpdater.OnSurfaceInserted(idx, _session.System,
            _session.MeritFunction, null);

        Refresh(); // structural change — rebuild directly
        _session.NotifySystemChanged("structure");
        SurfaceDiagnostics.Log("InsertSurface EXIT", _session.System);
    }

    [RelayCommand]
    public void DeleteSurface()
    {
        SurfaceDiagnostics.Log("DeleteSurface ENTER", _session.System,
            $"selectedIndex={SelectedSurface?.Index.ToString() ?? "null"}");
        if (SelectedSurface == null) return;
        int idx = SelectedSurface.Index;
        // Don't delete object (0) or image (last) surfaces
        if (idx <= 0 || idx >= _session.System.Surfaces.Count - 1) return;

        _session.System.Surfaces.RemoveAt(idx);

        // Reindex
        for (int i = 0; i < _session.System.Surfaces.Count; i++)
            _session.System.Surfaces[i].Index = i;

        // Update surface references in merit function, pickups, etc.
        SurfaceIndexUpdater.OnSurfaceRemoved(idx, _session.System,
            _session.MeritFunction, null);

        Refresh(); // structural change — rebuild directly
        _session.NotifySystemChanged("structure");
        SurfaceDiagnostics.Log("DeleteSurface EXIT", _session.System);
    }

    /// <summary>
    /// Copy the lens table to the clipboard as tab-delimited text.
    /// Raw values only — no V/P indicators. Rows flagged as Even Asphere
    /// get A2..A16 aspheric coefficients appended (in order, 8 values).
    /// Non-aspheric rows end after the base columns so pasting into a
    /// spreadsheet produces a clean column layout with the coefficients
    /// populated only where they apply.
    /// </summary>
    [RelayCommand]
    public async Task CopyTableToClipboard()
    {
        if (_session.System == null) return;

        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        // Header — always includes A2..A16 slots so pasted data lines up
        // in a spreadsheet even though non-aspheric rows leave them empty.
        sb.AppendLine("Surf\tType\tStop\tRadius\tThickness\tConic\tGlass\tSemiDiameter\tCA%\tFixedSD" +
                      "\tA2\tA4\tA6\tA8\tA10\tA12\tA14\tA16");

        int lastIdx = _session.System.Surfaces.Count - 1;
        for (int i = 0; i < _session.System.Surfaces.Count; i++)
        {
            var s = _session.System.Surfaces[i];
            string label  = i == 0 ? "OBJ" : (i == lastIdx ? "IMG" : i.ToString(ci));
            string type   = s.Type == SurfaceType.EvenAsphere ? "Even Asphere" : "Standard";
            string stop   = s.IsStop ? "Y" : "";
            string radius = double.IsPositiveInfinity(s.Radius)
                ? "Infinity" : s.Radius.ToString("G8", ci);
            string thick  = double.IsPositiveInfinity(s.Thickness)
                ? "Infinity" : s.Thickness.ToString("G8", ci);
            string conic  = s.Conic.ToString("G8", ci);
            string glass  = s.Material ?? "";
            string sd     = s.SemiDiameter.ToString("G8", ci);
            string caPct  = s.ClearAperturePercent.ToString("G8", ci);
            string fixedSd = s.SemiDiameterMode == SemiDiameterMode.Fixed ? "Y" : "";

            sb.Append(label).Append('\t').Append(type).Append('\t').Append(stop).Append('\t')
              .Append(radius).Append('\t').Append(thick).Append('\t').Append(conic).Append('\t')
              .Append(glass).Append('\t').Append(sd).Append('\t').Append(caPct).Append('\t')
              .Append(fixedSd);

            if (s.Type == SurfaceType.EvenAsphere && s.AsphericCoefficients != null)
            {
                foreach (double c in s.AsphericCoefficients)
                    sb.Append('\t').Append(c.ToString("G8", ci));
            }
            sb.AppendLine();
        }

        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (topLevel?.Clipboard is { } clip)
            await clip.SetTextAsync(sb.ToString());
    }
}
