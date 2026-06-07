// MeritEvalBench — merit-function evaluation timing (1.0.117).
//
// Times the primitives the optimizer is built from, with NO LM loop:
//
//   --mode value      whole-merit value (NO derivatives), as THROUGHPUT
//                     (designs/sec), measured the way the work is actually
//                     parallelized:
//                       C# 1-thread          (reference, one design at a time)
//                       C# all-cores         (Parallel.For over a batch — the
//                                             CPU baseline the product ships)
//                       C++ all-cores        (per-call across cores; includes
//                                             the C#->C++ marshal per design)
//   --mode jacobian   merit WITH derivatives: C# FD, C++ FD, C++ Analytic
//                     (one call = residuals + full Jacobian; per-eval ms)
//   --mode gpu        whole-merit GPU value kernel, swept at the device-fill
//                     batch size (read from the driver, not hardcoded) and 2x
//   --mode all        all of the above (default)
//
// Everything in `value`/`gpu` is reported PER DESIGN (ms/design + designs/sec)
// so CPU and GPU compare directly, and the headline is GPU / best-CPU-parallel.
//
// Thread count is Environment.ProcessorCount (logical processors), never
// hardcoded. GPU-fill is queried from the driver's occupancy for THIS kernel
// on THIS device. C++/GPU cells degrade gracefully when the native lib / CUDA
// is absent, so the C# cells still produce numbers everywhere.
//
// Usage:
//   MeritEvalBench --lens lens1.lhlt [lens2.lhlt ...]
//                  [--mode value|jacobian|gpu|all]   default all
//                  [--seconds 5]
//                  [--cpu-batch N]    designs/parallel-call (default = GPU fill)
//                  [--csv out.csv]

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LensHH.Core.Glass;
using LensHH.Core.IO;
using LensHH.Core.MeritFunction;
using LensHH.Core.Models;
using LensHH.Core.NativeInterop;
using LensHH.Core.Optimization;
using MfMeritFunction = LensHH.Core.MeritFunction.MeritFunction;

namespace MeritEvalBench;

internal static class Program
{
    [DllImport("lenshh_native", CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "lenshh_gpu_last_cuda_error")]
    private static extern int lenshh_gpu_last_cuda_error();
    // GPU-fill design count (driver occupancy query); 0 if GPU/kernel absent.
    [DllImport("lenshh_native", CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "lenshh_gpu_merit_value_fill_count")]
    private static extern int lenshh_gpu_merit_value_fill_count();
    // FP32-vs-FP64 ray-trace microbench. Returns 0 on success; out_elapsed_ms =
    // steady-state kernel wall time for launch_repeats launches at the requested
    // precision (use_double: 0=float, 1=double). See cuda_run_trace_microbench.
    [DllImport("lenshh_native", CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "lenshh_gpu_run_trace_microbench")]
    private static extern int lenshh_gpu_run_trace_microbench(
        int use_double, int num_rays, int num_surf, int iters, int launch_repeats,
        out double out_elapsed_ms, out double out_checksum);

    private static int Threads => Math.Max(1, Environment.ProcessorCount);

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var lenses = new List<string>();
        string mode = "all";
        double seconds = 5;
        int cpuBatchOverride = 0;       // 0 = use GPU fill (or fallback)
        string csvOut = string.Empty;
        // tracebench knobs (FP32-vs-FP64 microbench).
        int tbRays = 1 << 20, tbSurf = 6, tbIters = 200, tbRepeats = 30;

        for (int i = 0; i < args.Length; ++i)
        {
            string a = args[i];
            if (a == "--lens")
                while (i + 1 < args.Length && !args[i + 1].StartsWith("--")) lenses.Add(args[++i]);
            else if (a == "--mode") mode = args[++i].ToLowerInvariant();
            else if (a == "--seconds") seconds = double.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (a == "--cpu-batch") cpuBatchOverride = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (a == "--csv") csvOut = args[++i];
            else if (a == "--rays") tbRays = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (a == "--surfaces") tbSurf = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (a == "--iters") tbIters = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (a == "--repeats") tbRepeats = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (a == "--help" || a == "-h") { PrintUsage(); return 0; }
        }
        if (mode is not ("value" or "jacobian" or "gpu" or "all" or "prescreen" or "tracebench"))
        { Console.Error.WriteLine($"ERROR: --mode must be value|jacobian|gpu|all|prescreen|tracebench (was {mode})"); return 1; }
        // tracebench is a synthetic device trace — it needs no lens.
        if (lenses.Count == 0 && mode != "tracebench")
        { Console.Error.WriteLine("ERROR: no --lens specified."); PrintUsage(); return 1; }

