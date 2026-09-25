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
    /// Writes Code V .seq lens files.
    ///
    /// <para>The syntax follows what Code V itself writes, as the converters between it and
    /// OpticStudio read it (Zemax's CODEV-to-OpticStudio macro, v2.06): the aperture as
    /// <c>EPD</c>, <c>FNO</c> or <c>NAO</c>; fields as angles (<c>XAN</c>/<c>YAN</c>) or object
    /// heights (<c>XOB</c>/<c>YOB</c>); <c>REF</c> naming the primary wavelength; an asphere as
    /// <c>ASP</c> followed by its <c>K</c> and <c>A</c>..<c>G</c> lines, a pure conic as
    /// <c>CON</c> and <c>K</c>; a model glass as Code V's fictitious-glass code, or, when the code
    /// cannot carry it, as a private glass (<c>PRV</c>) given by its index at each wavelength.</para>
    /// </summary>
    public static class CodeVWriter
    {
        /// <summary>
        /// Write a Code V .seq file. Glasses are written as <c>GLASS_CATALOG</c>
        /// whenever the owning catalog can be identified and Code V has it, so
        /// that the glass -- not merely its name -- survives the round trip.
        /// <paramref name="glassMgr"/> is what identifies the owner; without one
        /// the system's own catalog list is used when it names exactly one. It
        /// also supplies the refractive indices a conversion needs: an F-number
        /// at a finite object, or a model glass Code V's code cannot carry.
        /// </summary>
        public static void Write(OpticalSystem system, string filePath, GlassCatalogManager? glassMgr = null)
        {
            var inv = CultureInfo.InvariantCulture;
            var qualifier = new CodeVGlassQualifier(glassMgr, system.GlassCatalogs);
            bool infiniteObject = system.Surfaces.Count == 0
                || double.IsInfinity(system.Surfaces[0].Thickness)
                || Math.Abs(system.Surfaces[0].Thickness) >= 1e10;
            var indicesByWavelength = new Dictionary<double, double[]>();
            double[] IndicesAt(double wavelengthUm)
            {
                if (!indicesByWavelength.TryGetValue(wavelengthUm, out var arr))
                {
                    arr = (glassMgr ?? throw new InvalidOperationException(
                            "Exporting this lens to Code V needs its refractive indices, and no glass catalog was given."))
                        .BuildRefractiveIndexArray(system, wavelengthUm);
                    indicesByWavelength[wavelengthUm] = arr;
                }
                return arr;
            }
            double PrimaryUm() => system.Wavelengths.Count > 0
                ? system.Wavelengths[Math.Max(0, system.PrimaryWavelengthIndex)].Value : 0.58756;

            var sb = new StringBuilder();
            sb.AppendLine("! Lens exported from LensHH-LT");
            sb.AppendLine("RDM;LEN");

            // Title. Code V quotes it, so an apostrophe inside would end it early.
            if (!string.IsNullOrEmpty(system.Title))
                sb.AppendLine($"TIT '{system.Title.Replace("'", "")}'");
            sb.AppendLine("DIM M");

            // Aperture. Code V's FNO is defined for an object at infinity, so at a finite object
            // an F-number goes out as the EPD it gives. (This wrote FNO with the value of any
            // aperture that was not an EPD: an object NA of 0.25 went out as F/0.25.)
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
                            "An object-space NA needs a finite object; Code V cannot take one for an object at infinity.");
                    sb.AppendLine(string.Format(inv, "NAO {0:R}", system.Aperture.Value));
                    break;
            }

            // Wavelengths in nm, with REF naming the primary.
            if (system.Wavelengths.Count > 0)
            {
                sb.AppendLine("WL  " + string.Join(" ", system.Wavelengths.Select(w => (w.Value * 1000.0).ToString("F4", inv))));
                sb.AppendLine("WTW " + string.Join(" ", system.Wavelengths.Select(w => w.Weight.ToString("G", inv))));
                sb.AppendLine($"REF {system.PrimaryWavelengthIndex + 1}");
            }

            // Fields: angles, or object heights. (Object heights went out as XAN/YAN, so a field
            // 5 mm off axis became one 5 degrees off.)
            if (system.Fields.Count > 0)
            {
                bool heights = system.FieldType == FieldType.ObjectHeight;
                if (heights && infiniteObject)
                    throw new InvalidOperationException(
                        "Fields given as object heights need a finite object; Code V takes a field angle for an object at infinity.");
                string x = heights ? "XOB" : "XAN", y = heights ? "YOB" : "YAN";
                sb.AppendLine(x + " " + string.Join(" ", system.Fields.Select(_ => "0.0")));
                sb.AppendLine(y + " " + string.Join(" ", system.Fields.Select(f => f.Y.ToString("R", inv))));
                sb.AppendLine("WTF " + string.Join(" ", system.Fields.Select(f => (f.Weight * 100).ToString("F0", inv))));
            }

            // Model glasses Code V's fictitious-glass code cannot carry go into a private catalog,
            // as their index at each wavelength.
            var privateGlass = new Dictionary<int, string>();
            for (int i = 0; i < system.Surfaces.Count; i++)
            {
                var s = system.Surfaces[i];
                if (s.ModelIndexEnabled && !s.IsMirror && FictitiousGlassCode(s.ModelNd, s.ModelVd, s.ModelDPgF) == null)
                    privateGlass[i] = $"LHHMODEL{i}";
            }
            if (privateGlass.Count > 0)
            {
                if (system.Wavelengths.Count == 0)
                    throw new InvalidOperationException("A model glass needs the lens's wavelengths to be written as a Code V private glass.");
                sb.AppendLine("PRV");
                sb.AppendLine("PWL " + string.Join(" ", system.Wavelengths.Select(w => (w.Value * 1000.0).ToString("F4", inv))));
                foreach (var kv in privateGlass)
                    sb.AppendLine($"'{kv.Value}' " + string.Join(" ", system.Wavelengths.Select(w =>
                        IndicesAt(w.Value)[kv.Key].ToString("R", inv))));
                sb.AppendLine("END");
            }

            // Surfaces
            for (int i = 0; i < system.Surfaces.Count; i++)
            {
                var s = system.Surfaces[i];
                bool isImage = i == system.Surfaces.Count - 1;
                string prefix = i == 0 ? "SO" : isImage ? "SI" : "S";

                // Code V has no ideal lens a .seq can name; writing one as a flat surface in air
                // (as this did) would hand over a different lens.
                if (s.Type == SurfaceType.Paraxial)
                    throw new InvalidOperationException(
                        $"Surface {i} is an ideal (paraxial) lens, which a Code V .seq file cannot carry.");

                double radius = double.IsInfinity(s.Radius) || double.IsNaN(s.Radius) ? 0.0 : s.Radius;
                double thickness = isImage ? 0.0
                                 : double.IsPositiveInfinity(s.Thickness) ? 1e20
                                 : s.Thickness;

                string material;
                if (s.IsMirror) material = "REFL";
                else if (s.ModelIndexEnabled)
                    material = privateGlass.TryGetValue(i, out var prv) ? prv
                             : FictitiousGlassCode(s.ModelNd, s.ModelVd, s.ModelDPgF)!;
                else if (string.IsNullOrEmpty(s.Material)) material = "AIR";
                else material = qualifier.ToCodeVMaterial(s.Material!);

                if (isImage)
                    sb.AppendLine(string.Format(inv, "SI {0:G14} 0", radius));
                else
                    sb.AppendLine(string.Format(inv, "{0} {1:G14} {2:G14} {3}", prefix, radius, thickness, material));

                if (s.IsStop)
                    sb.AppendLine("  STO");
                if (s.SemiDiameter > 0 && s.SemiDiameterMode == SemiDiameterMode.Fixed)
                    sb.AppendLine(string.Format(inv, "  CIR {0:G14}", s.SemiDiameter));
                double obscuration = s.InnerRadius > 0 ? s.InnerRadius : s.ObscurationRadius;
                if (obscuration > 0)
                    sb.AppendLine(string.Format(inv, "  CIR OBS {0:G14}", obscuration));

                // Aspheric data as Code V writes it: ASP, then the conic and the even terms on
                // lines of their own. (This wrote ASP and then CON, and CON makes the surface a
                // plain conic, dropping the terms.) Code V's A is r⁴; it has no r² term, so a
                // surface with one is refused rather than written as a different surface.
                var a = s.AsphericCoefficients ?? Array.Empty<double>();
                bool hasTerms = a.Skip(1).Any(c => c != 0);
                if (a.Length > 0 && a[0] != 0.0)
                    throw new InvalidOperationException(
                        $"Surface {i} has an r² aspheric term, which Code V's asphere has no place for.");
                if (hasTerms)
                {
                    sb.AppendLine("  ASP");
                    sb.AppendLine(string.Format(inv, "  K {0:R}", s.Conic));
                    var terms = new List<string>();
                    for (int c = 1; c < a.Length && c <= CoefficientNames.Length; c++)
                        terms.Add(string.Format(inv, "{0} {1:E14}", CoefficientNames[c - 1], a[c]));
                    for (int t = 0; t < terms.Count; t += 4)
                        sb.AppendLine("  " + string.Join(" ; ", terms.Skip(t).Take(4)));
                }
                else if (s.Conic != 0)
                {
                    sb.AppendLine("  CON");
                    sb.AppendLine(string.Format(inv, "  K {0:R}", s.Conic));
                }
            }

            sb.AppendLine("GO");
            System.IO.File.WriteAllText(filePath, sb.ToString());
        }

        /// <summary>Code V's names for the r⁴, r⁶ ... r¹⁶ terms.</summary>
        internal static readonly string[] CoefficientNames = { "A", "B", "C", "D", "E", "F", "G" };

        /// <summary>
        /// Code V's fictitious-glass code for (nd, Vd): the six digits of nd after "1.", a point,
        /// and six digits of Vd/100 - 1.5168 / 64.17 is <c>516800.641700</c>, as Zemax's
        /// converter reads it. Null when the code cannot carry the glass.
        /// </summary>
        public static string? FictitiousGlassCode(double nd, double vd, double dPgF)
        {
            if (dPgF != 0.0 || nd <= 1.0 || nd >= 2.0 || vd <= 0.0 || vd >= 100.0)
                return null;
            long n = (long)Math.Round((nd - 1.0) * 1e6);
            long v = (long)Math.Round(vd * 1e4);
            if (n >= 1_000_000 || v >= 1_000_000) return null;
            return n.ToString("000000", CultureInfo.InvariantCulture) + "."
                 + v.ToString("000000", CultureInfo.InvariantCulture);
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
