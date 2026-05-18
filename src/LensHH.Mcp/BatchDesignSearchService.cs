using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LensHH.Core.IO;
using LensHH.Core.Models;
using LensHH.Core.Optimization;

namespace LensHH.Mcp
{
    // ── Public types exposed to the tools layer ──────────────────────────────

    /// <summary>One stock-lens insert in a candidate's pipeline.</summary>
    public class InsertSpec
    {
        public string PartNumber { get; set; } = "";
        public string? Vendor { get; set; }
        public bool Reversed { get; set; }
        public double AirThickness { get; set; }
        /// <summary>
        /// Optional host-numbered surface index. If present, the insert
        /// starts a new group at host surface AfterSurface (translated
        /// to current-system numbering on the fly). If absent, the insert
        /// goes sequentially after the prior insert's back vertex.
        /// </summary>
        public int? AfterSurface { get; set; }
    }

    /// <summary>One design candidate to be cloned-from-host, modified, and optimized.</summary>
    public class CandidateDescriptor
    {
        public string? Label { get; set; }
        /// <summary>For floating-stop hosts (case a/b): seed S1.Thickness.</summary>
        public double? EntrancePupil { get; set; }
        public List<InsertSpec> Inserts { get; set; } = new();
    }

    /// <summary>Per-candidate optimized result. Index matches the input ordering.</summary>
    public class CandidateResult
    {
        public int CandidateIndex { get; set; }
        public string? Label { get; set; }
        public double InitialMerit { get; set; } = double.NaN;
        public double FinalMerit { get; set; } = double.NaN;
        /// <summary>"ok" / "skipped" / "error: <reason>"</summary>
        public string Status { get; set; } = "pending";
        public string? Error { get; set; }
        public int Iterations { get; set; }
        /// <summary>(surfaceIndex, finalThickness) for every air gap the candidate optimized.</summary>
        public List<(int surfaceIndex, double thickness)> OptimizedThicknesses { get; set; } = new();
        /// <summary>Final EFL (mm), from SystemDataCalculator post-optimization.</summary>
        public double FinalEfl { get; set; } = double.NaN;
        /// <summary>
        /// For floating-stop hosts (case a/b where entrance pupil was specified):
        /// reports where the stop actually landed after the optimizer moved S1.Thickness.
        /// Null when the candidate didn't supply entrance pupil (cases c/d/e — host stop fixed).
        /// </summary>
        public StopLocation? StopLocation { get; set; }
    }

    /// <summary>
    /// Post-optimization placement of the entrance pupil relative to the
    /// lens stack. For case-a hosts the optimizer may move S1.Thickness to
    /// any value; if that lands the stop INSIDE a glass element, the
    /// candidate is physically invalid (you can't put an iris inside glass).
    /// </summary>
    public class StopLocation
    {
        /// <summary>Final S1.Thickness; sign convention matches Zemax (negative = pupil behind S1).</summary>
        public double S1Thickness { get; set; }
        /// <summary>"in air before S2" / "between S3 and S4" / "INSIDE glass element at S4" / "between last lens and IMG".</summary>
        public string Context { get; set; } = "";
        /// <summary>True if the stop fell inside any glass element — candidate is physically invalid.</summary>
        public bool BuriedInGlass { get; set; }
    }

    /// <summary>
    /// Per-job state owned by BatchDesignSearchService. Sits beside the
    /// RunningJob in McpSession.Jobs; the RunningJob tracks status/progress
    /// strings while this holds the structural input + results array.
    /// </summary>
    public class BatchJobData
    {
        public string JobId { get; set; } = "";
        public string HostPath { get; set; } = "";
        public List<CandidateDescriptor> Candidates { get; set; } = new();
        public CandidateResult[] Results { get; set; } = Array.Empty<CandidateResult>();
        public string InnerOptimizer { get; set; } = "lm";
        public int Parallelism { get; set; } = 1;
        public int Done; // updated via Interlocked
    }

