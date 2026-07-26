using CommunityToolkit.Mvvm.ComponentModel;
using LensHH.App.Session;

namespace LensHH.App.ViewModels;

/// <summary>
/// Application preferences. Currently the GPU-acceleration settings: three
/// INDEPENDENT mechanisms (see feedback_gpu_settings_ux). Applied globally to
/// every optimizer EXCEPT Local Optimization (which stays CPU-parallel). Seeded
/// from the persisted <see cref="AppPreferences"/>; persisted on OK.
/// </summary>
public partial class PreferencesDialogViewModel : ObservableObject
{
    // GPU image-quality = dense-grid merit-VALUE trace; pre-screen = Multistart
    // candidate sieve; resident-DE = DE population on device. All distinct.
    [ObservableProperty] private bool _gpuImageQuality;
    [ObservableProperty] private bool _gpuPreScreen;
    [ObservableProperty] private bool _gpuResidentDe;

    /// <summary>True when a CUDA device is usable — gates the GPU toggles.</summary>
    public bool GpuAvailable => LensHH.Core.NativeInterop.GpuGridTracer.IsAvailable;

    /// <summary>One-line device status shown under the toggles.</summary>
    public string GpuStatusText => GpuAvailable
        ? "CUDA device detected — GPU acceleration available."
        : "No CUDA device detected — GPU options unavailable on this machine.";

    /// <summary>Set true in OK_Click; persisted state only changes when accepted.</summary>
    public bool Accepted { get; set; }

    public PreferencesDialogViewModel()
    {
        _gpuImageQuality = AppPreferences.GpuImageQuality;
        _gpuPreScreen    = AppPreferences.GpuPreScreen;
        _gpuResidentDe   = AppPreferences.GpuResidentDe;
    }

    /// <summary>Persist the settings. Called from OK.</summary>
    public void Save()
    {
        AppPreferences.GpuImageQuality = GpuImageQuality;
        AppPreferences.GpuPreScreen    = GpuPreScreen;
        AppPreferences.GpuResidentDe   = GpuResidentDe;
    }
}
