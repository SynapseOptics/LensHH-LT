using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LensHH.Core.IO;
using LensHH.Core.Optimization;

namespace LensHH.Mcp
{
    /// <summary>Per-job state for a de_pipeline run (params + progress + saved files).</summary>
    public class DeJobData
    {
        public string JobId { get; set; } = "";
        public string OutputDir { get; set; } = "";

        // Inputs (mirror DePipelineSettings).
        public bool UseGpu { get; set; }
        public int Generations { get; set; } = 10000;
        public int PopulationSize { get; set; }          // 0 = engine default (GPU auto-fills)
        public int FocusSurface { get; set; } = -1;       // -1 = auto (image − 1)
        public int EflSurface { get; set; } = -1;         // -1 = auto (last powered)
        public bool AdjustEfl { get; set; } = true;
        public double EflTolerance { get; set; } = 0.05;   // only adjust EFL within this fraction of target (default 0.05 = ±5%); <=0 = always
        public bool Condition { get; set; } = true;
        public string PolishMethod { get; set; } = "multistart";  // multistart | lm | none
        public int PolishCount { get; set; } = 16;
        public int SeedsToEmit { get; set; } = 16;
        public int BaseSeed { get; set; } = 1;
        public int LmIterations { get; set; } = 4000;
        /// <summary>When set, polish a previously-saved DE result folder (every *.lhlt) and skip
        /// the DE search. The files must match the loaded design's structure.</summary>
        public string? PolishFolder { get; set; }

        // Results / progress.
        public bool UsedGpu { get; set; }
        public int Population { get; set; }
        public int GenerationsRun { get; set; }
        public int SeedsFound { get; set; }
        public double DeElapsedSeconds { get; set; }
        public double DeSecondsPerGeneration { get; set; }
        public string? LogFile { get; set; }
        public double BestBefore { get; set; } = double.NaN;
        public double BestAfter { get; set; } = double.NaN;
        public List<string> PreSavedFiles { get; } = new();
        public List<string> PolishedSavedFiles { get; } = new();
        public List<(int rank, string glasses, double before, double after)> Polished { get; } = new();
        public string? Error { get; set; }
    }

    /// <summary>
    /// Backing store + worker for the de_pipeline_* MCP tools: runs the full
    /// DE → (LM | Multistart) starting-design pipeline (GPU-resident when available,
    /// population fills the device; else CPU) with the §8.5 focus+EFL conditioner, saves
    /// the pre-polish seed pool by default, then the polished candidates. Non-blocking —
    /// the agent polls de_pipeline_status.
    /// </summary>
    public class DePipelineService
    {
        private readonly Dictionary<string, DeJobData> _data = new();
        private readonly object _lock = new();

        public DeJobData? GetData(string jobId) { lock (_lock) return _data.TryGetValue(jobId, out var d) ? d : null; }
        public void Discard(string jobId) { lock (_lock) _data.Remove(jobId); }

        public RunningJob Start(McpSession session, DeJobData data)
        {
            if (session.System == null || session.System.Surfaces.Count < 3)
                throw new InvalidOperationException("de_pipeline requires a system loaded in the session (load_system first).");
            if (session.MeritFunction == null || session.MeritFunction.Operands.Count == 0)
                throw new InvalidOperationException("de_pipeline requires a merit function with operands on the loaded system.");
            if (string.IsNullOrWhiteSpace(data.OutputDir))
                throw new ArgumentException("outputDir is required.");
            Directory.CreateDirectory(data.OutputDir);

            var job = new RunningJob(kind: "de_pipeline");
            data.JobId = job.JobId;
            lock (_lock) _data[job.JobId] = data;
            session.AddJob(job);
            job.Phase = "starting";
            Task.Run(() => RunPipeline(data, session, job));
            return job;
        }

