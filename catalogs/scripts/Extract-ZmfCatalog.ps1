# Extract-ZmfCatalog.ps1
#
# Reads a binary Zemax .ZMF stock-lens catalog (e.g. THORLABS_MAY_2026.ZMF) and
# writes each embedded lens out as its own .zmx file. The ZMF container has:
#   - 4-byte file header: uint32 format version (only 1001 supported)
#   - Repeating per-lens records:
#       * 100-byte ASCII name (latin1, null-padded)
#       * 7 uint32: per-lens version, element count, shape index, aspheric flag,
#                   GRIN flag, toroidal flag, description-data length
#       * 2 doubles: EFL, ENP
#       * descLen bytes: XOR-obfuscated .zmx text
# The deobfuscation key is a per-byte XOR using a sin/cos schedule of EFL+ENP
# (Zemax's obfuscation scheme; algorithm reproduced from public domain spec).

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ZmfPath,
    [Parameter(Mandatory)] [string] $OutDir
)

$ErrorActionPreference = 'Stop'

function Get-ZmfXorKey {
    param([double] $Efl, [double] $Enp, [int] $Length)
    $iv = [Math]::Cos(6.0 * $Efl + 3.0 * $Enp)
    $iv = [Math]::Cos(655.0 * ([Math]::PI / 180.0) * $iv) + $iv
    $key = New-Object byte[] $Length
    $inv = [Globalization.CultureInfo]::InvariantCulture
    for ($p = 0; $p -lt $Length; $p++) {
        $kf = 13.2 * ($iv + [Math]::Sin(17.0 * ($p + 3))) * ($p + 1)
        $s  = $kf.ToString("0.00000000e+00", $inv)
        # Mirrors Python int(f"{kf:.8e}"[4:7]) — sign and decimal positions match.
        $digits = $s.Substring(4, 3)
        $n      = [int]$digits
        $key[$p] = [byte]($n -band 0xFF)
    }
    return $key
}

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$bytes = [System.IO.File]::ReadAllBytes($ZmfPath)
$offset = 0
$version = [BitConverter]::ToUInt32($bytes, $offset); $offset += 4
if ($version -ne 1001) {
    throw "Unsupported ZMF version $version (only 1001 is supported)"
}
Write-Host ("ZMF version {0} — extracting from {1}" -f $version, (Split-Path $ZmfPath -Leaf)) -ForegroundColor Cyan

$LENS_FIXED = 100 + 7*4 + 2*8  # 144 bytes
$count = 0
$failed = 0
while ($offset + $LENS_FIXED -le $bytes.Length) {
    # Lens name (100 bytes, null-padded latin1)
    $nameBytes = New-Object byte[] 100
    [Array]::Copy($bytes, $offset, $nameBytes, 0, 100); $offset += 100
    $nullIdx = [Array]::IndexOf($nameBytes, [byte]0)
    if ($nullIdx -lt 0) { $nullIdx = 100 }
    $name = [Text.Encoding]::GetEncoding(28591).GetString($nameBytes, 0, $nullIdx).Trim()

    $lensVersion = [BitConverter]::ToUInt32($bytes, $offset); $offset += 4
    $nElements   = [BitConverter]::ToUInt32($bytes, $offset); $offset += 4
    $shapeIdx    = [BitConverter]::ToUInt32($bytes, $offset); $offset += 4
    $aspheric    = [BitConverter]::ToUInt32($bytes, $offset); $offset += 4
    $grin        = [BitConverter]::ToUInt32($bytes, $offset); $offset += 4
    $toroidal    = [BitConverter]::ToUInt32($bytes, $offset); $offset += 4
    $descLen     = [BitConverter]::ToUInt32($bytes, $offset); $offset += 4
    $efl         = [BitConverter]::ToDouble($bytes, $offset); $offset += 8
    $enp         = [BitConverter]::ToDouble($bytes, $offset); $offset += 8

    if ($offset + $descLen -gt $bytes.Length) {
        Write-Host ("  truncated at lens '{0}' — expected {1} bytes, {2} remaining" -f $name, $descLen, ($bytes.Length - $offset)) -ForegroundColor Yellow
        break
    }

    # Pull encrypted blob then XOR-decode in-place
    $enc = New-Object byte[] $descLen
    [Array]::Copy($bytes, $offset, $enc, 0, $descLen); $offset += $descLen
    $key = Get-ZmfXorKey -Efl $efl -Enp $enp -Length $descLen
    for ($i = 0; $i -lt $descLen; $i++) { $enc[$i] = $enc[$i] -bxor $key[$i] }
    $zmxText = [Text.Encoding]::GetEncoding(28591).GetString($enc)

    # Sanity check: decoded text must start with "VERS NNNNNN\n"
    $expectedHeader = ("VERS {0:D6}" -f $lensVersion)
    if (-not $zmxText.StartsWith($expectedHeader)) {
        $previewLen = [Math]::Min(32, $zmxText.Length)
        Write-Host ("  XOR decode FAILED for '{0}' (efl={1:F3}, enp={2:F3}) — got {3}..." -f $name, $efl, $enp, $zmxText.Substring(0, $previewLen)) -ForegroundColor Red
        $failed++
        continue
    }

    # Sanitize filename — Thorlabs part numbers can contain '/', '\\', '*' etc.
    $safeName = ($name -replace '[\\/:*?"<>|]', '_').Trim()
    if ([string]::IsNullOrWhiteSpace($safeName)) { $safeName = "unnamed_{0:D4}" -f $count }
    $outPath = Join-Path $OutDir ($safeName + '.zmx')
    [System.IO.File]::WriteAllBytes($outPath, [Text.Encoding]::GetEncoding(28591).GetBytes($zmxText))
    $count++
    if ($count % 100 -eq 0) {
        Write-Host ("  {0} lenses extracted ..." -f $count) -ForegroundColor DarkGray
    }
}

Write-Host ("Done. {0} lenses written to {1}" -f $count, $OutDir) -ForegroundColor Green
if ($failed -gt 0) {
    Write-Host ("{0} lens(es) failed XOR decode — see warnings above" -f $failed) -ForegroundColor Yellow
}
