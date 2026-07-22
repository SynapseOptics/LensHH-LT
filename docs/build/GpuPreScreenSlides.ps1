# Generates GpuPreScreen-EveryLensIsAThread.pptx via PowerPoint COM,
# borrowing the LensHH-LT branding (logo on title slide, Calibri /
# dark-brown title color, lead-line + divider body pattern) from the
# Corning demo template at C:\GIT\C1\....
#
# Run from this directory:    pwsh -File GpuPreScreenSlides.ps1
# Output:                     ../GpuPreScreen-EveryLensIsAThread.pptx

$ErrorActionPreference = 'Stop'

$repoRoot     = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$templatePath = 'C:\GIT\C1\LensHH-LT_Corning_Demo_Tanabe_Example2_v6_FINAL2.pptx'
$baseOutPath  = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..')) `
                    'GpuPreScreen-EveryLensIsAThread.pptx'
$imgDir       = Join-Path $repoRoot 'docs\images\optimization'
$externDir    = 'C:\GIT\LayoutIssue'
$guiScreenshot = Join-Path $externDir 'MultiStartImageWithGPUOption.png'

if (-not (Test-Path $templatePath)) {
    throw "Template not found: $templatePath"
}

# Pick a writable output path. If the canonical name is locked because
# PowerPoint already has it open, fall back to a timestamped sibling so
# the user doesn't have to close their reviewing window.
$outPath = $baseOutPath
if (Test-Path $outPath) {
    try {
        Remove-Item -Force -ErrorAction Stop $outPath
    } catch {
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $dir   = Split-Path -Parent  $baseOutPath
        $stem  = [System.IO.Path]::GetFileNameWithoutExtension($baseOutPath)
        $outPath = Join-Path $dir ("{0}-{1}.pptx" -f $stem, $stamp)
        Write-Output "Canonical .pptx is locked; writing to: $outPath"
    }
}

# Copy template → outPath so we inherit the logo on slide 1 and the
# DEFAULT / CONTENT custom layouts + theme.
Copy-Item -Path $templatePath -Destination $outPath -Force

# ─── Open in PowerPoint COM ─────────────────────────────────────────────
$ppt = New-Object -ComObject PowerPoint.Application
$ppt.Visible = -1
$msoTrue  = -1
$msoFalse = 0

# Template brand palette, read from the source slides.
$colBrand    = 0x3D2908  # dark — title text + product brand
$colBody     = 0x202020  # near-black — body content
$colSubtle   = 0x555555  # gray — subtitles, leads, captions
$colDivider  = 0xC0C0C0  # light gray — divider line under the lead

