using System;

namespace LensHH.CLI.GlassCatalog;

public static class DispersionCalculator
{
    private const double Lambda_d = 0.5875618;
    private const double Lambda_C = 0.6562725;
    private const double Lambda_F = 0.4861327;
    private const double Lambda_g = 0.4358343;

    /// <summary>
    /// The index of an AGF glass at <paramref name="lambdaMicrons"/>, for any of the thirteen
    /// AGF formulas — the engine's evaluation, which is checked against OpticStudio. (This had
    /// its own copy of the formulas, and got 4, 12 and 13 wrong: Sellmeier 2 and Extended 2 and 3,
    /// which OpticStudio's HIKARI, NIKON-HIKARI and LZOS catalogs use.)
    /// </summary>
    public static double ComputeIndex(int formula, double[] c, double lambdaMicrons)
    {
        var padded = new double[Math.Max(10, c?.Length ?? 0)];   // as an AGF CD line: missing terms are 0
        if (c != null) Array.Copy(c, padded, c.Length);
        var glass = new LensHH.Core.Glass.GlassData { DispersionFormula = formula, Coefficients = padded };
        return glass.GetIndex(lambdaMicrons);
    }

    public static double ComputeDPgF(int formula, double[] coefficients, double Vd)
    {
        double nF = ComputeIndex(formula, coefficients, Lambda_F);
        double nC = ComputeIndex(formula, coefficients, Lambda_C);
        double ng = ComputeIndex(formula, coefficients, Lambda_g);

        double denominator = nF - nC;
        if (Math.Abs(denominator) < 1e-15)
            return 0.0;

        double PgF = (ng - nF) / denominator;
        double PgF_normal = 0.6438 - 0.001682 * Vd;

        return PgF - PgF_normal;
    }
}
