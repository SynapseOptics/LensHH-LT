namespace LensHH.Tolerancing;

/// <summary>
/// One adjustment a compensator may make after the lens is built, beside its focus: an air space,
/// an element's or a group's tilt or decentre in its cell. Each is set from zero, the nominal lens's
/// setting, by an amount in its own units, and may be held within a travel.
/// </summary>
public abstract record Adjustment
{
    /// <summary>The adjustment as a line of a tolerance set: "COMP 5", "CETX 3-4".</summary>
    public abstract string Code { get; }

    /// <summary>The surfaces it moves, as text: <c>5</c>, or <c>3-4</c> for an element.</summary>
    public abstract string Surfaces { get; }

    public abstract string Units { get; }

    /// <summary>The most it may move either way from zero, in its units; null for no limit.</summary>
    public double? Travel { get; init; }

    /// <summary>A step the size of a small part of a typical move, for the solver's derivatives.</summary>
    public abstract double Step { get; }

    /// <summary>Makes the adjustment on a lens with its errors already made.</summary>
    public abstract void Apply(TrialLens lens, double amount);
}

/// <summary>A thickness - an air space - after <see cref="Surface"/>, longer by the amount, in lens units.</summary>
public sealed record SpaceAdjustment(int Surface) : Adjustment
{
    public override string Code => "COMP";
    public override string Surfaces => Surface.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public override string Units => "mm";
    public override double Step => 1e-3;

    public override void Apply(TrialLens lens, double amount) => lens.System.Surfaces[Surface].Thickness += amount;
}

/// <summary>An element or group, surfaces first to last, tilted in its cell about its first vertex, in degrees: as <see cref="ElementTilt"/>.</summary>
public sealed record ElementTiltAdjustment(int First, int Last, Axis Axis) : Adjustment
{
    public override string Code => Axis == Axis.X ? "CETX" : "CETY";
    public override string Surfaces => $"{First}-{Last}";
    public override string Units => "deg";
    public override double Step => 1e-3;

    public override void Apply(TrialLens lens, double amount) => new ElementTilt(First, Last, Axis).Apply(lens, amount);
}

/// <summary>An element or group, surfaces first to last, shifted across the axis in its cell, in lens units: as <see cref="ElementDecentre"/>.</summary>
public sealed record ElementDecentreAdjustment(int First, int Last, Axis Axis) : Adjustment
{
    public override string Code => Axis == Axis.X ? "CEDX" : "CEDY";
    public override string Surfaces => $"{First}-{Last}";
    public override string Units => "mm";
    public override double Step => 1e-3;

    public override void Apply(TrialLens lens, double amount) => new ElementDecentre(First, Last, Axis).Apply(lens, amount);
}
