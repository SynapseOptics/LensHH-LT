using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LensHH.App.ViewModels;

namespace LensHH.App.Views
{
    public partial class GlobalSearchDialog : Window
    {
        public GlobalSearchDialog()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            // Closing the dialog must cancel a running search — otherwise its worker
            // threads keep running until the app exits. Fires for every close path.
            Closing += (_, _) => VM?.CancelRun();
        }

        private GlobalSearchDialogViewModel? VM => DataContext as GlobalSearchDialogViewModel;

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
                Title = "Save all designs to folder",
                AllowMultiple = false,
            });
            if (folders.Count > 0)
            {
                var path = folders[0].TryGetLocalPath();
                if (!string.IsNullOrEmpty(path)) VM.SaveAllTo(path!);
            }
        }

        private void Close_Click(object? sender, RoutedEventArgs e) => Close();
    }
}
