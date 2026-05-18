# Refresh-ParaxialFromLhlt.ps1
#
# Walks every stock_lenses row that has a sibling .lhlt, loads the file
# through the LensHH-LT engine in-process, and rewrites the paraxial-
# derived columns from a fresh SystemDataCalculator.Calculate() pass:
#   efl_mm, bfl_mm, ffl_mm, fnum, na_image, enp_diameter_mm, total_track_mm,
#   center_thickness_mm, wavelength_nm_primary, wavelengths_nm_json
#
# Use cases:
#   - After a structural .lhlt cleanup (Collapse-TrailingDummies, the future
#     RayAiming/MEMA passes, etc.) where the on-disk JSON changed but the
#     SQLite engine-derived fields are stale.
#   - After a glass-catalog refresh that changes how a previously-unresolved
#     glass now resolves (so paraxial actually has a non-zero answer).
#
# Idempotent. Re-runs are safe.

[CmdletBinding()]
param(
    [string] $DbPath     = "C:\GIT\SynapseLensHH-LT\LensHH-LT\catalogs\stock-lens-catalog.sqlite",
    [string] $LensesRoot = "C:\GIT\SynapseLensHH-LT\LensHH-LT\catalogs\Lenses",
    [string] $McpDir     = "C:\GIT\TEST_INSTALL\LensHH-LT\mcp",
    [string] $GlassDir   = "C:\GIT\SynapseLensHH-LT\LensHH-LT\catalogs\Glass",
    [string] $CsvDir     = "C:\GIT\SynapseLensHH-LT\LensHH-LT\catalogs\csv-export"
)

$ErrorActionPreference = 'Stop'
Import-Module PSSQLite

# ── Load LensHH assemblies via reflection ─────────────────────────────────
Write-Host "Loading LensHH assemblies from $McpDir ..." -ForegroundColor Cyan
Add-Type -Path (Join-Path $McpDir 'LensHH.Core.dll')
Add-Type -Path (Join-Path $McpDir 'LensHH.IO.dll')
foreach ($d in @('Microsoft.Extensions.AI.Abstractions.dll',
                 'ModelContextProtocol.Core.dll',
                 'ModelContextProtocol.dll')) {
    $p = Join-Path $McpDir $d
    if (Test-Path $p) { try { Add-Type -Path $p } catch {} }
}
Add-Type -Path (Join-Path $McpDir 'LensHH.Mcp.dll')

# Activation MUST come before any system load — without it, paraxial silently
# returns 0 (the documented trap).
$activated = [LensHH.Core.Activation.ActivationManager]::TryLoadExistingActivation()
if (-not $activated) { throw "ActivationManager.TryLoadExistingActivation() returned false." }
Write-Host "Activation OK." -ForegroundColor Green

# Construct McpSession + load glass catalogs from project tree.
$session = New-Object LensHH.Mcp.McpSession
$session.GlassCatalog.LoadCatalogsFromFolder($GlassDir)
Write-Host ("Glass catalogs loaded: {0}" -f $session.GlassCatalog.LoadedCatalogs.Count) -ForegroundColor Green

# ── Walk SQLite rows, refresh each ────────────────────────────────────────
$rows = Invoke-SqliteQuery -DataSource $DbPath -Query "SELECT vendor, part_number, lhlt_relpath FROM stock_lenses WHERE lhlt_relpath IS NOT NULL;"
Write-Host ("Walking {0} stock_lens rows ..." -f $rows.Count) -ForegroundColor Cyan

$conn = New-SQLiteConnection -DataSource $DbPath
if ($conn.State -ne 'Open') { $conn.Open() }
$tx = $conn.BeginTransaction()
$cmd = $conn.CreateCommand(); $cmd.Transaction = $tx
$cmd.CommandText = @"
UPDATE stock_lenses
SET efl_mm                 = @efl,
    bfl_mm                 = @bfl,
    ffl_mm                 = @ffl,
    fnum                   = @fnum,
    na_image               = @na,
    enp_diameter_mm        = @epd,
    total_track_mm         = @ttrack,
    center_thickness_mm    = @ct,
    wavelength_nm_primary  = @wp,
    wavelengths_nm_json    = @wj
WHERE vendor = @v AND part_number = @p;
"@
foreach ($n in 'efl','bfl','ffl','fnum','na','epd','ttrack','ct','wp','wj','v','p') {
    $null = $cmd.Parameters.Add((New-Object System.Data.SQLite.SQLiteParameter("@$n")))
}

