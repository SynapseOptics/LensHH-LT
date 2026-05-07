using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LensHH.App.Session;
using LensHH.Core.Enums;
using LensHH.Core.MeritFunction;

namespace LensHH.App.ViewModels;

public partial class OperandRowViewModel : ObservableObject
{
    private readonly Operand _operand;
    private readonly GuiSession _session;
    private readonly int _index;

    /// <summary>Fired when the operand Type changes, so the parent can refresh help text.</summary>
    public Action? OnTypeChanged { get; set; }

    public OperandRowViewModel(Operand operand, int index, GuiSession session)
    {
        _operand = operand;
        _index = index;
        _session = session;
    }

    public Operand Underlying => _operand;
    public int Number => _index + 1;

    // ── Operand category ──

    private enum Category { RayIntercept, Macro, MacroRect, System, Boundary, SurfaceProperty, Arithmetic }

    private Category GetCategory() => _operand.Type switch
    {
        // Ray intercept / paraxial
        OperandType.RX or OperandType.RY or OperandType.RZ
        or OperandType.RL or OperandType.RM or OperandType.RN
        or OperandType.AOID or OperandType.AOED or OperandType.AOER or OperandType.AOIR
        or OperandType.PL or OperandType.PM or OperandType.PN
        or OperandType.PX or OperandType.PY or OperandType.PZ
            => Category.RayIntercept,

        // Macro Forbes (rings/arms)
        OperandType.WAVEX or OperandType.WAVEM or OperandType.WAVEC
        or OperandType.SPOTM or OperandType.SPOT or OperandType.SENS
            => Category.Macro,

        // Macro Rectangular (gridsize)
        OperandType.WAVEXR or OperandType.WAVEMR or OperandType.WAVECR
        or OperandType.SPOTMR or OperandType.SPOTR
            => Category.MacroRect,

        // System
        OperandType.EFL or OperandType.MAG or OperandType.AMAG
        or OperandType.EXPZ or OperandType.ENPZ
        or OperandType.ENPD or OperandType.EXPD or OperandType.TTRACK
        or OperandType.ILL
        or OperandType.DITAN or OperandType.DITHETA
        or OperandType.DITANF or OperandType.DITHETAF or OperandType.LCF
            => Category.System,

        // Boundary
        OperandType.CV or OperandType.CVA or OperandType.CVG
        or OperandType.CT or OperandType.CTA or OperandType.CTG
        or OperandType.ET or OperandType.EA or OperandType.EG
        or OperandType.SD or OperandType.DTRG
        or OperandType.RI or OperandType.RE
            => Category.Boundary,

        // Surface property
        OperandType.DM
            => Category.SurfaceProperty,

        // Arithmetic
        OperandType.MULTC or OperandType.SUMR or OperandType.SUM
        or OperandType.DIV or OperandType.MULT
        or OperandType.DEV or OperandType.DIFF
        or OperandType.QSUMR or OperandType.DIFF
            => Category.Arithmetic,

        _ => Category.RayIntercept
    };

    // Relevance flags
    private bool NeedsSurface => GetCategory() is Category.RayIntercept or Category.Boundary or Category.SurfaceProperty;
    private bool NeedsSurface2 => GetCategory() is Category.Boundary;
    private bool NeedsWave => (GetCategory() is Category.RayIntercept or Category.System)
        && _operand.Type != OperandType.ILL
        && _operand.Type != OperandType.DITAN
        && _operand.Type != OperandType.DITHETA
        && _operand.Type != OperandType.DITANF
        && _operand.Type != OperandType.DITHETAF
        && _operand.Type != OperandType.LCF;
    private bool NeedsRayCoords => GetCategory() is Category.RayIntercept;
    private bool NeedsHyOnly => _operand.Type is OperandType.ILL
        or OperandType.DITANF or OperandType.DITHETAF or OperandType.LCF;
    private bool NeedsRingsArms => GetCategory() is Category.Macro;
    // RELI/ILL borrows the Arms column for pupil-boundary directions; Rings
    // doesn't apply (the binary search is the radial sample). Macro
    // operands need both.
    private bool NeedsArms => NeedsRingsArms || _operand.Type == OperandType.ILL;
    private bool NeedsRings => NeedsRingsArms;
    private bool NeedsGrid => GetCategory() is Category.MacroRect;
    private bool NeedsOp1 => GetCategory() is Category.Arithmetic;
    private bool NeedsOp2 => GetCategory() is Category.Arithmetic && _operand.Type != OperandType.MULTC;
    private bool NeedsFactor => _operand.Type == OperandType.MULTC;

