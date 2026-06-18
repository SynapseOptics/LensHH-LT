#!/bin/bash
set -e

# LensHH-LT Linux AppImage Builder
# Run from WSL: bash installer/build-linux-appimage.sh
#
# By DEFAULT this reuses the committed engine/linux-x64/ native (liblenshh_native.so
# + lenshh_kernel.fatbin) — exactly like the Windows installer reuses
# engine/win-x64/*.dll and the macOS packager reuses engine/osx-arm64/*.dylib. To
# rebuild the Linux native from source instead, set LENSHH_REBUILD_NATIVE=1 (needs
# nvcc + the LensHH-LT-NativeCore repo as a sibling of this one). (Task #37.)

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
# NativeCore is only needed when LENSHH_REBUILD_NATIVE=1; tolerate its absence in
# the default reuse path (a release machine need not have the engine repo checked out).
NATIVE_REPO="$(cd "$REPO_ROOT/../LensHH-LT-NativeCore" 2>/dev/null && pwd || echo "")"

# Only needed when rebuilding the native (LENSHH_REBUILD_NATIVE=1): nvcc on PATH so
# the CUDA fatbin builds. Harmless no-op in the default reuse path.
[ -d /usr/local/cuda/bin ] && export PATH="/usr/local/cuda/bin:$PATH"
# .NET SDK location on the build host (if not already on PATH).
[ -d /opt/dotnet ] && ! command -v dotnet >/dev/null && export PATH="/opt/dotnet:$PATH"

# Single source of truth for the version: the Inno-Setup MyAppVersion
# define in installer/LensHH-LT.iss. Keeps the Windows installer and
# Linux AppImage in lockstep so a 1.0.14.exe and a
# LensHH-LT-1.0.14-x86_64.AppImage are always built from the same code.
APP_VERSION=$(grep -E '^#define[[:space:]]+MyAppVersion' \
    "$REPO_ROOT/installer/LensHH-LT.iss" | sed -E 's/.*"([^"]+)".*/\1/')
if [ -z "$APP_VERSION" ]; then
    echo "ERROR: could not parse MyAppVersion from installer/LensHH-LT.iss"
    exit 1
fi

APP_DIR="$REPO_ROOT/installer/AppImage/LensHH-LT.AppDir"
OUTPUT_DIR="$REPO_ROOT/installer/Output"

echo "=== LensHH-LT Linux AppImage Builder ==="
echo "Repo:   $REPO_ROOT"
echo "Native: $NATIVE_REPO"
echo

# ── Step 1: Install build dependencies if needed ──
install_if_missing() {
    if ! command -v "$1" &>/dev/null; then
        echo "Installing $1..."
        sudo apt-get update -qq
        sudo apt-get install -y -qq "$2"
    fi
}

# ── Step 2: native library (linux-x64) ──
# DEFAULT: reuse the COMMITTED engine/linux-x64/ binaries (liblenshh_native.so +
# lenshh_kernel.fatbin), exactly like the Windows installer reuses
# engine/win-x64/*.dll and the macOS packager reuses engine/osx-arm64/*.dylib.
# The native is rebuilt from NativeCore source ONLY when LENSHH_REBUILD_NATIVE=1 —
# a deliberate engine step, never something an app-only release triggers
# implicitly (task #37). Previously this ALWAYS rebuilt, which coupled a Linux
# release to NativeCore's working-tree state and forced pinning NativeCore to the
# released commit for app-only releases (see the 1.0.123 release notes).
ENGINE_LINUX_DIR="$REPO_ROOT/engine/linux-x64"
ENGINE_SO="$ENGINE_LINUX_DIR/liblenshh_native.so"
ENGINE_FATBIN="$ENGINE_LINUX_DIR/lenshh_kernel.fatbin"

