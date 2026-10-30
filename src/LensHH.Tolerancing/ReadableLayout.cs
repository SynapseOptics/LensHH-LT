using LensHH.Core.Enums;
using LensHH.Core.Models;

namespace LensHH.Tolerancing;

/// <summary>
/// A trial lens laid out as a designer would to decentre or tilt an element by hand, to be saved,
/// read and edited. Every surface keeps its own thickness. A moved element or group is:
/// <list type="number">
/// <item>a Coordinate Break into its frame (Order 0), at its first vertex;</item>
/// <item>its surfaces, thicknesses as they are, the last one's to the next element;</item>
/// <item>a dummy surface that goes back along its axis to the first vertex;</item>
/// <item>the break back (Order 1), its decentres and tilts picking up the first's with a scale of −1,
/// its thickness the way on to the next surface.</item>
/// </list>
/// A surface moved on its element - a wedge - is the same around that one surface, inside its
/// element's frame, and there the dummy's and the break's thicknesses pick up the surface's too.
/// A move along the axis, which a break cannot make, is a dummy surface of that thickness before the
/// first break, taken off the second's.
/// </summary>
internal static class ReadableLayout
{
    /// <summary>Whether every two element moves are apart or one inside the other, as breaks must be.</summary>
    public static bool CanNest(IReadOnlyList<ElementMove> elements) =>
        elements.All(a => elements.All(b =>
            a.Last < b.First || b.Last < a.First
            || (a.First <= b.First && b.Last <= a.Last)
            || (b.First <= a.First && a.Last <= b.Last)));

    /// <param name="lens">The lens with its errors made and its pickups solved.</param>
    /// <param name="elements">The element moves, outermost first, nested (<see cref="CanNest"/>).</param>
    /// <param name="wedges">Each surface's move on its own element.</param>
    public static (OpticalSystem System, int[] Map) Lay(OpticalSystem lens, IReadOnlyList<ElementMove> elements,
                                                         IReadOnlyDictionary<int, Placement> wedges)
    {
        var system = lens.DeepClone();
        var source = system.Surfaces.ToList();
        var rows = new List<Surface>();
        var added = new List<Pickup>();
        var map = new int[source.Count];
        var open = new Stack<(ElementMove Element, int Break)>();

        for (int i = 0; i < source.Count; i++)
        {
            var s = source[i];
            foreach (var e in elements.Where(e => e.First == i))
                open.Push((e, Into(rows, e.Move, $"Element {e.First}-{e.Last} moved")));

            if (wedges.TryGetValue(i, out var w) && !w.IsIdentity)
            {
                int cb = Into(rows, w, $"Surface {i} moved");
                map[i] = rows.Count;
                rows.Add(s);
                int back = rows.Count;
                rows.Add(Dummy(rows, -s.Thickness, $"Back to surface {i}"));
                added.Add(new Pickup { SourceSurfaceIndex = map[i], TargetSurfaceIndex = back, Parameter = PickupParameter.Thickness, ScaleFactor = -1.0 });
                int undo = Undo(rows, added, cb, s.Thickness - w.Dz, $"Surface {i} back");
                added.Add(new Pickup { SourceSurfaceIndex = map[i], TargetSurfaceIndex = undo, Parameter = PickupParameter.Thickness, ScaleFactor = 1.0, Offset = -w.Dz });
            }
            else
            {
                map[i] = rows.Count;
                rows.Add(s);
            }

            while (open.Count > 0 && open.Peek().Element.Last == i)
            {
                var (e, cb) = open.Pop();
                double length = TrialLens.Distance(lens, e.First, i + 1);
                rows.Add(Dummy(rows, -length, $"Back to surface {e.First}"));
                Undo(rows, added, cb, length - e.Move.Dz, $"Element {e.First}-{e.Last} back");
            }
        }

        system.Surfaces.Clear();
        for (int k = 0; k < rows.Count; k++)
        {
            rows[k].Index = k;
            system.Surfaces.Add(rows[k]);
        }
        foreach (var p in system.Pickups)
        {
            p.TargetSurfaceIndex = map[p.TargetSurfaceIndex];
            p.SourceSurfaceIndex = map[p.SourceSurfaceIndex];
        }
        system.Pickups.AddRange(added);
        return (system, map);
    }

    /// <summary>The break into a moved frame, after a dummy for its move along the axis if it has one; its row.</summary>
    private static int Into(List<Surface> rows, Placement move, string comment)
    {
        if (move.Dz != 0.0) rows.Add(Dummy(rows, move.Dz, comment + ": along the axis"));
        var (dx, dy, tx, ty, tz) = move.ToBreak();
        rows.Add(Break(new[] { dx, dy, tx, ty, tz }, order: 0, thickness: 0.0, comment));
        return rows.Count - 1;
    }

    /// <summary>The break back out of the frame <paramref name="into"/> made, its values picked up from it; its row.</summary>
    private static int Undo(List<Surface> rows, List<Pickup> pickups, int into, double thickness, string comment)
    {
        var values = Enumerable.Range(0, 5).Select(k => -rows[into].Parameters[k]).ToArray();
        int row = rows.Count;
        rows.Add(Break(values, order: 1, thickness, comment));
        for (int k = 0; k < 5; k++)
            pickups.Add(new Pickup
            {
                SourceSurfaceIndex = into, TargetSurfaceIndex = row, Parameter = PickupParameter.SurfaceParameter,
                ParameterIndex = k, ScaleFactor = -1.0,
            });
        return row;
    }

    private static Surface Break(double[] values, int order, double thickness, string comment)
    {
        var cb = new Surface { Type = SurfaceType.CoordinateBreak, Thickness = thickness, Comment = comment };
        for (int k = 0; k < values.Length; k++) cb.SetParameter(k + 1, values[k]);
        cb.SetSetting(1, order);
        return cb;
    }

    /// <summary>A plane in the medium the rows have reached, of the given thickness.</summary>
    private static Surface Dummy(List<Surface> rows, double thickness, string comment)
    {
        var medium = Medium(rows);
        return new Surface
        {
            Thickness = thickness,
            Comment = comment,
            Material = medium.Material,
            IndexOffset = medium.IndexOffset,
            AbbeOffset = medium.AbbeOffset,
            ModelIndexEnabled = medium.ModelIndexEnabled,
            ModelNd = medium.ModelNd,
            ModelVd = medium.ModelVd,
            ModelDPgF = medium.ModelDPgF,
        };
    }

    /// <summary>
    /// The surface whose medium the rows end in: the last that is not a break, and before a mirror the
    /// one in front of it, as a mirror returns the ray into the medium it came through.
    /// </summary>
    private static Surface Medium(List<Surface> rows)
    {
        for (int k = rows.Count - 1; k > 0; k--)
        {
            var r = rows[k];
            if (r.Type == SurfaceType.CoordinateBreak || r.IsMirror) continue;
            return r;
        }
        return rows[0];
    }
}
