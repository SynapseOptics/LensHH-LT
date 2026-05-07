using System;
using System.Net.Http;
using System.Text;
using LensHH.Core.Activation;
using Spectre.Console;

namespace LensHH.CLI.Commands
{
    public class LicenseCommand : ICommand
    {
        public string Name => "license";
        public string Description => "License activation and deactivation";

        public string Help => @"[bold]license[/] - License management
  [green]license status[/]                     Show activation status
  [green]license trial <email>[/]              Start 45-day trial (email verified)
  [green]license activate <key>[/]             Activate with license key
  [green]license deactivate <key>[/]           Deactivate this machine
  [green]license offline <token-file>[/]       Activate from offline token file
  [green]license fetch-token <key> <machineId> [output][/]
                                       Fetch token for an offline machine
  [green]license machineid[/]                  Show this machine's fingerprint";

        public void Execute(Session session, string[] args)
        {
            if (args.Length == 0)
            {
                AnsiConsole.MarkupLine(Help);
                return;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "status":
                    ShowStatus();
                    break;

                case "trial":
                    if (args.Length < 2)
                    {
                        AnsiConsole.MarkupLine("[red]Usage: license trial <email>[/]");
                        return;
                    }
                    StartTrial(args[1]);
                    break;

                case "activate":
                    if (args.Length < 2)
                    {
                        AnsiConsole.MarkupLine("[red]Usage: license activate <key>[/]");
                        return;
                    }
                    Activate(args[1]);
                    break;

                case "deactivate":
                    if (args.Length < 2)
                    {
                        AnsiConsole.MarkupLine("[red]Usage: license deactivate <key>[/]");
                        return;
                    }
                    Deactivate(args[1]);
                    break;

                case "offline":
                    if (args.Length < 2)
                    {
                        AnsiConsole.MarkupLine("[red]Usage: license offline <token-file>[/]");
                        return;
                    }
                    ActivateOffline(args[1]);
                    break;

                case "fetch-token":
                    if (args.Length < 3)
                    {
                        AnsiConsole.MarkupLine("[red]Usage: license fetch-token <key> <machineId> [output-file][/]");
                        return;
                    }
                    FetchToken(args[1], args[2], args.Length >= 4 ? args[3] : "lenshh_token.json");
                    break;

                case "machineid":
                    AnsiConsole.MarkupLine($"Machine ID: [green]{ActivationManager.GetMachineFingerprint()}[/]");
                    break;

                default:
                    AnsiConsole.MarkupLine(Help);
                    break;
            }
        }

        private static void ShowStatus()
        {
            bool activated = ActivationManager.IsActivated;
            string machineId = ActivationManager.GetMachineFingerprint();

            if (activated)
            {
                if (TrialClock.IsTrialActive)
                {
                    AnsiConsole.MarkupLine($"[yellow]LensHH-LT is running in trial mode ({TrialClock.DaysRemaining} days remaining).[/]");
                    if (TrialClock.DaysRemaining <= 7)
                        AnsiConsole.MarkupLine("[red]Trial expiring soon! Use 'license activate <key>' to activate.[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[green]LensHH-LT is activated.[/]");
                }
            }
            else
            {
                if (TrialClock.IsTrialExpired)
                    AnsiConsole.MarkupLine("[red]Trial has expired. Use 'license activate <key>' to continue.[/]");
                else
                    AnsiConsole.MarkupLine("[yellow]LensHH-LT is not activated.[/]");
            }

            AnsiConsole.MarkupLine($"Machine ID: [dim]{machineId.Substring(0, 16)}...[/]");
        }

        private static void Activate(string key)
        {
            AnsiConsole.Status().Start("Activating...", ctx =>
            {
                string? error = ActivationManager.Activate(key);
                if (error == null)
                    AnsiConsole.MarkupLine("[green]Activation successful![/]");
                else
                    AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
            });
        }

