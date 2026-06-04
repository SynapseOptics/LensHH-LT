# Flowchart sources

Mermaid sources for the optimization-chapter flowcharts in
`docs/optimization.md`. PNGs rendered from these live under
`docs/images/optimization/`.

| Mermaid source | Rendered PNG (under `docs/images/optimization/`) |
|---|---|
| `1_multistart.mmd` | `multistart_architecture.png` |
| `2_hj_lm.mmd` | `hj_lm_trial.png` |
| `3_gpu_prescreen.mmd` | `gpu_prescreen_architecture.png` |

## Re-rendering after an edit

Install [`@mermaid-js/mermaid-cli`](https://github.com/mermaid-js/mermaid-cli)
(needs Node 18+; puppeteer is bundled). One-off via `npx`:

```bash
npx -y @mermaid-js/mermaid-cli \
  -i docs/build/diagrams/1_multistart.mmd \
  -o docs/images/optimization/multistart_architecture.png \
  -w 1400
```

Or install once globally (`npm i -g @mermaid-js/mermaid-cli`) and use
the `mmdc` binary directly. Use width `-w 1400` for the two
horizontal Multistart diagrams and `-w 1200` for the HJ-LM detail
(narrower and taller).

The PDF/HTML build (`docs/build/build-pdf.bat`) does **not** re-render
diagrams — it just embeds the PNGs already on disk. After editing a
`.mmd`, re-render, then rerun the doc build.
