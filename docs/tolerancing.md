# Tolerancing

Every lens that is built differs from its design: radii, thicknesses and glass are a little off,
and the elements sit a little decentred and tilted in their mounts. Tolerancing finds which of
those errors matter, and how good the lenses built to a set of tolerances will be.

Open it from **Tolerancing ▸ Tolerancing…**. It works on copies of the lens; your design is never
changed. Its tabs are **Tolerances**, **Compensators** and **Settings**, then the results:
**Report**, **Monte Carlo MTF** and **Trial lenses**. The buttons above the tabs load, save or
generate the set and run **Sensitivity** or **Monte Carlo**, from whichever tab is showing. A run
turns to the **Report** tab, and the line at the bottom counts the lenses done and the time so
far; the set and the settings can't be changed until it ends. **Stop** ends a run early, within
a second or so, and keeps what is finished: a Monte Carlo reports the lenses done by then, a
smaller Monte Carlo of the same lenses, and a sensitivity run the tolerances whose two ends were
both done.

## The tolerance set

Tolerances are written one per line, in the form

```
CODE surfaces ±t
CODE surfaces min max [uniform | normal[:k] | endpoint] [pivot:d]
```

`#` starts a comment. Surfaces are numbered as in the Lens Editor, 1 being the first after the
object. An element or a cemented group is written as its first and last surfaces, `3-4`.

| Code | Error | Units |
|---|---|---|
| `TRAD` | Radius | lens units |
| `TFRN` | Power: the sag at the semi-diameter changes by fringes × λ/2 | fringes |
| `TIRR` | Irregularity: half astigmatism (Fringe Z5), half spherical (Z9 − Z1) | fringes |
| `TRSI` | Rotationally symmetric irregularity (Z9 − Z1) | fringes |
| `TTHI` | Thickness of the space after the surface | lens units |
| `TIND` | Index of the glass after the surface, n<sub>d</sub> | |
| `TABB` | Abbe number of the glass after the surface, V<sub>d</sub> | |
| `TCON` | Conic constant | |
| `TSDX`, `TSDY` | Decentre of one surface on its element | lens units |
| `TSTX`, `TSTY` | Tilt of one surface about its vertex: wedge | degrees |
| `TEDX`, `TEDY` | Decentre of an element or group in its mount | lens units |
| `TETX`, `TETY` | Tilt of an element or group, about its first vertex or `pivot:d` along the axis from it | degrees |

A range that is not centred on zero is a bias: `TTHI 4 -0.02 0.08` is a thickness made on the
high side. The fringes of `TFRN`, `TIRR` and `TRSI` are counted at 0.6328 µm over the surface's
semi-diameter.

### Compensators

What is adjusted after each lens is built, to bring back the best image, is written among the
tolerances too:

```
COMP surface [±travel]
CEDX | CEDY | CETX | CETY first-last [±travel]
```

| Code | Adjustment | Units |
|---|---|---|
| `COMP` | The thickness after the surface: a focus or an air space | lens units |
| `CEDX`, `CEDY` | Decentre of an element or group in its cell | lens units |
| `CETX`, `CETY` | Tilt of an element or group in its cell, about its first vertex | degrees |

The travel limits how far it may move either way; with none it moves freely. With no compensator
lines each lens is refocused by its back focus. In the window they are the **Compensators** tab,
and **Adjust each lens** there turns adjustment on or off. With them, the `COMP` on the
thickness before the image is the focus if there is one, or else the first `COMP`; the others are
set together with it, by damped least squares on the RMS wavefront. For example

```
COMP 6 0.5      # refocus within ±0.5 mm
COMP 4          # and adjust the second air space
CETX 3-4 0.1    # and tilt the middle element up to ±0.1° about x
```

### Pickups

A lens's pickups hold through every error and every adjustment: an error on a pickup's source
carries to its target, as the design ties them, and so does a compensator's move. A value a pickup
sets cannot be toleranced or adjusted itself, since the pickup would set it back; the run says so,
and **Generate** leaves it out. Tolerance its source instead.

**Generate** writes a starting set for a shop grade, from low cost to extra precise: power,
irregularity and wedge on every refracting surface, thickness of every space except the back
focus, index and Abbe number of every glass, and decentre and tilt of every element. Edit it,
then **Save .tol…** to keep it.

## Settings

- **Criterion.** What each lens is judged by: the **RMS wavefront** error over the fields and
  wavelengths, by their weights, or the **MTF** at one field, frequency and orientation.
