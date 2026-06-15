using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LensHH.App.ViewModels;

namespace LensHH.App.Views
{
    public partial class DePipelineDialog : Window
    {
        public DePipelineDialog()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private DePipelineDialogViewModel? VM => DataContext as DePipelineDialogViewModel;

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (VM != null)
            {
                VM.CloseRequested -= OnCloseRequested;
                VM.CloseRequested += OnCloseRequested;
            }
        }

        private void OnCloseRequested(object? sender, EventArgs e) => Close();

        private async void SaveAll_Click(object? sender, RoutedEventArgs e)
        {
            if (VM == null) return;
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Save polished designs to folder",
                AllowMultiple = false,
            });
            if (folders.Count > 0)
            {
                var path = folders[0].TryGetLocalPath();
                if (!string.IsNullOrEmpty(path)) VM.SaveAllTo(path!);
            }
        }

        private async void BrowsePolishFolder_Click(object? sender, RoutedEventArgs e)
        {
            if (VM == null) return;
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select a folder of saved DE results to polish",
                AllowMultiple = false,
            });
            if (folders.Count > 0)
            {
                var path = folders[0].TryGetLocalPath();
                if (!string.IsNullOrEmpty(path)) VM.PolishFolder = path!;
            }
        }

        private void Close_Click(object? sender, RoutedEventArgs e) => Close();
    }
}
