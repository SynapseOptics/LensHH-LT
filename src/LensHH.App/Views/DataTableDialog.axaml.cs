using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LensHH.App.Views;

/// <summary>
/// Row for the DataTableDialog DataGrid. Each column value is stored
/// by index and exposed as C0, C1, C2, ... properties for binding.
/// Supports up to 20 columns.
/// </summary>
public class TableRow
{
    private readonly string[] _values;
    public TableRow(string[] values) => _values = values;

    public string C0 => _values.Length > 0 ? _values[0] : "";
    public string C1 => _values.Length > 1 ? _values[1] : "";
    public string C2 => _values.Length > 2 ? _values[2] : "";
    public string C3 => _values.Length > 3 ? _values[3] : "";
    public string C4 => _values.Length > 4 ? _values[4] : "";
    public string C5 => _values.Length > 5 ? _values[5] : "";
    public string C6 => _values.Length > 6 ? _values[6] : "";
    public string C7 => _values.Length > 7 ? _values[7] : "";
    public string C8 => _values.Length > 8 ? _values[8] : "";
    public string C9 => _values.Length > 9 ? _values[9] : "";
    public string C10 => _values.Length > 10 ? _values[10] : "";
    public string C11 => _values.Length > 11 ? _values[11] : "";
    public string C12 => _values.Length > 12 ? _values[12] : "";
    public string C13 => _values.Length > 13 ? _values[13] : "";
    public string C14 => _values.Length > 14 ? _values[14] : "";
    public string C15 => _values.Length > 15 ? _values[15] : "";
    public string C16 => _values.Length > 16 ? _values[16] : "";
    public string C17 => _values.Length > 17 ? _values[17] : "";
    public string C18 => _values.Length > 18 ? _values[18] : "";
    public string C19 => _values.Length > 19 ? _values[19] : "";

    public string GetValue(int index) => index < _values.Length ? _values[index] : "";
}

/// <summary>
/// Reusable modal dialog that displays analysis data in a table.
/// </summary>
public partial class DataTableDialog : Window
{
    private string[] _columnNames = System.Array.Empty<string>();

    public DataTableDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Configure the table with column names and row data.
    /// Each row is a string[] with one value per column.
    /// </summary>
    public void SetData(string title, string[] columns, List<string[]> rowData)
    {
        TitleText.Text = title;
        Title = title;
        _columnNames = columns;

        TableGrid.Columns.Clear();
        for (int i = 0; i < columns.Length && i < 20; i++)
        {
            TableGrid.Columns.Add(new DataGridTextColumn
            {
                Header = columns[i],
                Binding = new Avalonia.Data.Binding("C" + i),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
        }

        var rows = new ObservableCollection<TableRow>();
        foreach (var r in rowData)
            rows.Add(new TableRow(r));

        TableGrid.ItemsSource = rows;
    }

    private void SelectAll_Click(object? sender, RoutedEventArgs e)
    {
        TableGrid.SelectAll();
    }

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        var selected = TableGrid.SelectedItems;
        if (selected == null || selected.Count == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine(string.Join("\t", _columnNames));

        foreach (var item in selected)
        {
            if (item is TableRow row)
            {
                var values = new List<string>();
                for (int i = 0; i < _columnNames.Length; i++)
                    values.Add(row.GetValue(i));
                sb.AppendLine(string.Join("\t", values));
            }
        }

        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(sb.ToString());
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
