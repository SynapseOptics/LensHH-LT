# GPU Acceleration

LensHH-LT can offload the expensive parts of the multi-trial global optimizers
to a CUDA GPU. The GPU options are configured in one place and reported live, so
you can confirm the GPU is actually doing work.

## Requirements

A CUDA-capable NVIDIA GPU with a current driver. Without one, the GPU options
are unavailable and everything runs on the CPU — the results are identical, only
slower on the multi-trial workloads.

## Configuring GPU acceleration (Preferences)

Open **Editors → Preferences**. The GPU section has three independent settings:

- **GPU image-quality trace** — traces the dense image-quality ray grid on the
  GPU. It engages for merits whose image-quality operands are **spot**
  (`SPOT`/`SPOTR`/`SPOTM`), **wavefront** (`WAVEX`/`WAVEM`/`WAVEC`/OPD), or
  **sensitivity** (`SENS`) families, evaluated on a field with off-axis extent
  and with ray aiming off. **On-axis-only merits evaluate on the CPU** (the axial
  pupil is too small to pay for a GPU launch). Applies to every multi-trial
  optimizer, and — besides the Preferences switch — can be turned on from the CLI
  (`optimize multistart|basin gpuimage`) and MCP (`useGpuImageQuality`).
- **GPU pre-screen** — the Multistart candidate sieve: many trial designs are
  scored on the GPU and only the most promising are handed to the CPU optimizer.
- **GPU-resident evolutionary population** — runs the Differential Evolution
  population and its merit evaluation on the GPU.

These are **global settings**, persisted between sessions, rather than per-run
checkboxes. They apply to **Multistart, Basin Hopping, Global Search, and
Differential Evolution**. **Local Optimization always runs on the CPU** — a
single Levenberg–Marquardt chain already uses all cores for its operand
evaluations and does not benefit from the GPU.

## Tuning the pre-screen (CLI, MCP, API)

The Preferences switch turns the pre-screen on or off; it then runs with
defaults chosen to work well without tuning. Two knobs are available to power
users through the CLI, MCP, and API (the GUI intentionally exposes only the
on/off switch):

- **Difference gate** — only candidates structurally different from the running
  best (a glass swap, or a refractive surface whose curvature moved more than a
  set percentage) are fed to the sieve, so it keeps exploring instead of
  collapsing into a pure refiner. Default **2%**; `0` disables the gate.
- **Population / device-fill multiplier** — how large a candidate cloud each
  batch evaluates, as a multiple of the count that fills the GPU. Default
  **1.0** (fill the device once); `2.0` doubles the cloud. This replaced the
  older CPU-relative "oversample" multiplier — the batch now auto-sizes to the
  GPU.

| Surface | Difference gate | Fill multiplier |
|---|---|---|
| **CLI** (`optimize multistart`) | `mincurvchange=<pct>` | `gpufill=<x>` |
| **MCP** (`optimize_multistart_start`) | `gpuMinCurvatureChangePercent` | `gpuPreScreenFill` |
| **API** (`MultistartSettings`) | `GpuPreScreenMinCurvatureChangePercent` | `GpuPreScreenFill` |

For example, `optimize multistart gpu mincurvchange=1.5 gpufill=2` runs the
pre-screen with a 1.5% difference gate over a doubled candidate cloud.

## The GPU status chip

Each optimizer dialog shows a **⚡ GPU** chip. It reflects **ground-truth
activity**: it is driven by counters that increment on actual GPU kernel
launches, not by whether a setting is ticked, so it cannot claim the GPU is
running when it isn't.

- **Bright** when a GPU path is doing work during the run.
- **Dim** when the GPU is idle; **grey** when GPU is off or unavailable.
- **Hover** for a breakdown of which GPU modes are active and their live counts
  (image-quality traces, pre-screen batches, evolutionary generations).

Because it updates *during* the run, you can confirm the GPU is engaged without
waiting for the run to finish.

## What the GPU does — and doesn't

The GPU computes merit **values** — the parallel image-quality ray trace across
trials or pupil samples. It does not change the answer: a GPU run produces the
same merit as the equivalent CPU run, just faster on the multi-trial workloads.
Derivative (Jacobian) work stays on the CPU, so a single Local Optimization step
is unaffected — which is why the GPU is reserved for the multi-trial optimizers,
where there are many independent merit evaluations to run in parallel.