if [ "${LENSHH_REBUILD_NATIVE:-0}" = "1" ]; then
    echo
    echo "=== Rebuilding native library from source (LENSHH_REBUILD_NATIVE=1) ==="
    install_if_missing cmake cmake
    install_if_missing g++ g++
    install_if_missing make make
    if ! dpkg -s libomp-dev &>/dev/null 2>&1; then
        echo "Installing OpenMP..."
        sudo apt-get install -y -qq libomp-dev 2>/dev/null || true
    fi
    NATIVE_BUILD="$NATIVE_REPO/build-linux"
    mkdir -p "$NATIVE_BUILD"
    # LENSHH_DISABLE_ACTIVATION FORCED OFF (2026-06-05): cached CMake option used
    # only by the differential-validation harness; release builds must never
    # inherit a cached ON value.
    cmake -S "$NATIVE_REPO" -B "$NATIVE_BUILD" \
        -DCMAKE_BUILD_TYPE=Release \
        -DLENSHH_BUILD_TESTS=OFF \
        -DLENSHH_DISABLE_ACTIVATION=OFF
    cmake --build "$NATIVE_BUILD" --config Release -j$(nproc)

    NATIVE_SO=$(find "$NATIVE_BUILD" -name "liblenshh_native.so*" -not -type l | head -1)
    if [ -z "$NATIVE_SO" ]; then echo "ERROR: liblenshh_native.so not found!"; exit 1; fi
    NATIVE_FATBIN=$(find "$NATIVE_BUILD" -name "lenshh_kernel.fatbin" | head -1)
    if [ -z "$NATIVE_FATBIN" ]; then
        echo "ERROR: lenshh_kernel.fatbin not found — engine built WITHOUT CUDA."
        echo "       Install nvcc (add /usr/local/cuda/bin to PATH) and retry. Aborting."
        exit 1
    fi
    echo "Built: $NATIVE_SO"
    echo "Built fatbin: $NATIVE_FATBIN"

    # Stage rebuilt binaries into engine/linux-x64/ so dotnet publish bundles them
    # (the apps reference engine/linux-x64/liblenshh_native.so via PreserveNewest;
    # on Linux .NET resolves DllImport from AppContext.BaseDirectory first).
    echo "=== Staging rebuilt native into engine/linux-x64/ ==="
    mkdir -p "$ENGINE_LINUX_DIR"
    cp -f "$NATIVE_SO" "$ENGINE_SO";        echo "Staged: $ENGINE_SO"
    cp -f "$NATIVE_FATBIN" "$ENGINE_FATBIN"; echo "Staged: $ENGINE_FATBIN"
else
    echo
    echo "=== Using COMMITTED native engine/linux-x64/ (set LENSHH_REBUILD_NATIVE=1 to rebuild) ==="
    # Anti-staleness guard (reuse-mode equivalent of the old always-rebuild safety
    # net): the committed binaries MUST exist — fail loudly rather than ship an
    # AppImage with no/stale engine. They are git-tracked, so a clean checkout has
    # them; this trips only if someone deleted them or never staged a Linux native.
    if [ ! -f "$ENGINE_SO" ]; then
        echo "ERROR: $ENGINE_SO is missing. Commit the released Linux native, or"
        echo "       rebuild it with LENSHH_REBUILD_NATIVE=1. Aborting."
        exit 1
    fi
    if [ ! -f "$ENGINE_FATBIN" ]; then
        echo "ERROR: $ENGINE_FATBIN is missing — GPU support requires the fatbin."
        echo "       Commit it, or rebuild with LENSHH_REBUILD_NATIVE=1. Aborting."
        exit 1
    fi
    # Staleness hint (warn only, mirrors the macOS dylib age check): nudge if the
    # committed binary is old, in case the engine changed but wasn't re-staged.
    so_age_days=$(( ( $(date +%s) - $(stat -c %Y "$ENGINE_SO") ) / 86400 ))
    echo "Committed Linux native: $ENGINE_SO (${so_age_days} day(s) old)"
    if [ "$so_age_days" -gt 30 ]; then
        echo "WARNING: committed Linux native is ${so_age_days} days old. If the engine"
        echo "         changed since, rebuild with LENSHH_REBUILD_NATIVE=1. Packaging as-is."
    fi
fi

# Guard ALL engine/<rid>/ binaries — dotnet publish bundles every one of
# them (PreserveNewest), so a validation-configuration win-x64 DLL or osx
# dylib would ride inside the AppImage even though Linux never loads it.
for f in "$REPO_ROOT/engine/win-x64/lenshh_native.dll" \
         "$REPO_ROOT/engine/linux-x64/liblenshh_native.so" \
         "$REPO_ROOT/engine/osx-arm64/liblenshh_native.dylib"; do
    if [ -f "$f" ] && grep -q "validation-noauth" "$f"; then
        echo "FATAL: $f was built in the validation configuration."
        echo "       Refusing to package."
        exit 1
    fi
