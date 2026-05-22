# Sasian Design — Stock-Lens Triplet Pipeline

`sasian_design` is an end-to-end MCP orchestrator that takes a merit
function definition (spec only — fields, wavelengths, aperture, stop,
operands) and produces a finished triplet built **entirely out of
catalog stock lenses**. It implements José Sasián's "free-optimize,
then substitute" recipe: seed a Cooke-triplet skeleton, basin-hop it
to a low-merit polychromatic minimum with full glass substitution,
then walk element-by-element replacing each free-form singlet with
the best matching stock part (or stock pair) and re-optimizing after
each swap.

The pipeline runs on a worker task. The caller starts it, polls for
status, and inspects the intermediate `.lhlt` files saved at every
phase boundary.

## Related pages

- **[Agent Workflow — Stock-Lens-Based Design](agent-stock-lens-workflow.md)** —
  the per-tool chain (`search_stock_lenses`, `insert_stock_lens`,
  `replace_element`, etc.) that `sasian_design` automates end-to-end.
- **[Optimization](optimization.md)** — Basin Hopping + LM, which
  `sasian_design` uses for every re-optimization.
- **[Merit Function Reference](merit-function.md)** — operands the
  template `.lhlt` must contain. § *Surface sentinel values* and
  § *PenalizeVignetting* are particularly relevant for stock-lens
  designs.
- **[Glass Catalogs](glass-catalogs.md)** — `StockGlassesVisible.AGF`
  and `StockGlassesUV.AGF`, the curated glass sets the free-opt phase
  substitutes against.

---

## The chain

```
build_skeleton  (one-shot — or sasian_design does it for you)
   ↓
sasian_design_start   →  jobId
   ↓ (worker task running on the server)
sasian_design_status  →  current phase + trial table
   ↓
[ optionally  sasian_design_cancel  ]
   ↓ on completion
intermediate .lhlt files in outputDir/, ending in
   NN_final_allstock_merit<m>.lhlt
   ↓
sasian_design_discard  (optional — frees per-job memory)
```

Files saved at each phase boundary:

| File | Written after |
|---|---|
| `01_freeopt.lhlt` | Phase B (free-optimize) | 
| `01_starting.lhlt` | Phase 2 entry only (`startFromCurrentSystem=true`) |
| `02_E1_<descr>_merit<m>.lhlt` | Element 1 winner accepted |
| `03_E2_<descr>_merit<m>.lhlt` | Element 2 winner accepted |
| `0N_final_allstock_merit<m>.lhlt` | Pipeline complete |

The number of files is one per phase boundary plus the final, so a
3-element skeleton produces 5 files (`01_freeopt`, `02_E1_…`,
`03_E2_…`, `04_E3_…`, `05_final_allstock_…`).

---

## MCP tools

### `sasian_design_start`

Start the pipeline. Returns a `jobId` immediately; the worker runs in
the background.

```
sasian_design_start(
    templatePath,
    outputDir,
    architecture = "single-single-single",
    candidatesPerPattern = 3,
    bhMaxHops = 2000,
    bhLmPerHop = 4000,
    bhHjPerHop = 30,
    bhNoImprovementSeconds = 600,
    monochromaticPhase1 = false,
    startFromCurrentSystem = false,
    stopPosition = 2,
    semiDiameterSeed = 12.5,
    airGapSeed = 10,
    bflSeed = 45,
    substitutionCatalog = "auto")
```

### `sasian_design_status(jobId)`

Returns the current phase, elapsed time, trial table, accepted
winners, saved file list, and any error. Sample output (mid-run):

```
jobId:   7fe817b7
state:   Running
phase:   substituting element 2/3
elapsed: 2814.5 s
elements: 2/3
freeopt merit: 1.4530E-02
Trials so far:
  #   E  pattern         parts                          merit        status
  1   1  single          L-BCX154                       1.1600E-02   ★ ok
  2   1  split_pcx       L-PCX341+L-PCX349:rev          3.3800E-03   ok
  3   1  split_pcx_pcc   L-PCX217+L-DCV022:rev          4.1200E-03   ok
  4   2  single          L-DCX116                       …            pending

Accepted winners:
  E1: split_pcx L-PCX341+L-PCX349:rev → merit 3.3800E-03

Saved intermediates:
  C:\runs\v11\01_freeopt.lhlt
  C:\runs\v11\02_E1_L-PCX341_L-PCX349_rev_merit0.0034.lhlt
```

