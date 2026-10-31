using System;
using LensHH.Core.Enums;
using LensHH.Core.Models;

namespace LensHH.Core.IO
{
    /// <summary>
    /// Where each surface of a lens sits in space, for a file form with no Coordinate Break, whose
    /// surfaces each carry their own position and rotation. The frame is followed down the lens as
    /// the tracer follows it: a break decentres, then tilts about x, y and z (order 0), or tilts
    /// about z, y and x, then decentres (order 1); each thickness is then along the new axis.
    /// </summary>
    internal sealed class SurfaceFrames
    {
        public double X, Y, Z;

        /// <summary>The surface's own axes in lens space: column k is local axis k.</summary>
        public double[,] R = Identity();

        /// <summary>
        /// The rotation as angles (radians) about x, then y, then z, all about the lens's fixed
        /// axes: R = Rz·Ry·Rx, as Optiland turns a surface's coordinate system.
        /// </summary>
        public (double Rx, double Ry, double Rz) Angles()
        {
            double ry = Math.Asin(Math.Max(-1.0, Math.Min(1.0, -R[2, 0])));
            double rx = Math.Atan2(R[2, 1], R[2, 2]);
            double rz = Math.Atan2(R[1, 0], R[0, 0]);
            return (Clean(rx), Clean(ry), Clean(rz));
        }

        private static double Clean(double a) => Math.Abs(a) < 1e-15 ? 0.0 : a;

        /// <summary>The frame at each surface's vertex.</summary>
        public static SurfaceFrames[] Of(OpticalSystem system)
        {
            var frames = new SurfaceFrames[system.Surfaces.Count];
            var o = new double[3];
            var r = Identity();
            for (int i = 0; i < system.Surfaces.Count; i++)
            {
                var s = system.Surfaces[i];
                frames[i] = new SurfaceFrames { X = o[0], Y = o[1], Z = o[2], R = (double[,])r.Clone() };
                if (s.Type == SurfaceType.CoordinateBreak)
                {
                    double dx = s.Parameters[0], dy = s.Parameters[1];
                    double ax = s.Parameters[2] * Math.PI / 180.0;
                    double ay = s.Parameters[3] * Math.PI / 180.0;
                    double az = s.Parameters[4] * Math.PI / 180.0;
                    if (s.Settings[0] == 0)
                    {
                        Add(o, Apply(r, dx, dy, 0.0));
                        r = Mul(r, Mul(Rot(0, ax), Mul(Rot(1, ay), Rot(2, az))));
                    }
                    else
                    {
                        r = Mul(r, Mul(Rot(2, az), Mul(Rot(1, ay), Rot(0, ax))));
                        Add(o, Apply(r, dx, dy, 0.0));
                    }
                }
                double t = s.Thickness;
                if (!double.IsInfinity(t) && !double.IsNaN(t))
                    Add(o, Apply(r, 0.0, 0.0, t));
            }
            return frames;
        }

        private static double[,] Identity() => new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };

        // The active rotation by a about axis 0 (x), 1 (y) or 2 (z). The tracer's passive turn of
        // a ray into the tilted frame is its transpose, so the tilted frame's axes are its columns.
        private static double[,] Rot(int axis, double a)
        {
            double c = Math.Cos(a), s = Math.Sin(a);
            return axis switch
            {
                0 => new double[,] { { 1, 0, 0 }, { 0, c, -s }, { 0, s, c } },
                1 => new double[,] { { c, 0, s }, { 0, 1, 0 }, { -s, 0, c } },
                _ => new double[,] { { c, -s, 0 }, { s, c, 0 }, { 0, 0, 1 } },
            };
        }

        private static double[,] Mul(double[,] a, double[,] b)
        {
            var m = new double[3, 3];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    for (int k = 0; k < 3; k++)
                        m[i, j] += a[i, k] * b[k, j];
            return m;
        }

        private static double[] Apply(double[,] r, double x, double y, double z) => new[]
        {
            r[0, 0] * x + r[0, 1] * y + r[0, 2] * z,
            r[1, 0] * x + r[1, 1] * y + r[1, 2] * z,
            r[2, 0] * x + r[2, 1] * y + r[2, 2] * z,
        };

        private static void Add(double[] o, double[] d) { o[0] += d[0]; o[1] += d[1]; o[2] += d[2]; }
    }

    /// <summary>
    /// A surface's even-asphere terms and Zernike departure as one polynomial in x and y, added to
    /// its base conic: c[i, j] is the coefficient of x^i·y^j. Exact: both are polynomials.
    /// </summary>
    internal static class SagPolynomial
    {
        public static double[,] Of(Surface s)
        {
            int degree = 2 * s.AsphericCoefficients.Length;
            degree = Math.Max(degree, 12);
            var c = new double[degree + 1, degree + 1];

            // a_k r^(2k+2) = a_k (x² + y²)^(k+1)
            if (s.Type == SurfaceType.EvenAsphere)
                for (int k = 0; k < s.AsphericCoefficients.Length; k++)
                    AddRadial(c, s.AsphericCoefficients[k], k + 1, 0, false, 1.0);

            if (ZernikeDeparture.IsPresent(s))
            {
                double norm = ZernikeDeparture.Radius(s);
                int j = 0;
                for (int k = 0; j < ZernikeDeparture.MaxTerms; k++)
                    for (int m = k; m >= 0 && j < ZernikeDeparture.MaxTerms; m--)
                    {
                        int n = 2 * k - m;
                        var p = Radial(n, m);
                        double cos = ZernikeDeparture.Term(s, ++j);
                        for (int q = 0; q < p.Length; q++) AddRadial(c, cos * p[q], q, m, false, norm);
                        if (m > 0 && j < ZernikeDeparture.MaxTerms)
                        {
                            double sin = ZernikeDeparture.Term(s, ++j);
                            for (int q = 0; q < p.Length; q++) AddRadial(c, sin * p[q], q, m, true, norm);
                        }
                    }
            }
            return c;
        }

        // Adds w·(u² + v²)^q·Re or Im of (u + iv)^m, with u = x/norm, v = y/norm.
        private static void AddRadial(double[,] c, double w, int q, int m, bool imaginary, double norm)
        {
            if (w == 0.0) return;
            for (int a = 0; a <= q; a++)          // (u² + v²)^q = Σ C(q,a) u^2a v^2(q−a)
                for (int k = 0; k <= m; k++)      // (u + iv)^m = Σ C(m,k) u^(m−k) (iv)^k
                {
                    if ((k % 2 == 1) != imaginary) continue;
                    double sign = ((imaginary ? (k - 1) / 2 : k / 2) % 2 == 0) ? 1.0 : -1.0;
                    int px = 2 * a + m - k, py = 2 * (q - a) + k;
                    c[px, py] += w * sign * Binomial(q, a) * Binomial(m, k) / Math.Pow(norm, px + py);
                }
        }

        // R_n^m(ρ) = ρ^m · Σ_p a_p (ρ²)^p. Returns a_p.
        private static double[] Radial(int n, int m)
        {
            int half = (n - m) / 2;
            var a = new double[half + 1];
            for (int s = 0; s <= half; s++)
            {
                double v = Fact(n - s) / (Fact(s) * Fact((n + m) / 2 - s) * Fact(half - s));
                a[half - s] = (s % 2 == 0) ? v : -v;
            }
            return a;
        }

        private static double Fact(int n)
        {
            double f = 1;
            for (int i = 2; i <= n; i++) f *= i;
            return f;
        }

        private static double Binomial(int n, int k) => Fact(n) / (Fact(k) * Fact(n - k));
    }
}
