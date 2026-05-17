# Build-StockLensCatalog.ps1
#
# SQLite ingestion for the stock-lens catalog (step 3 of the pipeline; see
# project_stock_lens_catalog). Provides:
#
#   Initialize-StockLensDatabase  -- creates the schema if not present
#   Get-ZmxParsedData             -- pure parser; returns a hashtable
#   Add-StockLens                 -- inserts one row + N surface rows
#   Get-StockLensFamilyFromPath   -- derives "BiConvex" / "Achromat" / etc. from folder
#
# The engine round-trip (import_zemax / get_paraxial_data) is NOT done here --
# PowerShell cannot reach the LensHH-LT MCP. Paraxial fields are passed in as
# a hashtable; if absent, the corresponding columns are NULL.

$LensesRoot = 'C:/GIT/SynapseLensHH-LT/LensHH-LT/catalogs/Lenses'

function ConvertFrom-ParaxialText {
    # Parses the text output of mcp__lenshh-lt__get_paraxial_data into a hashtable
    # with the columns Add-StockLens expects.
    param([Parameter(Mandatory)] [string] $Text)
    $h = @{}
    $pairs = @{
        '^Effective Focal Length\s+([\d.eE+-]+)' = 'efl_mm'
        '^Back Focal Length\s+([\d.eE+-]+)'      = 'bfl_mm'
        '^Front Focal Length\s+([\d.eE+-]+)'     = 'ffl_mm'
        '^Image Space F/#\s+([\d.eE+-]+)'        = 'fnum'
        '^Image Space NA\s+([\d.eE+-]+)'         = 'na_image'
        '^Total Track\s+([\d.eE+-]+)'            = 'total_track_mm'
    }
    foreach ($line in ($Text -split "`r?`n")) {
        foreach ($pat in $pairs.Keys) {
            if ($line.Trim() -match $pat) { $h[$pairs[$pat]] = [double]$matches[1]; break }
        }
    }
    return $h
}

function ConvertFrom-GlassIndicesText {
    # Parses mcp__lenshh-lt__get_system_glass_indices text.
    # Handles three row shapes:
    #   air row:          "<surf> (air) <n>"                          -> catalog NULL
    #   single-cat row:   "<surf> <material> <catalog> <n>"
    #   multi-word cat:   "<surf> <material> NOT FOUND <n>"
    # Also handles multi-wavelength rows: "<surf> <material> <cat> <n1> <n2> <n3>".
    # n_primary is the FIRST n value after material/catalog.
    param([Parameter(Mandatory)] [string] $Text)
    $rows = @()
    foreach ($line in ($Text -split "`r?`n")) {
        $t = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($t)) { continue }
        if (-not ($t -match '^\d')) { continue }
        $tokens = $t -split '\s+'
        # Index of first numeric token after the surface index
        $nIdx = $null
        for ($i = 1; $i -lt $tokens.Length; $i++) {
            if ($tokens[$i] -match '^-?[\d.]+(?:[eE][+-]?\d+)?$') { $nIdx = $i; break }
        }
        if ($null -eq $nIdx) { continue }
        $material = $tokens[1]
        $catalog  = if ($nIdx -ge 3) { ($tokens[2..($nIdx - 1)] -join ' ') } else { $null }
        $rows += [pscustomobject]@{
            surface   = [int]$tokens[0]
            material  = $material
            catalog   = $catalog
            n_primary = [double]$tokens[$nIdx]
        }
    }
    return ,$rows
}

function Test-StockLensValid {
    # Returns @{Ok=$true/$false; Notes=@(...)}. A lens is INVALID if any
    # non-air surface has catalog "NOT FOUND" (engine couldn't resolve the glass)
    # or n_primary close to 1.0 (silent fallback for model glass).
    param([Parameter(Mandatory)] $IndicesRows)
    $notes = @()
    foreach ($row in $IndicesRows) {
        if ($row.material -eq '(air)') { continue }
        if ($row.catalog -eq 'NOT FOUND') {
            $notes += "surf $($row.surface): glass '$($row.material)' NOT FOUND in any catalog"
        }
        elseif ($row.n_primary -lt 1.001) {
            $notes += "surf $($row.surface): glass '$($row.material)' has n=$($row.n_primary) (likely model-glass fallback)"
        }
    }
    return @{ Ok = ($notes.Count -eq 0); Notes = $notes }
}

