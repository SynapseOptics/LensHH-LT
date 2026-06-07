# ============================================================
#  LensHH-LT — macOS (osx-arm64) package build, from Windows
# ============================================================
#
#  Cross-publishes the full tool set for Apple Silicon, assembles a
#  package whose content mirrors the Windows installer (LensHH-LT.iss),
#  signs every Mach-O file with rcodesign (ad-hoc by default, Developer
#  ID + notarization when a certificate is supplied), and zips it.
#
#  Usage (from anywhere):
#    pwsh installer/build-mac-package.ps1 -Version 1.0.119
#
#  Developer ID signing + notarization (optional — requires an Apple
#  Developer account; export the cert as .p12 and create an App Store
#  Connect API key):
#    $env:LENSHH_P12_PASSWORD = '...'
#    pwsh installer/build-mac-package.ps1 -Version 1.0.119 `
#        -P12File C:\secrets\developer-id.p12 `
#        -NotaryApiKeyFile C:\secrets\appstore-connect-key.json
#
#  Prerequisites:
#    - .NET 8 SDK
#    - engine/osx-arm64/liblenshh_native.dylib staged from the macOS CI
#      build (NativeCore .github/workflows/macos-build.yml artifact) —
#      it CANNOT be built on Windows; the script refuses to package if
#      it is missing and warns if it looks stale.
#    - rcodesign (auto-downloaded to installer/tools/rcodesign/ on
#      first run, SHA256-verified).
#
#  WHY SIGNING HAPPENS HERE (read before "simplifying"):
#    Apple Silicon kills unsigned arm64 binaries on exec (SIGKILL,
#    rc=137, no dialog). A plain Mach-O ad-hoc signature is accepted;
#    a bundle-style ad-hoc signature is NOT (amfid Error -423) — and
#    codesign produces a bundle-style signature whenever an Info.plist
#    sits next to the executable. Additionally each denied launch
#    poisons syspolicyd with a sticky per-path deny that survives
#    re-signing. Signing correctly HERE, before anything ever runs on
#    the Mac, sidesteps the whole minefield. Root-caused 2026-06-07.
# ============================================================

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    # Developer ID Application certificate (.p12). Omit for ad-hoc signing.
    [string]$P12File,

    # Name of the env var holding the .p12 password (never pass the
    # password itself on a command line).
    [string]$P12PasswordEnv = 'LENSHH_P12_PASSWORD',

    # App Store Connect API key (.json from `rcodesign encode-app-store-
    # connect-api-key`). Enables notarization of the final zip.
    [string]$NotaryApiKeyFile,

    # Skip dotnet publish and re-use the existing staging folder —
    # for fast iteration on packaging/signing only.
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = (Resolve-Path "$ScriptDir\..").Path
$Rid       = 'osx-arm64'
$PkgName   = "LensHH-LT-$Rid-$Version"
$Staging   = Join-Path $ScriptDir "osx-dist\$PkgName"
$OutZip    = Join-Path $ScriptDir "osx-dist\$PkgName.zip"

# ------------------------------------------------------------
# 0. rcodesign — auto-download (pinned version + SHA256)
# ------------------------------------------------------------
$RcVersion = '0.29.0'
$RcSha256  = '54bb500e2da7a8de02fcae0f331d1cac6e6d7173b4281042ff9c528ba3159aaa'
$RcDir     = Join-Path $ScriptDir 'tools\rcodesign'
$Rcodesign = Join-Path $RcDir 'rcodesign.exe'

if (-not (Test-Path $Rcodesign)) {
    Write-Host "=== rcodesign not found — downloading $RcVersion ==="
    $url = "https://github.com/indygreg/apple-platform-rs/releases/download/apple-codesign/$RcVersion/apple-codesign-$RcVersion-x86_64-pc-windows-msvc.zip"
    $tmp = Join-Path $env:TEMP "rcodesign-$RcVersion.zip"
    Invoke-WebRequest $url -OutFile $tmp
    $actual = (Get-FileHash $tmp -Algorithm SHA256).Hash.ToLower()
    if ($actual -ne $RcSha256) { throw "rcodesign download SHA256 mismatch: $actual" }
    $xdir = Join-Path $env:TEMP "rcodesign-$RcVersion"
    Expand-Archive $tmp -DestinationPath $xdir -Force
    New-Item -ItemType Directory -Force $RcDir | Out-Null
    Copy-Item (Join-Path $xdir "apple-codesign-$RcVersion-x86_64-pc-windows-msvc\rcodesign.exe") $RcDir
    Remove-Item $tmp, $xdir -Recurse -Force
}
& $Rcodesign --version | Out-Host

