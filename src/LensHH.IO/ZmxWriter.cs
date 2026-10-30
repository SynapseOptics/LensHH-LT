using System;
using System.Globalization;
using System.IO;
using System.Text;
using LensHH.Core.Enums;
using LensHH.Core.Models;

namespace LensHH.Core.IO
{
    public static class ZmxWriter
    {
        /// <param name="allDirections">
        /// Write each off-axis field at +y, +x, −y and −x, the field on the axis once, each off-axis
        /// field's weight split among its four: the fields a lens that is not rotationally symmetric
        /// is judged at. ZEMAX has x fields, which LensHH-LT shows by turning the lens instead.
        /// </param>
        public static void Write(OpticalSystem system, string filePath, bool allDirections = false)
        {
            var sb = new StringBuilder();
            var fields = FieldsToWrite(system, allDirections);

            WriteHeader(sb, system);
            WriteAperture(sb, system);
            WriteFieldType(sb, system, fields.Count);
            WriteFields(sb, fields);
            WriteWavelengths(sb, system);
            WriteSurfaces(sb, system);

            // Zemax writes its own ZMX files as UTF-16 LE with BOM (FF FE).
            // Writing UTF-8 with BOM (EF BB BF) confuses Zemax's parser:
            // the BOM bytes get read as if they were UTF-16 code units,
            // shifting subsequent tokens — exposed by RayAiming=Off
            // displaying as "Paraxial" because the parser misaligned by
            // one. Match Zemax's encoding exactly.
            File.WriteAllText(filePath, sb.ToString(), Encoding.Unicode);
        }

        private static void WriteHeader(StringBuilder sb, OpticalSystem system)
        {
            sb.AppendLine($"VERS 140228 258 40400");
            sb.AppendLine($"MODE SEQ");

            if (!string.IsNullOrEmpty(system.Title))
                sb.AppendLine($"TITL {system.Title}");

            sb.AppendLine("UNIT MM X W X CM MR CPMM");

            if (system.GlassCatalogs.Count > 0)
                sb.AppendLine("GCAT " + string.Join(" ", system.GlassCatalogs));
        }

        private static void WriteAperture(StringBuilder sb, OpticalSystem system)
        {
            switch (system.Aperture.Type)
            {
                case ApertureType.EPD:
                    sb.AppendLine(FormatDouble("ENPD", system.Aperture.Value));
                    break;
                case ApertureType.FNumber:
                    sb.AppendLine(FormatDouble("FNUM", system.Aperture.Value));
                    break;
                case ApertureType.ObjectSpaceNA:
                    // OBNA <object-space NA> <0>. The trailing field is not the telecentric flag —
                    // ZEMAX carries object-space telecentric in the FTYP line's 2nd field (see WriteFieldType).
                    sb.AppendLine(FormatDouble("OBNA", system.Aperture.Value) + " 0");
                    break;
            }
        }

        private static void WriteFieldType(StringBuilder sb, OpticalSystem system, int fieldCount)
        {
            int ftype = system.FieldType == FieldType.ObjectAngle ? 0 : 1;
            int afocal = system.IsAfocal ? 1 : 0;
            // FTYP field 2 (after the field-type) is the object-space telecentric flag.
            int telecentric = system.TelecentricObjectSpace ? 1 : 0;
            sb.AppendLine($"FTYP {ftype} {telecentric} {fieldCount} {system.Wavelengths.Count} 0 0 {afocal} 0 0");
            // RAIM format:
            //   RAIM 0 <mode> 1 1 0 <robust> 0 0 0 1
            //
            // Token positions (parts[0]="RAIM"):
            //   parts[2] = mode: 0=Off, 1=Paraxial, 2=Real
            //   parts[6] = robust flag (1 if real-ray aiming with robust
            //              iterative search). Always 0 unless mode=2.
            //   parts[10] = always 1 (afocal makes no difference here;
            //               afocal is signaled in FTYP).
            //
            // Mode mapping: LensHH-LT's Real and Robust both map to the
            // file format's real-ray-aiming mode (2). Robust additionally
            // sets the parts[6] flag. Off maps to 0.
            bool useRealAiming = system.RayAiming == RayAimingMode.Real
                              || system.RayAiming == RayAimingMode.Robust;
            int zmxRayAimMode = useRealAiming ? 2 : 0;
            int robust = system.RayAiming == RayAimingMode.Robust ? 1 : 0;
            sb.AppendLine($"RAIM 0 {zmxRayAimMode} 1 1 0 {robust} 0 0 0 1");
        }

