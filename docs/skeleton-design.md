# Designing from a Skeleton — Parallel Plates to a Corrected Lens

Most optimization starts from a design that already works. This page is about
the other case: starting from a stack of **flat plates with no optical power at
all**, and letting the optimizer find the form.

It works because of what the merit function is made of. Aberration coefficients
and paraxial ray data are closed-form functions of the curvatures, thicknesses
and indices — they need no ray tracing, they are defined for a design that
does not yet image anything, and they are cheap enough to drive a search that
would be impractical with spot size. A flat-plate stack has zero for every
aberration coefficient and infinite focal length; the merit function still has
a gradient, and the optimizer can pull a real lens out of it.

> **New in 1.0.152.** The fifth- and seventh-order Buchdahl coefficients,
> [`PRMS`](merit-function.md#prms--rms-spot-size-from-the-aberration-coefficients),
> and native analytic derivatives for the paraxial ray operands are what make
> this practical rather than merely possible.

## The skeleton

A skeleton is a surface list with the right *topology* and nothing else:

- the number of elements you intend, with plausible airspaces between them;
- every curvature **zero** — flat plates;
- a glass on each element (the search will change these);
- your intended aperture, fields and wavelengths.

The double Gauss skeleton used throughout this page is 13 surfaces (6 elements),
EPD 33.33, fields 0 / 10 / 14°, wavelengths 0.486 / 0.588 / 0.656 µm. Every
curvature starts at zero.

## Constrain the variables, don't penalize them

From a skeleton the optimizer will walk into geometry that cannot exist —
negative edge thickness, surfaces crossing before the rim — and once there it
often cannot get out. Bound the variables so those states are unreachable
rather than merely expensive:

```
system set-bound-handling reflect
```

**Reflect**, not the sigmoid default. A sigmoid's gradient vanishes at the
limits, so a variable pushed against a bound stops responding to the optimizer;
reflection keeps |slope| = 1 everywhere, and a variable at a bound still moves.
See [Bound handling](optimization.md#bound-handling-sigmoid-or-reflect-new-in-10147).

Typical bounds for the double Gauss skeleton — glass centres and airspaces get
different ranges, and the last airspace is the back working distance:

| Variable | Bounds |
|---|---|
| Glass centre thicknesses | 1 … 17 mm |
| Airspaces | 0.1 … 20 mm |
| Back working distance | 49 … 100 mm |
| Curvatures | unbounded |

A penalty operand is the alternative and is the better choice from a design
that already works, where a hard bound would clamp. From a skeleton, prefer the
bound.

## The merit function

Five jobs, and every one of them is load-bearing. Leave one out and the search
finds a degenerate answer that satisfies everything you did ask for.

| Job | Operands | Why |
|---|---|---|
| Focal length | `EFL` target, heavy weight | Otherwise nothing sets the scale. |
| **Image plane** | `PY` at the image surface, target **0** | Coefficients cannot see defocus. |
| Geometry | `EG`, `EA` minima | Keeps the elements physically buildable. |
| Monochromatic quality | `PRMS` at several fields | The thing you are actually minimizing. |
| Colour | `ACT`, `LCT` | Coefficients cannot see colour either. |

The working merit for the double Gauss skeleton:

| Operand | Setting | Weight |
|---|---|---|
| `EFL` | target 100 | 100 |
| `EG` | min | 10 |
| `EA` | min | 10 |
| `PY` | surface 12 (image), target 0 | 5 |
| `ACT` | target 0 | 20 |
| `LCT` | target 0 | 20 |
| `PRMS` | Hy = 0, 0.5, 0.9 at the primary wavelength | 0.1 each |
| `PRMS` | the same three fields at waves 0 and 2 | 0.05 each |

The third- and fifth-order totals (`SPHT`…`LCT`, `B5T`…`M3T`) are present at
**weight 0**. They cost nothing to carry and let you push on one aberration by
hand when a search settles somewhere you want to nudge.

### Why `PY` is not optional

Every aberration coefficient is referenced to the **paraxial image plane**. None
of them knows where the image surface actually is, and `EFL` fixes focal length,
not image distance. A coefficient-only merit therefore leaves the last airspace
completely unconstrained.

The failure is not subtle. On this skeleton the optimizer once reached `EFL`
exactly 100 with every coefficient near zero — and a **7 mm on-axis RMS spot**.
The aberration correction was genuinely good; the entire blur was defocus, with
the paraxial focus 6.6 mm *inside* the glass and the image plane 58 mm past it.

One `PY` operand at the image surface, targeted to 0, fixes it: on axis the
chief ray is the axis, so the marginal ray height at the image *is* the defocus.
Add a `BFL` minimum as well — `PY → 0` pins the plane to the focus but does not
stop the focus landing somewhere unreachable.

> `PY` is a **marginal-ray** operand. It ignores the `Px`/`Py` inputs even
> though the editor lets you set them, so a "chief minus marginal" construction
> with `DIFF` returns zero identically and silently contributes nothing.

### Why colour needs its own operands

`PRMS` evaluates each wavelength at *its own* paraxial focus, so axial and
lateral colour do not appear in it — even with all three wavelengths weighted.
`ACT` and `LCT` are the only things in the merit function that see colour.

They also carry far more optical error per unit than they appear to. Measured
on this design against a wavefront summary: `ACT = −0.0215` is 18.3 waves of
`W020`, and `LCT = −0.0106` is 18.1 waves of `W111`, while `SPHT = 0.0104` is
only 2.2 waves of `W040`. Roughly 850 and 1700 waves per unit against 213. Equal
weights trade an 18-wave error against a 2-wave one.

## Weighting is phased, not chosen once

This is the part that cannot be shortcut: **converge first at modest colour
weight, then raise it and re-polish.** The same weight change helps or destroys
the design depending on where you start it from.

From the converged design, raising colour weight from 5 to 500 gave four times
better colour *and* about twice better spots, with edge thickness *improving*
from 1.007 to 2.532 and `EFL` pinned at 100.0000.

From the flat skeleton, the identical change was destructive:

| Colour weight | Edge thickness `EG` | Focal shift |
|---|---|---|
| 5 | 1.140 mm | 3.26 mm |
| 500 | **−0.955 mm** | 1.98 mm |

Negative edge thickness against a 1.0 mm floor — surfaces crossing before the
rim. It bought colour by destroying the geometry.

The reason is that boundary operands contribute **exactly zero while satisfied**.
From a converged start `EG`, `EA` and `EFL` are already met, so they genuinely
cannot be diluted. During a search from a skeleton they are violated most of the
time, so they *do* compete, and a 100× colour weight overwhelms them.

**Watch `EG` as the tell.** If it drops toward its floor as you raise colour
weight, back off.

## Running it

Basin hopping with glass substitution is the tool: the skeleton has no form to
refine, so the search has to change topology, and the glasses matter as much as
the curvatures.

```
optimize basin hops=1000 chains=0 glasssub=true catalog=CoreSet28
```

A few things worth knowing before you watch the log:

- **Check the compute path first.** `optimize preview`, or the **Preview**
  button, tells you which engine the run will use. A skeleton merit built from
  coefficients and paraxial operands runs native analytic under an EPD aperture,
  which is the fast path — see
  [Preview](optimization.md#preview-what-will-actually-run-new-in-10152).
- **`EFL` has no gradient at exactly zero power.** A perfectly flat stack is
  afocal, and the analytic focal length is a constant there. The first step is
  driven by the other operands; once any curvature moves, `EFL` engages
  normally. It is not a reason to avoid the flat start, but it does mean the
  very first iteration is not doing what you might assume.
- **Hops cost more as the design converges.** Early hops are cheap because the
  local optimizer gives up quickly on a hopeless design; late ones are not. A
  run can end on the clock rather than the hop cap.
- **Let the chains diverge.** A skeleton search depends on chains exploring
  different forms. The
  [reseed thresholds](optimization.md#reseeding-a-chain-that-has-fallen-behind-new-in-10152)
  default to rescuing only a chain that is genuinely out of contention; loosen
  them and the population collapses onto one form early, which is exactly what
  you do not want here.

## Then hand it to a real merit function

Everything above is a *screening* merit. Coefficients are a truncated series and
`PRMS` is an estimate, so once the design has a form worth keeping, rebuild the
merit function around real rays — `WAVEX` or `SPOT`, with the same boundary
operands — and polish with the Local optimizer.

The skeleton stage is for finding the form. It is not for deciding the design is
finished.

## Related pages

- [Merit Function Reference](merit-function.md) — every operand used here.
- [Optimization](optimization.md) — the optimizers, Preview, and bound handling.
- [Semi-Diameter as an Optimization Variable](semi-diameter-variables.md).
