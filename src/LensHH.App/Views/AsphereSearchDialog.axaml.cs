using Avalonia.Controls;
using Avalonia.Interactivity;
using LensHH.App.ViewModels;

namespace LensHH.App.Views;

public partial class AsphereSearchDialog : Window
{
    public AsphereSearchDialog()
    {
        InitializeComponent();
    }

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AsphereSearchDialogViewModel vm) vm.Accepted = true;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AsphereSearchDialogViewModel vm) vm.Accepted = false;
        Close();
    }
}