        /// <summary>A field as written: its point and weight, and its vignetting factors.</summary>
        private readonly struct ZmxField
        {
            public ZmxField(double x, double y, double weight, double vdx, double vdy, double vcx, double vcy, double tan)
            {
                X = x; Y = y; Weight = weight; Vdx = vdx; Vdy = vdy; Vcx = vcx; Vcy = vcy; Tan = tan;
            }

            public double X { get; }
            public double Y { get; }
            public double Weight { get; }
            public double Vdx { get; }
            public double Vdy { get; }
            public double Vcx { get; }
            public double Vcy { get; }
            public double Tan { get; }
        }

        private static System.Collections.Generic.List<ZmxField> FieldsToWrite(OpticalSystem system, bool allDirections)
        {
            var fields = new System.Collections.Generic.List<ZmxField>();
            foreach (var f in system.Fields)
                fields.Add(new ZmxField(0.0, f.Y, allDirections && Math.Abs(f.Y) >= 1e-12 ? f.Weight / 4.0 : f.Weight,
                                        f.VDX, f.VDY, f.VCX, f.VCY, f.TAN));
            if (!allDirections) return fields;

            // +x, −y, −x: the +y field and its vignetting decentre turned by a quarter, a half and
            // three quarters of a turn; a quarter turn swaps the compressions in x and y.
            var turns = new (Func<double, double, (double, double)> Turn, bool Swap)[]
            {
                ((x, y) => (y, -x), true),
                ((x, y) => (-x, -y), false),
                ((x, y) => (-y, x), true),
            };
            foreach (var (turn, swap) in turns)
                foreach (var f in system.Fields)
                {
                    if (Math.Abs(f.Y) < 1e-12) continue;
                    var (x, y) = turn(0.0, f.Y);
                    var (vdx, vdy) = turn(f.VDX, f.VDY);
                    fields.Add(new ZmxField(x, y, f.Weight / 4.0, vdx, vdy, swap ? f.VCY : f.VCX, swap ? f.VCX : f.VCY, f.TAN));
                }
            return fields;
        }

        private static void WriteFields(StringBuilder sb, System.Collections.Generic.List<ZmxField> fields)
        {
            WriteFieldFactor(sb, fields, "XFLN", f => f.X);
            WriteFieldFactor(sb, fields, "YFLN", f => f.Y);
            WriteFieldFactor(sb, fields, "FWGN", f => f.Weight);

            // Vignetting factors. Derived per-field values from the auto-vignetting solver (all zero
            // when there is no vignetting). VANN (tangential angle) is always 0 in LT. Callers that
            // need current values should solve semi-diameters before exporting.
            WriteFieldFactor(sb, fields, "VDXN", f => f.Vdx);
            WriteFieldFactor(sb, fields, "VDYN", f => f.Vdy);
            WriteFieldFactor(sb, fields, "VCXN", f => f.Vcx);
            WriteFieldFactor(sb, fields, "VCYN", f => f.Vcy);
            WriteFieldFactor(sb, fields, "VANN", f => f.Tan);
        }

