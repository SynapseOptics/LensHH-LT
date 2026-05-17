# Sync-EpdFromLhlt.ps1
#
# Walks every stock_lenses row that has a sibling .lhlt file, reads the
# engine-canonical aperture value from the .lhlt JSON, and updates SQLite's
# enp_diameter_mm column so the DB matches the .lhlt. Necessary because
# Build-StockLensCatalog.ps1's Get-ZmxParsedData reads the *raw* .zmx ENPD
# field, which is pre-CLAP-override and pre-FLOA-resolution; the .lhlt is
# what users actually see in LensHH-LT.
#
# Idempotent: re-runs are safe.

[CmdletBinding()]
param(
    [string] $DbPath     = "C:\GIT\SynapseLensHH-LT\LensHH-LT\catalogs\stock-lens-catalog.sqlite",
    [string] $LensesRoot = "C:\GIT\SynapseLensHH-LT\LensHH-LT\catalogs\Lenses",
    [string] $CsvDir     = "C:\GIT\SynapseLensHH-LT\LensHH-LT\catalogs\csv-export"
)

$ErrorActionPreference = 'Stop'
Import-Module PSSQLite

$rows = Invoke-SqliteQuery -DataSource $DbPath -Query "SELECT vendor, part_number, lhlt_relpath FROM stock_lenses WHERE lhlt_relpath IS NOT NULL;"
Write-Host ("Walking {0} stock_lens rows with .lhlt sibling ..." -f $rows.Count) -ForegroundColor Cyan

$conn = New-SQLiteConnection -DataSource $DbPath
if ($conn.State -ne 'Open') { $conn.Open() }
$tx = $conn.BeginTransaction()
$cmd = $conn.CreateCommand(); $cmd.Transaction = $tx
$cmd.CommandText = "UPDATE stock_lenses SET enp_diameter_mm = @e WHERE vendor = @v AND part_number = @p;"
foreach ($p in 'e','v','p') { $null = $cmd.Parameters.Add((New-Object System.Data.SQLite.SQLiteParameter("@$p"))) }

$updated = 0
$skippedMissingFile = 0
$skippedParse = 0
foreach ($r in $rows) {
    $lhltPath = Join-Path $LensesRoot ($r.lhlt_relpath -replace '/', '\')
    if (-not (Test-Path $lhltPath)) { $skippedMissingFile++; continue }
    try {
        $json = Get-Content -Path $lhltPath -Raw | ConvertFrom-Json
        $ap = $json.Aperture
        if ($null -eq $ap) { $skippedParse++; continue }
        # We only sync EPD-type aperture values. FNumber-type lenses don't
        # have a pupil diameter without knowing the EFL; leave their SQLite
        # enp_diameter_mm alone in that case.
        if ($ap.Type -ne 'EPD') { continue }
        if ($null -eq $ap.Value) { $skippedParse++; continue }
        $cmd.Parameters['@e'].Value = [double]$ap.Value
        $cmd.Parameters['@v'].Value = $r.vendor
        $cmd.Parameters['@p'].Value = $r.part_number
        [void]$cmd.ExecuteNonQuery()
        $updated++
    }
    catch {
        $skippedParse++
    }
}
$tx.Commit(); $conn.Close()

Write-Host ("Updated enp_diameter_mm on {0} rows" -f $updated) -ForegroundColor Green
if ($skippedMissingFile -gt 0) { Write-Host ("Skipped (file missing):    {0}" -f $skippedMissingFile) -ForegroundColor Yellow }
if ($skippedParse -gt 0)        { Write-Host ("Skipped (parse/no value):  {0}" -f $skippedParse) -ForegroundColor Yellow }

# Re-export CSVs.
Invoke-SqliteQuery -DataSource $DbPath -Query "SELECT * FROM stock_lenses ORDER BY vendor, part_number;" |
    Export-Csv -Path (Join-Path $CsvDir 'stock_lenses.csv') -NoTypeInformation -Encoding UTF8
Invoke-SqliteQuery -DataSource $DbPath -Query "SELECT * FROM lens_surfaces ORDER BY vendor, part_number, surface_index;" |
    Export-Csv -Path (Join-Path $CsvDir 'lens_surfaces.csv') -NoTypeInformation -Encoding UTF8
Write-Host "CSVs refreshed" -ForegroundColor Green