# ------------------------------------------------------------
# 1. Engine dylib guards
# ------------------------------------------------------------
$Dylib = Join-Path $RepoRoot "engine\$Rid\liblenshh_native.dylib"
if (-not (Test-Path $Dylib)) {
    throw "engine\$Rid\liblenshh_native.dylib is missing. Stage it from the NativeCore macOS CI artifact (macos-build.yml) first."
}
$dylibAge = (Get-Date) - (Get-Item $Dylib).LastWriteTime
Write-Host ("Engine dylib: {0:N1} days old ({1})" -f $dylibAge.TotalDays, (Get-Item $Dylib).LastWriteTime)
if ($dylibAge.TotalDays -gt 3) {
    Write-Warning "engine\$Rid dylib is more than 3 days old — is it from the latest NativeCore macOS CI run? (It cannot be rebuilt on Windows; this script will package it as-is.)"
}
# Refuse validation-configuration engines (mirrors build-linux-appimage.sh).
$bytes  = [System.IO.File]::ReadAllBytes($Dylib)
$marker = [System.Text.Encoding]::ASCII.GetBytes('validation-noauth')
for ($i = 0; $i -le $bytes.Length - $marker.Length; $i++) {
    $hit = $true
    for ($j = 0; $j -lt $marker.Length; $j++) {
        if ($bytes[$i + $j] -ne $marker[$j]) { $hit = $false; break }
    }
    if ($hit) { throw "FATAL: $Dylib was built in the validation configuration. Refusing to package." }
}
Write-Host "Engine build-configuration check: OK"

# ------------------------------------------------------------
# 2. dotnet publish (six tools, self-contained osx-arm64)
# ------------------------------------------------------------
# Folder layout mirrors the Windows install ({app}\cli, {app}\mcp,
# {app}\renderapp, {app}\ollama, {app}\tools\bench → flattened to
# bench/). Tool-relative ..\catalogs\ probing (StockLensCatalog.
# ResolveDbPath and friends) therefore finds the package-root catalogs/.
$Projects = [ordered]@{
    'app'       = 'src\LensHH.App\LensHH.App.csproj'
    'cli'       = 'src\LensHH.CLI\LensHH.CLI.csproj'
    'mcp'       = 'src\LensHH.Mcp\LensHH.Mcp.csproj'
    'renderapp' = 'src\LensHH.RenderApp\LensHH.RenderApp.csproj'
    'ollama'    = 'src\LensHH.OllamaBridge\LensHH.OllamaBridge.csproj'
    'bench'     = 'src\MeritEvalBench\MeritEvalBench.csproj'
}
$MainExes = @{
    'app'       = 'LensHH.App'
    'cli'       = 'LensHH.CLI'
    'mcp'       = 'LensHH.Mcp'
    'renderapp' = 'LensHH.RenderApp'
    'ollama'    = 'LensHH.OllamaBridge'
    'bench'     = 'MeritEvalBench'
}

if (-not $SkipPublish) {
    if (Test-Path $Staging) { Remove-Item $Staging -Recurse -Force }
    foreach ($name in $Projects.Keys) {
        Write-Host ""
        Write-Host "=== Publishing $name ($Rid) ==="
        dotnet publish (Join-Path $RepoRoot $Projects[$name]) `
            -c Release -r $Rid --self-contained true `
            -o (Join-Path $Staging $name) -nologo -v q
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $name" }
    }
}
if (-not (Test-Path $Staging)) { throw "Staging folder missing: $Staging (remove -SkipPublish?)" }

