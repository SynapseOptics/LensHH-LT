using Avalonia.Controls;
using Avalonia.Interactivity;
using LensHH.App.ViewModels;

namespace LensHH.App.Views;

public partial class SetClearApertureDialog : Window
{
    public SetClearApertureDialog()
    {
        InitializeComponent();
    }

    private SetClearApertureViewModel VM => (SetClearApertureViewModel)DataContext!;

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        VM.Apply();
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
