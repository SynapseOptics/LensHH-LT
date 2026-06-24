using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using LensHH.Core.Configuration;
using LensHH.Core.Optimization;

namespace LensHH.Core.IO
{
    /// <summary>
    /// Persists the per-chain results of a parallel basin-hopping run, one .lhlt per chain,
    /// into a caller-specified folder. Shared by the CLI / MCP / API / GUI so all four save
    /// chains identically. Files are named so the best-merit design sorts first.
    /// </summary>
    public static class ChainResultWriter
    {
        /// <summary>
        /// Write every chain design in <paramref name="chains"/> to <paramref name="folder"/>
        /// as a separate .lhlt. Creates the folder if needed. Returns the written paths
        /// (best-merit first). The shared <paramref name="meritFunction"/> / <paramref name="configEditor"/>
        /// are embedded in each file (the merit definition is identical across chains).
        /// </summary>
        public static List<string> SaveChains(
            IReadOnlyList<BasinHoppingOptimizerBatch.ChainDesign> chains,
            string folder, string baseName,
            MeritFunction.MeritFunction? meritFunction = null,
            ConfigurationEditor? configEditor = null)
        {
            if (string.IsNullOrWhiteSpace(folder))
                throw new ArgumentException("Output folder is required.", nameof(folder));

            var written = new List<string>();
            if (chains == null || chains.Count == 0) return written;

            Directory.CreateDirectory(folder);
            string safeBase = SanitizeFileName(string.IsNullOrWhiteSpace(baseName) ? "design" : baseName);

            var ordered = chains.OrderBy(c => c.Merit).ToList();
            for (int rank = 0; rank < ordered.Count; rank++)
            {
                var c = ordered[rank];
                string meritStr = SanitizeFileName(c.Merit.ToString("G6", CultureInfo.InvariantCulture));
                string name = $"{safeBase}_rank{rank + 1:D2}_chain{c.ChainIndex:D2}_m{meritStr}.lhlt";
                string path = Path.Combine(folder, name);
                LhltWriter.Write(c.System, path, meritFunction, configEditor);
                written.Add(path);
            }
            return written;
        }

        private static string SanitizeFileName(string s)
        {
            foreach (char ch in Path.GetInvalidFileNameChars())
                s = s.Replace(ch, '_');
            return s;
        }
    }
}
