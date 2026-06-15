using System.ComponentModel;
using System.Linq;
using System.Text;
using ModelContextProtocol.Server;

namespace LensHH.Mcp.Tools
{
    /// <summary>
    /// MCP tools for the end-to-end starting-design pipeline: Differential-Evolution seed search
    /// (GPU-resident when a CUDA device is present — population fills the device — else CPU) with
    /// the focus+EFL conditioner, then optional Local-LM or Multistart-LM polish of the best
    /// candidates. ALL pre-polish seeds are saved by default. Non-blocking: start returns a jobId;
    /// poll de_pipeline_status.
    /// </summary>
    [McpServerToolType]
    public class DePipelineTools
    {
        private readonly McpSession _session;
        public DePipelineTools(McpSession session) => _session = session;

        [McpServerTool, Description(
            "Start the full DE starting-design pipeline on the currently loaded system + merit function. "
            + "Runs the Differential-Evolution seed generator, then polishes the best candidates with LM or Multistart. "
            + "Saves ALL pre-polish seeds to <outputDir>/seeds_pre_polish/ and the polished designs to <outputDir>/polished/. "
            + "Returns a jobId immediately; poll de_pipeline_status(jobId) to watch progress.\n\n"
            + "outputDir: folder for the saved .lhlt pools (created if missing).\n"
            + "gpu (default false): use the GPU-resident DE when a CUDA device is present (falls back to CPU if not). "
            + "On GPU the population is sized automatically to fill the device; on CPU set 'population'. Not every user has a GPU.\n"
            + "generations (default 10000): DE generations. Reduce for CPU.\n"
            + "population (default 0 = engine default): CPU population size. Ignored on GPU (auto-filled).\n"
            + "focusSurface (default -1 = auto): surface whose THICKNESS the focus solve adjusts. Auto = the airspace "
            + "just before the image plane (image surface index − 1). Set explicitly to use a different compensator.\n"
            + "eflSurface (default -1 = auto): surface whose CURVATURE the EFL solve adjusts. Auto = the last surface "
            + "with optical power. The EFL solve only runs when the merit has an EFL target operand.\n"
            + "adjustEfl (default true): when false the conditioner refocuses but does NOT adjust curvature for EFL.\n"
            + "eflTolerance (default 0.05): only adjust EFL when the member's EFL is within this FRACTION of the target "
            + "(0.05 = ±5%). Members further off are left for the merit to reject. <=0 = always adjust.\n"
            + "condition (default true): master on/off for the focus+EFL conditioner.\n"
            + "polish (default 'multistart'): 'multistart' = Multistart-LM (most thorough, mirrors Global optimization), "
            + "'lm' = Local Levenberg-Marquardt, 'none' = raw seeds.\n"
            + "polishCount (default 16): how many of the best distinct seeds to polish.\n"
            + "seedsToEmit (default 16): how many distinct seeds the DE emits.\n"
            + "baseSeed (default 1): RNG seed for reproducibility.\n"
            + "lmIterations (default 4000): LM iteration cap per candidate (LocalLM path).\n"
            + "polishFolder (default none): polish every *.lhlt in this folder (each must match the loaded design's "
            + "structure) and SKIP the DE search — re-optimize a previously-saved seed set.")]
        public string DePipelineStart(
            string outputDir,
            bool gpu = false,
            int generations = 10000,
            int population = 0,
            int focusSurface = -1,
            int eflSurface = -1,
            bool adjustEfl = true,
            double eflTolerance = 0.05,
            bool condition = true,
            string polish = "multistart",
            int polishCount = 16,
            int seedsToEmit = 16,
            int baseSeed = 1,
            int lmIterations = 4000,
            string? polishFolder = null)
        {
            try
            {
                var data = new DeJobData
                {
                    OutputDir = outputDir,
                    UseGpu = gpu,
                    Generations = generations,
                    PopulationSize = population,
                    FocusSurface = focusSurface,
                    EflSurface = eflSurface,
                    AdjustEfl = adjustEfl,
                    EflTolerance = eflTolerance,
                    Condition = condition,
                    PolishMethod = polish,
                    PolishCount = polishCount,
                    SeedsToEmit = seedsToEmit,
                    BaseSeed = baseSeed,
                    LmIterations = lmIterations,
                    PolishFolder = polishFolder,
                };
                var job = _session.DePipeline.Start(_session, data);
                return $"Started de_pipeline. jobId={job.JobId}; engine={(gpu ? "GPU if available (population fills the device)" : "CPU")}, "
                     + $"generations={generations}, conditioner={(condition ? "on" : "off")}, polish={polish} on best {polishCount}. "
                     + $"Saving pre-polish + polished pools under {outputDir}. Poll de_pipeline_status({job.JobId}).";
            }
            catch (System.Exception ex) { return $"de_pipeline_start failed: {ex.Message}"; }
        }

