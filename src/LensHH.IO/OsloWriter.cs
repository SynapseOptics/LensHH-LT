using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using LensHH.Core.Enums;
using LensHH.Core.Glass;
using LensHH.Core.Models;
using LensHH.Core.RayTrace;

namespace LensHH.Core.IO
{
    /// <summary>
    /// Writes OSLO .len lens files.
    ///
    /// <para>The syntax follows what OSLO 6.6 itself writes: an object at infinity takes
    /// <c>EBR</c> and <c>ANG</c>, a finite one <c>NAO</c> and <c>OBH</c>; a curved object surface
    /// takes <c>RD</c> on surface 0; an ideal lens is OSLO's perfect lens, <c>PFL</c> and, at a
    /// finite conjugate, <c>PFM</c>; a model glass is <c>GLA MOD name</c> followed by its index at
    /// each wavelength of the preceding <c>WV</c> line; and only an aperture that clips is written,
    /// as <c>AP CHK</c> - OSLO solves the rest. Every quantity it converts - an F-number to an
    /// entrance beam radius, an EPD to an object NA - is computed by C# paraxial traces, so the
    /// export is the same with or without an activated engine.</para>
    /// </summary>
    public static class OsloWriter
    {
        /// <param name="glassMgr">The glass catalogs, for the refractive indices a conversion
        /// needs: an F-number or EPD at a finite object, a field angle at a finite object, or a
        /// perfect lens at a finite conjugate. Not needed otherwise.</param>
        public static void Write(OpticalSystem system, string filePath, GlassCatalogManager? glassMgr = null)
        {
            // OSLO's standard asphere starts at r⁴ (AD); a surface with an r² term cannot be written
            // faithfully, so it is refused rather than dropped, as the Code V and Optalix writers do.
            // (The term was silently left out, and OSLO opened another lens.)
            for (int i = 0; i < system.Surfaces.Count; i++)
            {
                var a = system.Surfaces[i].AsphericCoefficients;
                if (a != null && a.Length > 0 && a[0] != 0.0)
                    throw new InvalidOperationException(
                        $"Surface {i} has an r² aspheric term, which OSLO's standard asphere has no place for.");
            }

            // Wavelengths, primary first (see the WV line below): model glasses list their
            // indices in this order too.
            var wavelengths = OrderedWavelengths(system);
            double primaryUm = wavelengths.Count > 0 ? wavelengths[0].Value : 0.58756;
            bool infiniteObject = system.Surfaces.Count == 0
                || double.IsInfinity(system.Surfaces[0].Thickness)
                || Math.Abs(system.Surfaces[0].Thickness) >= 1e10;
            double[]? indices = null;
            double[] Indices() => indices ??= IndicesAt(primaryUm);
            // Per-surface indices at a wavelength (index i = the medium after surface i), through the
            // glass catalogs - the engine's seam to its model-glass dispersion.
            var indicesByWavelength = new Dictionary<double, double[]>();
            double[] IndicesAt(double wavelengthUm)
            {
                if (!indicesByWavelength.TryGetValue(wavelengthUm, out var arr))
                {
                    arr = (glassMgr ?? throw new InvalidOperationException(
                            "Exporting this lens to OSLO needs its refractive indices, and no glass catalog was given."))
                        .BuildRefractiveIndexArray(system, wavelengthUm);
                    indicesByWavelength[wavelengthUm] = arr;
                }
                return arr;
            }

            var sb = new StringBuilder();
            sb.AppendLine("// OSLO 5.10");
            sb.AppendLine("// Exported from LensHH-LT");

            string title = string.IsNullOrEmpty(system.Title) ? "Untitled" : system.Title;
            // OSLO's LEN NEW lens-name field is rejected on import when it
            // exceeds 32 characters and barfs on embedded double quotes.
            // Sanitize + truncate for LEN NEW only; preserve the full title
            // in SNO1 (free-form note, no length cap) so descriptive context
            // is kept on round-trip.
            sb.AppendLine($"LEN NEW \"{SanitizeOsloLenName(title)}\"");
            sb.AppendLine($"SNO1 \"{title.Replace("\"", "'")}\"");

