using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LensHH.App.ViewModels;

namespace LensHH.App.Views;

public partial class VariableEditorDialog : Window
{
    public VariableEditorDialog()
    {
        InitializeComponent();
        AddHandler(GotFocusEvent, (_, e) =>
        {
            if (e.Source is TextBox tb && tb.FindAncestorOfType<DataGrid>() != null)
                Dispatcher.UIThread.Post(() => tb.SelectAll());
        }, RoutingStrategies.Bubble);
    }

    private VariableEditorViewModel VM => (VariableEditorViewModel)DataContext!;

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        VM.Session.NotifySystemChanged("properties");
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    private async void ThicknessConstraints_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new BatchConstraintsDialog
        {
            DataContext = new BatchConstraintsViewModel(VM.Session, BatchConstraintParam.Thickness)
        };
        await dialog.ShowDialog(this);
        VM.Refresh();
    }

    private async void CurvatureConstraints_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new BatchConstraintsDialog
        {
            DataContext = new BatchConstraintsViewModel(VM.Session, BatchConstraintParam.Curvature)
        };
        await dialog.ShowDialog(this);
        VM.Refresh();
    }
}
