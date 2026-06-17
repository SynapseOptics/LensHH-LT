using Avalonia.Controls;
using Avalonia.Interactivity;
using LensHH.App.ViewModels;

namespace LensHH.App.Views;

public partial class OptimizationDialog : Window
{
    public OptimizationDialog()
    {
        InitializeComponent();
        // Cancel any running operation when the dialog is closed; otherwise its worker
        // threads keep running (full speed) until the application exits.
        Closing += (_, _) => (DataContext as OptimizationDialogViewModel)?.CancelRun();
    }

    private OptimizationDialogViewModel VM => (OptimizationDialogViewModel)DataContext!;

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
