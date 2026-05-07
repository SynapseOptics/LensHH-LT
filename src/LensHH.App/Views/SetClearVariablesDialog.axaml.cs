using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LensHH.App.ViewModels;

namespace LensHH.App.Views;

public partial class SetClearVariablesDialog : Window
{
    public SetClearVariablesDialog()
    {
        InitializeComponent();
        AddHandler(GotFocusEvent, (_, e) =>
        {
            if (e.Source is TextBox tb)
                Dispatcher.UIThread.Post(() => tb.SelectAll());
        }, RoutingStrategies.Bubble);
    }

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        (DataContext as SetClearVariablesViewModel)?.Apply();
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
