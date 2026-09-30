using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using LensHH.Core.Enums;
using LensHH.Core.Glass;
using LensHH.Core.Models;

namespace LensHH.Core.IO
{
    /// <summary>
    /// Writes Optiland .json lens files.
    /// Produces JSON compatible with the Optiland Python optical design tool.
    ///
    /// <para>Glass is written as <see cref="OptilandGlass"/> describes:</para>
    /// <list type="bullet">
    /// <item>in the lens file, each glass is named with its catalog and
    /// <c>match_policy: "strict"</c>;</item>
    /// <item>beside it, the glass's own dispersion data goes into a folder of Optiland user
    /// catalogs;</item>
    /// <item>and the same catalogs are installed where Optiland on this machine reads them (see
    /// <see cref="OptilandGlass.UserCatalogsFolder"/>), so the lens opens there with no further
    /// step.</item>
    /// </list>
    /// <para>Earlier versions wrote a bare name with <c>robust_search</c>, which lets Optiland take
    /// the nearest name from any catalog. Many common glasses came back as a different glass that
    /// way, and a model glass was written as a name Optiland could not know.</para>
    /// </summary>
    /// <summary>Where an Optiland export put the lens's glasses.</summary>
    public sealed class OptilandExport
    {
        /// <summary>The folder of glasses beside the lens file, or null when it has none.</summary>
        public string? GlassFolder { get; set; }

        /// <summary>The Optiland catalogs folder the glasses were installed in, or null.</summary>
        public string? InstalledTo { get; set; }

        /// <summary>How many glass files were installed.</summary>
        public int InstalledGlasses { get; set; }

        /// <summary>Why installing failed, or null.</summary>
        public string? InstallError { get; set; }

        /// <summary>One or two lines saying where the glasses went, for a message.</summary>
        public string Describe()
        {
            if (GlassFolder == null) return "The lens has no glass, so there were no glasses to write.";
            var sb = new StringBuilder();
            if (InstalledTo != null)
                sb.Append($"The glasses are installed for Optiland in {InstalledTo}; Optiland reads them when it "
                        + "starts (restart a Python session that already had Optiland loaded). ");
            else if (InstallError != null)
                sb.Append(InstallError + ". Copy the folders inside the folder below into ~/.optiland/catalogs/ by hand. ");
            sb.Append($"A copy is in {GlassFolder}, to take with the lens to another machine.");
            return sb.ToString();
        }
    }

    public static class OptilandWriter
    {
        private const string Air = "{\"type\": \"IdealMaterial\", \"index\": 1.0, \"absorp\": 0.0}";

        /// <summary>Writes the lens, its glasses beside it, and installs the glasses for Optiland.</summary>
        /// <param name="glassMgr">The glass catalogs. With them, each glass is written with its
        /// dispersion data. Without them, a catalog glass goes out by name only, for Optiland's
        /// own database: strict, and with a catalog only when the system names exactly one.</param>
        /// <param name="install">Whether to install the glasses into this machine's Optiland
        /// catalogs as well as writing them beside the lens.</param>
        /// <returns>Where the glasses went.</returns>
        public static OptilandExport Write(OpticalSystem system, string filePath, GlassCatalogManager? glassMgr = null,
                                           bool install = true)
        {
            var sb = new StringBuilder();
            string indent = "    ";
            var materials = Materials(system, glassMgr, out var ymls);

            sb.AppendLine("{");
            sb.AppendLine($"{indent}\"version\": 1.0,");

            // Aperture
            // An object-space NA is Optiland's objectNA. (It went out as an EPD of the same number.)
            string apertureType = system.Aperture.Type switch
            {
                ApertureType.FNumber => "imageFNO",
                ApertureType.ObjectSpaceNA => "objectNA",
                _ => "EPD",
            };
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
                bool isMirror = s.IsMirror;
                bool hasGlass = materials[i] != null;

                if (!isObject)
                {
                    // material_pre: glass from previous surface's material_post, or air
                    bool prevHasGlass = materials[i - 1] != null;
                    if (prevHasGlass)
                        sb.AppendLine($"{indent}{indent}{indent}{indent}\"material_pre\": {materials[i - 1]},");
                    else
                        sb.AppendLine($"{indent}{indent}{indent}{indent}\"material_pre\": {Air},");
                }

                // material_post — trailing comma only when more properties
                // follow. Non-object surfaces have an is_stop block after
                // material_post; the object surface stops here, so no comma.
                string mpTail = isObject ? "" : ",";
                sb.AppendLine($"{indent}{indent}{indent}{indent}\"material_post\": {(hasGlass ? materials[i] : Air)}{mpTail}");

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
            var result = new OptilandExport { GlassFolder = WriteGlassFolder(filePath, ymls) };
            if (install && ymls.Count > 0) Install(ymls, result);
            return result;
        }

        // Into this machine's Optiland user catalogs. Only the lenshh- folders are written, and
        // nothing is deleted: other exported lenses may use the same glasses. A glass already
        // there is replaced by the current data, which is the same glass. A failure here leaves
        // the export itself good, and is reported.
        private static void Install(SortedDictionary<string, SortedDictionary<string, string>> ymls, OptilandExport result)
        {
            string target = OptilandGlass.UserCatalogsFolder;
            try
            {
                int count = 0;
                foreach (var catalog in ymls)
                {
                    string sub = Path.Combine(target, catalog.Key);
                    Directory.CreateDirectory(sub);
                    foreach (var glass in catalog.Value)
                    {
                        File.WriteAllText(Path.Combine(sub, glass.Key + ".yml"), glass.Value, new UTF8Encoding(false));
                        count++;
                    }
                }
                result.InstalledTo = target;
                result.InstalledGlasses = count;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                                       || ex is System.Security.SecurityException || ex is ArgumentException)
            {
                result.InstallError = $"The glasses could not be installed in {target}: {ex.Message}";
            }
        }