        private void RunPipeline(DeJobData data, McpSession session, RunningJob job)
        {
            try
            {
                var system = session.System!;
                var mf = session.MeritFunction!;
                var glassMgr = session.GlassCatalog;

                var pset = new DePipelineSettings
                {
                    UseGpu = data.UseGpu,
                    PolishCandidateCount = data.PolishCount,
                    LmIterations = data.LmIterations,
                    PolishMethod = data.PolishMethod.ToLowerInvariant() switch
                    {
                        "none" => DePolishMethod.None,
                        "multistart" or "ms" => DePolishMethod.MultistartLm,
                        _ => DePolishMethod.LocalLm,
                    },
                };
                pset.De.MaxGenerations = data.Generations;
                pset.De.StallGenerations = 0;
                pset.De.ConditionFocusEfl = data.Condition;
                pset.De.AdjustCurvatureForEfl = data.AdjustEfl;
                pset.De.EflAdjustTolerance = data.EflTolerance;
                pset.De.FocusCompensatorSurface = data.FocusSurface;
                pset.De.EflControlSurface = data.EflSurface;
                pset.De.SeedsToEmit = data.SeedsToEmit;
                pset.De.BaseSeed = data.BaseSeed;
                if (data.PopulationSize > 0) pset.De.PopulationSize = data.PopulationSize;

                var pipeline = new DeOptimizationPipeline(system, mf, glassMgr, session.ConfigEditor)
                {
                    Settings = pset,
                    OnProgress = p =>
                    {
                        job.Phase = p.StatusMessage;
                        job.BestMerit = p.BestMerit;
                        job.Trial = p.RestartIndex;
                        job.MaxTrials = p.MaxRestarts;
                    },
                };

                DePipelineResult result;
                if (!string.IsNullOrEmpty(data.PolishFolder))
                {
                    job.Phase = "loading saved seeds";
                    if (!Directory.Exists(data.PolishFolder))
                        throw new InvalidOperationException($"Folder not found: {data.PolishFolder}");
                    var files = Directory.GetFiles(data.PolishFolder, "*.lhlt");
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    if (files.Length == 0)
                        throw new InvalidOperationException($"No .lhlt files in {data.PolishFolder}");
                    var systems = new List<LensHH.Core.Models.OpticalSystem>(files.Length);
                    var labels = new List<string>(files.Length);
                    foreach (var f in files) { systems.Add(LhltReader.Read(f).System); labels.Add(Path.GetFileName(f)); }
                    job.Phase = "polishing saved seeds";
                    result = pipeline.PolishExisting(systems, labels, job.Cts.Token);
                }
                else
                {
                    job.Phase = "DE searching";
                    result = pipeline.Run(job.Cts.Token);
                }

                data.UsedGpu = result.UsedGpu;
                data.Population = result.Population;
                data.GenerationsRun = result.Generations;
                data.SeedsFound = result.SeedPool.Models.Count;
                data.DeElapsedSeconds = result.DeElapsed.TotalSeconds;
                data.DeSecondsPerGeneration = result.DeSecondsPerGeneration;

                // Run log OUTSIDE the lens-file folders (output root) — settings + per-candidate
                // before/after/elapsed, for easy run comparison.
                try
                {
                    string logStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string logMode = !string.IsNullOrEmpty(data.PolishFolder) ? "Polish previously-saved DE results" : "DE search + polish";
                    string logSrc = !string.IsNullOrEmpty(data.PolishFolder) ? data.PolishFolder! : "(DE search)";
                    string logPath = Path.Combine(data.OutputDir, $"de_run_{logStamp}.log");
                    File.WriteAllText(logPath, DeRunLog.Build(pset, result, logMode, logSrc, DateTime.Now));
                    data.LogFile = logPath;
                }
                catch { }

                string title = string.IsNullOrWhiteSpace(system.Title) ? "design" : system.Title;

                // Save ALL pre-polish seeds by default (skip when polishing a saved folder —
                // that folder already IS the pre-polish set).
                if (string.IsNullOrEmpty(data.PolishFolder))
                {
                job.Phase = "saving pre-polish seeds";
                string preDir = Path.Combine(data.OutputDir, "seeds_pre_polish");
                Directory.CreateDirectory(preDir);
                for (int r = 0; r < result.SeedPool.Models.Count; r++)
                {
                    var m = result.SeedPool.Models[r];
                    string glassTag = m.GlassSet.Count > 0 ? string.Join("-", m.GlassSet) : "noglass";
                    string path = Path.Combine(preDir, Sanitize($"{title}_seed{r + 1:00}_merit{m.Merit:G4}_{glassTag}") + ".lhlt");
                    try { LhltWriter.Write(m.System, path, mf, session.ConfigEditor); data.PreSavedFiles.Add(path); } catch { }
                }
                }

                // Save polished candidates.
                job.Phase = "saving polished";
                string postDir = Path.Combine(data.OutputDir, "polished");
                Directory.CreateDirectory(postDir);
                int pr = 1;
                foreach (var c in result.Polished)
                {
                    string glassTag = c.GlassSet.Count > 0 ? string.Join("-", c.GlassSet) : "noglass";
                    string path = Path.Combine(postDir, Sanitize($"{title}_polished{pr++:00}_merit{c.MeritAfter:G4}_{glassTag}") + ".lhlt");
                    try { LhltWriter.Write(c.System, path, mf, session.ConfigEditor); data.PolishedSavedFiles.Add(path); } catch { }
                    data.Polished.Add((c.SeedRank, glassTag, c.MeritBefore, c.MeritAfter));
                }
                data.BestBefore = result.SeedPool.Models.Count > 0 ? result.SeedPool.Models.Min(m => m.Merit) : double.NaN;
                data.BestAfter = result.Polished.Count > 0 ? result.Polished.Min(p => p.MeritAfter) : data.BestBefore;

                if (result.Cancelled) job.Cancel(); else job.Complete(result.Message);
            }
            catch (OperationCanceledException) { job.Cancel(); }
            catch (Exception ex) { data.Error = ex.Message; job.Fault(ex); }
        }

        private static string Sanitize(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
