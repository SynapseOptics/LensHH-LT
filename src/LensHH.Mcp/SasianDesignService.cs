using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LensHH.Core.IO;
using LensHH.Core.MeritFunction;
using LensHH.Core.Models;
using LensHH.Core.Optimization;
using Microsoft.Data.Sqlite;

namespace LensHH.Mcp
{
    // ── Public types exposed to the tools layer ──────────────────────────────

    /// <summary>One per-element substitution trial: a pattern + candidate
    /// part(s), and the post-reopt merit it achieved. Stored on the job for
    /// status reporting.</summary>
    public class SasianTrial
    {
        public int ElementIndex { get; set; }
        public string Pattern { get; set; } = "";
        public string PartsDescriptor { get; set; } = "";
        public double Merit { get; set; } = double.NaN;
        public string Status { get; set; } = "pending";
        public bool Winner { get; set; }
    }

    /// <summary>Per-job state owned by SasianDesignService.</summary>
    public class SasianJobData
    {
        public string JobId { get; set; } = "";
        public string TemplatePath { get; set; } = "";
        public string OutputDir { get; set; } = "";
        public string Architecture { get; set; } = "single-single-single";
        public int CandidatesPerPattern { get; set; } = 3;
        public string SubstitutionCatalog { get; set; } = "auto";
        public int StopPosition { get; set; } = 2;
        public double SemiDiameterSeed { get; set; } = 12.5;
        public double AirGapSeed { get; set; } = 10;
        public double BflSeed { get; set; } = 45;
        public int BhMaxHops { get; set; } = 2000;
        public int BhLmPerHop { get; set; } = 4000;
        public int BhHjPerHop { get; set; } = 30;

        /// <summary>No-improvement watchdog (seconds). When &gt; 0, each BH
        /// phase terminates if the best merit hasn't improved within this
        /// many seconds. Default 600 s (10 min) — practical termination
        /// criterion when BhMaxHops is set high. 0 = disabled.</summary>
        public double BhNoImprovementSeconds { get; set; } = 600.0;

        // Progress tracking
        public double FreeOptMerit { get; set; } = double.NaN;
        public int TotalElements { get; set; }
        public int CurrentElement { get; set; }
        public List<SasianTrial> Trials { get; } = new();
        public List<string> SavedFiles { get; } = new();
        public List<SasianTrial> Winners { get; } = new();
        public double FinalMerit { get; set; } = double.NaN;
        public string? Error { get; set; }
    }

    // ── Service ──────────────────────────────────────────────────────────────

    /// <summary>
    /// End-to-end Sasian-recipe orchestrator: build skeleton → free-optimize
    /// → for each element {try every (pattern, candidate), pick best, save}.
    /// Runs on a background Task; agent polls via sasian_design_status.
    /// </summary>
    public class SasianDesignService
    {
        private readonly Dictionary<string, SasianJobData> _data = new();
        private readonly object _lock = new();

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            WriteIndented = false,
        };

        public SasianJobData? GetData(string jobId)
        {
            lock (_lock) return _data.TryGetValue(jobId, out var d) ? d : null;
        }

        public void Discard(string jobId)
        {
            lock (_lock) _data.Remove(jobId);
        }

        public RunningJob Start(McpSession session, SasianJobData data)
        {
            if (!File.Exists(data.TemplatePath))
                throw new FileNotFoundException($"Template .lhlt not found: {data.TemplatePath}");
            if (string.IsNullOrWhiteSpace(data.OutputDir))
                throw new ArgumentException("OutputDir is required.");
            Directory.CreateDirectory(data.OutputDir);

            var job = new RunningJob(kind: "sasian_design");
            data.JobId = job.JobId;
            lock (_lock) _data[job.JobId] = data;
            session.AddJob(job);
            job.Phase = "starting";

            // Kick off the pipeline. Single worker — sasian doesn't parallelize.
            Task.Run(() => RunPipeline(data, session, job));

            return job;
        }

        // ── Pipeline ────────────────────────────────────────────────────────

