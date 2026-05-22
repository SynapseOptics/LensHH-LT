# LensHH-LT — Optical Lens Design

**LensHH-LT** is a sequential optical-design tool for refractive and
reflective imaging systems — triplets, Petzvals, double Gauss,
Schmidt-Cassegrain and Maksutov telescopes, and similar designs.

Load a starting design from ZEMAX (`.zmx`), OSLO (`.len`), Optalix
(`.otx`), Code V (`.seq`), Optiland (`.json`), or LensHH-LT's native
`.lhlt`; explore it
through a full set of polychromatic analyses (spot, MTF, wavefront,
ray and OPD fans, Seidel, Zernike, distortion, field curvature,
relative illumination, chromatic focal shift); and refine it with
merit-function-driven optimization (Levenberg-Marquardt, multistart,
basin hopping) plus design-shape operators (split-element,
SPC synthesis) and per-element glass substitution.

The same engine is reachable from a C# API, an interactive CLI, and
an MCP server for LLM agents (Claude Code, Cursor, etc.). The
desktop app runs on Windows, macOS, and Linux.

This guide walks through the GUI, every analysis panel, every
merit-function operand, the optimizers, the glass-catalog system,
and the programmatic interfaces.

## Pages

- **[Getting Started](getting-started.md)** — install, open a sample lens,
  run a first analysis, run a first optimization.
- **[Analyses Reference](analyses.md)** — every analysis panel (spot,
  MTF, wavefront, ray fans, Seidel, Zernike, etc.) with inputs and
  interpretation tips.
- **[Merit Function Reference](merit-function.md)** — every merit function
  operand with its inputs, meaning, and typical usage.
- **[Optimization](optimization.md)** — the three optimizers (Local LM,
  Multistart, Basin Hopping) plus design-shape operators
  (split-element, SPC synthesis), and when to use each.
- **[Glass Catalogs](glass-catalogs.md)** — AGF format, the five shipping
  catalogs, preference order, custom catalogs via GlassCatalogGenerator,
  and glass substitution during optimization.
- **[API, CLI, and MCP](api-cli-mcp.md)** — programmatic access: embed the
  C# API in your own .NET code, drive the engine from the interactive
  CLI, or expose it to Claude/Cursor/other MCP hosts.
- **[Agent Workflow — Stock-Lens-Based Design](agent-stock-lens-workflow.md)** —
  recipe for an LLM agent to chain `search_stock_lenses` →
  `insert_stock_lens` → analyze → `reverse_lens` → optimize against the
  bundled 7,600-part Edmund + Thorlabs + Ross Optical catalog.
- **[Sasian Design — Stock-Lens Triplet Pipeline](sasian-design.md)** —
  end-to-end MCP orchestrator (`sasian_design_start` / `_status` /
  `_cancel` / `_discard`) that builds a Cooke-triplet skeleton,
  free-optimizes with glass substitution, then walks element-by-element
  replacing each free-form singlet with the best matching catalog
  stock part. Phase 2 entry for doublet retrofits.
- **[Building from Source](building.md)** — required on macOS (no
  installer); also covers Linux and source-build options on Windows.

## Scope

LensHH-LT supports the common workflow of a general-purpose lens designer:

- Loading lens files from ZEMAX (`.zmx`), OSLO (`.len`), Optalix
  (`.otx`), Code V (`.seq`), Optiland (`.json`), or LensHH-LT's
  native `.lhlt`.
- Standard and Even Asphere surfaces.
- EPD or F/# apertures, object-angle or object-height fields.
- Polychromatic spot, PSF, and MTF analyses (wavefront, OPD,
  Zernike, and Seidel are evaluated one wavelength at a time).
- Merit-function-driven optimization with variables and pickups.
- Glass catalogs in AGF format (five catalogs ship with the installer).

It does **not** handle configurations, coatings,
thermal/environmental effects, tolerancing data, tilts and
decenters, folds, or non-standard surface types. For those,
see the full LensHH product.

## Feedback

File issues or suggestions at the project's GitHub repository.
