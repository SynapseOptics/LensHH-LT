# Agent Workflow — Stock-Lens-Based Design

This is for an AI agent (Claude, Cursor, etc.) connected to the LensHH-LT MCP
server. The catalog (`catalogs/stock-lens-catalog.sqlite`, ~7,600 parts from
Edmund Optics, Thorlabs, and Ross Optical) is queryable through three single-
candidate MCP tools — and a five-tool **batch search** workflow for trying
many configurations at once. They compose with the existing analyze/optimize
tools to let an agent go end-to-end without leaving the protocol.

---

## The chain

```
search_stock_lenses        →  list candidates that fit the spec
  ↓ pick one
insert_stock_lens          →  splice into host system
  ↓
get_paraxial_data          →  first-order sanity check
spot_diagram / ray_fan     →  geometric performance
fft_mtf                    →  final image quality
  ↓ if orientation matters
reverse_lens               →  flip in place, re-evaluate
  ↓ if it almost fits
add_variable / add_operand →  set up optimization
optimize                   →  LM refinement (curvatures, thicknesses, glass)
  ↓
save_as_system             →  persist the best design
```

---

## Batch design search (when you need to compare many candidates)

When the user's problem requires comparing tens or hundreds of stock-lens
configurations — different parts, orientations, insertion positions — use the
**batch_design_search** tool family instead of looping the single-candidate
tools.

```
agent prepares host .lhlt (case-appropriate stop convention + merit + fields)
batch_design_search_start(host, candidates_json, innerOptimizer, parallelism)
  ↓ returns jobId
batch_design_search_status(jobId)        →  poll for ranked partial results
                                            (cancel early if a winner is clear)
batch_design_search_keep(jobId, idx)     →  load winner into session
batch_design_search_discard(jobId)       →  free memory
```

### Five host setups depending on the design task

The agent prepares the host `.lhlt` differently per case. **The batch tool itself is agnostic** — it just clones the host, applies inserts, optimizes:

| Case | Host structure | Stop |
|---|---|---|
| (a) infinite from scratch | OBJ + S1 stop + merit/fields/wavelengths | floating (S1.T variable, seed via `entrance pupil`) |
| (a) finite from scratch | OBJ@finite + S1 + S2 stop + S2.T pickup from S1 × −1 | floating (S1.T variable) |
| (b) with filters in host | (a) + filter flat-flat surfaces fixed in place | floating, same as (a) |
| (c) augment existing lens, in front | full existing design + its stop | fixed in host |
| (d) augment existing lens, behind | full existing design + its stop | fixed in host |
| (e) augment existing lens, both ends | full existing design + its stop | fixed in host |

### Candidate descriptor

```json
{
  "label": "optional human-readable name",
  "entrance pupil": -10,                   // case (a)/(b) only — seeds S1.T
  "inserts": [
    { "partNumber": "AC127-050-AB",
      "reversed": false,                   // optional
      "air thickness": 5,                  // seed for trailing gap, auto-variable
      "after_surface": 3                   // optional; if absent → sequential.
                                           // HOST-numbered (pre-insert indices)
    }
  ]
}
```

