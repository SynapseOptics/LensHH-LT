# Changelog

All notable changes to LensHH-LT and the LensHH-LT-Engine.

## 1.0.122 — 2026-06-15

### Global Evolutionary Optimization (new — formerly "DE Starting-Design Pipeline")
- **New "Global Evolutionary Optimization"** — generates a diverse pool of
  starting designs from scratch (even flat parallel plates): a Differential-
  Evolution population search explores glass + form, with a per-member **focus +
  EFL conditioner**, then polishes the best N seeds with **Multistart-LM (default)
  or Local-LM** into a gallery. All pre-polish seeds are saved by default.
- **GPU-resident DE (optional).** With a CUDA device the whole vary→condition→
  evaluate→select loop runs on-device and the population fills the GPU; bit-
  identical to the CPU path otherwise.
- **Reproducible** — the whole run (DE search **and** Multistart polish) reproduces
  from the Base seed (each candidate's polish is seeded `BaseSeed + rank`).
- **Run logs** — each run writes a `de_run_*.log` (settings header + per-candidate
  source / merit-before / merit-after / polish-time) outside the lens-file folders,
  for easy run-to-run comparison.
- **Polish a saved set** — re-polish a folder of previously-saved DE seeds without
  re-running the search.
- Controls: GPU on/off, generations, population, glass-sub %, the focus/EFL
  conditioner (surfaces + EFL-adjust tolerance, default ±5%), polish method/count,
  and the Multistart polish knobs (trials, stop@cap, σ start/cap, LM/trial).
- Available in the GUI, CLI (`optimize deseed`), MCP (`de_pipeline_*`), and C# API.

### Optimization menu
- Renamed **"Global Search" → "Global Multi Start Optimization"** and reordered the
  Optimization menu so the two global modes group below Basin-Hopping HJ+LM.

### Linux
- The Linux native library is now built against **glibc 2.35** (Ubuntu 22.04), so
  it loads on 22.04 and newer (a pre-release `.so` required GLIBC_2.38).

## 1.0.121 — 2026-06-13

### Global Search (new optimization mode)
- **New "Global Search" mode.** Instead of returning a single best design, it
  runs N independent Multistart restarts from your starting point and collects a
  browsable **gallery of structurally-distinct, locally-optimized solutions**.
  Each restart uses a non-overlapping random seed, and results are de-duplicated
  by lens *form* (glass set + element power-sign signature) so the gallery shows
  genuinely different design forms — not near-copies of one. Available in the GUI
  (Global Search dialog), the CLI (`optimize global`), the API, and the MCP server.

### MCP server
- **Long-running operations are now non-blocking.** Multistart, Basin Hopping,
  Split-Element, SPC synthesis, and the new Global Search no longer tie up the MCP
  connection while they run. Each exposes a `*_start` tool that launches the run
  and returns a job id; poll `optimize_status` for progress and the final result.
  The old blocking variants were removed.

### Fixes
- **`add_surface` no longer crashes ray tracing.** Surfaces added with the
  `add_surface` MCP tool stored a *null* material for air surfaces, which raised a
  `NullReferenceException` ("object reference not set") in the ray tracer — while
  `add_singlet`-created surfaces worked. `add_surface` now uses the same empty-air
  convention as the rest of the engine.
- **Engine hardening against null materials.** A surface's material can no longer
  be null (it is coerced to the empty-air convention at the data model), so a null
  from any tool, an older or externally-authored `.lhlt`, or a missing field can't
  crash ray tracing on any platform.

## 1.0.120 — 2026-06-11

### Multistart (global optimization)
- **Reverted the perturbation-sigma schedule to grow-on-rejection.** The
  2026-06-06 "triangle-wave" schedule was a regression: on a rejection it shrank
  sigma *below* its starting value before growing, so the search dug into the
  current basin instead of climbing out and rarely escaped. Sigma now starts
  small and grows on rejection (up to the cap) to escape, resetting to the small
  start only on a new best. The historical default **Init Sigma = 0.001** is
  restored.
- **Fixed a sigma-reset bug.** A Metropolis center move (accepting a worse design
  as the new search center) was resetting sigma to its small start even though it
  isn't an improvement — so when stuck, sigma kept snapping back and never grew
  large enough to escape. Sigma now resets only on a genuine new best.
- **Sigma escalation with patience.** Sigma grows one step only after several
  consecutive batches without a new best, so it climbs toward the cap gradually
  rather than all at once.
- Net effect: substantially more reliable global search — on real designs it now
  finds better solutions, and faster, than before.

### Glass substitution
- **New "Rescale on Glass Swap" option.** When a glass is swapped during
  optimization, the element's curvatures are rescaled by `(n_old−1)/(n_new−1)` so
  its optical power is preserved — keeping the swapped design feasible instead of
  broken, which improves the typical result. **On by default for Multistart**
  (validated to help); **off by default for Basin Hopping** (its small-step
  trajectory is over-perturbed by the curvature jump). No effect when glass
  substitution is off.

### Performance
- **Removed nested-parallelism overhead in Multistart / Basin Hopping.** Those
  modes already run one trial per core; each trial's local optimizer no longer
  also spawns its own parallel Jacobian-column loop on top of that (it does so
  only for a standalone single optimization, which still owns all cores).
  Results are unchanged — the Jacobian and the optimization outcome are
  identical; only wasted thread-scheduling work is removed.

### macOS
- **Fixed the Samples menu**, which previously could not locate the bundled
  samples folder inside the macOS `.app` package.

## 1.0.119 — 2026-06-09

### Linux (GPU)
- **GPU acceleration on Linux.** The Linux build now ships the native CUDA
  kernel (`lenshh_kernel.fatbin`, native SASS for `sm_80/89/90/120`) alongside
  `liblenshh_native.so`, so the GPU value pre-screen engages automatically when
  an NVIDIA device is present — previously the Linux package shipped without the
  kernel and silently ran CPU-only. No CUDA toolkit is required on the target;
  the prebuilt cubin means no first-run JIT. Falls back to CPU when no GPU is
  found.
- Built against Ubuntu 22.04 (glibc 2.34) for broad compatibility — runs on
  22.04 / 24.04+, RHEL 9 / Rocky 9, and common datacenter Linux images.
- Validated on RTX 4060, A100, H100, and RTX PRO 6000 (Blackwell); GPU output
  is bit-identical to the CPU path. See the case study at
  synapseoptics.com/case-studies/.

## 1.0.118 — 2026-06-07

### macOS (Apple Silicon)
- **Official macOS build.** LensHH-LT now ships as a signed `LensHH-LT.app`
  bundle for Apple Silicon (M1 or later) — download, unzip, and double-click.
  Previous releases required building from source on macOS. The package bundles
  the GUI plus the CLI, MCP server, Ollama bridge, and MeritEvalBench, along with
  the full sample-lens set, the stock-lens catalog, the glass catalogs, and the
  documentation. Optimization is CPU-only on macOS (no GPU).
- The build is ad-hoc signed but not yet notarized, so the first launch needs a
  one-time right-click → **Open** (Gatekeeper "unidentified developer"), the
  macOS equivalent of the Windows SmartScreen "Run anyway". Intel Macs are not
  supported — the native engine is arm64-only and cannot run under Rosetta.

### Fixed
- **RenderApp auto-launch on macOS/Linux.** The CLI and MCP server hardcoded the
  Windows `.exe` name when locating the render helper, so headless PNG rendering
  could never start the helper on non-Windows platforms. The name is now resolved
  per platform.

### Documentation
- Getting-started, building, and README updated with macOS (Apple Silicon)
  install instructions, and corrected the Intel-Mac guidance (the arm64 engine
  cannot run through Rosetta 2).

## 1.0.117 — 2026-06-06

### Optimization
- **New perturbation schedule for Multistart optimization.** Sigma now follows a
  triangle wave: it starts at the initial value and returns there on every
  accepted improvement; on rejection it first *shrinks* for finer local
  refinement, and only after bottoming out does it grow back up to the cap to
  escape a stuck basin, then bounces back down. This replaces the previous
  grow-on-every-rejection sawtooth, which tended to wander away from
  near-solution designs instead of refining them. New defaults: initial sigma
  0.0003, cap 0.1 (was 0.0001 / 0.5). Basin Hopping is unchanged. The CLI
  (`optimize multistart`/`split`) and MCP (`optimize_multistart`) parameter
  defaults and help text were updated to match the new schedule.
- **Simplified the GPU pre-screen controls.** Removed the single-precision
  ("float") pre-screen toggle and the separate GPU sigma multiplier from the
  Multistart dialog — the pre-screen always runs in full double precision at the
  same perturbation radius as the CPU path. Behavior is unchanged from the
  recommended defaults; the two extra knobs are simply gone.

### Added
- **MeritEvalBench** — a merit-function timing tool (whole-merit value, residuals
  + Jacobian, and the GPU value kernel), reported per design so CPU and GPU
  compare directly. Ships in the Windows installer (`tools\bench`) and the Linux
  AppImage (`--bench`); on macOS it builds from this public repository alone.
- **GPU pre-screen now fills the device.** The Multistart GPU pre-screen sizes its
  per-batch candidate count to the device's occupancy (read from the CUDA driver,
  not hardcoded) instead of a CPU-thread-count heuristic, so the GPU is used at
  full capacity. New `GpuPreScreenFill` setting (default 1.0 = fill the device).

### Changed / Fixed
- **GPU pre-screen sampling**: each candidate's perturbation is now truncated to
  the same maximum radius the non-GPU path would use, so the device-filling
  candidate count samples that region *densely* rather than fattening the
  Gaussian tails and flinging most candidates too far (where merit can't
  recover). New `GpuPreScreenTruncate` setting (default on).
- **Optimization throughput on the shipped engine**: disabled the obfuscator's
  string-encryption pass, which used a lock-protected runtime cache that
  serialized the C# engine's parallel merit evaluation. The C# parallel path is
  now several times faster; algorithm protection (control-flow obfuscation) is
  unchanged and the engine exposes no secret strings.
- **GPU first launch is now instant.** The GPU merit kernel ships as precompiled
  native code (cubin) for the supported GPU generations instead of being
  just-in-time compiled on first use — the first GPU optimization run no longer
  pauses for minutes while the kernel builds. (A GPU whose generation isn't in the
  shipped set simply uses the CPU path; it never stalls.)
- **Faster GPU merit evaluation**: cut the merit kernel's per-thread memory
  footprint ~26× (down to the actual surface count instead of a fixed cap),
  reducing memory-bandwidth pressure on the bandwidth-bound kernel.

Maintenance release. All 1.0.115 users should update.

### Fixed

- **GPU pre-screen (Beta) reliability**: fixed two device-memory layout bugs in the batched
  merit-value dispatcher that could silently disable GPU pre-screening for the remainder of
  the session on some lens/batch-size combinations (scratch buffer undersized for the
  per-design glass table introduced in 1.0.115, and static-input offsets shifted when the
  batch size changed between launches). Results were never affected — evaluation fell back
  to the CPU path — but the GPU speedup could quietly disappear. Verified bit-equal
  GPU-vs-CPU merit on real designs at batch sizes 256–4096 after the fix.
- **GPU diagnostics**: the engine now records the underlying CUDA driver error code of a
  failed GPU launch (`lenshh_gpu_last_cuda_error`), so support can distinguish
  out-of-memory conditions from driver issues in the field.
- **Release packaging**: the Windows installer and Linux AppImage build scripts now verify
  that the packaged engine binaries were built in the production configuration, and the
  engine binaries bundled with this release have been rebuilt accordingly.

## 1.0.115 — 2026-06-04

