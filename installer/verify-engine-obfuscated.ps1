# Fail-closed obfuscation guard for the managed engine DLL.
#
# Detects the .NET Reactor "-suppressildasm 1" stamp (SuppressIldasmAttribute),
# which a plain `dotnet build` never adds. Exit 0 = obfuscated (OK to package);
# exit 1 = plain/dev DLL or missing file (refuse to package).
#
# Shared by BOTH the Windows installer compile (embedded in LensHH-LT.iss via an
# ISPP Exec at compile time — so even a direct ISCC run can't bypass it) and
# build-installer.bat. The Linux/mac path uses the sibling verify-engine-obfuscated.sh.
#
# Rationale (2026-07-19): never package — not even a -dev test build — with an
# unobfuscated engine DLL. Bypassing the guard for convenience is how an
# unobfuscated build eventually escapes to a customer.
param(
    [Parameter(Mandatory = $true)][string]$Dll
)

if (-not (Test-Path -LiteralPath $Dll)) {
    Write-Host "FATAL: engine DLL not found: $Dll"
    exit 1
}

$bytes = [IO.File]::ReadAllBytes($Dll)
$text  = [Text.Encoding]::ASCII.GetString($bytes)

if ($text.Contains('SuppressIldasmAttribute')) {
    Write-Host "Obfuscation check: OK (Reactor-protected engine DLL): $Dll"
    exit 0
}
else {
    Write-Host "FATAL: $Dll is a NON-OBFUSCATED (dev) build - refusing to package."
    Write-Host "       Restore it: run scripts\publish-obfuscated.bat (from the engine repo),"
    Write-Host "       which stages the VERIFIED-obfuscated DLL into LensHH-LT\engine\."
    exit 1
}
