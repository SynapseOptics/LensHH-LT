# Merit Function Reference

The merit function is a list of **operands**. Each operand produces one
value; the optimizer drives the weighted sum of squared residuals to
zero. A residual is the operand's value minus its target (in Target
mode), or the distance outside its Min/Max bounds (in Boundary mode).

## Operand Row — Common Columns

Every operand has these columns in the Merit Function Editor:

| Column | Meaning |
|---|---|
| **Type** | The operand type (e.g. `SPOT`, `EFL`, `CTG`). Determines what the operand computes. |
| **Mode** | `Target` (drive toward a value) or `Min/Max` (penalize if outside a bound). |
| **Target / Min / Max** | The reference value for the chosen mode. |
| **Weight** | Multiplier on the residual. 0 disables the row. |
| **OpCode** | Optional post-processing: `Abs`, `Sqrt`, `Sin`, `Cos`, `Tan`, `Asin`, `Acos`, `Atn`. Default `None`. |
| **Value** | Read-only — the last-computed value. |

The remaining columns depend on the operand type (see each category
below). Columns irrelevant to the selected type are greyed out.

---

## Spot Operands

Diffraction-quality merit — RMS transverse ray aberration over pupil
quadrature.

| Type | Reference | Sampling | Notes |
|---|---|---|---|
| `SPOT`   | Chief ray | Forbes Gauss-Legendre quadrature (`Rings × Arms`) | Tracks TRAD/TRAE per pupil point. |
| `SPOTM`  | Weighted centroid | same | Recommended for final-merit chromatic balancing. |
| `SPOTR`  | Chief ray | Rectangular grid (`GridSize × GridSize`) | Uniform sampling — see *Forbes vs rectangular* below. |
| `SPOTMR` | Weighted centroid | rectangular grid | |

**Inputs:** `Rings` (quadrature rings, 3–20), `Arms` (6, 8, 10, or 12)
for Forbes; `GridSize` (4–100) for rectangular.

**Units:** mm in focal mode, arcmin in afocal mode (automatic).

**Failure handling:** behavior depends on which field the failed
ray belongs to:

- **On-axis (|Hy| ≈ 0):** every vignetted or trace-failed pupil ray
  contributes a stiff per-ray penalty. On-axis vignetting is never
  a legitimate design choice — every defined pupil ray must reach
  the image. Without this penalty, the optimizer could "cheat" by
  letting most of the on-axis pupil get blocked: the spot RMS and
  OPD RMS are evaluated only over rays that survive, so a small
  surviving cluster could produce a tiny RMS while the design
  actually throws away most of the on-axis light. The per-ray
  penalty closes that loophole.
- **Off-axis (|Hy| > 0):** individual vignetted rays are silently
  excluded (residual = 0). Vignetting at field edges is often a
  deliberate aberration-relief choice, so the merit doesn't punish
  it ray-by-ray.
- **Whole-field collapse (any field):** if *every* hidden ray in a
  SPOT / WAVE group fails — or the chief ray fails for a SPOT or
  WAVE group — a synthetic per-group penalty fires so the optimizer
  backs away from designs that drop an entire spot.

### Forbes (`Rings × Arms`) vs rectangular grid

The Forbes scheme places rays on `Arms` evenly-spaced radial spokes
at the `Rings` zeros of a Legendre polynomial in the area variable
`ρ = r²/R²`. The rectangular scheme samples on a uniform Cartesian
grid and discards points that fall outside the unvignetted pupil.
Both estimate the same RMS integral; they differ only in how the
pupil is sampled and weighted.

**Forbes is the default, and it should stay the default for almost
all design work.** The reason is integration efficiency, not pupil
shape. For a smooth integrand — which any unvignetted, well-behaved
optical wavefront is — Gauss-Legendre quadrature converges
exponentially in the polynomial degree it can integrate exactly.
Forbes (1988) showed that for typical lens systems, ~12 rays of
Gaussian quadrature give 1% RMS-spot accuracy where ~75 rays of
the polar-uniform Andersen scheme are required, and a uniform
Cartesian grid needs roughly 500 rays for the same accuracy. In an
optimizer that evaluates the merit thousands of times per run, that
40× ray-count ratio dominates wall-clock time. Defaults of `Rings = 6`,
`Arms = 12` (72 rays per field per wavelength) sit comfortably
inside the regime where the residual quadrature error is well below
the visible aberration content.

There are, however, cases where the rectangular operands (`SPOTR`,
`SPOTMR`, `WAVEXR`, etc.) are the right tool:

