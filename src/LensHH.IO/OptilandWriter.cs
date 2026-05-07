using System;
using System.Globalization;
using System.IO;
using System.Text;
using LensHH.Core.Enums;
using LensHH.Core.Models;

namespace LensHH.Core.IO
{
    /// <summary>
    /// Writes Optiland .json lens files.
    /// Produces JSON compatible with the Optiland Python optical design tool.
    /// </summary>
    public static class OptilandWriter
    {
        public static void Write(OpticalSystem system, string filePath)
        {
            var sb = new StringBuilder();
            string indent = "    ";

            sb.AppendLine("{");
            sb.AppendLine($"{indent}\"version\": 1.0,");

            // Aperture
            string apertureType = system.Aperture.Type == ApertureType.FNumber ? "imageFNO" : "EPD";
            sb.AppendLine($"{indent}\"aperture\": {{");
            sb.AppendLine($"{indent}{indent}\"type\": \"{apertureType}\",");
            sb.AppendLine($"{indent}{indent}\"value\": {Fmt(system.Aperture.Value)},");
            sb.AppendLine($"{indent}{indent}\"object_space_telecentric\": false");
            sb.AppendLine($"{indent}}},");

            // Fields
            string fieldType = system.FieldType == FieldType.ObjectHeight ? "object_height" : "angle";
            sb.AppendLine($"{indent}\"fields\": {{");
            sb.AppendLine($"{indent}{indent}\"fields\": [");
            for (int i = 0; i < system.Fields.Count; i++)
            {
                var f = system.Fields[i];
                string comma = i < system.Fields.Count - 1 ? "," : "";
                sb.AppendLine($"{indent}{indent}{indent}{{\"field_type\": \"{fieldType}\", \"x\": 0.0, \"y\": {Fmt(f.Y)}, \"vx\": 0.0, \"vy\": 0.0}}{comma}");
            }
            sb.AppendLine($"{indent}{indent}],");
            sb.AppendLine($"{indent}{indent}\"telecentric\": false,");
            sb.AppendLine($"{indent}{indent}\"field_type\": \"{fieldType}\",");
            sb.AppendLine($"{indent}{indent}\"object_space_telecentric\": false");
            sb.AppendLine($"{indent}}},");

            // Wavelengths
            sb.AppendLine($"{indent}\"wavelengths\": {{");
            sb.AppendLine($"{indent}{indent}\"wavelengths\": [");
            for (int i = 0; i < system.Wavelengths.Count; i++)
            {
                var w = system.Wavelengths[i];
                string comma = i < system.Wavelengths.Count - 1 ? "," : "";
                string primary = w.IsPrimary ? "true" : "false";
                sb.AppendLine($"{indent}{indent}{indent}{{\"value\": {Fmt(w.Value)}, \"is_primary\": {primary}, \"unit\": \"um\", \"weight\": {Fmt(w.Weight)}}}{comma}");
            }
            sb.AppendLine($"{indent}{indent}],");
            sb.AppendLine($"{indent}{indent}\"polarization\": \"ignore\"");
            sb.AppendLine($"{indent}}},");

            // Pickups and Solves (empty)
            sb.AppendLine($"{indent}\"pickups\": [],");
            sb.AppendLine($"{indent}\"solves\": {{\"solves\": []}},");

            // Surface group
            sb.AppendLine($"{indent}\"surface_group\": {{");
            sb.AppendLine($"{indent}{indent}\"surfaces\": [");

            // Compute cumulative Z from thicknesses
            double cumulativeZ = 0;
            var zValues = new double[system.Surfaces.Count];
            for (int i = 0; i < system.Surfaces.Count; i++)
            {
                zValues[i] = cumulativeZ;
                double th = system.Surfaces[i].Thickness;
                if (!double.IsInfinity(th) && !double.IsNaN(th))
                    cumulativeZ += th;
            }

            for (int i = 0; i < system.Surfaces.Count; i++)
            {
                var s = system.Surfaces[i];
                bool isObject = (i == 0);
                bool isImage = (i == system.Surfaces.Count - 1);
                string comma = i < system.Surfaces.Count - 1 ? "," : "";

                string surfType = isObject ? "ObjectSurface" : "Surface";
                double z = isObject && double.IsInfinity(s.Thickness) ? double.NegativeInfinity : zValues[i];

                // Geometry
                string geomType;
                bool hasAsphere = s.Type == SurfaceType.EvenAsphere && HasNonZeroCoeffs(s);
                if (double.IsInfinity(s.Radius))
                    geomType = "Plane";
                else if (hasAsphere)
                    geomType = "EvenAsphere";
                else
                    geomType = "StandardGeometry";

                sb.AppendLine($"{indent}{indent}{indent}{{");
                sb.AppendLine($"{indent}{indent}{indent}{indent}\"type\": \"{surfType}\",");

                // Geometry block
                sb.AppendLine($"{indent}{indent}{indent}{indent}\"geometry\": {{");
                sb.AppendLine($"{indent}{indent}{indent}{indent}{indent}\"type\": \"{geomType}\",");
                sb.AppendLine($"{indent}{indent}{indent}{indent}{indent}\"cs\": {{");
                sb.AppendLine($"{indent}{indent}{indent}{indent}{indent}{indent}\"x\": 0.0, \"y\": 0.0, \"z\": {FmtInf(z)},");
                sb.AppendLine($"{indent}{indent}{indent}{indent}{indent}{indent}\"rx\": 0.0, \"ry\": 0.0, \"rz\": 0.0,");
                sb.AppendLine($"{indent}{indent}{indent}{indent}{indent}{indent}\"reference_cs\": null");
                sb.AppendLine($"{indent}{indent}{indent}{indent}{indent}}},");
                sb.Append($"{indent}{indent}{indent}{indent}{indent}\"radius\": {FmtInf(s.Radius)}");

                if (geomType == "StandardGeometry" || geomType == "EvenAsphere")
                {
                    sb.AppendLine(",");
                    sb.Append($"{indent}{indent}{indent}{indent}{indent}\"conic\": {Fmt(s.Conic)}");
                }

                if (geomType == "EvenAsphere" && hasAsphere)
                {
                    sb.AppendLine(",");
                    sb.Append($"{indent}{indent}{indent}{indent}{indent}\"coefficients\": [");
                    for (int j = 0; j < s.AsphericCoefficients.Length; j++)
                    {
                        if (j > 0) sb.Append(", ");
                        sb.Append(Fmt(s.AsphericCoefficients[j]));
                    }
                    sb.Append("]");
                }

                sb.AppendLine();
                sb.AppendLine($"{indent}{indent}{indent}{indent}}},");

                // Material pre (for non-object surfaces)
                bool isMirror = !string.IsNullOrEmpty(s.Material) &&
                                s.Material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase);
                bool hasGlass = !string.IsNullOrEmpty(s.Material) && !isMirror;

                if (!isObject)
                {
                    // material_pre: glass from previous surface's material_post, or air
                    var prevSurf = system.Surfaces[i - 1];
                    bool prevHasGlass = !string.IsNullOrEmpty(prevSurf.Material) &&
                                        !prevSurf.Material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase);
                    if (prevHasGlass)
                    {
                        sb.AppendLine($"{indent}{indent}{indent}{indent}\"material_pre\": {{");
                        sb.AppendLine($"{indent}{indent}{indent}{indent}{indent}\"type\": \"Material\",");
                        sb.AppendLine($"{indent}{indent}{indent}{indent}{indent}\"name\": \"{prevSurf.Material}\",");
                        sb.AppendLine($"{indent}{indent}{indent}{indent}{indent}\"reference\": null,");
                        sb.AppendLine($"{indent}{indent}{indent}{indent}{indent}\"robust_search\": true,");
                        sb.AppendLine($"{indent}{indent}{indent}{indent}{indent}\"min_wavelength\": null,");
                        sb.AppendLine($"{indent}{indent}{indent}{indent}{indent}\"max_wavelength\": null");
                        sb.AppendLine($"{indent}{indent}{indent}{indent}}},");
                    }
                    else
                    {
                        sb.AppendLine($"{indent}{indent}{indent}{indent}\"material_pre\": {{\"type\": \"IdealMaterial\", \"index\": 1.0, \"absorp\": 0.0}},");
                    }
                }

