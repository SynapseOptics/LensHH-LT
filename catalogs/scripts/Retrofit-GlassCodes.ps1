# Retrofit-GlassCodes.ps1
#
# One-off backfill: populates glass_codes_json + adds scalar nd_primary,
# vd_primary, n_elements columns to stock_lenses for AI-friendly queries.
#
# Reads (nd, Vd) for every glass referenced by the catalog from the 9 AGF
# files under catalogs/Glass/, then walks every stock_lenses row and updates
# the four glass-derived fields. Idempotent (re-runs overwrite cleanly).

[CmdletBinding()]
param(
    [string] $GlassDir = "C:\GIT\SynapseLensHH-LT\LensHH-LT\catalogs\Glass",
    [string] $DbPath   = "C:\GIT\SynapseLensHH-LT\LensHH-LT\catalogs\stock-lens-catalog.sqlite",
    [string] $CsvDir   = "C:\GIT\SynapseLensHH-LT\LensHH-LT\catalogs\csv-export"
)

$ErrorActionPreference = 'Stop'
Import-Module PSSQLite

function Read-AgfText {
    param([string] $Path)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
        return [System.Text.Encoding]::Unicode.GetString($bytes)
    }
    return [System.Text.Encoding]::UTF8.GetString($bytes)
}

function Get-AgfGlassIndex {
    param([string] $Dir)
    $index = @{}  # case-insensitive lookup
    foreach ($file in Get-ChildItem -Path $Dir -Filter *.AGF -File) {
        $text = Read-AgfText $file.FullName
        foreach ($line in $text -split "`r?`n") {
            if ($line.StartsWith('NM ')) {
                $parts = $line -split '\s+' | Where-Object { $_ -ne '' }
                if ($parts.Length -ge 6) {
                    # NM <name> <formula> <code_w_decimal> <nd> <Vd> ...
                    $name = $parts[1]
                    $nd   = [double]::Parse($parts[4], [Globalization.CultureInfo]::InvariantCulture)
                    $vd   = [double]::Parse($parts[5], [Globalization.CultureInfo]::InvariantCulture)
                    $key  = $name.ToUpperInvariant()
                    if (-not $index.ContainsKey($key)) {
                        $index[$key] = @{ name = $name; nd = $nd; vd = $vd; catalog = $file.BaseName }
                    }
                }
            }
        }
    }
    return $index
}

function Get-SchottCode {
    param([double] $nd, [double] $vd)
    $a = [int][Math]::Round(($nd - 1.0) * 1000)
    $b = [int][Math]::Round($vd * 10)
    if ($a -lt 0 -or $a -gt 999 -or $b -lt 0 -or $b -gt 999) { return $null }
    return ('{0:000}{1:000}' -f $a, $b)
}

Write-Host "Loading AGF catalogs from $GlassDir ..." -ForegroundColor Cyan
$glassIdx = Get-AgfGlassIndex $GlassDir
Write-Host ("  {0} glass entries indexed" -f $glassIdx.Count)

# Add scalar columns if missing (SQLite has no ADD COLUMN IF NOT EXISTS).
$existing = Invoke-SqliteQuery -DataSource $DbPath -Query "PRAGMA table_info(stock_lenses);" |
    Select-Object -ExpandProperty name
foreach ($col in @('nd_primary', 'vd_primary', 'n_elements')) {
    if ($existing -notcontains $col) {
        $type = if ($col -eq 'n_elements') { 'INTEGER' } else { 'REAL' }
        Invoke-SqliteQuery -DataSource $DbPath -Query "ALTER TABLE stock_lenses ADD COLUMN $col $type;"
        Write-Host "  added column $col ($type)" -ForegroundColor Yellow
    }
}

$rows = Invoke-SqliteQuery -DataSource $DbPath -Query "SELECT vendor, part_number, glass_names_json FROM stock_lenses;"
Write-Host ("Walking {0} stock_lens rows ..." -f $rows.Count) -ForegroundColor Cyan

$updated = 0
$unresolved = New-Object System.Collections.Generic.HashSet[string]
$conn = New-SQLiteConnection -DataSource $DbPath
if ($conn.State -ne 'Open') { $conn.Open() }
$tx = $conn.BeginTransaction()
try {
    $cmd = $conn.CreateCommand()
    $cmd.Transaction = $tx
    $cmd.CommandText = @"
UPDATE stock_lenses
SET glass_codes_json = @codes,
    nd_primary       = @nd,
    vd_primary       = @vd,
    n_elements       = @n
WHERE vendor = @vendor AND part_number = @part;
"@
    foreach ($p in 'codes','nd','vd','n','vendor','part') {
        $null = $cmd.Parameters.Add((New-Object System.Data.SQLite.SQLiteParameter("@$p")))
    }

    foreach ($row in $rows) {
        $names = @()
        if ($row.glass_names_json) {
            try { $names = ConvertFrom-Json $row.glass_names_json } catch { $names = @() }
        }
        $codes = @()
        $ndPrim = $null
        $vdPrim = $null
        foreach ($n in $names) {
            $k = $n.ToString().ToUpperInvariant()
            if ($glassIdx.ContainsKey($k)) {
                $g = $glassIdx[$k]
                $codes += (Get-SchottCode -nd $g.nd -vd $g.vd)
                if ($null -eq $ndPrim) { $ndPrim = $g.nd; $vdPrim = $g.vd }
            } else {
                $codes += $null
                [void]$unresolved.Add($n)
            }
        }
        $codesJson = if ($codes.Count -gt 0) { ConvertTo-Json @($codes) -Compress } else { $null }

        $cmd.Parameters['@codes'].Value  = if ($null -ne $codesJson) { $codesJson } else { [DBNull]::Value }
        $cmd.Parameters['@nd'].Value     = if ($null -ne $ndPrim)    { $ndPrim }    else { [DBNull]::Value }
        $cmd.Parameters['@vd'].Value     = if ($null -ne $vdPrim)    { $vdPrim }    else { [DBNull]::Value }
        $cmd.Parameters['@n'].Value      = $names.Count
        $cmd.Parameters['@vendor'].Value = $row.vendor
        $cmd.Parameters['@part'].Value   = $row.part_number
        [void]$cmd.ExecuteNonQuery()
        $updated++
    }
    $tx.Commit()
}
catch {
    $tx.Rollback()
    throw
}
finally {
    $conn.Close()
}

Write-Host ("Updated {0} rows" -f $updated) -ForegroundColor Green
if ($unresolved.Count -gt 0) {
    Write-Host ("Unresolved glass names ({0}):" -f $unresolved.Count) -ForegroundColor Yellow
    $unresolved | Sort-Object | ForEach-Object { Write-Host "  $_" }
}

# Re-export CSVs to reflect the new columns.
$stockCsv = Join-Path $CsvDir 'stock_lenses.csv'
$surfCsv  = Join-Path $CsvDir 'lens_surfaces.csv'
Invoke-SqliteQuery -DataSource $DbPath -Query "SELECT * FROM stock_lenses ORDER BY vendor, part_number;" |
    Export-Csv -Path $stockCsv -NoTypeInformation -Encoding UTF8
Invoke-SqliteQuery -DataSource $DbPath -Query "SELECT * FROM lens_surfaces ORDER BY vendor, part_number, surface_index;" |
    Export-Csv -Path $surfCsv -NoTypeInformation -Encoding UTF8
Write-Host "CSVs refreshed:" -ForegroundColor Green
Write-Host "  $stockCsv"
Write-Host "  $surfCsv"
