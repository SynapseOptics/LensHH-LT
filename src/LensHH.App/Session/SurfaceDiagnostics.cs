using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using LensHH.Core.Models;

namespace LensHH.App.Session;

/// <summary>
/// Diagnostic logger for tracking the "phantom Surface 1 below IMG plane" bug.
///
/// Writes timestamped entries to %LOCALAPPDATA%\SynapseOptics\LensHH-LT\diag.log.
/// Hooks at every entrypoint that mutates system.Surfaces or fires SystemChanged,
/// plus the 2D-layout render path. After each event, runs an invariant check —
/// if the system has anything other than exactly one IsStop=true surface, a
/// stack trace is captured so we can pin down the offending caller.
///
/// Disable by setting Enabled = false at startup (or just remove the calls).
/// </summary>
public static class SurfaceDiagnostics
{
    public static bool Enabled { get; set; } = true;

    private static readonly object _lock = new();
    private static readonly string _logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SynapseOptics", "LensHH-LT", "diag.log");

    private static bool _initialized;

    private static void EnsureInit()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            // Truncate previous run for clarity.
            File.WriteAllText(_logPath,
                $"=== Diag log started {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ===" +
                Environment.NewLine);
        }
        catch
        {
            // Best-effort. If logging fails we silently skip.
        }
    }

    /// <summary>
    /// Log an event with the system's current surface state.
    /// </summary>
    public static void Log(string eventName, OpticalSystem? system, string? extra = null)
    {
        if (!Enabled) return;

        try
        {
            EnsureInit();
            lock (_lock)
            {
                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("HH:mm:ss.fff"));
                sb.Append("  ");
                sb.Append(eventName);
                if (!string.IsNullOrEmpty(extra))
                {
                    sb.Append("  [");
                    sb.Append(extra);
                    sb.Append(']');
                }

                if (system == null)
                {
                    sb.AppendLine("   (no system)");
                    File.AppendAllText(_logPath, sb.ToString());
                    return;
                }

                int stopCount = system.Surfaces.Count(s => s.IsStop);
                int total = system.Surfaces.Count;
                sb.Append($"   surfaces={total}  IsStop=true count={stopCount}");

                // Invariant: exactly one stop. If not, dump full state + stack.
                bool invariantBroken = stopCount != 1;
                if (invariantBroken)
                    sb.Append("  *** INVARIANT BROKEN ***");
                sb.AppendLine();

                // Always dump the surface fingerprint table on every event so we
                // can compare snapshots side-by-side and see exactly when a row
                // appeared or moved.
                sb.AppendLine("    pos | s.Index | IsStop |    Radius |  Thickness | Material | SemiDia");
                for (int i = 0; i < system.Surfaces.Count; i++)
                {
                    var s = system.Surfaces[i];
                    string radius = double.IsPositiveInfinity(s.Radius) ? "Infinity" : s.Radius.ToString("F4");
                    string thick = double.IsPositiveInfinity(s.Thickness) ? "Infinity" : s.Thickness.ToString("F4");
                    sb.AppendLine($"     {i,3} | {s.Index,7} | {(s.IsStop ? "  Y   " : "      ")} | {radius,9} | {thick,10} | {s.Material,-8} | {s.SemiDiameter:F4}");
                }

                if (invariantBroken)
                {
                    // Trim noise — skip frames inside this class.
                    var stack = new StackTrace(skipFrames: 1, fNeedFileInfo: true);
                    sb.AppendLine("    Stack trace:");
                    foreach (var frame in stack.GetFrames() ?? Array.Empty<StackFrame>())
                    {
                        var m = frame.GetMethod();
                        if (m == null) continue;
                        // Skip framework noise.
                        var typeName = m.DeclaringType?.FullName ?? "";
                        if (typeName.StartsWith("System.") || typeName.StartsWith("Microsoft.") ||
                            typeName.StartsWith("Avalonia.") || typeName.StartsWith("CommunityToolkit."))
                            continue;
                        string file = frame.GetFileName() ?? "";
                        int line = frame.GetFileLineNumber();
                        sb.AppendLine($"      {typeName}.{m.Name}  ({Path.GetFileName(file)}:{line})");
                    }
                }

                File.AppendAllText(_logPath, sb.ToString());
            }
        }
        catch
        {
            // Logger must never crash the app.
        }
    }
}