    // ── Type ──

    public static List<string> TypeOptions { get; } = Enum.GetValues(typeof(OperandType))
        .Cast<OperandType>()
        .Where(t => !t.ToString().StartsWith("_"))
        .Select(t => t.ToString())
        .OrderBy(t => t)
        .ToList();

    public int TypeIndex
    {
        get => TypeOptions.IndexOf(_operand.Type.ToString());
        set
        {
            if (value >= 0 && value < TypeOptions.Count &&
                Enum.TryParse<OperandType>(TypeOptions[value], out var t))
            {
                _operand.Type = t;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TypeName));
                // Refresh all conditional display properties
                NotifyAllFields();
                OnTypeChanged?.Invoke();
            }
        }
    }

    public string TypeName => _operand.Type.ToString();

    // ── Operation Code ──

    public static List<string> OpCodeOptions { get; } = Enum.GetValues(typeof(OperationCode))
        .Cast<OperationCode>()
        .Select(c => c.ToString())
        .ToList();

    public int OpCodeIndex
    {
        get => OpCodeOptions.IndexOf(_operand.OpCode.ToString());
        set
        {
            if (value >= 0 && value < OpCodeOptions.Count &&
                Enum.TryParse<OperationCode>(OpCodeOptions[value], out var c))
            {
                _operand.OpCode = c;
                OnPropertyChanged();
            }
        }
    }

    // ── Conditional string properties (return "" when not relevant) ──

    public string SurfaceText
    {
        get => NeedsSurface ? (GetCategory() == Category.Boundary ? _operand.Surface1 : _operand.SurfaceIndex).ToString() : "";
        set
        {
            if (!NeedsSurface || !int.TryParse(value, out int v)) return;
            int maxSurf = _session.System.Surfaces.Count - 1;
            if (v < 0 || v > maxSurf) return;
            if (GetCategory() == Category.Boundary)
                _operand.Surface1 = v;
            else { _operand.SurfaceIndex = v; _operand.Surface1 = v; }
            OnPropertyChanged();
        }
    }

    public string Surface2Text
    {
        get => NeedsSurface2 ? _operand.Surface2.ToString() : "";
        set
        {
            if (!NeedsSurface2 || !int.TryParse(value, out int v)) return;
            int maxSurf = _session.System.Surfaces.Count - 1;
            if (v < 0 || v > maxSurf) return;
            _operand.Surface2 = v;
            OnPropertyChanged();
        }
    }

    public string WaveText
    {
        get
        {
            if (!NeedsWave) return "";
            // -1 means "use primary wavelength" — display its 1-based index
            if (_operand.WaveIndex < 0)
                return (_session.System.PrimaryWavelengthIndex + 1).ToString();
            return (_operand.WaveIndex + 1).ToString();
        }
        set
        {
            if (!NeedsWave || !int.TryParse(value, out int v)) return;
            int waveCount = _session.System.Wavelengths.Count;
            if (v < 1 || v > waveCount) return; // silently reject out-of-range
            _operand.WaveIndex = v - 1;
            OnPropertyChanged();
        }
    }

    public string HxText
    {
        get => NeedsRayCoords ? _operand.Hx.ToString("G4", CultureInfo.InvariantCulture) : "";
        set { if (NeedsRayCoords && TryParse(value, out double v)) { _operand.Hx = v; OnPropertyChanged(); } }
    }

    public string HyText
    {
        get => (NeedsRayCoords || NeedsHyOnly) ? _operand.Hy.ToString("G4", CultureInfo.InvariantCulture) : "";
        set { if ((NeedsRayCoords || NeedsHyOnly) && TryParse(value, out double v)) { _operand.Hy = v; OnPropertyChanged(); } }
    }

    public string PxText
    {
        get => NeedsRayCoords ? _operand.Px.ToString("G4", CultureInfo.InvariantCulture) : "";
        set { if (NeedsRayCoords && TryParse(value, out double v)) { _operand.Px = v; OnPropertyChanged(); } }
    }

    public string PyText
    {
        get => NeedsRayCoords ? _operand.Py.ToString("G4", CultureInfo.InvariantCulture) : "";
        set { if (NeedsRayCoords && TryParse(value, out double v)) { _operand.Py = v; OnPropertyChanged(); } }
    }

    public string RingsText
    {
        get => NeedsRings ? _operand.EffectiveRings.ToString() : "";
        set { if (NeedsRings && int.TryParse(value, out int v)) { _operand.Rings = v; OnPropertyChanged(); } }
    }

    public string ArmsText
    {
        // For ILL with no explicit Arms, show "36" (the analysis-default
        // pupil-boundary directions) so users see what's being used.
        get => NeedsArms
            ? (_operand.Arms > 0 ? _operand.Arms.ToString()
               : (_operand.Type == OperandType.ILL ? "36" : _operand.EffectiveArms.ToString()))
            : "";
        set { if (NeedsArms && int.TryParse(value, out int v)) { _operand.Arms = v; OnPropertyChanged(); } }
    }

    public string GridText
    {
        get => NeedsGrid ? _operand.EffectiveGridSize.ToString() : "";
        set { if (NeedsGrid && int.TryParse(value, out int v)) { _operand.GridSize = v; OnPropertyChanged(); } }
    }

    public string Op1Text
    {
        get => NeedsOp1 ? (_operand.OperandNo + 1).ToString() : "";
        set { if (NeedsOp1 && int.TryParse(value, out int v)) { _operand.OperandNo = v - 1; OnPropertyChanged(); } }
    }

    public string Op2Text
    {
        get => NeedsOp2 ? (_operand.OperandNo2 + 1).ToString() : "";
        set { if (NeedsOp2 && int.TryParse(value, out int v)) { _operand.OperandNo2 = v - 1; OnPropertyChanged(); } }
    }

    public string FactorText
    {
        get => NeedsFactor ? _operand.Factor.ToString("G6", CultureInfo.InvariantCulture) : "";
        set { if (NeedsFactor && TryParse(value, out double v)) { _operand.Factor = v; OnPropertyChanged(); } }
    }

    // ── Weight ──

    public string WeightText
    {
        get => _operand.Weight.ToString("G4", CultureInfo.InvariantCulture);
        set { if (TryParse(value, out double v)) { _operand.Weight = v; OnPropertyChanged(); } }
    }

    // ── Constraint mode (Target / Min / Max / Min+Max) ──

    public static List<string> ModeOptions { get; } = new() { "Target", "Min", "Max", "Min/Max" };

    public int ModeIndex
    {
        get
        {
            bool hasMin = _operand.Minimum.HasValue;
            bool hasMax = _operand.Maximum.HasValue;
            if (hasMin && hasMax) return 3; // Min/Max
            if (hasMin) return 1;           // Min
            if (hasMax) return 2;           // Max
            return 0;                       // Target
        }
        set
        {
            switch (value)
            {
                case 0: // Target — clear boundaries
                    _operand.Minimum = null;
                    _operand.Maximum = null;
                    break;
                case 1: // Min only
                    if (!_operand.Minimum.HasValue) _operand.Minimum = 0;
                    _operand.Maximum = null;
                    break;
                case 2: // Max only
                    _operand.Minimum = null;
                    if (!_operand.Maximum.HasValue) _operand.Maximum = 0;
                    break;
                case 3: // Min/Max
                    if (!_operand.Minimum.HasValue) _operand.Minimum = 0;
                    if (!_operand.Maximum.HasValue) _operand.Maximum = 0;
                    break;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(Bound1Text));
            OnPropertyChanged(nameof(Bound2Text));
            OnPropertyChanged(nameof(IsBound2Visible));
            OnPropertyChanged(nameof(Bound1Header));
        }
    }

    /// <summary>Header hint for the Bound1 column — exposed per-row for tooltip or future use.</summary>
    public string Bound1Header => ModeIndex switch { 0 => "Target", 2 => "Max", _ => "Min" };

    /// <summary>Whether Bound2 (Max in Min/Max mode) is active.</summary>
    public bool IsBound2Visible => ModeIndex == 3;

    /// <summary>
    /// Primary bound value: Target in Target mode, Min in Min/Min+Max mode, Max in Max mode.
    /// </summary>
    public string Bound1Text
    {
        get => ModeIndex switch
        {
            0 => _operand.Target.ToString("G6", CultureInfo.InvariantCulture),
            2 => _operand.Maximum.HasValue ? _operand.Maximum.Value.ToString("G6", CultureInfo.InvariantCulture) : "",
            _ => _operand.Minimum.HasValue ? _operand.Minimum.Value.ToString("G6", CultureInfo.InvariantCulture) : "",
        };
        set
        {
            if (!TryParse(value, out double v)) return;
            switch (ModeIndex)
            {
                case 0: _operand.Target = v; break;
                case 2: _operand.Maximum = v; break;
                default: _operand.Minimum = v; break;
            }
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Secondary bound value: Max in Min/Max mode, empty otherwise.
    /// </summary>
    public string Bound2Text
    {
        get => ModeIndex == 3 && _operand.Maximum.HasValue
            ? _operand.Maximum.Value.ToString("G6", CultureInfo.InvariantCulture) : "";
        set
        {
            if (ModeIndex != 3) return;
            if (TryParse(value, out double v)) { _operand.Maximum = v; OnPropertyChanged(); }
        }
    }

    // ── Computed value & contribution (read-only) ──

    public string ValueText => _operand.Value.ToString("E4");

    /// <summary>Contribution = residual², shows how much this operand drives the merit.</summary>
    public string ContributionText
    {
        get
        {
            double r = _operand.Residual;
            return r == 0 ? "0" : (r * r).ToString("E4");
        }
    }

    public void RefreshValue()
    {
        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(ContributionText));
    }

    // ── Helpers ──

    private void NotifyAllFields()
    {
        OnPropertyChanged(nameof(SurfaceText));
        OnPropertyChanged(nameof(Surface2Text));
        OnPropertyChanged(nameof(WaveText));
        OnPropertyChanged(nameof(HxText));
        OnPropertyChanged(nameof(HyText));
        OnPropertyChanged(nameof(PxText));
        OnPropertyChanged(nameof(PyText));
        OnPropertyChanged(nameof(RingsText));
        OnPropertyChanged(nameof(ArmsText));
        OnPropertyChanged(nameof(GridText));
        OnPropertyChanged(nameof(Op1Text));
        OnPropertyChanged(nameof(Op2Text));
        OnPropertyChanged(nameof(FactorText));
    }

    private static bool TryParse(string s, out double value)
        => double.TryParse(s, NumberStyles.Float | NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture, out value);
}

