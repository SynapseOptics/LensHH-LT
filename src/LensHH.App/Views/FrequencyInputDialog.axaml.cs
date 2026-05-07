using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LensHH.App.Views;

public partial class FrequencyInputDialog : Window
{
    public FrequencyInputDialog() : this(isAfocal: false) { }

    /// <summary>
    /// Construct the dialog with afocal-aware labels and defaults.
    /// Focal  → labels "(cy/mm)", defaults 50 and 100.
    /// Afocal → labels "(cy/arc-min)", defaults 0.25 and 0.5.
    /// </summary>
    public FrequencyInputDialog(bool isAfocal)
    {
        InitializeComponent();
        if (isAfocal)
        {
            Freq1Label.Text = "Frequency 1 (cy/arc-min):";
            Freq2Label.Text = "Frequency 2 (cy/arc-min):";
            Freq3Label.Text = "Frequency 3 (cy/arc-min):";
            Freq1Box.Text = "0.25";
            Freq2Box.Text = "0.5";
        }
    }

    public bool Accepted { get; private set; }

    public double[] GetFrequencies()
    {
        var list = new List<double>();
        TryAdd(list, Freq1Box.Text);
        TryAdd(list, Freq2Box.Text);
        TryAdd(list, Freq3Box.Text);
        return list.ToArray();
    }

    private static void TryAdd(List<double> list, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text) &&
            double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double val) &&
            val > 0)
        {
            list.Add(val);
        }
    }

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        if (GetFrequencies().Length == 0) return; // need at least one
        Accepted = true;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Accepted = false;
        Close();
    }
}