$ok = 0; $skipped = 0; $failed = 0
$start = Get-Date
try {
    foreach ($row in $rows) {
        $lhltPath = Join-Path $LensesRoot ($row.lhlt_relpath -replace '/','\')
        if (-not (Test-Path $lhltPath)) { $skipped++; continue }
        try {
            $session.LoadFromFile($lhltPath)
            $sys = $session.System
            $r   = [LensHH.Core.Analysis.SystemDataCalculator]::Calculate($sys, $session.GlassCatalog)

            # Center thickness = sum of DISZ over surfaces that bear a glass material.
            $ct = 0.0
            foreach ($s in $sys.Surfaces) {
                if (-not [string]::IsNullOrEmpty($s.Material) -and $s.Material -ne 'MIRROR') {
                    $ct += $s.Thickness
                }
            }
            # Wavelengths
            $waveJson = $null; $wPrimary = $null
            if ($sys.Wavelengths.Count -gt 0) {
                $list = New-Object Collections.Generic.List[double]
                foreach ($w in $sys.Wavelengths) { $list.Add([double]$w.Value * 1000.0) }
                $waveJson = ConvertTo-Json @($list) -Compress
                $idx = $sys.PrimaryWavelengthIndex
                if ($idx -ge 0 -and $idx -lt $sys.Wavelengths.Count) {
                    $wPrimary = [double]$sys.Wavelengths[$idx].Value * 1000.0
                }
            }

            $cmd.Parameters['@efl'].Value    = if ([double]::IsNaN($r.Efl))                  { [DBNull]::Value } else { [double]$r.Efl }
            $cmd.Parameters['@bfl'].Value    = if ([double]::IsNaN($r.Bfl))                  { [DBNull]::Value } else { [double]$r.Bfl }
            $cmd.Parameters['@ffl'].Value    = if ([double]::IsNaN($r.Ffl))                  { [DBNull]::Value } else { [double]$r.Ffl }
            # fnum: match the original Build-StockLensCatalog convention, which parses
            # "Image Space F/#" from get_paraxial_data text (= EFL/EPD for infinite
            # conjugate). WorkingFNumber differs and would be a regression on stable rows.
            $cmd.Parameters['@fnum'].Value   = if ([double]::IsNaN($r.ImageSpaceFNumber))    { [DBNull]::Value } else { [double]$r.ImageSpaceFNumber }
            $cmd.Parameters['@na'].Value     = if ([double]::IsNaN($r.ImageSpaceNA))         { [DBNull]::Value } else { [double]$r.ImageSpaceNA }
            $cmd.Parameters['@epd'].Value    = if ([double]::IsNaN($r.EntrancePupilDiameter)){ [DBNull]::Value } else { [double]$r.EntrancePupilDiameter }
            $cmd.Parameters['@ttrack'].Value = if ([double]::IsNaN($r.TotalTrack))           { [DBNull]::Value } else { [double]$r.TotalTrack }
            $cmd.Parameters['@ct'].Value     = if ($ct -gt 0)                                { $ct } else { [DBNull]::Value }
            $cmd.Parameters['@wp'].Value     = if ($null -ne $wPrimary)                      { $wPrimary } else { [DBNull]::Value }
            $cmd.Parameters['@wj'].Value     = if ($null -ne $waveJson)                      { $waveJson } else { [DBNull]::Value }
            $cmd.Parameters['@v'].Value      = $row.vendor
            $cmd.Parameters['@p'].Value      = $row.part_number
            [void]$cmd.ExecuteNonQuery()
            $ok++
        }
        catch { $failed++ }
        if (($ok + $failed + $skipped) % 1000 -eq 0) {
            Write-Host ("  {0} / {1} processed ..." -f ($ok + $failed + $skipped), $rows.Count) -ForegroundColor DarkGray
        }
    }
    $tx.Commit()
}
catch { $tx.Rollback(); throw }
finally { $conn.Close() }

$elapsed = (Get-Date) - $start
Write-Host ("Refreshed {0} rows; skipped {1}; failed {2}. Elapsed {3:N1} s." -f $ok, $skipped, $failed, $elapsed.TotalSeconds) -ForegroundColor Green

# Re-export CSVs.
Invoke-SqliteQuery -DataSource $DbPath -Query "SELECT * FROM stock_lenses ORDER BY vendor, part_number;" |
    Export-Csv -Path (Join-Path $CsvDir 'stock_lenses.csv') -NoTypeInformation -Encoding UTF8
Invoke-SqliteQuery -DataSource $DbPath -Query "SELECT * FROM lens_surfaces ORDER BY vendor, part_number, surface_index;" |
    Export-Csv -Path (Join-Path $CsvDir 'lens_surfaces.csv') -NoTypeInformation -Encoding UTF8
Write-Host "CSVs refreshed." -ForegroundColor Green