        private void RunPipeline(SasianJobData data, McpSession session, RunningJob job)
        {
            try
            {
                // Phase A: load template + build skeleton
                job.Phase = "building skeleton";
                LoadTemplate(session, data.TemplatePath);
                BuildSkeletonInline(session, data);
                job.Cts.Token.ThrowIfCancellationRequested();

                // Phase B: free-optimize
                job.Phase = "free-optimizing";
                RunOptimization(session, data, job.Cts.Token);
                data.FreeOptMerit = EvalMerit(session);
                SaveIntermediate(session, data, "01_freeopt");

                // Phase C: per-element substitution.
                //
                // We CANNOT use a fixed positional loop (`for elem = 1..N`) because
                // a split substitution (split_pcx, split_pcc, split_pcx_pcc) inserts
                // MORE glass elements than it removes — so the second iteration's
                // "element 2" would target the split partner that was just inserted
                // (now a stock part), not the original skeleton element at L2.
                //
                // Fix: find the next unsubstituted element each iteration by
                // scanning for the next contiguous-glass block whose front
                // surface still has CurvatureVariable=true. build_skeleton
                // sets that flag on every skeleton element; replace_element
                // inserts stock parts with the flag clear. So the variable
                // flag IS the "is this element still skeleton?" signal.
                //
                // The "Element index" in the trial table is the iteration
                // count (1, 2, 3 for a 3-element skeleton), not the surface-
                // position index — keeping the user-visible labels stable
                // across split vs single substitution patterns.
                int initialSkeletonElements = CountElements(session.System!);
                if (initialSkeletonElements == 0)
                    throw new InvalidOperationException("skeleton produced zero elements — pipeline aborted.");
                data.TotalElements = initialSkeletonElements;

                int iteration = 0;
                while (true)
                {
                    job.Cts.Token.ThrowIfCancellationRequested();

                    int targetElem = FindNextUnsubstitutedElement(session.System!);
                    if (targetElem < 0) break; // every skeleton element has been substituted

                    iteration++;
                    data.CurrentElement = iteration;
                    job.Phase = $"substituting element {iteration}/{initialSkeletonElements}";

                    // Snapshot the current best state
                    var snapshot = SerializeSystem(session);

                    // Compute target params for the element at its CURRENT
                    // surface-position index (may differ from `iteration` after
                    // a split substitution shifted things).
                    var target = ComputeElementTarget(session.System!, targetElem);

                    // Try every (pattern, candidate) combo
                    var trials = new List<(SasianTrial t, string snap)>();
                    foreach (var pattern in PickPatternsFor(target.Efl))
                    {
                        var candidates = FindMatchingStockInline(target, pattern, data.CandidatesPerPattern);
                        foreach (var cand in candidates)
                        {
                            job.Cts.Token.ThrowIfCancellationRequested();
                            var trial = new SasianTrial
                            {
                                ElementIndex = iteration,
                                Pattern = pattern,
                                PartsDescriptor = cand.Descriptor,
                            };
                            data.Trials.Add(trial);

                            try
                            {
                                // restore snapshot, find target again (snapshot restored
                                // the pre-trial state so targetElem from before is correct),
                                // splice candidate, re-optimize.
                                DeserializeSystem(session, snapshot);
                                ReplaceElementInline(session, targetElem, cand);
                                RunOptimization(session, data, job.Cts.Token);
                                double m = EvalMerit(session);
                                trial.Merit = m;
                                trial.Status = "ok";
                                trials.Add((trial, SerializeSystem(session)));
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex)
                            {
                                trial.Status = "error: " + ex.Message;
                            }
                        }
                    }

                    if (trials.Count == 0)
                    {
                        // nothing worked for this element — leave it as-is and continue
                        // (rare; happens if the catalog has nothing at this aperture/efl).
                        // We must break to avoid an infinite loop, because the same
                        // element will remain unsubstituted (still has CurvatureVariable=true)
                        // and FindNextUnsubstitutedElement would return the same index.
                        // Better: clear variable flags on this element so the scan moves
                        // past it, but for now just abort the substitution phase.
                        // (TODO: implement skip-and-continue)
                        break;
                    }

                    // Pick the best trial by merit, apply permanently
                    var best = trials.OrderBy(t => t.t.Merit).First();
                    best.t.Winner = true;
                    DeserializeSystem(session, best.snap);
                    data.Winners.Add(best.t);
                    SaveIntermediate(session, data,
                        $"{(iteration + 1):D2}_E{iteration}_{SanitizeFilename(best.t.PartsDescriptor)}_merit{best.t.Merit:F4}");
                }

                // Phase D: done
                data.FinalMerit = EvalMerit(session);
                SaveIntermediate(session, data, $"{data.TotalElements + 2:D2}_final_allstock_merit{data.FinalMerit:F4}");
                job.Phase = "completed";
                job.Complete($"Sasian design complete. Final merit={data.FinalMerit:E4}.");
            }
            catch (OperationCanceledException)
            {
                job.Phase = "cancelled";
                job.Cancel();
            }
            catch (Exception ex)
            {
                job.Phase = "faulted";
                data.Error = ex.Message;
                job.Fault(ex);
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static void LoadTemplate(McpSession session, string path)
        {
            var result = LhltReader.Read(path);
            session.System = result.System;
            session.MeritFunction = result.MeritFunction;
            session.ConfigEditor = result.ConfigEditor;
        }

        /// <summary>Build a Cooke-triplet skeleton on the loaded template
        /// using the physically-realizable-stop convention: drops the template
        /// dummy stop, inserts the three singlets in sequence, then inserts a
        /// real flat IsStop surface in the air gap selected by data.StopPosition.
        /// Mirrors SurfaceTools.BuildSkeleton; kept inline so the orchestrator
        /// doesn't depend on tool-class lifetimes.</summary>
        private static void BuildSkeletonInline(McpSession session, SasianJobData data)
        {
            var sys = session.System!;

            int numElements = 3;
            int stopPosition = Math.Max(0, Math.Min(numElements, data.StopPosition));

            // Drop the template's dummy stop, preserving OBJ→(first refractive) distance.
            int oldStopIdx = sys.StopSurfaceIndex;
            double origObjT   = sys.Surfaces[0].Thickness;
            double origDummyT = sys.Surfaces[oldStopIdx].Thickness;
            sys.Surfaces.RemoveAt(oldStopIdx);
            sys.Surfaces[0].Thickness = origObjT + origDummyT;
            for (int i = 0; i < sys.Surfaces.Count; i++) sys.Surfaces[i].Index = i;
            SurfaceIndexUpdater.OnSurfaceRemoved(oldStopIdx, sys, session.MeritFunction, session.ConfigEditor);

            // Insert the three singlets in sequence (no stop yet).
            var ops = new (double r1, double r2, double t, string mat, double airAfter)[]
            {
                (+50, -50, 4, "N-BK7",  data.AirGapSeed),
                (-30, +30, 3, "N-SF11", data.AirGapSeed),
                (+50, -50, 4, "N-BK7",  data.BflSeed),
            };

            int afterSurface = 0; // start inserting right after OBJ
            var glassSurfaces = new List<int>();
            var elementBacks = new List<int>();
            foreach (var op in ops)
            {
                var front = new Surface
                {
                    Radius = op.r1, Thickness = op.t, Material = op.mat,
                    SemiDiameter = data.SemiDiameterSeed,
                    SemiDiameterMode = LensHH.Core.Enums.SemiDiameterMode.Auto,
                    CurvatureVariable = true, ThicknessVariable = true,
                };
                var back = new Surface
                {
                    Radius = op.r2, Thickness = op.airAfter, Material = "",
                    SemiDiameter = data.SemiDiameterSeed,
                    SemiDiameterMode = LensHH.Core.Enums.SemiDiameterMode.Auto,
                    CurvatureVariable = true, ThicknessVariable = true,
                };
                int frontIdx = afterSurface + 1;
                sys.Surfaces.Insert(frontIdx, front);
                SurfaceIndexUpdater.OnSurfaceInserted(frontIdx, sys, session.MeritFunction, session.ConfigEditor);
                int backIdx = frontIdx + 1;
                sys.Surfaces.Insert(backIdx, back);
                SurfaceIndexUpdater.OnSurfaceInserted(backIdx, sys, session.MeritFunction, session.ConfigEditor);
                glassSurfaces.Add(frontIdx);
                elementBacks.Add(backIdx);
                afterSurface = backIdx;
            }
            for (int i = 0; i < sys.Surfaces.Count; i++) sys.Surfaces[i].Index = i;

            // Insert the physical stop into the chosen air gap.
            int prevSurfIdx = (stopPosition == 0) ? 0 : elementBacks[stopPosition - 1];
            double origGap = sys.Surfaces[prevSurfIdx].Thickness;
            double leadingAir, trailingAir;
            if (double.IsInfinity(origGap))
            {
                leadingAir  = origGap;
                trailingAir = data.AirGapSeed;
            }
            else
            {
                leadingAir  = origGap / 2.0;
                trailingAir = origGap / 2.0;
            }

            int stopInsertAt = prevSurfIdx + 1;
            sys.Surfaces[prevSurfIdx].Thickness = leadingAir;
            var stopS = new Surface
            {
                Type             = LensHH.Core.Enums.SurfaceType.Standard,
                Radius           = 1e18,
                Thickness        = trailingAir,
                Material         = "",
                SemiDiameter     = data.SemiDiameterSeed,
                SemiDiameterMode = LensHH.Core.Enums.SemiDiameterMode.Auto,
                Conic            = 0,
                IsStop           = true,
                ThicknessVariable = true,
            };
            sys.Surfaces.Insert(stopInsertAt, stopS);
            SurfaceIndexUpdater.OnSurfaceInserted(stopInsertAt, sys, session.MeritFunction, session.ConfigEditor);
            for (int i = 0; i < sys.Surfaces.Count; i++) sys.Surfaces[i].Index = i;

            if (!double.IsInfinity(sys.Surfaces[prevSurfIdx].Thickness))
                sys.Surfaces[prevSurfIdx].ThicknessVariable = true;

            // Glass-surface indices shift +1 if they're at or after the stop insert.
            for (int i = 0; i < glassSurfaces.Count; i++)
                if (glassSurfaces[i] >= stopInsertAt) glassSurfaces[i]++;

            // Resolve substitutionCatalog (auto-detect UV vs Visible).
            string resolvedCatalog = data.SubstitutionCatalog;
            if (string.Equals(resolvedCatalog?.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
            {
                double minWl = (sys.Wavelengths != null && sys.Wavelengths.Count > 0)
                    ? sys.Wavelengths.Min(w => w.Value) : 0.587;
                resolvedCatalog = minWl < 0.380 ? "StockGlassesUV" : "StockGlassesVisible";
            }

            if (!string.IsNullOrWhiteSpace(resolvedCatalog))
            {
                foreach (var glassSurf in glassSurfaces)
                {
                    sys.GlassSubstitutions.Add(new GlassSubstitutionSetting
                    {
                        SurfaceIndex = glassSurf,
                        Substitute = true,
                        CatalogName = resolvedCatalog
                    });
                }
            }

            // Rewrite merit-function span operands Surface1=-3 → -5 (same logic
            // as the standalone build_skeleton).
            if (session.MeritFunction != null)
            {
                foreach (var op in session.MeritFunction.Operands)
                {
                    bool isSpan = op.Type == LensHH.Core.MeritFunction.OperandType.CTA
                               || op.Type == LensHH.Core.MeritFunction.OperandType.EA
                               || op.Type == LensHH.Core.MeritFunction.OperandType.CTG
                               || op.Type == LensHH.Core.MeritFunction.OperandType.EG;
                    if (isSpan && op.Surface1 == -3) op.Surface1 = -5;
                }
            }
        }

        /// <summary>
        /// Run the configured optimizer (Basin Hopping + LM) on the current
        /// session. Glass substitution is enabled throughout; BH's auto-detect
        /// gates eligible surfaces by whether the element has any reshaping
        /// variable (curvature / glass-thickness / conic / asphere) on its
        /// front or back face. Locked stock parts inserted by replace_element
        /// have those flags clear, so BH correctly skips substitution on them.
        /// Free-opt skeleton elements have the flags set by build_skeleton, so
        /// they remain eligible. No manual surface-tracking needed.
        /// </summary>
        private static void RunOptimization(McpSession session, SasianJobData data, CancellationToken ct)
        {
            // Resolve substitutionCatalog (auto-detect UV vs Visible).
            string resolvedCatalog = data.SubstitutionCatalog;
            if (string.Equals(resolvedCatalog?.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
            {
                double minWl = (session.System!.Wavelengths != null && session.System.Wavelengths.Count > 0)
                    ? session.System.Wavelengths.Min(w => w.Value) : 0.587;
                resolvedCatalog = minWl < 0.380 ? "StockGlassesUV" : "StockGlassesVisible";
            }

            var settings = new BasinHoppingSettings
            {
                MaxHops                     = data.BhMaxHops,
                LmIterationsPerHop          = data.BhLmPerHop,
                HjStepsPerHop               = data.BhHjPerHop,
                NoImprovementTimeoutSeconds = data.BhNoImprovementSeconds,
                GlassSubstitution           = !string.IsNullOrWhiteSpace(resolvedCatalog),
                OnlyPreferred               = false, // filtered catalogs are already curated
            };
            if (!string.IsNullOrWhiteSpace(resolvedCatalog))
                settings.GlassCatalogs.Add(resolvedCatalog);

            var bh = new BasinHoppingOptimizer(session.System!, session.MeritFunction!, session.GlassCatalog, session.ConfigEditor)
            { Settings = settings };
            bh.Optimize(ct);
        }

        private static double EvalMerit(McpSession session)
        {
            if (session.MeritFunction == null || session.MeritFunction.Operands.Count == 0) return double.NaN;
            var ev = new MeritFunctionEvaluator(session.System!, session.GlassCatalog) { ParallelEvaluation = true };
            return ev.Evaluate(session.MeritFunction);
        }

        private static int CountElements(OpticalSystem sys)
        {
            int n = 0;
            bool inGlass = false;
            foreach (var s in sys.Surfaces)
            {
                bool hasGlass = !string.IsNullOrEmpty(s.Material);
                if (hasGlass && !inGlass) { n++; inGlass = true; }
                else if (!hasGlass) inGlass = false;
            }
            return n;
        }

        /// <summary>
        /// Scan the system for the next glass element (contiguous run of
        /// non-air, non-MIRROR surfaces) whose front surface still has
        /// CurvatureVariable=true — the "is this still a free-opt skeleton
        /// element?" marker. Returns the 1-based element position-index
        /// in the current system, or -1 if every element has been
        /// substituted (no skeleton remains).
        ///
        /// Why this rule works: build_skeleton sets CurvatureVariable=true
        /// on every skeleton element's surfaces; replace_element inserts
        /// stock parts whose .lhlt files have CurvatureVariable=false on
        /// their glass surfaces. So the flag is a precise signal of
        /// "skeleton vs stock" status that survives across surface
        /// renumbering caused by split substitutions.
        /// </summary>
        private static int FindNextUnsubstitutedElement(OpticalSystem sys)
        {
            int? glassStart = null;
            int elemIdx = 0;
            for (int i = 0; i < sys.Surfaces.Count; i++)
            {
                bool hasGlass = !string.IsNullOrEmpty(sys.Surfaces[i].Material);
                if (hasGlass && glassStart == null) glassStart = i;
                else if (!hasGlass && glassStart != null)
                {
                    elemIdx++;
                    if (sys.Surfaces[glassStart.Value].CurvatureVariable)
                        return elemIdx;
                    glassStart = null;
                }
            }
            return -1;
        }

        /// <summary>Compute the (Efl, Nd, SemiDiameter) of a specific element
        /// in the current system. Element index is 1-based; element 1 is the
        /// first contiguous run of glass surfaces.</summary>
        private static (double Efl, double Nd, double SemiD) ComputeElementTarget(OpticalSystem sys, int elementIndex)
        {
            // Find element bounds
            int? glassStart = null;
            int found = 0;
            int firstSurf = -1, lastSurf = -1;
            for (int i = 0; i < sys.Surfaces.Count; i++)
            {
                bool hasGlass = !string.IsNullOrEmpty(sys.Surfaces[i].Material);
                if (hasGlass && glassStart == null) glassStart = i;
                else if (!hasGlass && glassStart != null)
                {
                    found++;
                    if (found == elementIndex) { firstSurf = glassStart.Value; lastSurf = i; break; }
                    glassStart = null;
                }
            }
            if (firstSurf < 0) return (0, 1.5, 12.5);

            // Thin-lens EFL across all glass surfaces in this run.
            // Use the lens-maker formula for the front and back surface radii
            // of the OVERALL element (ignoring cement interfaces for simplicity).
            // Glass index = primary wavelength refractive index of front glass.
            var firstGlassSurf = sys.Surfaces[firstSurf];
            string mat = firstGlassSurf.Material ?? "";
            double n = 1.5;
            // We don't have access to glass index here without GlassCatalog; the
            // caller will substitute nd-based filtering via the catalog. Use a
            // hardcoded default of 1.517 (N-BK7) when we can't resolve.
            // (A future enhancement: pass session.GlassCatalog through to
            //  resolve actual nd.)
            n = 1.517;

            double r1 = sys.Surfaces[firstSurf].Radius;
            double r2 = sys.Surfaces[lastSurf].Radius;
            double t = 0;
            for (int i = firstSurf; i < lastSurf; i++) t += sys.Surfaces[i].Thickness;

            // Thin-lens with thick correction
            double invF = (n - 1) * (1.0 / r1 - 1.0 / r2 + (n - 1) * t / (n * r1 * r2));
            double efl = invF == 0 ? 1e10 : 1.0 / invF;

            double semiD = Math.Max(sys.Surfaces[firstSurf].SemiDiameter,
                                    sys.Surfaces[lastSurf].SemiDiameter);
            if (semiD <= 0) semiD = 12.5;

            return (efl, n, semiD);
        }

        /// <summary>Pick which patterns to try based on the element's sign.
        /// Positive: single + split_pcx + split_pcx_pcc. Negative: single +
        /// split_pcc + split_pcx_pcc. Trying all 4 unconditionally just wastes
        /// time on patterns that can't match (e.g. split_pcc on a positive
        /// target).</summary>
        private static string[] PickPatternsFor(double efl)
        {
            if (efl > 0) return new[] { "single", "split_pcx", "split_pcx_pcc" };
            else         return new[] { "single", "split_pcc", "split_pcx_pcc" };
        }

        /// <summary>Inline find_matching_stock — returns structured candidates
        /// the orchestrator can hand to ReplaceElementInline. Same logic as the
        /// MCP tool with maxDiameter cap and excludeAspherics defaults.</summary>
        public class CandSpec
        {
            public string Descriptor { get; set; } = "";       // "L-PCX040" or "L-PCX333,L-PCX412:rev"
            public List<(string part, bool reversed)> Parts { get; set; } = new();
        }

        private List<CandSpec> FindMatchingStockInline((double Efl, double Nd, double SemiD) tgt, string pattern, int topN)
        {
            double minDiameter = 2.0 * tgt.SemiD;
            double maxDiameter = 2.0 * minDiameter;   // hard cap at 4× target semi-D
            double tolFrac = 0.15;

            // Pool catalog
            string dbPath = StockLensCatalog.ResolveDbPath();
            var pool = new List<StockLensRow>();
            using (var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "SELECT vendor, part_number, family, efl_mm, diameter_mm, nd_primary "
                    + "FROM stock_lenses WHERE import_status='ok' AND n_elements=1 "
                    + "AND diameter_mm >= @minD AND diameter_mm <= @maxD AND efl_mm IS NOT NULL";
                cmd.Parameters.AddWithValue("@minD", minDiameter);
                cmd.Parameters.AddWithValue("@maxD", maxDiameter);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    var fam = rdr.IsDBNull(2) ? "" : rdr.GetString(2);
                    if (fam.Contains("Aspheric", StringComparison.OrdinalIgnoreCase)) continue;
                    pool.Add(new StockLensRow
                    {
                        Vendor = rdr.IsDBNull(0) ? "" : rdr.GetString(0),
                        PartNumber = rdr.IsDBNull(1) ? "" : rdr.GetString(1),
                        Family = fam,
                        Efl = rdr.IsDBNull(3) ? 0 : rdr.GetDouble(3),
                        Diameter = rdr.IsDBNull(4) ? 0 : rdr.GetDouble(4),
                        Nd = rdr.IsDBNull(5) ? 0 : rdr.GetDouble(5),
                    });
                }
            }

            bool IsPcx(string f) =>
                   f.Contains("PCX", StringComparison.OrdinalIgnoreCase)
                || f.Contains("PlanoConvex", StringComparison.OrdinalIgnoreCase)
                || f.Contains("ThorLabs/LA", StringComparison.OrdinalIgnoreCase);
            bool IsPcc(string f) =>
                   f.Contains("PCC", StringComparison.OrdinalIgnoreCase)
                || f.Contains("PlanoConcave", StringComparison.OrdinalIgnoreCase)
                || f.Contains("ThorLabs/LC", StringComparison.OrdinalIgnoreCase)
                || f.Contains("ThorLabs/LD", StringComparison.OrdinalIgnoreCase)
                || f.Contains("ThorLabs/LF", StringComparison.OrdinalIgnoreCase);

            var results = new List<CandSpec>();

            if (pattern == "single")
            {
                double absT = Math.Abs(tgt.Efl);
                double minE = tgt.Efl - absT * tolFrac;
                double maxE = tgt.Efl + absT * tolFrac;
                if (maxE < minE) (minE, maxE) = (maxE, minE);
                var hits = pool
                    .Where(s => s.Efl >= minE && s.Efl <= maxE)
                    .OrderBy(s => Math.Abs(s.Efl - tgt.Efl) / absT + 5 * Math.Abs(s.Nd - tgt.Nd))
                    .Take(topN);
                foreach (var h in hits)
                    results.Add(new CandSpec
                    {
                        Descriptor = $"{h.PartNumber}",
                        Parts = new() { (h.PartNumber, false) }
                    });
                return results;
            }

            // Pair patterns
            List<StockLensRow> poolA, poolB;
            if (pattern == "split_pcx") { poolA = pool.Where(s => IsPcx(s.Family) && s.Efl > 0).ToList(); poolB = poolA; }
            else if (pattern == "split_pcc") { poolA = pool.Where(s => IsPcc(s.Family) && s.Efl < 0).ToList(); poolB = poolA; }
            else /* split_pcx_pcc */ { poolA = pool.Where(s => IsPcx(s.Family) && s.Efl > 0).ToList();
                                       poolB = pool.Where(s => IsPcc(s.Family) && s.Efl < 0).ToList(); }
            if (poolA.Count == 0 || poolB.Count == 0) return results;

            double absTarget = Math.Abs(tgt.Efl);
            if (absTarget < 1e-6) return results;
            bool symmetric = pattern == "split_pcx" || pattern == "split_pcc";

            var pairs = new List<(StockLensRow a, StockLensRow b, double err, double score)>();
            foreach (var a in poolA)
            {
                if (Math.Abs(a.Efl - tgt.Efl) < 1e-9) continue;
                double requiredB = tgt.Efl * a.Efl / (a.Efl - tgt.Efl);
                StockLensRow? best = null;
                double bestDiff = double.MaxValue;
                foreach (var b in poolB)
                {
                    if (symmetric && string.Compare(a.PartNumber + a.Vendor, b.PartNumber + b.Vendor,
                            StringComparison.Ordinal) > 0) continue;
                    double diff = Math.Abs(b.Efl - requiredB);
                    if (diff < bestDiff) { bestDiff = diff; best = b; }
                }
                if (best == null) continue;
                double combined = 1.0 / (1.0 / a.Efl + 1.0 / best.Efl);
                double err = Math.Abs(combined - tgt.Efl) / absTarget;
                if (err > tolFrac) continue;
                double glass = Math.Abs(a.Nd - tgt.Nd) + Math.Abs(best.Nd - tgt.Nd);
                double aperture = (a.Diameter > 0 && best.Diameter > 0)
                    ? Math.Abs(Math.Log(a.Diameter / best.Diameter, 2)) : 0;
                pairs.Add((a, best, err, err + 0.5 * glass + 0.25 * aperture));
            }
            var ranked = pairs.OrderBy(p => p.score).Take(topN);
            foreach (var (a, b, _, _) in ranked)
            {
                // For Sasian-style plano-to-plano, second lens is reversed.
                bool reverseSecond = pattern == "split_pcx" || pattern == "split_pcc" || pattern == "split_pcx_pcc";
                results.Add(new CandSpec
                {
                    Descriptor = $"{a.PartNumber}+{b.PartNumber}{(reverseSecond ? ":rev" : "")}",
                    Parts = new() { (a.PartNumber, false), (b.PartNumber, reverseSecond) }
                });
            }
            return results;
        }

        /// <summary>Inline replace_element: surgically remove the chosen element's
        /// surfaces and splice in the new part(s). Sets variable air-gap thicknesses
        /// (split-pair gap = 0.5 mm, trailing gap = airGapSeed).</summary>
        private static void ReplaceElementInline(McpSession session, int elementIndex, CandSpec cand)
        {
            var sys = session.System!;
            // Find element bounds
            int firstSurf = -1, lastSurf = -1;
            int? glassStart = null;
            int found = 0;
            for (int i = 0; i < sys.Surfaces.Count; i++)
            {
                bool hasGlass = !string.IsNullOrEmpty(sys.Surfaces[i].Material);
                if (hasGlass && glassStart == null) glassStart = i;
                else if (!hasGlass && glassStart != null)
                {
                    found++;
                    if (found == elementIndex) { firstSurf = glassStart.Value; lastSurf = i; break; }
                    glassStart = null;
                }
            }
            if (firstSurf < 0) throw new InvalidOperationException($"element {elementIndex} not found");
            int afterSurface = firstSurf - 1;

            // Remove old surfaces (highest first)
            for (int i = lastSurf; i >= firstSurf; i--)
            {
                sys.Surfaces.RemoveAt(i);
                SurfaceIndexUpdater.OnSurfaceRemoved(i, sys, session.MeritFunction, session.ConfigEditor);
            }

            // Splice new parts
            int insertAt = afterSurface + 1;
            for (int pIdx = 0; pIdx < cand.Parts.Count; pIdx++)
            {
                var (part, rev) = cand.Parts[pIdx];
                var (_, lhltRel) = StockLensCatalog.ResolvePart(part, null);
                string lhltPath = StockLensCatalog.ResolveLhltPath(lhltRel);
                var stockSys = LhltReader.Read(lhltPath).System;
                var verts = LensInsertHelpers.ExtractLensVertices(stockSys, out _);
                if (verts == null || verts.Count == 0)
                    throw new InvalidOperationException($"stock lens '{part}' has no vertices");
                if (rev) verts = LensInsertHelpers.ReverseVertexGroup(verts);
                for (int v = 0; v < verts.Count; v++)
                {
                    sys.Surfaces.Insert(insertAt + v, verts[v]);
                    SurfaceIndexUpdater.OnSurfaceInserted(insertAt + v, sys, session.MeritFunction, session.ConfigEditor);
                }
                int partLast = insertAt + verts.Count - 1;
                bool morePartsAfter = (pIdx + 1) < cand.Parts.Count;
                sys.Surfaces[partLast].Thickness = morePartsAfter ? 0.5 : 5.0;
                sys.Surfaces[partLast].ThicknessVariable = true;
                insertAt = partLast + 1;
            }

            for (int i = 0; i < sys.Surfaces.Count; i++) sys.Surfaces[i].Index = i;
        }

        private string SerializeSystem(McpSession session)
        {
            var file = LhltWriter.ToLhltFile(session.System!, session.MeritFunction, session.ConfigEditor);
            return JsonSerializer.Serialize(file, JsonOpts);
        }

        private void DeserializeSystem(McpSession session, string snapshot)
        {
            var file = JsonSerializer.Deserialize<LhltFile>(snapshot, JsonOpts)!;
            var result = LhltReader.FromLhltFile(file);
            session.System = result.System;
            session.MeritFunction = result.MeritFunction;
            session.ConfigEditor = result.ConfigEditor;
        }

        private static void SaveIntermediate(McpSession session, SasianJobData data, string baseName)
        {
            string path = Path.Combine(data.OutputDir, baseName + ".lhlt");
            var file = LhltWriter.ToLhltFile(session.System!, session.MeritFunction, session.ConfigEditor);
            string json = JsonSerializer.Serialize(file, new JsonSerializerOptions
            {
                WriteIndented = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            });
            File.WriteAllText(path, json);
            data.SavedFiles.Add(path);
        }

        private static string SanitizeFilename(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '+' ? c : '_');
            return sb.ToString();
        }
    }

    /// <summary>Lightweight DTO for the SQLite query in FindMatchingStockInline.</summary>
    internal class StockLensRow
    {
        public string Vendor = "";
        public string PartNumber = "";
        public string Family = "";
        public double Efl;
        public double Diameter;
        public double Nd;
    }
}