                // material_post — trailing comma only when more properties
                // follow. Non-object surfaces have an is_stop block after
                // material_post; the object surface stops here, so no comma.
                string mpTail = isObject ? "" : ",";
                if (hasGlass)
                {
                    sb.AppendLine($"{indent}{indent}{indent}{indent}\"material_post\": {{");
                    sb.AppendLine($"{indent}{indent}{indent}{indent}{indent}\"type\": \"Material\",");
                    sb.AppendLine($"{indent}{indent}{indent}{indent}{indent}\"name\": \"{s.Material}\",");
                    sb.AppendLine($"{indent}{indent}{indent}{indent}{indent}\"reference\": null,");
                    sb.AppendLine($"{indent}{indent}{indent}{indent}{indent}\"robust_search\": true,");
                    sb.AppendLine($"{indent}{indent}{indent}{indent}{indent}\"min_wavelength\": null,");
                    sb.AppendLine($"{indent}{indent}{indent}{indent}{indent}\"max_wavelength\": null");
                    sb.AppendLine($"{indent}{indent}{indent}{indent}}}{mpTail}");
                }
                else
                {
                    sb.AppendLine($"{indent}{indent}{indent}{indent}\"material_post\": {{\"type\": \"IdealMaterial\", \"index\": 1.0, \"absorp\": 0.0}}{mpTail}");
                }

