using System;
using System.Collections.Generic;
using LensHH.Core.Glass;

namespace LensHH.App.GlassCatalog;

/// <summary>
/// Resolves a raw material string from an imported lens file (ZMX,
/// SEQ, OTX, etc.) against the loaded glass catalogs. Some legacy
/// exporters write the glass slot as a numeric "code" (e.g.
/// <c>620.364</c> = nd-1 × 1000 . V × 10 → nd 1.620, V 36.4) instead
/// of a real catalog name. The format varies between files but is
/// determinable from the value itself: try every plausible
/// interpretation, drop the unphysical ones, then pick the catalog
/// glass with the smallest distance to the surviving (nd, V)
/// candidates.
/// </summary>
public static class GlassNumericResolver
{
    public record Substitution(int SurfaceIndex, string Original, string Replacement, double Nd, double Vd, double DeltaNd, double DeltaVd);

    /// <summary>
    /// If <paramref name="rawName"/> is a numeric code that doesn't
    /// resolve directly against a loaded catalog, find the nearest
    /// catalog glass. Returns null if the name resolves directly, if
    /// it isn't numeric, or if no catalog is loaded.
    /// </summary>
    public static Substitution? TryResolve(
        int surfaceIndex,
        string rawName,
        GlassCatalogManager catalogs,
        IList<string>? preferredCatalogs)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return null;
        // Already resolves? Nothing to do.
        if (catalogs.GetGlass(rawName, preferredCatalogs?.Count > 0 ? preferredCatalogs : null) != null)
            return null;

        var candidates = ParseNumericCandidates(rawName);
        if (candidates.Count == 0) return null;

        string? bestName = null;
        double bestNd = 0, bestVd = 0;
        double bestUsedNd = 0, bestUsedVd = 0;
        double bestScore = double.MaxValue;

        // Search every loaded catalog. Treat preferred (if supplied)
        // as a lookup hint — we still consider all glasses, but bias
        // marginally toward preferred-catalog matches when scores tie.
        foreach (var catalogName in catalogs.LoadedCatalogs)
        {
            foreach (var g in catalogs.GetGlassesInCatalog(catalogName))
            {
                if (g.Status > 1) continue; // ignore obsolete / special / melt entries
                if (g.Nd <= 0 || g.Vd <= 0) continue;

                foreach (var (nd, vd) in candidates)
                {
                    double dn = (g.Nd - nd) / 0.001;       // Δnd weighted at 0.001 per unit
                    double dv = (g.Vd - vd) / 0.1;         // ΔV weighted at 0.1 per unit
                    double score = dn * dn + dv * dv;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestName  = g.Name;
                        bestNd    = g.Nd;
                        bestVd    = g.Vd;
                        bestUsedNd = nd;
                        bestUsedVd = vd;
                    }
                }
            }
        }

        if (bestName == null) return null;

        return new Substitution(
            SurfaceIndex: surfaceIndex,
            Original: rawName,
            Replacement: bestName,
            Nd: bestNd,
            Vd: bestVd,
            DeltaNd: bestNd - bestUsedNd,
            DeltaVd: bestVd - bestUsedVd);
    }

    /// <summary>
    /// Enumerate plausible (nd, V) interpretations of a single
    /// numeric token. Format isn't fixed across exporters:
    /// <list type="bullet">
    /// <item><description>Schott 6-digit code style — <c>620.364</c> = (nd-1)·1000 . V·10 → nd 1.620, V 36.4</description></item>
    /// <item><description>nd·1000 packed — <c>1620.364</c> → nd 1.620, V 36.4</description></item>
    /// <item><description>nd·100 packed — <c>162.004</c> → nd 1.62004, V 0.4 (unphysical, dropped)</description></item>
    /// <item><description>Direct nd literal — <c>1.62004</c> → nd 1.62004, V unknown (uses nd-only score)</description></item>
    /// </list>
    /// All interpretations are generated; the unphysical ones (nd
    /// outside [1.40, 2.10] or V outside [10, 110]) are filtered.
    /// The catalog match arbitrates which interpretation was correct.
    /// </summary>
    private static List<(double nd, double vd)> ParseNumericCandidates(string raw)
    {
        var result = new List<(double nd, double vd)>();
        raw = raw.Trim();

        // Must start with a digit and parse cleanly. If a glass name
        // starts with a letter or contains anything other than digits
        // and a decimal point, this is not a numeric code.
        if (raw.Length == 0 || !(char.IsDigit(raw[0]) || raw[0] == '-')) return result;

        int dot = raw.IndexOf('.');
        string intStr = dot >= 0 ? raw.Substring(0, dot) : raw;
        string fracStr = dot >= 0 ? raw.Substring(dot + 1) : "";

        if (!int.TryParse(intStr, out int intVal)) return result;
        int fracVal = 0;
        int fracLen = fracStr.Length;
        if (fracLen > 0 && !int.TryParse(fracStr, out fracVal)) return result;

        // Decode V from the fractional digit string. The packing is
        // typically V·10 for the standard Schott 6-digit code (3 digits
        // → V=36.4 from "364") with extra digits adding precision. The
        // single divider 10^(fracLen-2) handles V·10, V·100, V·1000…
        // uniformly: "364" → 36.4, "3640" → 36.40, "6030" → 60.30.
        double vd = (fracLen >= 2) ? fracVal / Math.Pow(10, fracLen - 2) : double.NaN;

        // Each plausible nd interpretation paired with the V above.
        // Filtering happens in TryAdd (drops nd outside [1.40, 2.10]
        // and V outside [10, 110]). The catalog match arbitrates
        // which interpretation was correct.
        TryAdd(result, 1.0 + intVal / 1000.0,  vd);   // (nd-1)·1000  — Schott 6-digit code "XXX.XXX"
        TryAdd(result, intVal / 1000.0,        vd);   // nd·1000      — packed 4-digit "XXXX.XXX"
        TryAdd(result, 1.0 + intVal / 10000.0, vd);   // (nd-1)·10000 — OpTaliX-export 4-digit "XXXX.XXXX"
        TryAdd(result, intVal / 100.0,         vd);   // nd·100       — rare alt 3-digit

        // Direct literal nd (e.g. "1.62004") when no scaled
        // interpretation survives. V isn't encoded; use the median
        // optical-glass V (~50) as a soft anchor so the match
        // prefers glasses with closer nd.
        if (result.Count == 0)
        {
            double literal = intVal + (fracLen > 0 ? fracVal / Math.Pow(10, fracLen) : 0);
            if (literal >= 1.40 && literal <= 2.10)
                result.Add((literal, 50.0));
        }

        return result;
    }

    private static void TryAdd(List<(double nd, double vd)> list, double nd, double vd)
    {
        if (double.IsNaN(nd) || double.IsNaN(vd)) return;
        if (nd < 1.40 || nd > 2.10) return;
        if (vd < 10 || vd > 110) return;
        list.Add((nd, vd));
    }
}
