#!/bin/bash
# ============================================================
#  Fail-closed guard: REFUSE to package a non-obfuscated (dev)
#  managed engine DLL. The shipped LensHH.Core.dll MUST be the
#  .NET Reactor-protected build.
#
#  Detection: Reactor is invoked with -suppressildasm 1, which
#  stamps the protected assembly with a
#  System.Runtime.CompilerServices.SuppressIldasmAttribute that
#  a plain `dotnet build` NEVER adds. We detect it as a raw
#  byte-string so this needs no .NET reflection and runs in any
#  shell / CI runner (verified: present in the obfuscated build,
#  absent in the plain one).
#
#  Usage:  verify-engine-obfuscated.sh <path-to-LensHH.Core.dll>
#  Exit:   0 = obfuscated (OK to ship) ; 1 = dev DLL / missing (ABORT)
# ============================================================
set -e
DLL="$1"

if [ -z "$DLL" ] || [ ! -f "$DLL" ]; then
    echo "FATAL: engine DLL not found: ${DLL:-<none>}"
    exit 1
fi

if grep -q -a 'SuppressIldasmAttribute' "$DLL"; then
    echo "Obfuscation check OK: $(basename "$DLL") is the Reactor-protected build."
    exit 0
fi

echo "FATAL: $DLL is a NON-OBFUSCATED (dev) engine DLL — refusing to package."
echo "       The shipped engine DLL must be the .NET Reactor-protected build"
echo "       (the proprietary engine would otherwise ship in the clear)."
echo
echo "  Fix on Windows, then re-run packaging:"
echo "    - run a Release build of Core (the ProtectWithReactor post-build),"
echo "      or  LensHH-LT-Engine\\scripts\\publish-obfuscated.bat"
echo "    - that copies Core\\bin\\Release\\netstandard2.0\\obfuscated\\LensHH.Core.dll"
echo "      to LensHH-LT\\engine\\LensHH.Core.dll"
exit 1
