using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LensHH.App.ViewModels;

namespace LensHH.App.Views;

public partial class SurfacePropertiesDialog : Window
{
    public SurfacePropertiesDialog()
    {
        InitializeComponent();
        AddHandler(GotFocusEvent, (_, e) =>
        {
            if (e.Source is TextBox tb && tb.FindAncestorOfType<DataGrid>() != null)
                Dispatcher.UIThread.Post(() => tb.SelectAll());
        }, RoutingStrategies.Bubble);
    }

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        (DataContext as SurfacePropertiesViewModel)?.Apply();
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