            // SNO2..SNO10 = multi-line notes (max 9 extra lines per OSLO).
            // Lines beyond the 9th are dropped silently rather than
            // truncated into one — keeps the rest readable.
            if (!string.IsNullOrEmpty(system.Notes))
            {
                var noteLines = system.Notes.Replace("\r\n", "\n").Split('\n');
                int slot = 2;
                foreach (var rawLine in noteLines)
                {
                    if (slot > 10) break;
                    string clean = (rawLine ?? string.Empty).Replace("\"", "'");
                    sb.AppendLine($"SNO{slot} \"{clean}\"");
                    slot++;
                }
            }

            // DES "<designer>" — single-line attribution.
            if (!string.IsNullOrWhiteSpace(system.Designer))
                sb.AppendLine($"DES \"{system.Designer.Replace("\"", "'")}\"");

            sb.AppendLine("UNI 1.0");

            // Aperture and field. OSLO writes EBR and ANG for an object at infinity, NAO and OBH
            // for a finite one; the other aperture types are converted to these. (This line once
            // wrote EBR 5 for any aperture that was not an EPD, silently: an F/6.3 lens of EFL
            // 66.04 left as F/6.6.)
            double maxField = 0;
            foreach (var f in system.Fields)
                if (Math.Abs(f.Y) > maxField) maxField = Math.Abs(f.Y);
            if (infiniteObject)
            {
                double ebr;
                switch (system.Aperture.Type)
                {
                    case ApertureType.EPD:
                        ebr = system.Aperture.Value / 2.0;
                        break;
                    case ApertureType.FNumber:
                        ebr = Math.Abs(ParaxialEfl(system, Indices())) / (2.0 * system.Aperture.Value);
                        break;
                    default:
                        throw new InvalidOperationException(
                            "An object-space NA needs a finite object; OSLO cannot take one for an object at infinity.");
                }
                if (system.FieldType == FieldType.ObjectHeight)
                    throw new InvalidOperationException(
                        "Fields given as object heights need a finite object; OSLO takes a field angle for an object at infinity.");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "EBR {0:R}", ebr));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "ANG {0:R}", maxField));
            }
            else
            {
                // Distance from the object to the entrance pupil, which an EPD or a field angle
                // needs to become an object NA or height. A telecentric object space has none.
                double ObjectToPupil()
                {
                    if (system.TelecentricObjectSpace)
                        throw new InvalidOperationException(
                            "A telecentric object space has no finite entrance pupil to convert this aperture or field with; give an object NA and object heights.");
                    return Math.Abs(ArbitraryRay.ComputeEntrancePupilPosition(system, Indices()) + system.Surfaces[0].Thickness);
                }

                double nao;
                if (system.Aperture.Type == ApertureType.ObjectSpaceNA)
                    nao = system.Aperture.Value;
                else
                {
                    double pupilRadius = system.Aperture.Type == ApertureType.FNumber
                        ? Math.Abs(ParaxialEfl(system, Indices())) / (2.0 * system.Aperture.Value)
                        : system.Aperture.Value / 2.0;
                    double n0 = Math.Abs(Indices()[0]);
                    nao = n0 * Math.Sin(Math.Atan(pupilRadius / ObjectToPupil()));
                }
                // OSLO's OBH is the object point's y (checked in OSLO 6.6: OBH 10, 100 before a
                // singlet, sends the chief ray down through the stop to an image at y = -9.5). A
                // field angle here aims the chief ray UP at the pupil, from an object BELOW the
                // axis (ArbitraryRay), so the height is negative. (This wrote +d tan(angle): the
                // object on the wrong side, every image in OSLO mirrored.)
                double obh = system.FieldType == FieldType.ObjectHeight
                    ? maxField
                    : -ObjectToPupil() * Math.Tan(maxField * Math.PI / 180.0);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "NAO {0:R}", nao));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "OBH {0:R}", obh));
            }

            // Surface 0 (object): a curved object surface takes a radius, as OSLO writes it.
            sb.AppendLine("// SRF 0");
            if (system.Surfaces.Count > 0)
            {
                var s0 = system.Surfaces[0];
                if (!double.IsInfinity(s0.Radius) && Math.Abs(s0.Curvature) > 1e-15)
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  RD {0:G10}", s0.Radius));
                double th0 = double.IsPositiveInfinity(s0.Thickness) ? 1e20 : s0.Thickness;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  TH {0:E7}", th0));
            }

            // Remaining surfaces
            for (int i = 1; i < system.Surfaces.Count; i++)
            {
                var s = system.Surfaces[i];
                sb.AppendLine($"NXT // SRF {i}");

                if (!double.IsInfinity(s.Radius) && Math.Abs(s.Curvature) > 1e-15)
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  RD {0:G10}", s.Radius));

                bool isMirror = s.Material != null &&
                    s.Material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase);

                if (isMirror)
                    sb.AppendLine("  RFH");
                else if (s.ModelIndexEnabled)
                {
                    // A model glass, as OSLO writes one: the wavelengths, then the glass's index at
                    // each of them, by this program's own model dispersion, so OSLO traces the lens
                    // this program traces. (It was not written at all, and the surface went out as
                    // air.)
                    sb.AppendLine("  WV " + string.Join(" ", wavelengths.Select(w =>
                        w.Value.ToString("F5", CultureInfo.InvariantCulture))));
                    sb.AppendLine($"  GLA MOD MODEL{i} " + string.Join(" ", wavelengths.Select(w =>
                        IndicesAt(w.Value)[i].ToString("R", CultureInfo.InvariantCulture))));
                }
                else if (!string.IsNullOrEmpty(s.Material))
                    sb.AppendLine($"  GLA {s.Material}");

                if (s.Type == SurfaceType.Paraxial)
                {
                    // OSLO's perfect lens: its focal length, and the magnification it is perfect
                    // at, which at an object at infinity is 0 and is left unwritten.
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  PFL {0:G10}", s.FocalLength));
                    if (!infiniteObject)
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  PFM {0:R}",
                            PerfectLensMagnification(system, Indices(), i)));
                }

                if (s.IsStop)
                    sb.AppendLine("  AST");

                // Conic constant
                if (s.Conic != 0)
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  CC {0:E7}", s.Conic));

                // Aspheric coefficients AD-AK: AD=r⁴→[1], AE=r⁶→[2], ...
                if (s.AsphericCoefficients != null)
                {
                    string[] aspKeywords = { "AD", "AE", "AF", "AG", "AH", "AI", "AJ" };
                    for (int c = 0; c < aspKeywords.Length; c++)
                    {
                        int idx = c + 1; // [1]=r⁴, [2]=r⁶, ...
                        if (idx < s.AsphericCoefficients.Length && s.AsphericCoefficients[idx] != 0)
                            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                                "  {0} {1:E7}", aspKeywords[c], s.AsphericCoefficients[idx]));
                    }
                }

                // Checked (AP CHK, which blocks rays) only for an aperture that clips: a Fixed
                // semi-diameter, or an automatic one held under 100 % of the beam. Any other
                // semi-diameter goes out not checked (AP), which in OSLO sizes the surface for
                // drawing and never blocks a ray - the meaning of an automatic one here. Left out,
                // OSLO would solve it from paraxial ray heights, which on a wide-angle lens exceed
                // the radius of curvature: the Topogon at 35 degrees drew as circles. The stop,
                // when automatic, is left to OSLO, which sizes it from EBR. (Every semi-diameter
                // used to go out checked, so an exported lens vignetted where its source did not.)
                bool clips = s.SemiDiameterMode == SemiDiameterMode.Fixed
                    || (s.ClearAperturePercent > 0 && s.ClearAperturePercent < 100.0);
                if (s.SemiDiameter > 0)
                {
                    if (clips)
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  AP CHK {0:G10}", s.SemiDiameter));
                    else if (!s.IsStop)
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  AP {0:G10}", s.SemiDiameter));
                }

                // Central obscuration / annular pupil — round-trip the same
                // AY1/AY2/AX1/AX2/ATP/AAC pattern OsloReader interprets.
                // InnerRadius (mirror central hole, e.g. Hubble primary) and
                // ObscurationRadius (baffle dummy surface in front of the
                // primary) both encode an "AAC=2" zone of given radius.
                double obscR = s.InnerRadius > 0 ? s.InnerRadius
                             : s.ObscurationRadius > 0 ? s.ObscurationRadius
                             : 0;
                if (obscR > 0)
                {
                    sb.AppendLine("  APN 1");
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  AY1 A {0:G8}", -obscR));
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  AY2 A {0:G8}",  obscR));
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  AX1 A {0:G8}", -obscR));
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  AX2 A {0:G8}",  obscR));
                    sb.AppendLine("  ATP A 1");
                    sb.AppendLine("  AAC A 2");
                }

                // Thickness — emit PK TH / PK THM if a Pickup targets this
                // surface's Thickness with scale ±1 (OSLO doesn't express
                // arbitrary scale on PK TH). Else emit a plain TH.
                Pickup? thPickup = null;
                if (system.Pickups != null)
                {
                    foreach (var p in system.Pickups)
                    {
                        if (p.TargetSurfaceIndex == i
                            && p.Parameter == PickupParameter.Thickness
                            && (Math.Abs(p.ScaleFactor - 1.0) < 1e-12
                                || Math.Abs(p.ScaleFactor + 1.0) < 1e-12))
                        {
                            thPickup = p; break;
                        }
                    }
                }

                if (thPickup != null)
                {
                    int relOffset = thPickup.SourceSurfaceIndex - i;
                    string pkType = thPickup.ScaleFactor < 0 ? "THM" : "TH";
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  PK {0} {1} {2:G10}", pkType, relOffset, thPickup.Offset));
                }
                else
                {
                    double th = i < system.Surfaces.Count - 1 ? s.Thickness : 0;
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  TH {0:G10}", th));
                }

                // CALLBACK 1 = "marginal ray height = 0 at this surface"
                // solve marker. Round-tripped from the surface flag set by
                // OsloReader; OSLO emits it as a top-level token after the
                // surface block, no leading indent.
                if (s.HasMarginalRaySolve)
                    sb.AppendLine("CALLBACK  1");
            }

            // Wavelengths, in the order OrderedWavelengths gives: primary first.
            if (wavelengths.Count > 0)
            {
                var wvSb = new StringBuilder("WV ");
                var wwSb = new StringBuilder("WW ");
                foreach (var wl in wavelengths)
                {
                    wvSb.Append(string.Format(CultureInfo.InvariantCulture, " {0:F5}", wl.Value));
                    wwSb.Append(string.Format(CultureInfo.InvariantCulture, " {0:G}", wl.Weight));
                }
                sb.AppendLine(wvSb.ToString());
                sb.AppendLine(wwSb.ToString());
            }

            sb.AppendLine($"END {system.Surfaces.Count - 1}");
            System.IO.File.WriteAllText(filePath, sb.ToString());
        }

        // OSLO has no primary-wavelength keyword: its primary IS wavelength 1, the first on the
        // WV line (OSLO Program Reference pp. 26, 123), and OsloReader reads it that way. So the
        // primary goes first, and the rest follow short to long - OSLO's own middle, short, long
        // order, d F C for the usual three. Each weight travels with its wavelength. Written in
        // stored order instead, an F d C lens with d primary opened in OSLO, and read back here,
        // as an F-line lens. Model glasses list their indices in this same order.
        private static List<Wavelength> OrderedWavelengths(OpticalSystem system)
        {
            if (system.Wavelengths.Count == 0)
                return new List<Wavelength>();
            int primary = system.PrimaryWavelengthIndex;
            if (primary < 0 || primary >= system.Wavelengths.Count)
                primary = 0;
            return system.Wavelengths
                .Where((_, i) => i != primary)
                .OrderBy(w => w.Value)
                .Prepend(system.Wavelengths[primary])
                .ToList();
        }

        // The paraxial EFL, from the C# transfer matrix of the optical surfaces: an axial ray
        // entering at height 1 leaves with slope c, and EFL = -1/(n' c). The native EFL is not
        // used because it returns 0 on an engine that is not activated.
        private static double ParaxialEfl(OpticalSystem system, double[] indices)
        {
            int last = system.Surfaces.Count - 2;
            var (_, _, c, _) = new ParaxialRayTracer(system, indices).ComputeSubsystemMatrix(1, last);
            if (Math.Abs(c) < 1e-300)
                throw new InvalidOperationException("The lens is afocal, so an F-number gives it no entrance beam radius.");
            double nImage = Math.Abs(indices[last]);
            return -1.0 / (nImage * c);
        }

        // The lateral magnification a perfect lens works at, from the paraxial axial ray leaving
        // the object point: m = n u / (n' u') across the lens.
        private static double PerfectLensMagnification(OpticalSystem system, double[] indices, int s)
        {
            double y = system.Surfaces[0].Thickness;   // incident on surface 1, slope 1
            double u = 1.0;
            if (s > 1)
            {
                var (a, b, c, d) = new ParaxialRayTracer(system, indices).ComputeSubsystemMatrix(1, s - 1);
                double y1 = a * y + b * u;
                double u1 = c * y + d * u;
                y = y1 + system.Surfaces[s - 1].Thickness * u1;
                u = u1;
            }
            double n = Math.Abs(indices[s - 1]);
            double nPrime = Math.Abs(indices[s]);
            double uPrime = (n * u - y / system.Surfaces[s].FocalLength) / nPrime;
            if (Math.Abs(uPrime) < 1e-300)
                return 1e7;   // imaged at infinity; OSLO caps the magnification there
            return n * u / (nPrime * uPrime);
        }

        // OSLO's LEN NEW lens-name field has a 32-character cap and rejects
        // embedded double quotes. Strip quotes, collapse whitespace runs to
        // single spaces, trim, and truncate to 32 characters.
        //
        // And no word of it may be a number. OSLO's own line is LEN NEW "name" 100 5,
        // the last number being the surface count, and OSLO reads a number standing as
        // a word INSIDE the quotes as one of those arguments: "Ideal lens, curved image
        // R 200" was refused with "Maximum number of surfaces is 10" (OSLO EDU,
        // 2026-09-25), while the same file named "Ideal lens curved image" opened.
        // "Topogon US 2031792 Fig 1" only got through because its last number was 1.
        // Such words are dropped from LEN NEW; SNO1 keeps the full title.
        //
        // They are dropped AFTER cutting to 32 characters as well as before: a cut can leave a new
        // number at the end - "POSITIVE DOUBLET; 26.50MM DIA; 100.00MM EFL" cut to
        // "...; 26.50MM DIA; 1" - which OSLO reads the same way. (Ported from StockLensDatabaseMCP.)
        private static string SanitizeOsloLenName(string title)
        {
            static string NoNumbers(string t) => string.Join(" ", t
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !double.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out _)));
            string s = NoNumbers(title.Replace("\"", "'"));
            while (s.Length > 32)
                s = NoNumbers(s.Substring(0, 32));
            return s.Length == 0 ? "Untitled" : s;
        }
    }
}
