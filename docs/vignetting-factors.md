# Automatic Vignetting Factors

## What vignetting factors are for

In a real lens the clear apertures of the elements block part of the off-axis
pupil — the field is *vignetted*. An off-axis field point therefore images
through only a fraction of the nominal pupil. If an analysis or the merit
function samples the **full** nominal pupil for such a field, it traces rays the
apertures have already clipped, and the result is a poor estimate of the real
image quality. During optimization it is worse than cosmetic: the merit is
scoring rays that don't exist, so it can point the optimizer in the wrong
direction.

Vignetting factors describe the vignetted pupil for each field as a simple
transform of the normalized pupil coordinates — a **decenter** (the shifted
centre of the surviving pupil) and a **compression** (how much narrower the
surviving pupil is in x and y). Applying them concentrates the sampled rays
inside the aperture that actually passes light, so even a modest ray grid
represents the field correctly.

## Enabling automatic vignetting factors

Turn on **Use Automatic Vignetting Factors** in the System settings. When it is
on:

- The factors are **computed automatically** for every field and
  **recalculated whenever the clear apertures are solved**, so they always
  reflect the current design.
- They are **derived, never hand-edited** — you don't type factor values.
  Turning the option off restores the identity transform (no remap).
- A **read-only per-field factor table** (decenter and compression for each
  field) appears in the **System Data** report, which you can copy or export.

On-axis fields are never given vignetting factors — the on-axis pupil isn't
vignetted — so the axial beam is always evaluated against its true, un-remapped
cone.

## Using them in an analysis

Each analysis (spot, wavefront, MTF, …) carries a **Use Vignetting Factors**
option, shown when the system-level option is on. With it enabled, that analysis
samples the vignetted pupil; with it off, it samples the full nominal pupil.

Analyses that **measure** vignetting directly — relative illumination and the
vignetting-over-field plot — always use the real apertures regardless of this
option, so they report the true light throughput rather than the remapped
sampling.

## In optimization

Optimizing a vignetted system is the main reason to use automatic vignetting
factors. Because the factors are recalculated on every clear-aperture solve, the
merit function's image-quality operands sample the true pupil as the design
changes — you are not setting the factors once and letting them go stale as the
optimizer reshapes the lens. This is what makes it practical to optimize a
design that must vignette to meet its specification, and it is the mechanism
behind [semi-diameter variables](semi-diameter-variables.md), where the
optimizer sizes the clear apertures directly and the factors follow them
automatically.

## When to use it

- **On** for any design with meaningful off-axis vignetting — especially during
  global optimization, and whenever you want image-quality analyses to reflect
  the light that actually reaches the image.
- **Off** for systems without vignetting (it is a no-op there), or when you
  specifically want to inspect the full-pupil result.
