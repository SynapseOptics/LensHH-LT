using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using ModelContextProtocol.Server;

namespace LensHH.Mcp.Tools
{
    /// <summary>
    /// MCP tools that drive the end-to-end Sasian design recipe:
    /// build skeleton → free-optimize → for each element {try every (pattern,
    /// candidate), pick best, save}. Backed by SasianDesignService running
    /// the pipeline on a worker Task. Agent polls via sasian_design_status.
    /// </summary>
    [McpServerToolType]
    public class SasianDesignTools
    {
        private readonly McpSession _session;
        public SasianDesignTools(McpSession session) => _session = session;

        // ── Tool 1: start ───────────────────────────────────────────────────

        [McpServerTool, Description(
            "Start the end-to-end Sasian design pipeline. Builds a starting skeleton on the given template, free-optimizes with glass substitution, then for each element tries every (pattern, candidate) substitution combination, multistart-reoptimizes each, and locks in the best. Saves intermediate .lhlt files at every step under outputDir. Returns a jobId immediately; poll sasian_design_status(jobId) to watch progress.\n\n"
            + "templatePath: a .lhlt with merit function operands, fields, wavelengths, aperture, and stop already configured. The pipeline replaces only the lens stack; everything else is preserved.\n\n"
            + "outputDir: a folder for intermediate .lhlt saves. Created if it doesn't exist. Files are named '01_freeopt.lhlt', '02_E1_<descr>_merit<m>.lhlt', '03_E2_…', and finally '0N_final_allstock_merit<m>.lhlt'.\n\n"
            + "architecture: skeleton layout (currently 'single-single-single' / 'cooke').\n\n"
            + "candidatesPerPattern (default 3): how many top stock candidates to try per pattern per element. Per element the pipeline runs about 3 patterns × N candidates BH+LM re-optimizations, so total wall time scales linearly with this. Use 1-2 for fast exploration, 3 for thorough.\n\n"
            + "bhMaxHops/bhLmPerHop/bhHjPerHop: Basin Hopping + LM budget per optimization run. Defaults 2000/4000/30 match the GUI BasinHoppingHjLm dialog preset. bhLmPerHop is an upper bound — LM terminates earlier on tolerance for easy problems, but problems that ARE making progress shouldn't be cut off prematurely (the engine class's 60 default is too low for real designs).\n\n"
            + "bhNoImprovementSeconds (default 600): no-improvement watchdog. Each BH phase terminates if best merit hasn't improved within this many seconds. Default 600 s = 10 min — practical termination criterion since BhMaxHops is set high (2000). Timer resets on every best-merit improvement. Set to 0 to disable (let BH run all MaxHops).\n\n"
            + "stopPosition / semiDiameterSeed / airGapSeed / bflSeed: skeleton seed parameters, see build_skeleton. stopPosition (default 2) places the physical aperture stop in the chosen air gap; 0 = leading air before L1, 1 = between L1 and L2, 2 = between L2 and L3 (classic Cooke default — near-symmetry around L2), 3 = BFL gap.\n\n"
            + "substitutionCatalog (default 'auto'): glass-substitution catalog used by BH on every hop. 'auto' picks StockGlassesUV if min(wavelengths) < 0.380 µm else StockGlassesVisible. BH auto-detects eligible glass surfaces by whether the element has a reshaping variable on its front or back face — locked stock parts inserted by replace_element are automatically skipped because their curvatures + glass thicknesses are fixed. Free-opt skeleton elements stay eligible. Pass '' to disable substitution.\n\n"
            + "monochromaticPhase1 (default false): when true, the template's non-primary wavelength weights are zeroed at the start of the run and restored just before the final all-stock file is written. The optimizer then minimizes a d-line-only merit during shape + glass exploration (no chromatic-aberration trade-offs). Pair with substitutionCatalog='' for a true 'all single-glass d-line design' phase that's a clean starting point for a Phase 2 doublet/chromatic-correction step. Intermediate files saved during the run carry the zeroed weights; only the final file is restored.")]
        public string SasianDesignStart(
            string templatePath,
            string outputDir,
            string architecture = "single-single-single",
            int candidatesPerPattern = 3,
            int bhMaxHops = 2000,
            int bhLmPerHop = 4000,
            int bhHjPerHop = 30,
            double bhNoImprovementSeconds = 600.0,
            bool monochromaticPhase1 = false,
            int stopPosition = 2,
            double semiDiameterSeed = 12.5,
            double airGapSeed = 10.0,
            double bflSeed = 45.0,
            string substitutionCatalog = "auto")
        {
            try
            {
                var data = new SasianJobData
                {
                    TemplatePath = templatePath,
                    OutputDir = outputDir,
                    Architecture = architecture,
                    CandidatesPerPattern = candidatesPerPattern,
                    SubstitutionCatalog = substitutionCatalog,
                    StopPosition = stopPosition,
                    SemiDiameterSeed = semiDiameterSeed,
                    AirGapSeed = airGapSeed,
                    BflSeed = bflSeed,
                    BhMaxHops = bhMaxHops,
                    BhLmPerHop = bhLmPerHop,
                    BhHjPerHop = bhHjPerHop,
                    BhNoImprovementSeconds = bhNoImprovementSeconds,
                    MonochromaticPhase1 = monochromaticPhase1,
                };
                var job = _session.SasianDesign.Start(_session, data);
                string watchdog = bhNoImprovementSeconds > 0 ? $", no-progress timeout {bhNoImprovementSeconds:F0}s" : ", no-progress timeout disabled";
                string mono = monochromaticPhase1 ? ", MONOCHROMATIC Phase 1 (non-primary wavelength weights zeroed during run, restored at end)" : "";
                return $"Started sasian_design. jobId={job.JobId}; architecture={architecture}, "
                     + $"candidatesPerPattern={candidatesPerPattern}, "
                     + $"BH: {bhMaxHops} hops × ({bhLmPerHop} LM + {bhHjPerHop} HJ){watchdog}{mono}. "
                     + $"Poll sasian_design_status({job.JobId}).";
            }
            catch (System.Exception ex)
            {
                return $"sasian_design_start failed: {ex.Message}";
            }
        }

