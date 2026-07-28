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

- **GPU image-quality trace** — traces the dense image-quality ray grid (the
  merit function's spot / wavefront sampling) on the GPU. Applies to every
  multi-trial optimizer.
- **GPU pre-screen** — the Multistart candidate sieve: many trial designs are
  scored on the GPU and only the most promising are handed to the CPU optimizer.
- **GPU-resident evolutionary population** — runs the Differential Evolution
  population and its merit evaluation on the GPU.

These are **global settings**, persisted between sessions, rather than per-run
checkboxes. They apply to **Multistart, Basin Hopping, Global Search, and
Differential Evolution**. **Local Optimization always runs on the CPU** — a
single Levenberg–Marquardt chain already uses all cores for its operand
evaluations and does not benefit from the GPU.

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