public partial class MeritFunctionEditorViewModel : ObservableObject
{
    private readonly GuiSession _session;

    public ObservableCollection<OperandRowViewModel> Operands { get; } = new();

    [ObservableProperty] private string _meritValueText = "";

    private OperandRowViewModel? _selectedOperand;
    public OperandRowViewModel? SelectedOperand
    {
        get => _selectedOperand;
        set
        {
            if (SetProperty(ref _selectedOperand, value))
                OnPropertyChanged(nameof(HelpText));
        }
    }

    /// <summary>Context-sensitive help for the selected operand type.</summary>
    public string HelpText => _selectedOperand != null
        ? GetOperandHelp(_selectedOperand.Underlying.Type)
        : "Select an operand to see its description.";

    private static string GetOperandHelp(OperandType type) => type switch
    {
        // Ray intercept
        OperandType.RX => "RX — Real ray X coordinate at a surface. Params: Surface, Wave, Hy, Px, Py",
        OperandType.RY => "RY — Real ray Y coordinate at a surface. Params: Surface, Wave, Hy, Px, Py",
        OperandType.RZ => "RZ — Real ray Z coordinate at a surface. Params: Surface, Wave, Hy, Px, Py",
        OperandType.RL => "RL — Real ray L direction cosine at a surface. Params: Surface, Wave, Hy, Px, Py",
        OperandType.RM => "RM — Real ray M direction cosine at a surface. Params: Surface, Wave, Hy, Px, Py",
        OperandType.RN => "RN — Real ray N direction cosine at a surface. Params: Surface, Wave, Hy, Px, Py",
        OperandType.RI => "RI — Angle of incidence (degrees) across surface range. Use Mode Min/Max to constrain. Params: Surf, Surf2",
        OperandType.RE => "RE — Angle of exitance (degrees) across surface range. Use Mode Min/Max to constrain. Params: Surf, Surf2",
        OperandType.AOID => "AOID — Angle of incidence in degrees. Params: Surface, Wave, Hy, Px, Py",
        OperandType.AOED => "AOED — Angle of exitance in degrees. Params: Surface, Wave, Hy, Px, Py",
        OperandType.AOER => "AOER — Angle of exitance in radians. Params: Surface, Wave, Hy, Px, Py",
        OperandType.AOIR => "AOIR — Angle of incidence in radians. Params: Surface, Wave, Hy, Px, Py",

        // Paraxial
        OperandType.PX => "PX — Paraxial ray X coordinate. Params: Surface, Wave, Hy, Px, Py",
        OperandType.PY => "PY — Paraxial ray Y coordinate. Params: Surface, Wave, Hy, Px, Py",
        OperandType.PZ => "PZ — Paraxial ray Z coordinate. Params: Surface, Wave, Hy, Px, Py",
        OperandType.PL => "PL — Paraxial ray L direction cosine. Params: Surface, Wave, Hy, Px, Py",
        OperandType.PM => "PM — Paraxial ray M direction cosine. Params: Surface, Wave, Hy, Px, Py",
        OperandType.PN => "PN — Paraxial ray N direction cosine. Params: Surface, Wave, Hy, Px, Py",

        // Wavefront macros
        OperandType.WAVEX => "WAVEX — RMS wavefront error, chief ray ref, tilt and piston removed. Expands across all fields/wavelengths. Params: Rings, Arms",
        OperandType.WAVEM => "WAVEM — RMS wavefront error, chief ray ref, piston (mean) removed only. Expands across all fields/wavelengths. Params: Rings, Arms",
        OperandType.WAVEC => "WAVEC — RMS wavefront error, chief ray ref, no removal. Expands across all fields/wavelengths. Params: Rings, Arms",
        OperandType.WAVEXR => "WAVEXR — RMS wavefront error, chief ray ref, tilt and piston removed, rectangular grid. Params: Grid",
        OperandType.WAVEMR => "WAVEMR — RMS wavefront error, chief ray ref, piston (mean) removed only, rectangular grid. Params: Grid",
        OperandType.WAVECR => "WAVECR — RMS wavefront error, chief ray ref, no removal, rectangular grid. Params: Grid",

        // Spot macros
        OperandType.SPOTM => "SPOTM — RMS spot size relative to centroid. Expands across all fields/wavelengths. Params: Rings, Arms",
        OperandType.SPOT => "SPOT — RMS spot size relative to chief ray. Expands across all fields/wavelengths. Params: Rings, Arms",
        OperandType.SPOTMR => "SPOTMR — RMS spot size (centroid), rectangular grid. Params: Grid",
        OperandType.SPOTR => "SPOTR — RMS spot size (chief ray), rectangular grid. Params: Grid",

        // Sensitivity
        OperandType.SENS => "SENS — Sensitivity operand for as-built performance (Moore, SPIE 10925, 2019). Penalizes surfaces with high Δn·(1−cosθ), reducing sensitivity to manufacturing errors (tilt, decenter). Params: Rings, Arms",

        // System
        OperandType.EFL => "EFL — Effective focal length. Params: Wave (optional)",
        OperandType.MAG => "MAG — Paraxial magnification. Params: Wave (optional)",
        OperandType.AMAG => "AMAG — Angular magnification. Params: Wave (optional)",
        OperandType.EXPZ => "EXPZ — Exit pupil Z position (distance from image). Params: Wave (optional)",
        OperandType.ENPZ => "ENPZ — Entrance pupil Z position (distance from surface 1). Params: Wave (optional)",
        OperandType.ENPD => "ENPD — Entrance pupil diameter. Params: Wave (optional)",
        OperandType.EXPD => "EXPD — Exit pupil diameter. Params: Wave (optional)",
        OperandType.TTRACK => "TTRACK — Total track (surface 1 to image). No params.",
        OperandType.ILL => "ILL — Relative illumination at field point. RI = (F/#_on-axis / F/#_field)². Uses primary wavelength. Can exceed 1.0. Params: Hy",
        OperandType.DITAN => "DITAN — Maximum F-tan(θ) distortion (%) across all fields. Returns signed value with largest absolute distortion. No params.",
        OperandType.DITHETA => "DITHETA — Maximum F-θ distortion (%) across all fields. Returns signed value with largest absolute distortion. No params.",
        OperandType.DITANF => "DITANF — F-tan(θ) distortion (%) at a specific field point. Params: Hy",
        OperandType.DITHETAF => "DITHETAF — F-θ distortion (%) at a specific field point. Params: Hy",
        OperandType.LCF => "LCF — Lateral color at a specific field point. Max chief ray height spread across all wavelengths (µm or arcmin). Params: Hy",

        // Boundary
        OperandType.CV => "CV — Curvature of surfaces in range. Use Mode to set Min/Max bounds. Params: Surf, Surf2",
        OperandType.CVA => "CVA — Curvature of air spaces in range. Params: Surf, Surf2",
        OperandType.CVG => "CVG — Curvature of glass surfaces in range. Params: Surf, Surf2",
        OperandType.CT => "CT — Center thickness in range (all). Use Mode to set Min/Max bounds. Params: Surf, Surf2",
        OperandType.CTA => "CTA — Center thickness of air spaces in range. Params: Surf, Surf2",
        OperandType.CTG => "CTG — Center thickness of glass elements in range. Params: Surf, Surf2",
        OperandType.ET => "ET — Edge thickness in range (all). Params: Surf, Surf2",
        OperandType.EA => "EA — Edge thickness of air spaces in range. Params: Surf, Surf2",
        OperandType.EG => "EG — Edge thickness of glass elements in range. Params: Surf, Surf2",
        OperandType.SD => "SD — Semi-diameter of surfaces in range. Params: Surf, Surf2",
        OperandType.DTRG => "DTRG — Diameter-to-thickness ratio (2·SD / |CT|) of glass elements in range. Fabrication constraint. Params: Surf, Surf2",

        // Surface property
        OperandType.DM => "DM — Diameter (semi-diameter) of a single surface. Params: Surf",

        // Arithmetic
        OperandType.MULTC => "MULTC — Multiply operand by a constant factor. Params: Op1, Factor",
        OperandType.SUMR => "SUMR — Sum of all operand values from Op1 to Op2 (inclusive range). Params: Op1, Op2",
        OperandType.SUM => "SUM — Op1 + Op2. Params: Op1, Op2",
        OperandType.DIV => "DIV — Op1 / Op2. Params: Op1, Op2",
        OperandType.MULT => "MULT — Op1 * Op2. Params: Op1, Op2",
        OperandType.DEV => "DEV — Sum of absolute deviations from mean across Op1..Op2 range. Params: Op1, Op2",
        OperandType.QSUMR => "QSUMR — Root sum of squares (RSS) across Op1..Op2 range. Params: Op1, Op2",
        OperandType.DIFF => "DIFF — Op1 - Op2. Params: Op1, Op2",

        _ => $"{type} — No description available."
    };