function Initialize-StockLensDatabase {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $DatabasePath)

    $schema = @'
CREATE TABLE IF NOT EXISTS stock_lenses (
    vendor                TEXT NOT NULL,
    part_number           TEXT NOT NULL,
    family                TEXT,
    system_name           TEXT,
    description           TEXT,
    diameter_mm           REAL,
    center_thickness_mm   REAL,
    total_track_mm        REAL,
    efl_mm                REAL,
    bfl_mm                REAL,
    ffl_mm                REAL,
    fnum                  REAL,
    na_image              REAL,
    enp_diameter_mm       REAL,
    glass_codes_json      TEXT,
    glass_names_json      TEXT,
    coating               TEXT,
    wavelength_nm_primary REAL,
    wavelengths_nm_json   TEXT,
    zmx_relpath           TEXT NOT NULL,
    lhlt_relpath          TEXT,
    imported_at           TEXT NOT NULL,
    import_status         TEXT NOT NULL,
    import_notes_json     TEXT,
    PRIMARY KEY (vendor, part_number)
);

CREATE TABLE IF NOT EXISTS lens_surfaces (
    vendor              TEXT NOT NULL,
    part_number         TEXT NOT NULL,
    surface_index       INTEGER NOT NULL,
    radius_mm           REAL,
    thickness_mm        REAL,
    glass_name          TEXT,
    glass_catalog       TEXT,
    conic               REAL,
    aspheric_coeffs_json TEXT,
    clear_aperture_mm   REAL,
    surface_type        TEXT,
    is_stop             INTEGER,
    PRIMARY KEY (vendor, part_number, surface_index),
    FOREIGN KEY (vendor, part_number) REFERENCES stock_lenses(vendor, part_number)
);

CREATE INDEX IF NOT EXISTS idx_stock_lenses_family   ON stock_lenses(family);
CREATE INDEX IF NOT EXISTS idx_stock_lenses_efl      ON stock_lenses(efl_mm);
CREATE INDEX IF NOT EXISTS idx_stock_lenses_diameter ON stock_lenses(diameter_mm);
CREATE INDEX IF NOT EXISTS idx_stock_lenses_vendor   ON stock_lenses(vendor);
'@

    Invoke-SqliteQuery -DataSource $DatabasePath -Query $schema | Out-Null
    Write-Verbose "Initialized stock-lens schema at $DatabasePath"
}

function Get-StockLensFamilyFromPath {
    param([string] $ZmxPath)
    # ".../EdmundOptics/<Family>/.../file.zmx" -> "<Family>"
    # ".../EdmundOptics/<Category>/<Family>/.../file.zmx" -> "<Category>/<Family>"
    $norm = $ZmxPath -replace '\\','/'
    if ($norm -match '/Lenses/(?<vendor>[^/]+)/(?<rest>.+)$') {
        $rest = $matches['rest']
        $parts = $rest -split '/'
        # rest is "<family>/.../<file.zmx>"; drop the final file to get the family path
        if ($parts.Length -ge 2) {
            return ($parts[0..($parts.Length - 2)] -join '/')
        }
    }
    return $null
}

function Get-VendorFromPath {
    param([string] $ZmxPath)
    $norm = $ZmxPath -replace '\\','/'
    if ($norm -match '/Lenses/(?<vendor>[^/]+)/') { return $matches['vendor'] }
    return 'Unknown'
}

