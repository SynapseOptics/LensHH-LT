using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LensHH.App.ViewModels;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Models;

namespace LensHH.App.Views
{
    public partial class DrawingsWindow : Window
    {
        private bool _closeConfirmed;

        public DrawingsWindow()
        {
            InitializeComponent();
            Closing += OnClosing;
        }

        private DrawingsViewModel? VM => DataContext as DrawingsViewModel;

        private static readonly IReadOnlyList<FilePickerFileType> TolTypes = new[]
        {
            new FilePickerFileType("Tolerance sets") { Patterns = new[] { "*.tol" } },
            new FilePickerFileType("All files") { Patterns = new[] { "*" } },
        };

        private static readonly IReadOnlyList<FilePickerFileType> JsonTypes = new[]
        {
            new FilePickerFileType("Drawing settings") { Patterns = new[] { "*.json" } },
        };

        private async void LoadTol_Click(object? sender, RoutedEventArgs e)
        {
            if (VM == null) return;
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Tolerance set", AllowMultiple = false, FileTypeFilter = TolTypes,
            });
            if (files.FirstOrDefault()?.TryGetLocalPath() is string path) VM.LoadTolerances(path);
        }

        private async void OpenSettings_Click(object? sender, RoutedEventArgs e)
        {
            if (VM == null) return;
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Drawing settings", AllowMultiple = false, FileTypeFilter = JsonTypes,
            });
            if (files.FirstOrDefault()?.TryGetLocalPath() is string path) VM.LoadSettings(path);
        }

        private async void SaveSettings_Click(object? sender, RoutedEventArgs e) => await SaveSettingsAsync();

        // Beside the lens; a lens not saved yet asks where.
        private async Task<bool> SaveSettingsAsync()
        {
            if (VM == null) return false;
            string? path = VM.SettingsPath;
            if (path == null)
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save drawing settings", DefaultExtension = "json",
                    SuggestedFileName = "lens.drawing.json", FileTypeChoices = JsonTypes,
                });
                path = file?.TryGetLocalPath();
                if (path == null) return false;
            }
            VM.SaveSettings(path);
            return true;
        }

        private async void ExportPdf_Click(object? sender, RoutedEventArgs e)
        {
            if (VM == null) return;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export drawings", DefaultExtension = "pdf", SuggestedFileName = "drawings.pdf",
                FileTypeChoices = new[] { new FilePickerFileType("PDF") { Patterns = new[] { "*.pdf" } } },
            });
            if (file?.TryGetLocalPath() is string path) VM.ExportPdf(path);
        }

        private async void ExportFiles_Click(object? sender, RoutedEventArgs e)
        {
            if (VM == null) return;
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Folder for the SVG and DXF drawings", AllowMultiple = false,
            });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is string folder) VM.ExportFiles(folder);
        }

        // Changes not saved: save them, leave them, or stay.
        private async void OnClosing(object? sender, WindowClosingEventArgs e)
        {
            if (_closeConfirmed || VM is not { IsDirty: true }) return;
            e.Cancel = true;
            var box = MessageBoxManager.GetMessageBoxCustom(new MessageBoxCustomParams
            {
                ContentTitle = "Drawing settings",
                ContentMessage = "Save the changes to the drawings, so they come back next time?",
                ButtonDefinitions = new[]
                {
                    new ButtonDefinition { Name = "Save", IsDefault = true },
                    new ButtonDefinition { Name = "Don't Save" },
                    new ButtonDefinition { Name = "Cancel", IsCancel = true },
                },
                Icon = MsBox.Avalonia.Enums.Icon.Question,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            });
            var answer = await box.ShowWindowDialogAsync(this);
            if (answer == "Save" && !await SaveSettingsAsync()) return;
            if (answer is "Save" or "Don't Save")
            {
                _closeConfirmed = true;
                Close();
            }
        }
    }
}
