using System;
using System.IO;
using System.Text.Json;

namespace LensHH.App.Session;

/// <summary>
/// Tiny app-global preference store. Backed by a JSON file in
/// %LOCALAPPDATA%\SynapseOptics\LensHH-LT\preferences.json. Loaded
/// once at startup; saved on every Set. Currently no preferences are
/// persisted — kept as scaffolding for future user-tunable settings.
/// </summary>
public static class AppPreferences
{
    private sealed class PrefData
    {
        // GPU acceleration — three INDEPENDENT mechanisms (see feedback_gpu_settings_ux).
        // All default false. Local Optimization never uses the GPU regardless.
        public bool GpuImageQuality { get; set; }   // dense-grid merit-VALUE trace
        public bool GpuPreScreen { get; set; }       // Multistart candidate sieve
        public bool GpuResidentDe { get; set; }      // GPU-resident Differential Evolution
    }

    // ── GPU settings (persisted on set) ──────────────────────────────────────
    /// <summary>Use the GPU dense-grid merit-value trace for all optimizers EXCEPT
    /// Local Optimization. Global; the image-quality accelerator.</summary>
    public static bool GpuImageQuality
    {
        get => _data.GpuImageQuality;
        set { if (_data.GpuImageQuality != value) { _data.GpuImageQuality = value; Save(); } }
    }

    /// <summary>Use the GPU candidate-design pre-screen in Multistart.</summary>
    public static bool GpuPreScreen
    {
        get => _data.GpuPreScreen;
        set { if (_data.GpuPreScreen != value) { _data.GpuPreScreen = value; Save(); } }
    }

    /// <summary>Run Differential Evolution resident on the GPU (population on device).</summary>
    public static bool GpuResidentDe
    {
        get => _data.GpuResidentDe;
        set { if (_data.GpuResidentDe != value) { _data.GpuResidentDe = value; Save(); } }
    }

    private static readonly string PrefDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SynapseOptics", "LensHH-LT");
    private static readonly string PrefFile = Path.Combine(PrefDir, "preferences.json");

    private static PrefData _data = new();

    /// <summary>
    /// Load preferences from disk. Call once at app startup.
    /// </summary>
    public static void Load()
    {
        try
        {
            if (File.Exists(PrefFile))
            {
                string json = File.ReadAllText(PrefFile);
                var loaded = JsonSerializer.Deserialize<PrefData>(json);
                if (loaded != null) _data = loaded;
            }
        }
        catch
        {
            // Corrupt file or no permission — keep defaults, don't crash.
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(PrefDir);
            string json = JsonSerializer.Serialize(_data,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(PrefFile, json);
        }
        catch
        {
            // Best-effort persistence; the in-memory toggle is still set.
        }
    }
}
