using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace ConfigureOllamaBridge
{
    public partial class MainWindow : Window
    {
        private string _serverExePath = "";
        private string _bridgeExePath = "";
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _serverExePath = FindExe("LensHH.Mcp.exe", siblingFolder: "mcp",
                projectFolder: "LensHH.Mcp");
            txtServerPath.Text = _serverExePath;

            _bridgeExePath = FindExe("LensHH.OllamaBridge.exe", siblingFolder: "ollama",
                projectFolder: "LensHH.OllamaBridge");
            txtBridgePath.Text = _bridgeExePath;

            UpdateAllStatuses();
        }

        // ── Executable Detection ────────────────────────────────────────

        /// <summary>
        /// Find a LensHH-LT executable by probing the install layout
        /// (sibling folder under {app}) and the dev/source build outputs.
        /// </summary>
        private string FindExe(string fileName, string siblingFolder, string projectFolder)
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;

            var candidates = new[]
            {
                // Install layout: configure tool sits in {app}\config\,
                // server in {app}\mcp\, bridge in {app}\ollama\.
                Path.GetFullPath(Path.Combine(exeDir, "..", siblingFolder, fileName)),
                // Same-folder fallback in case someone moves the exes around.
                Path.GetFullPath(Path.Combine(exeDir, fileName)),
                Path.GetFullPath(Path.Combine(exeDir, "..", fileName)),
                // Dev/source build outputs (running ConfigureOllamaBridge from
                // src\ConfigureOllamaBridge\bin\Debug\net8.0-windows\).
                Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..",
                    projectFolder, "bin", "Release", "net8.0", fileName)),
                Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..",
                    projectFolder, "bin", "Debug", "net8.0", fileName)),
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
            var path = BrowseForExe("Select LensHH.Mcp.exe", "LensHH.Mcp.exe");
            if (path != null)
            {
                _serverExePath = path;
                txtServerPath.Text = path;
                UpdateAllStatuses();
            }
        }

        private void BrowseBridge_Click(object sender, RoutedEventArgs e)
        {
            var path = BrowseForExe("Select LensHH.OllamaBridge.exe", "LensHH.OllamaBridge.exe");
            if (path != null)
            {
                _bridgeExePath = path;
                txtBridgePath.Text = path;
                UpdateAllStatuses();
            }
        }

        private string BrowseForExe(string title, string defaultName)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = title,
                Filter = $"{defaultName}|{defaultName}|All executables|*.exe",
                FileName = defaultName,
            };

            if (dialog.ShowDialog() == true)
                return dialog.FileName;

            return null;
        }

        // ── Ollama Detection ────────────────────────────────────────────

        private async void RefreshOllama_Click(object sender, RoutedEventArgs e)
        {
            await RefreshOllamaAsync();
        }

        private async System.Threading.Tasks.Task RefreshOllamaAsync()
        {
            string baseUrl = txtOllamaUrl.Text.TrimEnd('/');

            try
            {
                var response = await _http.GetAsync($"{baseUrl}/api/tags");
                var body = await response.Content.ReadAsStringAsync();

                var models = new List<string>();
                using (var doc = JsonDocument.Parse(body))
                {
                    if (doc.RootElement.TryGetProperty("models", out var arr) &&
                        arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var m in arr.EnumerateArray())
                        {
                            if (m.TryGetProperty("name", out var name) &&
                                name.ValueKind == JsonValueKind.String)
                            {
                                var s = name.GetString();
                                if (!string.IsNullOrEmpty(s))
                                    models.Add(s);
                            }
                        }
                    }
                }

                cmbModels.Items.Clear();
                foreach (var model in models)
                    cmbModels.Items.Add(model);

                if (models.Count > 0)
                    cmbModels.SelectedIndex = 0;

                SetIndicator(txtOllamaStatus, "Ollama", true, $"running ({models.Count} models)", "");
            }
            catch (Exception)
            {
                SetIndicator(txtOllamaStatus, "Ollama", false, "", "not reachable - is it running?");
                cmbModels.Items.Clear();
            }
        }

        // ── Pull Model ──────────────────────────────────────────────────

        private void PullModel_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("Pull Ollama Model",
                "Enter model name (e.g. qwen3:8b, llama3.1, mistral-nemo):", "qwen3:8b");
            if (dlg.ShowDialog() != true) return;

            var modelName = dlg.ResponseText;
            if (string.IsNullOrWhiteSpace(modelName)) return;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c ollama pull {modelName} & pause",
                    UseShellExecute = true,
                    CreateNoWindow = false,
                };

                Process.Start(psi);
                SetStatus($"Pulling {modelName} in a new window. Refresh when done.", true);
            }
            catch (Exception ex)
            {
                SetStatus("Error pulling model: " + ex.Message, false);
            }
        }

        // ── Actions ─────────────────────────────────────────────────────

        private void LaunchBridge_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidatePaths()) return;

            var model = GetSelectedModel();
            if (model == null) return;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    // /k keeps the window open after the bridge exits so the
                    // user can read errors. Quote-escaping for cmd.exe with
                    // spaces in path: outermost "..." encloses the full /k
                    // argument, the inner quotes wrap the two paths.
                    Arguments = $"/k \"\"{_bridgeExePath}\" \"{_serverExePath}\"\"",
                    UseShellExecute = false,
                    CreateNoWindow = false,
                };
                psi.EnvironmentVariables["OLLAMA_MODEL"] = model;
                psi.EnvironmentVariables["OLLAMA_URL"] = txtOllamaUrl.Text.TrimEnd('/');

                Process.Start(psi);
                SetStatus("Bridge launched in a new window.", true);
            }
            catch (Exception ex)
            {
                SetStatus("Error launching bridge: " + ex.Message, false);
            }
        }

        private void CreateShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidatePaths()) return;

            var model = GetSelectedModel();
            if (model == null) return;

            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string shortcutPath = Path.Combine(desktop, "LensHH-LT Ollama Bridge.lnk");

                // Create a batch file next to the bridge that the shortcut
                // calls — the shortcut sets up env vars before invoking the
                // bridge so users see the right model/URL the next launch.
                string batchDir = Path.GetDirectoryName(_bridgeExePath);
                string batchPath = Path.Combine(batchDir, "launch_ollama_bridge.bat");

                string batchContent =
                    "@echo off\r\n" +
                    $"set OLLAMA_MODEL={model}\r\n" +
                    $"set OLLAMA_URL={txtOllamaUrl.Text.TrimEnd('/')}\r\n" +
                    $"\"{_bridgeExePath}\" \"{_serverExePath}\"\r\n" +
                    "pause\r\n";

                File.WriteAllText(batchPath, batchContent);

                // Create .lnk shortcut via WScript.Shell COM (no extra refs).
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                var shell = Activator.CreateInstance(shellType);
                var shortcut = shellType.InvokeMember("CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod, null, shell,
                    new object[] { shortcutPath });
                var scType = shortcut.GetType();
                scType.InvokeMember("TargetPath",
                    System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { batchPath });
                scType.InvokeMember("WorkingDirectory",
                    System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { batchDir });
                scType.InvokeMember("Description",
                    System.Reflection.BindingFlags.SetProperty, null, shortcut,
                    new object[] { "Launch LensHH-LT Ollama Bridge" });
                scType.InvokeMember("Save",
                    System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);

                SetStatus($"Desktop shortcut created: {shortcutPath}", true);
            }
            catch (Exception ex)
            {
                SetStatus("Error creating shortcut: " + ex.Message, false);
            }
        }

        private void CreateBatchFile_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidatePaths()) return;

            var model = GetSelectedModel();
            if (model == null) return;

            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save Batch File",
                    Filter = "Batch files|*.bat|All files|*.*",
                    FileName = "launch_ollama_bridge.bat",
                    InitialDirectory = Path.GetDirectoryName(_bridgeExePath),
                };

                if (dialog.ShowDialog() != true) return;

                string batchContent =
                    "@echo off\r\n" +
                    "echo === LensHH-LT + Ollama Bridge ===\r\n" +
                    "echo.\r\n" +
                    $"set OLLAMA_MODEL={model}\r\n" +
                    $"set OLLAMA_URL={txtOllamaUrl.Text.TrimEnd('/')}\r\n" +
                    $"\"{_bridgeExePath}\" \"{_serverExePath}\"\r\n" +
                    "pause\r\n";

                File.WriteAllText(dialog.FileName, batchContent);
                SetStatus($"Batch file saved: {dialog.FileName}", true);
            }
            catch (Exception ex)
            {
                SetStatus("Error creating batch file: " + ex.Message, false);
            }
        }

        // ── Validation ──────────────────────────────────────────────────

        private bool ValidatePaths()
        {
            if (string.IsNullOrEmpty(_serverExePath) || !File.Exists(_serverExePath))
            {
                SetStatus("MCP Server executable not found. Use Browse to select it.", false);
                return false;
            }
            if (string.IsNullOrEmpty(_bridgeExePath) || !File.Exists(_bridgeExePath))
            {
                SetStatus("Ollama Bridge executable not found. Use Browse to select it.", false);
                return false;
            }
            return true;
        }

        private string GetSelectedModel()
        {
            var model = cmbModels.Text;
            if (string.IsNullOrWhiteSpace(model))
            {
                SetStatus("Please select or enter a model name.", false);
                return null;
            }
            return model;
        }

        // ── Status Updates ──────────────────────────────────────────────

        private async void UpdateAllStatuses()
        {
            bool serverFound = !string.IsNullOrEmpty(_serverExePath) && File.Exists(_serverExePath);
            SetIndicator(txtServerStatus, "LensHH.Mcp.exe", serverFound);

            bool bridgeFound = !string.IsNullOrEmpty(_bridgeExePath) && File.Exists(_bridgeExePath);
            SetIndicator(txtBridgeStatus, "LensHH.OllamaBridge.exe", bridgeFound);

            await RefreshOllamaAsync();
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
