using System;
using System.Runtime.InteropServices;

namespace LensHH.IO;

/// <summary>
/// Opts the current process out of Windows 11 "power throttling" (EcoQoS).
///
/// By default Windows 11 throttles the threads of a process that is not the
/// foreground app — parking them on efficiency cores and/or capping their clock
/// to save power. For a CPU-bound parallel optimization (DE / Multistart / local)
/// this makes the run crawl:
///   * the GUI slows the moment you switch to another app;
///   * a HEADLESS process (the MCP server, the render pipe server, a CLI job in a
///     background terminal) is never the foreground app, so it can be throttled
///     for its ENTIRE lifetime.
/// Opting out keeps compute at full speed regardless of focus (the behaviour
/// OpticStudio and other compute tools rely on).
///
/// Harmless when idle: power throttling only affects threads that are actually
/// running, so opting out costs nothing when there is no work. Windows-only and
/// best-effort — a silent no-op on other platforms or on Windows builds without
/// the API. Call once at process startup. (Targets netstandard2.0, so it uses
/// RuntimeInformation rather than OperatingSystem.IsWindows.)
/// </summary>
public static class WindowsPerformance
{
    // PROCESS_POWER_THROTTLING_STATE (processthreadsapi.h)
    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    private const int ProcessPowerThrottling = 4; // PROCESS_INFORMATION_CLASS
    private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
    private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessInformation(
        IntPtr hProcess, int processInformationClass,
        ref PROCESS_POWER_THROTTLING_STATE processInformation, int processInformationSize);

    /// <summary>
    /// Disable execution-speed throttling for this process so background /
    /// non-foreground compute keeps running at full speed. No-op off Windows.
    /// </summary>
    public static void DisablePowerThrottling()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        var state = new PROCESS_POWER_THROTTLING_STATE
        {
            Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
            // Take control of EXECUTION_SPEED and set its state bit OFF, i.e.
            // "never EcoQoS-throttle this process's execution speed".
            ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
            StateMask = 0,
        };
        try
        {
            SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling,
                ref state, Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
        }
        catch
        {
            // API/class unavailable (pre-1809) — leave default behaviour.
        }
    }
}