    // ── Service ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Drives the batch_design_search_* MCP tools. Holds per-job structural
    /// data keyed by jobId, runs candidate executions on a worker Task, and
    /// exposes Apply() so a "winner" can be loaded into McpSession.
    /// </summary>
    public class BatchDesignSearchService
    {
        private readonly Dictionary<string, BatchJobData> _data = new();
        private readonly object _lock = new();

        public BatchJobData? GetData(string jobId)
        {
            lock (_lock) return _data.TryGetValue(jobId, out var d) ? d : null;
        }

        public void Discard(string jobId)
        {
            lock (_lock) _data.Remove(jobId);
        }

        /// <summary>
        /// Kick off a batch. Creates a RunningJob on the session, registers
        /// per-job data, starts a worker Task that iterates candidates. The
        /// agent polls via batch_design_search_status(jobId).
        /// </summary>
        public RunningJob Start(McpSession session, string hostPath,
            List<CandidateDescriptor> candidates, string innerOptimizer, int parallelism)
        {
            if (!File.Exists(hostPath))
                throw new FileNotFoundException($"Host .lhlt not found: {hostPath}");
            if (candidates == null || candidates.Count == 0)
                throw new ArgumentException("candidates list is empty.");
            innerOptimizer = (innerOptimizer ?? "lm").ToLowerInvariant();
            if (innerOptimizer != "lm" && innerOptimizer != "multistart")
                throw new ArgumentException($"innerOptimizer must be 'lm' or 'multistart', got '{innerOptimizer}'.");
            // Thread-safety audit (2026-05-18, fa6e3a3-era):
            //   ArbitraryRay     : static _aimCache is ConditionalWeakTable (.NET-thread-safe);
            //                      DebugLog is atomic bool. Safe under concurrent calls.
            //   LocalOptimizer   : no non-readonly static fields; pure per-instance state.
            //   GlassCatalogMgr  : per-McpSession instance; Dictionary read-only after initial
            //                      load (LM doesn't mutate). Multistart with glass substitution
            //                      WOULD mutate — agent should pass parallelism=1 in that case.
            //   Native side      : caller-provides-buffers, no globals (per feedback_native_no_alloc).
            // Each LocalOptimizer already runs ParallelEvaluation=true internally, so candidate-
            // level parallelism × LM's inner ray-fan can saturate cores. Cap default at
            // ProcessorCount/4 to leave breathing room.
            if (parallelism < 1)
                parallelism = Math.Max(1, Environment.ProcessorCount / 4);

            var job = new RunningJob(kind: "batch_design_search")
            {
                MaxTrials = candidates.Count,
            };

            var data = new BatchJobData
            {
                JobId          = job.JobId,
                HostPath       = hostPath,
                Candidates     = candidates,
                Results        = new CandidateResult[candidates.Count],
                InnerOptimizer = innerOptimizer,
                Parallelism    = parallelism,
            };
            for (int i = 0; i < candidates.Count; i++)
                data.Results[i] = new CandidateResult
                {
                    CandidateIndex = i,
                    Label = candidates[i].Label,
                };

            lock (_lock) _data[job.JobId] = data;

            job.Phase = $"queued ({candidates.Count} candidate(s))";

            job.Task = Task.Run(() =>
            {
                try
                {
                    job.Phase = $"running 0/{candidates.Count}";
                    if (parallelism <= 1)
                    {
                        for (int i = 0; i < candidates.Count; i++)
                        {
                            if (job.Cts.IsCancellationRequested) break;
                            RunOne(data, session, i, job);
                        }
                    }
                    else
                    {
                        Parallel.For(0, candidates.Count, new ParallelOptions
                        {
                            MaxDegreeOfParallelism = parallelism,
                            CancellationToken      = job.Cts.Token,
                        }, i => RunOne(data, session, i, job));
                    }

                    if (job.Cts.IsCancellationRequested) { job.Cancel(); return; }

                    int best = -1;
                    double bestMerit = double.PositiveInfinity;
                    for (int i = 0; i < data.Results.Length; i++)
                    {
                        var r = data.Results[i];
                        if (r.Status == "ok" && !double.IsNaN(r.FinalMerit) && r.FinalMerit < bestMerit)
                        {
                            bestMerit = r.FinalMerit;
                            best = i;
                        }
                    }
                    job.BestMerit = bestMerit;
                    job.Complete(best >= 0
                        ? $"Best candidate: #{best} '{data.Results[best].Label ?? "(unlabeled)"}' merit={bestMerit:E6}. Call batch_design_search_keep({job.JobId}, {best}) to load it."
                        : $"All {candidates.Count} candidates failed. Inspect partialResults in batch_design_search_status({job.JobId}) for per-candidate errors.");
                }
                catch (OperationCanceledException) { job.Cancel(); }
                catch (Exception ex) { job.Fault(ex); }
            });

            session.AddJob(job);
            return job;
        }

