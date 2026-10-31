using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LensHH.App.Session;
using LensHH.Core.Activation;

namespace LensHH.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly GuiSession _session;

    [ObservableProperty] private string _windowTitle = "LensHH-LT";
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private int _selectedTabIndex = 0;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasUnresolvedGlass;

    /// <summary>The directions across the field the analyses and layout can show, as <see cref="LensHH.Tolerancing.FieldView.All"/>.</summary>
    public string[] FieldDirectionNames { get; } = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(LensHH.Tolerancing.FieldView.All, v => v.Name));

    /// <summary>
    /// The direction the analyses and the layout show (<see cref="GuiSession.FieldDirection"/>):
    /// 0 = +y, the lens's own fields; 1 = +x, 2 = −y, 3 = −x, the lens turned about its axis.
    /// </summary>
    [ObservableProperty] private int _fieldDirectionIndex;

    public bool IsFieldPlusY => FieldDirectionIndex == 0;
    public bool IsFieldPlusX => FieldDirectionIndex == 1;
    public bool IsFieldMinusY => FieldDirectionIndex == 2;
    public bool IsFieldMinusX => FieldDirectionIndex == 3;

    /// <summary>
    /// The field directions are offered only for a lens that differs from one direction to
    /// another - decentred, tilted or irregular, as a Monte Carlo trial is. A centred lens is the
    /// same in all four, and +y shows it.
    /// </summary>
    public bool ShowFieldDirections => !_session.IsRotationallySymmetric;

    /// <summary>True while the analyses recompute for a new direction: the selector waits for them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseFieldDirection))]
    [NotifyCanExecuteChangedFor(nameof(SetFieldDirectionCommand))]
    private bool _isChangingFieldDirection;

    /// <summary>A direction can be chosen when no analysis is being computed.</summary>
    public bool CanChooseFieldDirection => !IsChangingFieldDirection && !IsBusy && !AnyAnalysisBusy;

    private readonly List<(System.ComponentModel.INotifyPropertyChanged Analysis, System.Reflection.PropertyInfo IsBusy)> _analyses = new();

    /// <summary>Whether any analysis is computing, however it was started: its own tab, a button, F5.</summary>
    private bool AnyAnalysisBusy => _analyses.Any(a => a.IsBusy.GetValue(a.Analysis) is true);

    /// <summary>Follows every analysis's IsBusy, so the field direction waits for whichever is computing.</summary>
    private void WatchAnalyses(params object[] analyses)
    {
        foreach (var a in analyses)
        {
            if (a is not System.ComponentModel.INotifyPropertyChanged notifier
                || a.GetType().GetProperty("IsBusy") is not { PropertyType: var t } busy || t != typeof(bool)) continue;
            _analyses.Add((notifier, busy));
            notifier.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != "IsBusy") return;
                OnPropertyChanged(nameof(CanChooseFieldDirection));
                SetFieldDirectionCommand.NotifyCanExecuteChanged();
            };
        }
    }

    [RelayCommand(CanExecute = nameof(CanChooseFieldDirection))]
    private void SetFieldDirection(string index) => FieldDirectionIndex = int.Parse(index, System.Globalization.CultureInfo.InvariantCulture);

    partial void OnFieldDirectionIndexChanged(int value)
    {
        var all = LensHH.Tolerancing.FieldView.All;
        _session.FieldDirection = all[Math.Clamp(value, 0, all.Count - 1)];
        OnPropertyChanged(nameof(IsFieldPlusY));
        OnPropertyChanged(nameof(IsFieldPlusX));
        OnPropertyChanged(nameof(IsFieldMinusY));
        OnPropertyChanged(nameof(IsFieldMinusX));
        if (!_quietDirection) _ = ShowFieldDirection();
    }

    private bool _quietDirection;

    /// <summary>Redraws the layout and recomputes the analyses for the new direction, one change at a time.</summary>
    private async Task ShowFieldDirection()
    {
        IsChangingFieldDirection = true;
        try
        {
            StatusText = $"Showing the fields at {_session.FieldDirection.Name}...";
            Layout.Render();
            await UpdateAllAnalyses();
            StatusText = FieldDirectionIndex == 0 ? "Analyses show the fields at +y"
                : $"Analyses show the fields at {_session.FieldDirection.Name}: the lens turned {_session.FieldDirection.Turn:G3}° about its axis";
        }
        finally
        {
            IsChangingFieldDirection = false;
        }
    }

    /// <summary>
    /// The lens has no Coordinate Break that decentres or tilts. Optimization and tolerancing are
    /// for such a lens - the design as drawn - and their menus are disabled for any other, a Monte
    /// Carlo trial among them (the optimizers refuse one too).
    /// </summary>
    public bool IsCentred => !_session.HasSystem || _session.System.FirstTiltedOrDecentredBreak() == 0;

    /// <summary>
    /// The lens has no Coordinate Break, Zernike departure or glass offset, so the Code V, OSLO
    /// and Optalix exports, which hold none of them, are offered.
    /// </summary>
    public bool IsPlainCentred => _session.IsPlainCentred;

    /// <summary>A lens the same in every direction goes back to +y, quietly: its analyses are recomputed for the change anyway.</summary>
    private void RefreshFieldDirections()
    {
        OnPropertyChanged(nameof(ShowFieldDirections));
        OnPropertyChanged(nameof(IsCentred));
        OnPropertyChanged(nameof(IsPlainCentred));
        if (!ShowFieldDirections && FieldDirectionIndex != 0)
        {
            _quietDirection = true;
            try { FieldDirectionIndex = 0; }
            finally { _quietDirection = false; }
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanChooseFieldDirection));
        SetFieldDirectionCommand.NotifyCanExecuteChanged();
    }

    // ── License menu visibility ────────────────────────────────────
    // Visibility derived from ActivationManager.IsActivated (any token
    // active — trial or paid) and TrialClock.IsTrialActive (trial timer
    // running). MenuItems bind their IsVisible / IsEnabled to these
    // properties; call RefreshLicenseMenuState() after any operation
    // that may change activation state.

    /// <summary>"Start Free Trial" visible only when no token is active
    /// (neither a paid license nor a running trial).</summary>
    [ObservableProperty] private bool _startTrialVisible = true;

    /// <summary>"Deactivate License" enabled only when a paid license is
    /// currently active on this machine. Always visible so users can find
    /// it greyed out during trial.</summary>
    [ObservableProperty] private bool _deactivateLicenseEnabled = false;

    /// <summary>
    /// Recompute license menu visibility/enabled state from the engine.
    /// Call after StartTrial / Activate / Deactivate succeeds.
    ///
    /// "Activate License" is intentionally always visible (no property
    /// binding required) — a fresh customer with a purchased key must be
    /// able to activate without first starting a trial; trial users use
    /// it to upgrade; paid users use it to re-activate / refresh.
    /// </summary>
    public void RefreshLicenseMenuState()
    {
        bool isActivated = ActivationManager.IsActivated;
        bool isTrial = TrialClock.IsTrialActive;
        bool isPaid = isActivated && !isTrial;

        // The self-service trial start is offered only when the host supports it (default true).
        // A token-file-only host sets AppCapabilities.TrialStartSupported = false, so the menu
        // item never appears and that activation path is unreachable.
        StartTrialVisible = !isActivated && AppCapabilities.TrialStartSupported;
        DeactivateLicenseEnabled = isPaid;
    }

    public SurfaceEditorViewModel SurfaceEditor { get; }
    public LayoutViewModel Layout { get; }
    public MeritFunctionEditorViewModel MeritFunctionEditor { get; }
    public FftMtfViewModel FftMtf { get; }
    public FftMtfVsFieldViewModel FftMtfVsField { get; }
    public FftMtfVsFocusViewModel FftMtfVsFocus { get; }
    public FftPsfViewModel FftPsf { get; }
    public SpotDiagramViewModel SpotDiagram { get; }
    public DistortionViewModel Distortion { get; }
    public FieldCurvatureViewModel FieldCurvature { get; }
    public RelativeIlluminationViewModel RelativeIllumination { get; }
    public SeidelViewModel Seidel { get; }
    public GeoMtfVsFreqViewModel GeoMtfVsFreq { get; }
    public GeoMtfVsFieldViewModel GeoMtfVsField { get; }
    public GeoMtfVsFocusViewModel GeoMtfVsFocus { get; }
    public TransverseRayFanViewModel TransverseRayFan { get; }
    public OpdFanViewModel OpdFan { get; }
    public WavefrontMapViewModel WavefrontMap { get; }
    public ZernikeViewModel Zernike { get; }
    public PupilAberrationFanViewModel PupilAberrationFan { get; }
    public LateralColorViewModel LateralColor { get; }
    public ChromaticFocalShiftViewModel ChromaticFocalShift { get; }
    public LongitudinalAberrationViewModel LongitudinalAberration { get; }
    public SingleRayTraceViewModel SingleRayTrace { get; }
    public SystemDataViewModel SystemData { get; }
    public GuiSession Session => _session;

    /// <summary>True when the system has automatic vignetting factors enabled. Drives the visibility
    /// of every per-analysis "Use Vignetting Factors" checkbox (their IsVisible binds here, since the
    /// analysis TabItems' DataContext is this MainViewModel). Refreshed on system change and after the
    /// System editor is applied (see RefreshVignettingOption).</summary>
    public bool ShowVignettingOption => _session.System?.UseAutomaticVignettingFactors ?? false;

    /// <summary>Re-evaluate <see cref="ShowVignettingOption"/> — call after the System editor may have
    /// toggled the flag so the per-analysis checkboxes show/hide immediately.</summary>
    public void RefreshVignettingOption() => OnPropertyChanged(nameof(ShowVignettingOption));

    public MainViewModel(GuiSession session)
    {
        _session = session;
        SurfaceEditor = new SurfaceEditorViewModel(session);
        Layout = new LayoutViewModel(session);
        MeritFunctionEditor = new MeritFunctionEditorViewModel(session);
        FftMtf = new FftMtfViewModel(session);
        FftMtfVsField = new FftMtfVsFieldViewModel(session);
        FftMtfVsFocus = new FftMtfVsFocusViewModel(session);
        FftPsf = new FftPsfViewModel(session);
        SpotDiagram = new SpotDiagramViewModel(session);
        Distortion = new DistortionViewModel(session);
        FieldCurvature = new FieldCurvatureViewModel(session);
        RelativeIllumination = new RelativeIlluminationViewModel(session);
        Seidel = new SeidelViewModel(session);
        GeoMtfVsFreq = new GeoMtfVsFreqViewModel(session);
        GeoMtfVsField = new GeoMtfVsFieldViewModel(session);
        GeoMtfVsFocus = new GeoMtfVsFocusViewModel(session);
        TransverseRayFan = new TransverseRayFanViewModel(session);
        OpdFan = new OpdFanViewModel(session);
        WavefrontMap = new WavefrontMapViewModel(session);
        Zernike = new ZernikeViewModel(session);
        PupilAberrationFan = new PupilAberrationFanViewModel(session);
        LateralColor = new LateralColorViewModel(session);
        ChromaticFocalShift = new ChromaticFocalShiftViewModel(session);
        LongitudinalAberration = new LongitudinalAberrationViewModel(session);
        SingleRayTrace = new SingleRayTraceViewModel(session);
        SystemData = new SystemDataViewModel(session);
        WatchAnalyses(FftMtf, FftMtfVsField, FftMtfVsFocus, FftPsf, SpotDiagram, Distortion, FieldCurvature,
                      RelativeIllumination, Seidel, GeoMtfVsFreq, GeoMtfVsField, GeoMtfVsFocus, TransverseRayFan,
                      OpdFan, WavefrontMap, Zernike, PupilAberrationFan, LateralColor, ChromaticFocalShift,
                      LongitudinalAberration, SingleRayTrace, SystemData, Layout);
        _session.SystemChanged += _ =>
        {
            WindowTitle = _session.WindowTitle;
            HasUnresolvedGlass = _session.CannotCompute;
            if (_session.CannotCompute)
                StatusText = _session.CannotComputeMessage;
            OnPropertyChanged(nameof(ShowVignettingOption));
            RefreshFieldDirections();
        };
        RefreshFieldDirections();
        _session.FileStateChanged += () => WindowTitle = _session.WindowTitle;
        WindowTitle = _session.WindowTitle;

        // Initial license menu state.
        RefreshLicenseMenuState();
    }

    [RelayCommand]
    public void NewSystem()
    {
        _session.NewSystem();
        ClearAllAnalyses();
        SelectedTabIndex = 0;
        StatusText = "New system created.";
    }

    public void OpenFile(string path, string format = "lhlt")
    {
        try
        {
            _session.OpenFile(path, format);
            ClearAllAnalyses();
            if (_session.CannotCompute)
            {
                // Leave the user on the Lens Editor so they can fix the
                // problem (typically a missing glass). The 2D Layout tab
                // stays available and will auto-render once the system
                // becomes valid.
                SelectedTabIndex = 0;
                StatusText = _session.CannotComputeMessage;
            }
            else
            {
                SelectedTabIndex = 0; // briefly switch away...
                SelectedTabIndex = 1; // ...then back to force SelectionChanged
                Layout.RenderIfDirty();
                StatusText = $"Opened: {path}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    /// <summary>Clear all analysis images so stale results are not shown.</summary>
    public void ClearAllAnalyses()
    {
        SpotDiagram.SpotImage = null;
        FftMtf.MtfImage = null;
        FftMtfVsField.MtfImage = null;
        FftMtfVsFocus.PlotImage = null;
        GeoMtfVsFreq.PlotImage = null;
        GeoMtfVsField.PlotImage = null;
        GeoMtfVsFocus.PlotImage = null;
        TransverseRayFan.RayFanImage = null;
        OpdFan.OpdFanImage = null;
        PupilAberrationFan.PlotImage = null;
        WavefrontMap.WavefrontImage = null;
        Zernike.Clear();
        Seidel.PlotImage = null;
        FieldCurvature.PlotImage = null;
        Distortion.PlotImage = null;
        RelativeIllumination.PlotImage = null;
        LateralColor.PlotImage = null;
        ChromaticFocalShift.PlotImage = null;
        FftPsf.PsfImage = null;
        SystemData.PlotImage = null;
    }

    /// <summary>Re-compute all currently visible analysis tabs.</summary>
    [RelayCommand]
    public async Task UpdateAllAnalyses()
    {
        if (HasUnresolvedGlass)
        {
            ClearAllAnalyses();
            StatusText = _session.CannotComputeMessage;
            return;
        }

        IsBusy = true;
        try
        {
            StatusText = "Updating all analyses...";
            if (SpotDiagram.IsVisible) await SpotDiagram.ComputeCommand.ExecuteAsync(null);
            if (FftMtf.IsVisible) await FftMtf.ComputeCommand.ExecuteAsync(null);
            if (FftMtfVsField.IsVisible) await FftMtfVsField.ComputeCommand.ExecuteAsync(null);
            if (FftMtfVsFocus.IsVisible) await FftMtfVsFocus.ComputeCommand.ExecuteAsync(null);
            if (GeoMtfVsFreq.IsVisible) await GeoMtfVsFreq.ComputeCommand.ExecuteAsync(null);
            if (GeoMtfVsField.IsVisible) await GeoMtfVsField.ComputeCommand.ExecuteAsync(null);
            if (GeoMtfVsFocus.IsVisible) await GeoMtfVsFocus.ComputeCommand.ExecuteAsync(null);
            if (TransverseRayFan.IsVisible) await TransverseRayFan.ComputeCommand.ExecuteAsync(null);
            if (OpdFan.IsVisible) await OpdFan.ComputeCommand.ExecuteAsync(null);
            if (PupilAberrationFan.IsVisible) await PupilAberrationFan.ComputeCommand.ExecuteAsync(null);
            if (WavefrontMap.IsVisible) await WavefrontMap.ComputeCommand.ExecuteAsync(null);
            if (Zernike.IsVisible) await Zernike.ComputeCommand.ExecuteAsync(null);
            if (Seidel.IsVisible) await Seidel.ComputeCommand.ExecuteAsync(null);
            if (FieldCurvature.IsVisible) await FieldCurvature.ComputeCommand.ExecuteAsync(null);
            if (Distortion.IsVisible) await Distortion.ComputeCommand.ExecuteAsync(null);
            if (RelativeIllumination.IsVisible) await RelativeIllumination.ComputeCommand.ExecuteAsync(null);
            if (LateralColor.IsVisible) await LateralColor.ComputeCommand.ExecuteAsync(null);
            if (ChromaticFocalShift.IsVisible) await ChromaticFocalShift.ComputeCommand.ExecuteAsync(null);
            if (FftPsf.IsVisible) await FftPsf.ComputeCommand.ExecuteAsync(null);
            if (SystemData.IsVisible) await SystemData.ComputeCommand.ExecuteAsync(null);
            StatusText = "All analyses updated.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SaveFile(string path, string format = "lhlt")
    {
        try
        {
            _session.SaveFile(path, format);
            StatusText = $"Saved: {path}";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    public void ShowMeritFunction()
    {
        MeritFunctionEditor.Refresh();
        // Collection order: 0=LensEditor, 1=2DLayout, 2=MeritFunction
        // SelectedIndex uses collection index, not visible index
        SelectedTabIndex = 2;
    }
}
