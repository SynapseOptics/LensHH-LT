using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using LensHH.Core.IO;
using LensHH.Core.Models;

namespace LensHH.Mcp;

/// <summary>
/// DTO for sending render requests to the RenderApp via named pipe.
/// </summary>
public class RenderRequest
{
    public string Analysis { get; set; } = "";
    public LhltFile System { get; set; } = new();
    public Dictionary<string, object>? Params { get; set; }
}

/// <summary>
/// DTO for receiving render responses from the RenderApp.
/// </summary>
public class RenderResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Client for sending render commands to the LensHH.RenderApp via named pipe.
/// Auto-launches the RenderApp if it is not running.
/// </summary>
public static class RenderAppClient
{
    private const string PipeName = "LensHH-RenderApp";
    // Apphost is "LensHH.RenderApp.exe" on Windows but extension-less on
    // macOS/Linux — a hardcoded ".exe" makes auto-launch fail on Unix.
    private static readonly string ExeName =
        OperatingSystem.IsWindows() ? "LensHH.RenderApp.exe" : "LensHH.RenderApp";
    private const int ConnectTimeoutMs = 5000;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Send a render request to the RenderApp. Auto-launches if not running.
    /// </summary>
    public static async Task<RenderResponse> SendAsync(
        OpticalSystem system,
        string analysis,
        Dictionary<string, object>? parms = null)
    {
        try
        {
            // Serialize system to LhltFile DTO
            var lhltFile = LhltWriter.ToLhltFile(system);

            var request = new RenderRequest
            {
                Analysis = analysis,
                System = lhltFile,
                Params = parms
            };

            EnsureRunning();

            using var pipe = new NamedPipeClientStream(".", PipeName,
                PipeDirection.InOut, PipeOptions.Asynchronous);

            // Retry connection: RenderApp may still be starting up
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    await pipe.ConnectAsync(ConnectTimeoutMs);
                    break;
                }
                catch (TimeoutException) when (attempt < 2)
                {
                    await Task.Delay(1000);
                }
            }

            if (!pipe.IsConnected)
                return new RenderResponse { Success = false, Error = "Could not connect to RenderApp." };

            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);

            var json = JsonSerializer.Serialize(request, JsonOpts);
            await writer.WriteLineAsync(json);

            var responseLine = await reader.ReadLineAsync();
            if (responseLine == null)
                return new RenderResponse { Success = false, Error = "No response from RenderApp." };

            return JsonSerializer.Deserialize<RenderResponse>(responseLine, JsonOpts)
                ?? new RenderResponse { Success = false, Error = "Failed to parse response." };
        }
        catch (Exception ex)
        {
            return new RenderResponse { Success = false, Error = $"[Client] {ex.GetType().Name}: {ex.Message}" };
        }
    }

    private static void EnsureRunning()
    {
        if (Process.GetProcessesByName("LensHH.RenderApp").Length > 0)
            return;

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, ExeName),
            // Installed layout: MCP at {app}\mcp\, RenderApp at {app}\renderapp\
            Path.Combine(baseDir, "..", "renderapp", ExeName),
            Path.Combine(baseDir, "..", "LensHH.RenderApp", ExeName),
            // Dev layout: MCP bin/Debug/net8.0 -> src/LensHH.Mcp -> src -> LensHH.RenderApp/bin/Debug/net8.0
            Path.Combine(baseDir, "..", "..", "..", "..", "LensHH.RenderApp",
                "bin", "Debug", "net8.0", ExeName),
            Path.Combine(baseDir, "..", "..", "..", "..", "LensHH.RenderApp",
                "bin", "Release", "net8.0", ExeName),
        };

        var exePath = candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
        if (exePath == null)
            throw new FileNotFoundException(
                $"Cannot find {ExeName}. Build the LensHH.RenderApp project first.");

        Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal
        });

        // Give it time to start and begin listening on the pipe
        Thread.Sleep(2000);
    }
}
