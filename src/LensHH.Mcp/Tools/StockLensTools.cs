using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using LensHH.Core.Enums;
using LensHH.Core.IO;
using LensHH.Core.Models;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Server;

namespace LensHH.Mcp.Tools
{
    /// <summary>
    /// Catalog DTO for find_matching_stock pool enumeration.
    /// </summary>
    internal sealed class StockLens
    {
        public string Vendor      { get; set; } = "";
        public string PartNumber  { get; set; } = "";
        public string Family      { get; set; } = "";
        public double Efl;
        public double Diameter;
        public double Nd;
        public double Vd;
    }


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

        // ── Tool 1b: find ranked replacement candidates (single or split pair) ─

        [McpServerTool, Description(
            "Find ranked stock-lens replacement candidates for an optimized element, with optional split-pair patterns. "
            + "Given an optimized free element's EFL, glass index, and semi-diameter, returns candidate stock substitutions ranked by closeness. "
            + "Pattern choices:\n"
            + "  • 'single'           – one stock singlet whose EFL is near targetEfl.\n"
            + "  • 'split_pcx'        – two plano-convex singlets, plano sides facing, combined power matches target (both POSITIVE only).\n"
            + "  • 'split_pcc'        – two plano-concave singlets, plano sides facing, combined power matches target (both NEGATIVE only).\n"
            + "  • 'split_pcx_pcc'    – one PCX + one PCC plano-to-plano (the Sasian trick). Combined power can be net positive or net negative. The two different glasses give chromatic trim a single catalog lens can't provide.\n\n"
            + "Diameter constraints: every candidate's diameter_mm must be >= 2 × targetSemiDiameter; if maxDiameter is given, also <= maxDiameter (filters out preposterous oversized parts like Ø162 mm singlets paired with a Ø47 design).\n\n"
            + "Aspherics: excludeAspherics defaults to TRUE. Aspheric singlets (Thorlabs AL/ACL series, Edmund 'Aspheric*' family) are tuned for single-wavelength laser focusing and behave poorly under polychromatic field-of-view loads. Set excludeAspherics=false if you specifically want them.\n\n"
            + "Ranking: combined-EFL closeness + half-weighted glass-distance from targetNd (if given) + (pair only) aperture-mismatch penalty (favors pairs whose two diameters are similar; punishes mixing Ø25 + Ø162). The mismatch term uses |log2(D_A/D_B)|.\n\n"
            + "Output: one line per candidate / pair, format depends on pattern.")]
        public string FindMatchingStock(
            double targetEfl,
            double targetSemiDiameter,
            string pattern = "single",
            double? targetNd = null,
            double eflTolerancePercent = 10,
            double? maxDiameter = null,
            bool excludeAspherics = true,
            int topN = 5)
        {
            var p = (pattern ?? "single").ToLowerInvariant().Replace("-", "_");
            if (p != "single" && p != "split_pcx" && p != "split_pcc" && p != "split_pcx_pcc")
                return $"FindMatchingStock error: unknown pattern '{pattern}'. Use one of: single, split_pcx, split_pcc, split_pcx_pcc.";
            if (targetSemiDiameter <= 0)
                return $"FindMatchingStock error: targetSemiDiameter must be positive (got {targetSemiDiameter}).";
            if (topN <= 0 || topN > 50) topN = 5;

            double minDiameter = 2.0 * targetSemiDiameter;
            if (maxDiameter.HasValue && maxDiameter.Value < minDiameter)
                return $"FindMatchingStock error: maxDiameter ({maxDiameter}) is smaller than 2 × targetSemiDiameter ({minDiameter}).";
            double tolFrac = Math.Max(0, eflTolerancePercent) / 100.0;

            // Pool the catalog once. We need every singlet at or above the required
            // diameter; pattern-specific shape filtering happens in-memory below.
            // maxDiameter (when given) caps the pool — typical use: 1.6×–2× the
            // target full diameter, so we keep reasonable headroom but reject the
            // industrial-size outliers (e.g. Ø162 mm in a 73 mm EFL imaging lens).
            string dbPath = StockLensCatalog.ResolveDbPath();
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();
            var pool = new List<StockLens>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT vendor, part_number, family, efl_mm, diameter_mm, nd_primary, vd_primary "
                    + "FROM stock_lenses "
                    + "WHERE import_status='ok' AND n_elements=1 "
                    + "AND diameter_mm >= @minD "
                    + (maxDiameter.HasValue ? "AND diameter_mm <= @maxD " : "")
                    + "AND efl_mm IS NOT NULL";
                cmd.Parameters.AddWithValue("@minD", minDiameter);
                if (maxDiameter.HasValue) cmd.Parameters.AddWithValue("@maxD", maxDiameter.Value);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    var family = rdr.IsDBNull(2) ? "" : rdr.GetString(2);

                    // Aspheric filter: drops Thorlabs AL/ACL family, Edmund
                    // "AsphericGlassPolished" / "AsphericPlastic". Polychromatic
                    // imaging optimization rarely benefits from a stock aspheric
                    // because its conic + 4th–8th-order coefficients are tuned
                    // for a specific wavelength/conjugate combination; using one
                    // here introduces a fixed aberration the optimizer can't
                    // correct via gaps.
                    if (excludeAspherics &&
                        family.Contains("Aspheric", StringComparison.OrdinalIgnoreCase))
                        continue;

                    pool.Add(new StockLens
                    {
                        Vendor      = rdr.IsDBNull(0) ? "" : rdr.GetString(0),
                        PartNumber  = rdr.IsDBNull(1) ? "" : rdr.GetString(1),
                        Family      = family,
                        Efl         = rdr.IsDBNull(3) ? 0  : rdr.GetDouble(3),
                        Diameter    = rdr.IsDBNull(4) ? 0  : rdr.GetDouble(4),
                        Nd          = rdr.IsDBNull(5) ? 0  : rdr.GetDouble(5),
                        Vd          = rdr.IsDBNull(6) ? 0  : rdr.GetDouble(6),
                    });
                }
            }
            if (pool.Count == 0)
                return $"FindMatchingStock: no stock singlets with diameter in "
                     + $"[{minDiameter:F2}{(maxDiameter.HasValue ? $", {maxDiameter.Value:F2}" : ", ∞")}] mm "
                     + (excludeAspherics ? "(aspherics excluded) " : "")
                     + "in catalog.";

            // Glass-distance scorer. Smaller is better. Zero when targetNd not given
            // — we don't want to bias rankings by an unknown.
            double NdDistance(double nd) => targetNd.HasValue ? Math.Abs(nd - targetNd.Value) : 0;

            var sb = new StringBuilder();

            if (p == "single")
            {
                // Filter to candidates whose EFL is within ±tol of target.
                double absT = Math.Abs(targetEfl);
                double minE = targetEfl - absT * tolFrac;
                double maxE = targetEfl + absT * tolFrac;
                if (maxE < minE) (minE, maxE) = (maxE, minE);

                var hits = pool
                    .Where(s => s.Efl >= minE && s.Efl <= maxE)
                    .OrderBy(s => Math.Abs(s.Efl - targetEfl) / absT + 5 * NdDistance(s.Nd))
                    .Take(topN)
                    .ToList();
                if (hits.Count == 0)
                    return $"FindMatchingStock(single): no candidates with EFL in [{minE:F2}, {maxE:F2}] mm and Ø >= {minDiameter:F2} mm. Try a larger eflTolerancePercent.";

                sb.AppendLine($"Found {hits.Count} 'single' candidate(s) for EFL={targetEfl:F2} mm, Ø>={minDiameter:F2} mm"
                    + (targetNd.HasValue ? $", nd≈{targetNd.Value:F3}" : "") + ":");
                int rank = 1;
                foreach (var h in hits)
                {
                    double dE = h.Efl - targetEfl;
                    sb.Append($"  #{rank++} {h.Vendor} {h.PartNumber} {h.Family} ");
                    sb.Append($"EFL={h.Efl:F2} (ΔEFL={dE:+0.00;-0.00}) ");
                    sb.Append($"D={h.Diameter:F2} nd={h.Nd:F3} vd={h.Vd:F2}");
                    if (targetNd.HasValue) sb.Append($" (Δnd={(h.Nd - targetNd.Value):+0.000;-0.000})");
                    sb.AppendLine();
                }
                return sb.ToString();
            }

            // ── Pair patterns ─────────────────────────────────────────────────
            // Partition the pool by lens shape. The split trick requires plano-
            // sided lenses (PCX or PCC); biconvex/biconcave can't be plano-to-
            // plano. We identify shapes by family-string convention:
            //   PCX  ⇐ family contains 'PCX' / 'PlanoConvex' / Thorlabs 'LA'
            //   PCC  ⇐ family contains 'PCC' / 'PlanoConcave' / Thorlabs LC/LD/LF
            // Sign of EFL is the second check: PCX must be positive, PCC negative.
            bool IsPcxFamily(string f) =>
                   f.Contains("PCX", StringComparison.OrdinalIgnoreCase)
                || f.Contains("PlanoConvex", StringComparison.OrdinalIgnoreCase)
                || f.Contains("ThorLabs/LA", StringComparison.OrdinalIgnoreCase);
            bool IsPccFamily(string f) =>
                   f.Contains("PCC", StringComparison.OrdinalIgnoreCase)
                || f.Contains("PlanoConcave", StringComparison.OrdinalIgnoreCase)
                || f.Contains("ThorLabs/LC", StringComparison.OrdinalIgnoreCase)
                || f.Contains("ThorLabs/LD", StringComparison.OrdinalIgnoreCase)
                || f.Contains("ThorLabs/LF", StringComparison.OrdinalIgnoreCase);

            var pcxPool = pool.Where(s => IsPcxFamily(s.Family) && s.Efl > 0).ToList();
            var pccPool = pool.Where(s => IsPccFamily(s.Family) && s.Efl < 0).ToList();

            // 1/T = 1/f1 + 1/f2  ⇒  f2 = T·f1 / (f1 − T)
            // Enumerate f1 over the appropriate pool, compute the f2 the math
            // demands, then look up the closest stock match in the f2 pool.
            // Score = |combined − target| / |target|  +  Δglass-distance.
            List<(StockLens a, StockLens b, double combined, double score)> pairs = new();
            double absTarget = Math.Abs(targetEfl);
            if (absTarget < 1e-6) return "FindMatchingStock: targetEfl too close to zero for pair patterns.";

            List<StockLens> poolA, poolB;
            if (p == "split_pcx")
            {
                if (targetEfl <= 0) return "FindMatchingStock(split_pcx): targetEfl must be POSITIVE for split-PCX (both lenses contribute positive power).";
                poolA = pcxPool; poolB = pcxPool;
            }
            else if (p == "split_pcc")
            {
                if (targetEfl >= 0) return "FindMatchingStock(split_pcc): targetEfl must be NEGATIVE for split-PCC (both lenses contribute negative power).";
                poolA = pccPool; poolB = pccPool;
            }
            else // split_pcx_pcc
            {
                // PCX (positive) + PCC (negative). Combined power can be either sign,
                // so no targetEfl sign constraint.
                poolA = pcxPool; poolB = pccPool;
            }
            if (poolA.Count == 0 || poolB.Count == 0)
                return $"FindMatchingStock({p}): pool empty (PCX:{pcxPool.Count}, PCC:{pccPool.Count}) at the required diameter. Lower targetSemiDiameter or relax pattern.";

            // For each f1, compute required f2 and find nearest in poolB.
            // Skip same-part pairs in split_pcx / split_pcc when the user might
            // not want a "pair" of the IDENTICAL lens — but actually two of the
            // same PCX is a valid (often-preferred) design choice. So we KEEP
            // those, just don't enumerate (a,b) and (b,a) twice for symmetric
            // pools. For asymmetric (split_pcx_pcc) the order is fixed by sign.
            bool symmetric = p == "split_pcx" || p == "split_pcc";

            foreach (var a in poolA)
            {
                if (Math.Abs(a.Efl - targetEfl) < 1e-9) continue; // degenerate
                double requiredB = targetEfl * a.Efl / (a.Efl - targetEfl);

                // Closest f2 candidate in poolB
                StockLens? best = null;
                double bestDiff = double.MaxValue;
                foreach (var b in poolB)
                {
                    // Skip ordering-duplicate pairs in symmetric pools
                    if (symmetric && string.Compare(a.PartNumber + a.Vendor, b.PartNumber + b.Vendor,
                        StringComparison.Ordinal) > 0) continue;
                    double diff = Math.Abs(b.Efl - requiredB);
                    if (diff < bestDiff) { bestDiff = diff; best = b; }
                }
                if (best == null) continue;

                // Combined power check (thin-lens, ignoring small gap)
                double combined = 1.0 / (1.0 / a.Efl + 1.0 / best.Efl);
                double err = Math.Abs(combined - targetEfl) / absTarget;
                if (err > tolFrac) continue;

                // Glass-distance score is the SUM across the pair so the ranking
                // favors well-matched glass when targetNd is given.
                double glassScore = NdDistance(a.Nd) + NdDistance(best.Nd);

                // Aperture-mismatch penalty: punish pairs whose two diameters
                // differ by more than ~2× (|log2| > 1). Catches the
                // Ø57+Ø162 mathematically-perfect pair from the v7 retry —
                // physically nonsensical even though the EFL math is exact.
                // Within a factor of √2 (|log2| < 0.5) the term is negligible;
                // beyond that it scales linearly with the log ratio.
                double apertureMismatch = (a.Diameter > 0 && best.Diameter > 0)
                    ? Math.Abs(Math.Log(a.Diameter / best.Diameter, 2))
                    : 0;

                pairs.Add((a, best, combined,
                    err + 0.5 * glassScore + 0.25 * apertureMismatch));
            }

            if (pairs.Count == 0)
                return $"FindMatchingStock({p}): no pairs whose combined EFL lands within ±{eflTolerancePercent}% of {targetEfl:F2}. Try larger eflTolerancePercent or a different pattern.";

            var ranked = pairs.OrderBy(t => t.score).Take(topN).ToList();
            sb.AppendLine($"Found {ranked.Count} '{p}' pair(s) for combined EFL={targetEfl:F2} mm, Ø>={minDiameter:F2} mm"
                + (targetNd.HasValue ? $", nd≈{targetNd.Value:F3}" : "") + ":");
            int prank = 1;
            foreach (var (a, b, comb, _) in ranked)
            {
                sb.Append($"  #{prank++}  combined EFL={comb:F2} (Δ={(comb - targetEfl):+0.00;-0.00})");
                sb.AppendLine();
                sb.Append($"        A: {a.Vendor} {a.PartNumber} {a.Family} EFL={a.Efl:F2} D={a.Diameter:F2} nd={a.Nd:F3}");
                sb.AppendLine();
                sb.Append($"        B: {b.Vendor} {b.PartNumber} {b.Family} EFL={b.Efl:F2} D={b.Diameter:F2} nd={b.Nd:F3}");
                sb.AppendLine();
            }
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

        // ── Tool 2b: replace one element with stock part(s) ────────────────────

        [McpServerTool, Description(
            "Replace one element in the current optical system with stock part(s), surgically: identifies the chosen element's surfaces, removes them, splices in the new part(s), and reconnects air gaps + variables. Replaces the 6–10 micro-tool sequence (remove_surface ×N, insert_stock_lens ×M, edit_surface for trailing thickness, set_variable) with a single call.\n\n"
            + "elementIndex (1-based): which lens element in the stack to replace. Elements are counted as contiguous glass surfaces (a doublet counts as ONE element). Element 1 is the first lens after the stop.\n\n"
            + "replacementParts is a comma-separated list of part numbers, each optionally followed by ':rev' to flip orientation. Examples:\n"
            + "  • 'L-PCX040'                         – single stock singlet, default orientation\n"
            + "  • 'L-AOC203:rev'                     – single stock doublet, reversed\n"
            + "  • 'L-PCX333,L-PCX412:rev'            – split-pair (positive PCX-PCX, plano sides facing)\n"
            + "  • '45-145,48-316:rev'                – Sasian PCX+PCC pair (BK7 PCX + FS PCC, plano-to-plano)\n\n"
            + "airThicknessAfter (default 5 mm): trailing air gap to the next existing element / image. Marked variable. Use a larger seed when the replacement is much smaller-power than the original (gives the optimizer room).\n\n"
            + "splitGapMm (default 0.5 mm): for multi-part replacements, the air gap between the new parts. Marked variable. Use a small value for plano-to-plano contact pairs (the Sasian trick), or a larger value if you intend them as two separate elements.\n\n"
            + "Preserves the air gap BEFORE the element (the trailing thickness on the surface BEFORE the element's first surface is untouched — that gap defines where the lens stack starts and the agent typically wants to keep it).")]
        public string ReplaceElement(
            int elementIndex,
            string replacementParts,
            double airThicknessAfter = 5.0,
            double splitGapMm = 0.5)
        {
            var sys = _session.System;
            if (sys == null || sys.Surfaces.Count < 3)
                return "ReplaceElement error: no host system loaded (need at least OBJ + STOP + IMG).";
            if (string.IsNullOrWhiteSpace(replacementParts))
                return "ReplaceElement error: replacementParts is required (e.g. 'L-PCX040' or 'L-PCX333,L-PCX412:rev').";
            if (elementIndex < 1)
                return $"ReplaceElement error: elementIndex must be >= 1 (got {elementIndex}; element 1 is the first lens after the stop).";

            // ── Identify all elements ─────────────────────────────────────────
            // An element is a contiguous run of glass surfaces (consecutive
            // surfaces with non-empty Material). Air surface that immediately
            // follows ends the element's footprint; the element occupies
            // [first_glass, last_glass+1] where last_glass+1 is that trailing-
            // air surface (we'll remove it too on replacement so the new part's
            // trailing air thickness can take its place).
            var elements = new List<(int firstSurface, int lastSurface)>();
            int? glassStart = null;
            for (int i = 0; i < sys.Surfaces.Count; i++)
            {
                bool hasGlass = !string.IsNullOrEmpty(sys.Surfaces[i].Material);
                if (hasGlass)
                {
                    if (glassStart == null) glassStart = i;
                }
                else if (glassStart != null)
                {
                    // Surface i is the air-back of the element that started at glassStart.
                    elements.Add((glassStart.Value, i));
                    glassStart = null;
                }
            }
            if (elements.Count == 0)
                return "ReplaceElement error: no glass elements found in the current system.";
            if (elementIndex > elements.Count)
                return $"ReplaceElement error: elementIndex {elementIndex} out of range (system has {elements.Count} element(s)).";

            var (firstSurface, lastSurface) = elements[elementIndex - 1];
            int countToRemove = lastSurface - firstSurface + 1;
            int afterSurface = firstSurface - 1;  // we'll insert AFTER this surface

            // ── Parse the comma-separated replacement spec ────────────────────
            // Each token is 'partNumber' or 'partNumber:rev' / 'partNumber:fwd'.
            // Vendor disambiguation isn't supported here — when needed the agent
            // can call insert_stock_lens with the explicit vendor and skip this
            // helper. (Catalog has very few part-number collisions across
            // vendors in practice.)
            var tokens = replacementParts.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var parts = new List<(string part, bool reversed)>();
            foreach (var rawT in tokens)
            {
                var t = rawT.Trim();
                if (t.Length == 0) continue;
                bool rev = false;
                int colon = t.IndexOf(':');
                string pn = t;
                if (colon > 0)
                {
                    pn = t.Substring(0, colon).Trim();
                    string suffix = t.Substring(colon + 1).Trim().ToLowerInvariant();
                    if (suffix == "rev" || suffix == "reversed") rev = true;
                    else if (suffix == "fwd" || suffix == "forward" || suffix == "") rev = false;
                    else return $"ReplaceElement error: unknown orientation suffix ':{suffix}' for '{pn}'. Use ':rev' or omit.";
                }
                parts.Add((pn, rev));
            }
            if (parts.Count == 0)
                return "ReplaceElement error: replacementParts parsed to zero parts.";

            // ── Resolve every part up-front so we fail fast on a typo ─────────
            // (vs ripping out the original element and THEN discovering the
            // replacement doesn't exist).
            var resolvedParts = new List<(string vendor, List<Surface> vertices, bool reversed, string part)>();
            foreach (var (part, rev) in parts)
            {
                try
                {
                    var (vendor, lhltRel) = StockLensCatalog.ResolvePart(part, null);
                    string lhltPath = StockLensCatalog.ResolveLhltPath(lhltRel);
                    var stockSys = LhltReader.Read(lhltPath).System;
                    var verts = LensInsertHelpers.ExtractLensVertices(stockSys, out string? extractErr);
                    if (verts == null || verts.Count == 0)
                        return $"ReplaceElement error: stock lens '{part}' returned no vertices ({extractErr ?? "empty"}).";
                    if (rev) verts = LensInsertHelpers.ReverseVertexGroup(verts);
                    resolvedParts.Add((vendor, verts, rev, part));
                }
                catch (Exception ex)
                {
                    return $"ReplaceElement error: could not resolve '{part}': {ex.Message}";
                }
            }

            // ── Remove the old element's surfaces ─────────────────────────────
            // Remove from highest index downward so the indices stay stable
            // during the loop. Each removal fires SurfaceIndexUpdater to keep
            // merit-operand spans, pickups, and glass-subs in sync.
            for (int idx = lastSurface; idx >= firstSurface; idx--)
            {
                sys.Surfaces.RemoveAt(idx);
                LensHH.Core.Models.SurfaceIndexUpdater.OnSurfaceRemoved(
                    idx, sys, _session.MeritFunction, _session.ConfigEditor);
            }

            // ── Splice in the replacement parts sequentially ──────────────────
            int insertAt = afterSurface + 1;
            int firstInserted = insertAt;
            var insertedRanges = new List<(string label, int first, int last)>();
            for (int pIdx = 0; pIdx < resolvedParts.Count; pIdx++)
            {
                var (vendor, vertices, rev, part) = resolvedParts[pIdx];
                int partFirst = insertAt;
                for (int v = 0; v < vertices.Count; v++)
                {
                    sys.Surfaces.Insert(insertAt + v, vertices[v]);
                    LensHH.Core.Models.SurfaceIndexUpdater.OnSurfaceInserted(
                        insertAt + v, sys, _session.MeritFunction, _session.ConfigEditor);
                }
                int partLast = insertAt + vertices.Count - 1;
                insertedRanges.Add(($"{vendor}/{part}{(rev ? "(rev)" : "")}", partFirst, partLast));

                // Set the trailing air thickness of THIS part:
                //   – If another part follows: use splitGapMm, mark variable
                //     (the optimizer needs room to balance the split pair).
                //   – If this is the LAST part: use airThicknessAfter, variable
                //     (the trailing air gap to the next existing element / IMG).
                bool morePartsAfter = (pIdx + 1) < resolvedParts.Count;
                double trailingT = morePartsAfter ? splitGapMm : airThicknessAfter;
                sys.Surfaces[partLast].Thickness = trailingT;
                sys.Surfaces[partLast].ThicknessVariable = true;

                insertAt = partLast + 1;
            }

            // Reindex
            for (int i = 0; i < sys.Surfaces.Count; i++) sys.Surfaces[i].Index = i;

            int totalInserted = insertAt - firstInserted;
            var sb = new StringBuilder();
            sb.AppendLine($"ReplaceElement: removed element #{elementIndex} (surfaces [{firstSurface}-{lastSurface}], {countToRemove} surfaces). "
                + $"Inserted {resolvedParts.Count} part(s), {totalInserted} surfaces total.");
            foreach (var (label, first, last) in insertedRanges)
                sb.AppendLine($"  {label}: surfaces [{first}-{last}]");
            sb.AppendLine($"Trailing air thickness = {airThicknessAfter} mm (variable). "
                + (resolvedParts.Count > 1 ? $"Inter-part air gap = {splitGapMm} mm (variable)." : ""));
            sb.Append($"System now has {sys.Surfaces.Count} surfaces.");
            return sb.ToString();
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
