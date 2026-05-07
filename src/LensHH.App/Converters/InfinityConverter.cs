using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace LensHH.App.Converters;

/// <summary>
/// Converts double values to/from string, displaying "Infinity" for
/// double.PositiveInfinity. Used for Radius and Thickness columns.
/// </summary>
public class InfinityConverter : IValueConverter
{
    public static readonly InfinityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double d)
        {
            if (double.IsPositiveInfinity(d)) return "Infinity";
            if (double.IsNegativeInfinity(d)) return "-Infinity";
            if (double.IsNaN(d)) return "";
            return d.ToString("G10", CultureInfo.InvariantCulture);
        }
        return value?.ToString() ?? "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s)
        {
            s = s.Trim();
            if (s.Equals("Infinity", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("Inf", StringComparison.OrdinalIgnoreCase))
                return double.PositiveInfinity;
            if (s.Equals("-Infinity", StringComparison.OrdinalIgnoreCase))
                return double.NegativeInfinity;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                return d;
        }
        return 0.0;
    }
}
