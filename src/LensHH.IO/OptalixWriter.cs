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
    /// Writes Optalix .OTX lens files.
    ///
    /// <para>The syntax follows what Optalix itself writes, surveyed across the 1019 lens files
    /// that ship with it, and its reference manual (§32.2): the aperture as <c>EPD</c>,
    /// <c>FNO</c> or <c>NAO</c>; <c>FTYP</c> 1 for field angles and 2 for object heights;
    /// <c>RAIM</c> 1 (paraxial pupil), 2 (real stop, Optalix's default) or 3 (telecentric);
    /// an ideal lens as Optalix's lens module, a pair of <c>SUT L</c> surfaces with the power in
    /// <c>LMOD</c>; a model glass as Optalix's fictitious-glass code, or its index at each
    /// wavelength (<c>PRI</c>); a mirror as <c>SM</c> or <c>AM</c>; <c>FH 1</c> on a surface
    /// whose aperture clips (Optalix's apertures otherwise never block a ray); and <c>ASP</c> as
    /// the conic and eight even terms. <c>PIM 0</c> keeps the image where the lens puts it.</para>
    /// </summary>
    public static class OptalixWriter
    {
        /// <param name="glassMgr">The glass catalogs, for the refractive indices a conversion
        /// needs: an F-number at a finite object, or a model glass Optalix's code cannot carry
        /// (written as its index at each wavelength). Not needed otherwise.</param>
        public static void Write(OpticalSystem system, string filePath, GlassCatalogManager? glassMgr = null)
        {
            var inv = CultureInfo.InvariantCulture;
            bool infiniteObject = system.Surfaces.Count == 0
                || double.IsInfinity(system.Surfaces[0].Thickness)
                || Math.Abs(system.Surfaces[0].Thickness) >= 1e10;
            var indicesByWavelength = new Dictionary<double, double[]>();
            double[] IndicesAt(double wavelengthUm)
            {
                if (!indicesByWavelength.TryGetValue(wavelengthUm, out var arr))
                {
                    arr = (glassMgr ?? throw new InvalidOperationException(
                            "Exporting this lens to Optalix needs its refractive indices, and no glass catalog was given."))
                        .BuildRefractiveIndexArray(system, wavelengthUm);
                    indicesByWavelength[wavelengthUm] = arr;
                }
                return arr;
            }
            double PrimaryUm() => system.Wavelengths.Count > 0
                ? system.Wavelengths[Math.Max(0, system.PrimaryWavelengthIndex)].Value : 0.58756;

            var sb = new StringBuilder();
            sb.AppendLine("VERS 11.82");
            sb.AppendLine($"FILE {filePath}");
            if (!string.IsNullOrEmpty(system.Title))
                sb.AppendLine($"REM 1 {system.Title}");

            // Ray aiming. Optalix writes 2 - aim at the real stop, its default - in 962 of its 1019
            // lens files, 1 for the paraxial entrance pupil and 3 for a telecentric object space;
            // it never writes 0. (This used to write RAIM 0 and then RAIM 2 on a second line.)
            int raim = system.TelecentricObjectSpace ? 3
                     : system.RayAiming == RayAimingMode.Off ? 1 : 2;
            sb.AppendLine($"RAIM {raim}");
            if (system.IsAfocal)
                sb.AppendLine("AFO 1000.0");   // Optalix's afocal mode, with its internal 1000 mm lens

            // Aperture. Optalix's FNO is defined at an object at infinity, so at a finite object
            // an F-number goes out as the EPD it gives. (This used to write EPD with whatever the
            // aperture's value was: an F/6.3 lens went out with an entrance pupil 6.3 mm across.)
            switch (system.Aperture.Type)
            {
                case ApertureType.EPD:
                    sb.AppendLine(string.Format(inv, "EPD {0:R}", system.Aperture.Value));
                    break;
                case ApertureType.FNumber:
                    if (infiniteObject)
                        sb.AppendLine(string.Format(inv, "FNO {0:R}", system.Aperture.Value));
                    else
                        sb.AppendLine(string.Format(inv, "EPD {0:R}",
                            Math.Abs(ParaxialEfl(system, IndicesAt(PrimaryUm()))) / system.Aperture.Value));
                    break;
                case ApertureType.ObjectSpaceNA:
                    if (infiniteObject)
                        throw new InvalidOperationException(
                            "An object-space NA needs a finite object; Optalix cannot take one for an object at infinity.");
                    sb.AppendLine(string.Format(inv, "NAO {0:R}", system.Aperture.Value));
                    break;
            }

            // Wavelengths, with REF naming the primary. Weights are Optalix's integers, 0 to 100.
            if (system.Wavelengths.Count > 0)
            {
                sb.AppendLine("WL " + string.Join(" ", system.Wavelengths.Select(w => w.Value.ToString("F7", inv))));
                sb.AppendLine("WTW " + string.Join(" ", system.Wavelengths.Select(w => Weight(w.Weight))));
                sb.AppendLine($"REF {system.PrimaryWavelengthIndex + 1}");
            }

            // Fields. Optalix's FTYP is 1 for field angles and 2 for object heights (3 and 4 are
            // image heights). (This wrote 0 for object heights, which is not an Optalix field type.)
            sb.AppendLine($"FTYP {(system.FieldType == FieldType.ObjectHeight ? 2 : 1)}");
            sb.AppendLine($"NFLD {system.Fields.Count}");
            for (int i = 0; i < system.Fields.Count; i++)
            {
                var f = system.Fields[i];
                sb.AppendLine(string.Format(inv, "FLD {0} {1:R} {2:R} {3} 1 0", i + 1, 0.0, f.Y, Weight(f.Weight)));
            }

            // The image stays where the lens puts it, rather than moving to paraxial focus.
            sb.AppendLine("PIM 0");

            sb.AppendLine("! Surface data :");
            int k = 0;   // Optalix surface number: an ideal lens takes two
            for (int i = 0; i < system.Surfaces.Count; i++)
            {
                var s = system.Surfaces[i];
                bool isImage = i == system.Surfaces.Count - 1;
                double thi = isImage ? 0.0
                           : double.IsPositiveInfinity(s.Thickness) ? 1e20
                           : s.Thickness;

                if (s.Type == SurfaceType.Paraxial)
                {
                    // Optalix's lens module: two L surfaces, the principal planes, here coincident;
                    // the power (not the focal length) on the first. It is perfect at the
                    // magnification MRD, which the file does not carry: Optalix's default is an
                    // object at infinity.
                    double power = double.IsInfinity(s.FocalLength) || s.FocalLength == 0 ? 0.0 : 1.0 / s.FocalLength;
                    sb.AppendLine($"SUR {k++}");
                    sb.AppendLine("  SUT L");
                    sb.AppendLine("  CUY 0");
                    sb.AppendLine("  THI 0");
                    if (s.IsStop)
                        sb.AppendLine("  STO");
                    WriteApertures(sb, s, inv);
                    sb.AppendLine(string.Format(inv, "  LMOD {0:R} 0 0 0 0", power));
                    if (!string.IsNullOrWhiteSpace(s.Comment))
                        sb.AppendLine($"  COM {s.Comment.Trim()}");
                    sb.AppendLine($"SUR {k++}");
                    sb.AppendLine("  SUT L");
                    sb.AppendLine("  CUY 0");
                    sb.AppendLine(string.Format(inv, "  THI {0:R}", thi));
                    WriteMaterial(sb, s, i, IndicesAt, system, inv);
                    sb.AppendLine("  LMOD 0 0 0 0 0");
                    continue;
                }

                sb.AppendLine($"SUR {k++}");

                bool isMirror = s.IsMirror;
                bool hasAspheric = s.Type == SurfaceType.EvenAsphere
                    || s.Conic != 0
                    || (s.AsphericCoefficients != null && s.AsphericCoefficients.Any(c => c != 0));
                // One base type - S sphere or A asphere - and M for a mirror. (A plain mirror went out
                // as a bare M, which has no base type.)
                sb.AppendLine($"  SUT {(hasAspheric ? "A" : "S")}{(isMirror ? "M" : "")}");

                double cuy = double.IsInfinity(s.Radius) ? 0.0 : s.Curvature;
                sb.AppendLine(string.Format(inv, "  CUY {0:E16}", cuy));
                sb.AppendLine(string.Format(inv, "  THI {0:E16}", thi));

                if (!isMirror)
                    WriteMaterial(sb, s, i, IndicesAt, system, inv);

                if (s.IsStop)
                    sb.AppendLine("  STO");

                WriteApertures(sb, s, inv);

                // COM = surface label (PRIMARY, SECONDARY, etc.).
                if (!string.IsNullOrWhiteSpace(s.Comment))
                    sb.AppendLine($"  COM {s.Comment.Trim()}");

                // VAR <count> <param1> [...] — variable flags for optimization. A/B/C/D are the
                // Optalix names for the r⁴/r⁶/r⁸/r¹⁰ coefficients (internal indices 1..4).
                var vars = new List<string>();
                if (s.CurvatureVariable) vars.Add("CUY");
                if (s.ThicknessVariable) vars.Add("THI");
                if (s.ConicVariable)     vars.Add("K");
                if (s.AsphericVariable != null)
                {
                    string[] aspNames = { "A", "B", "C", "D" };
                    for (int v = 0; v < aspNames.Length; v++)
                    {
                        int idx = v + 1;
                        if (idx < s.AsphericVariable.Length && s.AsphericVariable[idx])
                            vars.Add(aspNames[v]);
                    }
                }
                if (vars.Count > 0)
                    sb.AppendLine($"  VAR {vars.Count} {string.Join(" ", vars)}");

                // Aspheric data as Optalix writes it: the conic, the eight even terms A..H (r⁴ to
                // r¹⁸), and a zero - ten values. Optalix's even asphere has no r² term, so a surface
                // with one cannot be written faithfully; it is refused rather than dropped.
                if (hasAspheric)
                {
                    var a = s.AsphericCoefficients ?? Array.Empty<double>();
                    if (a.Length > 0 && a[0] != 0.0)
                        throw new InvalidOperationException(
                            $"Surface {i} has an r² aspheric term, which Optalix's even asphere has no place for.");
                    var aspSb = new StringBuilder("  ASP");
                    aspSb.Append(string.Format(inv, " {0:R}", s.Conic));
                    for (int c = 1; c <= 8; c++)
                        aspSb.Append(string.Format(inv, " {0:E10}", c < a.Length ? a[c] : 0.0));
                    aspSb.Append(" 0");
                    sb.AppendLine(aspSb.ToString());
                }
            }

            System.IO.File.WriteAllText(filePath, sb.ToString());
        }

        private static string Weight(double w) =>
            Math.Max(0, Math.Min(100, (int)Math.Round(w * 100.0))).ToString(CultureInfo.InvariantCulture);

        // The medium after the surface: a catalog glass by name; a model glass as Optalix's
        // fictitious-glass code when it can carry it - nd to six decimals, Vd from 10 to 99.9999,
        // no partial-dispersion offset - and otherwise as its index at each wavelength (PRI). Air
        // is written as nothing. (A model glass was not written at all, and the surface went out
        // as air.)
        private static void WriteMaterial(StringBuilder sb, Surface s, int i, Func<double, double[]> indicesAt,
                                          OpticalSystem system, CultureInfo inv)
        {
            if (s.ModelIndexEnabled)
            {
                string? code = FictitiousGlassCode(s.ModelNd, s.ModelVd, s.ModelDPgF);
                if (code != null)
                    sb.AppendLine($"  GLA {code}");
                else
                    sb.AppendLine("  PRI " + string.Join(" ", system.Wavelengths.Select(w =>
                        indicesAt(w.Value)[i].ToString("R", inv))));
            }
            else if (!string.IsNullOrEmpty(s.Material) && !s.IsMirror)
                sb.AppendLine($"  GLA {s.Material}");
        }

        /// <summary>
        /// Optalix's fictitious-glass code for (nd, Vd): the digits of nd − 1 after the decimal
        /// point, a point, then Vd's two integer digits and its decimals - 1.6201 / 60.4 is
        /// <c>6201.604</c>, 1.516 / 64 is <c>516.64</c> - as the files that ship with Optalix write
        /// it. Null when the code cannot carry the glass.
        /// </summary>
        public static string? FictitiousGlassCode(double nd, double vd, double dPgF)
        {
            if (dPgF != 0.0 || nd <= 1.0 || nd >= 2.0 || vd < 10.0 || vd >= 100.0)
                return null;
            string ndDigits = Math.Round(nd - 1.0, 6).ToString("0.000000", CultureInfo.InvariantCulture)
                .Substring(2).TrimEnd('0');
            if (ndDigits.Length < 3)
                ndDigits = ndDigits.PadRight(3, '0');
            string vdText = Math.Round(vd, 4).ToString("00.####", CultureInfo.InvariantCulture);
            return ndDigits + "." + vdText.Replace(".", "");
        }

        private static void WriteApertures(StringBuilder sb, Surface s, CultureInfo inv)
        {
            if (s.SemiDiameter > 0)
                sb.AppendLine(string.Format(inv, "  APE  1 {0:G12} {0:G12} 0 0 0 1 0 0 1 ''", s.SemiDiameter));

            // APE 2 = a central obscuration, transmission 1 (obstruct): the reader's InnerRadius (a
            // mirror's central hole) or ObscurationRadius (a baffle shadow).
            double obscR = s.InnerRadius > 0 ? s.InnerRadius
                         : s.ObscurationRadius > 0 ? s.ObscurationRadius
                         : 0;
            if (obscR > 0)
                sb.AppendLine(string.Format(inv, "  APE  2 {0:G12} {0:G12} 0 0 0 1 0 1 1 ''", obscR));

            // FH 1 marks an aperture that clips: Optalix's apertures otherwise size the surface and
            // never block a ray (reference manual p. 166). A fixed semi-diameter clips here, and so
            // does an automatic one held under 100 % of the beam. (No FH was written, so an
            // exported lens was not vignetted in Optalix where its source was.)
            bool clips = s.SemiDiameterMode == SemiDiameterMode.Fixed
                || (s.ClearAperturePercent > 0 && s.ClearAperturePercent < 100.0);
            if (clips && s.SemiDiameter > 0)
                sb.AppendLine("  FH 1 1");
        }

        // The paraxial EFL, from the C# transfer matrix of the optical surfaces (EFL = -1/(n' c));
        // the native EFL returns 0 on an engine that is not activated.
        private static double ParaxialEfl(OpticalSystem system, double[] indices)
        {
            int last = system.Surfaces.Count - 2;
            var (_, _, c, _) = new ParaxialRayTracer(system, indices).ComputeSubsystemMatrix(1, last);
            if (Math.Abs(c) < 1e-300)
                throw new InvalidOperationException("The lens is afocal, so an F-number gives it no entrance pupil.");
            return -1.0 / (Math.Abs(indices[last]) * c);
        }
    }
}
