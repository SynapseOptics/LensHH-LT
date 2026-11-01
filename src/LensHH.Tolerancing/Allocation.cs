namespace LensHH.Tolerancing;

/// <summary>How tolerances are stated: in the preferred numbers a drawing uses.</summary>
public static class Allocator
{
    /// <summary>
    /// Rounds down to the preferred numbers 1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6, 8 of each decade, as a
    /// drawing states a tolerance; down, so the set stays within its goal.
    /// </summary>
    public static double RoundDown(double value)
    {
        if (!(value > 0.0) || !double.IsFinite(value)) return value;
        double decade = Math.Pow(10.0, Math.Floor(Math.Log10(value)));
        double mantissa = value / decade;
        double[] steps = { 1.0, 1.2, 1.5, 2.0, 2.5, 3.0, 4.0, 5.0, 6.0, 8.0, 10.0 };
        double best = 1.0;
        foreach (double s in steps)
            if (s <= mantissa * (1.0 + 1e-12)) best = s;
        int exponent = (int)Math.Floor(Math.Log10(value));
        return Math.Round(best * decade, Math.Clamp(2 - exponent, 0, 15));   // 0.15, not 0.15000000000000002
    }
}