done
echo "Engine build-configuration check (engine/<rid> staging): OK"

# ── Guard: the managed engine DLL must be the obfuscated (Reactor) build. ──
# The dotnet publishes below copy engine/LensHH.Core.dll via HintPath; a dev
# (plain) DLL must never ride into a release. Fail-closed.
bash "$SCRIPT_DIR/verify-engine-obfuscated.sh" "$REPO_ROOT/engine/LensHH.Core.dll"

# ── Step 3: Build .NET app for linux-x64 ──
echo
echo "=== Publishing .NET app (linux-x64) ==="
PUBLISH_DIR="$REPO_ROOT/src/LensHH.App/bin/publish/linux-x64"
dotnet publish "$REPO_ROOT/src/LensHH.App/LensHH.App.csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -o "$PUBLISH_DIR"

# Also publish CLI and MCP
dotnet publish "$REPO_ROOT/src/LensHH.CLI/LensHH.CLI.csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -o "$PUBLISH_DIR/cli"

dotnet publish "$REPO_ROOT/src/LensHH.Mcp/LensHH.Mcp.csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -o "$PUBLISH_DIR/mcp"

dotnet publish "$REPO_ROOT/src/LensHH.OllamaBridge/LensHH.OllamaBridge.csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -o "$PUBLISH_DIR/ollama"

# MeritEvalBench — merit-function timing tool (value / jacobian / GPU).
# Self-contained so it carries its own engine DLL + liblenshh_native.so +
# catalogs; launched via the AppImage's --bench flag.
dotnet publish "$REPO_ROOT/src/MeritEvalBench/MeritEvalBench.csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -o "$PUBLISH_DIR/bench"

# ── Step 4: Assemble AppDir ──
echo
echo "=== Assembling AppDir ==="
rm -rf "$APP_DIR"
mkdir -p "$APP_DIR/usr/bin"
mkdir -p "$APP_DIR/usr/lib"
mkdir -p "$APP_DIR/usr/share/applications"
mkdir -p "$APP_DIR/usr/share/icons/hicolor/256x256/apps"
mkdir -p "$APP_DIR/usr/share/lenshh-lt/catalogs/Glass"
mkdir -p "$APP_DIR/usr/share/lenshh-lt/catalogs/FilteredGlassCatalogues"
mkdir -p "$APP_DIR/usr/share/lenshh-lt/catalogs/Lenses"
mkdir -p "$APP_DIR/usr/share/lenshh-lt/samples"
mkdir -p "$APP_DIR/usr/share/lenshh-lt/cli"
mkdir -p "$APP_DIR/usr/share/lenshh-lt/mcp"
mkdir -p "$APP_DIR/usr/share/lenshh-lt/ollama"
mkdir -p "$APP_DIR/usr/share/lenshh-lt/bench"

