using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;

namespace ConfigureLensHHMcp
{
    public partial class MainWindow : Window
    {
        private const string ServerName = "lenshh-lt";
        private string _serverExePath = "";

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _serverExePath = FindServerExe();
            txtServerPath.Text = _serverExePath;
            UpdateAllStatuses();
        }

        // ── Server Detection ──────────────────────────────────────────

        private string FindServerExe()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;

            var candidates = new[]
            {
                // From ConfigureLensHHMcp bin/Debug/net8.0-windows -> LensHH.Mcp bin
                Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..", "LensHH.Mcp", "bin", "Debug", "net8.0", "LensHH.Mcp.exe")),
                Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..", "LensHH.Mcp", "bin", "Release", "net8.0", "LensHH.Mcp.exe")),
                // Installed layout: mcp subfolder sibling to this exe's parent
                Path.GetFullPath(Path.Combine(exeDir, "..", "mcp", "LensHH.Mcp.exe")),
                // Sibling to this exe
                Path.Combine(exeDir, "LensHH.Mcp.exe"),
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return "";
        }

        private void BrowseServer_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select LensHH.Mcp.exe",
                Filter = "LensHH MCP Server|LensHH.Mcp.exe|All executables|*.exe",
                FileName = "LensHH.Mcp.exe"
            };

            if (dialog.ShowDialog() == true)
            {
                _serverExePath = dialog.FileName;
                txtServerPath.Text = _serverExePath;
                UpdateAllStatuses();
            }
        }

        // ── Claude Desktop Configuration ──────────────────────────────

        private string GetClaudeDesktopConfigPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Claude", "claude_desktop_config.json");
        }

        private bool IsClaudeDesktopConfigured()
        {
            try
            {
                string configPath = GetClaudeDesktopConfigPath();
                if (!File.Exists(configPath))
                    return false;

                string json = File.ReadAllText(configPath);
                var root = JsonNode.Parse(json)?.AsObject();
                var servers = root?["mcpServers"]?.AsObject();
                return servers != null && servers[ServerName] != null;
            }
            catch
            {
                return false;
            }
        }

        private void ConfigureClaudeDesktop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_serverExePath) || !File.Exists(_serverExePath))
                {
                    SetStatus("Server executable not found. Use Browse to select it.", false);
                    return;
                }

                string configPath = GetClaudeDesktopConfigPath();
                string configDir = Path.GetDirectoryName(configPath);

                if (!Directory.Exists(configDir))
                    Directory.CreateDirectory(configDir);

                JsonObject root;
                if (File.Exists(configPath))
                {
                    string existing = File.ReadAllText(configPath);
                    root = JsonNode.Parse(existing)?.AsObject() ?? new JsonObject();
                }
                else
                {
                    root = new JsonObject();
                }

                if (root["mcpServers"] == null)
                    root["mcpServers"] = new JsonObject();

                var servers = root["mcpServers"].AsObject();
                servers[ServerName] = new JsonObject
                {
                    ["command"] = _serverExePath,
                    ["args"] = new JsonArray()
                };

                var writeOptions = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(configPath, root.ToJsonString(writeOptions));

                SetStatus("Claude Desktop configured successfully.", true);
                UpdateAllStatuses();
            }
            catch (Exception ex)
            {
                SetStatus("Error configuring Claude Desktop: " + ex.Message, false);
            }
        }

        private void RemoveClaudeDesktop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string configPath = GetClaudeDesktopConfigPath();
                if (!File.Exists(configPath))
                {
                    SetStatus("Claude Desktop config file not found.", false);
                    return;
                }

                string json = File.ReadAllText(configPath);
                var root = JsonNode.Parse(json)?.AsObject();
                var servers = root?["mcpServers"]?.AsObject();

                if (servers != null && servers[ServerName] != null)
                {
                    servers.Remove(ServerName);
                    var writeOptions = new JsonSerializerOptions { WriteIndented = true };
                    File.WriteAllText(configPath, root.ToJsonString(writeOptions));
                    SetStatus("Removed lenshh-lt from Claude Desktop.", true);
                }
                else
                {
                    SetStatus("lenshh-lt was not configured in Claude Desktop.", false);
                }

                UpdateAllStatuses();
            }
            catch (Exception ex)
            {
                SetStatus("Error removing from Claude Desktop: " + ex.Message, false);
            }
        }

        // ── Claude Code Configuration ─────────────────────────────────

        // Resolves the Claude Code launcher across the install variants we
        // see in the wild. Plain Process.Start("claude", …) with
        // UseShellExecute=false fails when the install is the npm shim
        // (claude.cmd) because Windows CreateProcess does NOT consult
        // PATHEXT — it only finds claude.exe. Interactive shells DO apply
        // PATHEXT, which is why "claude" works in cmd/PowerShell yet our
        // configurator reports "not found in PATH" on the same machine.
        // Returns null if no plausible launcher can be located.
        private static string ResolveClaudePath()
        {
            string envOverride = Environment.GetEnvironmentVariable("CLAUDE_EXE");
            if (!string.IsNullOrWhiteSpace(envOverride) && File.Exists(envOverride))
                return envOverride;

            string[] names = { "claude.exe", "claude.cmd", "claude.bat" };

            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                foreach (var name in names)
                {
                    try
                    {
                        string full = Path.Combine(dir.Trim(), name);
                        if (File.Exists(full)) return full;
                    }
                    catch { /* malformed PATH entry — skip */ }
                }
            }

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            string[] fallbacks =
            {
                // Anthropic standalone Windows installer drops here.
                Path.Combine(userProfile, ".local", "bin", "claude.exe"),
                // npm install -g — common on Windows even when %APPDATA%\npm is not on PATH.
                Path.Combine(appData, "npm", "claude.cmd"),
                Path.Combine(appData, "npm", "claude.exe"),
                // Some msi-style installers.
                Path.Combine(localAppData, "Programs", "claude", "claude.exe"),
            };

            foreach (var fb in fallbacks)
            {
                if (File.Exists(fb)) return fb;
            }

            return null;
        }

        // Build a ProcessStartInfo for invoking Claude with the given args.
        // For .cmd / .bat we wrap in cmd.exe /c so stdout/stderr can still
        // be redirected (CreateProcess refuses to execute scripts directly).
        // Returns null if no Claude launcher could be located.
        private static ProcessStartInfo CreateClaudeProcess(string args)
        {
            string path = ResolveClaudePath();
            if (path == null) return null;

            var psi = new ProcessStartInfo
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".cmd" || ext == ".bat")
            {
                psi.FileName = "cmd.exe";
                // The outer pair of quotes is consumed by cmd /c; everything
                // between them is forwarded to the script verbatim, so the
                // quoted server-exe path inside `args` is preserved.
                psi.Arguments = $"/c \"\"{path}\" {args}\"";
            }
            else
            {
                psi.FileName = path;
                psi.Arguments = args;
            }
            return psi;
        }

        private bool IsClaudeCodeConfigured()
        {
            try
            {
                var psi = CreateClaudeProcess("mcp list");
                if (psi == null) return false;

                using (var proc = Process.Start(psi))
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(5000);
                    return output.Contains(ServerName);
                }
            }
            catch
            {
                return false;
            }
        }

        private void ConfigureClaudeCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_serverExePath) || !File.Exists(_serverExePath))
                {
                    SetStatus("Server executable not found. Use Browse to select it.", false);
                    return;
                }

                string args = $"mcp add --transport stdio --scope user {ServerName} -- \"{_serverExePath}\"";
                var psi = CreateClaudeProcess(args);
                if (psi == null)
                {
                    string cmd = $"claude {args}";
                    Clipboard.SetText(cmd);
                    SetStatus("'claude' not found in PATH or known install paths. Command copied to clipboard for manual use.", false);
                    return;
                }

                using (var proc = Process.Start(psi))
                {
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(10000);

                    if (proc.ExitCode == 0)
                    {
                        SetStatus("Claude Code configured successfully.", true);
                    }
                    else
                    {
                        string msg = string.IsNullOrEmpty(stderr) ? stdout : stderr;
                        SetStatus("Claude Code error: " + msg.Trim(), false);
                    }
                }

                UpdateAllStatuses();
            }
            catch (Exception ex)
            {
                SetStatus("Error configuring Claude Code: " + ex.Message, false);
            }
        }

        private void RemoveClaudeCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var psi = CreateClaudeProcess($"mcp remove --scope user {ServerName}");
                if (psi == null)
                {
                    SetStatus("'claude' not found in PATH or known install paths.", false);
                    return;
                }

                using (var proc = Process.Start(psi))
                {
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(10000);

                    if (proc.ExitCode == 0)
                    {
                        SetStatus("Removed lenshh-lt from Claude Code.", true);
                    }
                    else
                    {
                        string msg = string.IsNullOrEmpty(stderr) ? stdout : stderr;
                        SetStatus("Claude Code error: " + msg.Trim(), false);
                    }
                }

                UpdateAllStatuses();
            }
            catch (Exception ex)
            {
                SetStatus("Error removing from Claude Code: " + ex.Message, false);
            }
        }

        // ── Status Updates ────────────────────────────────────────────

        private void UpdateAllStatuses()
        {
            bool serverFound = !string.IsNullOrEmpty(_serverExePath) && File.Exists(_serverExePath);
            SetIndicator(txtServerStatus, "LensHH.Mcp.exe", serverFound);

            bool desktopConfigured = IsClaudeDesktopConfigured();
            SetIndicator(txtDesktopStatus, "Claude Desktop", desktopConfigured, "configured", "not configured");

            bool codeConfigured = IsClaudeCodeConfigured();
            SetIndicator(txtCodeStatus, "Claude Code", codeConfigured, "configured", "not configured");
        }

        private static void SetIndicator(System.Windows.Controls.TextBlock indicator, string label, bool ok,
            string trueText = "found", string falseText = "not found")
        {
            if (ok)
            {
                indicator.Text = "\u2714 " + label + " " + trueText;
                indicator.Foreground = new SolidColorBrush(Color.FromRgb(0, 128, 0));
            }
            else
            {
                indicator.Text = "\u2718 " + label + " " + falseText;
                indicator.Foreground = new SolidColorBrush(Color.FromRgb(192, 0, 0));
            }
        }

        private void SetStatus(string message, bool success)
        {
            txtStatus.Text = message;
            txtStatus.Foreground = success
                ? new SolidColorBrush(Color.FromRgb(0, 128, 0))
                : new SolidColorBrush(Color.FromRgb(192, 0, 0));
        }
    }
}