        NativeEngine.EnsureInitialized();
        bool nativeOk = NativeEngine.Available;
        string activation = "n/a (native engine unavailable)";
        if (nativeOk)
        {
            if (LensHH.Core.Activation.ActivationManager.TryLoadExistingActivation())
            {
                var lic = LensHH.Core.Activation.ActivationManager.GetLicenseInfo();
                activation = lic != null
                    ? $"license token ({lic.LicenseType}, {lic.DaysUntilExpiry} days left)"
                    : "license token";
            }
            else if (LensHH.Core.Activation.TrialClock.TryActivateTrial())
                activation = $"trial ({LensHH.Core.Activation.TrialClock.DaysRemaining} days left)";
            else
                activation = "⚠ NOT ACTIVATED — C++/GPU cells will fail";
        }

        var catalogsRoot = Path.Combine(AppContext.BaseDirectory, "catalogs");
        var glassMgr = new GlassCatalogManager();
        glassMgr.LoadCatalogsFromFolder(Path.Combine(catalogsRoot, "Glass"));
        glassMgr.LoadCatalogsFromFolder(Path.Combine(catalogsRoot, "FilteredGlassCatalogues"));

        // GPU-fill design count (driver occupancy). 0 when no GPU/kernel.
        int gpuFill = 0;
        if (nativeOk && GpuPreScreener.IsAvailable)
        {
            try { gpuFill = lenshh_gpu_merit_value_fill_count(); } catch { gpuFill = 0; }
        }
        // Common batch for the parallel CPU + GPU cells, so they compare at the
        // same N. Default to the GPU-fill (the meaningful size); fall back to a
        // multiple of the logical-processor count when there's no GPU.
        int cpuBatch = cpuBatchOverride > 0 ? cpuBatchOverride
                     : gpuFill > 0 ? gpuFill
                     : Threads * 512;

        Console.WriteLine("════════════════════════════════════════════════════════════════════");
        Console.WriteLine(" MeritEvalBench — merit-evaluation timing (per design)");
        Console.WriteLine($" Mode: {mode}   Window: {seconds:F0}s/cell");
        Console.WriteLine($" Machine:  {Environment.MachineName}   OS: {RuntimeInformation.OSDescription}");
        Console.WriteLine($" Arch:     {RuntimeInformation.ProcessArchitecture}   Logical processors: {Threads}");
        Console.WriteLine($" Native:   {(nativeOk ? NativeEngine.Version : $"UNAVAILABLE — {NativeEngine.InitError}")}");
        Console.WriteLine($" License:  {activation}");
        if (gpuFill > 0)
        {
            var caps = NativeMeritEngine.GetGpuCapabilities();
            int perSm = caps.SmCount > 0 ? gpuFill / caps.SmCount : 0;
            Console.WriteLine($" GPU-fill: {gpuFill:N0} designs  ({caps.SmCount} SM × {perSm} threads/SM, " +
                              $"driver occupancy for the merit kernel)");
        }
        else if (nativeOk)
            Console.WriteLine(" GPU-fill: n/a (no CUDA device)");
        Console.WriteLine($" CPU batch (parallel cells): {cpuBatch:N0} designs");
        Console.WriteLine("════════════════════════════════════════════════════════════════════");

        // tracebench: lens-independent FP32-vs-FP64 trace throughput. Run and exit.
        if (mode == "tracebench")
        {
            RunTraceBench(nativeOk, tbRays, tbSurf, tbIters, tbRepeats);
            Console.WriteLine("\n Done.");
            return 0;
        }

        var csv = new List<string>
        {
            "machine,os,lens,mode,engine,threads,batch,calls,ms_per_design,designs_per_sec,merit,status"
        };

        foreach (var lensPath in lenses)
        {
            if (!File.Exists(lensPath)) { Console.WriteLine($"\n⚠ SKIPPING — not found: {lensPath}"); continue; }
            string name = Path.GetFileName(lensPath);
            try
            {
                PrintLensInfo(lensPath, glassMgr);
                double cpuBaseline = 0;
                if (mode is "value" or "all")    cpuBaseline = RunValue(lensPath, name, glassMgr, nativeOk, seconds, cpuBatch, csv);
                if (mode is "jacobian" or "all") RunJacobian(lensPath, name, glassMgr, nativeOk, seconds, csv);
                if (mode is "gpu" or "all")      RunGpu(lensPath, name, glassMgr, nativeOk, seconds, gpuFill, cpuBaseline, csv);
                if (mode is "prescreen")         RunPreScreenCheck(lensPath, name, glassMgr, gpuFill, seconds);
            }
            catch (Exception ex) { Console.WriteLine($"⚠ {name}: {ex.GetType().Name}: {ex.Message}"); }
        }

