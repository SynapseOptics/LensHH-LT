using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LensHH.App.ViewModels;

namespace LensHH.App.Views;

public partial class SpcSynthesisDialog : Window
{
    public SpcSynthesisDialog()
    {
        InitializeComponent();
        // Cancel any running operation when the dialog is closed; otherwise its worker
        // threads keep running (full speed) until the application exits.
        Closing += (_, _) => (DataContext as SpcSynthesisDialogViewModel)?.CancelRun();
    }

    private SpcSynthesisDialogViewModel VM => (SpcSynthesisDialogViewModel)DataContext!;

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        VM.Accept();
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        VM.Stop();
        Close();
    }

    private async void BrowseArchiveFolder_Click(object? sender, RoutedEventArgs e)
    {
        // Seed the picker at the current value (if any) so re-picking lands nearby.
        IStorageFolder? startAt = null;
        var current = VM.ArchiveDirectory;
        if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
        {
            try { startAt = await StorageProvider.TryGetFolderFromPathAsync(new Uri(current)); }
            catch { /* fall back to default */ }
        }

        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select archive folder for SPC designs",
            AllowMultiple = false,
            SuggestedStartLocation = startAt
        });

        if (picked.Count > 0)
        {
            var path = picked[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
                VM.ArchiveDirectory = path!;
        }
    }

    private void Help_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control c && c.Tag is string help)
            VM.SetHelp(help);
    }

    private void Help_PointerExited(object? sender, PointerEventArgs e)
        => VM.SetHelp(null);

    private void Help_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (sender is Control c && c.Tag is string help)
            VM.SetHelp(help);
    }

    private void Help_LostFocus(object? sender, RoutedEventArgs e)
        => VM.SetHelp(null);
}
