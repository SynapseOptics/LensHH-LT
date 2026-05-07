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