- **Polychromatic.** Every wavelength by its weight, or the primary alone.
- **Field directions.** A decentre or tilt makes one side of the field worse than another, and an
  error about y shows mostly along x. LensHH-LT's fields lie along y, so to judge a lens at −y, +x
  and −x it is turned about its axis, image and all, by 180°, 90° and 270°: the lens turned
  90° shows at its +y fields what it does at +x. **±x and ±y** (the default) judges every lens in
  all four directions, **+y and −y** in two, **+y only** at the fields as the lens gives them. Each
  off-axis field's weight is shared among the directions; the field on the axis, the same in
  every direction, counts once. The focus and any other compensators are set for all of them
  together. An MTF criterion, and the Monte Carlo MTF, take the worst direction at each frequency.
  Judging in four directions takes about twice as long as in one.
- **Lenses, Seed, Errors spread.** The Monte Carlo's number of lenses, its random seed (the same
  seed draws the same lenses), and how errors spread over their ranges when a tolerance names no
  distribution: uniform, normal (±2σ at the ends of the range), or at the end points.
  **x and y as one error** draws each decentre or tilt's x and y together, over a disc.
- **MTF up to.** Keep each Monte Carlo lens's polychromatic MTF up to this frequency, for the
  Monte Carlo MTF plots. 0 for none.
- **Save.** Save the first so many Monte Carlo lenses as lens files, with a table of every trial.

## Sensitivity

**Sensitivity** sets each tolerance to each end of its range in turn, refocuses, and lists the
criterion at both ends, worst first, with how far the focus moved. The root sum square of the
worst increases, added to the refocused nominal, is a quick estimate of the lens as built. It
treats every error as if it were at the end of its range, so it is a ranking more than a
prediction: use it to see which tolerances to tighten and which you can afford to loosen.

## Monte Carlo

**Monte Carlo** draws lenses at random within the tolerances, every error at once, refocuses
each and judges it. The report gives the criterion that 50, 80, 90 and 98 % of the lenses reach,
the range of focus the lenses needed, and, with **MTF up to** set, the MTF the nominal lens, half
the lenses and 90 % of the lenses reach at each field.

The **Monte Carlo MTF** tab plots it per field: the nominal tangential and sagittal MTF, the
curve half the lenses reach, the curve 90 % reach, and the band from 10 % to 90 % shaded. The same
plots are saved as SVG files with the trials.

## Trial lenses

Each saved trial is an ordinary lens file. Open one from the **Trial lenses** tab, or from the
folder, and every analysis works on it:

- every surface keeps its own radius and thickness, as in the design, with its errors;
- a decentred or tilted element or group is laid out as it would be by hand:

  | Row | Thickness |
  |---|---|
  | Coordinate Break, "Element 3-4 moved" | 0 |
  | the element's surfaces | as they are |
  | dummy, "Back to surface 3" | minus the element's length and the space after it |
  | Coordinate Break, "Element 3-4 back" | the element's length and the space after it |

  The second break's decentres and tilts pick up the first's with a scale of −1, so editing the
  first moves the element and the second follows;
- a surface decentred or tilted on its element (a wedge) is laid out the same way around that one
  surface, its dummy's and second break's thicknesses picking up the surface's;
- irregularity is a **Zernike departure** on the surface (Surface Properties ▸ Zernike);
- index and Abbe number errors are a **glass offset** (Surface Properties ▸ Glass Model);
- the focus and any other compensator are where the lens was adjusted to.

The trial file is the lens that was judged: it is built this way to be traced as well as saved.
It keeps the lens's own fields. To see it in another direction across the field, choose
**Analysis ▸ Field Direction** (or **Fields at** on the 2D Layout tab): every analysis and the
layout then show the lens turned about its axis to put its fields at +x, −y or −x.

**File ▸ Export ▸ Zemax (.zmx)** writes a trial for ZEMAX with each field in all four directions,
weighted as the run judged them; ZEMAX has x fields of its own. (A lens the same in every
direction is written with its fields as they are.) The Zernike departures are written as Zernike
Fringe Sag surfaces and the glass offsets as Offset solves on the catalog glass, so the file
carries every error the trial has. Pickups are written as the values they set.

**File ▸ Export ▸ Optiland (.json)** writes the same lens for Optiland, fields in four directions
likewise. Optiland has no Coordinate Break: each surface is written where it stands in space, with
its own decentre and tilt. A Zernike departure is written as the XY polynomial surface that it and
the surface's asphere terms add up to, the same surface; a glass offset as a glass of its own,
its index tabulated as LensHH-LT computes it, beside the lens file and installed for Optiland like
the lens's other glasses.

The Code V, OSLO and Optalix exports are for centred lenses: their files hold no coordinate
breaks, Zernike departures or glass offsets, so they are off for a trial.

`trials.csv` lists every trial's errors, the compensators' moves and the criterion, and which file
it was saved to. Its `focus_change_mm` is the focus thickness's change from the design, as the
trial's lens file has it.

