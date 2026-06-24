using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LensHH.App.Session;
using LensHH.App.ViewModels;
using LensHH.Core.Activation;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Models;

namespace LensHH.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Select all text in DataGrid TextBoxes on focus for easy editing
        AddHandler(GotFocusEvent, (_, e) =>
        {
            if (e.Source is TextBox tb && tb.FindAncestorOfType<DataGrid>() != null)
                Dispatcher.UIThread.Post(() => tb.SelectAll());
        }, RoutingStrategies.Bubble);

        // Window close interception: prompt to save unsaved changes.
        Closing += MainWindow_Closing;

        AboutMenuItem.Header = $"_About {AppCapabilities.ProductName}";
        PopulateExtensionsMenu();
    }

    // Render any host-contributed extension menu items under the neutral "Extensions" menu.
    // Empty in the standard build, so the menu stays hidden. Each item's Invoke runs at click
    // time with the live session, so closures see the currently loaded design.
    private void PopulateExtensionsMenu()
    {
        if (AppExtensions.MenuItems.Count == 0) return;
        ExtensionsMenu.Header = AppExtensions.MenuHeader;
        foreach (var ext in AppExtensions.MenuItems)
        {
            var captured = ext;
            var mi = new MenuItem { Header = captured.Header };
            mi.Click += async (_, _) =>
            {
                try { await captured.Invoke(this, VM.Session); }
                catch (Exception ex) { await ShowMessageBox("Error", ex.Message); }
            };
            ExtensionsMenu.Items.Add(mi);
        }
        ExtensionsMenu.IsVisible = true;
    }

    private bool _confirmedClose;
    private async void MainWindow_Closing(object? sender, Avalonia.Controls.WindowClosingEventArgs e)
    {
        if (_confirmedClose) return;
        e.Cancel = true;
        if (await ConfirmDiscardChangesAsync())
        {
            _confirmedClose = true;
            Close();
        }
    }

    private MainViewModel VM => (MainViewModel)DataContext!;

    // ── Native format: Open / Save / Save As (.lhlt) ──

    private async void NewSystem_Click(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardChangesAsync()) return;
        VM.NewSystem();
    }

    private async void OpenLhlt_Click(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardChangesAsync()) return;
        var patterns = AppCapabilities.NativeOpenExtensions.Select(x => "*." + x).ToArray();
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType($"{AppCapabilities.ProductName} Files") { Patterns = patterns },
            }
        });
        if (files.Count > 0)
        {
            var path = files[0].TryGetLocalPath();
            if (path != null) VM.OpenFile(path, NativeFormatFromPath(path));
        }
    }

    // The native format key is simply the file extension (without the dot, lowercased);
    // GuiSession maps it to a built-in or host-registered reader. Falls back to "lhlt".
    private static string NativeFormatFromPath(string path)
    {
        var ext = System.IO.Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return string.IsNullOrEmpty(ext) ? "lhlt" : ext;
    }

    private void SaveLhlt_Click(object? sender, RoutedEventArgs e)
    {
        string ext = AppCapabilities.NativeFileExtension;
        var fp = VM.Session.FilePath;
        // Save in place only when the current file already IS the host's native format;
        // otherwise (e.g. the file was opened in a different native extension) prompt Save
        // As so the result lands in a new, explicitly-named file.
        if (fp != null && Path.GetExtension(fp).TrimStart('.').Equals(ext, StringComparison.OrdinalIgnoreCase))
            VM.SaveFile(fp, ext);
        else
            SaveAsLhlt_Click(sender, e);
    }

    private async void SaveAsLhlt_Click(object? sender, RoutedEventArgs e)
        => await SaveAsLhltAsync();

    /// <summary>
    /// Show the Save As dialog and write to the chosen path. Returns true
    /// if the user picked a path and the save succeeded; false if they
    /// cancelled the dialog. Used both by the menu handler and by the
    /// dirty-check prompt.
    /// </summary>
    private async Task<bool> SaveAsLhltAsync()
    {
        string ext = AppCapabilities.NativeFileExtension;
        var suggested = VM.Session.CurrentFileName;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save As",
            DefaultExtension = ext,
            SuggestedFileName = suggested != null ? $"{suggested}.{ext}" : null,
            FileTypeChoices = new[]
            {
                new FilePickerFileType($"{AppCapabilities.ProductName} Files") { Patterns = new[] { "*." + ext } }
            }
        });
        if (file == null) return false;
        var path = file.TryGetLocalPath();
        if (path == null) return false;
        VM.SaveFile(path, ext);
        return true;
    }

    /// <summary>
    /// If the system has unsaved changes, prompt Save / Don't Save / Cancel.
    /// Returns true if the caller should proceed with the action (user
    /// chose Save and the save succeeded, or Don't Save). Returns false
    /// to abort (Cancel, or Save dialog cancelled). Uses MsBox.Avalonia
    /// for proper modal lifecycle handling.
    /// </summary>
    private async Task<bool> ConfirmDiscardChangesAsync()
    {
        if (!VM.Session.IsDirty) return true;
        var name = VM.Session.CurrentFileName ?? "the current design";
        var box = MessageBoxManager.GetMessageBoxCustom(new MessageBoxCustomParams
        {
            ContentTitle = "Save Changes",
            ContentMessage = $"Save changes to {name}?",
            ButtonDefinitions = new[]
            {
                new ButtonDefinition { Name = "Save", IsDefault = true },
                new ButtonDefinition { Name = "Don't Save" },
                new ButtonDefinition { Name = "Cancel", IsCancel = true },
            },
            Icon = MsBox.Avalonia.Enums.Icon.Question,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        });
        var result = await box.ShowWindowDialogAsync(this);
        return result switch
        {
            "Save" => await SaveAsLhltAsync(),
            "Don't Save" => true,
            _ => false, // Cancel, X, or any unmapped result
        };
    }

    // ── Import (foreign formats → load into session) ──

    private async void ImportZmx_Click(object? sender, RoutedEventArgs e)
        => await ImportFile("Import Zemax", "*.zmx", "Zemax Files", "zmx");

    private async void ImportCodeV_Click(object? sender, RoutedEventArgs e)
        => await ImportFile("Import Code V", "*.seq", "Code V Files", "codev");

    private async void ImportOslo_Click(object? sender, RoutedEventArgs e)
        => await ImportFile("Import OSLO", "*.len", "OSLO Files", "oslo");

    private async void ImportOptalix_Click(object? sender, RoutedEventArgs e)
        => await ImportFile("Import Optalix", "*.otx", "Optalix Files", "optalix");

    private async void ImportOptiland_Click(object? sender, RoutedEventArgs e)
        => await ImportFile("Import Optiland", "*.json", "Optiland Files", "optiland");

    private async System.Threading.Tasks.Task ImportFile(string title, string pattern, string label, string format)
    {
        if (!await ConfirmDiscardChangesAsync()) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(label) { Patterns = new[] { pattern } },
            }
        });
        if (files.Count > 0)
        {
            var path = files[0].TryGetLocalPath();
            if (path != null)
            {
                VM.OpenFile(path, format);
                await ShowImportSubstitutionsAsync();
            }
        }
    }

    /// <summary>
    /// If the just-opened file's importer carried numeric glass codes
    /// that GuiSession's substitution pass resolved to catalog names,
    /// surface a summary message box so the user knows which surfaces
    /// were rewritten and to what.
    /// </summary>
    private async System.Threading.Tasks.Task ShowImportSubstitutionsAsync()
    {
        var subs = VM.Session.LastImportSubstitutions;
        if (subs.Count == 0) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{subs.Count} glass substitution(s) made on import:");
        sb.AppendLine();
        foreach (var s in subs)
        {
            sb.AppendLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "  Surface {0}:  {1}  ->  {2}    (Δnd={3:+0.0000;-0.0000}, ΔV={4:+0.0;-0.0})",
                s.SurfaceIndex, s.Original, s.Replacement, s.DeltaNd, s.DeltaVd));
        }
        sb.AppendLine();
        sb.Append("These approximate matches were chosen from the loaded glass catalogs. Verify the choices before optimization.");

        await ShowMessageBox("Glass substitutions", sb.ToString());
    }

    // ── Export (session → foreign formats) ──

    private async void ExportZmx_Click(object? sender, RoutedEventArgs e)
        => await ExportFile("Export Zemax", "zmx", "Zemax Files", "zmx");

    private async void ExportCodeV_Click(object? sender, RoutedEventArgs e)
        => await ExportFile("Export Code V", "seq", "Code V Files", "codev");

    private async void ExportOslo_Click(object? sender, RoutedEventArgs e)
        => await ExportFile("Export OSLO", "len", "OSLO Files", "oslo");

    private async void ExportOptalix_Click(object? sender, RoutedEventArgs e)
        => await ExportFile("Export Optalix", "otx", "Optalix Files", "optalix");

    private async void ExportOptiland_Click(object? sender, RoutedEventArgs e)
        => await ExportFile("Export Optiland", "json", "Optiland Files", "optiland");

    private async System.Threading.Tasks.Task ExportFile(string title, string ext, string label, string format)
    {
        var suggested = VM.Session.CurrentFileName;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            DefaultExtension = ext,
            SuggestedFileName = suggested != null ? $"{suggested}.{ext}" : null,
            FileTypeChoices = new[]
            {
                new FilePickerFileType(label) { Patterns = new[] { $"*.{ext}" } }
            }
        });
        if (file != null)
        {
            var path = file.TryGetLocalPath();
            if (path != null) VM.SaveFile(path, format);
        }
    }

    // ── Tab switching ──

    private void TabControl_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // When switching to 2D Layout tab, render only if the design changed
        if (sender is TabControl tc && tc.SelectedIndex == 1)
        {
            VM.Layout.RenderIfDirty();
        }
    }

    // ── Other ──

    private async void OpenSamples_Click(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardChangesAsync()) return;
        // Locate the bundled samples folder. Its position relative to the
        // executable differs per package, so search in priority order (mirrors
        // GuiSession.LoadGlassCatalogs):
        //   1. LENSHH_SAMPLES env var — set by the Linux AppImage AppRun.
        //   2. samples/ next to the exe (Windows installer: {app}\samples).
        //   3. AppImage:  usr/bin/LensHH.App -> ../share/lenshh-lt/samples
        //   4. macOS .app: Contents/MacOS -> <package root>/samples (3 up).
        //   5. Dev layout: walk up from bin/<cfg>/net8.0 to the repo root.
        // Previously only #2 (+ the dev path) was checked, so the Samples menu
        // opened the default folder on Linux and macOS.
        string exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new System.Collections.Generic.List<string>();
        var envSamples = Environment.GetEnvironmentVariable("LENSHH_SAMPLES");
        if (!string.IsNullOrEmpty(envSamples)) candidates.Add(envSamples);
        candidates.Add(Path.Combine(exeDir, "samples"));
        candidates.Add(Path.Combine(exeDir, "..", "share", "lenshh-lt", "samples"));
        candidates.Add(Path.Combine(exeDir, "..", "..", "..", "samples"));
        candidates.Add(Path.Combine(exeDir, "..", "..", "..", "..", "..", "samples"));

        string samplesPath = "";
        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (Directory.Exists(full)) { samplesPath = full; break; }
        }

        var options = new FilePickerOpenOptions
        {
            Title = "Open Sample Lens",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("LensHH-LT Files") { Patterns = new[] { "*.lhlt" } },
            }
        };

        if (Directory.Exists(samplesPath))
            options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(samplesPath);

        var files = await StorageProvider.OpenFilePickerAsync(options);
        if (files.Count > 0)
        {
            var path = files[0].TryGetLocalPath();
            if (path != null) VM.OpenFile(path, "lhlt");
        }
    }

    private void Exit_Click(object? sender, RoutedEventArgs e) => Close();

    // ── License Activation ──

    private async void StartTrial_Click(object? sender, RoutedEventArgs e)
    {
        if (ActivationManager.IsActivated)
        {
            await ShowMessageBox("Already Activated", "LensHH-LT is already activated.");
            return;
        }
        if (TrialClock.IsTrialActive)
        {
            await ShowMessageBox("Trial Active",
                $"Your trial is already active ({TrialClock.DaysRemaining} days remaining).");
            return;
        }

        var dialog = new TrialActivationDialog();
        var result = await dialog.ShowDialog<string?>(this);
        if (result == null) return;

        if (result == TrialActivationDialog.OfflineActivatedResult)
        {
            // Offline path: dialog already verified the token file and activated
            // the native engine via ActivationManager.ActivateOffline.
            VM.RefreshLicenseMenuState();
            await ShowMessageBox("Trial Activated",
                "Your 45-day trial has been activated. Enjoy LensHH-LT!");
            return;
        }

        string? error = await Task.Run(() => ActivationManager.ActivateTrial(result));
        if (error == null)
        {
            VM.RefreshLicenseMenuState();
            await ShowMessageBox("Trial Activated",
                "Your 45-day trial has been activated. Enjoy LensHH-LT!");
        }
        else
            await ShowMessageBox("Trial Activation Failed", error);
    }

    private async void ActivateLicense_Click(object? sender, RoutedEventArgs e)
    {
        if (ActivationManager.IsActivated)
        {
            await ShowMessageBox("License Status", "LensHH-LT is already activated on this machine.");
            return;
        }

        var dialog = new ActivationDialog();
        var result = await dialog.ShowDialog<string?>(this);
        if (!string.IsNullOrWhiteSpace(result))
        {
            string? error = await Task.Run(() => ActivationManager.Activate(result.Trim()));
            if (error == null)
            {
                VM.RefreshLicenseMenuState();
                await ShowMessageBox("Activation Successful", "LensHH-LT has been activated. Thank you!");
            }
            else
                await ShowMessageBox("Activation Failed", error);
        }
    }

    // Offline activation from a provided, signed token file (ActivationManager.ActivateOffline).
    // Product-neutral: any host can activate from a license file; for a token-file-only host it
    // is the primary activation path.
    private async void ActivateFromFile_Click(object? sender, RoutedEventArgs e)
    {
        if (ActivationManager.IsActivated)
        {
            await ShowMessageBox("License Status", "This machine is already activated.");
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select License File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("License file") { Patterns = new[] { "*.json", "*.lic", "*.token" } },
                new FilePickerFileType("All files") { Patterns = new[] { "*" } },
            },
        });
        if (files == null || files.Count == 0) return;

        string path = files[0].Path.LocalPath;
        string? error = await Task.Run(() => ActivationManager.ActivateOffline(path));
        if (error == null)
        {
            VM.RefreshLicenseMenuState();
            await ShowMessageBox("Activation Successful", "License activated from file. Thank you!");
        }
        else
            await ShowMessageBox("Activation Failed", error);
    }

    private async void DeactivateLicense_Click(object? sender, RoutedEventArgs e)
    {
        if (!ActivationManager.IsActivated)
        {
            await ShowMessageBox("Not Activated", "LensHH-LT is not activated on this machine.");
            return;
        }

        // Prompt for license key to confirm deactivation
        var dialog = new ActivationDialog { Title = "Deactivate License" };
        var result = await dialog.ShowDialog<string?>(this);
        if (!string.IsNullOrWhiteSpace(result))
        {
            string? error = await Task.Run(() => ActivationManager.Deactivate(result.Trim()));
            if (error == null)
            {
                VM.RefreshLicenseMenuState();
                await ShowMessageBox("Deactivation Successful",
                    "This machine has been deactivated. The seat is now free for another machine.");
            }
            else
                await ShowMessageBox("Deactivation Failed", error);
        }
    }

    /// <summary>
    /// Walk upward from AppContext.BaseDirectory looking for a docs/
    /// folder containing the requested relative path. This handles every
    /// plausible launch configuration — installed ({app}/), Debug bin
    /// (bin/Debug/net8.0/), Release bin with RID (bin/Release/net8.0/
    /// win-x64/), dotnet run, and published self-contained layouts — by
    /// probing 0..MaxUp levels up until a match is found, so the exact
    /// depth to the repo root never has to be hard-coded.
    /// </summary>
    private static string? FindDocsFile(params string[] relativePathSegments)
    {
        const int MaxUp = 10;
        var rel = System.IO.Path.Combine(relativePathSegments);
        string dir = AppContext.BaseDirectory;
        for (int up = 0; up <= MaxUp; up++)
        {
            try
            {
                var candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, rel));
                if (System.IO.File.Exists(candidate)) return candidate;
                var parent = System.IO.Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            catch { break; }
        }
        return null;
    }

    private static async Task OpenWithShell(string filePath)
    {
        await Task.Run(() =>
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        });
    }

    /// <summary>
    /// Open the bundled User Guide PDF in the system's default PDF viewer.
    /// Installed location: {app}\docs\LensHH-LT-UserGuide.pdf. Dev builds:
    /// resolved by walking up from AppContext.BaseDirectory.
    /// </summary>
    private async void UserGuide_Click(object? sender, RoutedEventArgs e)
    {
        var path = FindDocsFile("docs", "LensHH-LT-UserGuide.pdf");
        if (path == null)
        {
            await ShowMessageBox("User Guide Not Found",
                "docs\\LensHH-LT-UserGuide.pdf was not found starting from " + AppContext.BaseDirectory
                + ".\n\nRun installer\\build-installer.bat (which also invokes docs\\build\\build-pdf.bat) to generate it.");
            return;
        }
        try { await OpenWithShell(path); }
        catch (Exception ex)
        {
            await ShowMessageBox("Could Not Open PDF",
                $"Failed to open the User Guide:\n{ex.Message}\n\nPath: {path}");
        }
    }

    /// <summary>
    /// Open the searchable single-file HTML help bundle in the system's
    /// default browser. Self-contained (lunr.js + index inlined), works
    /// from file:// on Windows/Linux/macOS.
    /// </summary>
    private async void HelpIndex_Click(object? sender, RoutedEventArgs e)
    {
        var path = FindDocsFile("docs", "html", "LensHH-LT-Help.html");
        if (path == null)
        {
            await ShowMessageBox("Help Index Not Found",
                "docs\\html\\LensHH-LT-Help.html was not found starting from " + AppContext.BaseDirectory
                + ".\n\nRun installer\\build-installer.bat to generate it — the HTML bundle is "
                + "produced by docs\\build\\build-help.js.");
            return;
        }
        try { await OpenWithShell(path); }
        catch (Exception ex)
        {
            await ShowMessageBox("Could Not Open Help",
                $"Failed to open the help index:\n{ex.Message}\n\nPath: {path}");
        }
    }

    private async void LicenseStatus_Click(object? sender, RoutedEventArgs e)
    {
        var info = ActivationManager.GetLicenseInfo();
        bool activated = ActivationManager.IsActivated;

        string message;
        if (info == null && TrialClock.IsTrialActive)
        {
            message = $"Local trial\n\nDays remaining: {TrialClock.DaysRemaining}";
        }
        else if (info == null)
        {
            message = "No active license.\n\nUse Help ▸ Start Free Trial, or Help ▸ Activate License.";
        }
        else
        {
            string type = (info.LicenseType ?? "").ToLowerInvariant();
            string expiry = FormatExpires(info.Expires);
            switch (type)
            {
                case "trial":
                    message = $"Trial license\n\nDays remaining: {info.DaysUntilExpiry}\nExpires: {expiry}";
                    break;
                case "perpetual":
                    message = $"Perpetual license\n\nActivation key: {info.Key}\nExpires: Never";
                    break;
                case "educational":
                    message = $"Educational license\n\nActivation key: {info.Key}\nDays remaining: {info.DaysUntilExpiry}\nExpires: {expiry}";
                    break;
                default:
                    message = $"License type: {info.LicenseType ?? "unknown"}\nKey: {info.Key}\nExpires: {expiry}";
                    break;
            }
            if (!activated)
                message += "\n\nThis license is registered to a different computer, or has expired."
                         + "\nTo transfer it to this machine or renew it, contact support@synapseoptics.com"
                         + " with your license key.";
        }

        await ShowMessageBox("License Status", message);
    }

    private static string FormatExpires(string iso)
    {
        if (string.IsNullOrEmpty(iso)) return "—";
        return DateTime.TryParse(iso, out var dt) ? dt.ToString("yyyy-MM-dd") : iso;
    }

    private async void About_Click(object? sender, RoutedEventArgs e)
    {
        string status;
        if (ActivationManager.IsActivated && !TrialClock.IsTrialActive)
            status = "Activated";
        else if (TrialClock.IsTrialActive)
            status = $"Trial ({TrialClock.DaysRemaining} days remaining)";
        else if (TrialClock.IsTrialExpired)
            status = "Trial expired";
        else
            status = "Not activated";

        var asmVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        string version = AppCapabilities.ProductVersion
            ?? (asmVersion != null
                ? $"{asmVersion.Major}.{asmVersion.Minor}.{asmVersion.Build}"
                : "unknown");

        string details = string.IsNullOrWhiteSpace(AppCapabilities.AboutDetails)
            ? ""
            : AppCapabilities.AboutDetails.TrimEnd() + "\n\n";

        await ShowMessageBox($"About {AppCapabilities.ProductName}",
            $"{AppCapabilities.ProductName} - {AppCapabilities.ProductTagline}\n" +
            $"Synapse Optics\n\n" +
            details +
            $"Version: {version}\n" +
            $"License status: {status}\n" +
            $"Machine ID: {ActivationManager.GetMachineFingerprint().Substring(0, 16)}...\n\n" +
            $"synapseoptics.com");
    }

    private async Task ShowMessageBox(string title, string message)
    {
        // Auto-grow vertically so callers don't have to estimate the line
        // count up front. Earlier this was a fixed Height=200 which clipped
        // the About dialog and the import "Glass substitutions" summary
        // (which can have one row per substituted surface). The MaxHeight +
        // ScrollViewer combo lets dozens-of-surface lists stay on-screen.
        var textBlock = new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        var scroller = new ScrollViewer
        {
            Content = textBlock,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            MaxHeight = 500
        };
        var okButton = new Button
        {
            Content = "OK",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        var msgBox = new Window
        {
            Title = title,
            // 560 was chosen so a single substitution row
            // ("Surface N:  GLASS_A  ->  GLASS_B  (Δnd=±0.0000, ΔV=±0.0)")
            // fits without ugly mid-string wrapping.
            Width = 560,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            Icon = new Avalonia.Controls.WindowIcon(
                Avalonia.Platform.AssetLoader.Open(new Uri("avares://LensHH.App/Assets/icon_256.png"))),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 15,
                Children = { scroller, okButton }
            }
        };
        okButton.Click += (_, _) => msgBox.Close();
        await msgBox.ShowDialog(this);
    }

    private async void WavelengthEditor_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new WavelengthEditorDialog { DataContext = new WavelengthEditorViewModel(VM.Session) };
        await dialog.ShowDialog(this);
    }

    private async void FieldEditor_Click(object? sender, RoutedEventArgs e)
    {
        // A host may substitute its own field editor (e.g. an edition with extra columns)
        // via AppExtensions.FieldEditorFactory; the standard build uses the built-in dialog.
        var dialog = AppExtensions.FieldEditorFactory?.Invoke(VM.Session)
                     ?? new FieldEditorDialog { DataContext = new FieldEditorViewModel(VM.Session) };
        await dialog.ShowDialog(this);
    }

    private async void SystemEditor_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new SystemEditorDialog { DataContext = new SystemEditorViewModel(VM.Session) };
        await dialog.ShowDialog(this);
    }

    private async void CatalogPreference_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new CatalogPreferenceDialog { DataContext = new CatalogPreferenceViewModel(VM.Session) };
        await dialog.ShowDialog(this);
    }

    // ── Insert Lens from File ─────────────────────────────────────────────

    private async void InsertLensFromFile_Click(object? sender, RoutedEventArgs e)
        => await InsertLensFromFileAsync(reversed: false);

    private async void InsertLensFromFileReversed_Click(object? sender, RoutedEventArgs e)
        => await InsertLensFromFileAsync(reversed: true);

    /// <summary>
    /// Open a file picker, load a .lhlt, extract its lens vertices (skipping
    /// OBJ + dummy stop + IMG), optionally reverse them, and splice into the
    /// current system after the currently-selected surface. The host's stop
    /// surface is left untouched; merit-function operand and pickup surface
    /// refs are bumped automatically via SurfaceIndexUpdater.
    /// </summary>
    private async Task InsertLensFromFileAsync(bool reversed)
    {
        var sys = VM.Session.System;
        if (sys == null || sys.Surfaces.Count < 2)
        {
            await ShowMessage("Insert Lens", "No host system loaded. Open or create a system first.");
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = reversed ? "Insert Lens (Reversed) — pick source .lhlt"
                             : "Insert Lens — pick source .lhlt",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("LensHH-LT Files") { Patterns = new[] { "*.lhlt" } },
            }
        });
        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath();
        if (path == null) return;

        try
        {
            var loaded = LensHH.Core.IO.LhltReader.Read(path);
            var vertices = LensHH.Core.IO.LensInsertHelpers.ExtractLensVertices(loaded.System, out string? extractError);
            if (vertices == null)
            {
                await ShowMessage("Insert Lens", $"Couldn't extract lens vertices from {System.IO.Path.GetFileName(path)}: {extractError}");
                return;
            }
            if (vertices.Count == 0)
            {
                await ShowMessage("Insert Lens", "Source file has no optical vertices between stop and IMG.");
                return;
            }
            if (reversed) vertices = LensHH.Core.IO.LensInsertHelpers.ReverseVertexGroup(vertices);

            // Insertion point: after the currently-selected row, or before IMG.
            int afterSurface;
            var sel = VM.SurfaceEditor.SelectedSurface;
            if (sel != null && sel.Index >= 0 && sel.Index < sys.Surfaces.Count - 1)
                afterSurface = sel.Index;
            else
                afterSurface = sys.Surfaces.Count - 2; // last lens vertex = just before IMG

            // Splice — match insert_stock_lens convention: last vertex T=0 (touches next existing surface).
            vertices[vertices.Count - 1].Thickness = 0.0;
            int insertAt = afterSurface + 1;
            for (int i = 0; i < vertices.Count; i++)
            {
                sys.Surfaces.Insert(insertAt + i, vertices[i]);
                LensHH.Core.Models.SurfaceIndexUpdater.OnSurfaceInserted(
                    insertAt + i, sys, VM.Session.MeritFunction, VM.Session.ConfigEditor);
            }
            for (int i = 0; i < sys.Surfaces.Count; i++) sys.Surfaces[i].Index = i;

            VM.SurfaceEditor.Refresh();
            VM.Session.NotifySystemChanged("structure");
        }
        catch (Exception ex)
        {
            await ShowMessage("Insert Lens", $"Failed: {ex.Message}");
        }
    }

    private async Task ShowMessage(string title, string body)
    {
        var dlg = new Avalonia.Controls.Window
        {
            Title = title,
            Width = 500, Height = 160,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
            Content = new Avalonia.Controls.StackPanel
            {
                Margin = new Avalonia.Thickness(12),
                Spacing = 12,
                Children =
                {
                    new Avalonia.Controls.TextBlock { Text = body, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new Avalonia.Controls.Button
                    {
                        Content = "OK",
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    }
                }
            }
        };
        ((Avalonia.Controls.Button)((Avalonia.Controls.StackPanel)dlg.Content).Children[1]).Click += (_, _) => dlg.Close();
        await dlg.ShowDialog(this);
    }

    private async void SetClearThickness_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new SetClearVariablesDialog
        {
            DataContext = new SetClearVariablesViewModel(VM.Session, VariableMode.Thickness)
        };
        await dialog.ShowDialog(this);
    }

    private async void SetClearCurvature_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new SetClearVariablesDialog
        {
            DataContext = new SetClearVariablesViewModel(VM.Session, VariableMode.Curvature)
        };
        await dialog.ShowDialog(this);
    }

    private async void SetClearAperture_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new SetClearApertureDialog
        {
            DataContext = new SetClearApertureViewModel(VM.Session)
        };
        await dialog.ShowDialog(this);
    }

    private async void ScaleLens_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new ScaleLensDialog
        {
            DataContext = new ScaleLensViewModel(VM.Session)
        };
        await dialog.ShowDialog(this);
    }

    private void ClearAllVariables_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var surf in VM.Session.System.Surfaces)
        {
            surf.CurvatureVariable = false;
            surf.ThicknessVariable = false;
            surf.ConicVariable = false;
            for (int i = 0; i < surf.AsphericVariable.Length; i++)
                surf.AsphericVariable[i] = false;
        }
        VM.Session.NotifySystemChanged("properties");
    }

    private async void VariableEditor_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new VariableEditorDialog
        {
            DataContext = new VariableEditorViewModel(VM.Session)
        };
        await dialog.ShowDialog(this);
    }

    private async void GlassSubstitution_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new GlassSubstitutionDialog
        {
            DataContext = new GlassSubstitutionViewModel(VM.Session)
        };
        await dialog.ShowDialog(this);
    }

    private void MeritFunctionTab_Click(object? sender, RoutedEventArgs e)
    {
        VM.ShowMeritFunction();
    }

    private async void MultistartOptimization_Click(object? sender, RoutedEventArgs e)
    {
        var snapshot = VM.Session.SnapshotVariableValues();
        // Also snapshot glass materials
        var glassSnapshot = new System.Collections.Generic.Dictionary<int, string>();
        for (int i = 0; i < VM.Session.System.Surfaces.Count; i++)
        {
            var mat = VM.Session.System.Surfaces[i].Material;
            if (!string.IsNullOrEmpty(mat))
                glassSnapshot[i] = mat;
        }

        var vm = new MultistartDialogViewModel(VM.Session);
        var dialog = new MultistartDialog { DataContext = vm };
        await dialog.ShowDialog(this);

        if (!vm.Accepted)
        {
            VM.Session.RestoreVariableValues(snapshot);
            foreach (var kvp in glassSnapshot)
            {
                if (kvp.Key < VM.Session.System.Surfaces.Count)
                    VM.Session.System.Surfaces[kvp.Key].Material = kvp.Value;
            }
        }

        VM.Session.NotifySystemChanged("optimization");
    }

    private async void GlobalSearch_Click(object? sender, RoutedEventArgs e)
    {
        // Global Search works entirely on internal clones — it never mutates the
        // live system during the run, so no snapshot/restore is needed here. Only
        // an explicit "Apply this design" inside the dialog changes the system.
        var vm = new GlobalSearchDialogViewModel(VM.Session);
        var dialog = new GlobalSearchDialog { DataContext = vm };
        await dialog.ShowDialog(this);

        if (vm.Accepted)
            VM.Session.NotifySystemChanged("global-search");
    }

    private async void DePipeline_Click(object? sender, RoutedEventArgs e)
    {
        // The DE pipeline works on internal clones — it never mutates the live
        // system during the run. Only an explicit "Apply this design" inside the
        // dialog changes the system.
        var vm = new DePipelineDialogViewModel(VM.Session);
        var dialog = new DePipelineDialog { DataContext = vm };
        await dialog.ShowDialog(this);

        if (vm.Accepted)
            VM.Session.NotifySystemChanged("de-pipeline");
    }

    private async void SplitElement_Click(object? sender, RoutedEventArgs e)
    {
        var snapshot = VM.Session.SnapshotSystem();
        var vm = new SplitElementDialogViewModel(VM.Session);
        var dialog = new SplitElementDialog { DataContext = vm };
        await dialog.ShowDialog(this);

        if (!vm.Accepted)
            VM.Session.RestoreSystemSnapshot(snapshot);

        VM.Session.NotifySystemChanged("optimization");
    }

    private async void SynthesisBySpc_Click(object? sender, RoutedEventArgs e)
    {
        var snapshot = VM.Session.SnapshotSystem();
        var vm = new SpcSynthesisDialogViewModel(VM.Session);
        var dialog = new SpcSynthesisDialog { DataContext = vm };
        await dialog.ShowDialog(this);

        if (!vm.Accepted)
            VM.Session.RestoreSystemSnapshot(snapshot);

        VM.Session.NotifySystemChanged("optimization");
    }

    private async void AsphereSearch_Click(object? sender, RoutedEventArgs e)
    {
        var snapshot = VM.Session.SnapshotSystem();
        var vm = new AsphereSearchDialogViewModel(VM.Session);
        var dialog = new AsphereSearchDialog { DataContext = vm };
        await dialog.ShowDialog(this);

        if (!vm.Accepted)
            VM.Session.RestoreSystemSnapshot(snapshot);

        VM.Session.NotifySystemChanged("optimization");
    }

    private async void BasinHoppingHjLm_Click(object? sender, RoutedEventArgs e)
    {
        var snapshot = VM.Session.SnapshotSystem();
        var vm = new BasinHoppingHjLmDialogViewModel(VM.Session);
        var dialog = new BasinHoppingHjLmDialog { DataContext = vm };
        await dialog.ShowDialog(this);

        if (!vm.Accepted)
            VM.Session.RestoreSystemSnapshot(snapshot);

        VM.Session.NotifySystemChanged("optimization");
    }

    private async void CreateGlassCatalogue_Click(object? sender, RoutedEventArgs e)
    {
        var vm = new GlassCatalogGeneratorViewModel();
        var dialog = new GlassCatalogGeneratorDialog { DataContext = vm };
        await dialog.ShowDialog(this);
    }

    private async void CherryPickCatalogue_Click(object? sender, RoutedEventArgs e)
    {
        var vm = new CherryPickCatalogViewModel();
        var dialog = new CherryPickCatalogDialog { DataContext = vm };
        await dialog.ShowDialog(this);
    }

    private async void LocalOptimization_Click(object? sender, RoutedEventArgs e)
    {
        // Snapshot current variable values so we can revert on Cancel
        var snapshot = VM.Session.SnapshotVariableValues();

        var vm = new OptimizationDialogViewModel(VM.Session);
        var dialog = new OptimizationDialog { DataContext = vm };
        await dialog.ShowDialog(this);

        if (!vm.Accepted)
        {
            // Revert to pre-optimization state
            VM.Session.RestoreVariableValues(snapshot);
        }

        // Refresh all views
        VM.Session.NotifySystemChanged("optimization");
    }

    private async void FftMtfShowTable_Click(object? sender, RoutedEventArgs e)
    {
        var fieldResults = VM.FftMtf.LastFieldResults;
        var fieldLabels = VM.FftMtf.LastFieldLabels;
        if (fieldResults == null || fieldLabels == null || fieldResults.Length == 0) return;

        // Show field selector dialog first
        int fieldIdx = 0;
        if (fieldResults.Length > 1)
        {
            var picker = new DataTableDialog();
            var pickerColumns = new[] { "Field" };
            var pickerRows = new System.Collections.Generic.List<string[]>();
            for (int f = 0; f < fieldResults.Length; f++)
                pickerRows.Add(new[] { f < fieldLabels.Length ? fieldLabels[f] : $"Field {f + 1}" });
            picker.SetData("Select Field", pickerColumns, pickerRows);
            picker.Title = "Select Field — double-click or close to use first";
            picker.Width = 300;
            picker.Height = 250;
            // For simplicity, show a small dialog — user closes it and we use field 0
            // TODO: proper field picker. For now, show all fields as separate tables.
        }

        // Build one table per field, concatenated
        string freqUnit = VM.Session.System.IsAfocal ? "cy/mrad" : "cy/mm";
        var allRows = new System.Collections.Generic.List<string[]>();
        var columns = new System.Collections.Generic.List<string>();

        // Determine max row count across all fields
        int maxRows = 0;
        foreach (var fr in fieldResults)
        {
            int n = Math.Max(fr.Points.Count,
                fr.DiffractionLimit.Count);
            if (n > maxRows) maxRows = n;
        }

        // Build columns: per field pair (Freq T, MTF T, Freq S, MTF S) + DL for field 0
        bool isOnAxis0 = fieldResults.Length > 0 &&
            Math.Abs(VM.Session.System.Fields[0].Y) < 1e-10;

        for (int f = 0; f < fieldResults.Length; f++)
        {
            string lbl = f < fieldLabels.Length ? fieldLabels[f] : $"F{f + 1}";
            columns.Add(lbl + " Freq T");
            columns.Add(lbl + " MTF T");
            columns.Add(lbl + " Freq S");
            columns.Add(lbl + " MTF S");
            if (f == 0 && isOnAxis0 && fieldResults[0].DiffractionLimit.Count > 0)
            {
                columns.Add("DL Freq");
                columns.Add("DL");
            }
        }

        for (int i = 0; i < maxRows; i++)
        {
            var vals = new System.Collections.Generic.List<string>();
            for (int f = 0; f < fieldResults.Length; f++)
            {
                var pts = fieldResults[f].Points;
                if (i < pts.Count)
                {
                    vals.Add(Rendering.LabelFormat.Auto(pts[i].SpatialFrequency));
                    vals.Add(Rendering.LabelFormat.Auto(pts[i].Tangential));
                    vals.Add(Rendering.LabelFormat.Auto(pts[i].SpatialFrequency));
                    vals.Add(Rendering.LabelFormat.Auto(pts[i].Sagittal));
                }
                else
                {
                    vals.Add(""); vals.Add(""); vals.Add(""); vals.Add("");
                }

                if (f == 0 && isOnAxis0 && fieldResults[0].DiffractionLimit.Count > 0)
                {
                    var dl = fieldResults[0].DiffractionLimit;
                    if (i < dl.Count)
                    {
                        vals.Add(Rendering.LabelFormat.Auto(dl[i].SpatialFrequency));
                        vals.Add(Rendering.LabelFormat.Auto(dl[i].Tangential));
                    }
                    else
                    {
                        vals.Add(""); vals.Add("");
                    }
                }
            }
            allRows.Add(vals.ToArray());
        }

        var dialog = new DataTableDialog();
        dialog.SetData("FFT MTF vs Spatial Frequency", columns.ToArray(), allRows);
        dialog.Width = 1200;
        await dialog.ShowDialog(this);
    }

    private void CloseFftMtfTab_Click(object? sender, RoutedEventArgs e)
    {
        VM.FftMtf.IsVisible = false;
        VM.SelectedTabIndex = 0;
    }

    private async void FftMtfVsFieldShowTable_Click(object? sender, RoutedEventArgs e)
    {
        var result = VM.FftMtfVsField.LastResult;
        if (result == null) return;
        await ShowMtfVsFieldTable("FFT MTF vs Field", result);
    }

    private void CloseFftMtfVsFieldTab_Click(object? sender, RoutedEventArgs e)
    {
        VM.FftMtfVsField.IsVisible = false;
        VM.SelectedTabIndex = 0;
    }

    private async void FftMtfVsFocus_Click(object? sender, RoutedEventArgs e)
    {
        VM.FftMtfVsFocus.IsVisible = true;
        VM.SelectedTabIndex = 5; // FFT MTF vs Focus tab
        // Compute is a no-op for afocal (the tab shows a "disabled" overlay).
        await VM.FftMtfVsFocus.ComputeCommand.ExecuteAsync(null);
    }

    private void FftMtfVsFocusGridChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext == null) return;
        if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem item)
        {
            if (int.TryParse(item.Content?.ToString(), out int grid))
                VM.FftMtfVsFocus.GridSize = grid;
        }
    }

    private async void FftMtfVsFocusShowTable_Click(object? sender, RoutedEventArgs e)
    {
        await ShowMtfVsFocusTable("FFT MTF vs Focus",
            VM.FftMtfVsFocus.LastResult, VM.FftMtfVsFocus.LastResults);
    }

    private void CloseFftMtfVsFocusTab_Click(object? sender, RoutedEventArgs e)
    {
        VM.FftMtfVsFocus.IsVisible = false;
        VM.SelectedTabIndex = 0;
    }

    private async void SpotDiagram_Click(object? sender, RoutedEventArgs e)
    {
        VM.SpotDiagram.IsVisible = true;
        VM.SelectedTabIndex = 6; // Spot Diagram tab
        await VM.SpotDiagram.ComputeCommand.ExecuteAsync(null);
    }

    private async void SpotDiagramShowTable_Click(object? sender, RoutedEventArgs e)
    {
        var results = VM.SpotDiagram.LastResults;
        if (results == null || results.Length == 0) return;

        var system = VM.Session.System;
        string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";

        var columns = new[] { "Field (" + fieldUnit + ")", "RMS Radius (\u00b5m)", "GEO Radius (\u00b5m)", "Chief X", "Chief Y" };
        var rows = new System.Collections.Generic.List<string[]>();
        for (int f = 0; f < results.Length; f++)
        {
            var r = results[f];
            double fieldY = f < system.Fields.Count ? system.Fields[f].Y : 0;
            rows.Add(new[]
            {
                LensHH.Rendering.LabelFormat.Auto(fieldY),
                LensHH.Rendering.LabelFormat.Auto(r.RmsRadius * 1000),
                LensHH.Rendering.LabelFormat.Auto(r.GeoRadius * 1000),
                LensHH.Rendering.LabelFormat.Auto(r.ChiefRayX),
                LensHH.Rendering.LabelFormat.Auto(r.ChiefRayY),
            });
        }

        var dialog = new DataTableDialog();
        dialog.SetData("Spot Diagram Summary", columns, rows);
        await dialog.ShowDialog(this);
    }

    private void CloseSpotDiagramTab_Click(object? sender, RoutedEventArgs e)
    {
        VM.SpotDiagram.IsVisible = false;
        VM.SelectedTabIndex = 0;
    }

    private async void Distortion_Click(object? sender, RoutedEventArgs e)
    {
        VM.Distortion.IsVisible = true;
        VM.SelectedTabIndex = 7; // Distortion tab
        await VM.Distortion.ComputeCommand.ExecuteAsync(null);
    }

    private async void DistortionShowTable_Click(object? sender, RoutedEventArgs e)
    {
        var result = VM.Distortion.LastResult;
        if (result == null || result.Points.Count == 0) return;

        var system = VM.Session.System;
        string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";

        var columns = new[] { "Field (" + fieldUnit + ")", "Distortion (%)" };
        var rows = new System.Collections.Generic.List<string[]>();
        foreach (var pt in result.Points)
        {
            rows.Add(new[]
            {
                LensHH.Rendering.LabelFormat.Auto(pt.FieldY),
                LensHH.Rendering.LabelFormat.Auto(pt.Distortion),
            });
        }

        var dialog = new DataTableDialog();
        dialog.SetData("Distortion", columns, rows);
        await dialog.ShowDialog(this);
    }

    private void CloseDistortionTab_Click(object? sender, RoutedEventArgs e)
    {
        VM.Distortion.IsVisible = false;
        VM.SelectedTabIndex = 0;
    }

    private async void FieldCurvature_Click(object? sender, RoutedEventArgs e)
    {
        VM.FieldCurvature.IsVisible = true;
        VM.SelectedTabIndex = 8; // Field Curvature tab
        await VM.FieldCurvature.ComputeCommand.ExecuteAsync(null);
    }

    private async void FieldCurvatureShowTable_Click(object? sender, RoutedEventArgs e)
    {
        var mwResult = VM.FieldCurvature.LastMwResult;
        if (mwResult == null || mwResult.PerWavelength.Count == 0) return;

        var system = VM.Session.System;
        string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";

        // Build columns: Field, then Tangential/Sagittal per wavelength
        var colList = new System.Collections.Generic.List<string> { "Field (" + fieldUnit + ")" };
        string fcWlFmt = "F" + LensHH.Rendering.LabelFormat.WavelengthDigits(mwResult.Wavelengths);
        for (int w = 0; w < mwResult.Wavelengths.Count; w++)
        {
            string wl = mwResult.Wavelengths[w].ToString(fcWlFmt, System.Globalization.CultureInfo.InvariantCulture);
            colList.Add($"T {wl} \u00b5m");
            colList.Add($"S {wl} \u00b5m");
        }

        var rows = new System.Collections.Generic.List<string[]>();
        // Use first wavelength's point count as reference
        int numPts = mwResult.PerWavelength[0].Points.Count;
        for (int i = 0; i < numPts; i++)
        {
            var vals = new System.Collections.Generic.List<string>();
            vals.Add(LensHH.Rendering.LabelFormat.Auto(mwResult.PerWavelength[0].Points[i].FieldY));
            for (int w = 0; w < mwResult.PerWavelength.Count; w++)
            {
                var pts = mwResult.PerWavelength[w].Points;
                if (i < pts.Count)
                {
                    vals.Add(LensHH.Rendering.LabelFormat.Auto(pts[i].TangentialFocus));
                    vals.Add(LensHH.Rendering.LabelFormat.Auto(pts[i].SagittalFocus));
                }
                else
                {
                    vals.Add(""); vals.Add("");
                }
            }
            rows.Add(vals.ToArray());
        }

        var dialog = new DataTableDialog();
        dialog.SetData("Field Curvature", colList.ToArray(), rows);
        dialog.Width = 800;
        await dialog.ShowDialog(this);
    }

    private void CloseFieldCurvatureTab_Click(object? sender, RoutedEventArgs e)
    {
        VM.FieldCurvature.IsVisible = false;
        VM.SelectedTabIndex = 0;
    }

    private async void RelativeIllumination_Click(object? sender, RoutedEventArgs e)
    {
        VM.RelativeIllumination.IsVisible = true;
        VM.SelectedTabIndex = 9; // Relative Illumination tab
        await VM.RelativeIllumination.ComputeCommand.ExecuteAsync(null);
    }

    private async void RelIllumShowTable_Click(object? sender, RoutedEventArgs e)
    {
        var result = VM.RelativeIllumination.LastResult;
        if (result == null || result.Points.Count == 0) return;

        var system = VM.Session.System;
        string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";

        var columns = new[] { "Field (" + fieldUnit + ")", "Relative Illumination", "Effective F/#" };
        var rows = new System.Collections.Generic.List<string[]>();
        foreach (var pt in result.Points)
        {
            rows.Add(new[]
            {
                LensHH.Rendering.LabelFormat.Auto(pt.FieldY),
                LensHH.Rendering.LabelFormat.Auto(pt.RelativeIllumination),
                LensHH.Rendering.LabelFormat.Auto(pt.EffectiveFNumber),
            });
        }

        var dialog = new DataTableDialog();
        dialog.SetData("Relative Illumination", columns, rows);
        await dialog.ShowDialog(this);
    }

    private void CloseRelativeIlluminationTab_Click(object? sender, RoutedEventArgs e)
    {
        VM.RelativeIllumination.IsVisible = false;
        VM.SelectedTabIndex = 0;
    }

    private async void Seidel_Click(object? sender, RoutedEventArgs e)
    {
        VM.Seidel.IsVisible = true;
        VM.SelectedTabIndex = 10; // Seidel tab
        await VM.Seidel.ComputeCommand.ExecuteAsync(null);
    }

    private async void SeidelShowTable_Click(object? sender, RoutedEventArgs e)
    {
        var result = VM.Seidel.LastResult;
        if (result == null || result.SurfaceData.Count == 0) return;

        var columns = new[] { "Surface", "S1 (SA)", "S2 (Coma)", "S3 (Astig)", "S4 (Field Curv)", "S5 (Distortion)", "CL", "CT" };
        var rows = new System.Collections.Generic.List<string[]>();
        foreach (var s in result.SurfaceData)
        {
            rows.Add(new[]
            {
                s.SurfaceIndex.ToString(),
                LensHH.Rendering.LabelFormat.Auto(s.S1),
                LensHH.Rendering.LabelFormat.Auto(s.S2),
                LensHH.Rendering.LabelFormat.Auto(s.S3),
                LensHH.Rendering.LabelFormat.Auto(s.S4),
                LensHH.Rendering.LabelFormat.Auto(s.S5),
                LensHH.Rendering.LabelFormat.Auto(s.CL),
                LensHH.Rendering.LabelFormat.Auto(s.CT),
            });
        }
        // Totals row
        rows.Add(new[]
        {
            "Total",
            LensHH.Rendering.LabelFormat.Auto(result.S1),
            LensHH.Rendering.LabelFormat.Auto(result.S2),
            LensHH.Rendering.LabelFormat.Auto(result.S3),
            LensHH.Rendering.LabelFormat.Auto(result.S4),
            LensHH.Rendering.LabelFormat.Auto(result.S5),
            LensHH.Rendering.LabelFormat.Auto(result.CL),
            LensHH.Rendering.LabelFormat.Auto(result.CT),
        });

        var dialog = new DataTableDialog();
        dialog.SetData("Seidel Aberration Coefficients", columns, rows);
        await dialog.ShowDialog(this);
    }

    private void CloseSeidelTab_Click(object? sender, RoutedEventArgs e)
    {
        VM.Seidel.IsVisible = false;
        VM.SelectedTabIndex = 0;
    }

    private async void GeoMtfVsFreq_Click(object? sender, RoutedEventArgs e)
    {
        VM.GeoMtfVsFreq.IsVisible = true;
        VM.SelectedTabIndex = 11;
        await VM.GeoMtfVsFreq.ComputeCommand.ExecuteAsync(null);
    }

    private async void GeoMtfVsFreqShowTable_Click(object? sender, RoutedEventArgs e)
    {
        var fieldResults = VM.GeoMtfVsFreq.LastFieldResults;
        var fieldLabels = VM.GeoMtfVsFreq.LastFieldLabels;
        if (fieldResults == null || fieldLabels == null || fieldResults.Length == 0) return;

        var allRows = new System.Collections.Generic.List<string[]>();
        var columns = new System.Collections.Generic.List<string>();

        int maxRows = 0;
        foreach (var fr in fieldResults)
        {
            int n = Math.Max(fr.Points.Count, fr.DiffractionLimit.Count);
            if (n > maxRows) maxRows = n;
        }

        bool isOnAxis0 = fieldResults.Length > 0 &&
            Math.Abs(VM.Session.System.Fields[0].Y) < 1e-10;

        for (int f = 0; f < fieldResults.Length; f++)
        {
            string lbl = f < fieldLabels.Length ? fieldLabels[f] : $"F{f + 1}";
            columns.Add(lbl + " Freq T");
            columns.Add(lbl + " MTF T");
            columns.Add(lbl + " Freq S");
            columns.Add(lbl + " MTF S");
            if (f == 0 && isOnAxis0 && fieldResults[0].DiffractionLimit.Count > 0)
            {
                columns.Add("DL Freq");
                columns.Add("DL");
            }
        }

        for (int i = 0; i < maxRows; i++)
        {
            var vals = new System.Collections.Generic.List<string>();
            for (int f = 0; f < fieldResults.Length; f++)
            {
                var pts = fieldResults[f].Points;
                if (i < pts.Count)
                {
                    vals.Add(Rendering.LabelFormat.Auto(pts[i].SpatialFrequency));
                    vals.Add(Rendering.LabelFormat.Auto(pts[i].Tangential));
                    vals.Add(Rendering.LabelFormat.Auto(pts[i].SpatialFrequency));
                    vals.Add(Rendering.LabelFormat.Auto(pts[i].Sagittal));
                }
                else
                {
                    vals.Add(""); vals.Add(""); vals.Add(""); vals.Add("");
                }

                if (f == 0 && isOnAxis0 && fieldResults[0].DiffractionLimit.Count > 0)
                {
                    var dl = fieldResults[0].DiffractionLimit;
                    if (i < dl.Count)
                    {
                        vals.Add(Rendering.LabelFormat.Auto(dl[i].SpatialFrequency));
                        vals.Add(Rendering.LabelFormat.Auto(dl[i].Tangential));
                    }
                    else
                    {
                        vals.Add(""); vals.Add("");
                    }
                }
            }
            allRows.Add(vals.ToArray());
        }

        var dialog = new DataTableDialog();
        dialog.SetData("Geometric MTF vs Spatial Frequency", columns.ToArray(), allRows);
        dialog.Width = 1200;
        await dialog.ShowDialog(this);
    }

    private void CloseGeoMtfVsFreqTab_Click(object? sender, RoutedEventArgs e)
    {
        VM.GeoMtfVsFreq.IsVisible = false;
        VM.SelectedTabIndex = 0;
    }

    private async void GeoMtfVsField_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new FrequencyInputDialog(VM.Session.System.IsAfocal);
        await dialog.ShowDialog(this);
        if (!dialog.Accepted) return;
        var freqs = dialog.GetFrequencies();
        if (freqs.Length == 0) return;

        VM.GeoMtfVsField.Frequencies = freqs;
        VM.GeoMtfVsField.IsVisible = true;
        VM.SelectedTabIndex = 12;
        await VM.GeoMtfVsField.ComputeCommand.ExecuteAsync(null);
    }

    private async void GeoMtfFrequencies_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new FrequencyInputDialog(VM.Session.System.IsAfocal);
        await dialog.ShowDialog(this);
        if (!dialog.Accepted) return;
        var freqs = dialog.GetFrequencies();
        if (freqs.Length > 0)
            VM.GeoMtfVsField.Frequencies = freqs;
    }

    private async void GeoMtfVsFieldShowTable_Click(object? sender, RoutedEventArgs e)
    {
        var result = VM.GeoMtfVsField.LastResult;
        if (result == null) return;
        await ShowMtfVsFieldTable("Geometric MTF vs Field", result);
    }

    private void CloseGeoMtfVsFieldTab_Click(object? sender, RoutedEventArgs e)
    {
        VM.GeoMtfVsField.IsVisible = false;
        VM.SelectedTabIndex = 0;
    }

    private async void GeoMtfVsFocus_Click(object? sender, RoutedEventArgs e)
    {
        VM.GeoMtfVsFocus.IsVisible = true;
        VM.SelectedTabIndex = 13;
        await VM.GeoMtfVsFocus.ComputeCommand.ExecuteAsync(null);
    }

    private async void GeoMtfVsFocusShowTable_Click(object? sender, RoutedEventArgs e)
    {
        await ShowMtfVsFocusTable("Geometric MTF vs Focus",
            VM.GeoMtfVsFocus.LastResult, VM.GeoMtfVsFocus.LastResults);
    }

    private void CloseGeoMtfVsFocusTab_Click(object? sender, RoutedEventArgs e)
    {
        VM.GeoMtfVsFocus.IsVisible = false;
        VM.SelectedTabIndex = 0;
    }

    private async void FftMtf_Click(object? sender, RoutedEventArgs e)
    {
        VM.FftMtf.IsVisible = true;
        VM.SelectedTabIndex = 3; // FFT MTF tab
        await VM.FftMtf.ComputeCommand.ExecuteAsync(null);
    }

    private void MtfGridChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext == null) return;
        if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem item)
        {
            if (int.TryParse(item.Content?.ToString(), out int grid))
                VM.FftMtf.GridSize = grid;
        }
    }

    private async void FftMtfVsField_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new FrequencyInputDialog(VM.Session.System.IsAfocal);
        await dialog.ShowDialog(this);
        if (!dialog.Accepted) return;

        var freqs = dialog.GetFrequencies();
        if (freqs.Length == 0) return;

        VM.FftMtfVsField.Frequencies = freqs;
        VM.FftMtfVsField.IsVisible = true;
        VM.SelectedTabIndex = 4; // MTF vs Field tab
        await VM.FftMtfVsField.ComputeCommand.ExecuteAsync(null);
    }

    private void MtfVsFieldGridChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext == null) return;
        if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem item)
        {
            if (int.TryParse(item.Content?.ToString(), out int grid))
                VM.FftMtfVsField.GridSize = grid;
        }
    }

    private async void MtfVsFieldFrequencies_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new FrequencyInputDialog(VM.Session.System.IsAfocal);
        await dialog.ShowDialog(this);
        if (!dialog.Accepted) return;

        var freqs = dialog.GetFrequencies();
        if (freqs.Length > 0)
            VM.FftMtfVsField.Frequencies = freqs;
    }

    private async void TransverseRayFan_Click(object? sender, RoutedEventArgs e)
    {
        VM.TransverseRayFan.IsVisible = true;
        VM.SelectedTabIndex = 14;
        await VM.TransverseRayFan.ComputeCommand.ExecuteAsync(null);
    }

    private void CloseTransverseRayFanTab_Click(object? sender, RoutedEventArgs e)
    {
        VM.TransverseRayFan.IsVisible = false;
        VM.SelectedTabIndex = 0;
    }

    private async void OpdFan_Click(object? sender, RoutedEventArgs e)
    {
        VM.OpdFan.IsVisible = true;
        VM.SelectedTabIndex = 15;
        await VM.OpdFan.ComputeCommand.ExecuteAsync(null);
    }

    private void CloseOpdFanTab_Click(object? sender, RoutedEventArgs e)
    {
        VM.OpdFan.IsVisible = false;
        VM.SelectedTabIndex = 0;
    }

    private async void WavefrontMap_Click(object? sender, RoutedEventArgs e)
    {
        VM.WavefrontMap.IsVisible = true;
        VM.SelectedTabIndex = 16;
        await VM.WavefrontMap.ComputeCommand.ExecuteAsync(null);
    }

    private void WavefrontGridChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext == null) return;
        if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem item)
        {
            if (int.TryParse(item.Content?.ToString(), out int grid))
                VM.WavefrontMap.GridSize = grid;
        }
    }

    private void CloseWavefrontMapTab_Click(object? sender, RoutedEventArgs e)
    {
        VM.WavefrontMap.IsVisible = false;
        VM.SelectedTabIndex = 0;
    }

    private async void PupilAberrationFan_Click(object? sender, RoutedEventArgs e)
    {
        VM.PupilAberrationFan.IsVisible = true;
        VM.SelectedTabIndex = 17;
        await VM.PupilAberrationFan.ComputeCommand.ExecuteAsync(null);
    }

    private void ClosePupilAberrationFanTab_Click(object? sender, RoutedEventArgs e)
    {
        VM.PupilAberrationFan.IsVisible = false;
        VM.SelectedTabIndex = 0;
    }

    // Diffraction Encircled Energy, Geometric Encircled Energy, and Line/Edge Spread
    // have been archived. Code preserved in the SynapseOptics/LensHH-LT-Archive
    // private repository.

    private async void LateralColor_Click(object? sender, RoutedEventArgs e)
    {
        VM.LateralColor.IsVisible = true;
        VM.SelectedTabIndex = 18;
        await VM.LateralColor.ComputeCommand.ExecuteAsync(null);
    }

    private async void LateralColorShowTable_Click(object? sender, RoutedEventArgs e)
    {
        var result = VM.LateralColor.LastResult;
        if (result == null || result.Points.Count == 0) return;

        var system = VM.Session.System;
        string fieldUnit = system.FieldType == Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
        string shiftUnit = result.IsAfocal ? "arcmin" : "\u00b5m";

        // Group points by wavelength to build per-wavelength columns
        var wavelengthIndices = new System.Collections.Generic.SortedSet<int>();
        foreach (var pt in result.Points)
            wavelengthIndices.Add(pt.WavelengthIndex);

        var colList = new System.Collections.Generic.List<string> { "Field (" + fieldUnit + ")" };
        string lcWlFmt = "F" + LensHH.Rendering.LabelFormat.WavelengthDigits(system.Wavelengths);
        foreach (var wi in wavelengthIndices)
        {
            string wlLabel = wi < system.Wavelengths.Count
                ? $"{system.Wavelengths[wi].Value.ToString(lcWlFmt, System.Globalization.CultureInfo.InvariantCulture)} \u00b5m"
                : $"W{wi + 1}";
            colList.Add(wlLabel + " (" + shiftUnit + ")");
        }

        // Group points by field
        var fieldMap = new System.Collections.Generic.SortedDictionary<double, System.Collections.Generic.Dictionary<int, double>>();
        foreach (var pt in result.Points)
        {
            if (!fieldMap.ContainsKey(pt.FieldY))
                fieldMap[pt.FieldY] = new System.Collections.Generic.Dictionary<int, double>();
            double shift = result.IsAfocal ? pt.LateralShift : pt.LateralShift * 1000; // mm -> um
            fieldMap[pt.FieldY][pt.WavelengthIndex] = shift;
        }

        var rows = new System.Collections.Generic.List<string[]>();
        foreach (var kvp in fieldMap)
        {
            var vals = new System.Collections.Generic.List<string>();
            vals.Add(LensHH.Rendering.LabelFormat.Auto(kvp.Key));
            foreach (var wi in wavelengthIndices)
            {
                if (kvp.Value.TryGetValue(wi, out double shift))
                    vals.Add(LensHH.Rendering.LabelFormat.Auto(shift));
                else
                    vals.Add("");
            }
            rows.Add(vals.ToArray());
        }

        var dialog = new DataTableDialog();
        dialog.SetData("Lateral Color", colList.ToArray(), rows);
        dialog.Width = 800;
        await dialog.ShowDialog(this);
    }

    private void CloseLateralColorTab_Click(object? sender, RoutedEventArgs e)
    {
        VM.LateralColor.IsVisible = false;
        VM.SelectedTabIndex = 0;
    }

    // ── Chromatic Focal Shift ──

    private async void ChromaticFocalShift_Click(object? sender, RoutedEventArgs e)
    {
        VM.ChromaticFocalShift.IsVisible = true;
        VM.SelectedTabIndex = 19; // after Lateral Color
        await VM.ChromaticFocalShift.ComputeCommand.ExecuteAsync(null);
    }

    private async void ChromaticFocalShiftShowTable_Click(object? sender, RoutedEventArgs e)
    {
        var result = VM.ChromaticFocalShift.LastResult;
        if (result == null || result.Points.Count == 0) return;

        string unit = result.IsAfocal ? "diopters" : "mm";
        var columns = new[] { "Wavelength (\u00b5m)", $"Focal Shift ({unit})", "EFL (mm)" };
        var rows = new System.Collections.Generic.List<string[]>();
        string cfsWlFmt = "F" + LensHH.Rendering.LabelFormat.WavelengthDigits(
            result.Points.Select(p => p.Wavelength));
        foreach (var pt in result.Points)
        {
            rows.Add(new[]
            {
                pt.Wavelength.ToString(cfsWlFmt, System.Globalization.CultureInfo.InvariantCulture),
                LensHH.Rendering.LabelFormat.Auto(pt.FocalShift),
                LensHH.Rendering.LabelFormat.Auto(pt.Efl)
            });
        }

        var dialog = new DataTableDialog();
        dialog.SetData("Chromatic Focal Shift", columns, rows);
        dialog.Width = 600;
        await dialog.ShowDialog(this);
    }

    private void CloseChromFocalShiftTab_Click(object? sender, RoutedEventArgs e)
    {
        VM.ChromaticFocalShift.IsVisible = false;
        VM.SelectedTabIndex = 0;
    }

    // ── Longitudinal Aberration ──

    private async void LongitudinalAberration_Click(object? sender, RoutedEventArgs e)
    {
        VM.LongitudinalAberration.IsVisible = true;
        VM.SelectedTabIndex = 20; // after Chromatic Focal Shift
        await VM.LongitudinalAberration.ComputeCommand.ExecuteAsync(null);
    }

    private async void LongitudinalAberrationShowTable_Click(object? sender, RoutedEventArgs e)
    {
        var result = VM.LongitudinalAberration.LastResult;
        if (result == null || result.Points.Count == 0) return;

        int numWl = result.WavelengthsUm.Length;
        int wlDigits = LensHH.Rendering.LabelFormat.WavelengthDigits(result.WavelengthsUm);
        string wlFmt = "F" + wlDigits;

        var perWl = new System.Collections.Generic.List<System.Collections.Generic.List<LensHH.Core.Analysis.LongitudinalAberrationPoint>>(numWl);
        for (int i = 0; i < numWl; i++) perWl.Add(new System.Collections.Generic.List<LensHH.Core.Analysis.LongitudinalAberrationPoint>());
        foreach (var p in result.Points)
            if (p.WavelengthIndex >= 0 && p.WavelengthIndex < numWl)
                perWl[p.WavelengthIndex].Add(p);
        for (int i = 0; i < numWl; i++)
            perWl[i].Sort((a, b) => a.PupilRadius.CompareTo(b.PupilRadius));

        var columns = new System.Collections.Generic.List<string> { "Pupil Radius (mm)" };
        for (int i = 0; i < numWl; i++)
            columns.Add($"Shift @ {result.WavelengthsUm[i].ToString(wlFmt, System.Globalization.CultureInfo.InvariantCulture)} µm (mm)");

        int nRows = 0;
        for (int i = 0; i < numWl; i++) if (perWl[i].Count > nRows) nRows = perWl[i].Count;

        var rows = new System.Collections.Generic.List<string[]>();
        for (int row = 0; row < nRows; row++)
        {
            double radius = 0;
            for (int i = 0; i < numWl; i++)
                if (row < perWl[i].Count) { radius = perWl[i][row].PupilRadius; break; }
            var cells = new string[numWl + 1];
            cells[0] = $"{radius:F6}";
            for (int i = 0; i < numWl; i++)
                cells[i + 1] = row < perWl[i].Count
                    ? LensHH.Rendering.LabelFormat.Auto(perWl[i][row].LongitudinalShift)
                    : "-";
            rows.Add(cells);
        }

        var dialog = new DataTableDialog();
        dialog.SetData("Longitudinal Aberration", columns.ToArray(), rows);
        dialog.Width = 600;
        await dialog.ShowDialog(this);
    }

    private void CloseLongitudinalAberrationTab_Click(object? sender, RoutedEventArgs e)
    {
        VM.LongitudinalAberration.IsVisible = false;
        VM.SelectedTabIndex = 0;
    }

    private void SingleRayTrace_Click(object? sender, RoutedEventArgs e)
    {
        VM.SingleRayTrace.IsVisible = true;
        VM.SelectedTabIndex = 21;
    }

    private void RayTraceSelectAll_Click(object? sender, RoutedEventArgs e)
    {
        RayTraceGrid.SelectAll();
    }

    private async void RayTraceCopy_Click(object? sender, RoutedEventArgs e)
    {
        // Copy selected rows as tab-delimited text with headers
        var selected = RayTraceGrid.SelectedItems;
        if (selected == null || selected.Count == 0) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Surf\tX\tY\tZ\tL\tM\tN\tLn\tMn\tNn\tAOI\tPath\tOPL\tCumul OPL\tComment");
        foreach (var item in selected)
        {
            if (item is RayTraceRow row)
                sb.AppendLine($"{row.Surf}\t{row.X}\t{row.Y}\t{row.Z}\t{row.L}\t{row.M}\t{row.N}\t{row.Ln}\t{row.Mn}\t{row.Nn}\t{row.AOI}\t{row.Path}\t{row.OPL}\t{row.CumulOPL}\t{row.Comment}");
        }
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(sb.ToString());
    }

    private void CloseSingleRayTraceTab_Click(object? sender, RoutedEventArgs e)
    {
        VM.SingleRayTrace.IsVisible = false;
        VM.SelectedTabIndex = 0;
    }

    // ── FFT PSF ─────────────────────────────────────────────────────────────

    private async void FftPsf_Click(object? sender, RoutedEventArgs e)
    {
        VM.FftPsf.IsVisible = true;
        VM.SelectedTabIndex = 22;
        await VM.FftPsf.ComputeCommand.ExecuteAsync(null);
    }

    private void PsfGridChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext == null) return;
        if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem item)
        {
            if (int.TryParse(item.Content?.ToString(), out int grid))
                VM.FftPsf.GridSize = grid;
        }
    }

    private void CloseFftPsfTab_Click(object? sender, RoutedEventArgs e)
    {
        VM.FftPsf.IsVisible = false;
        VM.SelectedTabIndex = 0;
    }

    // ── System Data ─────────────────────────────────────────────────────────

    private async void SystemData_Click(object? sender, RoutedEventArgs e)
    {
        VM.SystemData.IsVisible = true;
        VM.SelectedTabIndex = 23;
        await VM.SystemData.ComputeCommand.ExecuteAsync(null);
    }

    private void CloseSystemDataTab_Click(object? sender, RoutedEventArgs e)
    {
        VM.SystemData.IsVisible = false;
        VM.SelectedTabIndex = 0;
    }

    // ── Shared table helpers for MTF vs Field / vs Focus ────────────────────

    private async System.Threading.Tasks.Task ShowMtfVsFieldTable(
        string title, LensHH.Core.Analysis.MtfVsFieldMultiFreqResult result)
    {
        string fieldUnit = VM.Session.System.FieldType == LensHH.Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";
        string freqUnit = VM.Session.System.IsAfocal ? "cy/mrad" : "cy/mm";

        // Columns: Field, then per frequency: T, S
        var columns = new System.Collections.Generic.List<string>();
        columns.Add("Field (" + fieldUnit + ")");
        for (int fi = 0; fi < result.Frequencies.Length; fi++)
        {
            string freq = Rendering.LabelFormat.Auto(result.Frequencies[fi]);
            columns.Add(freq + " " + freqUnit + " T");
            columns.Add(freq + " " + freqUnit + " S");
        }

        var rows = new System.Collections.Generic.List<string[]>();
        foreach (var (fieldY, freqData) in result.Points)
        {
            var vals = new System.Collections.Generic.List<string>();
            vals.Add(Rendering.LabelFormat.Auto(fieldY));
            for (int fi = 0; fi < result.Frequencies.Length; fi++)
            {
                if (fi < freqData.Length)
                {
                    vals.Add(Rendering.LabelFormat.Auto(freqData[fi].tang));
                    vals.Add(Rendering.LabelFormat.Auto(freqData[fi].sag));
                }
                else
                {
                    vals.Add(""); vals.Add("");
                }
            }
            rows.Add(vals.ToArray());
        }

        var dialog = new DataTableDialog();
        dialog.SetData(title, columns.ToArray(), rows);
        dialog.Width = 800;
        await dialog.ShowDialog(this);
    }

    private async System.Threading.Tasks.Task ShowMtfVsFocusTable(
        string title,
        LensHH.Core.Analysis.MtfThroughFocusResult? singleResult,
        LensHH.Core.Analysis.MtfThroughFocusResult[]? multiResults)
    {
        var results = multiResults ?? (singleResult != null
            ? new[] { singleResult } : null);
        if (results == null || results.Length == 0) return;

        string fieldUnit = VM.Session.System.FieldType == LensHH.Core.Enums.FieldType.ObjectHeight ? "mm" : "deg";

        // Columns: per field: Focus Shift, T, S
        var columns = new System.Collections.Generic.List<string>();
        var fieldLabels = new string[results.Length];
        for (int f = 0; f < results.Length; f++)
        {
            int fIdx = results[f].FieldIndex;
            double fieldY = fIdx < VM.Session.System.Fields.Count
                ? VM.Session.System.Fields[fIdx].Y : 0;
            fieldLabels[f] = Rendering.LabelFormat.Auto(fieldY) + " " + fieldUnit;
            columns.Add(fieldLabels[f] + " Focus");
            columns.Add(fieldLabels[f] + " T");
            columns.Add(fieldLabels[f] + " S");
        }

        int maxRows = 0;
        foreach (var r in results)
            if (r.Points.Count > maxRows) maxRows = r.Points.Count;

        var rows = new System.Collections.Generic.List<string[]>();
        for (int i = 0; i < maxRows; i++)
        {
            var vals = new System.Collections.Generic.List<string>();
            for (int f = 0; f < results.Length; f++)
            {
                if (i < results[f].Points.Count)
                {
                    var pt = results[f].Points[i];
                    vals.Add(Rendering.LabelFormat.Auto(pt.FocusShift));
                    vals.Add(Rendering.LabelFormat.Auto(pt.Tangential));
                    vals.Add(Rendering.LabelFormat.Auto(pt.Sagittal));
                }
                else
                {
                    vals.Add(""); vals.Add(""); vals.Add("");
                }
            }
            rows.Add(vals.ToArray());
        }

        var dialog = new DataTableDialog();
        dialog.SetData(title, columns.ToArray(), rows);
        dialog.Width = 900;
        await dialog.ShowDialog(this);
    }

    private async void ExportImage_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;

        Avalonia.Media.Imaging.Bitmap? image = tag switch
        {
            "Layout" => VM.Layout.LayoutImage,
            "FftMtf" => VM.FftMtf.MtfImage,
            "FftMtfVsField" => VM.FftMtfVsField.MtfImage,
            "FftMtfVsFocus" => VM.FftMtfVsFocus.PlotImage,
            "SpotDiagram" => VM.SpotDiagram.SpotImage,
            "Distortion" => VM.Distortion.PlotImage,
            "FieldCurvature" => VM.FieldCurvature.PlotImage,
            "RelativeIllumination" => VM.RelativeIllumination.PlotImage,
            "Seidel" => VM.Seidel.PlotImage,
            "GeoMtfVsFreq" => VM.GeoMtfVsFreq.PlotImage,
            "GeoMtfVsField" => VM.GeoMtfVsField.PlotImage,
            "GeoMtfVsFocus" => VM.GeoMtfVsFocus.PlotImage,
            "TransverseRayFan" => VM.TransverseRayFan.RayFanImage,
            "OpdFan" => VM.OpdFan.OpdFanImage,
            "WavefrontMap" => VM.WavefrontMap.WavefrontImage,
            "PupilAberrationFan" => VM.PupilAberrationFan.PlotImage,
            "LateralColor" => VM.LateralColor.PlotImage,
            "ChromaticFocalShift" => VM.ChromaticFocalShift.PlotImage,
            "LongitudinalAberration" => VM.LongitudinalAberration.PlotImage,
            "FftPsf" => VM.FftPsf.PsfImage,
            "SystemData" => VM.SystemData.PlotImage,
            _ => null
        };

        if (image == null) return;

        var file = await this.StorageProvider.SaveFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Export Image",
                DefaultExtension = "png",
                SuggestedFileName = tag,
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } }
                }
            });

        if (file == null) return;
        var path = file.TryGetLocalPath();
        if (path == null) return;

        image.Save(path);
    }

    private async void SurfaceProperties_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is SurfaceRowViewModel row)
        {
            var dialog = new SurfacePropertiesDialog
            {
                DataContext = new SurfacePropertiesViewModel(row.UnderlyingSurface, VM.Session)
            };
            await dialog.ShowDialog(this);
        }
    }
}
