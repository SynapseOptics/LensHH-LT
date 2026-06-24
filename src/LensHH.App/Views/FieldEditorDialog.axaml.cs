using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LensHH.App.ViewModels;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace LensHH.App.Views;

public partial class FieldEditorDialog : Window
{
    public FieldEditorDialog()
    {
        InitializeComponent();
        AddHandler(GotFocusEvent, (_, e) =>
        {
            if (e.Source is TextBox tb && tb.FindAncestorOfType<DataGrid>() != null)
                Dispatcher.UIThread.Post(() => tb.SelectAll());
        }, RoutingStrategies.Bubble);
    }

    private async void OK_Click(object? sender, RoutedEventArgs e)
    {
        var vm = DataContext as FieldEditorViewModel;
        if (vm == null) { Close(); return; }

        // Hard-gate the on-axis-field rule at Apply time so the editor
        // refuses to close until at least one field is on-axis. The
        // session-level banner would still flag it, but stopping here
        // keeps the user inside the editor where the fix is one cell
        // away.
        bool hasOnAxis = false;
        foreach (var row in vm.Fields)
        {
            if (!TryParseInv(row.YText, out double yv) || Math.Abs(yv) >= 1e-9) continue;
            hasOnAxis = true; break;
        }

        if (!hasOnAxis)
        {
            await MessageBoxManager.GetMessageBoxStandard("Cannot Apply",
                "At least one field must be on-axis (Y = 0). " +
                "Add an on-axis field or set one existing field's Y to 0.",
                ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Warning).ShowWindowDialogAsync(this);
            return;
        }

        vm.Apply();
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    private static bool TryParseInv(string? s, out double v) =>
        double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out v);
}
