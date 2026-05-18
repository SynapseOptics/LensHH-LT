using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace LensHH.Mcp
{
    /// <summary>
    /// Stock-lens catalog plumbing: locate the bundled SQLite at runtime,
    /// resolve a part_number (with optional vendor) to its sibling .lhlt path.
    /// Lives in LensHH.Mcp because it pulls in Microsoft.Data.Sqlite; the
    /// SQLite-free vertex helpers (extract / reverse / clone) live in
    /// LensHH.Core.IO.LensInsertHelpers so the LT App can reuse them
    /// without dragging SQLite into the App's reference graph.
    /// </summary>
    public static class StockLensCatalog
    {
        /// <summary>
        /// Locate stock-lens-catalog.sqlite by walking a few candidate paths
        /// relative to the MCP executable. Production install: catalogs/ is a
        /// sibling of mcp/. Dev tree: catalogs/ is several levels up. The
        /// LENSHH_CATALOGS_DIR env var overrides everything.
        /// </summary>
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
    }
}
