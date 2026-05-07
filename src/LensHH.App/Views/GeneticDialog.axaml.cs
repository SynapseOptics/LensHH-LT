using Avalonia.Controls;
using Avalonia.Interactivity;
using LensHH.App.ViewModels;

namespace LensHH.App.Views;

public partial class GeneticDialog : Window
{
    public GeneticDialog()
    {
        InitializeComponent();
    }

    private GeneticDialogViewModel VM => (GeneticDialogViewModel)DataContext!;

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        VM.Accepted = true;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        if (VM.IsRunning)
        {
            VM.StopOptimizationCommand.Execute(null);
            return;
        }

        VM.Accepted = false;
        Close();
    }
}
