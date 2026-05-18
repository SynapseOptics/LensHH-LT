using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using LensHH.Core.Enums;
using LensHH.Core.IO;
using LensHH.Core.Models;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Server;

namespace LensHH.Mcp.Tools
{
    /// <summary>
    /// MCP tools for working with the bundled stock-lens catalog
    /// (catalogs/stock-lens-catalog.sqlite + per-lens .lhlt files).
    /// Lets an AI agent search for candidate stock lenses by paraxial
    /// properties, splice one into the current host system at a chosen
    /// surface, and reverse an inserted lens in place.
    /// </summary>
    [McpServerToolType]
    public class StockLensTools
    {
        private readonly McpSession _session;
        public StockLensTools(McpSession session) => _session = session;

        // Catalog path resolution + vertex helpers live in StockLensInsertHelpers
        // (shared with BatchDesignSearchService).

        // ── Tool 1: search ─────────────────────────────────────────────────────

        [McpServerTool, Description(
            "Search the bundled stock-lens catalog (Edmund + Thorlabs, 6000+ parts) for candidates that fit "
            + "a design slot. All filters are optional; only the provided ones are applied. "
            + "Returns one result per line: '<vendor> <part_number> <family> EFL=<mm> f/<fnum> D=<mm> "
            + "nd=<value> n_elem=<count> | <description>'. The part_number can be passed straight to insert_stock_lens. "
            + "Useful for design tasks like 'find an N-BK7/N-SF5 achromat doublet, EFL 45-55 mm, diameter <= 25.4 mm'.")]
        public string SearchStockLenses(
            double? eflMin = null,
            double? eflMax = null,
            double? diameterMin = null,
            double? diameterMax = null,
            double? fnumMin = null,
            double? fnumMax = null,
            int? nElements = null,
            string? vendor = null,
            string? familyLike = null,
            double? ndPrimaryMin = null,
            double? ndPrimaryMax = null,
            double? vdPrimaryMin = null,
            double? vdPrimaryMax = null,
            int limit = 20)
        {
            string dbPath = StockLensCatalog.ResolveDbPath();
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();

            var where = new List<string> { "import_status = 'ok'" };
            var args = new List<(string name, object value)>();

            void Add(string clause, string name, object value) { where.Add(clause); args.Add((name, value)); }
            if (eflMin.HasValue)         Add("efl_mm         >= @eflMin",       "@eflMin",       eflMin.Value);
            if (eflMax.HasValue)         Add("efl_mm         <= @eflMax",       "@eflMax",       eflMax.Value);
            if (diameterMin.HasValue)    Add("diameter_mm    >= @diaMin",       "@diaMin",       diameterMin.Value);
            if (diameterMax.HasValue)    Add("diameter_mm    <= @diaMax",       "@diaMax",       diameterMax.Value);
            if (fnumMin.HasValue)        Add("fnum           >= @fnMin",        "@fnMin",        fnumMin.Value);
            if (fnumMax.HasValue)        Add("fnum           <= @fnMax",        "@fnMax",        fnumMax.Value);
            if (nElements.HasValue)      Add("n_elements      = @nel",          "@nel",          nElements.Value);
            if (!string.IsNullOrWhiteSpace(vendor)) Add("vendor = @vend",       "@vend",         vendor!);
            if (!string.IsNullOrWhiteSpace(familyLike)) Add("family LIKE @fam", "@fam",          familyLike!);
            if (ndPrimaryMin.HasValue)   Add("nd_primary     >= @ndMin",        "@ndMin",        ndPrimaryMin.Value);
            if (ndPrimaryMax.HasValue)   Add("nd_primary     <= @ndMax",        "@ndMax",        ndPrimaryMax.Value);
            if (vdPrimaryMin.HasValue)   Add("vd_primary     >= @vdMin",        "@vdMin",        vdPrimaryMin.Value);
            if (vdPrimaryMax.HasValue)   Add("vd_primary     <= @vdMax",        "@vdMax",        vdPrimaryMax.Value);

            if (limit <= 0 || limit > 200) limit = 20;

            string sql =
                "SELECT vendor, part_number, family, efl_mm, fnum, diameter_mm, "
                + "       nd_primary, n_elements, description "
                + "FROM stock_lenses "
                + (where.Count > 0 ? ("WHERE " + string.Join(" AND ", where) + " ") : "")
                + "ORDER BY ABS(COALESCE(efl_mm,0) - @sortRef), part_number "
                + "LIMIT @lim;";

            // Sort reference: midpoint of requested EFL range if both ends given, else 0 (no sort preference).
            double sortRef = (eflMin ?? 0) + (((eflMax ?? eflMin) ?? 0) - (eflMin ?? 0)) * 0.5;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
            cmd.Parameters.AddWithValue("@sortRef", sortRef);
            cmd.Parameters.AddWithValue("@lim", limit);

            var sb = new StringBuilder();
            int count = 0;
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                count++;
                string V(int i) => rdr.IsDBNull(i) ? "?" : rdr.GetValue(i)?.ToString() ?? "?";
                string F(int i, string fmt) => rdr.IsDBNull(i) ? "?" : rdr.GetDouble(i).ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
                sb.Append(V(0)).Append(' ').Append(V(1)).Append(' ').Append(V(2));
                sb.Append(" EFL=").Append(F(3, "0.###"));
                sb.Append(" f/").Append(F(4, "0.##"));
                sb.Append(" D=").Append(F(5, "0.##"));
                sb.Append(" nd=").Append(F(6, "0.###"));
                sb.Append(" n_elem=").Append(V(7));
                sb.Append(" | ").Append(V(8));
                sb.Append('\n');
            }
            if (count == 0) return "No stock lenses match the given filters.";
            sb.Insert(0, $"Found {count} stock lens(es) (limit {limit}):\n");
            return sb.ToString();
        }

