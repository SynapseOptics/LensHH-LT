using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LensHH.App.Views;

public partial class ActivationDialog : Window
{
    public ActivationDialog()
    {
        InitializeComponent();
    }

    private void Activate_Click(object? sender, RoutedEventArgs e)
    {
        Close(LicenseKeyBox.Text);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
