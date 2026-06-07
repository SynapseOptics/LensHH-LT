using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using LensHH.Core.IO;
using LensHH.Core.Models;

namespace LensHH.CLI;

/// <summary>
/// Lightweight client for sending render commands to the LensHH.RenderApp via named pipe.
/// Mirrors the MCP RenderAppClient but lives in the CLI project to avoid cross-references.
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

    public class RenderRequest
    {
        public string Analysis { get; set; } = "";
        public LhltFile System { get; set; } = new();
        public Dictionary<string, object>? Params { get; set; }
    }

    public class RenderResponse
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// Send a render request to the RenderApp with an optional SavePngPath parameter.
    /// Auto-launches the RenderApp if not running.
    /// </summary>
    public static RenderResponse Send(
        OpticalSystem system,
        string analysis,
        Dictionary<string, object>? parms = null)
    {
        try
        {
            var lhltFile = LhltWriter.ToLhltFile(system);

            var request = new RenderRequest
            {
                Analysis = analysis,
                System = lhltFile,
                Params = parms
            };

            EnsureRunning();

            using var pipe = new NamedPipeClientStream(".", PipeName,
                PipeDirection.InOut, PipeOptions.None);

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    pipe.Connect(ConnectTimeoutMs);
                    break;
                }
                catch (TimeoutException) when (attempt < 2)
                {
                    Thread.Sleep(1000);
                }
            }

            if (!pipe.IsConnected)
                return new RenderResponse { Success = false, Error = "Could not connect to RenderApp." };

            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);

            var json = JsonSerializer.Serialize(request, JsonOpts);
            writer.WriteLine(json);

            var responseLine = reader.ReadLine();
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
            // Installed layout: CLI at {app}\cli\, RenderApp at {app}\renderapp\
            Path.Combine(baseDir, "..", "renderapp", ExeName),
            Path.Combine(baseDir, "..", "LensHH.RenderApp", ExeName),
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

        Thread.Sleep(2000);
    }
}
