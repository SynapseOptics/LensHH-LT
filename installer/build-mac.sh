#!/bin/bash
# ============================================================
#  LensHH-LT — macOS Developer Build
# ============================================================
#
#  Builds the full stack on macOS:
#    1. Native library (C++ → .dylib)
#    2. Deploys .dylib into Engine/Native/osx-arm64/
#    3. C# Core (netstandard2.0)
#    4. Deploys LensHH.Core.dll into LT/engine/
#    5. The App (net8.0, osx-arm64)
#
#  Assumes the three-repo layout produced by clone-dev.sh:
#
#    <parent>/
#    ├── LensHH-LT/              (you are here when running this)
#    ├── LensHH-LT-Engine/
#    └── LensHH-LT-NativeCore/
#
#  Usage (from LensHH-LT/):
#    bash installer/build-mac.sh
#
#  Requires: cmake, libomp (brew), .NET 8 SDK, Xcode CLT
# ============================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
WORKSPACE="$(cd "$LT_ROOT/.." && pwd)"
ENGINE="$WORKSPACE/LensHH-LT-Engine"
NATIVE="$WORKSPACE/LensHH-LT-NativeCore"

if [ ! -d "$ENGINE" ]; then
    echo "ERROR: LensHH-LT-Engine not found at $ENGINE"
    echo "Did you run clone-dev.sh from the workspace parent?"
    exit 1
fi
if [ ! -d "$NATIVE" ]; then
    echo "ERROR: LensHH-LT-NativeCore not found at $NATIVE"
    exit 1
fi

# Detect Apple architecture — CMakeLists maps APPLE → osx-arm64 by default.
# If you're building on an Intel Mac, override via CMAKE_OSX_ARCHITECTURES.
ARCH="osx-arm64"
if [ "$(uname -m)" = "x86_64" ]; then
    echo "Note: running on Intel Mac — setting CMAKE_OSX_ARCHITECTURES=x86_64."
    echo "      Engine/Native/ has no osx-x64 folder yet; you may need to add one."
    ARCH="osx-x64"
    EXTRA_CMAKE_ARGS="-DCMAKE_OSX_ARCHITECTURES=x86_64"
else
    EXTRA_CMAKE_ARGS="-DCMAKE_OSX_ARCHITECTURES=arm64"
fi

# -------------------------
# 1. Native build
# -------------------------
echo ""
echo "=== [1/5] Building native library (C++) ==="
cd "$NATIVE"
cmake -B build -S . -DCMAKE_BUILD_TYPE=Release $EXTRA_CMAKE_ARGS
cmake --build build --config Release

# dylib output (Apple prefixes with 'lib')
DYLIB_SRC="$NATIVE/build/bin/$ARCH/liblenshh_native.dylib"
if [ ! -f "$DYLIB_SRC" ]; then
    # Fall back to lib-output dir if CMake placed it elsewhere
    DYLIB_SRC="$(find "$NATIVE/build" -name "liblenshh_native.dylib" -print -quit)"
fi
if [ -z "$DYLIB_SRC" ] || [ ! -f "$DYLIB_SRC" ]; then
    echo "ERROR: liblenshh_native.dylib not found after CMake build"
    exit 1
fi

# -------------------------
# 2. Deploy native
# -------------------------
echo ""
echo "=== [2/5] Deploying dylib to Engine/Native/$ARCH/ and LT/engine/$ARCH/ ==="
mkdir -p "$ENGINE/Native/$ARCH"
cp "$DYLIB_SRC" "$ENGINE/Native/$ARCH/"
mkdir -p "$LT_ROOT/engine/$ARCH"
cp "$DYLIB_SRC" "$LT_ROOT/engine/$ARCH/"

# -------------------------
# 3. Core build
# -------------------------
echo ""
echo "=== [3/5] Building LensHH.Core (netstandard2.0) ==="
cd "$ENGINE/Core"
dotnet build -c Release -nologo

# -------------------------
# 4. Deploy Core
# -------------------------
echo ""
echo "=== [4/5] Deploying LensHH.Core.dll to LT/engine/ ==="
cp "$ENGINE/Core/bin/Release/netstandard2.0/LensHH.Core.dll" "$LT_ROOT/engine/LensHH.Core.dll"
# Intentionally not copying .pdb — keep symbol files out of the shipped tree.

# -------------------------
# 5. App build
# -------------------------
echo ""
echo "=== [5/5] Building LensHH.App (net8.0, osx) ==="
cd "$LT_ROOT/src/LensHH.App"
dotnet build -c Release -nologo

echo ""
echo "=== Done ==="
echo "Run the App with:"
echo "  cd $LT_ROOT/src/LensHH.App && dotnet run -c Release"
echo ""