function Get-ZmxParsedData {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $ZmxPath)

    $bytes = [System.IO.File]::ReadAllBytes($ZmxPath)
    if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
        $text = [System.Text.Encoding]::Unicode.GetString($bytes, 2, $bytes.Length - 2)
    } else {
        $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    }
    $lines = $text -split "`r?`n"

    $data = @{
        zmx_relpath           = ($ZmxPath -replace '\\','/' -replace [regex]::Escape($LensesRoot + '/'),'')
        vendor                = Get-VendorFromPath $ZmxPath
        family                = Get-StockLensFamilyFromPath $ZmxPath
        part_number           = $null
        system_name           = $null
        description           = $null
        enp_diameter_mm       = $null
        wavelengths_nm        = New-Object System.Collections.ArrayList
        coating               = $null
        diameter_mm           = 0.0
        surfaces              = New-Object System.Collections.ArrayList
    }

    # Header pass
    foreach ($line in $lines) {
        $t = $line.Trim()
        if ($t -match '^NAME\s+(.+)$') { $data.system_name = $matches[1].Trim() }
        elseif ($t -match '^NOTE\s+\d+\s+(.+)$') { $data.description = $matches[1].Trim() }
        elseif ($t -match '^ENPD\s+([\d.eE+-]+)') { $data.enp_diameter_mm = [double]$matches[1] }
        elseif ($t -match '^WAVM\s+\d+\s+([\d.eE+-]+)') {
            # WAVM stores wavelength in micrometers
            [void]$data.wavelengths_nm.Add([double]$matches[1] * 1000.0)
        }
        elseif ($t -match '^DBDT\s+0\s+(\S+)') { $data.part_number = $matches[1] }
    }

    # Surface pass -- accumulate per-surface dicts
    $current = $null
    foreach ($line in $lines) {
        $t = $line.Trim()
        if ($t -match '^SURF\s+(\d+)') {
            if ($null -ne $current) { [void]$data.surfaces.Add($current) }
            $current = @{
                surface_index        = [int]$matches[1]
                radius_mm            = $null
                thickness_mm         = $null
                glass_name           = $null
                conic                = $null
                aspheric_coeffs      = New-Object System.Collections.ArrayList
                clear_aperture_mm    = $null
                surface_type         = 'STANDARD'
                is_stop              = 0
            }
            continue
        }
        if ($null -eq $current) { continue }

        if ($t -eq 'STOP' -or $t -match '^STOP\b') { $current.is_stop = 1 }
        elseif ($t -match '^TYPE\s+(\S+)') { $current.surface_type = $matches[1] }
        elseif ($t -match '^CURV\s+([\d.eE+-]+)') {
            $c = [double]$matches[1]
            if ($c -ne 0.0) { $current.radius_mm = 1.0 / $c }
            else { $current.radius_mm = $null }   # flat -> NULL
        }
        elseif ($t -match '^DISZ\s+(\S+)') {
            $v = $matches[1]
            if ($v -eq 'INFINITY') { $current.thickness_mm = $null }
            else { $current.thickness_mm = [double]$v }
        }
        elseif ($t -match '^GLAS\s+(\S+)') {
            $name = $matches[1]
            if ($name -ne '___BLANK') { $current.glass_name = $name }
        }
        elseif ($t -match '^DIAM\s+([\d.eE+-]+)') { $current.clear_aperture_mm = [double]$matches[1] }
        elseif ($t -match '^COAT\s+(\S+)') {
            # Per-surface coating; promote first non-empty to lens-level for the pilot
            if (-not $data.coating) { $data.coating = $matches[1] }
        }
        elseif ($t -match '^PARM\s+1\s+([\d.eE+-]+)') {
            # PARM 1 on EVENASPH/ODDASPH is the conic constant
            $current.conic = [double]$matches[1]
        }
        elseif ($t -match '^PARM\s+(\d+)\s+([\d.eE+-]+)') {
            $idx = [int]$matches[1]
            $val = [double]$matches[2]
            if ($idx -ge 2 -and $val -ne 0.0) {
                [void]$current.aspheric_coeffs.Add(@{order = $idx; value = $val})
            }
        }
    }
    if ($null -ne $current) { [void]$data.surfaces.Add($current) }

    # Mechanical diameter: ENPD when present (it matches the catalog OD for singlets
    # with a stop at the first surface, which is the common case). Falls back to max
    # CA-x2 (optical clear aperture) for systems with an internal stop or no ENPD.
    # For more complex lenses where ENPD != mechanical OD, post-process by parsing
    # the NOTE description ("Xmm Dia.").
    $maxClap = 0.0
    foreach ($s in $data.surfaces) {
        if ($null -ne $s.clear_aperture_mm -and $s.clear_aperture_mm -gt $maxClap) {
            $maxClap = $s.clear_aperture_mm
        }
    }
    if ($data.enp_diameter_mm -and $data.enp_diameter_mm -gt 0) {
        $data.diameter_mm = $data.enp_diameter_mm
    } elseif ($maxClap -gt 0) {
        $data.diameter_mm = $maxClap * 2.0
    } else {
        $data.diameter_mm = $null
    }

    # Center thickness = sum of DISZ over surfaces that have a GLAS name on them
    # (i.e., glass-bearing surfaces; the DISZ of a glass surface is the CT to the next
    # surface, which is the back side of the same element). Air gaps within doublets
    # also count as glass-region thickness in vendor practice; we use the simple
    # definition here.
    $ct = 0.0
    foreach ($s in $data.surfaces) {
        if ($s.glass_name -and $null -ne $s.thickness_mm) { $ct += $s.thickness_mm }
    }
    $data.center_thickness_mm = if ($ct -gt 0) { $ct } else { $null }

    return $data
}