        // ── Tool 2: insert ─────────────────────────────────────────────────────

        [McpServerTool, Description(
            "Splice a stock lens (by part_number) into the CURRENT optical system after the given surface. "
            + "Host system's wavelengths, fields, aperture, ray-aiming, and merit function are preserved "
            + "(merit-operand surface refs are bumped automatically). The stock lens's OBJ, dummy aperture "
            + "stop, and IMG surfaces are stripped; only the optical vertices are inserted. The surface that "
            + "was at afterSurface keeps its thickness (the air gap before the lens); the last inserted "
            + "surface gets thickness 0 (touches whatever was at afterSurface+1). If reversed=true, the lens "
            + "is flipped 180 degrees (radii negated, surface order reversed, internal thicknesses and "
            + "materials reordered). If vendor is omitted, the first matching part_number wins.")]
        public string InsertStockLens(string partNumber, int afterSurface, bool reversed = false, string? vendor = null)
        {
            var sys = _session.System;
            if (sys == null || sys.Surfaces.Count < 2)
                return "No host system loaded. Create or load a system before inserting a stock lens.";
            if (afterSurface < 0 || afterSurface >= sys.Surfaces.Count)
                return $"afterSurface {afterSurface} out of range (0 to {sys.Surfaces.Count - 1}).";

            // 1. Resolve part number → lhlt path via SQLite.
            string lhltRel; string resolvedVendor;
            try { (resolvedVendor, lhltRel) = StockLensCatalog.ResolvePart(partNumber, vendor); }
            catch (Exception ex) { return ex.Message; }
            string lhltPath = StockLensCatalog.ResolveLhltPath(lhltRel);

            // 2. Load the stock-lens .lhlt and extract just its lens vertices.
            var stockResult = LhltReader.Read(lhltPath);
            var vertices = LensInsertHelpers.ExtractLensVertices(stockResult.System, out string? extractError);
            if (vertices == null) return $"Failed to extract lens vertices from {lhltRel}: {extractError}";
            if (vertices.Count == 0) return $"Stock lens {partNumber} contains no optical vertices.";

            // 3. Optionally reverse.
            if (reversed) vertices = LensInsertHelpers.ReverseVertexGroup(vertices);

            // The last inserted vertex has its trailing thickness zeroed (per design:
            // it touches the next existing surface; user adjusts later).
            vertices[vertices.Count - 1].Thickness = 0.0;

            // 4. Splice one-by-one, calling SurfaceIndexUpdater after each insert
            //    so merit-operand, pickup, config-editor, and glass-substitution
            //    surface refs all stay in sync.
            int insertAt = afterSurface + 1;
            var (eflBefore, fnBefore) = TryGetEflFnum(sys);
            int firstInserted = insertAt;
            int lastInserted  = insertAt + vertices.Count - 1;
            for (int i = 0; i < vertices.Count; i++)
            {
                sys.Surfaces.Insert(insertAt + i, vertices[i]);
                LensHH.Core.Models.SurfaceIndexUpdater.OnSurfaceInserted(
                    insertAt + i, sys, _session.MeritFunction, _session.ConfigEditor);
            }
            // Re-index
            for (int i = 0; i < sys.Surfaces.Count; i++) sys.Surfaces[i].Index = i;

            var (eflAfter, fnAfter) = TryGetEflFnum(sys);

            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Inserted {0} {1}{2} at surfaces [{3}-{4}] ({5} vertices). EFL {6:0.###} -> {7:0.###} mm; F/# {8:0.##} -> {9:0.##}.",
                resolvedVendor, partNumber, reversed ? " (reversed)" : "",
                firstInserted, lastInserted, vertices.Count,
                eflBefore, eflAfter, fnBefore, fnAfter);
        }

