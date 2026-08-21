# Flowchart sources

Mermaid sources for the optimization-chapter flowcharts in
`docs/optimization.md`. PNGs rendered from these live under
`docs/images/optimization/`.

| Mermaid source | Rendered PNG (under `docs/images/optimization/`) |
|---|---|
| `1_multistart.mmd` | `multistart_architecture.png` |
| `2_hj_lm.mmd` | `hj_lm_trial.png` |
| `3_gpu_prescreen.mmd` | `gpu_prescreen_architecture.png` |

Each `.mmd` carries a `config:` front-matter block (theme, palette,
spacing), so no extra theme flags are needed at render time — only
`-o` and `-w`. Colours follow a shared palette: green = terminals,
blue = phase/engine steps, amber = decisions, grey = plumbing, and
**purple = the convergence levers** (reduced-dim perturbation,
Metropolis, basin-memory / diverse restart, and the GPU pre-screen).

## Re-rendering after an edit

Install [`@mermaid-js/mermaid-cli`](https://github.com/mermaid-js/mermaid-cli)
(needs Node 18+; puppeteer is bundled). One-off via `npx` — render all three
with a white background:

```bash
npx -y @mermaid-js/mermaid-cli -i docs/build/diagrams/1_multistart.mmd    -o docs/images/optimization/multistart_architecture.png   -w 1500 -b white
npx -y @mermaid-js/mermaid-cli -i docs/build/diagrams/2_hj_lm.mmd         -o docs/images/optimization/hj_lm_trial.png               -w 1300 -b white
npx -y @mermaid-js/mermaid-cli -i docs/build/diagrams/3_gpu_prescreen.mmd -o docs/images/optimization/gpu_prescreen_architecture.png -w 1500 -b white
```

Or install once globally (`npm i -g @mermaid-js/mermaid-cli`) and use
the `mmdc` binary directly. Use width `-w 1500` for the two Multistart
architecture diagrams and `-w 1300` for the HJ-LM detail.

The PDF/HTML build (`docs/build/build-pdf.bat`) does **not** re-render
diagrams — it just embeds the PNGs already on disk. After editing a
`.mmd`, re-render, then rerun the doc build.
