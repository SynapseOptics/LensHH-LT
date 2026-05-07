using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LensHH.App.ViewModels;

namespace LensHH.App.Views;

public partial class MultistartDialog : Window
{
    public MultistartDialog()
    {
        InitializeComponent();
    }

    private MultistartDialogViewModel VM => (MultistartDialogViewModel)DataContext!;

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        VM.Accepted = true;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        if (VM.IsRunning)
        {
            // Just stop — don't close. User can then click OK to accept or Cancel again to revert.
            VM.StopOptimizationCommand.Execute(null);
            return;
        }

        VM.Accepted = false;
        Close();
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
