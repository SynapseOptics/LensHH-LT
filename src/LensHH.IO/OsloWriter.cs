using System;
using System.Globalization;
using System.Linq;
using System.Text;
using LensHH.Core.Enums;
using LensHH.Core.Models;

namespace LensHH.Core.IO
{
    /// <summary>
    /// Writes OSLO .len lens files.
    /// </summary>
    public static class OsloWriter
    {
        public static void Write(OpticalSystem system, string filePath)
        {
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

            // Aperture: EBR = EPD/2
            double ebr = system.Aperture.Type == ApertureType.EPD
                ? system.Aperture.Value / 2.0 : 5.0;
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "EBR {0:G8}", ebr));

            // Field angle (max field)
            double maxField = 0;
            foreach (var f in system.Fields)
                if (Math.Abs(f.Y) > maxField) maxField = Math.Abs(f.Y);
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "ANG {0:G8}", maxField));

            // Surface 0 (object)
            sb.AppendLine("// SRF 0");
            if (system.Surfaces.Count > 0)
            {
                var s0 = system.Surfaces[0];
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
                else if (!string.IsNullOrEmpty(s.Material))
                    sb.AppendLine($"  GLA {s.Material}");

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

                if (s.SemiDiameter > 0)
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  AP CHK {0:G8}", s.SemiDiameter));

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

            // Wavelengths
            if (system.Wavelengths.Count > 0)
            {
                var wvSb = new StringBuilder("WV ");
                var wwSb = new StringBuilder("WW ");
                foreach (var wl in system.Wavelengths)
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

        // OSLO's LEN NEW lens-name field has a 32-character cap and rejects
        // embedded double quotes. Strip quotes, collapse whitespace runs to
        // single spaces, trim, and truncate to 32 characters.
        private static string SanitizeOsloLenName(string title)
        {
            string s = title.Replace("\"", "'");
            var collapsed = new StringBuilder(s.Length);
            bool prevWs = false;
            foreach (char c in s)
            {
                bool ws = char.IsWhiteSpace(c);
                if (ws)
                {
                    if (!prevWs && collapsed.Length > 0) collapsed.Append(' ');
                    prevWs = true;
                }
                else { collapsed.Append(c); prevWs = false; }
            }
            string trimmed = collapsed.ToString().TrimEnd();
            return trimmed.Length > 32 ? trimmed.Substring(0, 32).TrimEnd() : trimmed;
        }
    }
}