    public MeritFunctionEditorViewModel(GuiSession session)
    {
        _session = session;
        _session.SystemChanged += OnSystemChanged;
        Refresh();
    }

    private void OnSystemChanged(string sender)
    {
        if (sender != "meritfunction")
            Refresh();
    }

    public void Refresh()
    {
        Operands.Clear();
        var mf = _session.MeritFunction;
        for (int i = 0; i < mf.Operands.Count; i++)
        {
            var row = new OperandRowViewModel(mf.Operands[i], i, _session);
            row.OnTypeChanged = () => OnPropertyChanged(nameof(HelpText));
            Operands.Add(row);
        }
        MeritValueText = mf.MeritValue != 0 ? $"Merit: {mf.MeritValue:E6}" : "";
    }

    [RelayCommand]
    public void InsertOperand()
    {
        var mf = _session.MeritFunction;
        int insertIdx = mf.Operands.Count;

        if (SelectedOperand != null)
        {
            int selIdx = Operands.IndexOf(SelectedOperand);
            if (selIdx >= 0)
                insertIdx = selIdx + 1;
        }

        var newOp = new Operand { Type = OperandType.EFL, Weight = 1.0 };
        mf.InsertOperand(insertIdx, newOp);
        Refresh();

        if (insertIdx < Operands.Count)
            SelectedOperand = Operands[insertIdx];
    }