# ------------------------------------------------------------
# 2b. Assemble the GUI as a real .app bundle
# ------------------------------------------------------------
# WHY (root-caused on macOS 2026-06-07 via a controlled filename test —
# LensHH.App=137/SIGKILL, LensHH=ran, LensHH.bin=ran): the GUI apphost is
# named "LensHH.App". On case-insensitive APFS ".App" == ".app", so
# syspolicyd tries to register the flat Mach-O as an application bundle,
# fails (error 45 — there is no Contents/Info.plist), and Gatekeeper
# SIGKILLs it. Wrapping it in a proper bundle makes registration succeed
# (and gives users a double-clickable app). Only the GUI is affected —
# the other five tools have non-".app" names and run as flat binaries.
# This is NOT the old "adjacent Info.plist" trap: here the plist lives in
# Contents/, the exe in Contents/MacOS/ — a valid bundle, not a flat file
# with a sibling plist.
$AppBundle = Join-Path $Staging 'LensHH-LT.app'
$flatApp   = Join-Path $Staging 'app'
# Idempotency for -SkipPublish re-runs: if the bundle is already assembled
# (a prior run consumed app/ into it), skip straight past assembly.
$bundleAlreadyBuilt = (Test-Path (Join-Path $AppBundle 'Contents\MacOS\LensHH.App')) -and -not (Test-Path $flatApp)
if (-not $bundleAlreadyBuilt) {
if (-not (Test-Path (Join-Path $flatApp 'LensHH.App'))) {
    throw "Published GUI apphost not found at $flatApp\LensHH.App"
}
Write-Host ""
Write-Host "=== Assembling LensHH-LT.app bundle ==="
if (Test-Path $AppBundle) { Remove-Item $AppBundle -Recurse -Force }
$cMacOS = Join-Path $AppBundle 'Contents\MacOS'
$cRes   = Join-Path $AppBundle 'Contents\Resources'
New-Item -ItemType Directory -Force $cMacOS, $cRes | Out-Null
# Everything published for the GUI lives next to the apphost (its glass
# catalogs at catalogs/Glass, the engine dylib, every DLL) → Contents/MacOS.
Get-ChildItem $flatApp -Force | Move-Item -Destination $cMacOS
Remove-Item $flatApp -Recurse -Force
# Icon → Contents/Resources/icon.icns (Info.plist CFBundleIconFile=icon).
Copy-Item (Join-Path $RepoRoot 'src\LensHH.App\Assets\icon.icns') (Join-Path $cRes 'icon.icns')
# Glass catalogs into the bundle so the GUI is self-contained — it probes
# baseDir/catalogs/Glass, and baseDir for a bundled .NET app is
# Contents/MacOS/. ~3.5 MB; mirrors {app}\catalogs\ on Windows (.iss
# 126-127). The dotnet publish does NOT include these (only MeritEvalBench
# bundles its own), so they must be staged explicitly.
$bundleCat = Join-Path $cMacOS 'catalogs'
New-Item -ItemType Directory -Force (Join-Path $bundleCat 'Glass') | Out-Null
Copy-Item (Join-Path $RepoRoot 'catalogs\Glass\*.AGF') (Join-Path $bundleCat 'Glass')
Copy-Item (Join-Path $RepoRoot 'catalogs\FilteredGlassCatalogues') $bundleCat -Recurse
# Info.plist → Contents/Info.plist, real version injected (the repo copy
# carries placeholder 1.0.0).
$plist = Get-Content (Join-Path $RepoRoot 'src\LensHH.App\Info.plist') -Raw
$plist = $plist -replace '(<key>CFBundleVersion</key>\s*<string>)[^<]*(</string>)', "`${1}$Version`${2}"
$plist = $plist -replace '(<key>CFBundleShortVersionString</key>\s*<string>)[^<]*(</string>)', "`${1}$Version`${2}"
Set-Content (Join-Path $AppBundle 'Contents\Info.plist') $plist -NoNewline
Write-Host "Bundle: LensHH-LT.app (CFBundleExecutable=LensHH.App, version $Version)"
} else {
    Write-Host "Bundle already assembled (skip-publish re-run) — reusing LensHH-LT.app"
}

# ------------------------------------------------------------
# 3. Guard: Info.plist only inside the .app bundle's Contents/
# ------------------------------------------------------------
# An Info.plist ADJACENT to a flat Mach-O executable is the dangerous
# case (Gatekeeper SIGKILL). A plist at <name>.app/Contents/Info.plist
# is correct and required. The csproj keeps Info.plist out of the flat
# publish (CopyToPublishDirectory=Never); the bundle's plist is placed
# by step 2b above. This guard catches a stray adjacent plist.
$plists = Get-ChildItem $Staging -Recurse -Force -Filter 'Info.plist' -ErrorAction SilentlyContinue
$badPlists = $plists | Where-Object { $_.FullName -notmatch '\.app\\Contents\\Info\.plist$' }
if ($badPlists) {
    $badPlists | ForEach-Object { Write-Host "  OFFENDER: $($_.FullName)" }
    throw "FATAL: Info.plist found outside a .app/Contents/ (see above). Adjacent to a flat Mach-O it causes a Gatekeeper SIGKILL on macOS."
}
Write-Host "Info.plist guard: OK (only LensHH-LT.app/Contents/Info.plist present)"