        // ── Tool 3: reverse ────────────────────────────────────────────────────

        [McpServerTool, Description(
            "Reverse a lens group in place: flips count surfaces starting at startSurface around the lens "
            + "center. Radii are negated and the surface order is reversed; internal thicknesses and "
            + "materials are reshuffled accordingly (the trailing air gap on the last surface of the group "
            + "is preserved, since it represents the host-system spacing and doesn't move when the lens "
            + "flips). Conic and even-asphere coefficients are sign-invariant under flip. Merit function "
            + "and pickup surface indices are untouched (surface count doesn't change). Typical use: after "
            + "insert_stock_lens, try the other orientation for a meniscus or plano-X singlet.")]
        public string ReverseLens(int startSurface, int count)
        {
            var sys = _session.System;
            if (sys == null) return "No system loaded.";
            if (count < 2) return "ReverseLens needs count >= 2 (a single surface has nothing to reverse).";
            if (startSurface < 0 || startSurface + count > sys.Surfaces.Count)
                return $"Range [{startSurface}-{startSurface + count - 1}] out of system bounds (0 to {sys.Surfaces.Count - 1}).";

            var (eflBefore, fnBefore) = TryGetEflFnum(sys);

            // Snapshot the original group, then write a mirrored copy back.
            var original = new List<Surface>(count);
            for (int i = 0; i < count; i++) original.Add(LensInsertHelpers.CloneSurface(sys.Surfaces[startSurface + i]));

            for (int i = 0; i < count; i++)
            {
                var newSurf = LensInsertHelpers.CloneSurface(original[count - 1 - i]);
                newSurf.Radius = -original[count - 1 - i].Radius; // sign of infinity stays infinity

                // Thickness + material reorder: gap *after* surface i comes from
                // gap after old surface (count-2-i), EXCEPT the final surface,
                // which keeps the host-system trailing gap unchanged.
                if (i < count - 1)
                {
                    newSurf.Thickness = original[count - 2 - i].Thickness;
                    newSurf.Material  = original[count - 2 - i].Material;
                }
                else
                {
                    newSurf.Thickness = original[count - 1].Thickness;
                    newSurf.Material  = original[count - 1].Material;
                }
                // Stop flag stays with the physical position (whatever was the stop in this
                // range stays the stop after flip — the dummy stop is OUTSIDE this range anyway).
                newSurf.IsStop = original[count - 1 - i].IsStop;

                sys.Surfaces[startSurface + i] = newSurf;
            }
            for (int i = 0; i < sys.Surfaces.Count; i++) sys.Surfaces[i].Index = i;

            var (eflAfter, fnAfter) = TryGetEflFnum(sys);

            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Reversed surfaces [{0}-{1}] ({2} surfaces). EFL {3:0.###} -> {4:0.###} mm; F/# {5:0.##} -> {6:0.##}.",
                startSurface, startSurface + count - 1, count, eflBefore, eflAfter, fnBefore, fnAfter);
        }

        private (double efl, double fnum) TryGetEflFnum(OpticalSystem system)
        {
            try
            {
                var r = LensHH.Core.Analysis.SystemDataCalculator.Calculate(system, _session.GlassCatalog);
                return (r.Efl, r.WorkingFNumber);
            }
            catch { return (double.NaN, double.NaN); }
        }
    }
}
