using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LensHH.App.ViewModels;

namespace LensHH.App.Views;

public partial class PreferencesDialog : Window
{
    public PreferencesDialog()
    {
        InitializeComponent();
    }

    private PreferencesDialogViewModel VM => (PreferencesDialogViewModel)DataContext!;

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        VM.Accepted = true;
        VM.Save();
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
