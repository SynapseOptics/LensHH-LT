# Building LensHH-LT from Source

LensHH-LT is distributed as a combination of:

- **Open source** — the App (UI), CLI, MCP server, IO, Rendering, API, and
  installer scripts, all in this repository under `src/`.
- **Pre-built engine binary** — `engine/LensHH.Core.dll` (optical
  computation core) and the platform-specific native rendering library
  (`engine/win-x64/*.dll`, `engine/osx-arm64/*.dylib`). These are shipped
  with every release; their source is not part of this repository.

You can build and run the full application from just this repository —
everything in `src/` compiles against the pre-built engine binaries.

---

## Prerequisites

Install **.NET 8 SDK** (or newer). Everything in `src/` builds on .NET 8.

- **Windows**: <https://dotnet.microsoft.com/download> or `winget install Microsoft.DotNet.SDK.8`
- **macOS**: `brew install dotnet-sdk`
- **Linux (Ubuntu/Debian)**: `sudo apt install dotnet-sdk-8.0` (or the upstream Microsoft repo instructions)

Optional tools:

- **Inno Setup 6** (Windows-only) — for building the `.exe` installer from `installer/LensHH-LT.iss`.
- **Node.js ≥ 18** — only needed if you want to regenerate the PDF user guide from `docs/`.
- **Git** — for cloning.

---

## Clone and build

```bash
git clone https://github.com/SynapseOptics/LensHH-LT.git
cd LensHH-LT
dotnet build
```

That's it. The build produces:

- `src/LensHH.App/bin/Debug/net8.0/LensHH.App.exe` — desktop GUI
- `src/LensHH.CLI/bin/Debug/net8.0/LensHH.CLI.*` — command-line tool
- `src/LensHH.Mcp/bin/Debug/net8.0/LensHH.Mcp.*` — MCP server
- `src/LensHH.RenderApp/bin/Debug/net8.0/LensHH.RenderApp.*` — native renderer helper

A `-c Release` flag builds optimized binaries.

---

## Running

**GUI (desktop app):**

```bash
dotnet run --project src/LensHH.App
```

or launch `src/LensHH.App/bin/Debug/net8.0/LensHH.App.exe` directly.

**CLI:**

```bash
dotnet run --project src/LensHH.CLI -- --help
```

**MCP server** — see `docs/api-cli-mcp.md` for setup instructions with Claude / other MCP-compatible clients.

---

## Platform notes

### macOS (Apple Silicon / Intel)

The shipped `engine/osx-arm64/liblenshh_native.dylib` is an ARM64 binary
for Apple Silicon machines. Intel Macs can run it through Rosetta 2, or
build under Rosetta by running `arch -x86_64 /bin/bash` before `dotnet
build`. The C# Core (`LensHH.Core.dll`) is platform-agnostic — it's the
same file across Windows / macOS / Linux.

On first launch you'll see a Gatekeeper dialog for an unsigned binary.
Right-click → Open → Open to bypass it (one-time per fresh checkout).

### Linux

Build and run the same way. If you want a self-contained AppImage,
`installer/build-linux-appimage.sh` packages the Release build.

### Windows

The installer pipeline (`installer/build-installer.bat`) builds Release
+ runs Inno Setup → produces a signed installer in `installer/Output/`.
You only need that if you're packaging for redistribution; for local
development `dotnet run` is all you need.

---

## What you cannot build from this repository

`engine/LensHH.Core.dll` and the native rendering libraries in
`engine/*/` are shipped pre-built — their source is not part of this
public repo. If you pull a new version of LensHH-LT, those binaries
update along with the source, so you always have a consistent pair.

The engine DLL exposes the same public API your `src/` code builds
against, so IntelliSense and compilation work normally.

---

## Troubleshooting

- **`dotnet: command not found`** — the .NET 8 SDK isn't on your PATH.
  Reinstall per the prerequisites section.
- **`Could not load file or assembly 'LensHH.Core'`** — the `engine/`
  folder is missing or corrupt. `git status` in the repo; if
  `engine/LensHH.Core.dll` is missing, `git checkout engine/` to
  restore it.
- **macOS: "cannot be opened because the developer cannot be verified"**
  — Gatekeeper. Right-click the executable → Open → Open.
- **Build succeeds but App crashes at startup on macOS** — likely an
  architecture mismatch. Confirm you're on Apple Silicon (arm64) or
  building under Rosetta.