                // is_stop, aperture, coating, bsdf, is_reflective
                if (!isObject)
                {
                    sb.AppendLine($"{indent}{indent}{indent}{indent}\"is_stop\": {(s.IsStop ? "true" : "false")},");
                    sb.AppendLine($"{indent}{indent}{indent}{indent}\"aperture\": null,");
                    sb.AppendLine($"{indent}{indent}{indent}{indent}\"coating\": null,");
                    sb.AppendLine($"{indent}{indent}{indent}{indent}\"bsdf\": null,");
                    sb.AppendLine($"{indent}{indent}{indent}{indent}\"is_reflective\": {(isMirror ? "true" : "false")}");
                }

                sb.AppendLine($"{indent}{indent}{indent}}}{comma}");
            }

            sb.AppendLine($"{indent}{indent}]");
            sb.AppendLine($"{indent}}}");
            sb.AppendLine("}");

            File.WriteAllText(filePath, sb.ToString());
        }

        private static string Fmt(double v)
        {
            return v.ToString("G14", CultureInfo.InvariantCulture);
        }

        private static string FmtInf(double v)
        {
            // Optiland's canonical JSON uses literal Infinity / -Infinity
            // tokens (see OptilandNet's own sample cooke_triplet_original.json).
            // Both Python json (with allow_nan=True, default) and OptilandNet's
            // parser accept these. A finite sentinel like 1e30 also parses but
            // breaks layout rendering — OptilandNet treats it as a real finite
            // object distance and the lens shrinks to invisibility.
            if (double.IsPositiveInfinity(v)) return "Infinity";
            if (double.IsNegativeInfinity(v)) return "-Infinity";
            return Fmt(v);
        }

        private static bool HasNonZeroCoeffs(Surface s)
        {
            if (s.AsphericCoefficients == null) return false;
            foreach (var c in s.AsphericCoefficients)
                if (c != 0) return true;
            return false;
        }
    }
}