- **Heavy or asymmetric vignetting.** Forbes quadrature assumes a
  circular pupil; the Gauss-Legendre weights are derived for the
  full disk. When a substantial fraction of those weighted sample
  points fall in vignetted zones and are dropped, the remaining
  rays no longer carry the correct quadrature weights for the
  *transmitted* pupil. With only a handful of rings to begin with,
  losing two or three of them can bias the RMS estimate noticeably.
  A dense rectangular grid (e.g., `GridSize = 16`–`32`) over the
  same pupil degrades much more gracefully because each surviving
  ray still carries equal weight over its own grid cell — the loss
  is proportional, not weight-distorted. Forbes himself flags this
  in §3 of the 1988 paper: "for cases in which the effects of pupil
  distortion and vignetting need to be determined… simultaneously
  with the integration, pure Gaussian schemes are not possible."

- **Non-polynomial wavefronts.** Strong aspheres, freeforms, or
  diffractive surfaces can produce wavefronts that aren't well
  approximated by a low-order polynomial in `ρ`. Gaussian quadrature
  loses its accuracy advantage there; a denser uniform grid
  estimates the integral more honestly even if it costs more rays.

- **Validation.** When a Forbes-driven optimization converges to a
  surprisingly good merit value, re-evaluating the same design with
  a rectangular grid is a cheap independent check that the result
  isn't an artifact of the quadrature sample placement. If the two
  schemes disagree at the same total ray count, the design is
  probably exploiting structure the Gauss-Legendre points happen to
  miss.

In short: pick rectangular when you have reason to distrust the
smoothness or circularity assumptions Gauss-Legendre is built on.
Otherwise stay with Forbes — the speed difference matters more than
it looks, especially on global-search and multistart runs.

> **Reference:** G. W. Forbes, "Optical system assessment for design:
> numerical ray tracing in the Gaussian pupil," *J. Opt. Soc. Am. A*
> **5**, 1943–1956 (1988).

---

## Wavefront (OPD) Operands

RMS wavefront error over pupil quadrature, in waves.

| Type | Reference | Removed | Sampling |
|---|---|---|---|
| `WAVEX` | Chief ray | Piston **and** tilt | Forbes quadrature |
| `WAVEM` | Chief ray | Piston only | Forbes quadrature |
| `WAVEC` | Chief ray | Nothing | Forbes quadrature |
| `WAVEXR`, `WAVEMR`, `WAVECR` | same | same | Rectangular grid |

**Inputs:** `Rings` + `Arms` for Forbes; `GridSize` for rectangular.
The Forbes-vs-rectangular trade-off is the same as for spot
operands — see *Forbes (`Rings × Arms`) vs rectangular grid* above.

`WAVEX` is the standard diffraction-quality merit; the removed piston
and tilt prevent focus-shift and field-centroid shift from spuriously
driving the merit.

---

## Ray-Intercept Operands

Single-ray queries. Evaluate at one `(Surface, Wave, Hy, Px, Py)`.

| Type | Meaning | Unit |
|---|---|---|
| `RX`, `RY`, `RZ`    | Ray position at surface | mm |
| `RL`, `RM`, `RN`    | Ray direction cosines at surface | unitless |
| `AOID`, `AOIR`       | Angle of incidence | degrees / radians |
| `AOED`, `AOER`       | Angle of exitance | degrees / radians |
| `PX`, `PY`, `PZ`, `PL`, `PM`, `PN` | **Paraxial** equivalents of the above | |

**Inputs:** `Surface`, `Wave`, `Hy` (normalized field 0–1), `Px`, `Py`
(normalized pupil -1..+1).

---

## Angle Boundary Operands

Scan a surface range and report max/min angle of a chief ray.

| Type | Meaning |
|---|---|
| `RI` | Angle of incidence, degrees, across a surface range. |
| `RE` | Angle of exitance, degrees, across a surface range. |

**Inputs:** `Surface1`, `Surface2`. Typical use: set `Max` bound to
keep TIR margin on glass surfaces or to limit coating AOI.

---

## Boundary Operands

Scan a surface range. Report min/max for use with `Min`/`Max` bounds.
Penalize the optimizer when outside the bound.

| Type | Meaning |
|---|---|
| `CV`, `CVA`, `CVG` | Curvature: all / air-only / glass-only |
| `CT`, `CTA`, `CTG` | Center thickness |
| `ET`, `EA`, `EG`   | Edge thickness (at max SD) |
| `SD`               | Semi-diameter |
| `DTRG`             | **D**iameter-to-**T**hickness **R**atio, **G**lass-only: `2·SD / |CT|`. Fabrication constraint — a typical bound is `Max = 10` to keep elements thick enough for reliable grinding. |

**Inputs:** `Surface1`, `Surface2` (range inclusive).

Glass-only variants walk only surfaces whose outgoing material is not
air; air-only variants the complement; the plain variant both.

---

## Surface Property Operand

| Type | Meaning |
|---|---|
| `DM` | Diameter (= 2·semi-diameter) of one surface. Input: `Surface`. |

---

## System Operands

Single value per system.