        [McpServerTool, Description(
            "Status of a de_pipeline job: current phase (DE searching / saving / completed), DE progress (generation, "
            + "best merit), the polished candidates (before → after merit, glasses), and the saved file counts.")]
        public string DePipelineStatus(string jobId)
        {
            var job = _session.GetJob(jobId);
            if (job == null) return $"No job '{jobId}'.";
            var data = _session.DePipeline.GetData(jobId);
            if (data == null) return $"Job '{jobId}' has no de_pipeline data (not a de_pipeline job?).";

            var sb = new StringBuilder();
            sb.AppendLine($"jobId:   {job.JobId}");
            sb.AppendLine($"state:   {job.Status}");
            sb.AppendLine($"phase:   {job.Phase}");
            sb.AppendLine($"elapsed: {job.Elapsed.TotalSeconds:F1} s");
            if (job.MaxTrials > 0) sb.AppendLine($"DE progress: gen {job.Trial}/{job.MaxTrials}");
            if (!double.IsNaN(job.BestMerit)) sb.AppendLine($"DE best merit: {job.BestMerit:E4}");
            if (data.SeedsFound > 0)
                sb.AppendLine($"engine: {(data.UsedGpu ? "GPU-resident" : "CPU")}, population {data.Population}, "
                            + $"{data.GenerationsRun} generations, {data.SeedsFound} distinct seeds");
            if (data.DeElapsedSeconds > 0)
                sb.AppendLine($"DE time: {data.DeElapsedSeconds:F1} s ({data.DeSecondsPerGeneration:F3} s/gen)");

            if (data.Polished.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Polished (best-first):  before → after");
                int n = 1;
                foreach (var p in data.Polished.OrderBy(x => x.after))
                    sb.AppendLine($"  #{n++,2}  {p.before:E4} → {p.after:E4}  [{p.glasses}]");
                sb.AppendLine($"best merit {data.BestBefore:E4} (pre-polish) → {data.BestAfter:E4} (post-polish)");
            }
            if (data.PreSavedFiles.Count > 0)
                sb.AppendLine($"saved {data.PreSavedFiles.Count} pre-polish + {data.PolishedSavedFiles.Count} polished .lhlt under {data.OutputDir}");
            if (!string.IsNullOrEmpty(data.LogFile)) sb.AppendLine($"run log: {data.LogFile}");
            if (!string.IsNullOrEmpty(data.Error)) sb.AppendLine($"ERROR: {data.Error}");
            return sb.ToString();
        }

        [McpServerTool, Description(
            "Cancel a running de_pipeline job. The DE stops at the next safe point; whatever seeds were found are still "
            + "saved. Poll de_pipeline_status to confirm the transition to Cancelled.")]
        public string DePipelineCancel(string jobId)
        {
            var job = _session.GetJob(jobId);
            if (job == null) return $"No job '{jobId}'.";
            if (job.Status != JobStatus.Running) return $"Job '{jobId}' is already {job.Status}.";
            job.Cts.Cancel();
            return $"Cancellation requested for job {jobId}. Poll de_pipeline_status to confirm.";
        }

        [McpServerTool, Description(
            "Discard a finished de_pipeline job's in-memory state (progress + result lists). The saved .lhlt files on "
            + "disk are NOT removed.")]
        public string DePipelineDiscard(string jobId)
        {
            _session.DePipeline.Discard(jobId);
            return $"Discarded de_pipeline data for job {jobId}.";
        }
    }
}