Air-gap variable policy:
- Trailing `air thickness` after each insert → **variable**
- Leading gap before each insert (host's `after_surface`.Thickness) → **variable**
- For case (a)/(b), `entrance pupil` sets S1.Thickness which is already variable

`after_surface` indices refer to the **host's original numbering**, not the
post-insert state. The tool translates on the fly: each prior insert with
host-target ≤ H bumps subsequent H references.

### Results

`batch_design_search_status` returns one row per candidate:
- `candidateIndex, label, finalMerit, iterations, FinalEfl, status`
- `OptimizedThicknesses` (surfaceIndex → final value, only for variables)
- `StopLocation` (only for case-(a)/(b) hosts with `entrance pupil`):
  - `S1Thickness` (final value the LM converged to)
  - `Context` — e.g. `"in air between S3 and S4"` or **`"INSIDE glass element at S2"`**
  - `BuriedInGlass` — true means physically invalid: an iris can't go inside a lens

**Always check `BuriedInGlass` before trusting a low merit on a case-(a) host.**

### Five worked examples

#### (a) pure stock from scratch, infinite conjugate, EFL ≈ 50 mm

Host build:
```
new_system
set_aperture(type='EPD', value=10)
set_wavelengths('0.4861,0.5876,0.6563')
set_fields('0,5,10')
add_surface(radius=1e18, thickness=∞)               # S0 = OBJ
add_surface(radius=1e18, thickness=-10)             # S1 = STOP (set IsStop, mark variable)
add_surface(radius=1e18, thickness=0)               # IMG placeholder
add_operand('EFL', target=50, weight=1)
add_operand('RMSSpot', weight=1)
save_as_system('C:/tmp/host_a.lhlt')
```

Batch call:
```
batch_design_search_start(
  hostLhltPath='C:/tmp/host_a.lhlt',
  candidatesJson='[
    { "label": "AC127-050 forward",
      "inserts": [{ "partNumber": "AC127-050-AB", "reversed": false, "air thickness": 0 }],
      "entrance pupil": -10
    },
    { "label": "AC127-050 reversed",
      "inserts": [{ "partNumber": "AC127-050-AB", "reversed": true, "air thickness": 0 }],
      "entrance pupil": -10
    },
    { "label": "LB1781 + LE5839 reversed",
      "inserts": [
        { "partNumber": "LB1781", "reversed": false, "air thickness": 5 },
        { "partNumber": "LE5839", "reversed": true,  "air thickness": 0 }
      ],
      "entrance pupil": -10
    }
  ]'
)
```

When `batch_design_search_status` reports done, the result rows include
`StopLocation`; reject any candidate where `BuriedInGlass: true`.

#### (b) with existing filters

Same as (a), but the host has filter surfaces somewhere. Just include filter
flat-flat surfaces in the host `.lhlt`; the batch tool's `after_surface`
indices target the host's numbering, so the agent can insert lenses before or
after the filters without disturbing them.

#### (c) add elements in FRONT of existing custom objective

Host: pre-existing complex objective with its own stop. Candidate `after_surface = 0`
(insert after OBJ, before existing first lens vertex). Omit `entrance pupil`.

```json
{ "label": "field-flattener in front",
  "inserts": [
    { "partNumber": "LA1027-A", "reversed": false, "air thickness": 2, "after_surface": 0 }
  ]
}
```

#### (d) add elements BEHIND existing custom objective

Host: pre-existing design ending at, say, surface 12 (last lens vertex).
Candidate `after_surface = 12` (insert after the last lens vertex, before IMG).
The host's S12.Thickness (= the original BFL) becomes the leading-gap variable
for the new element.

```json
{ "label": "relay behind",
  "inserts": [
    { "partNumber": "AC127-050-AB", "reversed": false, "air thickness": 8, "after_surface": 12 }
  ]
}
```

#### (e) add elements BEFORE AND BEHIND

Both groups in one candidate. The flat `inserts` list mixes both insertion
positions; each insert with explicit `after_surface` starts a new group, then
following inserts go sequentially after the prior:

```json
{ "label": "front field-flattener + rear relay",
  "inserts": [
    { "partNumber": "LA1027-A",     "reversed": false, "air thickness": 3, "after_surface": 0  },
    { "partNumber": "AC127-050-AB", "reversed": false, "air thickness": 8, "after_surface": 12 }
  ]
}
```

`after_surface` always refers to the **host's** numbering. After the LA1027-A
insert adds 2 vertices, the AC127-050-AB at host-target=12 lands at current
index 14 — the tool computes that translation internally.

### Inner optimizer choice

- `innerOptimizer="lm"` (default): one LM pass per candidate. Fast.
- `innerOptimizer="multistart"`: LM-with-random-restarts per candidate.
  Heavier, more likely to escape local minima. **Use `parallelism=1` if your
  merit function has glass-substitution operands** (which mutate the shared
  glass catalog and aren't safe under concurrent execution).

### Parallelism

`parallelism=0` (default): tool picks `Math.Max(1, ProcessorCount/4)`. Each LM
already parallelizes its inner ray-fan, so candidate-level × ray-fan can
saturate CPUs; the 1/4 cap leaves breathing room. Pass `parallelism=1` for
strict serial.

---

## Single-candidate flow (older)

The original chain still works for one-off explorations:

## 1. Frame the problem

Before searching, write down:

| Spec               | Example                                  |
| ------------------ | ---------------------------------------- |
| EFL                | 50 mm ± 10 %                             |
| Aperture           | EPD 12.7 mm, or f/4                      |
| Field              | ±2° object-angle, or 11 mm object height |
| Wavelength range   | VIS (486 / 587 / 656 nm); NIR; UV-FS     |
| Constraints        | OD ≤ 25.4 mm, mounted in Ø1" tube        |
| Image format       | sensor or fibre diameter                 |

This drives the search filters.

---

## 2. Search the catalog

`search_stock_lenses(...)` — all filters are optional. Common patterns:

| Goal                                         | Filters                                                                                          |
| -------------------------------------------- | ------------------------------------------------------------------------------------------------ |
| VIS achromat doublet near 50 mm, Ø ≤ 25.4 mm | `eflMin=45, eflMax=55, diameterMax=25.4, nElements=2, ndPrimaryMin=1.5, ndPrimaryMax=1.55`       |
| Fast plano-convex, Ø1"                       | `familyLike='%PlanoConvex%', fnumMin=1, fnumMax=2.5, diameterMin=24, diameterMax=26`             |
| High-NA molded aspheric, IR                  | `familyLike='%Aspheric%', fnumMax=1.2`                                                           |
| UV-grade fused silica                        | `familyLike='UVFS%'`                                                                             |
| Long EFL, low dispersion (crown)             | `eflMin=200, vdPrimaryMin=60`                                                                    |

Results are one line each:

```
ThorLabs AC127-050-AB ThorLabs/AC EFL=50 f/4.27 D=11.7 nd=1.517 n_elem=2 | <description>
```

Sorted by closeness to the EFL midpoint (or by part_number if no EFL given).
The `part_number` is the only field you need to feed to `insert_stock_lens`.

### Reading `family`

Edmund families use descriptive folders (`Achromats/MgF2CoatedAchromatic`,
`AsphericGlassPolished/PrecisionAspheric`). Thorlabs uses prefix letters:
`LA` = plano-convex, `LB` = bi-convex, `LBF` = best-form, `LC` = plano-concave,
`LD` = bi-concave, `LE` = +meniscus, `LF` = −meniscus, `LJ`/`LK` = cylindrical,
`AC*` = achromat variants, `AL`/`AYL`/`APL`/`ASL` = aspheric variants,
`AX` = axicon, `MAP` = matched achromatic pair, `GRIN` = gradient-index,
`TRS`/`TRH` = triplet.

---

## 3. Build the host system

Either:

```
new_system
set_aperture (type='EPD', value=12.7)
set_wavelengths ('0.4861,0.5876,0.6563')
set_fields ('0,7,10')
add_surface (radius=1e18, thickness=∞)             # OBJ at infinity
add_surface (radius=1e18, thickness=0)             # placeholder
```

Or load a partially-completed design with `load_system <file.lhlt>`.

---

## 4. Insert a candidate

```
insert_stock_lens(partNumber='AC127-050-AB', afterSurface=<int>, reversed=<bool>)
```

What happens:

- The stock lens's OBJ / dummy-stop / IMG bookends are stripped
- Only the optical vertices are inserted (singlet → 2, doublet → 3, triplet → 3)
- They land at `afterSurface+1, afterSurface+2, …`
- Surface at `afterSurface` keeps its thickness unchanged (preserves the air
  gap before the new lens)
- The last inserted surface gets thickness 0 (touches whatever was at
  `afterSurface+1` before)
- Host wavelengths / fields / aperture / merit function are preserved
- Merit-function operands and pickups referencing surfaces ≥ insertion point
  are bumped by N automatically

Pass `reversed=true` to flip the lens 180° on insertion. Or insert in its
natural orientation now and flip later with `reverse_lens`.

The return value reports the new surface range, the new EFL, and the new F/#
so you can decide instantly whether to evaluate or try a different candidate.

---

## 5. Evaluate

Run analyses in **increasing cost** order — bail out early if the cheap
checks already disqualify the candidate.

| Tool                            | What it tells you                          | Cost   |
| ------------------------------- | ------------------------------------------ | ------ |
| `get_paraxial_data`             | EFL, BFL, F#, NA, pupils, total track      | cheap  |
| `single_ray_trace`              | one ray's image-plane height               | cheap  |
| `field_curvature_and_distortion`| Petzval, field curvature, distortion %     | medium |
| `seidel_coefficients`           | 3rd-order aberrations (SA, coma, etc.)     | medium |
| `spot_diagram`                  | RMS spot radius per field / wavelength     | medium |
| `ray_fan` / `opd_fan`           | transverse ray / wavefront aberration      | medium |
| `fft_mtf`                       | diffraction-limited modulation transfer    | high   |

Pin the image-plane focus first if needed:
`set_surface_solve` with a Marginal-Ray-Height-Zero solve on the back air gap.

---

## 6. Orientation

For singlets and asymmetric multiplets, the orientation matters. Try:

```
reverse_lens(startSurface=<first lens vertex>, count=<number of inserted vertices>)
```

Re-run the analysis. Common rule of thumb: convex side toward the longer
conjugate. The tool returns the new EFL / F# so you can compare quickly.

---

## 7. Optimize (only if the stock part nearly fits)

Stock-lens designs are usually **picked, not optimized**. But sometimes you
want to:

- Vary the air-gap thickness between two lenses (focal-plane positioning)
- Bend curvatures slightly (only valid if the resulting part is no longer
  a stock lens — usually a sign you should pick a different stock part)
- Substitute a glass via `set_glass_substitution`

If the requirement really needs custom curvatures, the workflow becomes a
hybrid: start from the stock layout, then optimize. Use:

```
set_thickness_variables (surfaceIndices='3,5')
set_curvature_variables (surfaceIndices='2,4,6')
add_operand (type='EFFL', target=50.0, weight=1.0)
add_operand (type='SPOT', ...)
optimize                                  # LM
```

If the result no longer corresponds to a stock part, document it as a
custom design.

---

## 8. Iterate or save

Loop back to step 4 with the next candidate from the search results, or save
the best with `save_as_system <path.lhlt>`.

---

## Tips and gotchas

- **Glass coverage**: 99.77 % of catalog parts have a valid `nd_primary`. The
  14 lenses without it (GRIN parts using `SLW-1.8`, Vital IR aspherics using
  `VIG06`) will be filtered out by any `ndPrimaryMin`/`Max` constraint. If
  you need GRIN or proprietary IR materials, search without nd filters and
  inspect descriptions.
- **Sign of EFL**: the catalog stores signed EFL — negative means a diverging
  lens. Use `eflMax=-50, eflMin=-200` to find negative-power doublets.
- **Aspheric / molded vs polished**: search with `familyLike='%Molded%'` for
  injection-molded glass aspherics (LightPath Geltech etc.), or
  `familyLike='%Aspheric%'` for any. Avoid molded for high-precision
  applications.
- **Mechanical OD vs clear aperture**: `diameter_mm` is the mechanical OD
  (drawing extent). The optical CA is enforced by a dummy aperture stop the
  importer inserts ahead of every stock lens; in the resulting `.lhlt` you'll
  see an extra Standard surface with `IsStop=true` and `Material=""` between
  OBJ and the lens vertices. `insert_stock_lens` strips this dummy because
  the host system already has its own stop.
- **Cross-vendor mixing**: nothing prevents inserting an Edmund singlet plus
  a Thorlabs doublet in the same design. The catalog treats them uniformly.
- **Searching by description**: SQLite stores the vendor's NAME line in
  `description` — useful for grep-style queries, but not exposed as a search
  arg yet. Adjust `family` filters to narrow first, then read descriptions
  in the result.

---

## Minimal end-to-end example

Goal: f/4 vis achromat doublet, EFL 50 mm, mounted in Ø½" tube.

```
search_stock_lenses(eflMin=48, eflMax=52, diameterMax=12.7, nElements=2,
                    fnumMin=3.5, fnumMax=4.5)
  → ThorLabs AC127-050-AB ... EFL=50 f/4.27 D=11.7 nd=1.517 n_elem=2 | ...

new_system
set_aperture(type='EPD', value=11.7)
set_wavelengths('0.4861,0.5876,0.6563')
set_fields('0,2')
add_surface(radius=1e18, thickness=1e18)         # OBJ
add_surface(radius=1e18, thickness=10)           # placeholder before lens
add_surface(radius=1e18, thickness=0)            # IMG placeholder

insert_stock_lens(partNumber='AC127-050-AB', afterSurface=1)
  → Inserted ThorLabs AC127-050-AB at surfaces [2-4] (3 vertices). EFL ... mm; F/# ...

get_paraxial_data           → confirm EFL≈50, F#≈4.27
spot_diagram                → RMS spot per field
fft_mtf                     → cycles/mm at 50% contrast

# If MTF is acceptable, save.
save_as_system('C:/designs/ach_50mm_f4.lhlt')

# If the convex-side-forward orientation looks worse than reversed:
reverse_lens(startSurface=2, count=3)
# re-evaluate
```