        // ── Worker: one candidate ─────────────────────────────────────────────

        private void RunOne(BatchJobData data, McpSession session, int idx, RunningJob job)
        {
            var cand = data.Candidates[idx];
            var result = data.Results[idx];
            try
            {
                // Fresh load of the host so each worker has an isolated system.
                var hostResult = LhltReader.Read(data.HostPath);
                var system = hostResult.System;
                var merit  = hostResult.MeritFunction;
                var config = hostResult.ConfigEditor;

                // Seed entrance pupil (S1.Thickness) for floating-stop hosts.
                if (cand.EntrancePupil.HasValue)
                {
                    int stopIdx = system.StopSurfaceIndex;
                    if (stopIdx > 0 && stopIdx < system.Surfaces.Count)
                        system.Surfaces[stopIdx].Thickness = cand.EntrancePupil.Value;
                }

                // Apply inserts. Track host->current index translation so explicit
                // after_surface=H (host numbering) maps to the correct current index.
                int? cursor = null; // index of the back vertex of the most recent insert
                int totalVerticesAdded = 0;
                // perPriorInsert: list of (host_target, vertex_count) so we can
                // sum vertices contributed by prior inserts at host_target <= H.
                var priorByHostTarget = new List<(int hostTarget, int vertices)>();

                foreach (var ins in cand.Inserts)
                {
                    // Resolve part -> .lhlt
                    var (_, lhltRel) = StockLensCatalog.ResolvePart(ins.PartNumber, ins.Vendor);
                    string lhltPath = StockLensCatalog.ResolveLhltPath(lhltRel);
                    var stockSys = LhltReader.Read(lhltPath).System;

                    var vertices = LensInsertHelpers.ExtractLensVertices(stockSys, out string? extractErr);
                    if (vertices == null || vertices.Count == 0)
                        throw new InvalidOperationException(
                            $"insert {ins.PartNumber}: no lens vertices ({extractErr ?? "empty"}).");
                    if (ins.Reversed) vertices = LensInsertHelpers.ReverseVertexGroup(vertices);

                    // Determine where this insert lands in CURRENT numbering.
                    int insertAfterCurrent;
                    int hostTarget;
                    bool isExplicit = ins.AfterSurface.HasValue;
                    if (isExplicit)
                    {
                        hostTarget = ins.AfterSurface!.Value;
                        int verticesAddedByPriorAtOrBefore = priorByHostTarget
                            .Where(t => t.hostTarget <= hostTarget)
                            .Sum(t => t.vertices);
                        insertAfterCurrent = hostTarget + verticesAddedByPriorAtOrBefore;
                    }
                    else if (cursor.HasValue)
                    {
                        // Sequential: right after the prior back vertex.
                        insertAfterCurrent = cursor.Value;
                        hostTarget = priorByHostTarget.Count > 0
                            ? priorByHostTarget[priorByHostTarget.Count - 1].hostTarget
                            : system.StopSurfaceIndex;
                    }
                    else
                    {
                        // First insert with no after_surface — go after the stop.
                        insertAfterCurrent = system.StopSurfaceIndex;
                        hostTarget         = system.StopSurfaceIndex;
                    }

                    // Mark the surface BEFORE the inserts as variable (leading air gap).
                    // Skip for floating-stop case: S1's thickness IS the entrance pupil
                    // which is already variable from the host setup.
                    if (insertAfterCurrent >= 0 && insertAfterCurrent < system.Surfaces.Count)
                    {
                        bool isFloatingStopFirstInsert =
                            (insertAfterCurrent == system.StopSurfaceIndex)
                            && cand.EntrancePupil.HasValue
                            && priorByHostTarget.Count == 0;
                        if (!isFloatingStopFirstInsert)
                            system.Surfaces[insertAfterCurrent].ThicknessVariable = true;
                    }

                    // Splice vertices in.
                    for (int i = 0; i < vertices.Count; i++)
                    {
                        int at = insertAfterCurrent + 1 + i;
                        system.Surfaces.Insert(at, vertices[i]);
                        LensHH.Core.Models.SurfaceIndexUpdater.OnSurfaceInserted(at, system, merit, config);
                    }
                    int lastInsertedIdx = insertAfterCurrent + vertices.Count;
                    // Override the trailing thickness with the agent's seed value, and
                    // mark variable (the rule: any air gap adjacent to an insert is variable).
                    system.Surfaces[lastInsertedIdx].Thickness = ins.AirThickness;
                    system.Surfaces[lastInsertedIdx].ThicknessVariable = true;

                    cursor = lastInsertedIdx;
                    priorByHostTarget.Add((hostTarget, vertices.Count));
                    totalVerticesAdded += vertices.Count;
                }

                for (int i = 0; i < system.Surfaces.Count; i++) system.Surfaces[i].Index = i;

                // Run inner optimizer.
                double initMerit = double.NaN, finalMerit = double.NaN; int iters = 0;
                if (merit == null || merit.Operands.Count == 0)
                    throw new InvalidOperationException("host .lhlt has no merit function operands.");

                if (data.InnerOptimizer == "lm")
                {
                    var lm = new LocalOptimizer(system, merit, session.GlassCatalog)
                    {
                        ParallelEvaluation       = true,
                        MaxIterations            = 4000,
                        Tolerance                = 1e-10,
                        InitialDamping           = 1e-3,
                        UseBroydenUpdate         = true,
                        BroydenRefreshInterval   = 5,
                    };
                    lm.CollectVariables();
                    var optResult = lm.Optimize(job.Cts.Token);
                    initMerit  = optResult.InitialMerit;
                    finalMerit = optResult.FinalMerit;
                    iters      = optResult.Iterations;
                }
                else
                {
                    var msSettings = new MultistartSettings { MaxTrials = 200, LmIterationsPerTrial = 50 };
                    var ms = new MultistartOptimizer(system, merit, session.GlassCatalog, config)
                    { Settings = msSettings };
                    var msResult = ms.Optimize(job.Cts.Token);
                    initMerit  = msResult.InitialMerit;
                    finalMerit = msResult.FinalMerit;
                    iters      = msResult.TrialsRun;
                }

                // Record optimized thicknesses (only variable surfaces).
                var optimized = new List<(int, double)>();
                for (int i = 0; i < system.Surfaces.Count; i++)
                    if (system.Surfaces[i].ThicknessVariable)
                        optimized.Add((i, system.Surfaces[i].Thickness));

                double finalEfl = double.NaN;
                try
                {
                    var r = LensHH.Core.Analysis.SystemDataCalculator.Calculate(system, session.GlassCatalog);
                    finalEfl = r.Efl;
                }
                catch { }

                // Stop-location analysis for case-a/b hosts (floating stop).
                StopLocation? stopLoc = null;
                if (cand.EntrancePupil.HasValue) stopLoc = AnalyzeStopLocation(system);

                lock (_lock)
                {
                    result.InitialMerit         = initMerit;
                    result.FinalMerit           = finalMerit;
                    result.Iterations           = iters;
                    result.OptimizedThicknesses = optimized;
                    result.FinalEfl             = finalEfl;
                    result.StopLocation         = stopLoc;
                    result.Status               = "ok";
                }
            }
            catch (OperationCanceledException)
            {
                lock (_lock) { result.Status = "cancelled"; }
                throw;
            }
            catch (Exception ex)
            {
                lock (_lock) { result.Status = "error"; result.Error = ex.Message; }
            }
            finally
            {
                int done = Interlocked.Increment(ref data.Done);
                job.Phase = $"running {done}/{data.Candidates.Count}";
            }
        }

