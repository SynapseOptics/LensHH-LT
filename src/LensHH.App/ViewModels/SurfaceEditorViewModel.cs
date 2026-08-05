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

    // Type as string-based combo (Avalonia ComboBox works better with string/index).
    // "ABCD" (index 3) is a BASE surface (both editions), always offered. "Coordinate
    // Break" (index 4) is a PRO surface type — offered only when the edition enables it
    // (AppCapabilities.CoordinateBreakSupported), mirroring how the Hx column is gated.
    public static string[] TypeOptions { get; } =
        LensHH.App.AppCapabilities.CoordinateBreakSupported
            ? new[] { "Standard", "Even Asphere", "Paraxial", "ABCD", "Coordinate Break" }
            : new[] { "Standard", "Even Asphere", "Paraxial", "ABCD" };

    public string TypeDisplay => _surface.Type switch
    {
        SurfaceType.Paraxial => "Paraxial",
        SurfaceType.EvenAsphere => "Even Asphere",
        SurfaceType.Abcd => "ABCD",
        SurfaceType.CoordinateBreak => "Coordinate Break",
        _ => "Standard"
    };

    public int TypeIndex
    {
        get => _surface.Type switch
        {
            SurfaceType.Paraxial => 2,
            SurfaceType.EvenAsphere => 1,
            SurfaceType.Abcd => 3,
            SurfaceType.CoordinateBreak => 4,
            _ => 0
        };
        set
        {
            var newType = value switch
            {
                2 => SurfaceType.Paraxial,
                1 => SurfaceType.EvenAsphere,
                3 => SurfaceType.Abcd,
                4 => SurfaceType.CoordinateBreak,
                _ => SurfaceType.Standard
            };
            if (_surface.Type != newType)
            {
                _surface.Type = newType;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TypeDisplay));
                // Paraxial, ABCD, and Coordinate Break all blank + disable
                // Radius/Conic/Glass; refresh those cells.
                OnPropertyChanged(nameof(IsParaxial));
                OnPropertyChanged(nameof(IsAbcd));
                OnPropertyChanged(nameof(IsCoordinateBreak));
                OnPropertyChanged(nameof(RadiusDisplay));
                OnPropertyChanged(nameof(ConicDisplay));
                OnPropertyChanged(nameof(GlassDisplay));
                OnPropertyChanged(nameof(IsRadiusCellReadOnly));
                OnPropertyChanged(nameof(IsConicCellReadOnly));
                OnPropertyChanged(nameof(IsGlassCellReadOnly));
                _session.NotifySystemChanged("surface");
            }
        }
    }

    /// <summary>True when this surface is a Paraxial (ideal thin lens). Its only
    /// shape parameter is the focal length (edited in the Properties dialog); the
    /// Radius/Conic/Glass cells are blanked and non-editable.</summary>
    public bool IsParaxial => _surface.Type == SurfaceType.Paraxial;

    /// <summary>True when this surface is a Coordinate Break (PRO). It has no
    /// curvature/glass/conic — only decenter X/Y and tilt X/Y/Z (+ Order), edited in
    /// the Properties dialog's Coordinate Break tab. Radius/Conic/Glass cells are
    /// blanked and non-editable; Thickness and Semi-Diameter/CA% stay editable.</summary>
    public bool IsCoordinateBreak => _surface.Type == SurfaceType.CoordinateBreak;

    /// <summary>True when this surface is an ABCD ray-transfer-matrix surface (BASE —
    /// both editions). It has no curvature/glass/conic — only the 4 matrix params
    /// A/B/C/D, edited in the Properties dialog's ABCD tab. Radius/Conic/Glass cells are
    /// blanked and non-editable; Thickness and Semi-Diameter/CA% stay editable.</summary>
    public bool IsAbcd => _surface.Type == SurfaceType.Abcd;

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

            // Move the stop: clear it on every other surface, then set it
            // here. Notify with sender="stop" so the editor's
            // OnSystemChanged handler runs the full Refresh() path (Clear +
            // re-Add). Props-only refresh ("surface" sender) is not enough —
            // Avalonia's DataGrid leaves a stale visual row at the bottom
            // (the previously-stop surface) when only PropertyChanged events
            // fire. A Reset on the ObservableCollection forces the DataGrid
            // to drop its recycled visuals.
            foreach (var s in _session.System.Surfaces) s.IsStop = false;
            _surface.IsStop = true;
            OnPropertyChanged();
            _session.NotifySystemChanged("stop");
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
            if (IsParaxial || IsCoordinateBreak || IsAbcd) return string.Empty;   // no curvature on ideal-lens / coord-break
            string val = double.IsPositiveInfinity(_surface.Radius) ? "Infinity" : _surface.Radius.ToString("G8", CultureInfo.InvariantCulture);
            if (IsRadiusOwned) return val + Marker(LensHH.App.SurfaceConfigParam.Curvature);   // owned: show active-config solve
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
            if (IsThicknessOwned) return val + Marker(LensHH.App.SurfaceConfigParam.Thickness);   // owned: show active-config solve
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
            if (IsParaxial || IsCoordinateBreak || IsAbcd) return string.Empty;   // no conic on ideal-lens / coord-break
            string val = _surface.Conic.ToString("G8", CultureInfo.InvariantCulture);
            if (IsConicOwned) return val + Marker(LensHH.App.SurfaceConfigParam.Conic);   // owned: show active-config solve
            if (_surface.ConicVariable) val += " V";
            else if (HasPickup(_surface.Index, PickupParameter.Conic)) val += " P";
            return val;
        }
    }

    public string Material
    {
        get => _surface.Material;
        set { if (_surface.Material != value) { _surface.Material = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(GlassDisplay)); OnPropertyChanged(nameof(IsGlassUnresolved)); _session.NotifySystemChanged("surface"); } }
    }

    /// <summary>Glass-column text. For a model-index surface the index is computed from
    /// Nd/Vd/dPgF, not a catalog glass: show the nearest catalog glass name when one has
    /// been assigned (e.g. an import that snapped to "SK16"), else the generic "Model".
    /// Either way the cell is italic (IsModelGlass) to flag that it's a model, not a
    /// literal catalog glass.</summary>
    public string GlassDisplay => (IsParaxial || IsCoordinateBreak || IsAbcd) ? string.Empty
        : (_surface.ModelIndexEnabled
            ? (string.IsNullOrEmpty(_surface.Material) ? "Model" : _surface.Material)
            : (_surface.Material ?? string.Empty));

    /// <summary>True when the glass cell shows the model-index "Model" placeholder —
    /// drives the italic style on the Glass column (see MainWindow.axaml).</summary>
    public bool IsModelGlass => _surface.ModelIndexEnabled;

    /// <summary>The glass cell is not editable when the value is owned by a
    /// configuration OR when model-index mode is on (glass is replaced by Nd/Vd/dPgF;
    /// re-enable by unchecking "Enable Model Index" in the surface's Glass Model tab).</summary>
    public bool IsGlassCellReadOnly => IsGlassOwned || _surface.ModelIndexEnabled || IsParaxial || IsCoordinateBreak || IsAbcd;

    /// <summary>Radius / Conic cells are read-only when owned by a configuration
    /// OR when this is a Paraxial / Coordinate Break surface (curvature/conic are not applicable).</summary>
    public bool IsRadiusCellReadOnly => IsRadiusOwned || IsParaxial || IsCoordinateBreak || IsAbcd;
    public bool IsConicCellReadOnly => IsConicOwned || IsParaxial || IsCoordinateBreak || IsAbcd;

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

    // Aperture solve marker (" V" variable / " P" pickup / "") shown beside the value. The marker
    // sits on the free quantity for the aperture mode: Fixed -> Semi-Diameter; Auto -> Clear Aperture %.
    private string ApertureMarker(bool isCA)
    {
        bool variable = isCA ? _surface.ClearAperturePercentVariable : _surface.SemiDiameterVariable;
        var param = isCA ? Core.Enums.PickupParameter.ClearAperturePercent : Core.Enums.PickupParameter.SemiDiameter;
        bool pickup = _session.System.Pickups.Any(p => p.TargetSurfaceIndex == _surface.Index && p.Parameter == param);
        if (pickup) return " P";
        if (variable) return " V";
        return "";
    }

    public string SemiDiameterMarker =>
        _surface.SemiDiameterMode == SemiDiameterMode.Fixed ? ApertureMarker(isCA: false) : "";
    public string ClearAperturePercentMarker =>
        _surface.SemiDiameterMode == SemiDiameterMode.Auto ? ApertureMarker(isCA: true) : "";

    // Value + marker for the read-only display cell (edit cells bind the raw value). Keeps the
    // Semi-Diameter / CA% cells centered and aligned like Radius/Thickness/Conic.
    public string SemiDiameterWithMarker => SemiDiameterDisplay + SemiDiameterMarker;
    public string ClearAperturePercentWithMarker => ClearAperturePercentText + ClearAperturePercentMarker;

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

    // ── Multi-configuration ownership (advanced edition) ──────────────────────
    // A surface parameter that varies across configurations is OWNED by the multi-configuration
    // editor: the Lens Editor renders it read-only + italic, because its per-config values live
    // in that editor and editing the single base value here would silently desync. Derived from
    // the neutral cell-ownership seam — null in the standard build → never owned (LT unchanged).
    private bool Owned(LensHH.App.SurfaceConfigParam p)
        => LensHH.App.AppExtensions.CellOwnership?.IsVariedAcrossConfigs(_surface.Index, p) ?? false;

    // Active-configuration solve marker (" V" / " P" / "") for an owned parameter, appended to the
    // (italic, read-only) cell so the user sees the solve of the value currently shown.
    private string Marker(LensHH.App.SurfaceConfigParam p)
        => LensHH.App.AppExtensions.CellOwnership?.ConfigSolveMarker(_surface.Index, p) ?? "";

    public bool IsRadiusOwned    => Owned(LensHH.App.SurfaceConfigParam.Curvature);
    public bool IsThicknessOwned => Owned(LensHH.App.SurfaceConfigParam.Thickness);
    public bool IsConicOwned     => Owned(LensHH.App.SurfaceConfigParam.Conic);
    public bool IsGlassOwned     => Owned(LensHH.App.SurfaceConfigParam.Glass);

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
        OnPropertyChanged(nameof(SemiDiameterMarker));
        OnPropertyChanged(nameof(ClearAperturePercentMarker));
        OnPropertyChanged(nameof(SemiDiameterWithMarker));
        OnPropertyChanged(nameof(ClearAperturePercentWithMarker));
        OnPropertyChanged(nameof(IsGlassUnresolved));
        OnPropertyChanged(nameof(GlassDisplay));
        OnPropertyChanged(nameof(IsModelGlass));
        OnPropertyChanged(nameof(IsGlassCellReadOnly));
        OnPropertyChanged(nameof(IsFixedSemiDiameter));
        OnPropertyChanged(nameof(IsCaEditable));
        OnPropertyChanged(nameof(IsSemiDiameterEditable));
        OnPropertyChanged(nameof(CanSetFixed));
        // Multi-config ownership (read-only + italic) can change when an operand is
        // added/removed or the config count crosses 1.
        OnPropertyChanged(nameof(IsRadiusOwned));
        OnPropertyChanged(nameof(IsThicknessOwned));
        OnPropertyChanged(nameof(IsConicOwned));
        OnPropertyChanged(nameof(IsGlassOwned));
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
        // For cell edits from this editor: don't rebuild the collection (avoids crash),
        // but DO refresh display properties so semi-diameters etc. update immediately.
        if (sender == "surface" || sender == "properties")
        {
            foreach (var s in Surfaces)
                s.RefreshDisplayProperties();
            return;
        }

        // Structural changes (insert/delete/open/new) and stop changes: full rebuild.
        Refresh();
    }

    public void Refresh()
    {
        if (_isRefreshing) return;
        _isRefreshing = true;
        try
        {
            Surfaces.Clear();
            if (_session.System == null) return;
            foreach (var s in _session.System.Surfaces)
                Surfaces.Add(new SurfaceRowViewModel(s, _session));
        }
        finally { _isRefreshing = false; }
    }

    [RelayCommand]
    public void InsertSurface()
    {
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
            _session.MeritFunction);

        Refresh(); // structural change — rebuild directly
        _session.NotifySystemChanged("structure");
    }

    [RelayCommand]
    public void DeleteSurface()
    {
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
            _session.MeritFunction);

        Refresh(); // structural change — rebuild directly
        _session.NotifySystemChanged("structure");
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