        private static void WriteFieldFactor(StringBuilder sb, System.Collections.Generic.List<ZmxField> fields, string key,
            Func<ZmxField, double> sel)
        {
            sb.Append(key);
            foreach (var field in fields)
                sb.Append(" " + sel(field).ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();
        }

        private static void WriteWavelengths(StringBuilder sb, OpticalSystem system)
        {
            for (int i = 0; i < system.Wavelengths.Count; i++)
            {
                var wl = system.Wavelengths[i];
                sb.AppendLine($"WAVM {i + 1} {wl.Value.ToString(CultureInfo.InvariantCulture)} {wl.Weight.ToString(CultureInfo.InvariantCulture)}");
            }

            sb.AppendLine($"PWAV {system.PrimaryWavelengthIndex + 1}");
        }

        private static void WriteSurfaces(StringBuilder sb, OpticalSystem system)
        {
            foreach (var surface in system.Surfaces)
            {
                sb.AppendLine($"SURF {surface.Index}");

                if (!string.IsNullOrEmpty(surface.Comment))
                    sb.AppendLine($"  COMM {surface.Comment}");

                if (surface.IsStop)
                    sb.AppendLine("  STOP");

                // A Zernike departure on a Standard or Even Asphere surface is ZEMAX's Zernike
                // Fringe Sag surface: the even asphere's terms in PARM 1-8, the departure in its
                // extra data.
                bool zernike = (surface.Type == SurfaceType.Standard || surface.Type == SurfaceType.EvenAsphere)
                               && ZernikeDeparture.IsPresent(surface);
                switch (surface.Type)
                {
                    case SurfaceType.Standard when zernike:
                    case SurfaceType.EvenAsphere when zernike:
                        sb.AppendLine("  TYPE FZERNSAG");
                        break;
                    case SurfaceType.Standard:
                        sb.AppendLine("  TYPE STANDARD");
                        break;
                    case SurfaceType.EvenAsphere:
                        sb.AppendLine("  TYPE EVENASPH");
                        break;
                    case SurfaceType.Paraxial:
                        sb.AppendLine("  TYPE PARAXIAL");
                        break;
                    case SurfaceType.CoordinateBreak:
                        sb.AppendLine("  TYPE COORDBRK");
                        break;
                    case SurfaceType.Abcd:
                        sb.AppendLine("  TYPE ABCDSURF");
                        break;
                }

                sb.AppendLine(FormatDouble("  CURV", surface.Curvature));

                if (double.IsPositiveInfinity(surface.Thickness))
                    // ZEMAX's file parser accepts "INFINITY" (all caps) — that
                    // is what ZEMAX itself writes — and rejects "Infinity"
                    // (mixed case) with "Invalid input for thickness", even
                    // though the LDE cell DOES accept mixed-case input.
                    // (Reader side accepts any casing.)
                    sb.AppendLine("  DISZ INFINITY");
                else
                    sb.AppendLine(FormatDouble("  DISZ", surface.Thickness));

                if (surface.ModelIndexEnabled)
                {
                    // ZEMAX model glass: ___BLANK <flag> <solve> <Nd> <Vd> <dPgF> ...
                    // The index is defined by the model parameters, not a catalog name. An offset
                    // on a model glass is its Nd and Vd moved.
                    sb.AppendLine("  GLAS ___BLANK 1 0 " +
                        (surface.ModelNd + surface.IndexOffset).ToString("G17", CultureInfo.InvariantCulture) + " " +
                        (surface.ModelVd + surface.AbbeOffset).ToString("G17", CultureInfo.InvariantCulture) + " " +
                        surface.ModelDPgF.ToString("G17", CultureInfo.InvariantCulture) + " 0 0 0 0 0");
                }
                else if (!string.IsNullOrEmpty(surface.Material) && surface.HasGlassOffset)
                    // ZEMAX's Offset solve on the catalog glass: <name> 4 0 <Nd> <Vd> <dPgF> 0 0 0 <ΔNd> <ΔVd>.
                    // The catalog's own Nd, Vd and dPgF are filled in by ZEMAX when it reads the glass.
                    sb.AppendLine($"  GLAS {surface.Material} 4 0 0 0 0 0 0 0 " +
                        surface.IndexOffset.ToString("G17", CultureInfo.InvariantCulture) + " " +
                        surface.AbbeOffset.ToString("G17", CultureInfo.InvariantCulture));
                else if (!string.IsNullOrEmpty(surface.Material))
                    sb.AppendLine($"  GLAS {surface.Material} 0 0 0 0 0");

                // Only write DIAM line for Fixed mode; Auto diameters are computed by the reader
                if (surface.SemiDiameterMode == Enums.SemiDiameterMode.Fixed && surface.SemiDiameter > 0)
                {
                    sb.AppendLine($"  DIAM {surface.SemiDiameter.ToString("G17", CultureInfo.InvariantCulture)} 1 0 0 1 \"\"");
                }

                if (surface.Conic != 0)
                    sb.AppendLine(FormatDouble("  CONI", surface.Conic));

                // The drawn edge of the part. ZEMAX's MEMA never blocks a ray, like
                // MechanicalSemiDiameter.
                if (surface.MechanicalSemiDiameter > 0)
                    sb.AppendLine($"  MEMA {surface.MechanicalSemiDiameter.ToString("G17", CultureInfo.InvariantCulture)} 1 0 0 1 \"\"");

                // Aperture types
                if (surface.InnerRadius > 0 || surface.ClapOuterRadius > 0)
                {
                    double clapOuter = surface.ClapOuterRadius > 0 ? surface.ClapOuterRadius : surface.SemiDiameter;
                    sb.AppendLine($"  CLAP {surface.InnerRadius.ToString("G17", CultureInfo.InvariantCulture)} {clapOuter.ToString("G17", CultureInfo.InvariantCulture)} 0");
                }
                if (surface.ObscurationRadius > 0)
                    sb.AppendLine($"  OBSC 0 {surface.ObscurationRadius.ToString("G17", CultureInfo.InvariantCulture)} 0");
                if (surface.FloatingApertureRadius > 0)
                    sb.AppendLine($"  FLAP 0 {surface.FloatingApertureRadius.ToString("G17", CultureInfo.InvariantCulture)} 0");

                if (surface.Type == SurfaceType.EvenAsphere)
                {
                    for (int i = 0; i < surface.AsphericCoefficients.Length; i++)
                    {
                        if (surface.AsphericCoefficients[i] != 0)
                        {
                            sb.AppendLine($"  PARM {i + 1} {surface.AsphericCoefficients[i].ToString("E16", CultureInfo.InvariantCulture)}");
                        }
                    }
                }
                if (zernike)
                {
                    // Extra data 1 = the last term used, 2 = the normalization radius, 3.. = Z1, Z2, ...
                    int last = ZernikeDeparture.MaxTerms;
                    while (last > 1 && ZernikeDeparture.Term(surface, last) == 0.0) last--;
                    sb.AppendLine($"  XDAT 1 {last} 0 0 1 0 0 \"\"");
                    sb.AppendLine($"  XDAT 2 {ZernikeDeparture.Radius(surface).ToString("G17", CultureInfo.InvariantCulture)} 0 0 1 0 0 \"\"");
                    for (int j = 1; j <= last; j++)
                        sb.AppendLine($"  XDAT {j + 2} {ZernikeDeparture.Term(surface, j).ToString("G17", CultureInfo.InvariantCulture)} 0 0 1 0 0 \"\"");
                }
                else if (surface.Type == SurfaceType.Paraxial)
                {
                    // ZEMAX Paraxial: PARM 1 = focal length (mm), PARM 2 = OPD mode
                    // (1 = compute wavefront/OPD like a real lens — matches LT's
                    // diffraction-limited ideal lens). A VPAR line marks the focal
                    // length variable so an optimized design round-trips.
                    sb.AppendLine($"  PARM 1 {surface.FocalLength.ToString("G17", CultureInfo.InvariantCulture)}");
                    if (surface.FocalLengthVariable)
                        sb.AppendLine("  VPAR 1");
                    sb.AppendLine("  PARM 2 1");
                }
                else if (surface.Type == SurfaceType.CoordinateBreak)
                {
                    // ZEMAX Coordinate Break: PARM 1-5 = Decenter X, Decenter Y, Tilt X,
                    // Tilt Y, Tilt Z (Surface.Parameters[0-4], 1-based here); PARM 6 =
                    // Order (Settings[0]). A VPAR line marks each variable parameter so
                    // an optimized design round-trips.
                    for (int i = 1; i <= 5; i++)
                    {
                        sb.AppendLine($"  PARM {i} {surface.GetParameter(i).ToString("G17", CultureInfo.InvariantCulture)}");
                        if (surface.ParameterVariable[i - 1])
                            sb.AppendLine($"  VPAR {i}");
                    }
                    sb.AppendLine($"  PARM 6 {surface.GetSetting(1)}");
                }
                else if (surface.Type == SurfaceType.Abcd)
                {
                    // ZEMAX ABCD surface: PARM 1-8 = Ax,Bx,Cx,Dx,Ay,By,Cy,Dy. This
                    // implementation enforces x==y, so the x set (Parameters[0-3] =
                    // A,B,C,D) is written to BOTH halves. FIXED values only — variables
                    // and pickups are deliberately NOT emitted (per spec), so no VPAR.
                    for (int i = 1; i <= 4; i++)
                    {
                        string v = surface.GetParameter(i).ToString("G17", CultureInfo.InvariantCulture);
                        sb.AppendLine($"  PARM {i} {v}");       // Ax,Bx,Cx,Dx
                        sb.AppendLine($"  PARM {i + 4} {v}");   // Ay,By,Cy,Dy (= x set)
                    }
                }
            }
        }

        private static string FormatDouble(string keyword, double value)
        {
            return $"{keyword} {value.ToString("G17", CultureInfo.InvariantCulture)}";
        }
    }
}