**Two unrelated improvements stack to give 1.0.115 the largest end-to-end optimization speedup any LensHH-LT release has shipped.** First, the analytical Jacobian work that landed across Phase 9 ports every operand family that matters in practice — CT/CTA/CTG, CV/CVA/CVG, ET/EA/EG/DTRG (including bounded variants), arithmetic operands, RI / RE angle ops, SENS, Abso (`|x|`) op-code, and OPD in afocal mode (riflescopes / telescopes / beam expanders) — from finite-difference probes to analytic dual chains. On a representative cooke-class lens with full image-quality + boundary merit the Jacobian step drops from **54 ms (FD) to 13 ms (Analytic) — about 4× per LM iteration**. On SENS-bearing merits the per-`(field, pupil, wavelength)` dual-trace cache delivers an **11.2× speedup** on the Jacobian step alone. Second, a CPU oversubscription bug that had been hurting every multi-threaded Optimize() run was fixed: the C# optimizer used to spawn nested parallelism (outer Multistart threads, each spawning per-Jacobian-column workers in the native bridge), which on a 16-thread laptop ran `N_threads × J_columns` workers fighting for the same 16 cores and was catastrophic on a 96-core workstation. `ParallelNativeJacobian` now defaults **OFF** on every optimizer; `ParallelEvaluation` (operand-level parallelism for residual-only calls) defaults **ON** on LocalOptimizer. Measured net effect on the 16-thread reference machine: **1.8× to 5× faster** depending on merit shape; far larger on workstation-class hardware.

C++ native engine reaches functional parity with the C# evaluator for the operand and variable types that LensHH-LT exposes. Adds GUI dropdowns (Multistart and Basin-Hopping dialogs) to choose engine + Jacobian mode per run. New GPU pre-screen (Beta) screens thousands of Multistart candidates per CUDA launch including glass-swap variants, then forwards the top-K to HJ-LM for full polish — about 2× speedup on a consumer 4060; larger speedups expected on A100. Linux AppImage gets a long-overdue stale-native fix that resolved the "license shows OK but EFL/merit values are wrong" symptom on Linux 1.0.114.

### Added — Release-integration work (2026-06-03 / 04)

- **Linux AppImage native stale-bin fix** (`installer/build-linux-appimage.sh`, `engine/linux-x64/liblenshh_native.so`). The April-23 `liblenshh_native.so` checked into `engine/linux-x64/` was being silently bundled by `dotnet publish --self-contained` into every Linux binary (App, CLI, MCP, RenderApp) via `<None CopyToOutputDirectory="PreserveNewest">`. On Linux, .NET resolves `DllImport("lenshh_native")` from `AppContext.BaseDirectory` BEFORE `LD_LIBRARY_PATH`, so the freshly-built `.so` copied into `AppDir/usr/lib/` by the build script was never the one that actually loaded — the stale bundle next to `LensHH.App` won every time. Symptom in 1.0.114 / mid-115 Linux: `IsActivated` correctly returns true (the stale `.so` still implements Ed25519 verify fine), but every gated compute call ran two-month-old code — wrong EFL, wrong entrance pupil, NaN on optimization. Fix: build script Step 2b now does `cp -f "$NATIVE_SO" "$REPO_ROOT/engine/linux-x64/liblenshh_native.so"` BEFORE the `dotnet publish` calls, so the freshly-built native flows into every binary's publish output via the existing `PreserveNewest` references. The `usr/lib/` copy stays as belt-and-suspenders. Refresh of `engine/linux-x64/liblenshh_native.so` (265 KB → 707 KB, April 23 → June 4) lands alongside in the same commit so any developer `dotnet publish` from now produces a working Linux binary. Cross-repo concern: the same trap exists in principle for Windows and macOS builds — they happen to work because Windows uses `lenshh_native.dll` which the obfuscation publish script rebuilds + copies, and macOS isn't shipped yet. Future task: harmonize the publish flow so the engine/<rid>/ slot is never a stale checked-in artifact.
- **GPU pre-screen Beta — GUI + CLI surface** (`MultistartDialogViewModel.cs`, `MultistartDialog.axaml`, `LensHH.CLI`). Adds the user-facing toggle for the G1.C/G2 GPU pre-screen kernel that ports the whole `evaluate_merit` value path as a single `__device__` function. GUI defaults are *C++ Native + Analytic + GPU pre-screen ON* when a CUDA device is detected and the active design uses features the kernel covers (curvature/thickness/conic variables + the GPU-eligible operand set); auto-disables and shows a tagged reason when it doesn't (aspherics, multi-config, ray-aim modes the kernel hasn't grown a branch for yet). Hardware-acceleration strip on the dialog header surfaces the device name + compute capability and the auto-decision so users can audit it. CLI gains `--gpu-prescreen` / `--no-gpu-prescreen` flags; `optimization status` reports the per-run candidate count, batches launched, and surviving glass-swap variants from `MultistartOptimizer`'s pre-screen telemetry. Per-design `material_ids[N]` plumbing in `GpuPreScreen.cs` + `kernel.cu` lets a single batched launch mix continuous candidates (curvature/thickness/conic perturbations off the current center) with glass-swap candidates (each carrying its own materials_ids row). Beta labeling is explicit everywhere — release-notes language, dialog header, CLI flag help — so users know it can be turned off if a result looks off. Mid-115 user feedback ("HUGE speed increase over 114") was captured against a 24-SM 4060; A100 validation deferred to 1.0.116.
- **TTRACK wavelength column hidden** (`MeritFunctionEditorViewModel.cs`). TTRACK (axial track) is wavelength-independent — adding `OperandType.TTRACK` to the `NeedsWave` exclusion list hides the wavelength column from the merit-function editor for any TTRACK row. Trivial UI fix surfaced by user inspection of the 115 RC; harmless before, but now the table doesn't suggest configurability that the operand doesn't have.
- **Optimization chapter: three illustrated flowcharts** (`docs/optimization.md`, `docs/build/diagrams/`, `docs/images/optimization/`). The user guide now ships Mermaid-rendered diagrams of Multistart architecture, per-trial HJ-LM, and the GPU pre-screen pipeline — embedded as PNGs in the markdown and PDF, with `.mmd` sources committed so they can be re-rendered after edits (instructions in `docs/build/diagrams/README.md`). The PDF builder's image CSS in `docs/build/build.js` got a `max-height: 8.5in; object-fit: contain` rule so tall flowcharts shrink-to-fit instead of being clipped across page breaks on Letter-size pages.

### Added — C++ engine coverage (Phase 9h follow-up, 2026-06-01 / 02)

