using System;
using System.Collections.Generic;
using LensHH.Core.Enums;
using LensHH.Core.Models;

namespace LensHH.Core.IO
{
    /// <summary>
    /// What a lens holds beyond a centred, rotationally symmetric lens, and so which export forms
    /// can carry it. A Monte Carlo trial, or a tilted lens read from another program, has Coordinate
    /// Breaks, Zernike departures and glass offsets: the Zemax and Optiland files carry them, with
    /// each field in four directions when the lens is not the same in every direction; the Code V,
    /// OSLO and Optalix files are written for centred lenses and do not, so those refuse such a lens
    /// rather than write a different one.
    /// </summary>
    public static class LensSymmetry
    {
        /// <summary>
        /// Whether the lens is the same in every direction across the field: no Coordinate Break
        /// that decentres or tilts, and no Zernike departure but the rotationally symmetric terms
        /// (Z1, Z4, Z9, Z16, Z25). Then its +y fields show everything.
        /// </summary>
        public static bool IsRotationallySymmetric(OpticalSystem? system)
        {
            if (system == null) return true;
            if (system.FirstTiltedOrDecentredBreak() != 0) return false;
            foreach (var s in system.Surfaces)
            {
                if (!ZernikeDeparture.IsPresent(s)) continue;
                for (int j = 1; j <= ZernikeDeparture.MaxTerms; j++)
                {
                    int root = (int)Math.Round(Math.Sqrt(j));
                    if (root * root != j && ZernikeDeparture.Term(s, j) != 0.0) return false;
                }
            }
            return true;
        }

        /// <summary>Whether the lens is a plain centred lens, which every export form can carry.</summary>
        public static bool IsPlainCentred(OpticalSystem? system) => Extras(system).Count == 0;

        /// <summary>
        /// Why <paramref name="formatName"/> (Code V, OSLO, Optalix) can't carry the lens, or null
        /// when it can.
        /// </summary>
        public static string? CentredFormatRefusal(OpticalSystem? system, string formatName)
        {
            var extras = Extras(system);
            if (extras.Count == 0) return null;
            string what = extras.Count == 1 ? extras[0]
                : string.Join(", ", extras.GetRange(0, extras.Count - 1)) + " and " + extras[extras.Count - 1];
            return $"This lens has {what}, which {formatName} files don't hold, so it can't be exported as "
                 + $"{formatName}. Export it as Zemax or Optiland, which hold them.";
        }

        private static List<string> Extras(OpticalSystem? system)
        {
            var extras = new List<string>();
            if (system == null) return extras;
            bool breaks = false, zernike = false, offsets = false;
            foreach (var s in system.Surfaces)
            {
                breaks |= s.Type == SurfaceType.CoordinateBreak;
                zernike |= ZernikeDeparture.IsPresent(s);
                offsets |= s.HasGlassOffset;
            }
            if (breaks) extras.Add("coordinate breaks");
            if (zernike) extras.Add("Zernike departures");
            if (offsets) extras.Add("glass offsets");
            return extras;
        }
    }
}