| Type | Meaning |
|---|---|
| `EFL`    | Effective focal length |
| `MAG`    | Paraxial lateral magnification |
| `AMAG`   | Paraxial angular magnification |
| `ENPZ`   | Entrance pupil Z (distance from surface 1) |
| `EXPZ`   | Exit pupil Z (distance from image) |
| `ENPD`   | Entrance pupil diameter |
| `EXPD`   | Exit pupil diameter |
| `TTRACK` | Total track — surface 1 to image |
| `ILL`    | Relative illumination at a field, `= (F/#_axis / F/#_field)²`. Inputs: `Hy`, `Arms` (pupil-boundary direction probes — default 36, minimum 8). |

**Optional input:** `Wave` (defaults to primary wavelength).

---

## Distortion and Lateral Color

| Type | Meaning |
|---|---|
| `DITAN`     | Maximum signed F-tan(θ) distortion % across all fields. Reference is the paraxial EFL (or paraxial magnification for finite-conjugate object-height fields). |
| `DITHETA`   | Maximum signed F-θ distortion % across all fields. Reference is the paraxial EFL. |
| `DITANF`    | F-tan(θ) distortion % at a specific field. Input: `Hy`. Reference is the paraxial EFL. |
| `DITHETAF`  | F-θ distortion % at a specific field. Input: `Hy`. Reference is the paraxial EFL. |
| `LCF`       | Lateral color at a specific field — max chief ray height spread across all wavelengths. µm for focal systems, arcmin for afocal. Input: `Hy`. |

---

## Sensitivity (As-Built Performance)

| Type | Meaning |
|---|---|
| `SENS` | Sum of `|Δn|·(1 − cosθ)/6` over every pupil ray × surface × field × wavelength. Penalizes high-sensitivity surfaces (Moore, *SPIE* 10925, 2019). Inputs: `Rings`, `Arms`. |

The conventional design workflow optimizes nominal performance
first and then runs a separate tolerance analysis on top. `SENS`
folds tolerance sensitivity directly into the merit function. The
ray-bend at each surface — driven by `(1 − cos θ)` of the ray's
angle to the local normal weighted by the index step `|Δn|` — is
both the dominant aberration contribution *and* the dominant
sensitivity to fabrication errors (radius, thickness, tilt,
decenter). Adding `SENS` alongside the usual spot or wavefront
target steers the optimizer toward designs whose surfaces bend the
rays less aggressively, which means smaller perturbations under
tolerances. The as-built design may have slightly worse nominal
performance but holds up better through fabrication, and the
optimizer can land on different design forms entirely. Approach
follows Kenneth Moore, *Photonic Instrumentation Engineering VI*,
SPIE Proc. 10925, 1092502 (2019),
[doi:10.1117/12.2508062](https://doi.org/10.1117/12.2508062).

`Rings` and `Arms` set a Gauss-Legendre pupil sampling identical to
the geometric MTF — start with `Rings = 6, Arms = 12` and increase
only if the operand value looks noisy iteration-to-iteration.

---

## Arithmetic Operands

Derive new values from other operands already in the list. All operand
references are **1-based indices** into the merit-function editor's
table (the number shown in the `#` column).

| Type | Meaning | Inputs |
|---|---|---|
| `MULTC` | `Factor × Op1`                  | `Op1`, `Factor` |
| `SUM`   | `Op1 + Op2`                      | `Op1`, `Op2` |
| `DIFF`  | `Op1 − Op2`                      | `Op1`, `Op2` |
| `MULT`  | `Op1 × Op2`                      | `Op1`, `Op2` |
| `DIV`   | `Op1 ÷ Op2`                      | `Op1`, `Op2` |
| `SUMR`  | Σ Op[Op1..Op2]                   | `Op1`, `Op2` (range) |
| `QSUMR` | √( Σ Op[k]² ) over Op1..Op2      | `Op1`, `Op2` (range) |
| `DEV`   | Σ `|Op[k] − mean|` over Op1..Op2 | `Op1`, `Op2` (range) |

---

## Target vs. Boundary Mode

Every operand can operate in either mode:

- **Target mode** — residual is `(value − target) × weight`. Good when
  you want the value to *equal* something (e.g. `EFL = 100`).
- **Min/Max mode** — residual is zero inside the bounds,
  `(value − min) × weight` or `(value − max) × weight` outside. Good
  for constraints like "CTG ≥ 2mm" where any value above the minimum
  is acceptable.

Switch via the **Mode** column. Target and bounds share the same
numeric input field.

---

## Tips

- **Start simple.** One `SPOT` or `WAVEX` with weight 1, plus a few
  boundary operands (`CTG min`, `EG min`, `TTRACK max`) is enough to
  optimize most classical designs.
- **Watch the displayed Value.** After each evaluation the editor
  shows each operand's current value. If an operand is way off,
  check its inputs before assuming the optimizer is broken.
- **Weights don't need to be normalized.** LensHH-LT rescales pupil
  quadrature weights internally so that SPOT/WAVE contributions
  match their analysis-tool RMS values.
- **Afocal systems.** SPOT/SPOTM and lateral color automatically
  switch to angular units (arcmin) when the system is afocal.
