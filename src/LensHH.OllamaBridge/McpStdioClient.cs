using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LensHH.OllamaBridge
{
    public class McpStdioClient : IDisposable
    {
        private readonly Process _process;
        private readonly StreamWriter _writer;
        private readonly StreamReader _reader;
        private readonly object _lock = new();
        private int _requestId = 0;

        public McpStdioClient(string executablePath, string? arguments = null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments ?? "",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // If it's a .dll, launch via dotnet
            if (executablePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                psi.FileName = "dotnet";
                psi.Arguments = $"\"{executablePath}\"" + (arguments != null ? $" {arguments}" : "");
            }

            _process = Process.Start(psi)!;
            _writer = _process.StandardInput;
            _reader = _process.StandardOutput;

            // Log stderr in background
            _ = Task.Run(async () =>
            {
                while (!_process.HasExited)
                {
                    var line = await _process.StandardError.ReadLineAsync();
                    if (line != null) Console.Error.WriteLine($"[MCP] {line}");
                }
            });
        }

        public async Task InitializeAsync()
        {
            var initParams = JObject.FromObject(new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = "lenshh-ollama-bridge", version = "1.0.0" }
            });

            await SendRequestAsync("initialize", initParams);

            // Send initialized notification (no id, no response expected)
            var notification = new JsonRpcRequest { Method = "notifications/initialized" };
            var json = JsonConvert.SerializeObject(notification,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            await _writer.WriteLineAsync(json);
            await _writer.FlushAsync();
        }

        public async Task<List<McpToolInfo>> ListToolsAsync()
        {
            var result = await SendRequestAsync("tools/list", null);
            var toolsList = result?.ToObject<McpToolsListResult>();
            return toolsList?.Tools ?? new List<McpToolInfo>();
        }

        public async Task<string> CallToolAsync(string toolName, JObject arguments)
        {
            var callParams = new JObject
            {
                ["name"] = toolName,
                ["arguments"] = arguments
            };

            var result = await SendRequestAsync("tools/call", callParams);
            if (result == null) return "(no result)";

            var callResult = result.ToObject<McpToolCallResult>();
            if (callResult == null) return result.ToString();

            var texts = callResult.Content
                .Where(c => c.Type == "text" && c.Text != null)
                .Select(c => c.Text!);
            return string.Join("\n", texts);
        }

        private async Task<JToken?> SendRequestAsync(string method, JObject? @params)
        {
            int id;
            lock (_lock) { id = ++_requestId; }

            var request = new JsonRpcRequest
            {
                Id = id,
                Method = method,
                Params = @params
            };

            var json = JsonConvert.SerializeObject(request,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            await _writer.WriteLineAsync(json);
            await _writer.FlushAsync();

            // Read responses until we find ours
            while (true)
            {
                var line = await _reader.ReadLineAsync();
                if (line == null) throw new IOException("MCP server closed connection");
                if (!line.StartsWith("{")) continue;

                JsonRpcResponse? response;
                try { response = JsonConvert.DeserializeObject<JsonRpcResponse>(line); }
                catch { continue; }

                if (response == null || response.Id == null) continue; // skip notifications
                if (response.Id != id) continue;

                if (response.Error != null)
                    throw new Exception($"MCP error {response.Error.Code}: {response.Error.Message}");

                return response.Result;
            }
        }

        public void Dispose()
        {
            try { _writer.Close(); } catch { }
            try { _reader.Close(); } catch { }
            if (!_process.HasExited)
            {
                try { _process.Kill(); } catch { }
                _process.WaitForExit(3000);
            }
            _process.Dispose();
        }
    }
}
