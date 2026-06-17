using Avalonia.Controls;
using Avalonia.Interactivity;
using LensHH.App.ViewModels;

namespace LensHH.App.Views;

public partial class GeneticDialog : Window
{
    public GeneticDialog()
    {
        InitializeComponent();
        // Cancel any running operation when the dialog is closed; otherwise its worker
        // threads keep running (full speed) until the application exits.
        Closing += (_, _) => (DataContext as GeneticDialogViewModel)?.CancelRun();
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
