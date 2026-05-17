# Patch-ZmxModelGlass.ps1
#
# Pre-processes Zemax .zmx files so LensHH-LT can import them cleanly:
#   1. Rewrites model-glass GLAS lines ("GLAS ___BLANK <code> <flag> <nd> <Vd> ...")
#      to reference a named catalog entry, since LensHH-LT has no model-glass
#      support and would otherwise silently leave the surface as n=1.
#   2. Substitutes vendor-named glasses whose names don't match any AGF entry
#      (e.g. Edmund's "ZEONEX_K22R&K26R_2017") for a near-equivalent catalog
#      glass we DO have.
#
# Handles both .zmx file encodings produced by OpticStudio:
#   - UTF-16 LE with BOM and CRLF line endings (newer vendor files)
#   - ASCII / UTF-8 with CRLF or LF line endings (older Edmund files)
# Detected by BOM probe; the output preserves the source encoding.
#
# Substitution tables -- add entries here as new tuples / names appear in
# vendor files. The named catalog glass must already exist in a loaded AGF.

$ModelGlassMap = @{
    # ___BLANK (nd, Vd) tuples seen in Edmund vendor .zmx files.
    # Key format: '<nd>_<Vd>' rounded to 3 decimals / 1 decimal.
    '1.517_52.0' = 'EDMUND_REPLICA_POLYMER_152V52'   # 9 hybrid-doublet replica polymer surfaces (49-656..49-665)
}

$NamedGlassMap = @{
    # vendor-named glasses whose exact name doesn't match any AGF NM entry,
    # but where the material can be safely aliased to a known catalog glass.
    # Add the actual glass to MISC.AGF first, then add the substitution here.
    'ZEONEX_K22R&K26R_2017' = 'ZEONEX_K26R'   # Zeon brochure: K22R and K26R both nd=1.535 (26 surface uses)
}

function Get-ZmxEncoding {
    # Inspects raw bytes to determine encoding. Returns a pscustomobject with
    # .Encoding (.NET Encoding), .HasBom (bool), .Decode (closure), .EncodeWithBom (closure).
    param([byte[]] $Bytes)
    if ($Bytes.Length -ge 2 -and $Bytes[0] -eq 0xFF -and $Bytes[1] -eq 0xFE) {
        return [pscustomobject]@{
            Kind = 'utf16le-bom'
            Decode = { param($b) [System.Text.Encoding]::Unicode.GetString($b, 2, $b.Length - 2) }.GetNewClosure()
            EncodeWithBom = {
                param($text)
                $payload = [System.Text.Encoding]::Unicode.GetBytes($text)
                $out = New-Object byte[] ($payload.Length + 2)
                $out[0] = 0xFF; $out[1] = 0xFE
                [System.Array]::Copy($payload, 0, $out, 2, $payload.Length)
                return $out
            }.GetNewClosure()
        }
    } else {
        # ASCII / UTF-8 without BOM
        return [pscustomobject]@{
            Kind = 'ascii'
            Decode = { param($b) [System.Text.Encoding]::UTF8.GetString($b) }.GetNewClosure()
            EncodeWithBom = { param($text) [System.Text.Encoding]::UTF8.GetBytes($text) }.GetNewClosure()
        }
    }
}

function Convert-ZmxModelGlass {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $SourcePath,
        [Parameter(Mandatory)] [string] $DestinationPath
    )

    $bytes = [System.IO.File]::ReadAllBytes($SourcePath)
    $enc   = Get-ZmxEncoding -Bytes $bytes
    $text  = & $enc.Decode $bytes

    # ___BLANK model-glass pattern (multi-line, anchored to line start).
    # NOTE: use [^\r\n]* (not .*$) so the trailing \r before \n is preserved.
    $blankPattern = '(?m)^(?<lead>\s*)GLAS\s+___BLANK\s+\S+\s+\S+\s+(?<nd>[\d.eE+-]+)\s+(?<vd>[\d.eE+-]+)[^\r\n]*'

    # Named-glass pattern: matches "GLAS <name>" where <name> is in $NamedGlassMap.
    # Built dynamically from the map keys; needs each key regex-escaped because
    # vendor names may contain special chars (e.g. "&" in ZEONEX_K22R&K26R_2017).
    $namedAlternation = ($NamedGlassMap.Keys | ForEach-Object { [regex]::Escape($_) }) -join '|'
    $namedPattern = if ($namedAlternation) {
        "(?m)^(?<lead>\s*)GLAS\s+(?<name>$namedAlternation)\b[^\r\n]*"
    } else { $null }

    $state = @{
        Subs = 0; SubsNamed = 0
        Unresolved = New-Object System.Collections.ArrayList
        Map = $ModelGlassMap; NamedMap = $NamedGlassMap
    }

    # Pass 1: ___BLANK substitutions
    $text = [regex]::Replace($text, $blankPattern, {
        param($m)
        $nd = [double]$m.Groups['nd'].Value
        $vd = [double]$m.Groups['vd'].Value
        $key = '{0:F3}_{1:F1}' -f $nd, $vd
        if ($state.Map.ContainsKey($key)) {
            $state.Subs++
            return "$($m.Groups['lead'].Value)GLAS $($state.Map[$key])"
        }
        [void]$state.Unresolved.Add(@{kind='blank'; nd=$nd; vd=$vd; line=$m.Value.Trim()})
        return $m.Value
    }.GetNewClosure())

    # Pass 2: named-glass substitutions
    if ($namedPattern) {
        $text = [regex]::Replace($text, $namedPattern, {
            param($m)
            $name = $m.Groups['name'].Value
            if ($state.NamedMap.ContainsKey($name)) {
                $state.SubsNamed++
                return "$($m.Groups['lead'].Value)GLAS $($state.NamedMap[$name])"
            }
            return $m.Value
        }.GetNewClosure())
    }

    [System.IO.File]::WriteAllBytes($DestinationPath, (& $enc.EncodeWithBom $text))

    return [pscustomobject]@{
        Encoding      = $enc.Kind
        Substitutions = $state.Subs
        NamedSubs     = $state.SubsNamed
        Unresolved    = $state.Unresolved
    }
}