# Main app
cp -r "$PUBLISH_DIR"/* "$APP_DIR/usr/bin/"
# Remove CLI/MCP/OllamaBridge/bench from main bin (they have their own dirs)
rm -rf "$APP_DIR/usr/bin/cli" "$APP_DIR/usr/bin/mcp" "$APP_DIR/usr/bin/ollama" "$APP_DIR/usr/bin/bench"

# CLI, MCP, OllamaBridge, and MeritEvalBench
cp -r "$PUBLISH_DIR/cli"/* "$APP_DIR/usr/share/lenshh-lt/cli/"
cp -r "$PUBLISH_DIR/mcp"/* "$APP_DIR/usr/share/lenshh-lt/mcp/"
cp -r "$PUBLISH_DIR/ollama"/* "$APP_DIR/usr/share/lenshh-lt/ollama/"
cp -r "$PUBLISH_DIR/bench"/* "$APP_DIR/usr/share/lenshh-lt/bench/"

# Native library — copy the engine/linux-x64/ binaries (the committed ones in
# reuse mode, or the freshly-staged ones when LENSHH_REBUILD_NATIVE=1). Both modes
# leave them at $ENGINE_SO/$ENGINE_FATBIN, so use those (NOT the rebuild-only
# $NATIVE_* vars, which are unset in reuse mode).
cp "$ENGINE_SO" "$APP_DIR/usr/lib/liblenshh_native.so"

# GPU kernel sidecar. The native loader reads lenshh_kernel.fatbin from the
# directory of whichever liblenshh_native.so actually loads. dotnet bundles a
# copy of the .so into each app dir (GUI in usr/bin, plus cli/mcp/ollama/bench),
# and .NET resolves DllImport from AppContext.BaseDirectory first — so place the
# fatbin next to EVERY bundled .so, plus usr/lib as the LD_LIBRARY_PATH fallback.
cp -f "$ENGINE_FATBIN" "$APP_DIR/usr/lib/lenshh_kernel.fatbin"
find "$APP_DIR" -name "liblenshh_native.so" -printf '%h\n' | sort -u | while read -r d; do
    cp -f "$ENGINE_FATBIN" "$d/lenshh_kernel.fatbin"
done
echo "Bundled lenshh_kernel.fatbin next to every liblenshh_native.so in the AppDir"

# Glass catalogs (full Glass\*.AGF tree — picks up MISC.AGF, CORNING_*, etc.
# automatically; plus the curated filtered subsets used by sasian_design /
# auto glass-substitution: CoreSet28, StockGlassesUV, StockGlassesVisible).
cp "$REPO_ROOT/catalogs/Glass/"*.AGF "$APP_DIR/usr/share/lenshh-lt/catalogs/Glass/"
cp "$REPO_ROOT/catalogs/FilteredGlassCatalogues/"* "$APP_DIR/usr/share/lenshh-lt/catalogs/FilteredGlassCatalogues/" 2>/dev/null || true

# Stock-lens catalog: SQLite index + .lhlt prescriptions only (skip the
# build-time .zmx / .zar / .seq / .xlsx originals that live alongside).
# StockLensCatalog.ResolveDbPath looks under catalogs/ at runtime, and
# ResolveLhltPath then descends into Lenses/<vendor>/... — required for
# search_stock_lenses, find_matching_stock, insert_stock_lens,
# replace_element, and sasian_design's stock-substitution phase.
cp "$REPO_ROOT/catalogs/stock-lens-catalog.sqlite" \
    "$APP_DIR/usr/share/lenshh-lt/catalogs/stock-lens-catalog.sqlite"
rsync -a --include='*/' --include='*.lhlt' --exclude='*' \
    "$REPO_ROOT/catalogs/Lenses/" \
    "$APP_DIR/usr/share/lenshh-lt/catalogs/Lenses/"

# Sample lenses (entire tree, including UserGuide subfolders)
cp -r "$REPO_ROOT/samples/"* "$APP_DIR/usr/share/lenshh-lt/samples/"

# Icon (convert PNG for use)
cp "$REPO_ROOT/src/LensHH.App/Assets/icon_256.png" "$APP_DIR/usr/share/icons/hicolor/256x256/apps/lenshh-lt.png"
cp "$REPO_ROOT/src/LensHH.App/Assets/icon_256.png" "$APP_DIR/lenshh-lt.png"

# Desktop file
cat > "$APP_DIR/usr/share/applications/lenshh-lt.desktop" << 'DESKTOP'
[Desktop Entry]
Type=Application
Name=LensHH-LT
Comment=Optical Lens Design Software
Exec=LensHH.App
Icon=lenshh-lt
Categories=Science;Engineering;
MimeType=application/x-lenshh;
Terminal=false
DESKTOP
cp "$APP_DIR/usr/share/applications/lenshh-lt.desktop" "$APP_DIR/lenshh-lt.desktop"

