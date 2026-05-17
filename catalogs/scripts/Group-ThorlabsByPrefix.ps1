# Group-ThorlabsByPrefix.ps1
#
# Reorganizes the flat ThorLabs/zmx/ directory of .zmx files into
# ThorLabs/<prefix>/<part>.zmx, where <prefix> is the alphabetic prefix
# of the part number (run of letters before the first digit).
# Digit-leading part numbers (e.g. 110V-E, 354060-A) go to ThorLabs/Numeric/.
#
# Thorlabs uses these prefixes consistently:
#   LA  PlanoConvex          LB  BiConvex          LBF BestForm
#   LC  PlanoConcave         LD  BiConcave         LE  PositiveMeniscus
#   LF  NegativeMeniscus     LJ  CylPlanoConvex    LK  CylPlanoConcave
#   AC  Achromat             ACL AchromatLarge     ACT AchromatACT
#   AL  Aspheric             AX  Axicon            MAP MatchedAchromaticPair
#   GRIN GRIN                TRS Triplet           ... etc.
# We preserve the literal prefix as folder name so part numbers map cleanly.

[CmdletBinding()]
param(
    [string] $SourceDir = "C:\GIT\SynapseLensHH-LT\LensHH-LT\catalogs\Lenses\ThorLabs\zmx",
    [string] $TargetDir = "C:\GIT\SynapseLensHH-LT\LensHH-LT\catalogs\Lenses\ThorLabs"
)

$ErrorActionPreference = 'Stop'

$files = Get-ChildItem -Path $SourceDir -Filter *.zmx -File
$movedByPrefix = @{}
foreach ($f in $files) {
    $stem = [System.IO.Path]::GetFileNameWithoutExtension($f.Name)
    # Strip everything from the first digit onward to get the alphabetic prefix.
    if ($stem -match '^([A-Za-z]+)') {
        $prefix = $Matches[1]
    } else {
        $prefix = 'Numeric'
    }
    $dst = Join-Path $TargetDir $prefix
    if (-not (Test-Path $dst)) { New-Item -ItemType Directory -Path $dst -Force | Out-Null }
    Move-Item -Path $f.FullName -Destination (Join-Path $dst $f.Name) -Force
    if (-not $movedByPrefix.ContainsKey($prefix)) { $movedByPrefix[$prefix] = 0 }
    $movedByPrefix[$prefix]++
}

# Remove the now-empty source dir.
if ((Get-ChildItem -Path $SourceDir -Force | Measure-Object).Count -eq 0) {
    Remove-Item -Path $SourceDir -Force
}

Write-Host ("Reorganized {0} files into {1} family folders:" -f ($files.Count), $movedByPrefix.Count) -ForegroundColor Green
$movedByPrefix.GetEnumerator() | Sort-Object -Property Value -Descending | ForEach-Object {
    Write-Host ("  {0,-8} {1,5}" -f $_.Key, $_.Value)
}