        /// <summary>
        /// Locate the entrance pupil for a case-a/b host after LM converged.
        /// The host convention is S0=OBJ, S1=stop with thickness = entrance-pupil
        /// distance (signed). Walk forward summing z from S1 and find which
        /// inter-surface interval the pupil falls into. If that interval has
        /// glass material on it, the candidate is physically invalid.
        /// </summary>
        private static StopLocation AnalyzeStopLocation(OpticalSystem system)
        {
            int stopIdx = system.StopSurfaceIndex;
            if (stopIdx < 0 || stopIdx >= system.Surfaces.Count)
                return new StopLocation { S1Thickness = 0, Context = "no stop surface", BuriedInGlass = false };

            double s1T = system.Surfaces[stopIdx].Thickness;
            // Entrance pupil z relative to S1: just s1T (the thickness IS the offset).
            // Walk forward from S1 cumulatively; find which gap contains the pupil position.
            // A negative s1T means the pupil is behind S1 (toward OBJ) — between S0 and S1.
            if (s1T <= 0)
            {
                return new StopLocation
                {
                    S1Thickness = s1T,
                    Context     = $"in air {-s1T:0.###} mm before S{stopIdx + 1} (between S{stopIdx} and S{stopIdx + 1})",
                    BuriedInGlass = false,
                };
            }
            // Positive s1T: pupil is somewhere downstream of S1. Sum forward.
            double zRel = 0.0; // z relative to S1 going forward through subsequent surfaces
            for (int i = stopIdx; i < system.Surfaces.Count - 1; i++)
            {
                double t = system.Surfaces[i].Thickness;
                double zNext = zRel + t;
                // The pupil at z = s1T falls in the gap (zRel, zNext) on the OUT side of surface i.
                if (s1T >= zRel && s1T <= zNext)
                {
                    bool inGlass = !string.IsNullOrEmpty(system.Surfaces[i].Material)
                                && !system.Surfaces[i].Material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase);
                    string context = inGlass
                        ? $"INSIDE glass element after S{i} (material '{system.Surfaces[i].Material}'), S1.T = {s1T:0.###}"
                        : $"in air between S{i} and S{i + 1}, S1.T = {s1T:0.###}";
                    return new StopLocation
                    {
                        S1Thickness  = s1T,
                        Context      = context,
                        BuriedInGlass = inGlass,
                    };
                }
                zRel = zNext;
            }
            return new StopLocation
            {
                S1Thickness = s1T,
                Context     = $"past the last lens vertex, S1.T = {s1T:0.###}",
                BuriedInGlass = false,
            };
        }