function Export-StockLensCsv {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $DatabasePath,
        [Parameter(Mandatory)] [string] $OutputDirectory
    )
    if (-not (Test-Path $OutputDirectory)) {
        New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
    }
    $lenses = Invoke-SqliteQuery -DataSource $DatabasePath -Query 'SELECT * FROM stock_lenses ORDER BY vendor, part_number'
    $lenses | Export-Csv -Path (Join-Path $OutputDirectory 'stock_lenses.csv') -NoTypeInformation -Encoding UTF8
    $surfaces = Invoke-SqliteQuery -DataSource $DatabasePath -Query 'SELECT * FROM lens_surfaces ORDER BY vendor, part_number, surface_index'
    $surfaces | Export-Csv -Path (Join-Path $OutputDirectory 'lens_surfaces.csv') -NoTypeInformation -Encoding UTF8
}

function Add-StockLens {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]    $DatabasePath,
        [Parameter(Mandatory)] [hashtable] $ZmxData,
        [hashtable] $Paraxial,
        [hashtable] $GlassIndex,
        [string]    $LhltRelPath,
        [string[]]  $ImportNotes,
        [string]    $ImportStatus = 'ok'
    )

    # Glass codes (Schott-style 6-digit nd_Vd) -- need nd & Vd; if GlassIndex was
    # provided by the engine it gives us nd at each wavelength. For the pilot we
    # leave glass_codes_json NULL when GlassIndex is absent and just list glass names.
    $glassNames = @($ZmxData.surfaces | Where-Object { $_.glass_name } | ForEach-Object { $_.glass_name })
    $glassCodesJson = $null
    if ($GlassIndex -and $GlassIndex.Count -gt 0) {
        $codes = @()
        foreach ($name in $glassNames) {
            if ($GlassIndex.ContainsKey($name)) {
                $g = $GlassIndex[$name]
                if ($g.nd -and $g.vd) {
                    $nd6 = [int][Math]::Round(($g.nd - 1.0) * 1000)
                    $vd3 = [int][Math]::Round($g.vd * 10)
                    $codes += ('{0:000}{1:000}' -f $nd6, $vd3)
                } else { $codes += $null }
            } else { $codes += $null }
        }
        $glassCodesJson = ConvertTo-Json $codes -Compress
    }

    $waveJson = if ($ZmxData.wavelengths_nm.Count -gt 0) { ConvertTo-Json @($ZmxData.wavelengths_nm) -Compress } else { $null }
    $primaryWave = if ($ZmxData.wavelengths_nm.Count -gt 0) { $ZmxData.wavelengths_nm[0] } else { $null }
    $notesJson = if ($ImportNotes) { ConvertTo-Json @($ImportNotes) -Compress } else { $null }

    $params = @{
        vendor              = $ZmxData.vendor
        part_number         = $ZmxData.part_number
        family              = $ZmxData.family
        system_name         = $ZmxData.system_name
        description         = $ZmxData.description
        diameter            = $ZmxData.diameter_mm
        ct                  = $ZmxData.center_thickness_mm
        tt                  = if ($Paraxial) { $Paraxial.total_track_mm } else { $null }
        efl                 = if ($Paraxial) { $Paraxial.efl_mm } else { $null }
        bfl                 = if ($Paraxial) { $Paraxial.bfl_mm } else { $null }
        ffl                 = if ($Paraxial) { $Paraxial.ffl_mm } else { $null }
        fnum                = if ($Paraxial) { $Paraxial.fnum } else { $null }
        na                  = if ($Paraxial) { $Paraxial.na_image } else { $null }
        enp                 = $ZmxData.enp_diameter_mm
        glassCodes          = $glassCodesJson
        glassNames          = ConvertTo-Json $glassNames -Compress
        coating             = $ZmxData.coating
        wavePrimary         = $primaryWave
        waveJson            = $waveJson
        zmxRel              = $ZmxData.zmx_relpath
        lhltRel             = $LhltRelPath
        importedAt          = (Get-Date).ToString('o')
        importStatus        = $ImportStatus
        notesJson           = $notesJson
    }

    $insertLens = @'
