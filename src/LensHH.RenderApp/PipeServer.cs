using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using LensHH.Core.Glass;
using LensHH.Core.IO;

namespace LensHH.RenderApp;

public class PipeServer
{
    public const string PipeName = "LensHH-RenderApp";

    private readonly RenderWindow _window;
    private readonly GlassCatalogManager _glassCatalog;
    private readonly AnalysisDispatcher _dispatcher;
    private readonly CancellationTokenSource _cts = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "renderapp_error.log");

    public PipeServer(RenderWindow window)
    {
        _window = window;
        _glassCatalog = new GlassCatalogManager();
        LoadGlassCatalogs();
        _dispatcher = new AnalysisDispatcher(_glassCatalog);
    }

    public void Start()
    {
        Task.Run(async () =>
        {
            try
            {
                await ListenLoop(_cts.Token);
            }
            catch (Exception ex)
            {
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ListenLoop CRASHED: {ex}\n\n");
            }
        });
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var pipe = new NamedPipeServerStream(PipeName,
                PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await pipe.WaitForConnectionAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

                var line = await reader.ReadLineAsync();
                if (line == null) continue;

                var request = JsonSerializer.Deserialize<RenderRequest>(line, JsonOpts);
                if (request == null) continue;

                RenderResponse response;
                try
                {
                    // Handle clear command (no system needed)
                    if (string.Equals(request.Analysis, "Clear", StringComparison.OrdinalIgnoreCase))
                    {
                        await Dispatcher.UIThread.InvokeAsync(() => _window.ClearDisplay());
                        response = new RenderResponse { Success = true };
                        await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOpts));
                        continue;
                    }

                    // Deserialize OpticalSystem from the embedded LhltFile
                    var readResult = LhltReader.FromLhltFile(request.System);
                    var system = readResult.System;

                    // Compute analysis + render to bitmap (CPU-bound)
                    var (title, bitmap) = _dispatcher.Execute(system, request.Analysis, request.Params);

                    // If SavePngPath is specified, save bitmap to disk as PNG
                    string savePngPath = ParamHelper.GetString(request.Params, "SavePngPath", "");
                    if (!string.IsNullOrEmpty(savePngPath))
                    {
                        var dir = Path.GetDirectoryName(savePngPath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);
                        bitmap.Save(savePngPath);
                    }

                    // Marshal bitmap display to the UI thread
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _window.UpdateImage(title, bitmap);
                        if (_window.WindowState == WindowState.Minimized)
                            _window.WindowState = WindowState.Normal;
                        _window.Activate();
                    });

                    response = new RenderResponse { Success = true };
                }
                catch (Exception ex)
                {
                    File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {request.Analysis}: {ex}\n\n");
                    response = new RenderResponse { Success = false, Error = ex.Message };
                }

                await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOpts));
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // Client disconnected or pipe disposed; loop back
            }
            catch (Exception ex)
            {
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Pipe error: {ex}\n\n");
                // Loop back to accept next connection instead of crashing
            }
        }
    }

    private void LoadGlassCatalogs()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var searchPaths = new[]
        {
            Path.Combine(baseDir, "catalogs", "Glass"),
            Path.Combine(baseDir, "..", "catalogs", "Glass"),
            Path.Combine(baseDir, "catalogs"),
            // Dev: bin/Debug/net8.0 -> src/LensHH.RenderApp -> src -> LensHH-LT -> catalogs/Glass
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "catalogs", "Glass"),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "catalogs"),
        };

        foreach (var path in searchPaths)
        {
            var full = Path.GetFullPath(path);
            if (Directory.Exists(full))
            {
                _glassCatalog.LoadCatalogsFromFolder(full);
                break;
            }
        }
    }
}