try {
    # Open NOT read-only so we can Save back in place. Args:
    # Open(FileName, ReadOnly, Untitled, WithWindow).
    $pres = $ppt.Presentations.Open($outPath, $msoFalse, $msoFalse, $msoTrue)

    # ─── Slide 1 — keep the logo, rewrite the title text ───────────────
    $s1 = $pres.Slides[1]
    # Walk the textboxes by index. The template's slide 1 had:
    #   [2] Picture 1 (logo) — leave untouched
    #   [3] TextBox 2 = "LensHH-LT"            (brand, 54pt bold)
    #   [4] TextBox 3 = walkthrough title       (24pt)
    #   [5] TextBox 4 = subtitle                (16pt)
    #   [6] TextBox 5 = "Prepared for:"         (12pt)
    #   [7] TextBox 6 = audience                (20pt bold)
    #   [8] TextBox 7 = date                    (12pt)
    # We keep [2] and [3], replace [4]-[8] with our content.
    function Set-TextSafe { param($shape, $text)
        if ($shape -and $shape.HasTextFrame -and $shape.TextFrame.HasText) {
            $shape.TextFrame.TextRange.Text = $text
        }
    }
    # Lookup by Name so re-orderings can't break the script.
    $s1tb = @{}
    foreach ($sh in $s1.Shapes) { $s1tb[$sh.Name] = $sh }
    Set-TextSafe $s1tb['TextBox 3'] 'GPU Pre-Screen Architecture'
    Set-TextSafe $s1tb['TextBox 4'] 'Value-only Multistart sieve, CPU analytic LM polish'
    Set-TextSafe $s1tb['TextBox 5'] 'Release'
    Set-TextSafe $s1tb['TextBox 6'] 'LensHH-LT 1.0.115'
    Set-TextSafe $s1tb['TextBox 7'] (Get-Date -Format 'MMMM d, yyyy')

    # ─── Strip remaining template slides (2..N) ────────────────────────
    while ($pres.Slides.Count -gt 1) {
        $pres.Slides[$pres.Slides.Count].Delete()
    }

    # Pull the CONTENT layout (index 2 per the template) — this is the
    # body-slide background w/ no built-in placeholders so we lay every
    # element out by hand at template coordinates.
    $contentLayout = $pres.SlideMaster.CustomLayouts.Item(2)

    # The CONTENT layout ships with a 'Text 1' banner that reads
    # "LensHH-LT  |  Tanabe US 12,321,041 B2 — Example 2" — Corning-demo
    # specific. It appears on every slide using this layout via layout
    # inheritance, so we rewrite it to a 1.0.115-specific banner before
    # any body slide is added. (The 'Shape 0' background bar and the
    # 'Image 0' footer mark are part of the brand chrome — keep them.)
    foreach ($sh in $contentLayout.Shapes) {
        if ($sh.Name -eq 'Text 1' -and $sh.HasTextFrame -and $sh.TextFrame.HasText) {
            $sh.TextFrame.TextRange.Text = `
                'LensHH-LT 1.0.115  |  GPU Pre-Screen Architecture'
        }
    }

    # ─── Layout helpers ───────────────────────────────────────────────
    function Add-StyledTitle {
        param($slide, [string]$text, [int]$top = 40, [int]$height = 50,
              [int]$size = 32)
        $tb = $slide.Shapes.AddTextbox(1, 32, $top, 893, $height)
        $tr = $tb.TextFrame.TextRange
        $tr.Text          = $text
        $tr.Font.Name     = 'Calibri'
        $tr.Font.Size     = $size
        $tr.Font.Bold     = $msoTrue
        $tr.Font.Color.RGB = $colBrand
        $tb
    }

    function Add-LeadText {
        param($slide, [string]$text, [int]$top = 90, [int]$height = 29,
              [int]$size = 14)
        if (-not $text) { return $null }
        $tb = $slide.Shapes.AddTextbox(1, 32, $top, 893, $height)
        $tr = $tb.TextFrame.TextRange
        $tr.Text          = $text
        $tr.Font.Name     = 'Calibri'
        $tr.Font.Size     = $size
        $tr.Font.Color.RGB = $colSubtle
        $tb
    }

    function Add-Divider {
        param($slide, [int]$top = 85)
        # AddLine(BeginX, BeginY, EndX, EndY)
        $ln = $slide.Shapes.AddLine(32, $top, 925, $top)
        $ln.Line.ForeColor.RGB = $colDivider
        $ln.Line.Weight        = 0.75
        $ln
    }

    function Add-BodyBullets {
        param($slide, [string[]]$lines, [int]$left = 32, [int]$top = 112,
              [int]$width = 893, [int]$height = 380, [int]$size = 18)
        $box = $slide.Shapes.AddTextbox(1, $left, $top, $width, $height)
        $tr  = $box.TextFrame.TextRange
        $tr.Text = ($lines -join "`r")
        $tr.ParagraphFormat.Bullet.Type = 1
        $tr.Font.Name      = 'Calibri'
        $tr.Font.Size      = $size
        $tr.Font.Color.RGB = $colBody
        $tr.ParagraphFormat.SpaceAfter = 6
        $box
    }

    function Add-Caption {
        param($slide, [string]$text, [int]$top = 510, [int]$size = 11)
        if (-not $text) { return $null }
        $tb = $slide.Shapes.AddTextbox(1, 32, $top, 893, 25)
        $tr = $tb.TextFrame.TextRange
        $tr.Text          = $text
        $tr.Font.Name     = 'Calibri'
        $tr.Font.Size     = $size
        $tr.Font.Italic   = $msoTrue
        $tr.Font.Color.RGB = $colSubtle
        $tb
    }

    # ─── Compose a text-only body slide ───────────────────────────────
    function Add-BodySlide {
        param([string]$Title, [string]$Lead = '', [string[]]$Bullets,
              [string]$Caption = '')
        $slide = $pres.Slides.AddSlide($pres.Slides.Count + 1, $contentLayout)
        Add-StyledTitle  -slide $slide -text $Title         | Out-Null
        if ($Lead) { Add-LeadText -slide $slide -text $Lead | Out-Null }
        Add-Divider      -slide $slide                      | Out-Null
        Add-BodyBullets  -slide $slide -lines $Bullets      | Out-Null
        if ($Caption) { Add-Caption -slide $slide -text $Caption | Out-Null }
        $slide
    }

    # ─── Compose a table slide (header row + data rows, full width) ────
    # NOTE on the $Cells parameter: PowerShell parameter binding flattens
    # nested arrays passed to [object[]] params (the leading-comma `,@(...)`
    # row trick does NOT survive parameter binding to [object[]] — all row
    # values end up space-joined into column 1, columns 2..N empty). Pass
    # a flat [string[]] of length nRows * nCols laid out row-major; the
    # helper reshapes internally.
    function Add-TableSlide {
        param([string]$Title, [string]$Lead = '',
              [string[]]$Headers, [string[]]$Cells,
              [string]$Caption = '', [int[]]$ColWidthsPct = $null,
              [int]$HighlightCol = -1)
        $slide = $pres.Slides.AddSlide($pres.Slides.Count + 1, $contentLayout)
        Add-StyledTitle -slide $slide -text $Title         | Out-Null
        if ($Lead) { Add-LeadText -slide $slide -text $Lead | Out-Null }
        Add-Divider     -slide $slide                      | Out-Null

        $nCols     = $Headers.Count
        $nDataRows = [int]($Cells.Count / $nCols)
        if ($Cells.Count -ne ($nDataRows * $nCols)) {
            throw "Cells.Count=$($Cells.Count) is not a multiple of nCols=$nCols"
        }
        $nRows = $nDataRows + 1     # +1 for header row

        $tableLeft   = 32
        $tableTop    = 120
        $tableWidth  = 893
        $tableHeight = 380

        $tbl = $slide.Shapes.AddTable($nRows, $nCols,
                $tableLeft, $tableTop, $tableWidth, $tableHeight).Table

        # Optional fractional column widths (defaults to even split).
        if ($ColWidthsPct -and $ColWidthsPct.Count -eq $nCols) {
            for ($c = 0; $c -lt $nCols; $c++) {
                $tbl.Columns.Item($c + 1).Width = [int]($tableWidth * $ColWidthsPct[$c] / 100.0)
            }
        }

        # Header row: brand background, white text, bold.
        for ($c = 0; $c -lt $nCols; $c++) {
            $cell = $tbl.Cell(1, $c + 1)
            $cell.Shape.Fill.ForeColor.RGB = $colBrand
            $tr = $cell.Shape.TextFrame.TextRange
            $tr.Text          = [string]$Headers[$c]
            $tr.Font.Name     = 'Calibri'
            $tr.Font.Size     = 13
            $tr.Font.Bold     = $msoTrue
            $tr.Font.Color.RGB = 0xFFFFFF
            $cell.Shape.TextFrame.MarginTop    = 4
            $cell.Shape.TextFrame.MarginBottom = 4
            $cell.Shape.TextFrame.MarginLeft   = 6
            $cell.Shape.TextFrame.MarginRight  = 6
        }

        # Data rows from the flat row-major Cells array.
        for ($r = 0; $r -lt $nDataRows; $r++) {
            for ($c = 0; $c -lt $nCols; $c++) {
                $cell = $tbl.Cell($r + 2, $c + 1)
                if (($r % 2) -eq 1) {
                    $cell.Shape.Fill.ForeColor.RGB = 0xF5F1EB
                } else {
                    $cell.Shape.Fill.ForeColor.RGB = 0xFFFFFF
                }
                $tr = $cell.Shape.TextFrame.TextRange
                $tr.Text          = [string]$Cells[$r * $nCols + $c]
                $tr.Font.Name     = 'Calibri'
                $tr.Font.Size     = 12
                $tr.Font.Color.RGB = $colBody
                if ($c -eq $HighlightCol) {
                    $tr.Font.Bold     = $msoTrue
                    $tr.Font.Color.RGB = $colBrand
                }
                if ($c -eq 0) {
                    $tr.Font.Bold = $msoTrue
                }
                $cell.Shape.TextFrame.MarginTop    = 3
                $cell.Shape.TextFrame.MarginBottom = 3
                $cell.Shape.TextFrame.MarginLeft   = 6
                $cell.Shape.TextFrame.MarginRight  = 6
            }
        }

        if ($Caption) { Add-Caption -slide $slide -text $Caption | Out-Null }
        $slide
    }

    # ─── Compose a flowchart body slide (image left, bullets right) ────
    function Add-FlowchartSlide {
        param([string]$Title, [string]$Lead = '', [string]$ImagePath,
              [string[]]$Bullets, [string]$Caption = '',
              [int]$ImageWidth = 220, [int]$ImageHeight = 380)
        $slide = $pres.Slides.AddSlide($pres.Slides.Count + 1, $contentLayout)
        Add-StyledTitle -slide $slide -text $Title         | Out-Null
        if ($Lead) { Add-LeadText -slide $slide -text $Lead | Out-Null }
        Add-Divider     -slide $slide                      | Out-Null
        # Image: AddPicture(FileName, LinkToFile, SaveWithDocument, L, T, W, H)
        $slide.Shapes.AddPicture(
            $ImagePath, $msoFalse, $msoTrue,
            32, 112, $ImageWidth, $ImageHeight) | Out-Null
        $bulletLeft  = 32 + $ImageWidth + 28
        $bulletWidth = 925 - $bulletLeft
        Add-BodyBullets -slide $slide -lines $Bullets `
                        -left $bulletLeft -top 112 -width $bulletWidth `
                        -height 380 -size 17 | Out-Null
        if ($Caption) { Add-Caption -slide $slide -text $Caption | Out-Null }
        $slide
    }

    # ═══ Slide 2 — Map for this deck ═══════════════════════════════════
    Add-BodySlide `
        -Title 'A map for this deck — three flowcharts' `
        -Lead  'Three flowcharts from the LensHH-LT user guide do the heavy lifting in this talk.' `
        -Bullets @(
            'Multistart, CPU-only — the baseline Basin-Hopping loop every release before 1.0.115 ran',
            'Inside one trial: HJ-LM — what each Multistart trial actually does on the CPU',
            'Multistart with GPU pre-screen — what changes in 1.0.115',
            'We walk the three diagrams first, then unpack the GPU mental model: one CUDA thread per candidate lens'
        ) `
        -Caption 'All three diagrams live in docs/optimization.md and the User Guide PDF.' `
        | Out-Null

    # ═══ Slide 3 — Why Basin Hopping ═══════════════════════════════════
    Add-BodySlide `
        -Title 'Basin Hopping is the bottleneck of lens design' `
        -Lead  'Local LM converges in seconds — lens design is a global problem.' `
        -Bullets @(
            'The wall-clock budget of a real design session is dominated by Basin Hopping (Multistart + Metropolis acceptance) — the exploration of design space',
            'Every accepted basin can re-anchor the search; every rejected basin still cost a full HJ-LM polish on the CPU',
            'The fundamental design choice (Cooke vs double-Gauss vs split-flint) is made during exploration. Everything downstream is refinement',
            'Speeding up Basin Hopping = speeding up the actual day-to-day work of a lens designer'
        ) | Out-Null

    # ═══ Slide 4 — Multistart, CPU-only (flowchart) ═══════════════════
    Add-FlowchartSlide `
        -Title 'Multistart today: the CPU-only baseline' `
        -Lead  'Same outer Basin-Hopping loop every release before 1.0.115.' `
        -ImagePath (Join-Path $imgDir 'multistart_architecture.png') `
        -Bullets @(
            'Phase 1: initial LM refines the user start',
            'Phase 2: spawn N_CPU trials in parallel — every trial is a full HJ-LM polish',
            'Each trial is expensive: a typical 20-surface multi-field polish ~80 ms times LM iterations times N_CPU cores',
            'Merge, accept best-of-batch or run Metropolis, then loop',
            'Throughput is bounded by HJ-LM cost times core count — the number of basins you can try in a fixed wall-clock'
        ) `
        -Caption '1_multistart.mmd in docs/build/diagrams/' `
        | Out-Null

    # ═══ Slide 5 — Inside one trial (flowchart) ════════════════════════
    Add-FlowchartSlide `
        -Title 'Inside one trial: HJ-LM' `
        -Lead  'What each Multistart trial actually does on the CPU.' `
        -ImagePath (Join-Path $imgDir 'hj_lm_trial.png') `
        -Bullets @(
            'Each trial clones the system, rolls a glass-swap dice, then perturbs continuous variables',
            'Glass-swap trials get Hooke-Jeeves pattern search PLUS Levenberg-Marquardt (HJ helps escape sharp local minima from the swap)',
            'Continuous trials run LM only — HJ is skipped by default since 2026-05-31',
            'LM with analytical Jacobian (1.0.115) drops the per-iteration cost ~4x vs FD — that win compounds across every trial'
        ) `
        -Caption '2_hj_lm.mmd in docs/build/diagrams/' `
        | Out-Null

    # ═══ Slide 6 — Multistart + GPU pre-screen (flowchart) ════════════
    Add-FlowchartSlide `
        -Title 'Multistart with GPU pre-screen — 1.0.115' `
        -Lead  'Same outer loop. One stage added between candidate generation and HJ-LM polish.' `
        -ImagePath (Join-Path $imgDir 'gpu_prescreen_architecture.png') `
        -Bullets @(
            'GENERATE N = 1,000 to 10,000 candidates — much wider than the CPU could ever polish',
            'GPU PRE-SCREEN: one CUDA launch, full merit value per candidate, ~60 us per design on a 4060',
            'Sort by merit, keep top-K (K = N_CPU, typically 16 to 64) — the rest are abandoned',
            'CPU HJ-LM polish runs only on the survivors — same cost as before, applied to better candidates'
        ) `
        -Caption '3_gpu_prescreen.mmd in docs/build/diagrams/' `
        | Out-Null

    # ═══ Slide 7 — GUI screenshot: how it ships in 1.0.115 ════════════
    # MultiStartImageWithGPUOption.png is 946x723 px (~1.31:1). At 420 pt
    # wide it's 321 pt tall — fits cleanly on the left with bullets on
    # the right at the same template spacing as the flowchart slides.
    Add-FlowchartSlide `
        -Title 'How it looks in 1.0.115' `
        -Lead  'The GPU pre-screen toggle in the Multistart dialog — Beta-labeled, auto-disabled when the design uses features the GPU kernel does not cover.' `
        -ImagePath $guiScreenshot `
        -ImageWidth 420 -ImageHeight 321 `
        -Bullets @(
            'Engine / Derivative controls sit alongside the Hardware acceleration strip',
            'GPU pre-screen toggle defaults ON when a CUDA device is detected and the active design is GPU-eligible',
            'Auto-disables with a visible reason when it cannot help (aspherics today, certain multi-config layouts)',
            'Same dialog, same workflow as before — pre-screen is an additive option, never a behavior change'
        ) `
        -Caption 'Beta in 1.0.115 — surface will harden as we collect production feedback.' `
        | Out-Null

    # ═══ Slide 8 — The shift ═══════════════════════════════════════════
    Add-BodySlide `
        -Title 'The shift: wider exploration in the same wall-clock' `
        -Lead  'Pre-screen cost is ~1000x cheaper per candidate than HJ-LM polish.' `
        -Bullets @(
            'Pre-screen ~60 us per candidate on GPU vs HJ-LM ~80 ms per candidate on CPU',
            'For the same wall-clock that CPU-only spent polishing K candidates, 1.0.115 can pre-screen 1,000K candidates AND polish the K best',
            'CPU-only Basin Hopping was always picking the best-of-K random shots; 1.0.115 picks the best-of-1000K random shots, then polishes those',
            'The exploration radius around the current center grows because we sample more before committing budget',
            'Same Metropolis acceptance, same convergence guarantees — just a wider net cast at each step'
        ) | Out-Null

    # ═══ Slide 9 — Every lens is a thread (CUDA mental model) ═════════
    Add-BodySlide `
        -Title 'Every lens is a thread' `
        -Lead  'The CUDA mental model behind the pre-screen kernel.' `
        -Bullets @(
            'CUDA grid: gridDim = (N candidates), one thread per candidate design',
            'Each thread loads only its own variables — curvature[V], thickness[V], conic[V], material_ids[N_surfaces]',
            'All threads execute the SAME code: the whole evaluate_merit value path',
            'Session-persistent device data shared by all threads: glass catalog, fields, operands, merit template',
            'Thread divergence is data-driven, not code-driven — same instructions, different lens'
        ) | Out-Null

    # ═══ Slide 10 — Batch size math ═══════════════════════════════════
    Add-BodySlide `
        -Title 'How big is the batch, and how fast does it run?' `
        -Lead  'Each lens is a thread. The per-SM concurrent thread budget is FUNCTION-DEPENDENT — set by register and shared-memory pressure per thread.' `
        -Bullets @(
            'evaluate_merit is register-heavy (ray trace + refraction + operand scratch per thread): 256 concurrent threads per SM is the practical budget — well below the hardware''s theoretical 1,536-2,048',
            'Resident-thread budget per launch = N_SMs x 256:',
            '   RTX 4060 (consumer):   24 x 256 = ~6,100 resident lenses (one wave)',
            '   A100 80GB (data center):  108 x 256 = ~27,600 resident lenses — about 4.5x wider wave than 4060',
            '   H100 SXM5 (data center):  132 x 256 = ~33,800 resident lenses — about 5.5x wider wave than 4060',
            'Launching more candidates than one wave is fine — extra warps queue and run as later waves; the SM never idles. The 256 number is wave width, not the launch cap.',
            'Throughput follows the total FP64 TFLOPS ratio.  4060 = 0.24 TFLOPS, A100 = 9.7 TFLOPS, H100 = 34 TFLOPS.  evaluate_merit is FP64-compute-bound (sin / cos / sqrt / division per ray, state lives in registers), so wall-clock scales close to the TFLOPS ratio — memory bandwidth (~7x A100, ~12x H100) sets a floor but does not bind here.',
            'A100 vs 4060: ~40x throughput — process ~40x more lenses per second, or finish the same batch in ~1/40 the time.  H100 vs 4060: ~140x throughput.'
        ) `
        -Caption 'Net effect: A100 finishes the same Multistart loop ~40x faster than 4060, OR covers ~40x more candidates in the same wall-clock minute. H100 pushes that to ~140x. The 4.5x wider wave on A100 is HOW it gets there — not a separate factor on top.' `
        | Out-Null

    # ═══ Slide 11 — Why occupancy matters ═════════════════════════════
    Add-BodySlide `
        -Title 'Why filling GPU occupancy matters' `
        -Lead  'Memory latency is hidden by parallel work, not by faster cores.' `
        -Bullets @(
            'Global memory access takes ~400 to 800 cycles to return on a GPU — much longer than a CPU cache miss',
            'A stalled thread is invisible if the SM can swap in another warp (32 threads) that is ready to run',
            'Each SM needs MANY resident warps to keep its execution units fed — that is the definition of "occupancy". At 256 concurrent threads per SM we get 8 warps in flight per SM — enough to overlap typical memory latency',
            'Under-occupied launches (a few candidates per SM, not a few thousand total) leave SMs idle on memory stalls — wasted silicon, no speedup vs CPU',
            'Multistart''s natural width — 1,000 to 10,000+ random candidates per outer iteration — saturates a consumer 4060''s ~6K-thread budget and starts to make the A100''s ~28K-thread budget worth paying for'
        ) `
        -Caption 'Rule of thumb: launch enough candidates to fill the function-specific concurrent budget. Wider Multistart loops = better GPU utilization = closer to the FP64 ceiling.' `
        | Out-Null

    # ═══ Slide 12 — What runs on the device ═══════════════════════════
    Add-BodySlide `
        -Title 'What runs on the device' `
        -Lead  'Whole evaluate_merit value path, ported to __device__ via LENSHH_HD header refactor.' `
        -Bullets @(
            'About 1,560 lines of merit-eval code — paraxial, refraction, surface intersection, ray trace, robust ray-aim, exit-pupil trace, every operand family',
            'Bit-equal to the CPU value path by construction — same source, just compiled for the device',
            'Validation: representative imaging lenses (Cooke triplet at 6 surfaces, complex multi-element designs at 20+ surfaces) both produce delta = 0 vs CPU evaluate_merit',
            'No derivatives, no Duals, no Jacobian — pure scalar value path'
        ) `
        -Caption 'Phase G1.C: 10 slices, ~2,100 device lines, bedrock 134/134 native tests green throughout.' `
        | Out-Null

    # ═══ Slide 13 — Glass swap ═════════════════════════════════════════
    Add-BodySlide `
        -Title 'Glass swap is a single int' `
        -Lead  'Discrete glass substitutions ride alongside continuous perturbations in the same launch.' `
        -Bullets @(
            'Device-side glass catalog stored once per session: K_glasses x W_wavelengths refractive-index table',
            'Each candidate carries a material_ids[N_surfaces] int32 array — one int per surface',
            'Swapping N-BK7 for N-SK16 on surface 3 changes ONE int — no host->device transfer, no re-upload',
            'A single batched launch can mix continuous candidates with glass-swap variants',
            'Combinatorial coverage of geometry x glass that single-trial CPU never had the budget for'
        ) | Out-Null

    # ═══ Slide 14 — Why value-only ═════════════════════════════════════
    Add-BodySlide `
        -Title 'Why value-only? Why not GPU Jacobian?' `
        -Lead  'Use each piece of silicon where it wins.' `
        -Bullets @(
            'Consumer GPU FP64 throughput is the binding constraint: ~0.23 TFLOPS on a 4060',
            'CPU AVX2 FP64 is comparable or better for sequential dual-number derivative work — about 0.8 TFLOPS',
            'CPU analytic Jacobian on a typical multi-field merit is already 13 ms (4x faster than FD)',
            'GPU comparative advantage is WIDTH — thousands of independent candidates, not narrow derivative chains',
            'GPU for pre-screen breadth, CPU for analytic LM depth'
        ) `
        -Caption 'A100 raises the FP64 ceiling — a full-LM GPU port becomes attractive on data-center hardware. Open question, not a 1.0.115 promise.' `
        | Out-Null

    # ═══ Slide 15 — GPU FP64 landscape (table) ═════════════════════════
    Add-TableSlide `
        -Title 'GPU FP64 landscape — consumer to data center' `
        -Lead  'Why "value-only" is the right architectural choice for consumer GPUs — and what scales on A100 / H100.' `
        -Headers @('Model','SMs','FP32 (TFLOPS)','FP64 (TFLOPS)','Memory','Bandwidth') `
        -Cells @(
            'RTX 4060 (desktop)', '24',  '15.1',     '0.24',                       '8 GB GDDR6',   '0.272 TB/s',
            'A100 80GB',          '108', '19.5',     '9.7 vector / 19.5 tensor',   '80 GB HBM2e',  '2.0 TB/s',
            'H100 SXM5',          '132', '67',       '34 vector / 67 tensor',      '80 GB HBM3',   '3.35 TB/s',
            'H200 SXM',           '132', '67',       '34 vector / 67 tensor',      '141 GB HBM3e', '4.8 TB/s',
            'B200',               '148', '70-80',    '40 tensor',                  '192 GB HBM3e', '8 TB/s',
            'B300',               '148', 'high',     '1.4 (FP64 gutted)',          '288 GB HBM3e', '8 TB/s'
        ) `
        -ColWidthsPct @(20, 8, 16, 24, 18, 14) `
        -HighlightCol 3 `
        -Caption 'Consumer 4060 FP64 ~0.24 TFLOPS, data-center A100 vector FP64 ~9.7 TFLOPS — a ~40x ceiling lift. Note Blackwell B300 explicitly gutted FP64 for AI workloads.' `
        | Out-Null

    # ═══ Slide 16 — FP64 economics (table) ═════════════════════════════
    Add-TableSlide `
        -Title 'FP64 per dollar — what each tier actually costs' `
        -Lead  'Throughput-per-dollar drives the deployment decision. Cloud rental is where the real lever is.' `
        -Headers @('Model','Vector FP64','Buy (USD)','FP64 / $1k','Cloud $/hr','FP64 per $/hr') `
        -Cells @(
            'RTX 4060',  '0.24',     '300',     '0.80',  '0.15', '1.6',
            'A100 80GB', '9.7',      '10,000',  '0.97',  '1.50', '6.5',
            'H100 SXM5', '34',       '28,000',  '1.21',  '2.50', '13.6',
            'H200 SXM',  '34',       '32,000',  '1.06',  '3.70', '9.2',
            'B200',      '20 (est)', '35,000',  '0.57',  '5.50', '3.6',
            'B300',      '1.4 (est)','40,000+', '0.035', 'n/a',  'n/a'
        ) `
        -ColWidthsPct @(16, 16, 16, 16, 16, 20) `
        -HighlightCol 5 `
        -Caption 'Cloud H100 at $2.50/hr buys the most FP64 per dollar-hour. Buying a 4060 for a developer desk still gives surprisingly good FP64 per dollar.' `
        | Out-Null

    # ═══ Slide 17 — Measured throughput ═══════════════════════════════
    Add-BodySlide `
        -Title 'Measured throughput' `
        -Lead  'Where 1.0.115 lands on real fixtures.' `
        -Bullets @(
            'Consumer 4060 (24 SMs, sm_89): about 2x end-to-end Multistart speedup across a representative range of imaging lenses (6 to 20+ surfaces)',
            'Per-design value cost ~60 us — thousands of candidates per second on commodity hardware',
            'CPU pieces (analytic LM, hybrid CT-family / TTRACK / span ops) keep their existing speed unchanged',
            'A100 (data-center): code path is implemented end-to-end and expected to scale with FP64 throughput; cloud-test validation pending',
            'Combined with the analytic-Jacobian + oversubscription fixes, 1.0.115 is the biggest end-to-end Multistart speedup the project has shipped'
        ) | Out-Null

    # ═══ Slide 18 — Summary ═══════════════════════════════════════════
    Add-BodySlide `
        -Title 'Summary' `
        -Lead  'One mental model, three numbers.' `
        -Bullets @(
            'Basin Hopping (Multistart + Metropolis) dominates the wall-clock of a real lens-design session',
            'Every lens is a thread — N candidate designs evaluated in one CUDA launch',
            'GPU runs the whole merit function VALUE — no derivatives, no Jacobian',
            'CPU does the analytical Jacobian during HJ-LM polish on the top-K survivors',
            'Net effect: same Basin-Hopping loop, but the exploration radius widens by ~1000x for the same wall-clock'
        ) | Out-Null

    # Save in place — we opened the copy of the template at $outPath.
    $pres.Save()
    Write-Output "Wrote: $outPath"
    Write-Output "Slides: $($pres.Slides.Count)"
    $pres.Close()
}
finally {
    $ppt.Quit()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($ppt) | Out-Null
    [System.GC]::Collect()
    [System.GC]::WaitForPendingFinalizers()
}