# ------------------------------------------------------------
# 4. Shared package-root content (mirrors LensHH-LT.iss [Files])
# ------------------------------------------------------------
Write-Host ""
Write-Host "=== Copying samples / docs / stock-lens catalog ==="

# Samples — entire tree, including UserGuide subfolders (.iss line 141).
robocopy (Join-Path $RepoRoot 'samples') (Join-Path $Staging 'samples') /E /NFL /NDL /NJH /NJS | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy samples failed ($LASTEXITCODE)" }

# Docs — markdown set + PDF user guide + searchable HTML help
# (.iss lines 145-151).
$docsDst = Join-Path $Staging 'docs'
New-Item -ItemType Directory -Force $docsDst | Out-Null
Copy-Item (Join-Path $RepoRoot 'docs\*.md') $docsDst
$pdf = Join-Path $RepoRoot 'docs\LensHH-LT-UserGuide.pdf'
if (Test-Path $pdf) { Copy-Item $pdf $docsDst } else { Write-Warning "UserGuide.pdf not found — run the docs build (installer\build-installer.bat regenerates it)." }
$help = Join-Path $RepoRoot 'docs\html\LensHH-LT-Help.html'
if (Test-Path $help) {
    New-Item -ItemType Directory -Force (Join-Path $docsDst 'html') | Out-Null
    Copy-Item $help (Join-Path $docsDst 'html')
} else { Write-Warning "docs\html\LensHH-LT-Help.html not found — searchable help will be absent." }

# Package-root catalogs/ — mirrors what the Windows installer ships at
# {app}\catalogs\ (.iss 126-138), shared by the flat CLI/MCP/RenderApp/
# Ollama tools via their ..\catalogs\ probe:
#   - Glass\*.AGF                 glass catalogs (.iss 126)
#   - FilteredGlassCatalogues\*   filtered catalogs (.iss 127)
#   - stock-lens-catalog.sqlite   stock-lens index (.iss 137)
#   - Lenses\**\*.lhlt            per-vendor prescriptions, *.lhlt ONLY —
#                                 excludes build-time .zmx/.zar/.seq/.xlsx
$catDst = Join-Path $Staging 'catalogs'
New-Item -ItemType Directory -Force (Join-Path $catDst 'Glass') | Out-Null
Copy-Item (Join-Path $RepoRoot 'catalogs\Glass\*.AGF') (Join-Path $catDst 'Glass')
Copy-Item (Join-Path $RepoRoot 'catalogs\FilteredGlassCatalogues') $catDst -Recurse
Copy-Item (Join-Path $RepoRoot 'catalogs\stock-lens-catalog.sqlite') $catDst
robocopy (Join-Path $RepoRoot 'catalogs\Lenses') (Join-Path $catDst 'Lenses') *.lhlt /S /NFL /NDL /NJH /NJS | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy stock lenses failed ($LASTEXITCODE)" }

# ------------------------------------------------------------
# 5. README
# ------------------------------------------------------------
$readme = Get-Content (Join-Path $ScriptDir 'README-macOS.template.txt') -Raw
$readme = $readme.Replace('{VERSION}', $Version).Replace('{DATE}', (Get-Date -Format 'yyyy-MM-dd'))
Set-Content (Join-Path $Staging 'README-macOS.txt') $readme -NoNewline

# ------------------------------------------------------------
# 6. Sign every Mach-O file with rcodesign
# ------------------------------------------------------------
Write-Host ""
Write-Host "=== Signing (rcodesign) ==="

function Test-MachO([string]$Path) {
    $fs = [System.IO.File]::OpenRead($Path)
    try {
        if ($fs.Length -lt 4) { return $false }
        $b = New-Object byte[] 4
        [void]$fs.Read($b, 0, 4)
        # 64-bit LE (cffaedfe), 32-bit LE (cefaedfe), and fat/universal
        # (cafebabe / bebafeca) Mach-O magics.
        $magic = ('{0:x2}{1:x2}{2:x2}{3:x2}' -f $b[0], $b[1], $b[2], $b[3])
        return $magic -in @('cffaedfe', 'cefaedfe', 'cafebabe', 'bebafeca')
    } finally { $fs.Dispose() }
}

