using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LensHH.App.ViewModels;

namespace LensHH.App.Views;

public partial class WavelengthEditorDialog : Window
{
    public WavelengthEditorDialog()
    {
        InitializeComponent();
        AddHandler(GotFocusEvent, (_, e) =>
        {
            if (e.Source is TextBox tb && tb.FindAncestorOfType<DataGrid>() != null)
                Dispatcher.UIThread.Post(() => tb.SelectAll());
        }, RoutingStrategies.Bubble);
    }

    /// <summary>Block editing of MCE-owned cells outright — cancelling the edit before it begins is
    /// cleaner than a read-only TextBox the user can still type into (and see rejected). The cell
    /// stays italic/greyed so the read-only status is obvious.</summary>
    private void Grid_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row?.DataContext is not WavelengthRowViewModel row) return;
        bool owned = e.Column.Header switch
        {
            "Weight" => row.IsWeightOwned,
            _ => row.IsValueOwned,   // the wavelength value column
        };
        if (owned) e.Cancel = true;
    }

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        (DataContext as WavelengthEditorViewModel)?.Apply();
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
