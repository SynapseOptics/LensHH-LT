namespace LensHH.Tolerancing;

/// <summary>
/// A rigid move of one surface's frame: a point <c>p</c> of the mechanical frame is at
/// <c>R (p − d)</c> in the surface's own. <c>d</c> is where the surface's vertex went, <c>R</c> how
/// its frame turned. R is the matrix a Coordinate Break applies to the ray, so a Tilt about x by
/// +a here is a break's Tilt X of +a.
///
/// <para>A wedge (the surface tilted on its own element) and the element tilted in the barrel are
/// two such moves, and they compose as rotations, not by adding angles. The composite becomes a
/// pair of Coordinate Breaks around the surface (<see cref="ToBreak"/>).</para>
/// </summary>
public readonly record struct Placement(double[] R, double Dx, double Dy, double Dz)
{
    public static Placement Identity => new(new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }, 0.0, 0.0, 0.0);

    public static Placement Decentre(double dx, double dy, double dz = 0.0) => Identity with { Dx = dx, Dy = dy, Dz = dz };

    /// <summary>Tilts in radians about x then y, through the vertex: R = Ry(tiltY)·Rx(tiltX).</summary>
    public static Placement Tilt(double tiltX, double tiltY)
    {
        double ca = Math.Cos(tiltX), sa = Math.Sin(tiltX);
        double cb = Math.Cos(tiltY), sb = Math.Sin(tiltY);
        // Rx(a) = [1 0 0; 0 ca sa; 0 −sa ca], Ry(b) = [cb 0 −sb; 0 1 0; sb 0 cb], as a break turns a ray.
        return new(new[] { cb, sb * sa, -sb * ca, 0.0, ca, sa, sb, -cb * sa, cb * ca }, 0.0, 0.0, 0.0);
    }

    /// <summary>
    /// Turned by the tilts about a pivot on the axis, <paramref name="pivotZ"/> from the vertex:
    /// the vertex swings to <c>P − Rᵀ P</c>, off the axis and along it.
    /// </summary>
    public static Placement TiltAbout(double tiltX, double tiltY, double pivotZ)
    {
        var t = Tilt(tiltX, tiltY);
        var r = t.R;
        // Rᵀ P with P = (0, 0, pivotZ): the third row of R, times pivotZ.
        return t with { Dx = -r[6] * pivotZ, Dy = -r[7] * pivotZ, Dz = pivotZ - r[8] * pivotZ };
    }

    /// <summary>
    /// The same rigid move seen from a point <paramref name="z"/> further along the axis: a move of an
    /// element given at its first vertex, as it places the vertex <paramref name="z"/> behind it.
    /// <c>d' = d + Rᵀ z − z</c>; R is unchanged.
    /// </summary>
    public Placement At(double z) =>
        z == 0.0 ? this : this with { Dx = Dx + R[6] * z, Dy = Dy + R[7] * z, Dz = Dz + R[8] * z - z };

    /// <summary>
    /// This move made inside <paramref name="outer"/>'s frame: first the outer, then this one.
    /// <c>p_s = R_i (R_o (p − d_o) − d_i)</c>, so <c>R = R_i R_o</c> and <c>d = d_o + R_oᵀ d_i</c>.
    /// </summary>
    public Placement Within(Placement outer)
    {
        var a = R; var o = outer.R;
        var m = new double[9];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                m[3 * i + j] = a[3 * i] * o[j] + a[3 * i + 1] * o[3 + j] + a[3 * i + 2] * o[6 + j];
        return new(m,
                   outer.Dx + o[0] * Dx + o[3] * Dy + o[6] * Dz,
                   outer.Dy + o[1] * Dx + o[4] * Dy + o[7] * Dz,
                   outer.Dz + o[2] * Dx + o[5] * Dy + o[8] * Dz);
    }

    /// <summary>
    /// The Coordinate Break (Order 0: decenter, then tilt X, Y, Z) that takes the mechanical frame to
    /// this one: decenters Dx, Dy and the tilts in degrees with Rz(c)·Ry(b)·Rx(a) = R. Dz is not a
    /// break's; it is carried by the thicknesses either side.
    /// </summary>
    public (double Dx, double Dy, double TiltX, double TiltY, double TiltZ) ToBreak()
    {
        // Rz·Ry·Rx has third row (sb, −cb·sa, cb·ca) and first column (cc·cb, −sc·cb, sb).
        double b = Math.Asin(Math.Clamp(R[6], -1.0, 1.0));
        double a = Math.Atan2(-R[7], R[8]);
        double c = Math.Atan2(-R[3], R[0]);
        const double deg = 180.0 / Math.PI;
        return (Dx, Dy, a * deg, b * deg, c * deg);
    }

    public bool IsIdentity =>
        Dx == 0 && Dy == 0 && Dz == 0 && R[0] == 1 && R[4] == 1 && R[8] == 1
        && R[1] == 0 && R[2] == 0 && R[3] == 0 && R[5] == 0 && R[6] == 0 && R[7] == 0;
}
