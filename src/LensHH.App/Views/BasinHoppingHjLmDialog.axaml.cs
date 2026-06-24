using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LensHH.App.ViewModels;

namespace LensHH.App.Views;

public partial class BasinHoppingHjLmDialog : Window
{
    public BasinHoppingHjLmDialog()
    {
        InitializeComponent();
        // Cancel any running operation when the dialog is closed; otherwise its worker
        // threads keep running (full speed) until the application exits.
        Closing += (_, _) => (DataContext as BasinHoppingHjLmDialogViewModel)?.CancelRun();
    }

    private BasinHoppingHjLmDialogViewModel VM => (BasinHoppingHjLmDialogViewModel)DataContext!;

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

    private void Help_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control c && c.Tag is string help)
            VM.SetHelp(help);
    }

    private void Help_PointerExited(object? sender, PointerEventArgs e)
        => VM.SetHelp(null);

    // Keyboard focus also shows the help line so the dialog is usable without a mouse.
    private void Help_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (sender is Control c && c.Tag is string help)
            VM.SetHelp(help);
    }

    private void Help_LostFocus(object? sender, RoutedEventArgs e)
        => VM.SetHelp(null);

    // Folder picker for the "save chains" folder; the chosen path lands in the textbox.
    private async void BrowseChainsFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select a folder for the chain designs",
            AllowMultiple = false,
        });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
            VM.SaveChainsFolder = path;
    }
}