        /// <summary>
        /// Materialise candidate `index` from the job into the session: clone
        /// the host, replay the inserts, set the optimized thicknesses. Leaves
        /// the result loaded as the session's current system.
        /// </summary>
        public void ApplyToSession(McpSession session, string jobId, int candidateIndex)
        {
            var data = GetData(jobId) ?? throw new InvalidOperationException($"No batch job with id '{jobId}'.");
            if (candidateIndex < 0 || candidateIndex >= data.Candidates.Count)
                throw new ArgumentOutOfRangeException(nameof(candidateIndex));
            var result = data.Results[candidateIndex];
            if (result.Status != "ok")
                throw new InvalidOperationException(
                    $"Candidate {candidateIndex} status is '{result.Status}'{(result.Error != null ? ": " + result.Error : "")}. Nothing to apply.");

            // Re-run the assembly to rebuild the system at the optimized state.
            session.LoadFromFile(data.HostPath); // resets session.System / Merit / Config
            var cand = data.Candidates[candidateIndex];

            if (cand.EntrancePupil.HasValue)
            {
                int stopIdx = session.System.StopSurfaceIndex;
                if (stopIdx > 0 && stopIdx < session.System.Surfaces.Count)
                    session.System.Surfaces[stopIdx].Thickness = cand.EntrancePupil.Value;
            }

            int? cursor = null;
            var priorByHostTarget = new List<(int hostTarget, int vertices)>();
            foreach (var ins in cand.Inserts)
            {
                var (_, lhltRel) = StockLensCatalog.ResolvePart(ins.PartNumber, ins.Vendor);
                string lhltPath = StockLensCatalog.ResolveLhltPath(lhltRel);
                var stockSys = LhltReader.Read(lhltPath).System;
                var vertices = LensInsertHelpers.ExtractLensVertices(stockSys, out _);
                if (vertices == null || vertices.Count == 0) continue;
                if (ins.Reversed) vertices = LensInsertHelpers.ReverseVertexGroup(vertices);

                int insertAfterCurrent;
                int hostTarget;
                if (ins.AfterSurface.HasValue)
                {
                    hostTarget = ins.AfterSurface.Value;
                    int added = priorByHostTarget.Where(t => t.hostTarget <= hostTarget).Sum(t => t.vertices);
                    insertAfterCurrent = hostTarget + added;
                }
                else if (cursor.HasValue)
                {
                    insertAfterCurrent = cursor.Value;
                    hostTarget = priorByHostTarget.Count > 0 ? priorByHostTarget[^1].hostTarget : session.System.StopSurfaceIndex;
                }
                else
                {
                    insertAfterCurrent = session.System.StopSurfaceIndex;
                    hostTarget         = session.System.StopSurfaceIndex;
                }

                if (insertAfterCurrent >= 0 && insertAfterCurrent < session.System.Surfaces.Count)
                {
                    bool isFloatingStopFirstInsert =
                        (insertAfterCurrent == session.System.StopSurfaceIndex)
                        && cand.EntrancePupil.HasValue
                        && priorByHostTarget.Count == 0;
                    if (!isFloatingStopFirstInsert)
                        session.System.Surfaces[insertAfterCurrent].ThicknessVariable = true;
                }
                for (int i = 0; i < vertices.Count; i++)
                {
                    int at = insertAfterCurrent + 1 + i;
                    session.System.Surfaces.Insert(at, vertices[i]);
                    LensHH.Core.Models.SurfaceIndexUpdater.OnSurfaceInserted(at, session.System, session.MeritFunction, session.ConfigEditor);
                }
                int lastInsertedIdx = insertAfterCurrent + vertices.Count;
                session.System.Surfaces[lastInsertedIdx].Thickness = ins.AirThickness;
                session.System.Surfaces[lastInsertedIdx].ThicknessVariable = true;

                cursor = lastInsertedIdx;
                priorByHostTarget.Add((hostTarget, vertices.Count));
            }
            for (int i = 0; i < session.System.Surfaces.Count; i++) session.System.Surfaces[i].Index = i;

            // Apply optimized thicknesses captured in the result row.
            foreach (var (surfIdx, t) in result.OptimizedThicknesses)
            {
                if (surfIdx >= 0 && surfIdx < session.System.Surfaces.Count)
                    session.System.Surfaces[surfIdx].Thickness = t;
            }
        }
    }
}
