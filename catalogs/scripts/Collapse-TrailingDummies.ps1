# Collapse-TrailingDummies.ps1
#
# One-off cleanup: for each stock-lens .lhlt that has extra air-only "dummy"
# surfaces between the last refractive vertex and the IMG surface, sum the
# trailing thicknesses into the lens-back vertex and remove the dummies.
#
# Why: some vendor .zmx files (notably Edmund 29-094 / 29-095 plano-convex
# singlets and a handful of Thorlabs AC / LA / LJ) include a best-focus
# offset surface between the lens back and IMG. Our import preserves them
# faithfully but they aren't real optical surfaces — they just position the
# IMG plane at the vendor's preferred focus. For our stock-lens-as-building-
# block use, the offset is noise: the host system has its own image plane.
# Plus the engine's paraxial BFL formula (-y/u at the surface *immediately*
# before IMG) reads 0 when an offset dummy sits at the paraxial focus.
#
# Idempotent. Re-runs are safe.

[CmdletBinding()]
param(
    [string] $LensesRoot = "C:\GIT\SynapseLensHH-LT\LensHH-LT\catalogs\Lenses"
)

$ErrorActionPreference = 'Stop'

$cleaned = 0
$untouched = 0
Get-ChildItem -Path $LensesRoot -Recurse -Filter *.lhlt -File | ForEach-Object {
    $j = Get-Content $_.FullName -Raw | ConvertFrom-Json
    $surfs = $j.Surfaces
    if (-not $surfs -or $surfs.Count -lt 4) { $untouched++; return }

    # Find the last surface that has a non-empty Material — that's the last
    # surface where light *enters* glass. The surface immediately after it is
    # the lens back (air-side exit of the final element).
    $lastGlassIdx = -1
    for ($i = 0; $i -lt $surfs.Count; $i++) {
        if (-not [string]::IsNullOrEmpty($surfs[$i].Material)) { $lastGlassIdx = $i }
    }
    if ($lastGlassIdx -lt 0) { $untouched++; return }

    $lastBackIdx = $lastGlassIdx + 1
    $imgIdx      = $surfs.Count - 1

    # Anything strictly between lastBackIdx and imgIdx is a trailing dummy.
    $nTrail = $imgIdx - $lastBackIdx - 1
    if ($nTrail -le 0) { $untouched++; return }

    # Sum trailing thicknesses (lens back through last pre-IMG surface).
    $sumT = 0.0
    for ($i = $lastBackIdx; $i -lt $imgIdx; $i++) {
        $t = $surfs[$i].Thickness
        if ($t -isnot [string]) { $sumT += [double]$t }
    }

    # Collapse: set lens-back thickness to the sum; drop the intermediate dummies.
    $surfs[$lastBackIdx].Thickness = $sumT
    $newSurfs = New-Object Collections.Generic.List[psobject]
    for ($i = 0; $i -le $lastBackIdx; $i++) { $newSurfs.Add($surfs[$i]) }
    $newSurfs.Add($surfs[$imgIdx]) # IMG stays as the last surface
    # Reindex.
    for ($i = 0; $i -lt $newSurfs.Count; $i++) { $newSurfs[$i].Index = $i }
    $j.Surfaces = $newSurfs

    $json = $j | ConvertTo-Json -Depth 10
    Set-Content -Path $_.FullName -Value $json -NoNewline -Encoding UTF8
    $cleaned++
}

Write-Host ("Cleaned {0} .lhlt files; {1} untouched (already clean or non-stock-lens shape)." -f $cleaned, $untouched) -ForegroundColor Green