        // The material_post object for each surface, or null for air and mirrors; and the .yml
        // of each glass, keyed by catalog then name.
        private static string?[] Materials(OpticalSystem system, GlassCatalogManager? glassMgr,
            out SortedDictionary<string, SortedDictionary<string, string>> ymls)
        {
            ymls = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var materials = new string?[system.Surfaces.Count];
            IList<string>? preferred = system.GlassCatalogs.Count > 0 ? system.GlassCatalogs : null;

            for (int i = 0; i < system.Surfaces.Count; i++)
            {
                var s = system.Surfaces[i];
                if (s.IsMirror) continue;

                if (s.ModelIndexEnabled)
                {
                    string name = OptilandGlass.ModelName(s.ModelNd, s.ModelVd, s.ModelDPgF);
                    var (formula, c) = OptilandGlass.ModelDispersion(s.ModelNd, s.ModelVd, s.ModelDPgF);
                    string description = string.Format(CultureInfo.InvariantCulture,
                        "LensHH-LT model glass: nd {0:R}, Vd {1:R}, dPgF {2:R}", s.ModelNd, s.ModelVd, s.ModelDPgF);
                    Add(ymls, OptilandGlass.ModelCatalog, name, OptilandGlass.Yml(description, formula, c, 0.2, 5.0));
                    materials[i] = MaterialJson(name, OptilandGlass.ModelCatalog);
                    continue;
                }

                if (string.IsNullOrEmpty(s.Material) || s.Material.Equals("AIR", StringComparison.OrdinalIgnoreCase))
                    continue;

                var glass = glassMgr?.GetGlass(s.Material, preferred);
                var dispersion = glass != null ? OptilandGlass.Dispersion(glass) : null;
                if (glass != null && dispersion != null && !string.IsNullOrEmpty(glass.Catalog)
                    && OptilandGlass.IsFileName(glass.Name))
                {
                    string catalog = OptilandGlass.DataCatalog(glass.Catalog);
                    var (formula, c) = dispersion.Value;
                    double lo = glass.WavelengthMin > 0 ? glass.WavelengthMin : 0.2;
                    double hi = glass.WavelengthMax > lo ? glass.WavelengthMax : 5.0;
                    Add(ymls, catalog, glass.Name,
                        OptilandGlass.Yml($"{glass.Catalog} {glass.Name}, as LensHH-LT computes it", formula, c, lo, hi));
                    materials[i] = MaterialJson(glass.Name, catalog);
                }
                else
                {
                    // Not in any catalog we have (or a name no file can carry): the name alone,
                    // still strict, so Optiland either has exactly this glass or says it does not.
                    string? catalog = system.GlassCatalogs.Count == 1
                        ? OptilandGlass.VendorCatalog(system.GlassCatalogs[0]) : null;
                    materials[i] = MaterialJson(s.Material, catalog);
                }
            }
            return materials;
        }

        private static void Add(SortedDictionary<string, SortedDictionary<string, string>> ymls,
                                string catalog, string name, string yml)
        {
            if (!ymls.TryGetValue(catalog, out var names))
                ymls[catalog] = names = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            names[name] = yml;
        }

        private static string MaterialJson(string name, string? catalog) =>
            "{\"type\": \"Material\", \"name\": " + JsonString(name)
            + ", \"reference\": null, \"catalog\": " + (catalog == null ? "null" : JsonString(catalog))
            + ", \"match_policy\": \"strict\", \"robust_search\": null"
            + ", \"min_wavelength\": null, \"max_wavelength\": null}";

        private static string JsonString(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char ch in s)
            {
                if (ch == '"' || ch == '\\') sb.Append('\\').Append(ch);
                else if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4"));
                else sb.Append(ch);
            }
            return sb.Append('"').ToString();
        }

        // The glasses, as Optiland user catalogs in a folder named after the lens file. The folder
        // is rewritten whole, so a glass the lens no longer uses does not linger in it.
        private static string? WriteGlassFolder(string filePath,
            SortedDictionary<string, SortedDictionary<string, string>> ymls)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".";
            string folder = Path.Combine(dir, Path.GetFileNameWithoutExtension(filePath) + "_glass");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            if (ymls.Count == 0) return null;

            foreach (var catalog in ymls)
            {
                string sub = Path.Combine(folder, catalog.Key);
                Directory.CreateDirectory(sub);
                foreach (var glass in catalog.Value)
                    File.WriteAllText(Path.Combine(sub, glass.Key + ".yml"), glass.Value, new UTF8Encoding(false));
            }
            File.WriteAllText(Path.Combine(folder, "README.txt"),
                OptilandGlass.ReadMe(Path.GetFileName(filePath), ymls.Keys));
            return folder;
        }

        private static string Fmt(double v)
        {
            return v.ToString("G14", CultureInfo.InvariantCulture);
        }

        private static string FmtInf(double v)
        {
            // Optiland's canonical JSON uses literal Infinity / -Infinity
            // tokens, which Python's json accepts (allow_nan=True, the default).
            // A finite sentinel like 1e30 also parses but is taken as a real
            // finite object distance.
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