# Flat-tool Mach-O files — everything EXCEPT the .app bundle, which is
# signed as a unit afterwards (rcodesign signs nested code + seals it).
$machos = @()
$machos += Get-ChildItem $Staging -Recurse -File -Filter '*.dylib'
$machos += Get-ChildItem $Staging -Recurse -File |
    Where-Object { $_.Extension -eq '' -or $MainExes.Values -contains $_.Name } |
    Where-Object { Test-MachO $_.FullName }
$machos = $machos |
    Where-Object { $_.FullName -notlike "$AppBundle*" } |
    Sort-Object FullName -Unique |
    Sort-Object { if ($_.Name -like '*.dylib') { 0 } else { 1 } }

$signArgs = @('sign')
if ($P12File) {
    if (-not (Test-Path $P12File)) { throw "P12 file not found: $P12File" }
    if (-not (Get-Item "env:$P12PasswordEnv" -ErrorAction SilentlyContinue)) {
        throw "Env var $P12PasswordEnv is not set (the .p12 password)."
    }
    # Hardened runtime is required for notarization.
    $signArgs += @('--p12-file', $P12File, '--p12-password-env', $P12PasswordEnv,
                   '--code-signature-flags', 'runtime')
    Write-Host "Mode: Developer ID ($P12File) + hardened runtime"
} else {
    Write-Host "Mode: ad-hoc (flat tools as plain Mach-O; GUI as a signed .app bundle)"
}

$n = 0
foreach ($f in $machos) {
    & $Rcodesign @signArgs $f.FullName 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "rcodesign sign failed: $($f.FullName)" }
    $n++
}
# Sign the GUI .app as a unit (signs nested dylibs + apphost and seals
# Contents/). Ad-hoc → a valid bundle macOS accepts; with -P12File →
# Developer ID + hardened runtime.
Write-Host "Signing LensHH-LT.app bundle..."
& $Rcodesign @signArgs $AppBundle 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "rcodesign sign failed for the .app bundle" }
$n++
Write-Host "Signed $n Mach-O targets (flat tools + .app bundle)."

# Spot-verify the GUI apphost (inside the bundle) + the five flat tools.
$verifyTargets = @(Join-Path $AppBundle 'Contents\MacOS\LensHH.App')
foreach ($name in 'cli', 'mcp', 'renderapp', 'ollama', 'bench') {
    $verifyTargets += Join-Path $Staging "$name\$($MainExes[$name])"
}
foreach ($exe in $verifyTargets) {
    if (-not (Test-Path $exe)) { throw "Main executable missing after publish: $exe" }
    $info = & $Rcodesign print-signature-info $exe 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0 -or $info -notmatch 'code_directory') {
        throw "Signature verification failed for $exe"
    }
}
Write-Host "Signature spot-check (GUI bundle + 5 flat tools): OK"

