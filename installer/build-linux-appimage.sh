#!/bin/bash
set -e

# LensHH-LT Linux AppImage Builder
# Run from WSL: bash installer/build-linux-appimage.sh

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
NATIVE_REPO="$(cd "$REPO_ROOT/../LensHH-LT-NativeCore" && pwd)"

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

install_if_missing cmake cmake
install_if_missing g++ g++
install_if_missing make make

# For OpenMP support
if ! dpkg -s libomp-dev &>/dev/null 2>&1; then
    echo "Installing OpenMP..."
    sudo apt-get install -y -qq libomp-dev 2>/dev/null || true
fi

# ── Step 2: Build native library ──
echo
echo "=== Building native library (linux-x64) ==="
NATIVE_BUILD="$NATIVE_REPO/build-linux"
mkdir -p "$NATIVE_BUILD"
cmake -S "$NATIVE_REPO" -B "$NATIVE_BUILD" \
    -DCMAKE_BUILD_TYPE=Release \
    -DLENSHH_BUILD_TESTS=OFF
cmake --build "$NATIVE_BUILD" --config Release -j$(nproc)

NATIVE_SO=$(find "$NATIVE_BUILD" -name "liblenshh_native.so*" -not -type l | head -1)
if [ -z "$NATIVE_SO" ]; then
    echo "ERROR: liblenshh_native.so not found!"
    exit 1
fi
echo "Built: $NATIVE_SO"

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
mkdir -p "$APP_DIR/usr/share/lenshh-lt/samples"
mkdir -p "$APP_DIR/usr/share/lenshh-lt/cli"
mkdir -p "$APP_DIR/usr/share/lenshh-lt/mcp"
mkdir -p "$APP_DIR/usr/share/lenshh-lt/ollama"

# Main app
cp -r "$PUBLISH_DIR"/* "$APP_DIR/usr/bin/"
# Remove CLI/MCP/OllamaBridge from main bin (they have their own dirs)
rm -rf "$APP_DIR/usr/bin/cli" "$APP_DIR/usr/bin/mcp" "$APP_DIR/usr/bin/ollama"

# CLI, MCP, and OllamaBridge
cp -r "$PUBLISH_DIR/cli"/* "$APP_DIR/usr/share/lenshh-lt/cli/"
cp -r "$PUBLISH_DIR/mcp"/* "$APP_DIR/usr/share/lenshh-lt/mcp/"
cp -r "$PUBLISH_DIR/ollama"/* "$APP_DIR/usr/share/lenshh-lt/ollama/"

# Native library
cp "$NATIVE_SO" "$APP_DIR/usr/lib/liblenshh_native.so"

# Glass catalogs
cp "$REPO_ROOT/catalogs/Glass/"*.AGF "$APP_DIR/usr/share/lenshh-lt/catalogs/Glass/"
cp "$REPO_ROOT/catalogs/FilteredGlassCatalogues/"* "$APP_DIR/usr/share/lenshh-lt/catalogs/FilteredGlassCatalogues/" 2>/dev/null || true

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
#   ./LensHH-LT.AppImage --help-bundled     -> List the above
# Each branch shares LD_LIBRARY_PATH so liblenshh_native.so resolves.
cat > "$APP_DIR/AppRun" << 'APPRUN'
#!/bin/bash
HERE="$(dirname "$(readlink -f "$0")")"
export LD_LIBRARY_PATH="$HERE/usr/lib:$LD_LIBRARY_PATH"
export LENSHH_CATALOGS="$HERE/usr/share/lenshh-lt/catalogs"
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
    --help-bundled)
        # Quoted heredoc — keeps $0 / $@ literal so the example wrapper
        # script doesn't get partially expanded on the help screen.
        cat <<'EOF'
LensHH-LT AppImage — bundled binaries:
  (no flag)            Launch the GUI app (default)
  --cli [args]         Launch the LensHH-LT CLI (REPL / --script)
  --mcp [args]         Run the MCP server (stdio; for Claude Desktop / Cursor)
  --ollama-bridge      Run the local-LLM bridge to the MCP server
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
