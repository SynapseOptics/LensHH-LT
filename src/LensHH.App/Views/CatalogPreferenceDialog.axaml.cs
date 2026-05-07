using Avalonia.Controls;
using Avalonia.Interactivity;
using LensHH.App.ViewModels;

namespace LensHH.App.Views;

public partial class CatalogPreferenceDialog : Window
{
    public CatalogPreferenceDialog()
    {
        InitializeComponent();
    }

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CatalogPreferenceViewModel vm)
        {
            vm.Accepted = true;
            vm.Apply();
        }
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