        if (!string.IsNullOrEmpty(csvOut))
        {
            File.WriteAllLines(csvOut, csv);
            Console.WriteLine($"\nCSV written: {Path.GetFullPath(csvOut)}");
        }
        Console.WriteLine("\n Done.");
        return 0;
    }

    // ── Throughput timer: call evalBatch (which processes batchN designs)
    //    repeatedly for ~seconds; report ms/design + designs/sec. ───────────────
    private sealed record Tput(long Designs, double Sec, int Calls)
    {
        public double DesignsPerSec => Sec > 0 ? Designs / Sec : 0;
        public double MsPerDesign  => Designs > 0 ? Sec * 1000.0 / Designs : 0;
    }
    private static Tput TimeThroughput(Action evalBatch, int batchN, double seconds, int minCalls = 3)
    {
        long designs = 0; int calls = 0;
        var sw = Stopwatch.StartNew();
        while (calls < minCalls || sw.Elapsed.TotalSeconds < seconds)
        {
            evalBatch();
            designs += batchN; calls++;
        }
        sw.Stop();
        return new Tput(designs, sw.Elapsed.TotalSeconds, calls);
    }

    // ── value: whole-merit value throughput (designs/sec). Returns the
    //    C# all-cores designs/sec (the fair CPU baseline for the GPU ratio). ─────
    private static double RunValue(string lensPath, string name, GlassCatalogManager glassMgr,
        bool nativeOk, double seconds, int cpuBatch, List<string> csv)
    {
        Console.WriteLine(" [value] whole-merit value throughput — designs/sec (higher = better)");
        double cpuParallelDps = 0;   // C# all-cores
        double cppParallelDps = 0;   // C++ all-cores (per-call)

        // C# 1-thread reference.
        {
            var read = LhltReader.Read(lensPath);
            var (sys, mf) = (read.System, read.MeritFunction!);
            var eval = new MeritFunctionEvaluator(sys, glassMgr) { ParallelEvaluation = false };
            double merit = eval.Evaluate(mf);
            var t = TimeThroughput(() => eval.GetResiduals(mf), 1, seconds);
            Report("C#", 1, 1, t, merit, csv, name, "value");
        }

        // C# all-cores: Parallel.For over a batch, each thread its own evaluator
        // (own system read) so the SD/pickup mutation doesn't race.
        {
            var pool = new ThreadLocal<(MeritFunctionEvaluator eval, MfMeritFunction mf)>(() =>
            {
                var r = LhltReader.Read(lensPath);
                return (new MeritFunctionEvaluator(r.System, glassMgr) { ParallelEvaluation = false },
                        r.MeritFunction!);
            }, trackAllValues: false);
            var po = new ParallelOptions { MaxDegreeOfParallelism = Threads };
            try
            {
                // WARM-UP (untimed): the ThreadLocal factory reads+parses the
                // lens once per worker thread (cold file I/O + JSON), and JIT
                // happens here too. Without this, that one-time cost lands
                // inside the timed window and — with few calls on a slow build —
                // crushes the parallel number. Run a full batch once, untimed.
                Parallel.For(0, cpuBatch, po, _ => { var w = pool.Value; w.eval.GetResiduals(w.mf); });
                var t = TimeThroughput(() =>
                    Parallel.For(0, cpuBatch, po, _ => { var w = pool.Value; w.eval.GetResiduals(w.mf); }),
                    cpuBatch, seconds);
                cpuParallelDps = t.DesignsPerSec;
                Report("C#", Threads, cpuBatch, t, double.NaN, csv, name, "value");
            }
            finally { pool.Dispose(); }
        }

        // C++ all-cores: per-call across cores. Each thread its own
        // NativeMeritEngine (own system) — stateful, so not shared. Includes the
        // C#->C++ marshal PER design (the real bridge cost; the batched optimizer
        // path amortizes it, so this is a conservative floor for native value).
        if (nativeOk)
        {
            var pool = new ThreadLocal<(NativeMeritEngine eng, MfMeritFunction mf)>(() =>
            {
                var r = LhltReader.Read(lensPath);
                return (new NativeMeritEngine(r.System, glassMgr), r.MeritFunction!);
            }, trackAllValues: false);
            var po = new ParallelOptions { MaxDegreeOfParallelism = Threads };
            try
            {
                // Warm-up (untimed): per-thread NativeMeritEngine + lens read + JIT.
                Parallel.For(0, cpuBatch, po, _ => { var w = pool.Value; w.eng.GetResiduals(w.mf); });
                var t = TimeThroughput(() =>
                    Parallel.For(0, cpuBatch, po, _ => { var w = pool.Value; w.eng.GetResiduals(w.mf); }),
                    cpuBatch, seconds);
                cppParallelDps = t.DesignsPerSec;
                Report("C++ (per-call)", Threads, cpuBatch, t, double.NaN, csv, name, "value");
            }
            catch (Exception ex) { ReportFail("C++ (per-call)", Threads, ex, csv, name, "value"); }
            finally { pool.Dispose(); }
        }
        else Console.WriteLine("  ▸ C++              ⚠ native engine unavailable");

        // Headline baseline = the BEST CPU-all-cores cell. On the shipped
        // (obfuscated) engine the pure-C# eval is ~2× slower and barely scales
        // (Reactor protections serialize managed parallel work), so C++ wins;
        // on an unobfuscated dev build C# wins. Taking the max keeps the
        // GPU÷CPU headline honest either way.
        double best = Math.Max(cpuParallelDps, cppParallelDps);
        if (best > 0)
        {
            string which = cppParallelDps >= cpuParallelDps ? "C++" : "C#";
            Console.WriteLine($"        → best CPU all-cores baseline: {best:N0} designs/sec ({which}) " +
                              $"— GPU rows compare against this");
        }
        return best;
    }

    // ── jacobian: residuals + full Jacobian (per-eval ms) ──────────────────────
    private static void RunJacobian(string lensPath, string name, GlassCatalogManager glassMgr,
        bool nativeOk, double seconds, List<string> csv)
    {
        Console.WriteLine(" [jacobian] residuals + full Jacobian — one fresh-J LM iteration (ms/eval)");
        foreach (var (engine, deriv) in new[] { ("C#", "FD"), ("C++", "FD"), ("C++", "Analytic") })
        {
            if (engine == "C++" && !nativeOk) { Console.WriteLine($"  ▸ C++  | {deriv,-8}  ⚠ native unavailable"); continue; }
            var read = LhltReader.Read(lensPath);
            var opt = new LocalOptimizer(read.System, read.MeritFunction!, glassMgr)
            {
                EngineMode = engine == "C++" ? EngineMode.Native : EngineMode.CSharp,
                NativeDerivativeMode = deriv == "Analytic" ? MeritDerivativeMode.Analytic : MeritDerivativeMode.FiniteDifference,
            };
            try
            {
                var warm = opt.ComputeJacobianOnce();
                int nRes = warm.GetLength(0), nVars = warm.GetLength(1);
                long n = 0; var sw = Stopwatch.StartNew();
                while (n < 3 || sw.Elapsed.TotalSeconds < seconds) { opt.ComputeJacobianOnce(nRes); n++; }
                sw.Stop();
                double msEach = sw.Elapsed.TotalMilliseconds / n;
                Console.WriteLine($"  ▸ {engine,-4} | {deriv,-8}  {msEach,8:F3} ms/eval  {1000.0/msEach,8:N1} evals/s   (J=[{nRes}×{nVars}], {n} evals)");
                csv.Add(Csv2(name, "jacobian", $"{engine}/{deriv}", nVars, 0, (int)n, msEach, msEach > 0 ? 1000.0/msEach : 0, double.NaN, "OK"));
            }
            catch (Exception ex) { ReportFail($"{engine}/{deriv}", 0, ex, csv, name, "jacobian"); }
        }
    }

    // ── gpu: whole-merit value kernel at the device-fill batch size ────────────
    private static void RunGpu(string lensPath, string name, GlassCatalogManager glassMgr,
        bool nativeOk, double seconds, int gpuFill, double cpuParallelDps, List<string> csv)
    {
        Console.WriteLine(" [gpu] whole-merit GPU value kernel — designs/sec at device-fill batch");
        if (!nativeOk) { Console.WriteLine("  ▸ GPU  ⚠ native engine unavailable"); return; }
        if (!GpuPreScreener.IsAvailable) { Console.WriteLine("  ▸ GPU  ⚠ no CUDA device (expected on macOS) — skipped"); return; }
        if (gpuFill <= 0) { Console.WriteLine("  ▸ GPU  ⚠ could not query device-fill — skipped"); return; }

        var read = LhltReader.Read(lensPath);
        var (system, mf) = (read.System, read.MeritFunction!);
        try { LensHH.Core.Analysis.PickupSolver.Solve(system); } catch { }
        try { LensHH.Core.Analysis.SemiDiameterSolver.Solve(system, glassMgr); } catch { }

        var surfaces = NativeMarshaling.ToNativeSurfaces(system);
        var config = NativeMarshaling.ToNativeConfig(system);
        var fields = NativeMarshaling.ToNativeFields(system);
        int nSurf = system.Surfaces.Count, nWl = system.Wavelengths.Count;
        var wavelengthsUm = new double[nWl];
        for (int w = 0; w < nWl; ++w) wavelengthsUm[w] = system.Wavelengths[w].Value;

        var indPerWl = new double[nWl][];
        for (int w = 0; w < nWl; ++w) indPerWl[w] = glassMgr.BuildRefractiveIndexArray(system, system.Wavelengths[w].Value);
        var centerIds = new int[nSurf];
        var rows = new List<double[]>();
        for (int i = 0; i < nSurf; ++i)
        {
            if (string.IsNullOrEmpty(system.Surfaces[i].Material)) { centerIds[i] = -1; continue; }
            var row = new double[nWl];
            for (int w = 0; w < nWl; ++w) row[w] = indPerWl[w][i];
            centerIds[i] = rows.Count; rows.Add(row);
        }
        var catalog = new double[Math.Max(1, rows.Count) * nWl];
        for (int r = 0; r < rows.Count; ++r) for (int w = 0; w < nWl; ++w) catalog[r * nWl + w] = rows[r][w];

        var evaluator = new MeritFunctionEvaluator(system, glassMgr);
        var expanded = evaluator.ExpandMacros(mf);
        var operands = new MeritOperandDesc[expanded.Count];
        for (int i = 0; i < expanded.Count; ++i) operands[i] = MeritWorkspace.ToOperandDesc(expanded[i]);
        MeritWorkspace.StampSourceMacroWeights(operands, expanded, mf.Operands);

        double cpuMerit = double.NaN;
        try { cpuMerit = new NativeMeritEngine(system, glassMgr).Evaluate(mf); } catch { }

        int[] batches = { gpuFill, gpuFill * 2 };
        int maxBatch = batches.Max();
        GpuPreScreener gpu;
        try { gpu = new GpuPreScreener(surfaces, catalog, rows.Count, wavelengthsUm, config, fields, operands, maxBatch); }
        catch (Exception ex) { Console.WriteLine($"  ▸ GPU  ⚠ session creation failed: {ex.Message}"); return; }

        using (gpu)
        {
            var curv0 = new double[nSurf]; var thick0 = new double[nSurf]; var conic0 = new double[nSurf];
            for (int i = 0; i < nSurf; ++i) { curv0[i] = surfaces[i].Curvature; thick0[i] = surfaces[i].Thickness; conic0[i] = surfaces[i].Conic; }

            bool firstDone = false;
            foreach (int n in batches.Distinct().OrderBy(b => b))
            {
                var curv = new double[n * nSurf]; var thick = new double[n * nSurf];
                var conic = new double[n * nSurf]; var matIds = new int[n * nSurf];
                for (int t = 0; t < n; ++t)
                    for (int i = 0; i < nSurf; ++i)
                    { curv[t*nSurf+i]=curv0[i]; thick[t*nSurf+i]=thick0[i]; conic[t*nSurf+i]=conic0[i]; matIds[t*nSurf+i]=centerIds[i]; }
                var merits = new double[n];

                try
                {
                    if (!firstDone)
                    {
                        long t0 = Stopwatch.GetTimestamp();
                        int rc0 = gpu.EvaluateBatch(curv, thick, conic, matIds, merits, n);
                        double firstMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                        if (rc0 != 0) { Console.WriteLine($"  ▸ GPU  | n={n,-7} ⚠ EvaluateBatch {GpuErr(rc0)}"); continue; }
                        firstDone = true;
                        Console.WriteLine($"  ▸ GPU first launch (PTX JIT + HtoD): {firstMs,9:F1} ms (excluded from timing)");
                    }
                    int rcLoop = 0;
                    var tp = TimeThroughput(() => { int rc = gpu.EvaluateBatch(curv, thick, conic, matIds, merits, n); if (rc != 0 && rcLoop == 0) rcLoop = rc; }, n, seconds);
                    if (rcLoop != 0) { Console.WriteLine($"  ▸ GPU  | n={n,-7} ⚠ {GpuErr(rcLoop)}"); continue; }

                    double parity = double.IsNaN(cpuMerit) ? double.NaN : merits.Take(n).Select(m => Math.Abs(m - cpuMerit)).Max();
                    string fillTag = n == gpuFill ? " (fill)" : n == gpuFill * 2 ? " (2×fill)" : "";
                    string ratio = cpuParallelDps > 0 ? $"  = {tp.DesignsPerSec / cpuParallelDps:F2}× CPU all-cores" : "";
                    Console.WriteLine($"  ▸ GPU  | n={n,-7}{fillTag,-9} {tp.MsPerDesign,7:F4} ms/design  {tp.DesignsPerSec,12:N0} designs/s{ratio}   |GPU−CPU|={parity:E1}");
                    csv.Add(Csv2(name, "gpu", $"GPU", 0, n, tp.Calls, tp.MsPerDesign, tp.DesignsPerSec, merits[0], "OK"));
                }
                catch (Exception ex) { Console.WriteLine($"  ▸ GPU  | n={n,-7} ⚠ {ex.GetType().Name}: {ex.Message}"); }
            }
        }
    }

    // ── prescreen: verify Multistart fills the GPU + show the sampling effect ──
    private static void RunPreScreenCheck(string lensPath, string name, GlassCatalogManager glassMgr,
        int gpuFill, double seconds)
    {
        Console.WriteLine($" [prescreen] GPU pre-screen sizing + sampling check (fill ≈ {gpuFill:N0} candidates/batch)");
        // Run both ways on the same lens so the truncation effect on the
        // candidate merit distribution is visible side by side.
        foreach (bool truncate in new[] { false, true })
        {
            var read = LhltReader.Read(lensPath);
            var opt = new MultistartOptimizer(read.System, read.MeritFunction!, glassMgr)
            {
                Settings = new MultistartSettings
                {
                    MaxTrials = 64,
                    LmIterationsPerTrial = 3,       // tiny — checking sampling, not convergence
                    InitialLmIterations = 0,
                    GlassSubstitutionProbability = 0.0,  // keep GPU-eligible
                    EnableMetropolis = false,
                    UseGpuPreScreen = true,
                    GpuPreScreenFill = 1.0,
                    GpuPreScreenTruncate = truncate,
                }
            };
            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, seconds * 2)));
            var result = opt.Optimize(cts.Token);
            string tag = truncate ? "truncated (1.0.117)" : "untruncated (old) ";
            Console.WriteLine($"   [{tag}] candidate merit over the sieved batch: " +
                              $"best={opt.GpuPreScreenBestMerit:E3}  median={opt.GpuPreScreenMedianMerit:E3}  worst={opt.GpuPreScreenWorstMerit:E3}");
        }
        Console.WriteLine($"   (truncation should pull MEDIAN/WORST candidate merit far closer to BEST —");
        Console.WriteLine($"    i.e. the device-filling batch samples the recoverable region, not the tails)");
    }

    // ── lens info ──────────────────────────────────────────────────────────────
    private static void PrintLensInfo(string lensPath, GlassCatalogManager glassMgr)
    {
        var read = LhltReader.Read(lensPath);
        var sys = read.System; var mf = read.MeritFunction!;
        var probe = new LocalOptimizer(sys, mf, glassMgr); probe.CollectVariables();
        int ex; try { ex = new MeritFunctionEvaluator(sys, glassMgr).ExpandMacros(mf).Count; } catch { ex = -1; }
        bool finite = sys.Surfaces.Count > 0 && !double.IsInfinity(sys.Surfaces[0].Thickness)
                      && !double.IsNaN(sys.Surfaces[0].Thickness) && Math.Abs(sys.Surfaces[0].Thickness) < 1e10;
        Console.WriteLine();
        Console.WriteLine("────────────────────────────────────────────────────────────────────");
        Console.WriteLine($" Lens: {Path.GetFileName(lensPath)}");
        Console.WriteLine($"   N={sys.Surfaces.Count} W={sys.Wavelengths.Count} F={sys.Fields.Count}" +
                          $" Aim={sys.RayAiming} Conj={(finite ? "finite" : "infinite")}" +
                          $" Vars={probe.Variables.Count} Ops={mf.Operands.Count}" + (ex > 0 ? $"/{ex}ex" : "") +
                          (sys.IsAfocal ? " Afocal" : ""));
        Console.WriteLine("────────────────────────────────────────────────────────────────────");
    }

    // ── reporting ──────────────────────────────────────────────────────────────
    private static void Report(string engine, int threads, int batch, Tput t, double merit,
        List<string> csv, string name, string mode)
    {
        string m = double.IsNaN(merit) ? "" : $"   merit={merit:E4}";
        Console.WriteLine($"  ▸ {engine,-14} {threads,2}T  {t.MsPerDesign,8:F4} ms/design  {t.DesignsPerSec,12:N0} designs/s   ({t.Calls} calls){m}");
        csv.Add(Csv2(name, mode, engine, threads, batch, t.Calls, t.MsPerDesign, t.DesignsPerSec, merit, "OK"));
    }
    private static void ReportFail(string engine, int threads, Exception ex, List<string> csv, string name, string mode)
    {
        string msg = ex.Message.Length > 70 ? ex.Message[..69] + "…" : ex.Message;
        Console.WriteLine($"  ▸ {engine,-14}      ⚠ {ex.GetType().Name}: {msg}");
        csv.Add(Csv2(name, mode, engine, threads, 0, 0, 0, 0, double.NaN, ex.GetType().Name));
    }

    private static string Csv2(string lens, string mode, string engine, int threads, int batch, int calls,
        double msPerDesign, double dps, double merit, string status)
    {
        static string F(double d) => double.IsNaN(d) ? "" : d.ToString("G9", CultureInfo.InvariantCulture);
        return string.Join(",",
            Csv(Environment.MachineName), Csv(RuntimeInformation.OSDescription), Csv(lens), mode, Csv(engine),
            threads.ToString(CultureInfo.InvariantCulture), batch.ToString(CultureInfo.InvariantCulture),
            calls.ToString(CultureInfo.InvariantCulture), F(msPerDesign), F(dps), F(merit), Csv(status));
    }
    private static string Csv(string s)
        => s.Contains(',') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    private static string GpuErr(int rc)
    {
        int cu = 0; try { cu = lenshh_gpu_last_cuda_error(); } catch { }
        return cu != 0 ? $"rc={rc} (CUresult {cu})" : $"rc={rc}";
    }

    // ── tracebench: FP32-vs-FP64 ray-trace throughput ─────────────────────────
    //
    // Measures the single-precision speedup of a trace-shaped kernel (the part
    // of the GPU pre-screen merit we're considering porting to float). The GPU
    // f32/f64 ratio is the decision number: consumer cards cripple FP64, so a
    // large ratio means a float pre-screen could finally make consumer GPUs pay
    // off. For contrast we run the IDENTICAL synthetic trace on the CPU in float
    // and double — CPU FP64≈FP32 (AVX2), so its ratio ~1 frames why float is a
    // GPU-specific unlock, not a universal one.
    private static void RunTraceBench(bool nativeOk, int rays, int surf, int iters, int repeats)
    {
        long opsPerLaunch = (long)rays * surf * iters;   // ray-surface traces
        Console.WriteLine(" [tracebench] FP32 vs FP64 ray-trace throughput");
        Console.WriteLine($"   rays={rays:N0}  surfaces={surf}  iters={iters}  launch-repeats={repeats}");
        Console.WriteLine($"   work/launch = {opsPerLaunch:N0} ray-surface traces");
        Console.WriteLine();

        // ---- GPU (the decision) ----
        double gpuF32Dps = 0, gpuF64Dps = 0;
        if (nativeOk && GpuPreScreener.IsAvailable)
        {
            bool okF = TryGpuTrace(0, rays, surf, iters, repeats, opsPerLaunch, out gpuF32Dps, out double csF);
            bool okD = TryGpuTrace(1, rays, surf, iters, repeats, opsPerLaunch, out gpuF64Dps, out double csD);
            if (okF && okD)
            {
                Console.WriteLine($"   GPU f32 / f64 speedup : {gpuF32Dps / Math.Max(1e-9, gpuF64Dps):F2}×");
                Console.WriteLine($"   checksum f32={csF:E6}  f64={csD:E6}  (rel diff {Math.Abs(csF - csD) / Math.Max(1e-30, Math.Abs(csD)):E2})");
            }
        }
        else
            Console.WriteLine("   GPU: n/a (no CUDA device) — running CPU contrast only");
        Console.WriteLine();

        // ---- CPU contrast (same trace, C#) ----
        // One pass over `rays` threads, `iters` re-traces each, Parallel.For.
        double cpuF32Dps = TimeCpuTrace(useDouble: false, rays, surf, iters, opsPerLaunch);
        double cpuF64Dps = TimeCpuTrace(useDouble: true,  rays, surf, iters, opsPerLaunch);
        Console.WriteLine($"   CPU f32 throughput    : {cpuF32Dps:N0} traces/s");
        Console.WriteLine($"   CPU f64 throughput    : {cpuF64Dps:N0} traces/s");
        Console.WriteLine($"   CPU f32 / f64 speedup : {cpuF32Dps / Math.Max(1e-9, cpuF64Dps):F2}×  (expected ~1 — AVX2 FP64≈FP32)");

        if (gpuF64Dps > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"   GPU f64 / CPU f64     : {gpuF64Dps / Math.Max(1e-9, cpuF64Dps):F2}×  (today's pre-screen path)");
            Console.WriteLine($"   GPU f32 / CPU f64     : {gpuF32Dps / Math.Max(1e-9, cpuF64Dps):F2}×  (a float pre-screen's ceiling)");
        }
    }

    private static bool TryGpuTrace(int useDouble, int rays, int surf, int iters, int repeats,
        long opsPerLaunch, out double tracesPerSec, out double checksum)
    {
        tracesPerSec = 0; checksum = 0;
        int rc;
        try { rc = lenshh_gpu_run_trace_microbench(useDouble, rays, surf, iters, repeats, out double ms, out checksum);
              if (rc == 0 && ms > 0) tracesPerSec = opsPerLaunch * repeats / (ms / 1000.0);
              else { Console.WriteLine($"   GPU {(useDouble == 1 ? "f64" : "f32")}: FAILED ({GpuErr(rc)})"); return false; }
              Console.WriteLine($"   GPU {(useDouble == 1 ? "f64" : "f32")} throughput    : {tracesPerSec:N0} traces/s  ({ms / repeats:F3} ms/launch)");
              return true; }
        catch (Exception ex) { Console.WriteLine($"   GPU {(useDouble == 1 ? "f64" : "f32")}: {ex.GetType().Name}: {ex.Message}"); return false; }
    }

    private static double TimeCpuTrace(bool useDouble, int rays, int surf, int iters, long opsPerLaunch)
    {
        // Cap the CPU pass so it doesn't dominate the run; scale the reported
        // throughput by the work actually done.
        int raysCpu = Math.Min(rays, 1 << 16);
        long ops = (long)raysCpu * surf * iters;
        var sw = Stopwatch.StartNew();
        double sink = 0;
        object gate = new();
        Parallel.For(0, raysCpu, () => 0.0,
            (r, _, acc) => acc + (useDouble ? CpuTraceD(surf, iters, r) : CpuTraceF(surf, iters, r)),
            acc => { lock (gate) sink += acc; });
        sw.Stop();
        GC.KeepAlive(sink);
        return ops / Math.Max(1e-9, sw.Elapsed.TotalSeconds);
    }

    // C# replicas of trace_microbench_ray<T> (kernel.cu) — same arithmetic, so
    // the CPU f32/f64 ratio is a like-for-like contrast to the GPU's.
    private static double CpuTraceD(int numSurf, int iters, int rayIdx)
    {
        double acc = 0;
        for (int it = 0; it < iters; ++it)
        {
            double y = ((rayIdx & 1023) - 512) * 0.001, x = 0, z = 0;
            double M = ((it & 255) - 128) * 1.0e-5, L = 0;
            double N = Math.Sqrt(1 - M * M - L * L);
            for (int s = 0; s < numSurf; ++s)
            {
                double curv = (s & 1) != 0 ? 0.02 : -0.015, thick = 5.0;
                double n1 = (s & 1) != 0 ? 1.5 : 1.0, n2 = (s & 1) != 0 ? 1.0 : 1.5;
                double zt = z + thick, t0 = (zt - z) / N; x += L * t0; y += M * t0; z = zt;
                double R = curv != 0 ? 1.0 / curv : 1e9, cz = z + R, oz = z - cz;
                double b = x * L + y * M + oz * N, c = x * x + y * y + oz * oz - R * R;
                double disc = b * b - c; if (disc < 0) disc = 0;
                double t1 = -b - Math.Sqrt(disc); x += L * t1; y += M * t1; z += N * t1;
                double invR = 1.0 / R, nx = x * invR, ny = y * invR, nz = (z - cz) * invR;
                double mu = n1 / n2, cosi = -(nx * L + ny * M + nz * N);
                double k = 1 - mu * mu * (1 - cosi * cosi); if (k < 0) k = 0;
                double coef = mu * cosi - Math.Sqrt(k);
                L = mu * L + coef * nx; M = mu * M + coef * ny; N = mu * N + coef * nz;
            }
            acc += y + M;
        }
        return acc;
    }
    private static float CpuTraceF(int numSurf, int iters, int rayIdx)
    {
        float acc = 0;
        for (int it = 0; it < iters; ++it)
        {
            float y = ((rayIdx & 1023) - 512) * 0.001f, x = 0, z = 0;
            float M = ((it & 255) - 128) * 1.0e-5f, L = 0;
            float N = MathF.Sqrt(1 - M * M - L * L);
            for (int s = 0; s < numSurf; ++s)
            {
                float curv = (s & 1) != 0 ? 0.02f : -0.015f, thick = 5.0f;
                float n1 = (s & 1) != 0 ? 1.5f : 1.0f, n2 = (s & 1) != 0 ? 1.0f : 1.5f;
                float zt = z + thick, t0 = (zt - z) / N; x += L * t0; y += M * t0; z = zt;
                float R = curv != 0 ? 1.0f / curv : 1e9f, cz = z + R, oz = z - cz;
                float b = x * L + y * M + oz * N, c = x * x + y * y + oz * oz - R * R;
                float disc = b * b - c; if (disc < 0) disc = 0;
                float t1 = -b - MathF.Sqrt(disc); x += L * t1; y += M * t1; z += N * t1;
                float invR = 1.0f / R, nx = x * invR, ny = y * invR, nz = (z - cz) * invR;
                float mu = n1 / n2, cosi = -(nx * L + ny * M + nz * N);
                float k = 1 - mu * mu * (1 - cosi * cosi); if (k < 0) k = 0;
                float coef = mu * cosi - MathF.Sqrt(k);
                L = mu * L + coef * nx; M = mu * M + coef * ny; N = mu * N + coef * nz;
            }
            acc += y + M;
        }
        return acc;
    }

    private static void PrintUsage() => Console.WriteLine(@"MeritEvalBench — merit-evaluation timing (per design)

Usage:
  MeritEvalBench --lens <path1.lhlt> [<path2.lhlt> ...]
                 [--mode value|jacobian|gpu|all|prescreen]   default all
                 [--seconds <N>]                   timed window per cell, default 5
                 [--cpu-batch <N>]                 designs per parallel call (default = GPU fill)
                 [--csv <out.csv>]

  MeritEvalBench --mode tracebench                 FP32-vs-FP64 trace throughput (no lens)
                 [--rays <N>]      threads, default 1048576
                 [--surfaces <N>]  trace depth, default 6
                 [--iters <N>]     re-traces per thread, default 200
                 [--repeats <N>]   timed kernel launches, default 30

value/gpu are reported PER DESIGN (ms/design + designs/sec). Threads =
logical processors; GPU batch = device-fill (driver occupancy, not hardcoded).
tracebench's GPU f32/f64 ratio is the input to the float-pre-screen decision.");
}
