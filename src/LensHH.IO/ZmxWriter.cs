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
        public static void Write(OpticalSystem system, string filePath)
        {
            var sb = new StringBuilder();

            WriteHeader(sb, system);
            WriteAperture(sb, system);
            WriteFieldType(sb, system);
            WriteFields(sb, system);
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
            }
        }

        private static void WriteFieldType(StringBuilder sb, OpticalSystem system)
        {
            int ftype = system.FieldType == FieldType.ObjectAngle ? 0 : 1;
            int afocal = system.IsAfocal ? 1 : 0;
            sb.AppendLine($"FTYP {ftype} 0 {system.Fields.Count} {system.Wavelengths.Count} 0 0 {afocal} 0 0");
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

        private static void WriteFields(StringBuilder sb, OpticalSystem system)
        {
            sb.Append("XFLN");
            for (int i = 0; i < system.Fields.Count; i++)
                sb.Append(" 0");
            sb.AppendLine();

            sb.Append("YFLN");
            foreach (var field in system.Fields)
                sb.Append(" " + field.Y.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();

            sb.Append("FWGN");
            foreach (var field in system.Fields)
                sb.Append(" " + field.Weight.ToString(CultureInfo.InvariantCulture));
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

                switch (surface.Type)
                {
                    case SurfaceType.Standard:
                        sb.AppendLine("  TYPE STANDARD");
                        break;
                    case SurfaceType.EvenAsphere:
                        sb.AppendLine("  TYPE EVENASPH");
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

                if (!string.IsNullOrEmpty(surface.Material))
                    sb.AppendLine($"  GLAS {surface.Material} 0 0 0 0 0");

                // Only write DIAM line for Fixed mode; Auto diameters are computed by the reader
                if (surface.SemiDiameterMode == Enums.SemiDiameterMode.Fixed && surface.SemiDiameter > 0)
                {
                    sb.AppendLine($"  DIAM {surface.SemiDiameter.ToString("G17", CultureInfo.InvariantCulture)} 1 0 0 1 \"\"");
                }

                if (surface.Conic != 0)
                    sb.AppendLine(FormatDouble("  CONI", surface.Conic));

                // Aperture types
                if (surface.InnerRadius > 0)
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
            }
        }

        private static string FormatDouble(string keyword, double value)
        {
            return $"{keyword} {value.ToString("G17", CultureInfo.InvariantCulture)}";
        }
    }
}