    [RelayCommand]
    public void RemoveOperand()
    {
        if (SelectedOperand == null) return;
        int idx = Operands.IndexOf(SelectedOperand);
        if (idx < 0) return;

        _session.MeritFunction.RemoveOperand(idx);
        Refresh();

        if (Operands.Count > 0)
            SelectedOperand = Operands[Math.Min(idx, Operands.Count - 1)];
    }

    [RelayCommand]
    public void Evaluate()
    {
        if (_session.CannotCompute) { MeritValueText = _session.CannotComputeMessage; return; }
        try
        {
            var mf = _session.MeritFunction;
            var evaluator = new MeritFunctionEvaluator(
                _session.System, _session.GlassCatalog)
            { ParallelEvaluation = true };
            double merit = evaluator.Evaluate(mf);
            MeritValueText = $"Merit: {merit:E6}";

            foreach (var row in Operands)
                row.RefreshValue();
        }
        catch (Exception ex)
        {
            MeritValueText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    public void ClearAll()
    {
        _session.MeritFunction.Clear();
        Refresh();
        MeritValueText = "";
    }

    /// <summary>Copy the merit-function table to the clipboard as tab-delimited text.
    /// Includes the header row (column names), one row per operand with the same
    /// columns shown in the editor grid, and a final row with the overall merit
    /// value. Intended for paste-to-spreadsheet and for sharing diagnostic data.</summary>
    [RelayCommand]
    public async Task CopyTableToClipboard()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("#\tType\tOpCode\tSurf\tSurf2\tWave\tWeight\tMode\tBound1\tBound2\tHy\tPx\tPy\tRings\tArms\tGrid\tOp1\tOp2\tFactor\tValue\tContribution");
        foreach (var op in Operands)
        {
            string type = (op.TypeIndex >= 0 && op.TypeIndex < OperandRowViewModel.TypeOptions.Count)
                ? OperandRowViewModel.TypeOptions[op.TypeIndex] : "";
            string opCode = (op.OpCodeIndex >= 0 && op.OpCodeIndex < OperandRowViewModel.OpCodeOptions.Count)
                ? OperandRowViewModel.OpCodeOptions[op.OpCodeIndex] : "";
            string mode = (op.ModeIndex >= 0 && op.ModeIndex < OperandRowViewModel.ModeOptions.Count)
                ? OperandRowViewModel.ModeOptions[op.ModeIndex] : "";

            sb.Append(op.Number).Append('\t')
              .Append(type).Append('\t')
              .Append(opCode).Append('\t')
              .Append(op.SurfaceText ?? "").Append('\t')
              .Append(op.Surface2Text ?? "").Append('\t')
              .Append(op.WaveText ?? "").Append('\t')
              .Append(op.WeightText ?? "").Append('\t')
              .Append(mode).Append('\t')
              .Append(op.Bound1Text ?? "").Append('\t')
              .Append(op.Bound2Text ?? "").Append('\t')
              .Append(op.HyText ?? "").Append('\t')
              .Append(op.PxText ?? "").Append('\t')
              .Append(op.PyText ?? "").Append('\t')
              .Append(op.RingsText ?? "").Append('\t')
              .Append(op.ArmsText ?? "").Append('\t')
              .Append(op.GridText ?? "").Append('\t')
              .Append(op.Op1Text ?? "").Append('\t')
              .Append(op.Op2Text ?? "").Append('\t')
              .Append(op.FactorText ?? "").Append('\t')
              .Append(op.ValueText ?? "").Append('\t')
              .Append(op.ContributionText ?? "")
              .AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(MeritValueText))
        {
            sb.AppendLine();
            sb.AppendLine(MeritValueText);
        }

        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (topLevel?.Clipboard is { } clip)
            await clip.SetTextAsync(sb.ToString());
    }

    [RelayCommand]
    public async Task SaveMft()
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Merit Function Table",
            DefaultExtension = "mft",
            SuggestedFileName = "merit",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Merit Function Table") { Patterns = new[] { "*.mft" } }
            }
        });
        if (file == null) return;
        var path = file.TryGetLocalPath();
        if (path == null) return;
        if (!path.EndsWith(MeritFunctionTableIO.FileExtension, StringComparison.OrdinalIgnoreCase))
            path += MeritFunctionTableIO.FileExtension;

        try
        {
            MeritFunctionTableIO.Save(_session.MeritFunction, path);
            MeritValueText = $"Saved: {System.IO.Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            MeritValueText = $"Save error: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task OpenMft()
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Merit Function Table",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Merit Function Table") { Patterns = new[] { "*.mft" } }
            }
        });
        if (files == null || files.Count == 0) return;
        var path = files[0].TryGetLocalPath();
        if (path == null) return;

        try
        {
            int surfaceCount = _session.System?.Surfaces.Count ?? 0;
            var mf = MeritFunctionTableIO.Load(path, surfaceCount);
            _session.MeritFunction.Clear();
            foreach (var op in mf.Operands)
                _session.MeritFunction.AddOperand(op);
            Refresh();
            Evaluate();  // auto-evaluate after load per GUI spec
        }
        catch (Exception ex)
        {
            MeritValueText = $"Open error: {ex.Message}";
        }
    }
}