# ------------------------------------------------------------
# 7. Zip (preserving Unix exec bits)
# ------------------------------------------------------------
# PowerShell's Compress-Archive drops Unix permissions, so the bundle's
# apphost (Contents/MacOS/LensHH.App) would unzip without its execute
# bit and double-click would fail. We write the zip manually and stamp
# each Mach-O entry's external attributes with mode 0755; macOS unzip /
# Archive Utility honor the high bits. (Belt-and-suspenders: README also
# documents a one-line chmod fallback in case a user's extractor ignores
# them.)
function New-UnixZip([string]$SourceDir, [string]$ZipPath) {
    Add-Type -AssemblyName System.IO.Compression | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null
    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
    $base     = (Resolve-Path $SourceDir).Path
    $parent   = Split-Path $base -Parent
    $execMode = [Convert]::ToInt32('755', 8) -shl 16
    $fileMode = [Convert]::ToInt32('644', 8) -shl 16
    $fs  = [System.IO.File]::Open($ZipPath, [System.IO.FileMode]::Create)
    $zip = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($f in Get-ChildItem $base -Recurse -File -Force) {
            $rel = $f.FullName.Substring($parent.Length + 1).Replace('\', '/')
            $entry = [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $f.FullName, $rel, [System.IO.Compression.CompressionLevel]::Optimal)
            $isExec = ($f.Extension -eq '.dylib') -or (Test-MachO $f.FullName)
            $entry.ExternalAttributes = if ($isExec) { $execMode } else { $fileMode }
        }
    } finally { $zip.Dispose(); $fs.Dispose() }
}
# Stamp every central-directory record's "version made by" host byte to
# 3 (Unix). .NET writes host 0 (FAT), and macOS's Archive Utility (the
# Finder double-click extractor) ONLY applies the Unix mode bits from
# ExternalAttributes when the host says Unix — otherwise it drops them and
# the .app's apphost extracts without +x ("application cannot be opened").
# Our package is well under the ZIP64 thresholds (<4 GB, <65535 entries),
# so the standard EOCD layout applies.
function Repair-ZipUnixHost([string]$ZipPath) {
    $bytes = [System.IO.File]::ReadAllBytes($ZipPath)
    $eocd = -1
    $floor = [Math]::Max(0, $bytes.Length - 65557)
    for ($i = $bytes.Length - 22; $i -ge $floor; $i--) {
        if ($bytes[$i] -eq 0x50 -and $bytes[$i + 1] -eq 0x4B -and
            $bytes[$i + 2] -eq 0x05 -and $bytes[$i + 3] -eq 0x06) { $eocd = $i; break }
    }
    if ($eocd -lt 0) { throw "EOCD record not found in $ZipPath" }
    $cdOffset = [BitConverter]::ToUInt32($bytes, $eocd + 16)
    $cdCount  = [BitConverter]::ToUInt16($bytes, $eocd + 10)
    $p = [int]$cdOffset
    for ($e = 0; $e -lt $cdCount; $e++) {
        if (-not ($bytes[$p] -eq 0x50 -and $bytes[$p + 1] -eq 0x4B -and
                  $bytes[$p + 2] -eq 0x01 -and $bytes[$p + 3] -eq 0x02)) {
            throw "Central-directory record $e malformed at offset $p"
        }
        $bytes[$p + 5] = 0x03   # version-made-by HIGH byte = Unix host
        $nameLen  = [BitConverter]::ToUInt16($bytes, $p + 28)
        $extraLen = [BitConverter]::ToUInt16($bytes, $p + 30)
        $cmtLen   = [BitConverter]::ToUInt16($bytes, $p + 32)
        $p += 46 + $nameLen + $extraLen + $cmtLen
    }
    [System.IO.File]::WriteAllBytes($ZipPath, $bytes)
    Write-Host "Patched $cdCount central-directory records to Unix host."
}

Write-Host ""
Write-Host "=== Zipping (preserving exec bits) ==="
New-UnixZip $Staging $OutZip
Repair-ZipUnixHost $OutZip
$zipMB = [math]::Round((Get-Item $OutZip).Length / 1MB, 1)
Write-Host "Created: $OutZip ($zipMB MB)"

# ------------------------------------------------------------
# 8. Notarize (optional; Developer ID signing required)
# ------------------------------------------------------------
if ($NotaryApiKeyFile) {
    if (-not $P12File) { throw "Notarization requires Developer ID signing (-P12File)." }
    Write-Host ""
    Write-Host "=== Notarizing (this waits on Apple) ==="
    & $Rcodesign notary-submit --api-key-file $NotaryApiKeyFile --wait $OutZip
    if ($LASTEXITCODE -ne 0) { throw "Notarization failed." }
    # Note: stapling only works on bundles/dmg/pkg — flat-binary zips are
    # verified online by Gatekeeper instead. That is expected.
    Write-Host "Notarization accepted."
}

# ------------------------------------------------------------
# 9. Manifest summary
# ------------------------------------------------------------
Write-Host ""
Write-Host "=== Package summary ==="
foreach ($d in 'LensHH-LT.app','cli','mcp','renderapp','ollama','bench','samples','docs','catalogs') {
    $p = Join-Path $Staging $d
    $c = if (Test-Path $p) { (Get-ChildItem $p -Recurse -File).Count } else { 'MISSING' }
    Write-Host ("  {0,-14} {1} files" -f $d, $c)
}
Write-Host ("  GUI apphost in bundle: " + (Test-Path (Join-Path $AppBundle 'Contents\MacOS\LensHH.App')))
Write-Host ("  stock-lens sqlite:     " + (Test-Path (Join-Path $Staging 'catalogs\stock-lens-catalog.sqlite')))
Write-Host ("  UserGuide.pdf:         " + (Test-Path (Join-Path $Staging 'docs\LensHH-LT-UserGuide.pdf')))
Write-Host ""
Write-Host "Done: $OutZip"
