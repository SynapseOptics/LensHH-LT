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

    // Composite: a whole-system operand that covers every field and wavelength by
    // construction and takes NOTHING but a weight. It is its own category because every
    // other one turns something on that does not apply — System would offer a wavelength,
    // Macro would offer rings and arms, and the RayIntercept fallback offers a surface and
    // ray coordinates as well. An unlisted operand lands on that fallback, which is how
    // PRMSA came to show a surface, a wavelength and pupil coordinates it ignores.
    private enum Category { RayIntercept, Macro, MacroRect, System, Boundary, SurfaceProperty, Arithmetic, Composite }

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
        OperandType.EFL or OperandType.BFL or OperandType.MAG or OperandType.AMAG
        or OperandType.EXPZ or OperandType.ENPZ
        or OperandType.ENPD or OperandType.EXPD or OperandType.TTRACK
        or OperandType.CFS
        or OperandType.ILL
        or OperandType.DITAN or OperandType.DITHETA
        or OperandType.DITANF or OperandType.DITHETAF or OperandType.LCF
        or OperandType.FCF or OperandType.ASTF
        // Sub-system ABCD matrix element (Surface1, Surface2, Wave) + parameter value
        // (Surface1 = surface, Surface2 = 1-based parameter index).
        or OperandType.A or OperandType.B or OperandType.C or OperandType.D
        or OperandType.DET
        or OperandType.PRMV
        // Seidel aberration contribution over a surface range (Surface1, Surface2 only; no Wave).
        or OperandType.SPHS or OperandType.COMAS or OperandType.ASTGS
        or OperandType.FCS or OperandType.DISTS or OperandType.ACS or OperandType.LCS
        // Third-order totals: whole system, no surface range.
        or OperandType.SPHT or OperandType.COMAT or OperandType.ASTGT
        or OperandType.FCT or OperandType.DISTT or OperandType.ACT or OperandType.LCT
        // Fifth/seventh-order Buchdahl/Rimmer, span (S) and total (T) forms.
        or OperandType.B5S or OperandType.F1S or OperandType.F2S
        or OperandType.M1S or OperandType.M2S or OperandType.M3S
        or OperandType.N1S or OperandType.N2S or OperandType.N3S
        or OperandType.C5S or OperandType.PI5S or OperandType.E5S or OperandType.B7S
        or OperandType.B5T or OperandType.F1T or OperandType.F2T
        or OperandType.M1T or OperandType.M2T or OperandType.M3T
        or OperandType.N1T or OperandType.N2T or OperandType.N3T
        or OperandType.C5T or OperandType.PI5T or OperandType.E5T or OperandType.B7T
        // Paraxial screening: aperture adequacy and the paraxial RMS spot.
        or OperandType.PSD or OperandType.PSDT or OperandType.SDR or OperandType.SDRT
        or OperandType.PRMS
            => Category.System,

        // PRMSA expands into one PRMS per (wavelength, field) using the system's own
        // weights, so a wavelength or a field height on the macro would be ignored — set
        // Wave = 1 and you would still get every wavelength. It takes no surface either,
        // and no rings or arms: Robb's pupil average is closed form, so the sampling is
        // exact rather than something to choose. Weight only.
        OperandType.PRMSA
            => Category.Composite,

        // Boundary
        OperandType.CV or OperandType.CVA or OperandType.CVG
        or OperandType.CT or OperandType.CTA or OperandType.CTG
        or OperandType.ET or OperandType.EA or OperandType.EG
        or OperandType.SD or OperandType.DTRG
        or OperandType.RI or OperandType.RE
        // "Total" (T) boundary operands — same Surface1/Surface2/Min/Max params.
        or OperandType.CTT or OperandType.CTAT or OperandType.CTGT
        or OperandType.ETT or OperandType.EAT or OperandType.EGT
        or OperandType.CVT or OperandType.CVAT or OperandType.CVGT
        or OperandType.SDT or OperandType.DTRGT
        or OperandType.RIT or OperandType.RET
        or OperandType.NDT or OperandType.VDT or OperandType.DPGFT
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

    // A/B/C/D (sub-system matrix element) + PRMV (parameter value): both use Surface1 and
    // Surface2 (for PRMV, Surface2 is the 1-based parameter index). A/B/C/D also use Wave.
    private bool IsAbcdMatrix => _operand.Type is OperandType.A or OperandType.B
        or OperandType.C or OperandType.D or OperandType.DET;
    private bool IsMatrixOrParam => IsAbcdMatrix || _operand.Type == OperandType.PRMV;

    // Aberration-coefficient operands that take a surface range: Surface1 + Surface2
    // only, no Wave. The TOTAL forms (SPHT..LCT, B5T..B7T) deliberately do NOT appear
    // here - they span the whole system, so offering surface inputs would invite the
    // user to set values that are then ignored.
    private bool IsSeidel => _operand.Type is OperandType.SPHS or OperandType.COMAS
        or OperandType.ASTGS or OperandType.FCS or OperandType.DISTS
        or OperandType.ACS or OperandType.LCS
        or OperandType.B5S or OperandType.F1S or OperandType.F2S
        or OperandType.M1S or OperandType.M2S or OperandType.M3S
        or OperandType.N1S or OperandType.N2S or OperandType.N3S
        or OperandType.C5S or OperandType.PI5S or OperandType.E5S or OperandType.B7S
        or OperandType.PSD or OperandType.PSDT or OperandType.SDR or OperandType.SDRT;

    // Relevance flags
    private bool NeedsSurface => GetCategory() is Category.RayIntercept or Category.Boundary or Category.SurfaceProperty
        || IsMatrixOrParam || IsSeidel;
    private bool NeedsSurface2 => GetCategory() is Category.Boundary || IsMatrixOrParam || IsSeidel;
    private bool NeedsWave => ((GetCategory() is Category.RayIntercept or Category.System) && !IsMatrixOrParam && !IsSeidel || IsAbcdMatrix)
        && _operand.Type != OperandType.ILL
        && _operand.Type != OperandType.DITAN
        && _operand.Type != OperandType.DITHETA
        && _operand.Type != OperandType.DITANF
        && _operand.Type != OperandType.DITHETAF
        && _operand.Type != OperandType.LCF
        && _operand.Type != OperandType.CFS
        // TTRACK is a pure sum of surface thicknesses (geometric) — no ray
        // trace, no refractive index, no wavelength dependence. The engine's
        // evaluate_operand_value TTRACK branch never reads `wave_index` or
        // `indices_per_wavelength` (merit_function_eval.h). Hide the column.
        && _operand.Type != OperandType.TTRACK;
    private bool NeedsRayCoords => GetCategory() is Category.RayIntercept;
    private bool NeedsHyOnly => _operand.Type is OperandType.ILL
        or OperandType.DITANF or OperandType.DITHETAF or OperandType.LCF
        or OperandType.FCF or OperandType.ASTF
        or OperandType.PRMS;
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
            // Boundary operands accept the sentinel range -5..-1 in addition
            // to literal [0, maxSurf]: -1 = last refractive, -2 = image,
            // -3 = first surface after stop, -4 = stop, -5 = first surface
            // after OBJ. Other categories (ray intercept, surface property)
            // don't use sentinels.
            bool isBoundary = GetCategory() == Category.Boundary;
            int minV = isBoundary ? -5 : 0;
            if (v < minV || v > maxSurf) return;
            if (isBoundary)
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
            if (_operand.Type == OperandType.PRMV)
            {
                // For PRMV, Surface2 is the 1-based PARAMETER index (not a surface),
                // so it must NOT be clamped to the surface range. Accept any positive
                // index; the evaluator returns 0 for indices the surface type lacks.
                if (v < 1) return;
            }
            else
            {
                int maxSurf = _session.System.Surfaces.Count - 1;
                // Boundary ops accept the sentinel range -5..-1 alongside literal
                // [0, maxSurf] so users can type the auto-tracking placeholders
                // directly into the grid; A/B/C/D use Surface2 as the second
                // surface of the sub-system.
                if (v < -5 || v > maxSurf) return;
            }
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

    /// <summary>
    /// Configuration this operand is evaluated in (advanced edition): "All" (evaluated in the
    /// active configuration only — the default) or a 1-based configuration number. Multi-config
    /// optimization needs the metric present in each configuration, so assign one operand per
    /// config (e.g. a WAVEX on Config 1 and another on Config 2). The merit editor hides this
    /// column in the single-configuration (standard) build.
    /// </summary>
    public string ConfigText
    {
        get => _operand.ConfigurationNo < 0
            ? "All"
            : (_operand.ConfigurationNo + 1).ToString(CultureInfo.InvariantCulture);
        set
        {
            string s = (value ?? "").Trim();
            int cfg;
            if (s.Length == 0 || s.Equals("All", StringComparison.OrdinalIgnoreCase) || s == "0")
                cfg = -1;   // all / active-config
            else if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 1)
                cfg = n - 1;   // 1-based UI → 0-based config index
            else { OnPropertyChanged(); return; }   // reject unparseable; snap back
            if (_operand.ConfigurationNo != cfg) _operand.ConfigurationNo = cfg;
            OnPropertyChanged();
        }
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
        OperandType.PY => "PY — Paraxial MARGINAL ray height at Surface. Traces the marginal ray only: the Hy/Px/Py boxes are ignored. At the image surface, targeting 0 places the image plane at paraxial focus, which is the usual way to pin focus for a coefficient-based merit. Params: Surface, Wave",
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
        OperandType.A => "A — Element A of the paraxial ray-transfer (ABCD) matrix of the sub-system from Surface1 to Surface2, [h';w'] = [[A,B],[C,D]]·[h;w] (w = geometric ray slope). For a single ABCD surface (Surface1 = Surface2) equals its stored A. Use to match a replacement lens group to an ABCD surface. Params: Surface1, Surface2, Wave (optional)",
        OperandType.B => "B — Element B of the sub-system paraxial ABCD matrix from Surface1 to Surface2. For a single ABCD surface (Surface1 = Surface2) equals its stored B. Params: Surface1, Surface2, Wave (optional)",
        OperandType.C => "C — Element C of the sub-system paraxial ABCD matrix from Surface1 to Surface2. For a single ABCD surface (Surface1 = Surface2) equals its stored C. Params: Surface1, Surface2, Wave (optional)",
        OperandType.D => "D — Element D of the sub-system paraxial ABCD matrix from Surface1 to Surface2. For a single ABCD surface (Surface1 = Surface2) equals its stored D. Params: Surface1, Surface2, Wave (optional)",
        OperandType.DET => "DET — Determinant A·D − B·C of the sub-system paraxial ABCD matrix from Surface1 to Surface2. A lossless black box in one medium has determinant 1, so TARGET DET = 1 on an ABCD surface (Surface1 = Surface2 = that surface) to keep its optimized matrix physically realizable. Params: Surface1, Surface2, Wave (optional)",
        OperandType.PRMV => "PRMV — Value of a surface parameter. Surface1 = the surface number. Surface2 = the 1-BASED PARAMETER INDEX (not a surface): ABCD 1–4 = A,B,C,D; Paraxial 1 = focal length; Even Asphere 1..N = the aspheric coefficients. Standard has no parameters. Out-of-range indices return 0. Params: Surface1 (surface), Surface2 (parameter index)",
        OperandType.SPHS => "SPHS — Seidel spherical aberration (S1) summed over the surface range Surface1..Surface2. Same per-surface third-order coefficients the Seidel analysis reports; a full-range sum equals the analysis total. Primary wavelength; NO Wave input. Params: Surface1, Surface2",
        OperandType.COMAS => "COMAS — Seidel coma (S2) summed over the surface range Surface1..Surface2. Matches the Seidel analysis. Primary wavelength; NO Wave input. Params: Surface1, Surface2",
        OperandType.ASTGS => "ASTGS — Seidel astigmatism (S3) summed over the surface range Surface1..Surface2. Matches the Seidel analysis. Primary wavelength; NO Wave input. Params: Surface1, Surface2",
        OperandType.FCS => "FCS — Seidel field curvature / Petzval (S4) summed over the surface range Surface1..Surface2. Matches the Seidel analysis. Primary wavelength; NO Wave input. Params: Surface1, Surface2",
        OperandType.DISTS => "DISTS — Seidel distortion (S5) summed over the surface range Surface1..Surface2. Matches the Seidel analysis. Primary wavelength; NO Wave input. Params: Surface1, Surface2",
        OperandType.ACS => "ACS — Axial (longitudinal) chromatic aberration (CL) summed over the surface range Surface1..Surface2. Uses the system's min/max wavelengths. Matches the Seidel analysis. NO Wave input. Params: Surface1, Surface2",
        OperandType.LCS => "LCS — Lateral (transverse) chromatic aberration (CT) summed over the surface range Surface1..Surface2. Uses the system's min/max wavelengths. Matches the Seidel analysis. NO Wave input. Params: Surface1, Surface2",
        // Third-order TOTALS: same quantities as SPHS..LCS over the whole system.
        OperandType.SPHT => "SPHT — Seidel spherical aberration (S1), summed over the WHOLE system. Same value as the Seidel analysis total and as the matching S operand over its full range; no surface arguments. Primary wavelength (the colour terms use the wavelength spread). NO Wave input. Params: none",
        OperandType.COMAT => "COMAT — Seidel coma (S2), summed over the WHOLE system. Same value as the Seidel analysis total and as the matching S operand over its full range; no surface arguments. Primary wavelength (the colour terms use the wavelength spread). NO Wave input. Params: none",
        OperandType.ASTGT => "ASTGT — Seidel astigmatism (S3), summed over the WHOLE system. Same value as the Seidel analysis total and as the matching S operand over its full range; no surface arguments. Primary wavelength (the colour terms use the wavelength spread). NO Wave input. Params: none",
        OperandType.FCT => "FCT — Seidel field curvature / Petzval (S4), summed over the WHOLE system. Same value as the Seidel analysis total and as the matching S operand over its full range; no surface arguments. Primary wavelength (the colour terms use the wavelength spread). NO Wave input. Params: none",
        OperandType.DISTT => "DISTT — Seidel distortion (S5), summed over the WHOLE system. Same value as the Seidel analysis total and as the matching S operand over its full range; no surface arguments. Primary wavelength (the colour terms use the wavelength spread). NO Wave input. Params: none",
        OperandType.ACT => "ACT — axial (longitudinal) chromatic aberration (CL), summed over the WHOLE system. Same value as the Seidel analysis total and as the matching S operand over its full range; no surface arguments. Primary wavelength (the colour terms use the wavelength spread). NO Wave input. Params: none",
        OperandType.LCT => "LCT — lateral (transverse) chromatic aberration (CT), summed over the WHOLE system. Same value as the Seidel analysis total and as the matching S operand over its full range; no surface arguments. Primary wavelength (the colour terms use the wavelength spread). NO Wave input. Params: none",

        // Fifth/seventh-order Buchdahl/Rimmer coefficients. S = surface span, T = whole
        // system. NOTE: here the trailing S means span, not Seidel - fifth order is not
        // Seidel. Transverse coefficients, scaled by the working F/number.
        OperandType.B5S => "B5S — fifth-order spherical aberration (Buchdahl/Rimmer), summed over the surface range Surface1..Surface2. Each surface's contribution includes its induced interaction with every preceding surface, so a range starting mid-system is well defined but is not a property of those surfaces alone. Primary wavelength. NO Wave input. Params: Surface1, Surface2",
        OperandType.F1S => "F1S — fifth-order coma (first form) (Buchdahl/Rimmer), summed over the surface range Surface1..Surface2. Each surface's contribution includes its induced interaction with every preceding surface, so a range starting mid-system is well defined but is not a property of those surfaces alone. Primary wavelength. NO Wave input. Params: Surface1, Surface2",
        OperandType.F2S => "F2S — fifth-order coma (second form) (Buchdahl/Rimmer), summed over the surface range Surface1..Surface2. Each surface's contribution includes its induced interaction with every preceding surface, so a range starting mid-system is well defined but is not a property of those surfaces alone. Primary wavelength. NO Wave input. Params: Surface1, Surface2",
        OperandType.M1S => "M1S — oblique spherical aberration (first form) (Buchdahl/Rimmer), summed over the surface range Surface1..Surface2. Each surface's contribution includes its induced interaction with every preceding surface, so a range starting mid-system is well defined but is not a property of those surfaces alone. Primary wavelength. NO Wave input. Params: Surface1, Surface2",
        OperandType.M2S => "M2S — oblique spherical aberration (second form) (Buchdahl/Rimmer), summed over the surface range Surface1..Surface2. Each surface's contribution includes its induced interaction with every preceding surface, so a range starting mid-system is well defined but is not a property of those surfaces alone. Primary wavelength. NO Wave input. Params: Surface1, Surface2",
        OperandType.M3S => "M3S — oblique spherical aberration (third form) (Buchdahl/Rimmer), summed over the surface range Surface1..Surface2. Each surface's contribution includes its induced interaction with every preceding surface, so a range starting mid-system is well defined but is not a property of those surfaces alone. Primary wavelength. NO Wave input. Params: Surface1, Surface2",
        OperandType.N1S => "N1S — elliptical coma (first form) (Buchdahl/Rimmer), summed over the surface range Surface1..Surface2. Each surface's contribution includes its induced interaction with every preceding surface, so a range starting mid-system is well defined but is not a property of those surfaces alone. Primary wavelength. NO Wave input. Params: Surface1, Surface2",
        OperandType.N2S => "N2S — elliptical coma (second form) (Buchdahl/Rimmer), summed over the surface range Surface1..Surface2. Each surface's contribution includes its induced interaction with every preceding surface, so a range starting mid-system is well defined but is not a property of those surfaces alone. Primary wavelength. NO Wave input. Params: Surface1, Surface2",
        OperandType.N3S => "N3S — elliptical coma (third form) (Buchdahl/Rimmer), summed over the surface range Surface1..Surface2. Each surface's contribution includes its induced interaction with every preceding surface, so a range starting mid-system is well defined but is not a property of those surfaces alone. Primary wavelength. NO Wave input. Params: Surface1, Surface2",
        OperandType.C5S => "C5S — fifth-order astigmatism (Buchdahl/Rimmer), summed over the surface range Surface1..Surface2. Each surface's contribution includes its induced interaction with every preceding surface, so a range starting mid-system is well defined but is not a property of those surfaces alone. Primary wavelength. NO Wave input. Params: Surface1, Surface2",
        OperandType.PI5S => "PI5S — fifth-order field curvature (Petzval) (Buchdahl/Rimmer), summed over the surface range Surface1..Surface2. Each surface's contribution includes its induced interaction with every preceding surface, so a range starting mid-system is well defined but is not a property of those surfaces alone. Primary wavelength. NO Wave input. Params: Surface1, Surface2",
        OperandType.E5S => "E5S — fifth-order distortion (Buchdahl/Rimmer), summed over the surface range Surface1..Surface2. Each surface's contribution includes its induced interaction with every preceding surface, so a range starting mid-system is well defined but is not a property of those surfaces alone. Primary wavelength. NO Wave input. Params: Surface1, Surface2",
        OperandType.B7S => "B7S — seventh-order spherical aberration (Buchdahl/Rimmer), summed over the surface range Surface1..Surface2. Each surface's contribution includes its induced interaction with every preceding surface, so a range starting mid-system is well defined but is not a property of those surfaces alone. Primary wavelength. NO Wave input. Params: Surface1, Surface2",
        OperandType.B5T => "B5T — fifth-order spherical aberration (Buchdahl/Rimmer) for the WHOLE system. Equals the matching S operand over its full range; no surface arguments. Primary wavelength. NO Wave input. Params: none",
        OperandType.F1T => "F1T — fifth-order coma (first form) (Buchdahl/Rimmer) for the WHOLE system. Equals the matching S operand over its full range; no surface arguments. Primary wavelength. NO Wave input. Params: none",
        OperandType.F2T => "F2T — fifth-order coma (second form) (Buchdahl/Rimmer) for the WHOLE system. Equals the matching S operand over its full range; no surface arguments. Primary wavelength. NO Wave input. Params: none",
        OperandType.M1T => "M1T — oblique spherical aberration (first form) (Buchdahl/Rimmer) for the WHOLE system. Equals the matching S operand over its full range; no surface arguments. Primary wavelength. NO Wave input. Params: none",
        OperandType.M2T => "M2T — oblique spherical aberration (second form) (Buchdahl/Rimmer) for the WHOLE system. Equals the matching S operand over its full range; no surface arguments. Primary wavelength. NO Wave input. Params: none",
        OperandType.M3T => "M3T — oblique spherical aberration (third form) (Buchdahl/Rimmer) for the WHOLE system. Equals the matching S operand over its full range; no surface arguments. Primary wavelength. NO Wave input. Params: none",
        OperandType.N1T => "N1T — elliptical coma (first form) (Buchdahl/Rimmer) for the WHOLE system. Equals the matching S operand over its full range; no surface arguments. Primary wavelength. NO Wave input. Params: none",
        OperandType.N2T => "N2T — elliptical coma (second form) (Buchdahl/Rimmer) for the WHOLE system. Equals the matching S operand over its full range; no surface arguments. Primary wavelength. NO Wave input. Params: none",
        OperandType.N3T => "N3T — elliptical coma (third form) (Buchdahl/Rimmer) for the WHOLE system. Equals the matching S operand over its full range; no surface arguments. Primary wavelength. NO Wave input. Params: none",
        OperandType.C5T => "C5T — fifth-order astigmatism (Buchdahl/Rimmer) for the WHOLE system. Equals the matching S operand over its full range; no surface arguments. Primary wavelength. NO Wave input. Params: none",
        OperandType.PI5T => "PI5T — fifth-order field curvature (Petzval) (Buchdahl/Rimmer) for the WHOLE system. Equals the matching S operand over its full range; no surface arguments. Primary wavelength. NO Wave input. Params: none",
        OperandType.E5T => "E5T — fifth-order distortion (Buchdahl/Rimmer) for the WHOLE system. Equals the matching S operand over its full range; no surface arguments. Primary wavelength. NO Wave input. Params: none",
        OperandType.B7T => "B7T — seventh-order spherical aberration (Buchdahl/Rimmer) for the WHOLE system. Equals the matching S operand over its full range; no surface arguments. Primary wavelength. NO Wave input. Params: none",

        // Paraxial screening operands. No ray tracing: these read the paraxial marginal
        // and chief heights, which the aberration coefficients already compute.
        OperandType.PSD => "PSD — Paraxial required semi-diameter: the largest |y_marginal| + |y_chief| at maximum field over Surface1..Surface2, i.e. the semi-diameter the PARAXIAL beam needs there. Real rays are aberrated and can exceed it, so this is a paraxial requirement rather than a guaranteed clear aperture. Bound it with Maximum to cap element size for packaging and cost, or with Minimum to stop a design collapsing to a beam that passes no light. No ray tracing. Params: Surface1, Surface2",
        OperandType.PSDT => "PSDT — Additive form of PSD: sqrt(sum of SQUARED per-surface bound violations) over Surface1..Surface2, against an implicit target of 0. Every offending surface contributes gradient rather than only the worst one. Params: Surface1, Surface2",
        OperandType.SDR => "SDR — Aperture adequacy: actual semi-diameter divided by the paraxial requirement, worst case over Surface1..Surface2. 1.0 means the aperture exactly accommodates the beam; below 1 the beam is being cut. Bound with Minimum near 1. Its gradient points the right way — the optimizer cannot satisfy it by shrinking apertures, only by making the geometry pass the beam. Under Auto Diameters = Paraxial the ratio is 1 on every AUTO surface, since the semi-diameter is then set to the requirement — but NOT on Fixed surfaces, and not where Clear Aperture % is below 100, so a range covering those still reports real information. Prefer PSD when every surface in the range is Auto. Params: Surface1, Surface2",
        OperandType.SDRT => "SDRT — Additive form of SDR: sqrt(sum of SQUARED per-surface bound violations) over Surface1..Surface2, against an implicit target of 0, so every inadequate surface contributes gradient. Same caveat as SDR: meaningless when Auto Diameters is Paraxial. Params: Surface1, Surface2",
        OperandType.PRMSA => "PRMSA - Paraxial RMS spot across the WHOLE field and spectrum: one operand that expands into a PRMS per (wavelength, field), weighted by the field and wavelength weights already on the system. Identical merit to writing those rows out by hand, but the weights follow the system, and the expansion is wavelength-major so the analytic Jacobian can hold one coefficient pass across a wavelength's fields. TAKES NOTHING BUT A WEIGHT. No wavelength and no field: it covers them all, so either would be ignored. No surface: it is a whole-system quantity. No rings or arms: the pupil average is CLOSED FORM, exact rather than sampled - effectively infinite rings and arms, and its accuracy is set by the truncated series, not by sampling. Inherits every PRMS limitation: defocus and colour are both invisible, so pin focus with PY at the image surface targeted to 0 and use ACT/LCT for colour. Params: Weight",
        OperandType.PRMS => "PRMS — Paraxial RMS spot radius at field Hy, formed from the aberration coefficients instead of traced rays (Robb's analytic merit function, JOSA 66 1037). Covers the full third and fifth order plus seventh-order spherical; measured about the CENTROID, at the paraxial image plane. One scalar in place of many individually weighted coefficient operands. THREE LIMITATIONS, all structural rather than approximations. (1) DEFOCUS IS INVISIBLE: it is referenced to the paraxial image plane, so a design away from focus has a real spot far larger than this reports — constrain focus separately, e.g. PY at the image surface targeted to 0. (2) COLOUR IS INVISIBLE: the coefficients are monochromatic, so each wavelength is measured at ITS OWN focus and about ITS OWN chief ray — neither axial nor lateral colour appears, however many wavelengths you evaluate. Use ACT and LCT for those. (3) Seventh-order FIELD terms are absent (only B7 is computed), so accuracy falls off toward the edge of a wide fast field. Wave selects the wavelength; blank uses the primary. For a spectral spot, add one PRMS per wavelength and set each row's weight — the merit squares and weight-averages them, which is exactly Robb's weighting. Params: Hy, Wave",
        OperandType.BFL => "BFL — Paraxial back focal length: distance from last refractive surface to paraxial focus. For infinite conjugate matches the parallel-ray BFL; for finite conjugate equals the paraxial image distance from the last lens. Params: Wave (optional)",
        OperandType.MAG => "MAG — Paraxial magnification. Params: Wave (optional)",
        OperandType.AMAG => "AMAG — Angular magnification. Params: Wave (optional)",
        OperandType.EXPZ => "EXPZ — Exit pupil Z position (distance from image). Params: Wave (optional)",
        OperandType.ENPZ => "ENPZ — Entrance pupil Z position (distance from surface 1). Params: Wave (optional)",
        OperandType.ENPD => "ENPD — Entrance pupil diameter. Params: Wave (optional)",
        OperandType.EXPD => "EXPD — Exit pupil diameter. Params: Wave (optional)",
        OperandType.TTRACK => "TTRACK — Total track (surface 1 to image). No params.",
        OperandType.CFS => "CFS — Chromatic focal shift. Peak-to-trough range across the system's wavelengths (mm focal / diopters afocal). Same number reported by the Chromatic Focal Shift analysis. No params.",
        OperandType.ILL => "ILL — Relative illumination at field point. RI = (F/#_on-axis / F/#_field)². Uses primary wavelength. Can exceed 1.0. Params: Hy",
        OperandType.DITAN => "DITAN — Maximum F-tan(θ) distortion (%) across all fields. Returns signed value with largest absolute distortion. No params.",
        OperandType.DITHETA => "DITHETA — Maximum F-θ distortion (%) across all fields. Returns signed value with largest absolute distortion. No params.",
        OperandType.DITANF => "DITANF — F-tan(θ) distortion (%) at a specific field point. Params: Hy",
        OperandType.DITHETAF => "DITHETAF — F-θ distortion (%) at a specific field point. Params: Hy",
        OperandType.LCF => "LCF — Lateral color at a specific field point. Max chief ray height spread across all wavelengths (µm or arcmin). Params: Hy",
        OperandType.FCF => "FCF — Field curvature at a field point. Medial focus shift (sag+tan)/2 relative to on-axis primary-wave focus. Zero on-axis at primary by construction. Params: Hy, WaveNo",
        OperandType.ASTF => "ASTF — Astigmatism at a field point. SAG focus − TAN focus. Identically zero on-axis. Params: Hy, WaveNo",

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

        // "Total" (T) boundary operands — sum the Min/Max violation across EVERY qualifying
        // surface in the range (value = √Σ hinge², 0 when all satisfied), so every offending
        // surface drives the optimizer — vs the plain operands, which report only the worst one.
        OperandType.CTT => "CTT — Total center-thickness violation over the range (all). Every out-of-bounds surface contributes. Params: Surf, Surf2, Min, Max",
        OperandType.CTAT => "CTAT — Total center-thickness violation of air spaces in range. Params: Surf, Surf2, Min, Max",
        OperandType.CTGT => "CTGT — Total center-thickness violation of glass elements in range. Params: Surf, Surf2, Min, Max",
        OperandType.ETT => "ETT — Total edge-thickness violation over the range (all). Params: Surf, Surf2, Min, Max",
        OperandType.EAT => "EAT — Total edge-thickness violation of air spaces in range. Params: Surf, Surf2, Min, Max",
        OperandType.EGT => "EGT — Total edge-thickness violation of glass elements in range. Params: Surf, Surf2, Min, Max",
        OperandType.CVT => "CVT — Total curvature violation over the range (all). Params: Surf, Surf2, Min, Max",
        OperandType.CVAT => "CVAT — Total curvature violation of air spaces in range. Params: Surf, Surf2, Min, Max",
        OperandType.CVGT => "CVGT — Total curvature violation of glass surfaces in range. Params: Surf, Surf2, Min, Max",
        OperandType.SDT => "SDT — Total semi-diameter violation of surfaces in range. Params: Surf, Surf2, Min, Max",
        OperandType.DTRGT => "DTRGT — Total diameter-to-thickness-ratio violation of glass elements in range. Params: Surf, Surf2, Min, Max",
        OperandType.RIT => "RIT — Total chief-ray angle-of-incidence violation over the range. Params: Surf, Surf2, Min, Max",
        OperandType.RET => "RET — Total chief-ray angle-of-exitance violation over the range. Params: Surf, Surf2, Min, Max",
        OperandType.NDT => "NDT — Total model-glass index (Nd) violation over model-index surfaces in range. Params: Surf, Surf2, Min, Max",
        OperandType.VDT => "VDT — Total model-glass Abbe (Vd) violation over model-index surfaces in range. Params: Surf, Surf2, Min, Max",
        OperandType.DPGFT => "DPGFT — Total model-glass partial-dispersion (dPgF) violation over model-index surfaces in range. Params: Surf, Surf2, Min, Max",

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
            _session.EnsureSolved();   // canonical accurate-SD + vignetting state (see issue: Evaluate vs Multistart-initial)
            var mf = _session.MeritFunction;
            // Config-aware in the advanced edition: evaluates every configuration (each operand in
            // its assigned config), so the merit is the same regardless of the active config.
            var evaluator = AppExtensions.CreateMeritEvaluator(_session.System, _session.GlassCatalog);
            evaluator.ParallelEvaluation = true;
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