        private static void Deactivate(string key)
        {
            AnsiConsole.Status().Start("Deactivating...", ctx =>
            {
                string? error = ActivationManager.Deactivate(key);
                if (error == null)
                    AnsiConsole.MarkupLine("[green]Deactivation successful. Seat freed.[/]");
                else
                    AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
            });
        }

        private static void ActivateOffline(string tokenFile)
        {
            string? error = ActivationManager.ActivateOffline(tokenFile);
            if (error == null)
                AnsiConsole.MarkupLine("[green]Offline activation successful![/]");
            else
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
        }

        private static void StartTrial(string email)
        {
            const string baseUrl = "https://synapseoptics-license.javier-ruiz.workers.dev";

            if (ActivationManager.IsActivated)
            {
                AnsiConsole.MarkupLine("[yellow]LensHH-LT is already activated.[/]");
                return;
            }
            if (TrialClock.IsTrialActive)
            {
                AnsiConsole.MarkupLine($"[yellow]Trial is already active ({TrialClock.DaysRemaining} days remaining).[/]");
                return;
            }

            string machineId = ActivationManager.GetMachineFingerprint();

            // Step 1: Request code
            AnsiConsole.Status().Start("Requesting trial code...", ctx =>
            {
                try
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                    string body = $"{{\"email\":\"{Escape(email)}\",\"machineId\":\"{Escape(machineId)}\"}}";
                    var content = new StringContent(body, Encoding.UTF8, "application/json");
                    var response = client.PostAsync($"{baseUrl}/trial/request", content).Result;
                    string responseBody = response.Content.ReadAsStringAsync().Result;

                    if (!response.IsSuccessStatusCode)
                    {
                        string error = ExtractJsonField(responseBody, "error") ?? $"HTTP {(int)response.StatusCode}";
                        AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
                        return;
                    }

                    AnsiConsole.MarkupLine($"[green]Activation code sent to {Markup.Escape(email)}[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Connection error: {Markup.Escape(ex.Message)}[/]");
                }
            });

            // Step 2: Prompt for code
            string code = AnsiConsole.Ask<string>("Enter the 6-digit code from your email:");

            // Step 3: Verify code
            AnsiConsole.Status().Start("Verifying code...", ctx =>
            {
                try
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                    string body = $"{{\"email\":\"{Escape(email)}\",\"code\":\"{Escape(code.Trim())}\",\"machineId\":\"{Escape(machineId)}\"}}";
                    var content = new StringContent(body, Encoding.UTF8, "application/json");
                    var response = client.PostAsync($"{baseUrl}/trial/verify", content).Result;
                    string responseBody = response.Content.ReadAsStringAsync().Result;

                    if (!response.IsSuccessStatusCode)
                    {
                        string error = ExtractJsonField(responseBody, "error") ?? $"HTTP {(int)response.StatusCode}";
                        AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
                        return;
                    }

                    string? token = ExtractJsonField(responseBody, "token");
                    if (string.IsNullOrEmpty(token))
                    {
                        AnsiConsole.MarkupLine("[red]Server returned no trial token.[/]");
                        return;
                    }

                    string? activationError = ActivationManager.ActivateTrial(token);
                    if (activationError == null)
                        AnsiConsole.MarkupLine("[green]45-day trial activated successfully![/]");
                    else
                        AnsiConsole.MarkupLine($"[red]{Markup.Escape(activationError)}[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Connection error: {Markup.Escape(ex.Message)}[/]");
                }
            });
        }

        private static string? ExtractJsonField(string json, string fieldName)
        {
            string pattern = "\"" + fieldName + "\"";
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

        private static void FetchToken(string key, string machineId, string outputPath)
        {
            AnsiConsole.Status().Start("Fetching token...", ctx =>
            {
                string? error = ActivationManager.FetchToken(key, machineId, outputPath);
                if (error == null)
                    AnsiConsole.MarkupLine($"[green]Token saved to {Markup.Escape(outputPath)}[/]");
                else
                    AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
            });
        }
    }
}