## Zernike departures, glass offsets and Coordinate Breaks

Zernike departures and glass offsets are surface settings in their own right, not only for
tolerancing; Coordinate Breaks come with tolerancing trials and with tilted lenses read from
other programs:

- **Zernike departure.** A Fringe Zernike sag, Z1 to Z35 over a normalization radius, added to a
  Standard or Even Asphere surface, for a measured or toleranced irregularity. Each term can be
  an optimization variable.
- **Glass offset.** A change of n<sub>d</sub> and V<sub>d</sub> of the glass after a surface, the
  partial dispersions kept, for melt data.
- **Coordinate Break.** Decentres and tilts the coordinate frame for every surface after it.

A lens with a Zernike departure or a glass offset is traced on the CPU. A lens with a Coordinate
Break that decentres or tilts is for analysis only: the optimizers refuse it, since the merit
function assumes a centred lens. Optimize the centred design, then tolerance it. Paraxial values
(focal length, pupils, Seidel sums) are those of the lens without its tilts and decentres.

A lens with Coordinate Breaks has its structure locked. Its values - radii, thicknesses, glasses,
apertures, the breaks' decentres and tilts - can be edited, but surfaces can't be inserted,
deleted or changed in type, in the Lens Editor (Insert Surface, Delete Surface, Insert Lens and
the Surface Type boxes are off), the CLI or MCP. A trial's elements are laid out between a break
and the break back that picks it up, with a row back to the element's first vertex, and a row
added or removed there would break those links. Coordinate Break is not offered as a surface type
for a lens of one's own.

## Drawings

A tolerance set becomes a drawing of each element for the optical shop: one sheet per piece of
glass, the pieces of a cemented group each on their own (a mirror is not drawn: the lens gives no
substrate). Each sheet has

- the section through the axis, to a standard scale, with the centre thickness and the edge
  diameter dimensioned with their tolerances;
- each surface's radius (convex or concave on the part), clear aperture, power and irregularity in
  fringes at the test wavelength, the rotationally symmetric part of the irregularity, tilt (and
  decentre on an asphere), surface quality, protective chamfer and coating;
- the glass, with its n<sub>d</sub> and ν<sub>d</sub> and their tolerances;
- an asphere's equation, coefficients and sag table: at each height the sag, and ±Δz and ±Δs, the
  most the surface's power and irregularity tolerances move it and tilt it there;
- the element's mount tolerances (its decentre and tilt, and its group's), or, for an element the
  set's `CEDX`, `CEDY`, `CETX` or `CETY` aligns, that it is set at assembly;
- a title block.

Two practices are offered: **ISO 10110** (A4, the three-field table and the coded indications 0/
to 5/) and **ASME** (ANSI A, a surface table and numbered notes per ASME Y14.18, surface quality as
MIL-PRF-13830B scratch-dig, wedge as an edge thickness difference). Power of more fringes than a
test plate is read to (10 by default) is stated as a tolerance on the radius instead, rounded down.
The edge diameter is the largest clear aperture and a margin, rounded up to the half millimetre;
cemented pieces are made to one diameter. Anything the set leaves out is filled from the settings'
defaults, and each drawing lists what it filled and anything else to check - a thin edge, a bevel
that would take the whole edge, a note that did not fit - as warnings.

### The Drawings window

Open it from **Tolerancing ▸ Drawings…**. It draws from the tolerance set the Tolerancing window
last had for the lens (**Load .tol…** reads another, **Generate** makes a starting set at a shop
grade). On the left are **Sheet and title block**, for what goes on every drawing, and each
element's drawing; on the right, the selected sheet as it will print, with its warnings under it.

For an element, each value on the drawing is listed with what the tolerances give it. Type in the
box beside it to put another value on the drawing; clear the box, or ↺, to go back to the computed
value. Changed values are highlighted, and the sheet follows at once.

- **Save settings** keeps the sheet's fields and every change, beside the lens as
  `name.drawing.json`; the window opens with them next time. (The CLI and MCP read the same file.)
- **Save as my defaults** keeps the title block, classes, chamfers, coatings and notes, without
  this lens's project or element changes, as the start for any lens with no settings of its own.
- **Export PDF…** writes every sheet in one PDF; **Export SVG and DXF…** each sheet in both, named
  by drawing number.

Closing the window with changes not saved asks whether to save them.

### Drawing settings

What a drawing needs that a tolerance set does not hold comes from a settings file, JSON, every
field optional (`tolerance drawings settings <file.json>` writes the defaults to edit):

