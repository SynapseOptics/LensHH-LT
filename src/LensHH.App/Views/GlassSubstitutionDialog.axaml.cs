using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LensHH.App.Views;

public partial class GlassSubstitutionDialog : Window
{
    public GlassSubstitutionDialog()
    {
        InitializeComponent();
    }

    public bool Accepted { get; private set; }

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        Accepted = true;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