# AppRun dispatcher — the single entry point the AppImage invokes.
# A leading flag selects which bundled binary to run:
#   ./LensHH-LT.AppImage                    -> GUI app (default)
#   ./LensHH-LT.AppImage --cli [args]       -> LensHH.CLI REPL / scripts
#   ./LensHH-LT.AppImage --mcp [args]       -> MCP server (stdio)
#   ./LensHH-LT.AppImage --ollama-bridge    -> Local-LLM bridge to MCP
#   ./LensHH-LT.AppImage --bench [args]     -> MeritEvalBench timing tool
#   ./LensHH-LT.AppImage --help-bundled     -> List the above
# Each branch shares LD_LIBRARY_PATH so liblenshh_native.so resolves.
cat > "$APP_DIR/AppRun" << 'APPRUN'
#!/bin/bash
HERE="$(dirname "$(readlink -f "$0")")"
export LD_LIBRARY_PATH="$HERE/usr/lib:$LD_LIBRARY_PATH"
export LENSHH_CATALOGS="$HERE/usr/share/lenshh-lt/catalogs"
# LENSHH_CATALOGS is read by the GUI (LensHH.App.Session.GuiSession);
# LENSHH_CATALOGS_DIR is read by the MCP stock-lens resolver
# (LensHH.Mcp.StockLensCatalog.ResolveDbPath) to locate
# stock-lens-catalog.sqlite without walking up the directory tree.
# Both point at the same bundled catalogs root.
export LENSHH_CATALOGS_DIR="$HERE/usr/share/lenshh-lt/catalogs"
export LENSHH_SAMPLES="$HERE/usr/share/lenshh-lt/samples"

# Help the OllamaBridge find the bundled MCP server without the user
# having to pass a path on every invocation.
export LENSHH_MCP_PATH="$HERE/usr/share/lenshh-lt/mcp/LensHH.Mcp"

case "$1" in
    --cli)
        shift
        exec "$HERE/usr/share/lenshh-lt/cli/LensHH.CLI" "$@"
        ;;
    --mcp)
        shift
        exec "$HERE/usr/share/lenshh-lt/mcp/LensHH.Mcp" "$@"
        ;;
    --ollama-bridge|--bridge)
        shift
        exec "$HERE/usr/share/lenshh-lt/ollama/LensHH.OllamaBridge" "$@"
        ;;
    --bench)
        shift
        exec "$HERE/usr/share/lenshh-lt/bench/MeritEvalBench" "$@"
        ;;
    --help-bundled)
        # Quoted heredoc — keeps $0 / $@ literal so the example wrapper
        # script doesn't get partially expanded on the help screen.
        cat <<'EOF'
LensHH-LT AppImage — bundled binaries:
  (no flag)            Launch the GUI app (default)
  --cli [args]         Launch the LensHH-LT CLI (REPL / --script)
  --mcp [args]         Run the MCP server (stdio; for Claude Desktop / Cursor)
  --ollama-bridge      Run the local-LLM bridge to the MCP server
  --bench [args]       Run MeritEvalBench (merit-eval timing; --lens ... --csv ...)
  --help-bundled       Show this list

Tip: to expose --cli / --mcp / --ollama-bridge on your PATH, drop a
wrapper script into ~/.local/bin (or anywhere on PATH). For example,
to add 'lenshh-cli' as a shell command:

    #!/bin/sh
    exec /full/path/to/LensHH-LT-x86_64.AppImage --cli "$@"

Save with execute permission (chmod +x), then run 'lenshh-cli'.
EOF
        ;;
    *)
        exec "$HERE/usr/bin/LensHH.App" "$@"
        ;;
esac
APPRUN
chmod +x "$APP_DIR/AppRun"

# ── Step 5: Download appimagetool and build AppImage ──
echo
echo "=== Building AppImage ==="
mkdir -p "$OUTPUT_DIR"
APPIMAGETOOL="$REPO_ROOT/installer/appimagetool-x86_64.AppImage"

if [ ! -f "$APPIMAGETOOL" ]; then
    echo "Downloading appimagetool..."
    wget -q -O "$APPIMAGETOOL" \
        "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage"
    chmod +x "$APPIMAGETOOL"
fi

# Build the AppImage
ARCH=x86_64 "$APPIMAGETOOL" "$APP_DIR" "$OUTPUT_DIR/LensHH-LT-${APP_VERSION}-x86_64.AppImage"

echo
echo "=== Done! ==="
echo "AppImage: $OUTPUT_DIR/LensHH-LT-${APP_VERSION}-x86_64.AppImage"
