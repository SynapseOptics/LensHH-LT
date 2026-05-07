using System;
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

        StartTrialVisible = !isActivated;
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
    public PupilAberrationFanViewModel PupilAberrationFan { get; }
    public LateralColorViewModel LateralColor { get; }
    public ChromaticFocalShiftViewModel ChromaticFocalShift { get; }
    public SingleRayTraceViewModel SingleRayTrace { get; }
    public SystemDataViewModel SystemData { get; }
    public GuiSession Session => _session;

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
        PupilAberrationFan = new PupilAberrationFanViewModel(session);
        LateralColor = new LateralColorViewModel(session);
        ChromaticFocalShift = new ChromaticFocalShiftViewModel(session);
        SingleRayTrace = new SingleRayTraceViewModel(session);
        SystemData = new SystemDataViewModel(session);
        _session.SystemChanged += _ =>
        {
            WindowTitle = _session.WindowTitle;
            HasUnresolvedGlass = _session.CannotCompute;
            if (_session.CannotCompute)
                StatusText = _session.CannotComputeMessage;
        };
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
