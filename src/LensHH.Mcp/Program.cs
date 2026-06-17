using LensHH.Core.Activation;
using LensHH.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

// Keep MCP-driven optimization at full speed. This is a headless, never-foreground
// process, so Windows 11 would otherwise EcoQoS-throttle it for its entire lifetime.
LensHH.IO.WindowsPerformance.DisablePowerThrottling();

// Load existing license or start/continue 45-day trial
ActivationManager.TryLoadExistingActivation();

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton<McpSession>();

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "LensHH-LT",
            Version = "1.0.0"
        };
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
