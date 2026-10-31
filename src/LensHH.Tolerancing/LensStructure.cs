using System.Collections.Generic;
using System.Linq;
using LensHH.Core.Enums;
using LensHH.Core.Models;

namespace LensHH.Tolerancing;

/// <summary>
/// The rule that keeps a lens with Coordinate Breaks in the shape it was laid out in. Breaks come
/// with tolerancing trials (and with tilted lenses read from other programs), where each element
/// sits between a break and the break back that picks it up, with a row back to its first vertex:
/// a surface added, removed or changed in type there breaks those links and leaves a lens that
/// traces wrongly without saying so. Its values stay editable; its rows do not change. Breaks are
/// not added by hand to a lens of one's own.
/// </summary>
public static class LensStructure
{
    public const string LockedMessage =
        "This lens has coordinate breaks, so its structure is locked: values can be edited, " +
        "but surfaces can't be added, removed or changed in type.";

    public const string NoBreaksByHand =
        "Coordinate breaks can't be added by hand; they come with tolerancing trials and imported lenses.";

    /// <summary>True when <paramref name="system"/> has a Coordinate Break: its rows are fixed.</summary>
    public static bool IsLocked(OpticalSystem? system) =>
        system != null && HasBreaks(system.Surfaces);

    public static bool HasBreaks(IEnumerable<Surface> surfaces) =>
        surfaces.Any(s => s.Type == SurfaceType.CoordinateBreak);

    /// <summary>Why <paramref name="system"/>'s rows can't change, or null when they can.</summary>
    public static string? Refusal(OpticalSystem? system) => IsLocked(system) ? LockedMessage : null;
}
