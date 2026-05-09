using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LensHH.Rendering
{
    /// <summary>
    /// Central formatting utilities for axis labels, tick marks, and legends.
    /// Chooses the appropriate number of significant digits based on data magnitude.
    /// </summary>
    public static class LabelFormat
    {
        /// <summary>
        /// Format a numeric value with an appropriate number of decimal places
        /// based on its magnitude. Avoids both excessive precision and truncation.
        /// </summary>
        public static string Auto(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return value.ToString(CultureInfo.InvariantCulture);
            double abs = Math.Abs(value);
            if (abs == 0) return "0";
            if (abs >= 10000) return value.ToString("F0", CultureInfo.InvariantCulture);
            if (abs >= 1000) return value.ToString("F1", CultureInfo.InvariantCulture);
            if (abs >= 100) return value.ToString("F2", CultureInfo.InvariantCulture);
            if (abs >= 10) return value.ToString("F2", CultureInfo.InvariantCulture);
            if (abs >= 1) return value.ToString("F3", CultureInfo.InvariantCulture);
            if (abs >= 0.1) return value.ToString("F4", CultureInfo.InvariantCulture);
            if (abs >= 0.01) return value.ToString("F5", CultureInfo.InvariantCulture);
            if (abs >= 0.001) return value.ToString("F6", CultureInfo.InvariantCulture);
            return value.ToString("G4", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Format an axis tick value. Uses the axis range to determine precision:
        /// all ticks on the same axis use the same number of decimal places
        /// (determined by the tick step size, not individual values).
        /// </summary>
        public static string Tick(double value, double tickStep)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return value.ToString(CultureInfo.InvariantCulture);
            int decimals = DecimalsForStep(tickStep);
            return value.ToString("F" + decimals, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Format an axis tick using the axis range and number of ticks.
        /// </summary>
        public static string Tick(double value, double axisRange, int numTicks)
        {
            double step = numTicks > 0 ? axisRange / numTicks : axisRange;
            return Tick(value, step);
        }

        /// <summary>
        /// Determine how many decimal places to show for a given tick step.
        /// E.g., step=0.5 → 1 decimal, step=0.02 → 2 decimals, step=50 → 0 decimals.
        /// </summary>
        public static int DecimalsForStep(double step)
        {
            if (step <= 0) return 2;
            double abs = Math.Abs(step);
            if (abs >= 100) return 0;
            if (abs >= 1) return 1;
            // Count digits needed: -log10(step), rounded up
            int d = (int)Math.Ceiling(-Math.Log10(abs));
            return Math.Max(1, Math.Min(d + 1, 8));
        }

        /// <summary>
        /// Format a field value for a legend label (field angle or height).
        /// </summary>
        public static string Field(double value, string unit)
        {
            if (unit == "mm")
                return Auto(value) + " mm";
            return Auto(value) + "\u00b0";
        }

        /// <summary>
        /// Format a wavelength value for a legend label (in micrometers).
        /// Default precision is enough to keep typical visible/UV/IR wavelengths
        /// distinguishable; for systems with closely-spaced wavelengths
        /// (e.g. 0.265985 / 0.266000 / 0.266015) compute digits up-front
        /// via <see cref="WavelengthDigits"/> and use the (value, digits) overload.
        /// </summary>
        public static string Wavelength(double wavelengthUm)
        {
            if (wavelengthUm >= 1.0)
                return wavelengthUm.ToString("F4", CultureInfo.InvariantCulture) + " \u00b5m";
            return wavelengthUm.ToString("F6", CultureInfo.InvariantCulture) + " \u00b5m";
        }

        /// <summary>
        /// Format a wavelength value with caller-specified decimal precision.
        /// </summary>
        public static string Wavelength(double wavelengthUm, int decimals)
        {
            if (decimals < 0) decimals = 0;
            if (decimals > 10) decimals = 10;
            return wavelengthUm.ToString("F" + decimals, CultureInfo.InvariantCulture) + " \u00b5m";
        }

        /// <summary>
        /// Compute the minimum number of decimal digits needed so that every
        /// value in <paramref name="wavelengthsUm"/> renders to a distinct
        /// string. Floor 4 (standard visible-spectrum precision); cap 10
        /// (beyond which IEEE-754 doubles run out of significant digits).
        /// Returns 4 for empty / single-value inputs.
        /// </summary>
        public static int WavelengthDigits(IEnumerable<double> wavelengthsUm)
        {
            if (wavelengthsUm == null) return 4;
            var values = wavelengthsUm.ToList();
            if (values.Count <= 1) return 4;
            for (int d = 4; d <= 10; d++)
            {
                var formatted = values.Select(w => w.ToString("F" + d, CultureInfo.InvariantCulture));
                if (formatted.Distinct().Count() == values.Count)
                    return d;
            }
            return 10;
        }

        /// <summary>
        /// Format a value with units, choosing precision based on magnitude.
        /// </summary>
        public static string WithUnit(double value, string unit)
        {
            return Auto(value) + " " + unit;
        }
    }

    public static class FieldAxisHelper
    {
        /// <summary>
        /// Pick a "nice" tick step for an axis with the given range.
        /// </summary>
        public static double NiceStep(double range, int targetTicks = 5)
        {
            if (range <= 0) return 1;
            double rough = range / targetTicks;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(rough)));
            double norm = rough / mag;
            double nice = norm < 1.5 ? 1 : norm < 3.5 ? 2 : norm < 7.5 ? 5 : 10;
            return nice * mag;
        }

        /// <summary>
        /// Format a field tick label with the appropriate unit symbol.
        /// </summary>
        public static string FormatTick(double value, string fieldUnit)
        {
            return LabelFormat.Field(value, fieldUnit);
        }

        /// <summary>
        /// Return axis label text, e.g. "Field (degrees)" or "Field (mm)".
        /// </summary>
        public static string AxisLabel(string fieldUnit)
        {
            return fieldUnit == "mm" ? "Field (mm)" : "Field (degrees)";
        }
    }

    public class RenderingOptions
    {
        public int SvgSize { get; set; } = 400;
        public int Margin { get; set; } = 40;
        public double DotRadius { get; set; } = 1.2;
        public double DotOpacity { get; set; } = 0.7;
        public bool ShowRmsCircle { get; set; } = true;
        public bool ShowGeoCircle { get; set; } = false;
        public bool ShowAiryDisk { get; set; } = false;
        public string BackgroundColor { get; set; } = "white";
        public string GridColor { get; set; } = "#ddd";
        public string RmsCircleColor { get; set; } = "#aaa";

        /// <summary>
        /// Shared 15-entry qualitative palette used for both wavelength and
        /// field traces. The first 6 entries match the legacy palette so
        /// existing 1–6 wavelength/field designs render unchanged; entries
        /// 7–15 add distinguishable hues for designs with up to 15
        /// wavelengths or fields. All entries are saturated enough to be
        /// visible on white and chosen to maximize hue separation between
        /// adjacent pairs.
        /// </summary>
        public static readonly string[] DefaultPalette15 = new[]
        {
            "#2060ff", // 1 blue
            "#20aa20", // 2 green
            "#ff2020", // 3 red
            "#cc6600", // 4 orange
            "#8800ff", // 5 purple
            "#008888", // 6 teal
            "#e377c2", // 7 pink
            "#8c564b", // 8 brown
            "#bcbd22", // 9 olive
            "#17becf", // 10 cyan
            "#393b79", // 11 navy
            "#637939", // 12 dark olive-green
            "#7b4173", // 13 mauve
            "#843c39", // 14 brick
            "#5254a3", // 15 indigo
        };

        /// <summary>
        /// Wavelength colors indexed by wavelength number. Defaults to the
        /// shared 15-entry qualitative palette.
        /// </summary>
        public string[] WavelengthColors { get; set; } = DefaultPalette15;

        public string GetWavelengthColor(int wavelengthIndex)
        {
            return WavelengthColors[wavelengthIndex % WavelengthColors.Length];
        }
    }
}
