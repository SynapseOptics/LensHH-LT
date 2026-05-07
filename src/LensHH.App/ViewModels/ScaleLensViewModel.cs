using System;
using CommunityToolkit.Mvvm.ComponentModel;
using LensHH.App.Session;
using LensHH.Core.Enums;

namespace LensHH.App.ViewModels;

public partial class ScaleLensViewModel : ObservableObject
{
    private readonly GuiSession _session;

    [ObservableProperty] private double _scaleFactor = 1.0;

    public ScaleLensViewModel(GuiSession session)
    {
        _session = session;
    }

    public void Apply()
    {
        double s = ScaleFactor;
        if (s <= 0 || s == 1.0)
            return;

        var system = _session.System;

        // Scale all surfaces (including object thickness and image, but skip
        // flat-only object/image curvatures which are already zero/infinity).
        for (int i = 0; i < system.Surfaces.Count; i++)
        {
            var surf = system.Surfaces[i];

            // Radius: scale directly (Curvature = 1/R scales inversely)
            if (!double.IsInfinity(surf.Radius) && surf.Radius != 0)
                surf.Radius *= s;

            // Thickness
            if (!double.IsInfinity(surf.Thickness) && !double.IsNaN(surf.Thickness))
                surf.Thickness *= s;

            // Fixed semi-diameters
            if (surf.SemiDiameterMode == SemiDiameterMode.Fixed && surf.SemiDiameter > 0)
                surf.SemiDiameter *= s;

            // Aperture radii
            if (surf.InnerRadius > 0) surf.InnerRadius *= s;
            if (surf.ClapOuterRadius > 0) surf.ClapOuterRadius *= s;
            if (surf.ObscurationRadius > 0) surf.ObscurationRadius *= s;
            if (surf.FloatingApertureRadius > 0) surf.FloatingApertureRadius *= s;

            // Even asphere coefficients: A_{2n} has units of mm^{-(2n-1)},
            // so scaling lengths by s requires A_{2n} *= s^{1-2n} = s / s^{2n}.
            // Equivalently: the sag equation is z = c*r^2/... + A4*r^4 + A6*r^6 + ...
            // z scales by s, r scales by s, so A_{2n} * r^{2n} scales by s =>
            // A_{2n} * s^{2n} = s * A_{2n}_old => A_{2n}_new = A_{2n}_old * s^{1-2n}.
            if (surf.AsphericCoefficients != null)
            {
                for (int j = 0; j < surf.AsphericCoefficients.Length; j++)
                {
                    if (surf.AsphericCoefficients[j] != 0)
                    {
                        int twoN = (j + 1) * 2; // j=0 -> r^2 (2n=2), j=1 -> r^4 (2n=4), ...
                        surf.AsphericCoefficients[j] *= Math.Pow(s, 1.0 - twoN);
                    }
                }
            }

            // Scale variable bounds
            if (surf.ThicknessMin.HasValue) surf.ThicknessMin *= s;
            if (surf.ThicknessMax.HasValue) surf.ThicknessMax *= s;
            if (surf.CurvatureMin.HasValue) surf.CurvatureMin /= s;
            if (surf.CurvatureMax.HasValue) surf.CurvatureMax /= s;
        }

        // Scale EPD if aperture mode is EPD
        if (system.Aperture.Type == ApertureType.EPD)
            system.Aperture.Value *= s;

        // Scale ObjectHeight fields (they have length units)
        if (system.FieldType == FieldType.ObjectHeight)
        {
            foreach (var field in system.Fields)
                field.Y *= s;
        }

        _session.NotifySystemChanged("surface");
    }
}
