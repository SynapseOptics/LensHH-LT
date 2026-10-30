using LensHH.Core.Enums;
using LensHH.Core.Models;

namespace LensHH.Tolerancing;

/// <summary>
/// A lens turned about its axis, image and all, to see it in another direction across the field:
/// LensHH-LT's fields lie along y, and the lens turned by 90° shows at them what it does at +x.
/// After the object comes a Coordinate Break of the turn's tilt about z, which leaves the plane it
/// stands on where it was; before the image, the break back (Order 1), so the image and its
/// tangential and sagittal directions are the field's own.
/// </summary>
public static class FieldTurn
{
    /// <summary>The rows the turn adds before surface 1.</summary>
    public const int RowsBefore = 1;

    /// <summary>
    /// <paramref name="system"/> turned by <paramref name="turn"/> degrees, and where each of its
    /// surfaces went. A turn of 0 is the lens as it is, copied.
    /// </summary>
    public static (OpticalSystem System, int[] Map) Apply(OpticalSystem system, double turn)
    {
        var turned = system.DeepClone();
        int n = turned.Surfaces.Count;
        var map = new int[n];
        if (turn % 360.0 == 0.0)
        {
            for (int i = 0; i < n; i++) map[i] = i;
            return (turned, map);
        }

        var source = turned.Surfaces.ToList();
        var rows = new List<Surface> { source[0] };
        rows.Add(Break(turn, order: 0, $"Turned {turn:G6}°"));
        for (int i = 1; i < n - 1; i++)
        {
            map[i] = rows.Count;
            rows.Add(source[i]);
        }
        rows.Add(Break(-turn, order: 1, "Turned back"));
        map[n - 1] = rows.Count;
        rows.Add(source[n - 1]);

        turned.Surfaces.Clear();
        for (int k = 0; k < rows.Count; k++)
        {
            rows[k].Index = k;
            turned.Surfaces.Add(rows[k]);
        }
        foreach (var p in turned.Pickups)
        {
            if (p.TargetSurfaceIndex >= 0 && p.TargetSurfaceIndex < n) p.TargetSurfaceIndex = map[p.TargetSurfaceIndex];
            if (p.SourceSurfaceIndex >= 0 && p.SourceSurfaceIndex < n) p.SourceSurfaceIndex = map[p.SourceSurfaceIndex];
        }
        return (turned, map);
    }

    private static Surface Break(double turn, int order, string comment)
    {
        var cb = new Surface { Type = SurfaceType.CoordinateBreak, Thickness = 0.0, Comment = comment };
        cb.SetParameter(5, turn);
        cb.SetSetting(1, order);
        return cb;
    }
}