### `sasian_design_cancel(jobId)`

Cooperative cancel. Any element already accepted keeps its winner;
the current trial is interrupted at the next safe point. The session
state holds whatever the pipeline reached.

### `sasian_design_discard(jobId)`

Frees the per-job state (trial list, winner list, saved-file list).
**Does not delete the `.lhlt` files on disk** — those persist for
later inspection or as restart points.

---

## Parameters in detail

### `templatePath`

A `.lhlt` file holding **the spec only**:

- Fields (object-angle or object-height; off-axis fields recommended)
- Wavelengths (with weights — the pipeline reads them at start)
- Aperture (EPD or F/#)
- A stop placeholder surface — the skeleton builder drops it and
  inserts a real physical stop at the chosen `stopPosition`
- Merit-function operands (RMS-spot, distortion, vignetting,
  thickness/edge constraints — use surface sentinels for spans:
  `CTA(-5,-1)`, `EG(-5,-1)`, etc. since absolute surface indices will
  shift during substitution)

The pipeline replaces the lens stack but preserves everything else.
For a clean working template see `samples/UserGuide/SasianTemplate/
StartingLensTemplate.lhlt`.

**Ignored** when `startFromCurrentSystem=true` — pipeline reuses the
system already loaded in the session.

### `outputDir`

Folder for intermediate `.lhlt` saves. Created if absent. The
worker also saves the merit function and config editor with each
file, so any intermediate is a valid restart point — `load_system`
on `02_E1_…` and continue from there manually.

### `architecture`

`"single-single-single"` (or `"cooke"`) — currently the only
supported architecture. Three contiguous-glass elements separated
by air gaps; aperture stop in one of the four candidate positions
(see `stopPosition`).

### `candidatesPerPattern` (default 3)

Top-N stock candidates to try per pattern per element. Per element
the pipeline runs roughly **3 patterns × N candidates** Basin-Hopping
re-optimizations. With defaults that's about 9 trials per element,
27 total across a 3-element skeleton.

| Value | Wall time (rough) | When |
|---|---|---|
| `1` | ~25 min total | Fast exploration; the answer might be one of the rejected siblings |
| `2` | ~50 min total | Reasonable balance |
| `3` (default) | ~75 min total | Thorough; usually finds the global best |
| `≥5` | Hours | Diminishing returns; the ranker already filters by EFL/N_d/aperture fit |

### `bhMaxHops`, `bhLmPerHop`, `bhHjPerHop`

Basin-Hopping + Levenberg-Marquardt budget per optimization run.
Defaults are tuned for stock-substitution problems where each
candidate splice changes the optical layout enough that the
optimizer often has to climb out of a different basin.

| Param | Default | Notes |
|---|---:|---|
| `bhMaxHops` | 2000 | Outer basin-hop count. Practical termination is `bhNoImprovementSeconds`, not exhaustion. |
| `bhLmPerHop` | 4000 | LM iterations per hop. *Upper bound* — LM stops earlier on tolerance. The Basin-Hopping engine class default (60) is far too low for stock-substitution problems and aborts mid-progress. |
| `bhHjPerHop` | 30 | Hooke-Jeeves polish steps after each LM converges. |

These match the GUI's *BasinHoppingHjLm* dialog preset.

### `bhNoImprovementSeconds` (default 600)

No-improvement watchdog. Each BH phase terminates if best merit
hasn't improved within this many seconds. The timer resets on every
best-merit improvement (sawtooth, not held at ceiling — see
*Optimization* doc). With the default of 600 s (10 min) a normally-
progressing stock-substitution converges in 1–3 minutes per trial.
Set to `0` to disable and let BH run all `bhMaxHops`.

### `monochromaticPhase1` (default `false`)

When `true`: at pipeline start, capture the template's wavelength
weights, then **zero every non-primary weight**. The optimizer
minimizes a d-line-only merit during shape + glass exploration —
freed from chromatic-aberration trade-offs. Just before the final
`*_final_allstock_*.lhlt` save, the original weights are restored.

The intermediate `01_freeopt.lhlt` and per-element files **carry the
zeroed weights** and reflect the monochromatic merit. Only the final
file is restored. This makes monochromatic Phase 1 a clean starting
point for a follow-up Phase 2 doublet/chromatic-correction step.

### `startFromCurrentSystem` (default `false`) — Phase 2 entry

When `true`: **skip** `LoadTemplate`, `BuildSkeleton`, and the
free-opt phase. Go directly to the per-element substitution loop on
whatever system is currently loaded in the session. The loop targets
elements via `CurvatureVariable=true` on the front surface, so the
caller picks which elements get re-substituted by unlocking those
flags before starting.

The intended workflow:

1. `load_system` a Phase 1 all-stock design (e.g. v11's
   `0N_final_allstock_merit0.0116.lhlt`).
2. `replace_element` to swap one positive singlet for a candidate
   doublet (e.g. an achromat with EFL ≈ singlet's EFL and similar CT).
3. **Manually unlock one neighbor**:
   - `set_curvature_variables` on both glass surfaces
   - `set_thickness_variables` on its glass thickness
   - `set_glass_substitution` with the visible/UV stock catalog
4. `sasian_design_start templatePath="" outputDir=... startFromCurrentSystem=true`.

The pipeline re-substitutes only the unlocked neighbor, giving the
design enough freedom to absorb the doublet's different power balance
while every other element stays locked to its existing stock part.

The `templatePath` parameter is unused in this mode but still
required by the signature (pass `""` or the original template path).

**Pitfall**: forgetting to unlock at least one neighbor produces an
immediate `"requires at least one element with CurvatureVariable=true
on its front surface; none found"` error.

### `stopPosition` (default 2)

Where the physical aperture stop goes in the skeleton.

| Value | Position |
|---|---|
| `0` | Leading air (before L1) |
| `1` | Between L1 and L2 |
| **`2`** (default) | **Between L2 and L3** (classic Cooke — near-symmetry around L2) |
| `3` | BFL gap (after L3) |

Position 2 is the historical Cooke convention; the near-symmetry
about the stop helps the optimizer correct coma and lateral color in
the free-opt phase. Position 1 is occasionally useful for designs
where L3 has to be small (entrance pupil close to L3).

### `semiDiameterSeed`, `airGapSeed`, `bflSeed`

Skeleton seed numbers. They get re-optimized away during free-opt —
small inaccuracies cost only a few extra LM iterations. Defaults
(12.5 / 10 / 45 mm) work for ~25 mm aperture, ~50 mm EFL designs.

### `substitutionCatalog` (default `"auto"`)

Glass-substitution catalog used by Basin Hopping on every hop. The
free-opt phase doesn't lock glass to a specific catalog; instead it
substitutes against the chosen filtered catalog so the winning glass
is always one that actually exists in stock parts.

| Value | Resolves to |
|---|---|
| `"auto"` (default) | `StockGlassesUV` if `min(wavelengths) < 0.380 µm`, else `StockGlassesVisible` |
| `"StockGlassesVisible"` | 19 glasses harvested from non-aspheric singlets |
| `"StockGlassesUV"` | 2 base glasses + aliases (CaF₂ + Corning HPFS 7980 fused silica) |
| `""` (empty) | Disable glass substitution entirely — free-opt stays on the seed glasses (N-BK7 / N-SF11 / N-BK7) |

Basin Hopping auto-detects which surfaces are eligible: any element
with a reshaping variable (curvature, glass-thickness, conic, or
asphere) on its front or back face. Locked stock parts inserted by
`replace_element` have those flags clear, so substitution skips them
automatically. **Skeleton elements stay eligible — no manual
surface-tracking needed.**

---

## What each phase does

### Phase A — Load template + build skeleton

(Skipped when `startFromCurrentSystem=true`.)

1. Load `.lhlt` template into the session.
2. Drop the template's dummy stop, preserving the
   OBJ → first-refractive distance by collapsing the air gap.
3. Insert three singlets in sequence with seed prescription:
   - L1: R₁=+50, R₂=−50, CT=4, **N-BK7**, air after = `airGapSeed`
   - L2: R₁=−30, R₂=+30, CT=3, **N-SF11**, air after = `airGapSeed`
   - L3: R₁=+50, R₂=−50, CT=4, **N-BK7**, air after = `bflSeed`
   - Every curvature and thickness flagged `Variable=true`.
4. Insert a real flat stop surface (`IsStop=true`, `Radius=1e18`) into
   the air gap selected by `stopPosition`, splitting the gap in half
   (each side gets `original / 2`).
5. Enable glass substitution on every skeleton glass surface against
   the resolved catalog (auto-UV/Visible).
6. Rewrite merit-function span operands' `Surface1` from `-3` to `-5`
   so spans like `CTA(-5,-1)` cover the full element stack from the
   first refractive surface to the last, even after the stop moves.

### Phase B — Free-optimize

Basin-Hopping + LM with full glass substitution against the resolved
catalog. Locked stock parts (from prior iterations or from the caller
in Phase 2) are auto-skipped. Terminates on `bhMaxHops` exhausted OR
`bhNoImprovementSeconds` watchdog firing.

Saves `01_freeopt.lhlt`. Records `data.FreeOptMerit`.

### Phase C — Per-element substitution

A `while` loop, not a `for` loop, because split-substitution patterns
can insert two elements where one used to be — a `for elem=1..N`
loop would target the wrong element on later iterations.

Instead the loop finds the next unsubstituted element each iteration:
the next contiguous-glass block whose front surface still has
`CurvatureVariable=true`. `build_skeleton` sets that flag on every
skeleton element; `replace_element` clears it on inserted stock
parts. The flag IS the "still skeleton?" signal.

For each element:

1. Snapshot the current best system state.
2. Compute the element's target params (EFL, n_d, semi-diameter)
   from its current radii and thickness.
3. **Pick patterns based on sign**:
   - EFL > 0: `single`, `split_pcx`, `split_pcx_pcc`
   - EFL < 0: `single`, `split_pcc`, `split_pcx_pcc`
4. **For each pattern, find top-N stock candidates** by querying the
   SQLite catalog (see *Stock-candidate selection* below).
5. **For each candidate**: restore snapshot → splice candidate via
   `replace_element` → re-optimize via Basin Hopping + LM → record
   the post-reopt merit on the trial.
6. **Pick the best trial by merit**, apply it permanently, save the
   intermediate `.lhlt`, log the winner.

### Phase D — Done

Restore original wavelength weights (if monochromatic Phase 1 was
active). Evaluate the *polychromatic* final merit. Save
`0N_final_allstock_merit<m>.lhlt`.

---

## Stock-candidate selection

`FindMatchingStockInline` queries
`catalogs/stock-lens-catalog.sqlite` directly. Pool filters:

- `import_status='ok'`
- `n_elements=1` (singlets only — doublets are **not** considered as
  candidates; they only appear via the Phase 2 doublet-retrofit
  workflow described above)
- `2·semiDiameter ≤ diameter_mm ≤ 4·semiDiameter` (lower bound is
  the target's clear aperture; upper bound caps over-large parts)
- Family does not contain `"Aspheric"` (aspherized achromats /
  aspheric singlets are excluded)

### Patterns

| Pattern | Element sign | Pool A | Pool B | Notes |
|---|---|---|---|---|
| `single` | either | full pool | n/a | One stock part replaces the element. |
| `split_pcx` | positive | PCX only (EFL > 0) | same as A | Two PCX lenses, second reversed (plano-to-plano). Symmetric: A ≤ B by `(part, vendor)`. |
| `split_pcc` | negative | PCC only (EFL < 0) | same as A | Two PCC lenses, second reversed. Symmetric. |
| `split_pcx_pcc` | either | PCX (EFL > 0) | PCC (EFL < 0) | One of each, second reversed. |

Split-PCX is the *split-PCX trick*: two plano-convex lenses placed
plano-to-plano give a free shape factor while remaining catalog-only.
Reversing the second part puts the planos back-to-back — a single
"virtual" element with tunable bending.

### Ranking

**Singlet**:
- Filter by `|EFL_part − EFL_target| ≤ 15% · |EFL_target|`.
- Sort by `|ΔEFL|/|EFL_target| + 5·|Δn_d|` (EFL fit dominates;
  glass-index fit is a secondary tie-breaker).

**Pair** (one of the split patterns):
- For each `a` in poolA, solve `1/EFL_target = 1/EFL_a + 1/EFL_b` for
  the required `EFL_b`; pick the `b` in poolB with smallest
  `|EFL_b − required|`.
- Filter combined `|EFL_combined − EFL_target| ≤ 15% · |EFL_target|`.
- Sort by `combined_err + 0.5·glass_mismatch + 0.25·aperture_mismatch`
  where `glass_mismatch = |Δn_d_a| + |Δn_d_b|` and
  `aperture_mismatch = |log₂(D_a / D_b)|` (penalize aperture
  asymmetry, slightly).

If no candidate passes the 15% EFL tolerance the trial list for that
element will be empty and the pipeline **aborts the substitution
phase** (TODO: skip-and-continue is not yet implemented; the
element remains as the free-opt skeleton glass).

---

## Worked example — Cooke triplet, F/2.8, 50 mm, ±20°

Starting point: a 4-surface template (OBJ → stop placeholder → IMG)
with merit-function operands:

```
RMS spot for each field
DIST < 2% on max field
CTA(-5,-1) > 1.5 mm   (minimum centre thickness across all elements)
EG(-5,-1)  > 1.0 mm   (minimum edge thickness across all glass)
PenalizeVignetting = true  (system property)
```

Saved at `C:\Projects\F2.8-50-20\template.lhlt`.

### Phase 1 — free-opt + all-stock substitution

```
sasian_design_start
  templatePath = "C:\Projects\F2.8-50-20\template.lhlt"
  outputDir    = "C:\Projects\F2.8-50-20\v1"
  // accept all defaults
```

Returns `jobId=a3f...`. Poll with `sasian_design_status`. ~75 min
later (3 elements × 9 trials each × ~3 min per trial) the pipeline
finishes. `v1/` contains:

```
01_freeopt.lhlt                            (merit ≈ 0.014)
02_E1_<descr>_merit<m>.lhlt
03_E2_<descr>_merit<m>.lhlt
04_E3_<descr>_merit<m>.lhlt
05_final_allstock_merit<m>.lhlt            ← your finished design
```

Open `05_final_allstock_…` — every element is a real catalog part,
spec-compliant, BFL set, vignetting controlled.

### Phase 2 (optional) — retrofit a doublet

If the Phase 1 final has noticeable chromatic-aberration residuals,
try replacing one positive singlet with a stock achromat:

```python
load_system "C:\Projects\F2.8-50-20\v1\05_final_allstock_merit0.0116.lhlt"

# Replace element 1 (the front positive singlet) with achromat 49-371
replace_element elementIndex=1 partNumber="49-371"

# Unlock element 2 (the negative middle) so the design has
# DOF to absorb the doublet's different power balance.
set_curvature_variables  surfaces="3,4"  variable=true
set_thickness_variables  surfaces="3"    variable=true
set_glass_substitution   surfaces="3"    catalog="StockGlassesVisible"

sasian_design_start
  templatePath           = ""    # ignored in Phase 2
  outputDir              = "C:\Projects\F2.8-50-20\v2"
  startFromCurrentSystem = true
```

The pipeline runs the substitution loop on element 2 only (because
element 2 is the only one with `CurvatureVariable=true` after the
doublet swap and lock). Output:

```
01_starting.lhlt                        (merit before substitution)
02_E1_<descr>_merit<m>.lhlt              (best new neighbor)
03_final_allstock_merit<m>.lhlt          ← finished retrofit
```

Compare `v1/05_final_…` to `v2/03_final_…`. If the doublet retrofit
beat Phase 1, ship `v2`. Otherwise revert to `v1`.

---

## Tips and pitfalls

### Use surface sentinels in span operands

Absolute surface indices shift during skeleton-build and
substitution. Write spans as `CTA(-5, -1)` / `EG(-5, -1)` /
`CTG(-5, -1)` etc. so they automatically follow the first and last
refractive surfaces. See § *Surface sentinel values* in
*merit-function.md* for the full reference.

### Use `PenalizeVignetting=true` for stock-lens designs

Stock-lens diameters are catalog-fixed; the optimizer cannot grow a
lens to admit clipped off-axis rays. Without `PenalizeVignetting`,
the merit function will quietly accept aggressive vignetting because
failed rays are dropped from the per-field RMS sum. Turn it on in
the template's system properties (GUI: System Editor; MCP:
`set_penalize_vignetting`).

### Set `bhLmPerHop ≥ 4000`

The Basin-Hopping engine class default (`60`) is far too low for
stock-substitution problems and aborts mid-progress. The pipeline
already passes `4000` as the default, but if you override it,
keep it high.

### Set CTG/EG operand weights to 10, not 1

Centre-thickness and edge-thickness penalty operands need *enforcement-
level* weight against the RMS-spot terms. Weight 1 lets the optimizer
buy aberration relief by violating physical thickness limits.
Recommended: `Weight=10` on every CT/CTG/EA/EG operand in the
template.

### Phase 2 needs at least one unlocked neighbor

After `replace_element` inserts a doublet (or any stock part),
**none** of the resulting glass surfaces have `CurvatureVariable=true`.
If you forget to unlock a neighboring singlet before calling
`sasian_design_start startFromCurrentSystem=true`, the pipeline
errors immediately. Unlock at least one element's curvature variables
(and ideally its glass-substitution flag) before kicking off Phase 2.

### Intermediates are valid restart points

Every `0N_…lhlt` includes the merit function and config editor.
`load_system` on any of them gives you a working session you can
continue manually (`optimize`, `replace_element`, analyses) without
restarting the pipeline.

### Phase 1 merit floor depends on doublet choice (Phase 2)

In the v11 → v12 development cycle, retrofitting an L-BCX154 singlet
with an 89-681 doublet (CT=26 mm) hit a merit floor of 0.346 — the
thick doublet ate the track budget. The same swap with a 49-371
doublet (same EFL 50 mm, but CT=13.2 mm) dropped the merit to
0.00829 — beating the v11 Phase 1 baseline by ~28%. **Doublet CT
matters as much as doublet EFL** when picking the retrofit
candidate; small EFL mismatches are easy to absorb, large CT
differences are not.

---

## Internals reference

| Source | Role |
|---|---|
| `src/LensHH.Mcp/Tools/SasianDesignTools.cs` | MCP-tool surface; 4 tools (start / status / cancel / discard). |
| `src/LensHH.Mcp/SasianDesignService.cs` | Pipeline orchestrator. Owns `SasianJobData`, runs the worker task. |
| `LensHH.Core.Optimization.BasinHoppingOptimizer` | The actual optimizer the pipeline invokes for free-opt and every per-element re-opt. |
| `LensHH.Mcp.StockLensCatalog` | SQLite resolver — `ResolvePart`, `ResolveDbPath`, `ResolveLhltPath`. |
| `LensHH.Core.IO.LensInsertHelpers` | Vertex extraction + reversal used by `ReplaceElementInline`. |
| `LensHH.Core.Models.SurfaceSentinelResolver` | Resolves the `-5 / -4 / -3 / -1` sentinels in merit-function spans. |