        // ── Tool 2: status ──────────────────────────────────────────────────

        [McpServerTool, Description(
            "Get the current status of a sasian_design job. Returns the current phase (building skeleton / free-optimizing / substituting element X / completed), all trial results so far (per element: pattern, parts, post-reopt merit), the list of accepted winners, and saved intermediate file paths.")]
        public string SasianDesignStatus(string jobId)
        {
            var job = _session.GetJob(jobId);
            if (job == null) return $"No job '{jobId}'.";
            var data = _session.SasianDesign.GetData(jobId);
            if (data == null) return $"Job '{jobId}' has no sasian data (not a sasian_design job?).";

            var sb = new StringBuilder();
            sb.AppendLine($"jobId:   {job.JobId}");
            sb.AppendLine($"state:   {job.Status}");
            sb.AppendLine($"phase:   {job.Phase}");
            sb.AppendLine($"elapsed: {job.Elapsed.TotalSeconds:F1} s");
            sb.AppendLine($"elements: {data.CurrentElement}/{data.TotalElements}");
            if (!double.IsNaN(data.FreeOptMerit))
                sb.AppendLine($"freeopt merit: {data.FreeOptMerit:E4}");
            if (!double.IsNaN(data.FinalMerit))
                sb.AppendLine($"final merit:   {data.FinalMerit:E4}");

            sb.AppendLine();
            sb.AppendLine("Trials so far:");
            if (data.Trials.Count == 0)
                sb.AppendLine("  (none)");
            else
            {
                sb.AppendLine($"  {"#",-3} {"E",-2} {"pattern",-15} {"parts",-30} {"merit",-12} {"status"}");
                int n = 1;
                foreach (var t in data.Trials)
                {
                    string m = double.IsNaN(t.Merit) ? "—" :
                        t.Merit.ToString("E4", CultureInfo.InvariantCulture);
                    string label = t.Winner ? "★ " + t.Status : t.Status;
                    string parts = t.PartsDescriptor.Length > 30
                        ? t.PartsDescriptor.Substring(0, 27) + "..."
                        : t.PartsDescriptor;
                    sb.AppendLine($"  {n++,-3} {t.ElementIndex,-2} {t.Pattern,-15} {parts,-30} {m,-12} {label}");
                }
            }

            if (data.Winners.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Accepted winners:");
                foreach (var w in data.Winners)
                    sb.AppendLine($"  E{w.ElementIndex}: {w.Pattern} {w.PartsDescriptor} → merit {w.Merit:E4}");
            }

            if (data.SavedFiles.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Saved intermediates:");
                foreach (var p in data.SavedFiles) sb.AppendLine($"  {p}");
            }

            if (!string.IsNullOrEmpty(data.Error))
            {
                sb.AppendLine();
                sb.AppendLine($"ERROR: {data.Error}");
            }
            return sb.ToString();
        }

        // ── Tool 3: cancel ──────────────────────────────────────────────────

        [McpServerTool, Description("Cancel a running sasian_design job. Any element already completed keeps its winner; the current trial is interrupted at the next safe point. The session.System holds whatever state the pipeline reached.")]
        public string SasianDesignCancel(string jobId)
        {
            var job = _session.GetJob(jobId);
            if (job == null) return $"No job '{jobId}'.";
            if (job.Status != JobStatus.Running) return $"Job '{jobId}' is already {job.Status}.";
            job.Cts.Cancel();
            return $"Cancellation requested for job {jobId}. Poll sasian_design_status to confirm transition to Cancelled.";
        }

        // ── Tool 4: discard ─────────────────────────────────────────────────

        [McpServerTool, Description("Discard a finished sasian_design job's per-job state (trial list, intermediate paths). The intermediate .lhlt files on disk are NOT removed.")]
        public string SasianDesignDiscard(string jobId)
        {
            _session.SasianDesign.Discard(jobId);
            return $"Discarded sasian_design data for job {jobId}.";
        }
    }
}