- **Phase 11 Part 1b (task #117) — WAVEX host post-pass rewritten to match C# `RemovePistonAndTiltFromOpdx`** (2026-06-02). Phase 7d's host post-pass at `batched_eval.cpp:1798-1923` summed the tilt-fit LSQ accumulators (sum_z, sum_zpx, sum_zpy) over every raw per-slot residual — counting each (px, py) once per wavelength. The C# canonical algorithm (mirrored faithfully by `merit_function.cpp:1466-1592`) fits the tilt plane on UNIQUE pupil points only, using the wavelength-weighted-average OPD at each unique point. For multi-wave WAVEX merits where OPD varies across wavelengths at the same pupil, the two LSQ fits return different (A, B) tilt coefficients. Phase 11 Part 1b rewrites the post-pass with three matching changes: (1) tilt fit now iterates over unique pupil points within the post-group, computing `avg_v = Σ(w_m·v_m) / Σ(w_m)` and `avg_d[k] = Σ(w_m·d_m[k]) / Σ(w_m)` across all slots at that (px, py), then accumulates the averaged value+derivatives into the LSQ; mirror-doubling for hx-symmetric fields (use_sym from task #112) now applies at the averaged-pupil level; (2) tilt subtraction in its own loop applies (A·px + B·py) to ALL slots in the post-group (matches `merit_function.cpp:1552-1558`); (3) piston removal is now PER-WAVELENGTH for WAVEX (POST_TILT_PISTON) — for each unique wavelength in the post-group, compute weighted mean of post-tilt values and subtract per-wavelength (matches `merit_function.cpp:1561-1592`). WAVEM (POST_PISTON) keeps field-wide piston because its post-groups already key by wavelength. **All 100 BatchedEval / CudaDispatch / family tests still pass**; Probe I 2-field bit-match preserved (single-wave WAVEX in its synthetic system — neither algorithm exercises the multi-wave averaging path so they give identical answers). **Investigation also pin-pointed a separate upstream bug**: Tanabe's "3 × 0.266 µm" wavelengths are actually 0.265985, 0.266, 0.266015 (display rounds), so refractive indices DIFFER per wave. The GPU OPD kernel uses ONLY primary-wave indices (`g_indices` is a single `[num_surfaces]` slab) — produces near-identical OPD per pupil triple. CPU uses each operand's own wave indices (correctly varying). Phase 11 Part 1b post-pass fix is necessary but not sufficient; full Tanabe bit-equality requires task #118 (OPD kernel multi-wave indices, mirrors Phase 7c-2 SPOT plumbing). Until #118 lands, `Phase10Bench --gpu` runs end-to-end but initial merit stays at 8.461 vs CPU 8.437.
- **Phase 11 Part 1 (task #116) — hybrid GPU/CPU operand split restored** (2026-06-02). The architectural intent documented in `project_gpu_cpu_split.md` (2026-05-29) — GPU runs ONLY image-quality operands, CPU OpenMP handles boundary/paraxial/scalar — had drifted: phases 4-10 grew GPU `classify_ops` handlers for EFL/RY/RX/SD/ET/EA/EG/DTRG, and the dispatcher became all-or-nothing. Any unknown operand type caused `rc = -2` rejection, which meant every real production merit (Tanabe has CTA, CTG, TTRACK; every realistic .lhlt has CT-family span operands) bounced through the C# bridge as `NotSupportedException` and never reached the GPU. Phase 11 Part 1 adds: (1) `ClassifiedOp::Family::CPU_HANDLED` enum value as the tag for operands the dispatcher routes to CPU; (2) the classify_ops fall-through `else { return -2; }` becomes `else { fam = CPU_HANDLED }`, and three other reject points — `op.op_code != 0 && != Abso`, ET/EA/EG span (`surface2 != surface1` or `surface1+1 >= num_surfaces`), SD/DTRG out-of-range — also route to CPU_HANDLED instead of returning -2; (3) a `cpu_op_indices[kMaxOps]` array tracks original-array indices for the CPU-handled subset, populated alongside the existing classifier; (4) new scratch slabs `cpu_res_off`, `cpu_jac_off`, `cpu_results_off`, `cpu_count_off`, `cpu_merit_off` sized `num_trials × n_cpu_handled × {1 or 2 or num_variables} × {4 or 8 or sizeof(MeritResult)}` carved out of the existing scratch buffer; (5) per-trial CPU eval injected into the existing OpenMP worker right after `trial_surfs` is built — calls `evaluate_merit_and_jacobian(trial_surfs, …, subset_ops, n_cpu_handled, variables, …)` with the per-trial analytic-scratch slices (`my_operand_duals`, `my_arith_rows`, `my_dasph`, `my_ray_scratch` — same slice formulas as the existing device=0 CPU path); (6) `case ClassifiedOp::CPU_HANDLED:` added to the scatter switch that copies CPU's FINAL residual + Jacobian row directly to `base_residuals` / `jacobian` (bypassing the post-switch op_code/weight/target/bounds processing, which the CPU evaluator already applied); (7) `evaluate_mixed` signature gained `operand_duals_buf` / `arith_rows_buf` / `dasph_buf` / `ray_scratch_buf` defaulted-nullptr params, threaded through from `evaluate_merit_batched`. **Verified**: Phase10Bench `--gpu --lens Tanabe_…_CURVATURE.lhlt --duration 10` now completes end-to-end with `Batched-GPU | Analytic | Broyden=T  …  128 trials × 0.080 s/trial`, classify summary `n_active=241 n_smooth=1 n_opd_groups=9 n_opd_slots=234 n_cpu_handled=6 num_operands=241` — 6 of Tanabe's operands route to CPU (the EA/EG/DTRG spans + TTRACK/CTA/CTG), the rest stay on GPU. All 100 BatchedEval + CudaDispatch + family tests still pass at bit-equal noise floors (Probe I 2-field, FNumber, Phase 8d-3 ObjectHeight, Phase 8d-4 ObjectAngle finite all unchanged — none of them have CPU_HANDLED operands so the new code path is bypassed). **Known follow-up (task #117)**: GPU initial merit on Tanabe reports `8.461E-01` vs CPU's `8.437E-01` — `~0.024` difference on merit ~0.8. The pipeline is wired but the CPU_HANDLED scatter isn't yet producing bit-equal residuals to the single-trial Analytic engine on the full merit. Tracked in #117 with a debug plan. Until that lands, `--gpu` is FUNCTIONAL but not bit-equal on merits with span-CT-family operands. **Phase 11 Part 2 (architectural purification)**: still pending — remove the EFL/RY/RX/SD/ET/EA/EG/DTRG GPU handlers, route all non-image-quality through the Part 1 CPU pass. Shrinks the dispatcher substantially; unnecessary to unblock the GPU timing test (which Part 1 already does).
- **Phase 10b (task #106) — Phase10Bench per-lens info header gains conjugate + expanded-op count + one-line summary** (2026-06-02). The existing `PrintLensInfoHeader` printed surfaces, wavelengths, fields, aperture, ray aiming, afocal, variables, and pre-macro-expansion operand count + op-type breakdown. Phase 10b adds: (1) **Conjugate**: `finite (object distance = X mm)` or `infinite`, determined by `surfaces[0].Thickness` finiteness — critical context for interpreting OPD / ray-aim behavior (Phase 8d/8d-3/8d-4 all branch on this) so it's promoted to a top-level field. (2) **Post-macro-expansion operand count**: appended to the Operands line, so a 8-operand merit that expands to 241 primitive `_WAVEX1`/`_TRCY`/etc. ops makes its real cost visible. (3) **Summary**: a single-line `N=21 W=3 F=3 Aper=EPD Aim=Off Conj=infinite Vars=18 Ops=8/241ex` formatted so a human can grep for it and it copy-pastes cleanly into commit messages or shipped bench tables. The expansion call is wrapped in try/catch so configs that ExpandMacros doesn't yet support don't kill the header. Verified on Tanabe (infinite, EPD): correctly shows `Conjugate: infinite`, `Operands: 8 ... 241 primitive ops post-expansion`, summary line lists all the shape numbers. Pure presentation cleanup; no behavior change on the benchmark cells.
- **Phase 8d-4 (task #113) — OPD GPU kernel adds ObjectAngle finite (Real/Robust) branch + scaffolding for Off+finite ObjectHeight** (2026-06-02). Phase 8d-3 closed ObjectHeight Real/Robust at finite conjugate; #113 extends the kernel's `direction_seed` path to also cover ObjectAngle Real/Robust (the more common case for microscope/lithography optics with finite-conjugate angular fields) and adds the kernel-side direct-construct branch (`off_finite_h_seed`) for Off + finite + ObjectHeight. Kernel diff: three new int params (`off_finite_h_seed`, `field_type`, `const double* g_field_yh[num_groups]`); per-(trial, group) shared cache `s_z1a_v` + `s_dz1a_dvar[W]` computed by thread 0 via `compute_entrance_pupil_position_t<DualT>(dcurv, dthick, g_indices, …) + dthick[0]`; four trace sites (chief stop, chief end, pupil stop, pupil end) extended with a three-way conditional — `off_finite_h_seed` (direct-construct Dual ray from z1a + obj_h + px·R + py·R), `direction_seed + field_type=ObjectAngle` (slot 0/1 on l, m + r.y carries `dya_dvar[k] = -tan(field) · dz1a_dvar[k]` tangents), or the existing Phase 8d-3 ObjectHeight / infinite paths. For `off_finite_h_seed` the implicit-aim invert at the chief and pupil stop sites is skipped (slots 0/1 are zero so det=0 would falsely trigger the failure penalty) — `chief_dlx/dly` and `pup_dlx/pup_dly` zero out; the var chain flows directly through slot 2+k. Dispatcher flags computed from `surfaces[0].thickness` finiteness + `config->ray_aiming == Off` + `config->field_type == ObjectHeight`. Dispatcher: new `d_field_yh[num_groups]` device buffer + HtoD upload + kernel arg; allocation is unconditional (~8 B/group, trivial overhead) so the legacy CudaDispatch direct-kernel probes can pass `field_yh = nullptr` and skip the HtoD without hitting the new branches. Also restructured the chief block to use `if (s_ok) { … }` guards instead of `goto chief_done` so the new Dual<W+2> locals (z1a, r_y, x1a_d, y1a_d, ep_dual) don't violate the C++ "transfer of control bypasses initialization" rule (CUDA/NVCC enforces this strictly for goto past non-trivial constructors). **Validated**: new probe `GpuPathMatchesCpuPath_EflOpd_ObjectAngle_Finite` (ProtoSystem + 500 mm obj_z + 0.5° field + ObjectAngle + Real + 1 EFL + 5 _WAVEC1 × 128 trials × 4 vars) — `|Δr|_max = 2.33e-10` on residuals up to 204, `|ΔJ|_max = 1.16e-10` vs 1e-8 threshold, `|Δmerit|_max = 1.32e-10` on merit ~155 (rel 1.1e-12) — same noise floor as the Phase 8d-3 ObjectHeight test. Phase 8d-3 ObjectHeight probe unchanged at 2.33e-10 / 8.73e-11 / 1.76e-10. All 100 BatchedEval / CudaDispatch / family tests pass; Probe I 2-field still bit-matches; Tanabe 8.437E-01 unchanged. **Carved out**: `GpuPathMatchesCpuPath_EflOpd_ObjectHeight_OffFinite` lives in the file as `DISABLED_` — the kernel-side `off_finite_h_seed` branch is implemented and exercised end-to-end, but the OPD dispatcher's host pre-prep (`batched_eval.cpp` chief failure check) falls through to the legacy `chief_aim = {0,0,1}` + `chief_launch_l/m/n = (0, sin(rad), cos(rad))` default for Off+finite, which doesn't match the kernel's direct-construct path; the host's scalar trace then "fails" and writes the 1e6 FailedGroupPenalty over the kernel's correct result. Filed as task #115 — adding the matching `if (finite_obj && aim_off && is_object_h)` branch in the per-trial worker is ~30-60 min of mechanical work mirroring the existing Real/Robust+finite branch at `batched_eval.cpp:973`. COVERAGE.md "OPD finite-conjugate" row updated to `✓ ObjectHeight Real/Robust (Phase 8d-3) + ObjectAngle Real/Robust (Phase 8d-4); Off + finite + ObjectHeight kernel implemented, host pre-prep tracked as #115`.
- **Phase 7c-2 (task #86) — SPOT-family multi-(hy, wave) groups in the GPU kernel** (2026-06-02). The dispatcher's "first-cut single (hy, wave) group" SPOT restriction (`batched_eval.cpp:404-411`, pre-Phase-7c-2) forced any merit with SPOT operands across multiple fields or wavelengths to fall back to CPU. Phase 7c-2 mirrors the Phase 7c OPD pattern through the SPOT path: classify_ops now tracks `spot_group_hy[kMaxSpotGroups=64]` + `spot_group_wave[]` with per-slot `group_id`; the scratch slabs grow to per-(trial, group) chief_sx/sy and per-(trial, slot) aim_sx/sy; the host pre-prep does per-group chief aim convergence and per-slot pupil aim using each slot's group's launch direction + wave indices; the kernel signature gains `chief_launch_l/m/n[num_groups]`, `group_wave_idx[num_groups]`, `pupil_offset/pupils_per_group[num_groups]`, and a flat `indices[num_wavelengths × num_surfaces]` ptr that each block offsets by its group's wave idx; gridDim becomes `(num_trials, num_groups, 1)` with one block per (trial, group) doing chief work in thread 0 then __syncthreads(). The SpotPupilSlot struct gains a `group_id` field. The output residuals/Jacobian layout stays per-(trial, 2×slot) — only the slot index space widens to cover all groups, so the existing per-operand scatter at `batched_eval.cpp:1887-1909` works unchanged. SPOT-family kind selection (SPOT / SPOTM / SPOTR / SPOTMR) is by-authoring-convention exclusive: SPOT and SPOTR expand to _TRAD/_TRAE → SPOT kernel; SPOTM and SPOTMR expand to _TRCX/_TRCY → RY-batched kernel. The kernel itself only sees _TRAD/_TRAE pairs; centroid removal (SPOTM/SPOTMR) and RMS reduction (SPOTR/SPOTMR) live in the host post-pass on flat per-(trial, slot) output and are unchanged. **Verified**: all 99 BatchedEval + CudaDispatch + family tests pass; Phase 8d-3 ObjectHeight probe and Probe I 2-field still bit-match at noise floor; Tanabe merit 8.437E-01 unchanged. Three direct-kernel test sites in `test_analytic_proto.cpp` (SpotJacobianMatchesHostDual + SpotJacobianMatchesHostDual_N{12,18,30} + SpotJacobianBenchmarkSweep) updated to the new signature (single-group adapter: `launch_l_arr[1]`, `group_wave_idx[1]={0}`, `pupil_offset_arr[1]={0}`, `pupils_per_group[1]={P}`). Bound `kMaxSpotGroups = 64` matches the OPD multi-group cap. Multi-group SPOT validation by extension of `GpuPathMatchesCpuPath_EflSpot` to multi-(hy) and SPOTM/SPOTR/SPOTMR post-pass cross-tests is a follow-up.
- **Task #114 — test-suite analytic-scratch backfill** (2026-06-02). Tasks #107 / #109 / #110 added four caller-allocated buffer params to `evaluate_merit_batched` and `evaluate_merit_and_jacobian` (`operand_duals_buf`, `arith_rows_buf`, `dasph_buf`, `ray_scratch_buf`). All four default to `nullptr` in the header so existing test sites still compile, but Analytic-mode calls return `rc = -2` (or silently produce zero Jacobian on the single-trial path) when the buffers aren't supplied. The test suite had 28 such stale sites in `test_analytic_proto.cpp` plus 5 single-trial call sites in `test_bounded_operand.cpp` / `test_arithmetic_operand.cpp` / `test_ct_boundary_operand.cpp` / `test_conic_variable.cpp` / `test_aspheric_variable.cpp`, all failing as a class. Phase 5.5 #114 adds: (1) a shared `AnalyticScratch{operand_duals, arith_rows, dasph, ray_scratch}` helper at the top of `test_analytic_proto.cpp` sized by `(num_trials, num_surfaces, num_operands)`, used at every batched site; (2) inline `std::vector<double>` allocations at the five external single-trial sites; (3) a one-line addition to the `BatchedEvalMatchesSingleTrial` reference-loop site that uses its own num_trials=1 `AnalyticScratch`; (4) backfill of three pre-existing failures surfaced by the green sweep: `RayTracer.CExportVersion` now accepts both `"0.1.0"` and `"0.1.0+validation-noauth"` (the `LENSHH_DISABLE_ACTIVATION` variant), and `CudaDispatch.OpdJacobianMatchesHostDual` + `OpdJacobianBenchmarkSweep` had `launch_l_arr[1]` / `pup_ll[P]` sized for the pre-Phase-8d-2 per-group/per-slot layout — bumped to per-(trial × group) and per-(trial × slot) replication. **Verified**: all 99 BatchedEval + CudaLoader + CudaDispatch + BoundedOperand + ArithmeticOperand + CtBoundaryOperand + ConicVariable + AsphericVariable + RayTracer tests pass (was: 96/99 immediately post-#114-batched-fix, 16/99 immediately post-#107/#109/#110). Phase 8d-3 ObjectHeight probe and Probe I 2-field remain bit-equal at their respective noise floors. The `AnalyticScratch` helper is now the canonical pattern for any new test that calls `evaluate_merit_batched` in Analytic mode.
- **Phase 5.5 (task #77) — `MultistartOptimizerBatch.BatchSize` auto-tunes from device capability**. The class default has been 200 since Phase 6, which over-provisions a 24-SM GPU (saturates at ~96 trials) and under-provisions a 32+-thread CPU OpenMP worker pool (wants ~96–200 to keep workers fed). Phase 5.5 adds a new native query `lenshh_gpu_query` → `CudaCapabilities` (sm_count, threads_per_sm, max_threads_per_block, warp_size, cc_major/minor) wrapping `cuDeviceGetAttribute` reads on device 0; resulted in `NativeMeritEngine.GetGpuCapabilities()` (lazily cached, returns `Available = 0` when no driver/device) + `NativeMeritEngine.AutoBatchSize(useGpu, maxThreads, …)` which targets `sm_count × 4` on the GPU path and `maxThreads × 3` on the CPU path, rounded up to a 64-trial quantum and clamped to [64, 2048]. `MultistartOptimizerBatch.AutoBatchSize` (default true) makes `Optimize()` call this at run start; `LastEffectiveBatchSize` exposes what actually ran for telemetry. Phase10Bench's `--use-batched` path prints `AutoBatch: N trials/batch  (CLI --batch-size M is the fallback)` per cell. **Verified**: on this 24-SM sm_89 device (RTX-40 class, 1536 threads/SM) the GPU formula gives 96 → rounded to 128; on the same machine's CPU path it gives 16-thread × 3 = 48 → floor 64; Tanabe Multistart-Batch Analytic still converges to 8.437E-01 merit. Phase 8d-3 ObjectHeight, Phase 8c-2 FNumber, and Probe I 2-field all bit-match unchanged. A new gtest `CudaLoader.QueryCapabilitiesPopulatesFields` prints the populated struct as a sanity probe (`sm_89, 24 SMs × 1536 threads/SM = 36864 active, warp 32, blockMax 1024`). The native side defines a POD `LenshhGpuCapabilities` struct (Available, SmCount, ThreadsPerSm, MaxThreadsPerBlock, WarpSize, CCMajor, CCMinor) returned by value; the C# `[StructLayout(Sequential)]` mirror matches. Caller can override via `AutoBatchSize = false` (locks in the user's configured value); the per-occupancy ratio (4 blocks/SM by default) is tunable in `AutoBatchSize(occupancyBlocksPerSm:)` for tests.
- **Phase 8d-3 (task #111) — OPD GPU kernel handles finite-conjugate ObjectHeight Real/Robust**. The `DISABLED_GpuPathMatchesCpuPath_EflOpd_ObjectHeight` probe that Phase 8d-2 filed (1 EFL + 5 `_WAVEC1`, ObjectHeight finite at 500 mm object distance, Real aiming) had a ~447 residual diff and ~8690 Jacobian diff because the GPU `lenshh_opd_jacobian_kernel` was missing two finite-conjugate adaptations the single-trial path already does at `merit_function.cpp:2802`: (1) the implicit-aim seed slots {0, 1} were still on `(xa, ya)` instead of `(la, ma)` — for finite the launch position is the fixed object-plane point `(0, obj_height)` and the var-dependent quantity is the converged direction; (2) `compute_exit_pupil_waves_t` was called with `is_infinite=1` hardcoded, applying the infinite-conjugate input-side projection adjustment that doesn't exist when the input plane is at the object. Phase 8d-3 adds two new int params (`is_finite_opd`, `direction_seed`) to the kernel + dispatcher; when `direction_seed=1` the four trace sites (chief stop, chief end, pupil stop, pupil end) build the Dual ray with `r.x = DualT(launch_x); r.y = DualT(launch_y)` as object-plane constants and `r.l = DualT::seed(ldir, 0); r.m = DualT::seed(mdir, 1); r.n = sqrt(DualT(1.0) - r.l*r.l - r.m*r.m)` as the seeded direction — the implicit-invert math (`J = ∂stop/∂[slot0, slot1]`, solve for tangents) is structurally unchanged; only the slot semantics swap. The two `compute_exit_pupil_waves_t` calls now pass `is_infinite=(is_finite_opd ? 0 : 1)`. The dispatcher in `batched_eval.cpp` computes both flags from the base `surfaces[0].thickness` + `config->field_type` + `config->ray_aiming`; the seed flag stays `0` for ObjectAngle finite and Off-mode + finite ObjectHeight (those need additional Dual chains and stay in scope for a later phase). **Validated**: probe now bit-matches at machine-precision relative noise — `|Δr|_max = 2.33e-10` on residuals up to 204 waves (rel ~1e-12), `|ΔJ|_max = 8.73e-11` (vs 1e-8 threshold, 100× headroom), `|Δmerit|_max = 1.76e-10` on merit ~155 (rel ~1e-12). Tolerance for the residual + merit checks is relative 5e-12 with a 1e-9 absolute floor, mirroring the FP64 noise floor for the extra surfaces[0].thickness=500 mm propagation step the finite-conjugate trace adds. Phase 8c-2 FNumber probe stays at 3.55e-15 / 7.11e-15 (zero mismatches); existing infinite-conjugate OPD test sites updated to pass `/*is_finite_opd*/0, /*direction_seed*/0`. Remaining finite-conjugate OPD GPU gaps: ObjectAngle finite (needs `dya_dvar = -tan(field) · d(z1a)/dvar` chain into `r.y`); Off + finite ObjectHeight (needs `EP_Dual` on the device for direct Dual launch construction). Both filed as task #113.
- **Task #112 — multi-field WAVEX post-pass X-symmetry now keys off `hx`, not `hy`**. Probe I's 2-field configuration (hy=0° on-axis + hy=5° off-axis fields, each with a WAVEX rings=6, arms=12 group) was diverging at residual magnitude ~3.37 / Jacobian ~3.97e-2 between batched-CPU (`useGpu=false`) and the single-trial Analytic reference. Per-op residual dump showed ops 0-5 (the on-axis group, hy=0) bit-matching while ops 6-41 (the off-axis group, hy=5°) all carried a constant offset with CPU values looking "pre tilt+piston removal" and GPU values "post tilt+piston removal" — pointing at the host post-pass in the dispatcher. The merit-function single-trial reference at `merit_function.cpp:1488` gates X-symmetry mirror-doubling by `use_symmetry = std::abs(operands[i].hx) < 1e-10` (mirror-double only when X-field is zero — the geometric symmetry-plane condition); the dispatcher's matching host post-pass at `batched_eval.cpp` was checking `std::abs(opd_post_hy[pg]) < 1e-10` instead — wrong variable. On a 1-field on-axis configuration this happened to agree (both hx and hy were zero); on the Probe I 2-field configuration, the off-axis group had hx=0 (Y-only field) but hy≠0, so the dispatcher disabled mirror-doubling while the single-trial reference kept it on — produced exactly the constant per-op offset the dump showed. Fix: track `opd_post_hx[kMaxOpdGroups]` alongside the existing `opd_post_hy` array, gate post-group matching by hx as well as hy, and key `use_sym` off `opd_post_hx[pg]`. Verified: Probe I 2-field now bit-matches at `|Δresidual| = 1.97e-11`, `|ΔJac| = 4.94e-13`, `|Δloss| = 1.98e-12` (machine-precision noise floor — both losses report identical 3.72979110881); Phase 8c-2 FNumber probe still passes at `|Δresidual| = 3.55e-15` / `|ΔJac| = 7.11e-15` with zero mismatches; Phase10Bench Tanabe `C++ Analytic Broyden=T` unchanged at 176 trials × 11 ms/trial with final merit 8.437E-01. The bug had been latent since Phase 7d's host post-pass landed because Probe D (1-field × 1-wave on real .lhlt) and Probe H were the only cross-tests exercising the multi-group path, and both happened to have hx=0 on all their fields.
- **Phase 4a-validation (task #78) — Probe I added to Phase5LhltValidation as a synthetic-system WAVEX cross-test**. The in-source comments at `test_analytic_proto.cpp:3538` had been calling for "a C# cross-test that calls `MeritFunctionEvaluator.ExpandMacros` on a real WAVEX(rings=6, arms=12) merit and compares useGpu=true vs useGpu=false" — the gtest fixtures up to now used hand-built `_WAVEX1` operands that *might* not match real macro expansion's wire format. Probe I closes that: builds a synthetic 4-surface achromatic-doublet system in-memory (no `.lhlt` file dependency), adds a WAVEX(rings=6, arms=12) macro, calls the **real** `MeritFunctionEvaluator.ExpandMacros` to expand it, then hands the resulting flat operand list to `evaluate_merit_batched(useGpu=false)` and `evaluate_merit_batched(useGpu=true)` and compares residuals + Jacobians + per-trial loss at machine-precision thresholds (`|Δresidual| ≤ 1e-9`, `|ΔJac| ≤ 1e-7`, `|Δloss| ≤ 1e-9`). On the synthetic 1-field configuration: **CPU and GPU bit-match at 1.97e-11 residual / 0.0 Jacobian / 5.66e-12 loss** — confirms ExpandMacros + Phase 4a's `_WAVEX1` kernel + Phase 7d's tilt+piston host post-pass are sound for single-(field, wave)-group WAVEX. Total Phase5LhltValidation failure count drops 18 → 17. The probe's 2-field variant surfaced a separate divergence (~3.37 residual / ~3.97e-2 Jacobian) specific to the multi-group host post-pass on the synthetic system — filed as task #112 with a localization plan because Probe D (1-field × 1-wave on real .lhlt) had been bit-matching too, so this is the first cross-test that actually exercises the multi-group path with the same merit going to both engines.
- **Phase 4c (task #79) — RY kernel gains runtime output-axis flag; RX and `_TRCX` now route through GPU**. Pre-Phase-4c, the RY GPU kernel always read `eres.y` for the output value and Jacobian, so anything that wanted `ray.x` (`RX` operand, or the SPOTM `_TRCX` hidden op whose mirror-symmetry value reduces to raw `ray.x`) fell back to the CPU path via the explicit `batched_eval.cpp:337` `_TRCX` rejection plus a "RX dispatch path is a known pre-existing gap" comment. Phase 4c adds `int output_axis` to both `cuda_run_ry_jacobian` (scalar) and `cuda_run_ry_batched_jacobian` (per-op `const int*` of length `num_ops`, so a single batched launch can carry any mix of RY and RX operands). The kernels pick `eres.y` vs `eres.x` at the scatter site (`kernel.cu` — `const auto& out_comp = (axis == 0) ? eres.y : eres.x;`) — the Dual<W+2> chain is structurally identical in either component, so adding the axis pick is a one-line refactor at the output write. The dispatcher in `cuda_dispatch.cpp` allocates `d_output_axis` (single `int32_t` for the per-op kernel; `num_ops × int32_t` for the batched kernel) and HtoD-copies the per-op array built in `batched_eval.cpp`'s operand-classification loop (`(cls[a_idx].family == ClassifiedOp::RAY_X) ? 1 : 0`). The `_TRCX` rejection at `batched_eval.cpp:337` lifts. Verified: Phase 8c-2 FNumber probe still passes at machine precision (max |ΔJac|=7.11e-15, zero mismatches) — the per-op axis array plumbing doesn't regress the pure-RY ObjectAngle infinite path; Phase10Bench Tanabe `C++ Analytic Broyden=T` runs at 176 trials × 11 ms/trial with final merit 8.437E-01 bit-identical. COVERAGE.md §2g RX row flipped from ✗fb to ✓ for both Batched-CPU and GPU columns.
- **CLI cleanup (task #102) — multi-config orphans removed from the LensHH-LT CLI surface**. Dropped four out-of-scope hint sites that referred to features only the planned LensHH-Pro will expose: the `config=N` parameter on `merit add` (`MeritCommand.cs:564`), the `config=N` parameter on `pickup add` (`PickupCommand.cs:124` plus the `[config=N]` chunk in its inline help block at :17 and the usage error at :91), and the `'system field-variable'` / `'config variable'` references in the `optimize`'s no-variables-defined warning (`OptimizeCommand.cs:727`) and the `variable list` empty-list hint (`VariableCommand.cs:64`). The underlying data-model fields (`ConfigurationNo` on `MeritOperandDesc`, `SourceConfigurationIndex` on the pickup record) stay so a Pro-authored `.lhlt` file with multi-config metadata round-trips through LensHH-LT cleanly — only the CLI surface for setting them at author time is gone. Rationale: per `project_repo_layout` and the COVERAGE.md §7 production-blockers list, **multi-configuration and FieldY variables are explicitly out of scope for LensHH-LT and are planned for LensHH-Pro**; leaving advice that pointed at "use `config variable`" or "use `system field-variable`" was actively misleading because no such subcommand exists in LT.
- **Phase 7e (task #85) — Phase5LhltValidation re-run at N=200 on all 4 .lhlt lenses**. Bumped `const int numTrials = 32` → `200` in all four N=32 probes (B Real, C synth-EFL, E Off, F Robust, plus the synthetic Probe-D 1f×1w) in `Phase5LhltValidation/Program.cs`, rebuilt, and ran against the current native DLL containing the full Phase 6→9 + #95→#110 + #96 cascade landed today. **Result: 17 probes failed at N=200, identical to the 17 that fail at N=32.** Same set, same mismatch magnitudes — Probe D's machine-noise floor scales from 2.34e-11 (N=32) to 4.67e-11 (N=200), which is just a larger pool, not a degradation. Bit-equal cells (Probes C, D, H) stay bit-equal; pre-existing mismatch cells (B, E, F, G) carry the same diffs they had at N=32. The long refactor cascade from this session did not introduce any new validation regression and did not break any previously bit-equal probe. Pre-existing failures (out of #85 scope, all have separate root causes): Probes B/E/F's WAVEX-with-curvature-vars residual mismatch (~1.6–2e3 magnitude on WideAngle-class lenses, same root cause class as task #111's OPD finite-conjugate work); Probe G's FNumber + N=1 mismatch (~1e6 = FailedGroupPenalty triggering on one side; the Phase 8c-2 per-trial fix doesn't help at N=1 because there's no perturbation); Probe F's CPU NotSupportedException on lenses where the underlying config aiming is Robust (the batched-CPU per-trial worker path doesn't yet support Robust + Analytic; single-trial does post-Phase 9).
- **Phase 8d-2 (task #96) — per-(trial, group/slot) `aim_to_stop_finite` plumbing in the OPD GPU dispatcher**. Phase 8d lifted the dispatcher rejection of `FieldType::ObjectHeight` (and finite conjugate via Phase 8e), but the per-trial OPD aim loop was still unconditionally calling `aim_to_stop` (the infinite-conjugate Newton secant) with the chief direction replicated to every slot — silently wrong for finite conjugate where each pupil ray heads from a fixed object point toward its own `(px·R, py·R)` at the stop, and each trial's perturbed EP/EFL yields a different converged direction. Phase 8d-2 detects finite conjugate at the per-trial level (`std::isfinite(trial_surfs[0].thickness)`) and branches: for finite ObjectHeight or finite ObjectAngle, the chief and per-slot aim loops compute initial launch direction via the closed-form `(ld0, md0, nd0)` geometry from `analytic-derivatives-plan.md:150` and refine via `aim_to_stop_finite`; ObjectAngle infinite keeps the existing `aim_to_stop` path unchanged. Six new per-(trial, group/slot) scratch slots carry the converged directions; the dispatcher passes per-trial-major arrays to a kernel signature change in `cuda_run_opd_jacobian` (chief from `[num_groups]` to `[num_trials × num_groups]`, pupil from `[n_opd_slots]` to `[num_trials × n_opd_slots]`); the OPD kernel in `kernel.cu` reads `g_chief_launch_l[trial * num_groups + group]` and `g_pupil_launch_l[trial * n_opd_slots + slot]` at thread entry. `batched_scratch_size` bumped by `3 × kMaxOpdGroupsBound + 3 × num_operands` doubles per trial. Verified: Phase 8c-2 FNumber probe (ObjectAngle infinite + FNumber) continues to pass at machine precision (max |ΔJac|=7.11e-15, zero mismatches), confirming the per-trial direction plumbing didn't regress the working path; Phase10Bench Tanabe Analytic still runs at 176 trials × 11 ms/trial with final merit 8.437E-01 bit-identical. The new `GpuPathMatchesCpuPath_EflOpd_ObjectHeight` probe (1 EFL + 5 `_WAVEC1` operands × 128 trials × ObjectHeight at 500 mm object distance) is filed `DISABLED_` because full bit-equality requires two kernel-side follow-ups beyond Phase 8d-2's plumbing scope: the OPD kernel's reference-sphere construction differs for finite vs infinite (single-trial `evaluate_opd` has its own branching the GPU doesn't mirror — gives ~447 residual diff today), and the launch direction's var-dependence through `z1a = EP + obj_thick` isn't threaded as Duals so Jacobian tangents miss that contribution (~8690 max Jacobian diff). Both tracked as task #111 (Phase 8d-3). The plumbing template that landed here — per-trial scratch slot + per-trial-major kernel signature + per-(trial, slot) HtoD + indexed kernel read — is exactly what subsequent finite-conjugate kernel work will build on.
- **Task #110 — three `RayStateT<DualW>[kMaxSurfaces]` scratch arrays moved off the stack**. Completes the stack-overflow remediation begun by tasks #108 (root-cause analysis) and #109 (dasph move). The three arrays — `sd_per_surf` (SD argmax retrace cache), `sens_per_surf_d` (SENS trace cache), and `per_surf_d` (RI/RE angle path) — were ~244 KB each (`RayStateT<DualW>` = 11 `Dual<10>` fields + a `RayStatus` enum, 122 doubles = 976 bytes × 256 surfaces). Combined ~730 KB on stack per analytic-mode tile iteration. They now live in a single caller-allocated `ray_scratch_buf` carved into 3 contiguous regions, sized `num_surfaces × kRayStateScratchPerSurfaceDoubles(=366)` doubles (~57 KB for Tanabe-class, vs ~730 KB stack before). New header constants `kRayStateDualWDoubles(=122)` and `kRayStateScratchPerSurfaceDoubles(=366)` exposed in `include/lenshh/merit_function.h`. A `static_assert` in `evaluate_merit_jacobian_analytic` confirms `sizeof(RayStateT<DualW>) == kRayStateDualWDoubles * sizeof(double)` so the buffer math stays correct if `RayStateT` fields ever change. Plumbed through the same three-layer pattern as task #109: single-trial `evaluate_merit_and_jacobian` adds `ray_scratch_buf` (defaulted nullptr); batched `evaluate_merit_batched` slices per-trial by `t`; C ABI wrappers in `export.cpp` forward; C# `NativeMeritEngine` allocates `_rayScratchBuf` in `EnsureBuffersForShape` and reuses it across calls. Function stack frame drops from ~828 KB (post-#109) to ~98 KB — **a 87% reduction**. Test-suite stack workaround removed: `tests/CMakeLists.txt` no longer needs `/STACK:8388608`; `lenshh_tests.exe` runs on Windows' default 1 MB thread stack with comfortable headroom. Validated end-to-end: Phase 8c-2 FNumber probe still passes at machine precision (max |ΔJac| = 7.11e-15, zero mismatches); bisect probe (single-trial direct Analytic) runs in 1 ms; Phase10Bench Tanabe `C++ Analytic Broyden=T` runs at 272 trials × 11 ms/trial, final merit 8.437E-01 bit-identical. The four caller-allocated analytic-scratch buffers (`operand_duals_buf`, `arith_rows_buf`, `dasph_buf`, `ray_scratch_buf`) now cover every stack allocation that used to exceed comfortable thread-stack limits; if a future feature introduces a new large scratch array, the pattern to follow is documented in three places: the project_analytic_stack_overflow memory, the comment block on each buffer in `merit_function.h`, and the `EnsureBuffersForShape` allocation block in `NativeMeritEngine.cs`.
- **Task #109 — `dasph` aspheric-tangent scratch moved off the stack**. Follow-up to task #108's stack-overflow root cause analysis. The 176 KB `DualW dasph[kMaxSurfaces=256][kMaxAsphericCoeffs=8]` array inside `evaluate_merit_jacobian_analytic`'s tile loop is now caller-allocated via a new `dasph_buf` parameter, sized `num_surfaces × kMaxAsphericCoeffs × kAnalyticDualWDoubles` doubles (= num_surfaces × 88 doubles, e.g. 21 surfaces × 88 = 14.4 KB for Tanabe-class). New constants `kAnalyticTileWidth` (8) and `kAnalyticDualWDoubles` (11) exposed in `include/lenshh/merit_function.h` so the C# bridge and tests can size buffers without duplicating magic numbers. Plumbed through three API layers: `evaluate_merit_and_jacobian` (single-trial), `evaluate_merit_batched` (CPU OpenMP per-trial worker slices the buffer by `t`), and the C ABI wrapper in `export.cpp`. C# `NativeMeritEngine` allocates `_daspBuf` in `EnsureBuffersForShape` and reuses it across calls like the other analytic scratch buffers; reuse cost is ~14 KB per Tanabe-class instance, negligible. The reinterpret cast pattern (`DualW (*dasph)[kMaxAsphericCoeffs] = reinterpret_cast<DualW(*)[kMaxAsphericCoeffs]>(dasph_buf)`) preserves both the existing `dasph[s][a]` element indexing and `dasph[si]` row-pointer decay used by `trace_ray_t`, so the rest of the analytic function is unchanged. Validated end-to-end: Phase 8c-2 FNumber probe still passes at machine precision (max |ΔJac|=7.1e-15, zero mismatches); minimal-config bisect probe still passes; Phase10Bench Tanabe `C++ Analytic Broyden=T` runs at 272 trials in 3s at 11 ms/trial, final merit 8.437E-01 bit-identical to pre-#109 baseline. Function stack frame drops ~975 KB → ~828 KB, **a real 15% reduction** — but **not enough to lift the `/STACK:8388608` workaround for `lenshh_tests.exe` on its own**, because the remaining locals (`RayStateT<DualW> sd_per_surf[256]` / `sens_per_surf_d[256]` / `per_surf_d[256]` at ~244 KB each, plus `DualW dcurv/dthick/dconic[256]` at ~66 KB combined) still sum past Windows' 1 MB default thread stack under gtest's per-test overhead. Task #110 filed for the same treatment of those three RayStateT arrays. The dasph buffer pattern there is the template.
- **Stack-overflow root cause for the Analytic hang surfaced by the Phase 8c-2 probe** (task #108). When first writing the FNumber bit-equality test, the CPU per-trial Analytic call never returned — the test infrastructure also showed pre-existing GPU-vs-CPU tests failing with rc=-2, and Tanabe-class Analytic through Phase10Bench worked, so the immediate suspect was Phase 6 batched plumbing. Bisecting down to single-trial direct Analytic with 1 EFL operand and 1 trial reproduced the hang at the public-entrypoint print "dispatching to analytic" with the static `evaluate_merit_jacobian_analytic`'s first body statement never executing. Stack-frame audit: the function declares `DualW dasph[kMaxSurfaces=256][kMaxAsphericCoeffs=8]` (147 KB) plus `dcurv/dthick/dconic` (~55 KB) plus `SdArgmax sd_argmax[256]` plus a half-dozen ray-state arrays — total ~200 KB stack frame. lenshh_tests.exe defaulted to Windows' 1 MB thread stack; gtest + the test's own setup consumed enough that the 200 KB function prolog hit the guard page and the OS handler silently spun (no SEH dialog). Mitigated by linking lenshh_tests.exe with `/STACK:8388608` (8 MB reserve) in `tests/CMakeLists.txt`; the FNumber bit-equality test now runs and **proves Phase 8c-2 end-to-end at machine precision**: 128 trials × (1 EFL + 5 RY) × FNumber F#=4 ProtoSystem with 10× the EPD-baseline curvature perturbation, max |ΔResidual| = 3.5e-15, max |ΔJacobian| = 7.1e-15, max |ΔMerit| = 1.8e-15, **zero mismatches across the full ~4,900-entry comparison**. Also kept the minimal-config bisect probe (single-trial Analytic on EFL-only ProtoSystem) as a permanent regression sentinel — runs in 1 ms and catches anyone reverting the stack-fix. Task #109 filed for the proper architectural follow-up: move `dasph` to caller-allocated scratch (matching the Phase 9f-followup `operand_duals_buf` pattern) so the function frame stays bounded regardless of the static caps, removing the test-stack workaround and protecting production .NET callers (also default 1 MB) from silent hangs at the limit.
- **Phase 8c-2 FNumber bit-equality probe + native test-suite signature alignment** — extends `test_analytic_proto.cpp` with a `GpuPathMatchesCpuPath_MixedEflRy_FNumber` test (1 EFL + 5 RY operands, F#=4 ProtoSystem, 10× larger curvature perturbation than the EPD parent so per-trial pupil_radius drift is visible at machine precision). Also brings 12 direct GPU kernel call sites in the same file into compliance with the new `const double* pupil_radii` signatures (uniform-array pattern: `std::vector<double> pupil_radii(kNumTrials, kPupilR)` declared right before each kernel call). `evaluate_merit_and_jacobian` declaration gains defaulted-nullptr for `operand_duals_buf`/`arith_rows_buf` so the half-dozen single-trial test files (test_bounded_operand, test_aspheric_variable, test_conic_variable, test_arithmetic_operand, test_ct_boundary_operand, …) compile against the post-Phase-9f-followup signature without code changes — they'll still fail at runtime if they request Analytic mode without supplying buffers, but the build is unblocked. The new FNumber test is marked `DISABLED_` because it surfaces a pre-existing CPU per-trial Analytic-mode hang on the synthetic ProtoSystem + RY operand config (FD-mode variant runs in 108 ms with residuals matching at 3.5e-15, confirming scaffolding + GPU dispatch + per-trial scratch buffer plumbing all work correctly; the hang is real and needs root-causing before the bit-equality assertions can run). Tanabe-class merits via Phase10Bench `--use-batched` continue to complete cleanly, so the Analytic batched path isn't generically broken — the hang is specific to this synthetic config. Tracked as task #108 for follow-up.
- **Phase 8c-2 per-trial pupil_radius in GPU dispatcher + all 5 kernels** — closes the FNumber bit-equality gap that Phase 8c left open. Phase 8c lifted the FNumber rejection by computing `pupil_radius` once on the base surfaces (a single `compute_pupil_radius(surfaces, ...)` call before the per-trial loop) and reusing that scalar for every trial in the GPU batch; for EPD aperture this is correct (system-independent), but for FNumber's `|EFL|/(2·F#)` the per-trial perturbed-system EFL drifts ~sub-percent under optimization, so every GPU trial saw a slightly wrong launch position — diverging from the CPU per-trial path that computes pupil_radius from each trial's actual perturbed surfaces. Phase 8c-2 makes it per-trial throughout: a `pupil_radii[num_trials]` slot is carved from the GPU dispatcher's scratch and filled inside the host pre-prep loop (`compute_pupil_radius` on `trial_surfs` for FNumber; `base_pupil_radius` for EPD). All 5 GPU kernel signatures (`cuda_run_ry_jacobian`, `_spot_`, `_opd_`, `_ry_batched_`, `_sd_`) change `double pupil_radius` → `const double* pupil_radii`; cuda_dispatch.cpp allocates/HtoD-copies a per-trial device buffer per kernel call; kernel.cu reads `g_pupil_radii[trial_id]` into a local scalar at thread entry, so all existing marginal-trace code (`(0, pupil_radius, 0, 0, 1)` seed) keeps working unchanged. CPU per-trial path smoke (Tanabe Multistart, 5s): 432 trials × 12 ms/trial Analytic Broyden=T, final merit 8.437E-01 bit-identical to pre-Phase-8c-2 — confirms no regression on the CPU half of the API.
- **Phase 6 batched-CPU dispatch** (`MultistartOptimizerBatch` end-to-end). The `evaluate_merit_batched` device=0 path now plumbs per-trial analytic scratch (`operand_duals_buf` + `arith_rows_buf`) through the per-trial worker, so each OpenMP-parallel trial gets its own `Dual<W=8>` scratch slice. Native signature adds two pointer params (defaulted nullptr in the C++ namespace header for backwards compat; explicit non-null in the C-ABI for the C# bridge). C# `NativeMeritEngine.ComputeBatchedResidualsAndJacobian` allocates the buffers per call sized to `numTrials × nOps × 9` doubles + `numTrials × nOps` int32. Also extends Phase 9h.2's Abso (`|x|`) op_code support into the batched dispatcher (`batched_eval.cpp:309` gate + post-switch sign-flip), so curvature-only lenses with bounded thickness operands (CTA/CTG/EA/EG/DTRG → Abso hidden ops via macro expansion) route through batched without rejection. **A/B measured on curvature-only Tanabe** (18 curvatures, 8 ops pre-expansion, 1200 trials Analytic Broyden=T): per-trial CPU at 25 ms/trial vs batched-CPU at 45 ms/trial — final merit bit-identical (8.437E-01) confirming correctness, but per-trial CPU is **~1.8× faster** than batched-CPU on this lens. Lock-step LM (every trial bounded by the slowest-converging in the batch) doesn't amortize on 16-way parallelism — batched only pays off on GPU's 1000-way parallelism. The Phase 6 wiring IS the GPU dispatch plumbing: `useGpu=false` → `useGpu=true` and CUDA kernels take over the same API. Phase10Bench gains `--use-batched` and `--batch-size N` flags; the 6-cell per-trial grid switches to a 2-cell batched-CPU grid (Analytic only). Limitation: `MultistartOptimizerBatch.Optimize()` runs exactly `BatchSize` trials per call and exits — bench `--duration` is ignored, match trial counts via `--batch-size N` for fair A/B.
- **SENS analytic trace caching** (Phase 9f follow-up). The SENS emit branch now caches the Dual ray trace per `(hy, px, py, wave_index)` via an `ensure_sens_trace` lambda — last-key match, mirroring Phase 9e's `retrace_sd_argmax` pattern. The SENS macro emits F×W×P×(N-2) `_SENS1` hidden ops grouped by trace key, with each group sharing the *same* ray and reading a *different* target surface. Without the cache, every `_SENS1` re-traced the same ray (~19× redundant work on Tanabe-class SENS merits, where (N-2)=19 surfaces per group). **Measured on Tanabe + SENS (~12,500 expanded ops)**: C++ Analytic Jacobian mean time drops from 413 ms to 37 ms (an **11.2× speedup**); Analytic is now ~34× faster than C++ FD on this lens (37 ms vs 1,262 ms). Trial throughput: 192 trials/30s with C++ Analytic Broyden=T vs 48 trials/30s with C++ FD — exactly the win the FD-vs-Analytic ratio promises on SENS-bearing merits. All 10 Phase 9 probes still pass; SENS Jacobian is bit-identical to FD at the noise floor (5.4E-09).
- **Analytic operand cap removed; scratch is now caller-allocated.** The previous `kMaxArithOps = 1024` hardcoded limit caused SENS-bearing merits (Tanabe + SENS expands to ~12,500 `_SENS1` hidden ops) to be rejected by the analytic dispatcher with `rc=-2`, forcing FD fallback. The fix matches the existing scratch-buffer pattern: `lenshh_evaluate_merit_and_jacobian` now takes two additional buffer parameters (`operand_duals_buf`, `arith_rows_buf`); the C# `NativeMeritEngine` sizes them to actual `nOps` and reuses across calls. No hardcoded limit, no per-call allocation, scales with the lens. The C++ side never allocates — CLAUDE.md no-heap rule satisfied. All 10 Phase 9 probes still pass; regular Tanabe (no SENS) Analytic Jacobian timing unchanged (Jac mean 13 ms — preserved). On Tanabe + SENS, C++ Analytic now runs at all — though it's not faster than C++ FD on this specific lens because every `_SENS1` operand currently does its own `trace_dual` with no per-`(hy,px,py,wave)` caching. Adding that caching (mirror Phase 9e's `retrace_sd_argmax` pattern) is a focused follow-up that should restore the expected ~4× Analytic-vs-FD ratio on SENS-bearing merits.
- **OPD afocal analytic + value path in C++ single-trial** (Phase 9g) — the second Tier A user-cited production blocker, for riflescopes, telescopes, and beam expanders. Previously `evaluate_opd` at `merit_function.cpp:728` returned false immediately when `config->is_afocal != 0`, forcing the entire merit to FD via C# fallback. Now both the scalar value path and the Dual analytic path compute exit-pupil waves via a tilted plane-wave projection: `waves = 1000 · t1 · n_exit / wl_um` where `t1 = -(chief · displacement) / (ray · chief)`, with infinite-conjugate input correction subtracting the same projection on surface 1 (object-space n=1). Refactor extracted the focal/afocal branch into a single lambda (`compute_exit_waves` for scalar, `compute_exit_waves_d` for Dual), so the focal path is untouched. **Validated end-to-end**: Tanabe with `IsAfocal=true` + injected `_WAVEC1`, residuals bit-identical (0.0E+00), OPD Jacobian row max relative diff = 9.3E-05 (FD truncation noise floor). All Tier A and Tier B user-cited Analytic blockers are now closed.
- **SENS / `_SENS1` analytic Jacobian in C++ single-trial.** The tolerance/sensitivity operand (your stated Tier A priority for production fabrication-error budgeting) now flows through the analytic path instead of forcing a whole-merit fall-back to FD. The Phase 9f emit branch traces a per-operand Dual ray, captures the Dual surface normal and Dual ray direction at the target surface, and computes `HYLD = |n1-n2| · (1 - dot(n, ray)) / 6` as a single Dual chain. `|n1-n2|` is a constant w.r.t. optimization variables (it depends only on wavelength + materials, both fixed during opt) — so the chain is just the Dual dot product, structurally simpler than RI/RE which need a `acos` chain. **Validated end-to-end**: residuals match 0.0E+00 between FD and Analytic; Jacobian row max `|FD - Analytic|` = 5.4E-09 (numerical noise floor for h=1e-6 FD step). This unlocks the SENS macro for analytic optimization — a SENS-bearing merit with many `_SENS1` children now gets the full ~4× Jacobian speedup instead of every Multistart trial cascading to FD.
- **Abso (`|x|`) op_code analytic Jacobian in C++ single-trial.** The most common scalar transform users wrap merit operands in — `|EFL - target|`, `|MAG|`, `|distance|`, etc. — now flows through the analytic path instead of forcing the whole merit to FD. Implemented via post-emit sign-flip (multiply Jacobian row by ±1 based on raw operand value) at the main loop and arithmetic Pass B. Restricted to families that populate `operand_duals[i]` with the raw scalar (smooth, sd-dep, arith, CT/CV boundary, RI/RE angle, TTRACK) — ray-trace operands still reject Abso, a documented limitation. Other op_codes (Sin/Cos/Sqrt/Asin/Acos/Tan/Atn) still fall back to FD. **Also fixes a pre-existing latent bug**: the Phase 9a bounded gate compared the raw value (not the op-code-transformed value) to the operand's bounds — invisible previously because op_code was rejected outright; surfaces now. **Validated end-to-end**: `RunAbsoOpCodeParityProbe` injects `|CV|` with a min bound that forces argmin → negative raw → sign-flip path; Jacobian row negation matches FD at 2.2E-11.
- **RI / RE analytic Jacobian in C++ single-trial.** Angle-of-incidence and angle-of-emergence operands (the bedrock of glass-safety constraints — total internal reflection, AR-coating angle limits, etc.) now flow through the analytic path instead of forcing the whole merit to fall back to FD. New emit branch traces a per-operand Dual ray, computes `acos(|n·d|) × 180/π` through a Dual chain with edge-case clamping (`|n·d|>1`, `1-x²` near zero), and selects argmin/argmax surface with the same bound-violation logic as `evaluate_angle_boundary` — argmax-default to match the value path. **Validated end-to-end**: `RunRiAnalyticParityProbe` injects an off-axis RI operand into Tanabe's merit and confirms the RI row's Jacobian matches the FD reference at 5.9E-08 (FD truncation noise floor for h=1e-6); residuals are bit-identical.
- **CV / CVA / CVG analytic Jacobian in C++ single-trial.** Curvature-boundary operands now flow through the analytic path instead of forcing a whole-merit fall-back to FD. Direct mirror of the Phase 9e CT/CTA/CTG pattern with `dcurv[chosen]` substituted for `dthick[chosen]`; argmin/argmax bound-selection logic identical to evaluate_boundary. **End-to-end parity validated**: a new `RunCvAnalyticParityProbe` injects a max-bounded CV operand into Tanabe's merit, compares FD vs Analytic — residuals match perfectly (0.0E+00) and the CV row's Jacobian matches at 4.9E-11 (FD numerical noise floor). Remaining Tier B Analytic gaps from the COVERAGE audit (`op_code` scalar transforms, RI/RE angle-of-incidence) still fall back to FD; staged as follow-up phases.
- **COVERAGE.md stale TTRACK row corrected** — Phase 9e shipped TTRACK analytic at `merit_function.cpp:3100` but the matrix still showed ✗ FD; now reads ✓.

### Added — C++ engine coverage

- **Bounded operands (min/max)** in C++ single-trial AND batched paths. Piecewise residual + tangent-gated Jacobian matching the C# evaluator's `MeritFunctionEvaluator.cs:240-243` semantics bit-exact. (Phase 9a)
- **Conic variables** in C++ single-trial. The existing `dconic[]` Dual array now gets its seed branch; ray-trace operands chain through conic tangents automatically. (Phase 9b)
- **Aspheric coefficient variables + templated aspheric intersection** in C++ single-trial. New `intersect_even_asphere_t` Newton + implicit-differentiation solver, with `even_asphere_sag_t` and `even_asphere_normal_t` helpers in `raytrace_diff.h`. Also fixes a pre-existing latent bug where aspheric surfaces were silently traced as conics in the analytic Jacobian path. (Phase 9c)
- **Arithmetic operands** (`MULTC`, `SUM`, `SUMR`, `DIFF`, `MULT`, `DIV`, `DEV`, `QSUMR`) in C++ single-trial analytic Jacobian. Value-path was already complete; analytic adds per-tile operand-Dual storage + chain-rule Pass B. (Phase 9d)

### Added — GUI

- **DEV engine + derivative selector** in the Multistart and Basin-Hopping dialogs. Lets the user pick C# vs C++ (Native) and FD vs Analytic per run. Defaults to the existing C# + FD behavior of 1.0.114.

### Fixed — Multistart

- **HJ pre-step no longer dominates first-batch wall time.** Previously every Multistart trial ran Hooke-Jeeves before LM with a 50-outer-step default; on a 36-variable design that's ~3,650 merit evals **before LM even starts**, ~76 sec/trial on Tanabe. New `MultistartSettings.HjOnGlassSwapOnly` (default true) confines HJ to glass-swap trials — the only case the algorithm comment cites it as helpful (crossing the post-swap index discontinuity). Continuous-variable trials skip HJ entirely; LM's Marquardt damping handles smooth perturbations on its own. Default `HjStepsPerTrial` also dropped from 50 → 10 for the glass-swap path. Basin-Hopping unchanged (HJ is core to that algorithm, not a pre-step).
- **C++ Analytic now actually runs in Multistart trials.** The native DLL bundled in the prior 1.0.115 build was compiled before the Phase 9e/9e-2 analytic Jacobian source landed, so every "C++ Analytic" trial silently threw `NotSupportedException` and was swallowed by Multistart's per-trial catch. On Tanabe (36 vars, WAVEX + bounded ops) Analytic now runs at ~28k logical evals/sec with Broyden off — about 3.5× the C++ FD rate, matching the expected 1-vs-37 perturbation ratio. Fresh `lenshh_native.dll` is bundled here.
- **Multistart's per-trial catch now logs the first failure.** Previously `catch { /* trial failed, skip */ }` made any per-trial exception (NotSupported, NaN, etc.) silently disappear, so a fully-broken cell looked like "few evals, no error." First failing trial per `Optimize()` call now writes its `GetType().Name + Message + StackTrace` to stderr; subsequent trials in the same run stay silent to avoid spam. Surfaced the Analytic gap above in seconds when a longer benchmark wouldn't have.
- **`NativeMeritEngine` now caches marshaled buffers across calls and reuses a single `MeritFunctionEvaluator` for macro expansion** (Phase 9-bridge perf). Previously each `ComputeResidualsAndJacobian` and `GetResiduals` call allocated ~12 fresh native-layout arrays (`NativeSurfaceData[]`, `MeritOperandDesc[]`, indices, scratch buffers, jac) and built a fresh expander, ~50 KB per Jacobian on Tanabe-class designs. Now buffers are sized on first use, reused across iterations, and refreshed only when the system shape changes. Measured ~35% improvement in LM iter-rate on Tanabe C++ Analytic / Broyden=F (794 → 1,072 iters/sec). Parity verified: Phase 9e probe RMS 0.843715 bit-identical to pre-refactor.
- **C++ Release build now uses `/arch:AVX2 /Ob3 /GL /LTCG`** (or `-O3 -march=native` on Clang/GCC). The Dual<W=8> arithmetic in the analytic Jacobian path is 8-wide vector work that benefits from AVX2's 256-bit registers; aggressive inlining + LTCG let the templated `trace_ray_t<T>` instantiations cross-inline across compilation units. `/fp:fast` NOT enabled — kernel.cu validation depends on strict IEEE fp64.
- **Counter fix for Analytic Jacobian credits.** `LocalOptimizer.ComputeJacobian` was unconditionally crediting `nVars + 1` evaluations to the counter for any native call. For FD that's correct (one base + one per perturbed column). For Analytic it over-credited by ~37× — Analytic is a single forward-mode Dual-chain pass, not 37 perturbed traces. Effect on the bench: Broyden=false looked artificially 2× faster than Broyden=true because each Broyden=false iteration accrued 38 to the counter vs ~1 for Broyden=true. With the fix, the bench correctly shows Broyden=true running ~4-5× more LM iters/sec than Broyden=false on Analytic — matching the algorithm intuition.
- **`ParallelEvaluation` default flipped from false to TRUE on `LocalOptimizer`.** LM does ~1 Jacobian call + 3-5 residual-only calls (base, trial, refresh) per iteration. The Jacobian path has its own parallelism (Knob A in C# / Knob C in C++); flipping this on parallelises the *residual-only* calls too, which dominate per-iter wall time in Analytic mode (where Jacobian = 1 eval) and contribute meaningfully in FD mode. No nesting risk because (a) `ComputeJacobian`'s C# column workers explicitly disable it inside their cloned evaluators, and (b) outer-loop callers (Multistart trials, Genetic LM batch, Asphere candidate search) now explicitly set it back to false to avoid double-saturating the pool. Standalone LM ("Optimize" button, Basin-Hopping inner LM) gets the speedup; outer-parallel uses are unaffected.
- **`ParallelNativeJacobian` default flipped from true to FALSE on every optimizer (Multistart, BasinHopping, Local, Genetic).** The previous default nested two parallelisms — Multistart's Parallel.For at the trial level **and** OpenMP across the Jacobian's columns inside each trial — so on a 16-thread laptop the optimizer was creating 16 × 16 = 256 threads contending for 16 cores. A/B measurement on Tanabe shows the old default cost 1.8-5.0× across all six Engine × Derivative × Broyden cells (worst: C++ FD Broyden=F went from 1,884 to 9,316 evals/sec — a 4.95× regression). On a 96-core machine the same trap would create 9,216 threads on 96 cores. Set the property to `true` explicitly when running a single big Jacobian without an outer parallel loop.

### Documentation

- New `LensHH-LT-NativeCore/docs/COVERAGE.md` — single-page reference matrix showing what C#, C++ single-trial, C++ batched, and GPU each support across operands, variable types, configurations, and modifiers. Cited at file:line throughout. Updated at the end of every C++ or GPU phase.

### Known limitations (unchanged from 1.0.114)

- C++ batched API (`MultistartOptimizerBatch`) still doesn't accept boundary operand types (CT/CV/CTA/CTG) or Thickness variables — Tanabe-class designs still run through C# for Multistart Batch. Tracked for a follow-up release.
- OPD afocal (riflescopes, telescopes) still falls back to C++ FD in the analytic path. Operand value is correct; switch the Derivative dropdown to FD if you see a NotSupportedException on an afocal lens.
- Multi-configuration (zoom systems) and FieldY variables remain out of scope for LensHH-LT; planned for LensHH-Pro.

## 1.0.114 — 2026-05-22

This entry describes the additions accumulated since the initial
1.0.114 release on 2026-05-17. The version number is unchanged (per
the project's "version bump only on release" convention); all changes
below are in the current `LensHH-LT-Setup-1.0.114.exe` installer.

### Added — merit-function surface sentinels

`Surface1` and `Surface2` on every boundary operand
(`CT` / `CTA` / `CTG`, `ET` / `EA` / `EG`, `CV` / `CVA` / `CVG`,
`SD`, `DTRG`) and every angle operand (`RI`, `RE`) now accept
negative-integer sentinels that resolve dynamically at evaluation
time. These survive surface insert/remove/split-substitution
operations that shift absolute indices.

| Sentinel | Resolves to |
|---:|---|
| `0` | Mirror of the other endpoint |
| `-1` | Surface immediately before IMG (`Count − 2`) |
| `-2` | Image surface (`Count − 1`) |
| `-3` | First surface after stop (`StopIndex + 1`) |
| `-4` | Stop surface (`StopIndex`) |
| **`-5`** | **Surface immediately after OBJ (`1`) — NEW** |

These are position sentinels — they always resolve to the indexed
positions above regardless of material. For a clean design (OBJ → L1
→ … → Ln → IMG) `-5` and `-1` correspond to the first and last
refractive surfaces; for designs that include dummy air surfaces at
position 1 or before IMG, they resolve to the dummy. A material-aware
"true refractive" sentinel is planned for a future release. See
`docs/merit-function.md` § *Surface sentinel values* for the full
reference with worked examples.

### Added — `PenalizeVignetting` system flag

`OpticalSystem.PenalizeVignetting` (GUI: System Properties dialog;
MCP: `set_penalize_vignetting`) is a system-wide boolean. When `true`,
the engine forces every failed ray (on-axis OR off-axis) to receive
the stiff per-ray penalty, overriding the default "off-axis vignetting
is tolerated as a deliberate design choice" behavior. **Recommended on
for stock-lens designs** whose lens diameters are catalog-fixed —
without it the optimizer can buy aberration relief by quietly clipping
the off-axis pupil. See `docs/merit-function.md` § *PenalizeVignetting*.

### Added — Stock-lens design pipeline (MCP)

A new family of MCP tools encapsulates a Sasian-style "free-optimize
then stock-substitute" workflow end-to-end:

- **`build_skeleton`** — one-call seed of a `single-single-single`
  (Cooke triplet) lens stack with a real physical aperture stop in a
  selectable air gap. `stopPosition` parameter: `0` leading, `1`
  between L1 and L2, **`2` between L2 and L3 (default, classic Cooke
  position)**, `3` BFL gap. Marks every curvature, glass thickness,
  and air thickness variable, enables glass substitution against the
  chosen catalog, and rewrites template span operands' `Surface1` from
  `-3` to `-5` so the merit covers every refractive surface regardless
  of where the stop sits.
- **`add_singlet`** — insert a complete singlet (front + back surface)
  with every variable / SemiDiameter / Material invariant set
  correctly. Replaces the 12-call ritual of `add_surface ×N` +
  `edit_surface` + `set_variable` + `set_glass_substitution`.
- **`find_matching_stock`** — ranked candidate search across the
  stock-lens catalog for a target (EFL, Ø, glass index). Patterns:
  `single`, `split_pcx`, `split_pcc`, `split_pcx_pcc` (Sasian-style
  positive-negative plano-to-plano).
- **`replace_element`** — surgical element swap. Removes the chosen
  element's surfaces and splices in stock part(s), including
  multi-part patterns (split pairs, doublets, etc.). Reconnects air
  gaps and updates merit-function references.
- **`relocate_stop_scan`** — convert a buried-pupil design
  (`S1.Thickness < 0`) into one with a physically realizable stop by
  scanning every internal air gap and reporting where the entrance
  pupil lands.
- **`set_stock_glass_substitution`** — enable BH glass substitution on
  every spherical glass surface in the current system using the
  `StockGlassesUV` catalog for systems with `min(wavelengths) < 0.380
  µm` and `StockGlassesVisible` otherwise. Aspheric surfaces skipped.
- **`sasian_design_start` / `_status` / `_cancel` / `_discard`** —
  end-to-end orchestrator: skeleton + free-opt + per-element
  substitution + final all-stock save. Returns a job id; runs in the
  background. Documented in `docs/sasian-design.md` (recipe overview,
  every parameter, Phase 1 vs Phase 2 doublet-retrofit flow, pattern
  + candidate-ranking internals, worked F/2.8 example).

### Added — Stock-lens-derived glass catalogs

Two new filtered AGF catalogs ship in
`catalogs/FilteredGlassCatalogues/`:

- **`StockGlassesVisible.AGF`** — 19 distinct glasses harvested from
  every non-aspheric singlet in the stock-lens catalog (Edmund,
  Thorlabs, Ross Optical). N-BK7 dominates; the rest are Schott
  N-SF11, N-SF5, N-SF8, N-LASF9, N-LASF44, LASFN30, N-BAF10, N-BAK1,
  N-F2, N-SK11, B270, CDGM BAF2, Acrylic, and the Corning HPFS_7980
  fused-silica aliases.
- **`StockGlassesUV.AGF`** — 2 base glasses + aliases: CaF₂ and Corning
  HPFS 7980 fused silica (with `C79-80`, `F_SILICA`, `FUSED`, `SILICA`,
  `FUSED_SILICA` all duplicating the HPFS coefficients).

A regeneration script `catalogs/scripts/Build-StockGlassesVisible.ps1`
rebuilds the visible catalog from the SQLite stock-lens index.

### Added — `sasian_design` orchestrator options

- **`bhNoImprovementSeconds`** (default 600 s) — Basin-Hopping
  watchdog. Each BH phase terminates if best merit hasn't improved
  within this many seconds. The timer resets on every best-merit
  improvement. Setting `bhMaxHops` high (2000) and trusting the
  watchdog as the practical termination criterion. Pass `0` to disable.
- **`monochromaticPhase1`** (default `false`) — opt-in. At pipeline
  start the non-primary wavelength weights are zeroed; the optimizer
  minimizes a d-line-only merit during shape + glass exploration. The
  original weights are restored before the final all-stock save so the
  saved file remains valid for downstream polychromatic work.
- **`startFromCurrentSystem`** (default `false`) — Phase 2 entry. Skip
  `LoadTemplate` / `BuildSkeleton` / free-opt and drop straight into
  the per-element substitution loop on whatever system the caller has
  arranged in the session. Intended for doublet-retrofit workflows:
  load a Phase 1 all-stock design, swap one positive singlet for a
  candidate doublet via `replace_element`, manually unlock one
  neighbor (`set_curvature_variables` + `set_thickness_variables` +
  `set_glass_substitution`), then call `sasian_design_start` with the
  flag to re-substitute the unlocked neighbor with a stock part.

### Changed — Optimizer defaults

- `sasian_design` now uses **Basin Hopping + LM throughout** (was LM
  Multistart). BH's auto-detect respects per-surface variable flags so
  locked stock parts (curvatures + glass thickness fixed) are
  automatically skipped from glass substitution; free-opt skeleton
  elements (variables set by `build_skeleton`) stay eligible.
- BH `LmIterationsPerHop` default raised from `60` to **`4000`** in
  the sasian_design surface — matching the GUI BasinHoppingHjLm dialog
  preset. LM still terminates earlier on tolerance for easy
  sub-problems; the higher cap prevents premature exit on harder ones.
- `build_skeleton` default `stopPosition` changed from `1` (between L1
  and L2) to **`2`** (between L2 and L3) — canonical Cooke-triplet
  position; gives near-symmetry around the negative middle element
  and naturally balances coma and lateral color.

### Fixed — Engine: `SurfaceIndexUpdater` invalidation collision

When a surface was removed, a positive `Surface1` / `Surface2` that
pointed at the deleted surface was being replaced with `-1` to flag
the reference as invalidated. But `-1` is also the documented sentinel
for "last refractive surface," so the invalidation silently aliased to
a valid-looking sentinel — span operands lost their actual coverage
and became degenerate. The optimizer would then violate the original
constraint without registering a residual.

Fix: invalidation now uses `int.MinValue` as an unambiguous marker.
`OnSurfaceRemoved` handles it explicitly per kind — span endpoints
snap to `removedIndex` (the surface that took the slot continues the
boundary role), per-surface refs become inert (`0`), pickups are
dropped.

### Fixed — `sasian_design` element-targeting after split substitutions

The substitution loop used a positional `for elem = 1..N` counter
where `N` was the initial skeleton element count. A `split_pcx` /
`split_pcc` / `split_pcx_pcc` substitution at E1 turns the design from
N elements into N+1, so the next iteration's "element 2" targeted the
split partner E1 just inserted (a stock part) instead of the original
L2. The original L3 was never reached; its variable flags stayed set
from `build_skeleton` and BH+LM was free to morph its geometry into
something that didn't match any real stock part. The saved "all-stock"
file's labels then disagreed with its actual geometry.

Fix: the substitution loop now finds the next unsubstituted element
each iteration by scanning for the next contiguous-glass block whose
front surface has `CurvatureVariable=true`. That flag is precisely the
"is this still a free-opt skeleton element?" marker — `build_skeleton`
sets it on every skeleton element; `replace_element` inserts stock
parts with the flag clear. The flag survives across surface
renumbering caused by split inserts.

### Fixed — `relocate_stop_scan`: preserve glass substitutions

The helper used a private `CloneSurface` loop that did not copy
`OpticalSystem.GlassSubstitutions` (which lives on the system, not on
individual surfaces). Saved candidate files therefore came out with
glass-substitution disabled on every glass surface, which silently
defeated any follow-on Multistart that was expected to swap glasses.
Fix: `BuildBaseSystemWithoutDummyStop` and `CloneSystem` now both copy
`GlassSubstitutions` with `SurfaceIndex` re-mapped for the dummy-drop
and stop-insert.

### Fixed — `evaluate_merit` display: residuals for boundary operands

The MCP `evaluate_merit` tool was recomputing the displayed `Residual`
column with the formula `op.IsTargetActive ? (Value-Target)*Weight :
0` — so every boundary operand (`CTA`, `EA`, `CTG`, `EG`, …) reported
residual = 0 in the diagnostic table even when its value was outside
the configured `Min` / `Max` bounds. The actual merit value at the top
of the table was correct (the optimizer saw the real penalty); only
the per-row display was wrong. Fix: use `op.Residual` directly (which
the evaluator computed correctly for both target-mode and bound-mode
operands).

### Installer — stock-lens catalog now bundled

Previously the Windows installer and Linux AppImage shipped only the
glass AGF tree; users had to clone the repo (or copy `catalogs/`
manually) to use any stock-lens MCP tool. Now both installers ship:

- **`catalogs/stock-lens-catalog.sqlite`** — the index that
  `find_matching_stock`, `search_stock_lenses`, `insert_stock_lens`,
  `replace_element`, and `sasian_design`'s stock-substitution phase
  query at runtime (~7 MB).
- **`catalogs/Lenses/<vendor>/.../<part>.lhlt`** — the 7,623 per-lens
  prescription files referenced by the SQLite (~26 MB). Build-time
  `.zmx` / `.zar` / `.seq` / `.xlsx` originals are excluded.
- **`MISC.AGF`** (already bundled, but worth calling out) — picked up
  automatically by the existing `Glass\*.AGF` glob; provides the
  Corning HPFS 7980 fused-silica aliases (`C79-80`, `F_SILICA`,
  `FUSED`, `SILICA`, `FUSED_SILICA`) that stock-lens prescriptions
  reference. Built from public sources only — no OpticStudio MISC.AGF
  content.
- **`StockGlassesUV.AGF`** + **`StockGlassesVisible.AGF`** — also
  already bundled via the existing `FilteredGlassCatalogues\*` glob,
  but called out here because the stock-lens pipeline depends on them
  for auto glass-substitution.

`LensHH.Mcp.StockLensCatalog.ResolveDbPath` finds the SQLite at
`{app}\catalogs\` automatically; the Linux AppRun additionally
exports `LENSHH_CATALOGS_DIR` so the resolver doesn't need to walk
the directory tree.

Installer size impact: **~+10 MB** after LZMA2 ultra64 compression
(~33 MB raw before compression).

### Fixed — Engine: `RayTracer` infinite-conjugate threshold

`RayTracer.cs` line 134 only treated `double.PositiveInfinity` /
`NaN` as "infinite thickness" when deciding whether to skip ray
propagation. Large-but-finite sentinels commonly used as "infinity"
in optical CAD (1e10, 1e18, 1e20 — emitted by OpTaliX, Code V,
legacy ZMX, and the GUI's own infinity convention) were treated as
finite and the trace literally propagated rays through 10^18 mm.
For any off-axis field, `ray.Y` at surface 1 became astronomical
(`tan(14°) × 1e18 ≈ 2.5 × 10^17 mm`). Downstream `SystemLayout`
then auto-scaled the viewport to ~5 × 10^17 mm and the lens stack
rendered microscopic / invisible. Threshold now matches the
existing convention used elsewhere in the engine:
`Math.Abs(thickness) >= 1e10`. Engine commit `2985898`.

### Fixed — Engine: SPC `IsSurfaceCovered` uses post-insertion system

`SpcSynthesisService.AddOrExtendBoundaryOperand` was passing
`_system` (the service-level original parent system) to
`IsSurfaceCovered`, but `surfaceIndex` referred to the
post-insertion `sys` clone. The sentinel resolver consequently
computed `Count-2` against the *pre-insertion* count, missing the
newly-inserted surfaces, and SPC added redundant per-surface
`CTA(N, N)` / `EA(N, N)` operands on top of an existing
`CTA(-5, -1)` / `EA(-5, -1)` sentinel span. Fixed by threading the
branch's `sys` through `AddCenterThicknessOperands` /
`AddEdgeThicknessOperands` / `AddOrExtendBoundaryOperand`. Engine
commit `684304b`.

### Fixed — GUI: merit-function editor accepts the `-5` sentinel

`MeritFunctionEditorViewModel.cs` had hard-coded `minV = -4` on
`Surface1` / `Surface2` input validation. When the user typed `-5`
in the merit-function grid the setter silently rejected the input
and the cell reverted to whatever the underlying value was —
producing the appearance that "the GUI rewrote -5 to 1." The
engine has supported the `-5` sentinel since `3e1e5c9`; the GUI
input was the lone holdout. Now accepts the full `-5..-1` range.
LT commit `2c43406`.

### Fixed — GUI: `Basin-Hopping HJ+LM` dialog checkbox/label overlap

The "Stop on no improvement" `CheckBox` had `Grid.ColumnSpan="5"`
(covering columns 0-4), but the "Timeout (s):" `TextBlock` next to
it lives in column 4 — the two overlapped pixel-by-pixel and
rendered as the garbled string `Stop on no improvememendut (s):`.
Reduced ColumnSpan to 4 so the checkbox stops cleanly before
column 4. LT commit `5dc760f`.

### Fixed — Installer: `build-installer.bat` line endings

`build-installer.bat` was checked in with LF-only line endings.
`cmd.exe` tolerates LF most of the time but occasionally misreads
the file character-by-character — in particular when invoked via
PowerShell's `& "...bat"` or even `cmd /c`. Symptom: leading
characters of `setlocal` / `cd /d` get eaten (e.g. `'tlocal' is
not recognized`), so `cd /d "%~dp0\.."` silently fails, the
subsequent `dotnet build -c Release` runs from PowerShell's cwd
(often the engine repo), picks up `LensHH-LT-Engine.sln` instead
of `LensHH-LT.sln`, and bombs trying to compile engine `Tests/`
projects. Converted to CRLF; script content unchanged. LT commit
`52f404c`.

### Build & deploy

- LT installer rebuilt via `installer/LensHH-LT.iss`. Filename
  remains `LensHH-LT-Setup-1.0.114.exe`.
