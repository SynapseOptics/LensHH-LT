# Repair-StockLensDescriptionDiameter.ps1
#
# Re-derives stock_lenses.description and stock_lenses.diameter_mm from each row's .zmx with
# Build-StockLensCatalog.ps1's own parser, so the database matches what a rebuild would give:
#
#   description - the last NOTE line that describes the lens, else its NAME. It was the last NOTE
#                 line, whatever it held: "ABS.COM" (the tail of Thorlabs' disclaimer) on 4,187
#                 lenses, nothing on 1,430 Ross Optical lenses.
#   diameter_mm - the part's outer diameter, from the semi-diameters on its faces. It was ENPD,
#                 the clear aperture or a laser beam: 22.86 for a 1" lens, 2 for a 25.4 mm axicon.
#
# Then repairs the lost diameter, micro and degree signs (U+FFFD) in each .lhlt Title, as
# Repair-StockLensText already does for the database's names, and regenerates the CSV exports.
#
# -WhatIf reports what would change and writes nothing. Idempotent: re-runs are safe.

[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $DbPath     = "C:\GIT\SynapseLensHH-LT\LensHH-LT\catalogs\stock-lens-catalog.sqlite",
    [string] $LensesRoot = "C:\GIT\SynapseLensHH-LT\LensHH-LT\catalogs\Lenses",
    [string] $CsvDir     = "C:\GIT\SynapseLensHH-LT\LensHH-LT\catalogs\csv-export"
)

$ErrorActionPreference = 'Stop'
Import-Module PSSQLite
. (Join-Path $PSScriptRoot 'Build-StockLensCatalog.ps1')
$script:LensesRoot = ($LensesRoot -replace '\\', '/')

$rows = Invoke-SqliteQuery -DataSource $DbPath -Query "SELECT vendor, part_number, description, diameter_mm, zmx_relpath FROM stock_lenses;"
Write-Host ("Checking {0} stock_lens rows ..." -f $rows.Count) -ForegroundColor Cyan

# 32 Edmund rows were imported with an absolute zmx_relpath; store them relative, as the rest are.
$prefix = ($LensesRoot -replace '\\', '/').TrimEnd('/') + '/'
$absolute = @($rows | Where-Object { $_.zmx_relpath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) })
Write-Host ("Absolute zmx_relpath to make relative: {0}" -f $absolute.Count)
foreach ($r in $absolute) {
    $rel = $r.zmx_relpath.Substring($prefix.Length)
    if ($PSCmdlet.ShouldProcess("$($r.vendor) $($r.part_number)", "zmx_relpath -> $rel")) {
        Invoke-SqliteQuery -DataSource $DbPath -Query "UPDATE stock_lenses SET zmx_relpath = @z WHERE vendor = @v AND part_number = @p;" `
            -SqlParameters @{ z = $rel; v = $r.vendor; p = $r.part_number } | Out-Null
    }
    $r.zmx_relpath = $rel
}

$changes = New-Object System.Collections.ArrayList
$missing = 0
foreach ($r in $rows) {
    $zmx = Join-Path $LensesRoot ($r.zmx_relpath -replace '/', '\')
    if (-not (Test-Path -LiteralPath $zmx)) { $missing++; continue }
    $d = Get-ZmxParsedData -ZmxPath $zmx
    $oldDesc = if ($r.description -is [DBNull]) { $null } else { $r.description }
    $oldDia  = if ($r.diameter_mm -is [DBNull]) { $null } else { [double]$r.diameter_mm }
    $newDia  = if ($null -eq $d.diameter_mm) { $null } else { [Math]::Round([double]$d.diameter_mm, 6) }
    $descChanged = $oldDesc -cne $d.description
    $diaChanged  = -not (($null -eq $oldDia -and $null -eq $newDia) -or ($null -ne $oldDia -and $null -ne $newDia -and [Math]::Abs($oldDia - $newDia) -lt 1e-9))
    if ($descChanged -or $diaChanged) {
        [void]$changes.Add([pscustomobject]@{
            vendor = $r.vendor; part = $r.part_number
            oldDesc = $oldDesc; newDesc = $d.description; descChanged = $descChanged
            oldDia = $oldDia; newDia = $newDia; diaChanged = $diaChanged
        })
    }
}
$nDesc = @($changes | Where-Object descChanged).Count
$nDia  = @($changes | Where-Object diaChanged).Count
Write-Host ("Descriptions to change: {0}; diameters to change: {1}; .zmx missing: {2}" -f $nDesc, $nDia, $missing)

# .lhlt titles with a lost sign
$titleFixes = New-Object System.Collections.ArrayList
$titleRx = [regex]'"Title":\s*("(?:[^"\\]|\\.)*")'
foreach ($f in Get-ChildItem $LensesRoot -Recurse -Filter *.lhlt) {
    $text = [System.IO.File]::ReadAllText($f.FullName)
    $m = $titleRx.Match($text)
    if (-not $m.Success) { continue }
    $title = [System.Text.Json.JsonSerializer]::Deserialize[string]($m.Groups[1].Value)
    if ($title -notmatch [char]0xFFFD) { continue }
    $fixed = Repair-StockLensText $title
    if ($fixed -ceq $title) { continue }
    [void]$titleFixes.Add([pscustomobject]@{ path = $f.FullName; text = $text; m = $m; old = $title; new = $fixed })
}
Write-Host (".lhlt titles to repair: {0}" -f $titleFixes.Count)

if (-not $PSCmdlet.ShouldProcess($DbPath, "update $nDesc descriptions, $nDia diameters, and $($titleFixes.Count) .lhlt titles")) {
    return [pscustomobject]@{ Changes = $changes; Titles = $titleFixes }
}

$conn = New-SQLiteConnection -DataSource $DbPath
if ($conn.State -ne 'Open') { $conn.Open() }
$tx = $conn.BeginTransaction()
$cmd = $conn.CreateCommand(); $cmd.Transaction = $tx
$cmd.CommandText = "UPDATE stock_lenses SET description = @d, diameter_mm = @m WHERE vendor = @v AND part_number = @p;"
foreach ($p in 'd','m','v','p') { $null = $cmd.Parameters.Add((New-Object System.Data.SQLite.SQLiteParameter("@$p"))) }
foreach ($c in $changes) {
    $cmd.Parameters['@d'].Value = if ($null -eq $c.newDesc) { [DBNull]::Value } else { $c.newDesc }
    $cmd.Parameters['@m'].Value = if ($null -eq $c.newDia) { [DBNull]::Value } else { $c.newDia }
    $cmd.Parameters['@v'].Value = $c.vendor
    $cmd.Parameters['@p'].Value = $c.part
    [void]$cmd.ExecuteNonQuery()
}
$tx.Commit(); $conn.Close()

# The writer escapes every non-ASCII character (\u00D8), as the .lhlt files already do.
$enc = New-Object System.Text.UTF8Encoding($false)
foreach ($t in $titleFixes) {
    $json = [System.Text.Json.JsonSerializer]::Serialize([object]$t.new, [string], [System.Text.Json.JsonSerializerOptions]$null)
    $g = $t.m.Groups[1]
    $text = $t.text.Substring(0, $g.Index) + $json + $t.text.Substring($g.Index + $g.Length)
    [System.IO.File]::WriteAllText($t.path, $text, $enc)
}

Export-StockLensCsv -DatabasePath $DbPath -OutputDirectory $CsvDir
Write-Host ("Updated {0} rows and {1} .lhlt titles; CSV exports regenerated." -f $changes.Count, $titleFixes.Count) -ForegroundColor Green
