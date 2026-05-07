using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LensHH.OllamaBridge
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            Console.WriteLine();
            Console.WriteLine("LensHH-LT Ollama Bridge");
            Console.WriteLine("=======================");
            Console.WriteLine();

            // Find MCP server
            string? mcpServerPath = args.Length > 0 ? args[0] : null;
            mcpServerPath ??= Environment.GetEnvironmentVariable("LENSHH_MCP_PATH");
            mcpServerPath ??= FindMcpServer();

            if (mcpServerPath == null || !File.Exists(mcpServerPath))
            {
                Console.Error.WriteLine("ERROR: LensHH.Mcp server not found.");
                Console.Error.WriteLine("  Build it: dotnet build src/LensHH.Mcp");
                Console.Error.WriteLine("  Or set LENSHH_MCP_PATH environment variable.");
                return 1;
            }

            Console.WriteLine($"MCP Server: {mcpServerPath}");

            // Connect to Ollama
            var ollamaUrl = Environment.GetEnvironmentVariable("OLLAMA_URL") ?? "http://localhost:11434";
            var temperature = double.TryParse(Environment.GetEnvironmentVariable("OLLAMA_TEMP"),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : 0.1;
            var numCtx = int.TryParse(Environment.GetEnvironmentVariable("OLLAMA_NUM_CTX"), out var n) ? n : 8192;
            var enableStream = !string.Equals(Environment.GetEnvironmentVariable("OLLAMA_STREAM"), "false",
                StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"Ollama URL: {ollamaUrl}");
            Console.WriteLine($"Temperature: {temperature}  NumCtx: {numCtx}  Stream: {enableStream}");

            using var ollama = new OllamaClient(ollamaUrl);

            // Pick model
            string? model = args.Length > 1 ? args[1] : null;
            model ??= Environment.GetEnvironmentVariable("OLLAMA_MODEL");

            if (model == null)
            {
                model = await PickModelAsync(ollama);
                if (model == null) return 1;
            }

            Console.WriteLine($"Model: {model}");
            Console.WriteLine();

            // Start MCP server
            Console.WriteLine("Starting LensHH-LT MCP server...");
            using var mcp = new McpStdioClient(mcpServerPath);
            await mcp.InitializeAsync();

            // Discover tools
            var mcpTools = await mcp.ListToolsAsync();
            Console.WriteLine($"Discovered {mcpTools.Count} tools.");

            // Convert to Ollama format
            var ollamaTools = mcpTools.Select(t => new OllamaTool
            {
                Type = "function",
                Function = new OllamaFunction
                {
                    Name = t.Name,
                    Description = t.Description,
                    Parameters = t.InputSchema
                }
            }).ToList();

            // System prompt
            var systemPrompt = BuildSystemPrompt(mcpTools);

            var messages = new List<OllamaMessage>
            {
                new() { Role = "system", Content = systemPrompt }
            };

            Console.WriteLine();
            Console.WriteLine("Ready! Type your message (or 'quit' to exit, 'tools' to list tools).");
            Console.WriteLine();

            // Chat loop
            while (true)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("You> ");
                Console.ResetColor();

                var input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input)) continue;
                if (input.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
                    input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    break;

                if (input.Equals("tools", StringComparison.OrdinalIgnoreCase))
                {
                    PrintTools(mcpTools);
                    continue;
                }

                messages.Add(new OllamaMessage { Role = "user", Content = input });

                // Tool-call loop
                while (true)
                {
                    var request = new OllamaChatRequest
                    {
                        Model = model,
                        Messages = messages,
                        Tools = ollamaTools,
                        Stream = enableStream,
                        Options = new OllamaOptions { Temperature = temperature, NumCtx = numCtx }
                    };

                    string content;
                    List<OllamaToolCall>? toolCalls;
                    string? error;
                    try
                    {
                        (content, toolCalls, error) = enableStream
                            ? await RunStreamingChatAsync(ollama, request)
                            : await RunChatAsync(ollama, request);
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Ollama error: {ex.Message}");
                        Console.ResetColor();
                        break;
                    }

                    if (error != null)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Ollama error: {error}");
                        Console.ResetColor();
                        break;
                    }

                    if (toolCalls != null && toolCalls.Count > 0)
                    {
                        messages.Add(new OllamaMessage
                        {
                            Role = "assistant",
                            Content = content,
                            ToolCalls = toolCalls
                        });

                        foreach (var tc in toolCalls)
                        {
                            var toolName = tc.Function.Name;
                            var toolArgs = tc.Function.Arguments;

                            Console.ForegroundColor = ConsoleColor.DarkCyan;
                            Console.WriteLine($"  [Tool: {toolName}]");
                            Console.ResetColor();

                            string result;
                            try
                            {
                                result = await mcp.CallToolAsync(toolName, toolArgs);
                            }
                            catch (Exception ex)
                            {
                                result = $"Error: {ex.Message}";
                            }

                            var displayResult = result.Length > 200
                                ? result.Substring(0, 200) + "..."
                                : result;
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.WriteLine($"  {displayResult}");
                            Console.ResetColor();

                            messages.Add(new OllamaMessage
                            {
                                Role = "tool",
                                Content = result
                            });
                        }

                        continue;
                    }

                    messages.Add(new OllamaMessage { Role = "assistant", Content = content });
                    if (!enableStream)
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.Write("LensHH> ");
                        Console.ResetColor();
                        Console.WriteLine(content);
                    }
                    Console.WriteLine();
                    break;
                }
            }

            Console.WriteLine("Goodbye!");
            return 0;
        }

        static string? FindMcpServer()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var binNames = OperatingSystem.IsWindows()
                ? new[] { "LensHH.Mcp.exe", "LensHH.Mcp.dll" }
                : new[] { "LensHH.Mcp", "LensHH.Mcp.dll" };

            var roots = new[]
            {
                // Source-tree layouts (running via `dotnet run` from the repo).
                Path.Combine(baseDir, "..", "..", "..", "..", "LensHH.Mcp", "bin", "Debug", "net8.0"),
                Path.Combine(baseDir, "..", "..", "..", "..", "LensHH.Mcp", "bin", "Release", "net8.0"),
                // Installed layout: bridge in {app}\ollama\, MCP in {app}\mcp\.
                Path.Combine(baseDir, "..", "mcp"),
            };

            foreach (var root in roots)
            foreach (var name in binNames)
            {
                var full = Path.GetFullPath(Path.Combine(root, name));
                if (File.Exists(full)) return full;
            }
            return null;
        }

        static async Task<(string content, List<OllamaToolCall>? toolCalls, string? error)> RunChatAsync(
            OllamaClient ollama, OllamaChatRequest request)
        {
            var resp = await ollama.ChatAsync(request);
            if (resp.Error != null) return ("", null, resp.Error);
            var msg = resp.Message;
            return (msg?.Content ?? "", msg?.ToolCalls, null);
        }

        static async Task<(string content, List<OllamaToolCall>? toolCalls, string? error)> RunStreamingChatAsync(
            OllamaClient ollama, OllamaChatRequest request)
        {
            var sb = new System.Text.StringBuilder();
            List<OllamaToolCall>? finalCalls = null;
            bool printedHeader = false;

            await foreach (var chunk in ollama.ChatStreamAsync(request))
            {
                if (chunk.Error != null)
                {
                    if (printedHeader) Console.WriteLine();
                    return (sb.ToString(), null, chunk.Error);
                }

                var msg = chunk.Message;
                if (msg != null)
                {
                    if (!string.IsNullOrEmpty(msg.Content))
                    {
                        if (!printedHeader)
                        {
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.Write("LensHH> ");
                            Console.ResetColor();
                            printedHeader = true;
                        }
                        Console.Write(msg.Content);
                        sb.Append(msg.Content);
                    }
                    if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                        finalCalls = msg.ToolCalls;
                }

                if (chunk.Done) break;
            }
            if (printedHeader) Console.WriteLine();
            return (sb.ToString(), finalCalls, null);
        }

        static async Task<string?> PickModelAsync(OllamaClient ollama)
        {
            List<string> models;
            try
            {
                models = await ollama.ListModelsAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Cannot connect to Ollama: {ex.Message}");
                Console.Error.WriteLine("Is Ollama running? Start it with: ollama serve");
                return null;
            }

            if (models.Count == 0)
            {
                Console.Error.WriteLine("No models found. Pull one: ollama pull qwen3:8b");
                return null;
            }

            Console.WriteLine("Available models:");
            for (int i = 0; i < models.Count; i++)
                Console.WriteLine($"  {i + 1}. {models[i]}");
            Console.Write($"Select model (1-{models.Count}): ");

            var input = Console.ReadLine();
            if (int.TryParse(input, out int idx) && idx >= 1 && idx <= models.Count)
                return models[idx - 1];

            Console.Error.WriteLine("Invalid selection.");
            return null;
        }

        static string BuildSystemPrompt(List<McpToolInfo> tools)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("You are an AI assistant for optical lens design using LensHH-LT.");
            sb.AppendLine("You have access to tools for loading lens files, modifying surfaces,");
            sb.AppendLine("running analyses (MTF, spot diagram, wavefront, etc.), and optimization.");
            sb.AppendLine();
            sb.AppendLine("Available tools:");
            foreach (var t in tools)
                sb.AppendLine($"  - {t.Name}: {t.Description}");
            sb.AppendLine();
            sb.AppendLine("Rules:");
            sb.AppendLine("- Always load a lens file before running analyses.");
            sb.AppendLine("- Use the appropriate tool for each task.");
            sb.AppendLine("- Present results clearly and concisely.");
            return sb.ToString();
        }

        static void PrintTools(List<McpToolInfo> tools)
        {
            Console.WriteLine($"\nAvailable tools ({tools.Count}):");
            foreach (var t in tools)
                Console.WriteLine($"  {t.Name,-35} {t.Description}");
            Console.WriteLine();
        }
    }
}
