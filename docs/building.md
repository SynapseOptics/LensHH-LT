# Building from Source

The source code in this repository is MIT-licensed; the optical
engine binaries shipped under `engine/` are proprietary to Synapse
Optics and require activation to run. See `LICENSE` for the full
terms. You can build, modify, and redistribute the source freely;
end users still need a valid trial or paid license to compute.

LensHH-LT ships packaged builds for every platform at each release:
a Windows installer, a pre-signed macOS (Apple Silicon) zip package,
and a Linux AppImage. See [Getting Started](getting-started.md) for
the install steps. This page is only for users who *want* a source
build (e.g., to track main).

## Prerequisites

You need:

- **.NET 8 SDK** (or newer)
- **Git**

Install commands by platform:

| Platform | Command |
|---|---|
| macOS | `brew install --cask dotnet-sdk` |
| Linux (Debian/Ubuntu) | `sudo apt install dotnet-sdk-8.0` |
| Windows | `winget install Microsoft.DotNet.SDK.8` |

That's it for prerequisites. The optical engine, native ray-trace
library, and rendering helpers are all included as pre-built
binaries in the repo.

## Clone and build

```bash
git clone https://github.com/SynapseOptics/LensHH-LT.git
cd LensHH-LT
dotnet build -c Release
```

The build takes ~30 s on a modern laptop.

## Run the desktop app

```bash
dotnet run --project src/LensHH.App -c Release
```

Or launch the binary directly:

- **macOS**: `src/LensHH.App/bin/Release/net8.0/LensHH.App`
- **Linux**: same path
- **Windows**: `src\LensHH.App\bin\Release\net8.0\LensHH.App.exe`

## macOS: first-launch security dialog

The first time you launch on macOS, Gatekeeper will block the
unsigned binary with "cannot be opened because the developer cannot
be verified". To bypass it:

1. **Right-click** the `LensHH.App` binary → **Open** → **Open**.
2. macOS remembers your choice; subsequent launches are silent.

You only need to do this once per fresh checkout.

## macOS: Apple Silicon only

The shipped native ray-trace library is built for Apple Silicon
(`arm64`). Intel Macs cannot run it: Rosetta 2 translates x86-64
code *for* Apple Silicon, not the other way around, and no `osx-x64`
build of the engine is currently shipped.

## CLI and MCP server

The same build also produces:

- `src/LensHH.CLI/bin/Release/net8.0/LensHH.CLI` — interactive CLI
- `src/LensHH.Mcp/bin/Release/net8.0/LensHH.Mcp` — MCP server for
  Claude Code, Cursor, and other LLM agents

See [API, CLI, and MCP](api-cli-mcp.md) for how to wire the MCP
server into Claude Code and similar clients.

## Linux AppImage

If you want a self-contained, no-install single-file binary on
Linux, run `installer/build-linux-appimage.sh` from the project root.
The output AppImage in `installer/Output/` is portable across most
modern Linux distributions.

## Troubleshooting

| Symptom | Fix |
|---|---|
| `dotnet: command not found` | The .NET 8 SDK isn't on your PATH. Reinstall per the table above. |
| `Could not load file or assembly 'LensHH.Core'` | The `engine/` folder is missing. Run `git checkout engine/` to restore. |
| macOS: app crashes immediately on launch | Architecture mismatch. Confirm you're on Apple Silicon, or building under Rosetta if Intel. |
| Build succeeds but app shows blank window | Likely a graphics-driver issue on Linux. Try `LIBGL_ALWAYS_SOFTWARE=1 dotnet run --project src/LensHH.App`. |
