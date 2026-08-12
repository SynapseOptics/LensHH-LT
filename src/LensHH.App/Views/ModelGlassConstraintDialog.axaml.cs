using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LensHH.App.ViewModels;

namespace LensHH.App.Views;

public partial class ModelGlassConstraintDialog : Window
{
    public ModelGlassConstraintDialog()
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
        (DataContext as ModelGlassConstraintViewModel)?.Apply();
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