| Field | What it is |
|---|---|
| `standard` | `Iso10110` or `Asme` |
| `maximumPowerFringes` | The most power stated in fringes; more is a radius tolerance (10) |
| `edgeMargin`, `diameterTolerance` | Edge diameter over the clear aperture (1 mm), and its minus tolerance (0.05 mm) |
| `chamferMin`, `chamferMax` | Protective chamfer face widths; by default from the diameter |
| `imperfections`, `scratchDig` | Surface quality: ISO 10110-7 (`5/3x0.16`), MIL-PRF-13830B (`60-40`) |
| `stressBirefringence`, `bubbles`, `inhomogeneity` | ISO 10110-2, -3 and -4 material classes |
| `coating`, `coatings`, `coatingSpecs` | The coating of every surface, by surface number, and each name written out in full as a note |
| `asphereSlopeMrad`, `asphereSlopeSamplingMm` | An asphere's slope deviation, as a note |
| `notes` | Notes on every drawing |
| `defaultFringes`, `defaultIrregularity`, `defaultThickness`, `defaultTiltArcmin`, `defaultIndex`, `defaultAbbePercent` | For a tolerance the set leaves out |
| `project`, `company`, `drawnBy`, `numberPrefix`, `date` | Title block; drawings are numbered `OPT-01`, `OPT-02`, … |
| `elements` | Per-element overrides, below |

### Per-element overrides

Every value on a drawing comes from the lens and its tolerances, and any of them can be given
instead, per element, under `elements`, keyed by the element's first surface (`"3"`) or its
drawing number (`"OPT-02"`). A field left out keeps the computed value:

- `title`, `number`, `description` (a line in the title block), `notes`;
- `glass`: the name the drawing gives the glass ("N-BK7 or equivalent"); its n<sub>d</sub> and
  ν<sub>d</sub> stay the design's;
- `ndTolerance`, `vdTolerancePercent`;
- `thicknessMinus`, `thicknessPlus`: the centre thickness's deviations, signed (`-0.02`, `0.03`);
- `diameter`, `diameterMinus`, `diameterPlus`: the edge diameter and its deviations, signed. A
  cemented partner is made to the same diameter;
- `front` and `back`, the element's two surfaces: `powerFringes`, `radiusTolerance`,
  `irregularityFringes`, `symmetricIrregularityFringes`, `tiltArcmin`, `decentreMm`, `coating`
  (`"none"` for none) and `clearAperture`.

What follows from a value follows it: power given past `maximumPowerFringes` is stated as a radius
tolerance as computed power is (give `radiusTolerance` to state your own), an asphere's sag table
is taken from the power, irregularity and clear aperture as drawn, and the edge thickness from the
diameter as drawn.

```json
{
  "standard": "Iso10110",
  "coating": "AR1",
  "coatingSpecs": { "AR1": "Broadband AR 450-650 nm, Ravg ≤ 0.5 % per surface" },
  "project": "Triplet",
  "elements": {
    "3": {
      "title": "NEGATIVE", "glass": "F2 or equivalent", "diameter": 12,
      "thicknessMinus": -0.02, "thicknessPlus": 0.03,
      "front": { "powerFringes": 3, "irregularityFringes": 0.5 },
      "back": { "radiusTolerance": 0.1, "coating": "none" }
    }
  }
}
```

The files are written by hand, with no library: **PDF** (every sheet in `drawings.pdf`, in
Helvetica, which every reader has), and per element **SVG** to look at and **DXF** (R12, on layers
a CAD program can switch), each named by its drawing number.

## From the CLI and MCP

The CLI's `tolerance` command takes the same .tol file:

```
tolerance defaults precision lens.tol
tolerance sensitivity lens.tol show=15
tolerance montecarlo lens.tol trials=500 mtf=60 step=5 save=mc savecount=20
```

Options: `criterion=rms` or `criterion=mtf:field:frequency:t|s`, `mono`, `nofocus`, `travel=<mm>`,
`fields=xy|y|file`, `wave=<µm>`, `seed=<n>`, `stats=uniform|normal[:k]|endpoint[+disc]`.

Drawings:

```
tolerance drawings settings shop.json
tolerance drawings lens.tol out=drawings settings=shop.json format=all standard=asme
```

`format=pdf` (the default) writes `drawings.pdf`; `svg` and `dxf` a file per element; `all` all
three, or several as `format=pdf,dxf`. `standard=iso|asme` overrides the settings file's;
`wave=<µm>` is the test wavelength. Each drawing is listed with its warnings.

Over MCP the tools are `ToleranceDefaults`, `ToleranceSensitivity`, `ToleranceMonteCarlo` and
`ToleranceDrawings` (the tolerance set, `outputDirectory`, and optionally `settingsPath`, `format`,
`standard` and `testWavelength`); each takes the tolerance set as text or as the path of a .tol
file. The CLI's drawings and `ToleranceDrawings` both come from the library's `DrawingRun.Write`.
