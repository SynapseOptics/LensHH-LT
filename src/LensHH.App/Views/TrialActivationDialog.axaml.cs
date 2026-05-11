using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LensHH.Core.Activation;

namespace LensHH.App.Views;

public partial class TrialActivationDialog : Window
{
    // Sentinel returned to the caller when the offline path completes
    // successfully. Real online tokens are JSON objects beginning with '{',
    // so this string can never collide with a valid token.
    public const string OfflineActivatedResult = "OFFLINE_OK";

    private const string BaseUrl = "https://synapseoptics-license.javier-ruiz.workers.dev";
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private string _email = "";
    private string? _offlineTokenPath;

    public TrialActivationDialog()
    {
        InitializeComponent();
    }

    private async void SendCode_Click(object? sender, RoutedEventArgs e)
    {
        string email = EmailBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(email) || !email.Contains("@") || !email.Contains("."))
        {
            StatusText.Text = "Please enter a valid email address.";
            return;
        }

        _email = email;
        SendCodeButton.IsEnabled = false;
        StatusText.Text = "Sending code...";

        try
        {
            string machineId = ActivationManager.GetMachineFingerprint();
            string body = $"{{\"email\":\"{Escape(email)}\",\"machineId\":\"{Escape(machineId)}\"}}";
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{BaseUrl}/trial/request", content);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                // Move to step 2
                Step1Panel.IsVisible = false;
                Step2Panel.IsVisible = true;
                CodePromptText.Text = $"Enter the 6-digit code sent to {email}:";
                StatusText.Text = "Check your email for the activation code.";
                CodeBox.Focus();
            }
            else
            {
                string error = ExtractError(responseBody);
                StatusText.Text = error ?? $"Request failed (HTTP {(int)response.StatusCode}).";
                SendCodeButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Connection error: {ex.Message}";
            SendCodeButton.IsEnabled = true;
        }
    }

    private async void ResendCode_Click(object? sender, RoutedEventArgs e)
    {
        ResendButton.IsEnabled = false;
        StatusText.Text = "Resending code...";

        try
        {
            string machineId = ActivationManager.GetMachineFingerprint();
            string body = $"{{\"email\":\"{Escape(_email)}\",\"machineId\":\"{Escape(machineId)}\"}}";
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{BaseUrl}/trial/request", content);

            if (response.IsSuccessStatusCode)
                StatusText.Text = "New code sent. Check your email.";
            else
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                StatusText.Text = ExtractError(responseBody) ?? "Failed to resend code.";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Connection error: {ex.Message}";
        }
        finally
        {
            ResendButton.IsEnabled = true;
        }
    }

    private async void Activate_Click(object? sender, RoutedEventArgs e)
    {
        string code = CodeBox.Text?.Trim() ?? "";
        if (code.Length != 6)
        {
            StatusText.Text = "Please enter the 6-digit code.";
            return;
        }

        ActivateButton.IsEnabled = false;
        StatusText.Text = "Verifying...";

        try
        {
            string machineId = ActivationManager.GetMachineFingerprint();
            string body = $"{{\"email\":\"{Escape(_email)}\",\"code\":\"{Escape(code)}\",\"machineId\":\"{Escape(machineId)}\"}}";
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{BaseUrl}/trial/verify", content);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                string token = ExtractField(responseBody, "token");
                Close(token); // Return the signed token to the caller
            }
            else
            {
                string error = ExtractError(responseBody);
                StatusText.Text = error ?? $"Verification failed (HTTP {(int)response.StatusCode}).";
                ActivateButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Connection error: {ex.Message}";
            ActivateButton.IsEnabled = true;
        }
    }

    private void Back_Click(object? sender, RoutedEventArgs e)
    {
        Step2Panel.IsVisible = false;
        Step1Panel.IsVisible = true;
        SendCodeButton.IsEnabled = true;
        StatusText.Text = "";
    }

    private void Offline_Click(object? sender, RoutedEventArgs e)
    {
        MachineIdBox.Text = ActivationManager.GetMachineFingerprint();
        TokenPathBox.Text = "";
        _offlineTokenPath = null;
        ActivateOfflineButton.IsEnabled = false;
        Step1Panel.IsVisible = false;
        Step3Panel.IsVisible = true;
        StatusText.Text = "";
    }

    private void BackFromOffline_Click(object? sender, RoutedEventArgs e)
    {
        Step3Panel.IsVisible = false;
        Step1Panel.IsVisible = true;
        StatusText.Text = "";
    }

    private async void BrowseToken_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select trial token file",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Trial token") { Patterns = new[] { "*.json" } },
                new FilePickerFileType("All files") { Patterns = new[] { "*" } },
            }
        });
        if (files.Count == 0) return;

        string? path = files[0].TryGetLocalPath();
        if (path == null)
        {
            StatusText.Text = "Could not resolve the selected file path.";
            return;
        }

        _offlineTokenPath = path;
        TokenPathBox.Text = path;
        ActivateOfflineButton.IsEnabled = true;
        StatusText.Text = "";
    }

    private async void ActivateOffline_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_offlineTokenPath))
        {
            StatusText.Text = "Choose a token file first.";
            return;
        }

        ActivateOfflineButton.IsEnabled = false;
        BrowseTokenButton.IsEnabled = false;
        StatusText.Text = "Activating...";

        string tokenPath = _offlineTokenPath;
        string? error = await Task.Run(() => ActivationManager.ActivateOffline(tokenPath));

        if (error == null)
        {
            Close(OfflineActivatedResult);
        }
        else
        {
            StatusText.Text = error;
            ActivateOfflineButton.IsEnabled = true;
            BrowseTokenButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private static string? ExtractError(string json)
    {
        return ExtractField(json, "error");
    }

    private static string? ExtractField(string json, string field)
    {
        string pattern = "\"" + field + "\"";
        int idx = json.IndexOf(pattern);
        if (idx < 0) return null;
        int colon = json.IndexOf(':', idx + pattern.Length);
        if (colon < 0) return null;
        int start = colon + 1;
        while (start < json.Length && json[start] == ' ') start++;
        if (start >= json.Length || json[start] != '"') return null;
        int end = start + 1;
        while (end < json.Length)
        {
            if (json[end] == '\\') { end += 2; continue; }
            if (json[end] == '"') break;
            end++;
        }
        return json.Substring(start + 1, end - start - 1)
            .Replace("\\\"", "\"").Replace("\\\\", "\\");
    }

    private static string Escape(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
