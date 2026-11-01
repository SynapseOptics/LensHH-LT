using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LensHH.App.ViewModels;

namespace LensHH.App.Views
{
    public partial class ToleranceDialog : Window
    {
        public ToleranceDialog()
        {
            InitializeComponent();
            // Closing the window cancels a running analysis, so its workers do not outlive it.
            Closing += (_, _) => { VM?.CancelRun(); VM?.Remember(); };
        }

        private ToleranceDialogViewModel? VM => DataContext as ToleranceDialogViewModel;

        // A double-click on a trial lens opens it, as the button does.
        private void Trial_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e) => VM?.OpenTrialCommand.Execute(null);

        private static readonly IReadOnlyList<FilePickerFileType> TolTypes = new[]
        {
            new FilePickerFileType("Tolerance sets") { Patterns = new[] { "*.tol" } },
            new FilePickerFileType("All files") { Patterns = new[] { "*" } },
        };

        private async void Load_Click(object? sender, RoutedEventArgs e)
        {
            if (VM == null) return;
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load tolerances",
                AllowMultiple = false,
                FileTypeFilter = TolTypes,
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) VM.LoadTolerances(path);
        }

        private async void Save_Click(object? sender, RoutedEventArgs e)
        {
            if (VM == null) return;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save tolerances",
                DefaultExtension = "tol",
                FileTypeChoices = TolTypes,
            });
            if (file?.TryGetLocalPath() is { } path) VM.SaveTolerances(path);
        }

        private async void Browse_Click(object? sender, RoutedEventArgs e)
        {
            if (VM == null) return;
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Save trial lenses to folder",
                AllowMultiple = false,
            });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) VM.SaveDirectory = path;
        }
    }
}
