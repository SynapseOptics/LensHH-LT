using System;
using System.Collections.Generic;
using System.Linq;

namespace LensHH.App.GlassCatalog;

public class GlassFilterService
{
    // Filter 1: Preferred Only
    public bool PreferredEnabled { get; set; }

    // Filter 2: Distance Radius
    public bool DistanceEnabled { get; set; }
    public double DistanceThreshold { get; set; } = 0.05;
    public double Wn { get; set; } = 1.0;
    public double Wa { get; set; } = 1E-04;
    public double Wp { get; set; } = 1E+02;
    public double NdTarget { get; set; } = 1.5168;
    public double VdTarget { get; set; } = 64.17;
    public double DPgFTarget { get; set; } = 0.0;

    // Filter 3: BK7 Relative Cost
    public bool CostEnabled { get; set; }
    public double CostLimit { get; set; } = 5.0;

    // Filter 4: Nd Range
    public bool NdRangeEnabled { get; set; }
    public double NdMin { get; set; } = 1.4;
    public double NdMax { get; set; } = 2.1;

    // Filter 5: Vd Range
    public bool VdRangeEnabled { get; set; }
    public double VdMin { get; set; } = 0;
    public double VdMax { get; set; } = 100;

    // Filter 6: DPgF Range
    public bool DPgFRangeEnabled { get; set; }
    public double DPgFMin { get; set; } = -0.2;
    public double DPgFMax { get; set; } = 0.2;

    // Filter 7: TCE Range
    public bool TCERangeEnabled { get; set; }
    public double TCEMin { get; set; } = 0;
    public double TCEMax { get; set; } = 20;

    // Filter 8: Min Wavelength
    public bool MinWavelengthEnabled { get; set; }
    public double MinWavelengthValue { get; set; } = 0.42;

    // Filter 9: Max Wavelength
    public bool MaxWavelengthEnabled { get; set; }
    public double MaxWavelengthValue { get; set; } = 2.0;

    // Filter 10: Melt Frequency
    public bool MeltFrequencyEnabled { get; set; }
    public int MeltFrequencyLimit { get; set; } = 3;

    public List<GlassEntry> Apply(IEnumerable<GlassEntry> glasses)
    {
        return glasses.Where(PassesAllFilters).ToList();
    }

    private bool PassesAllFilters(GlassEntry g)
    {
        if (PreferredEnabled && g.Status != 1)
            return false;

        if (DistanceEnabled)
        {
            double dNd = g.Nd - NdTarget;
            double dVd = g.Vd - VdTarget;
            double dPgF = g.DPgF - DPgFTarget;
            double d = Math.Sqrt(Wn * dNd * dNd + Wa * dVd * dVd + Wp * dPgF * dPgF);
            if (d > DistanceThreshold)
                return false;
        }

        if (CostEnabled)
        {
            if (g.RelativeCost <= 0)
                return false;
            if (g.RelativeCost > CostLimit)
                return false;
        }

        if (NdRangeEnabled)
        {
            if (g.Nd < NdMin || g.Nd > NdMax)
                return false;
        }

        if (VdRangeEnabled)
        {
            if (g.Vd < VdMin || g.Vd > VdMax)
                return false;
        }

        if (DPgFRangeEnabled)
        {
            if (g.DPgF < DPgFMin || g.DPgF > DPgFMax)
                return false;
        }

        if (TCERangeEnabled)
        {
            if (g.TCE < 0)
                return false;
            if (g.TCE < TCEMin || g.TCE > TCEMax)
                return false;
        }

        if (MinWavelengthEnabled)
        {
            if (g.MinWavelength < 0)
                return false;
            if (g.MinWavelength > MinWavelengthValue)
                return false;
        }

        if (MaxWavelengthEnabled)
        {
            if (g.MaxWavelength < 0)
                return false;
            if (g.MaxWavelength < MaxWavelengthValue)
                return false;
        }

        if (MeltFrequencyEnabled)
        {
            if (g.MeltFrequency < 1)
                return false;
            if (g.MeltFrequency > MeltFrequencyLimit)
                return false;
        }

        return true;
    }
}
