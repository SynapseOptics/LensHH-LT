using System;
using System.Collections.Generic;
using System.IO;
using LensHH.Core.Enums;
using LensHH.Core.Models;
using Microsoft.Data.Sqlite;

namespace LensHH.Mcp
{
    /// <summary>
    /// Helpers shared by StockLensTools and BatchDesignSearchService:
    /// catalog path resolution, .lhlt -> lens-vertex extraction, vertex
    /// reversal, and deep-clone of a Surface. Pure utilities, no session
    /// state. Keep the StockLensTools and BatchDesignSearchService logic
    /// using these instead of duplicating.
    /// </summary>
    public static class StockLensInsertHelpers
    {
        // ── Catalog file discovery ─────────────────────────────────────────────

        public static string ResolveDbPath()
        {
            var env = Environment.GetEnvironmentVariable("LENSHH_CATALOGS_DIR");
            if (!string.IsNullOrWhiteSpace(env))
            {
                var p = Path.Combine(env, "stock-lens-catalog.sqlite");
                if (File.Exists(p)) return p;
            }
            foreach (var rel in new[] {
                Path.Combine("..", "catalogs", "stock-lens-catalog.sqlite"),
                Path.Combine("catalogs", "stock-lens-catalog.sqlite"),
                Path.Combine("..", "..", "..", "..", "..", "catalogs", "stock-lens-catalog.sqlite"),
            })
            {
                var p = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, rel));
                if (File.Exists(p)) return p;
            }
            string baseDir = AppContext.BaseDirectory.Replace('\\', '/');
            if (baseDir.Contains("/TEST_INSTALL/"))
            {
                string mirror = baseDir.Replace("/TEST_INSTALL/", "/SynapseLensHH-LT/");
                var dir = new DirectoryInfo(mirror);
                while (dir != null)
                {
                    var c = Path.Combine(dir.FullName, "catalogs", "stock-lens-catalog.sqlite");
                    if (File.Exists(c)) return c;
                    dir = dir.Parent;
                }
            }
            throw new FileNotFoundException(
                "stock-lens-catalog.sqlite not found. Searched relative to "
                + AppContext.BaseDirectory + "; set LENSHH_CATALOGS_DIR env var as a fallback.");
        }

        public static string ResolveLhltPath(string lhltRelPath)
        {
            string dbPath = ResolveDbPath();
            string root = Path.GetDirectoryName(dbPath)!;
            string full = Path.GetFullPath(Path.Combine(root, "Lenses", lhltRelPath));
            if (!File.Exists(full))
                throw new FileNotFoundException($"Stock-lens .lhlt not found: {full}");
            return full;
        }

        /// <summary>
        /// Resolve a part_number (with optional vendor) to (vendor, lhltRelPath).
        /// Throws if not found.
        /// </summary>
        public static (string vendor, string lhltRelPath) ResolvePart(string partNumber, string? vendor)
        {
            using var conn = new SqliteConnection($"Data Source={ResolveDbPath()};Mode=ReadOnly");
            conn.Open();
            using var cmd = conn.CreateCommand();
            if (vendor != null)
            {
                cmd.CommandText = "SELECT vendor, lhlt_relpath FROM stock_lenses WHERE vendor=@v AND part_number=@p LIMIT 1;";
                cmd.Parameters.AddWithValue("@v", vendor);
            }
            else
            {
                cmd.CommandText = "SELECT vendor, lhlt_relpath FROM stock_lenses WHERE part_number=@p LIMIT 1;";
            }
            cmd.Parameters.AddWithValue("@p", partNumber);
            using var rdr = cmd.ExecuteReader();
            if (!rdr.Read())
                throw new InvalidOperationException(
                    $"Stock lens not found: part_number='{partNumber}'{(vendor != null ? $", vendor='{vendor}'" : "")}.");
            string v = rdr.GetString(0);
            if (rdr.IsDBNull(1))
                throw new InvalidOperationException($"Stock lens '{partNumber}' has no .lhlt file recorded.");
            return (v, rdr.GetString(1));
        }

        // ── Vertex extraction / reversal / clone ──────────────────────────────

        /// <summary>
        /// Pull just the optical-vertex surfaces from a stock-lens .lhlt:
        /// skip OBJ (index 0), the dummy aperture stop (next IsStop surface),
        /// and IMG (last surface). The remaining surfaces are the lens body.
        /// Returns null on shape mismatch.
        /// </summary>
        public static List<Surface>? ExtractLensVertices(OpticalSystem system, out string? error)
        {
            error = null;
            int n = system.Surfaces.Count;
            if (n < 4) { error = $"system has only {n} surface(s); need at least 4"; return null; }

            int stopIdx = -1;
            for (int i = 1; i < n - 1; i++)
            {
                if (system.Surfaces[i].IsStop) { stopIdx = i; break; }
            }
            if (stopIdx < 0) { error = "no stop surface; cannot identify the lens range"; return null; }

            var verts = new List<Surface>();
            for (int i = stopIdx + 1; i < n - 1; i++)
                verts.Add(CloneSurface(system.Surfaces[i]));
            foreach (var s in verts) s.IsStop = false;
            return verts;
        }

        /// <summary>
        /// Reverse a lens group in place: radii negate, surface order mirrors,
        /// internal thicknesses + materials reshuffle. Trailing thickness of
        /// the final surface is preserved (the host-system air gap).
        /// </summary>
        public static List<Surface> ReverseVertexGroup(List<Surface> vertices)
        {
            int n = vertices.Count;
            var reversed = new List<Surface>(n);
            for (int i = 0; i < n; i++)
            {
                var src = CloneSurface(vertices[n - 1 - i]);
                src.Radius = -vertices[n - 1 - i].Radius;
                if (i < n - 1)
                {
                    src.Thickness = vertices[n - 2 - i].Thickness;
                    src.Material  = vertices[n - 2 - i].Material;
                }
                else
                {
                    src.Thickness = vertices[n - 1].Thickness;
                    src.Material  = vertices[n - 1].Material;
                }
                reversed.Add(src);
            }
            return reversed;
        }

        public static Surface CloneSurface(Surface s)
        {
            return new Surface
            {
                Index                  = s.Index,
                Type                   = s.Type,
                Comment                = s.Comment,
                Radius                 = s.Radius,
                Thickness              = s.Thickness,
                Material               = s.Material,
                SemiDiameter           = s.SemiDiameter,
                SemiDiameterMode       = s.SemiDiameterMode,
                ClearAperturePercent   = s.ClearAperturePercent,
                Conic                  = s.Conic,
                AsphericCoefficients   = s.AsphericCoefficients != null ? (double[])s.AsphericCoefficients.Clone() : new double[8],
                CurvatureVariable      = s.CurvatureVariable,
                ThicknessVariable      = s.ThicknessVariable,
                ConicVariable          = s.ConicVariable,
                AsphericVariable       = s.AsphericVariable != null ? (bool[])s.AsphericVariable.Clone() : new bool[8],
                HasMarginalRaySolve    = s.HasMarginalRaySolve,
                IsStop                 = s.IsStop,
                InnerRadius            = s.InnerRadius,
                ClapOuterRadius        = s.ClapOuterRadius,
                ObscurationRadius      = s.ObscurationRadius,
                FloatingApertureRadius = s.FloatingApertureRadius,
            };
        }
    }
}