INSERT OR REPLACE INTO stock_lenses (
    vendor, part_number, family, system_name, description,
    diameter_mm, center_thickness_mm, total_track_mm,
    efl_mm, bfl_mm, ffl_mm, fnum, na_image, enp_diameter_mm,
    glass_codes_json, glass_names_json, coating,
    wavelength_nm_primary, wavelengths_nm_json,
    zmx_relpath, lhlt_relpath, imported_at, import_status, import_notes_json
) VALUES (
    @vendor, @part_number, @family, @system_name, @description,
    @diameter, @ct, @tt,
    @efl, @bfl, @ffl, @fnum, @na, @enp,
    @glassCodes, @glassNames, @coating,
    @wavePrimary, @waveJson,
    @zmxRel, @lhltRel, @importedAt, @importStatus, @notesJson
);
'@
    Invoke-SqliteQuery -DataSource $DatabasePath -Query $insertLens -SqlParameters $params | Out-Null

    # Wipe and reinsert surface rows for this lens
    Invoke-SqliteQuery -DataSource $DatabasePath `
        -Query 'DELETE FROM lens_surfaces WHERE vendor = @v AND part_number = @p' `
        -SqlParameters @{ v = $ZmxData.vendor; p = $ZmxData.part_number } | Out-Null

    foreach ($s in $ZmxData.surfaces) {
        $catalog = $null
        if ($GlassIndex -and $s.glass_name -and $GlassIndex.ContainsKey($s.glass_name)) {
            $catalog = $GlassIndex[$s.glass_name].catalog
        }
        $aspJson = if ($s.aspheric_coeffs.Count -gt 0) { ConvertTo-Json @($s.aspheric_coeffs) -Compress } else { $null }
        $sp = @{
            v = $ZmxData.vendor; p = $ZmxData.part_number; i = $s.surface_index
            r = $s.radius_mm; t = $s.thickness_mm
            g = $s.glass_name; gc = $catalog
            k = $s.conic; asp = $aspJson
            ca = $s.clear_aperture_mm; st = $s.surface_type; stop = $s.is_stop
        }
        Invoke-SqliteQuery -DataSource $DatabasePath `
            -Query 'INSERT INTO lens_surfaces (vendor, part_number, surface_index, radius_mm, thickness_mm, glass_name, glass_catalog, conic, aspheric_coeffs_json, clear_aperture_mm, surface_type, is_stop) VALUES (@v, @p, @i, @r, @t, @g, @gc, @k, @asp, @ca, @st, @stop)' `
            -SqlParameters $sp | Out-Null
    }
}
