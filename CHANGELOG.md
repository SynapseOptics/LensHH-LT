# Changelog

All notable changes to LensHH-LT and the LensHH-LT-Engine.

## 1.0.114 — 2026-05-21

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

### Build & deploy

- Engine commits: `3e1e5c9` (Adjust sentinel collision + `-5`
  sentinel), `ef01e85` (BH no-improvement watchdog), `a1e3d89`
  (`SurfaceSentinelResolver` + IsSurfaceCovered fixes). All built
  through `LensHH-LT-Engine/scripts/publish-obfuscated.bat` and the
  obfuscated `LensHH.Core.dll` deployed into `LensHH-LT/engine/`.
- LT installer rebuilt via `installer/LensHH-LT.iss`. Filename
  remains `LensHH-LT-Setup-1.0.114.exe`.
