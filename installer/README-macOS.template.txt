LensHH-LT — macOS (Apple Silicon / arm64)
==========================================
Version: {VERSION}     Built: {DATE}

The GUI ships as a normal macOS app bundle (LensHH-LT.app) and every
binary is PRE-SIGNED at build time. You do NOT need to run codesign.

It is not notarized by Apple yet, so the first launch shows Gatekeeper's
"unidentified developer" prompt — the macOS equivalent of the Windows
SmartScreen "Run anyway". Clearing it is a one-time right-click (below).

IMPORTANT: GPU acceleration is NOT supported on macOS (and will not be).
All optimization runs on the CPU. GPU acceleration is Windows-only today.

--------------------------------------------------------------------
WHAT'S INSIDE
--------------------------------------------------------------------
  LensHH-LT.app   The GUI  -- double-click to launch
  cli/            Command-line REPL/tools  ->  ./LensHH.CLI
  mcp/            MCP server (stdio)       ->  ./LensHH.Mcp
  renderapp/      Render helper (PNG export for MCP/CLI; auto-launched)
  ollama/         Local-LLM REPL bridge    ->  ./LensHH.OllamaBridge
  bench/          Merit-eval timing tool   ->  ./MeritEvalBench
  catalogs/       Stock-lens database (sqlite + 7,600+ prescriptions)
  samples/        Sample lenses, incl. all User Guide example systems
  docs/           User Guide PDF, searchable HTML help, markdown docs
  README-macOS.txt

The GUI is fully self-contained inside LensHH-LT.app (its glass catalogs
and the CPU-only native engine ride along). The command-line tools are
self-contained too; they share the package-root catalogs/ folder for the
stock-lens database. Keep the package folder structure intact and do not
move individual tool folders out on their own.

--------------------------------------------------------------------
LAUNCH THE GUI
--------------------------------------------------------------------
1. Double-click LensHH-LT.app.
2. The first time, macOS says it "cannot be opened because the developer
   cannot be verified." Dismiss it, then RIGHT-CLICK LensHH-LT.app ->
   Open -> Open. macOS remembers the choice; future launches are a plain
   double-click.
   (Equivalently: System Settings -> Privacy & Security -> "Open Anyway".)

That's it for the GUI — no Terminal, no codesign.

Open one of the samples/*.lhlt files to start. Docs are in docs/ — start
with docs/LensHH-LT-UserGuide.pdf, or open docs/html/LensHH-LT-Help.html
in a browser for searchable help.

--------------------------------------------------------------------
COMMAND-LINE TOOLS (optional)
--------------------------------------------------------------------
The CLI, MCP server, Ollama bridge, and benchmark are run from Terminal.
First time only, clear quarantine on the package so they're allowed to
load their bundled libraries:

    xattr -dr com.apple.quarantine .

Then:

    cli/LensHH.CLI                 # interactive CLI
    mcp/LensHH.Mcp                 # MCP server (stdio)
    ollama/LensHH.OllamaBridge     # local-LLM REPL
    bench/MeritEvalBench           # merit-eval timing

If a tool reports "permission denied", your unzip tool dropped the Unix
execute bit; restore it with:

    chmod +x cli/LensHH.CLI mcp/LensHH.Mcp renderapp/LensHH.RenderApp \
             ollama/LensHH.OllamaBridge bench/MeritEvalBench \
             "LensHH-LT.app/Contents/MacOS/LensHH.App"

--------------------------------------------------------------------
TROUBLESHOOTING: GUI instantly "Killed" (rc=137)
--------------------------------------------------------------------
This is exactly why the GUI ships as LensHH-LT.app and not a bare
binary. Background, in case you ever run into it with a loose copy:

The GUI's inner executable is named "LensHH.App". macOS filesystems are
case-insensitive, so ".App" == ".app" — and a file ending in .app is
treated as an application bundle. If that file sits LOOSE (not inside a
proper LensHH-LT.app/Contents/MacOS/ structure with a Contents/
Info.plist), Gatekeeper's syspolicyd tries to register it as a bundle,
fails (error 45), and terminates the process on launch. Wrapping it in
the real .app bundle — which this package does — makes registration
succeed and the app runs.

So: always launch the GUI via LensHH-LT.app. Do NOT pull
Contents/MacOS/LensHH.App out and run it directly — a loose file named
LensHH.App will be killed regardless of signing or quarantine. (This was
root-caused 2026-06-07 with a controlled test: the identical signed bytes
ran fine when named "LensHH" or "LensHH.bin" and were killed only when
named "LensHH.App".)

The command-line tools (LensHH.CLI, LensHH.Mcp, etc.) are NOT affected —
their names don't end in .app, so they run as plain signed binaries.

--------------------------------------------------------------------
NOTES
--------------------------------------------------------------------
- CPU-only by design: no CUDA on macOS, so the GPU pre-screen is
  unavailable.
- The Claude/Ollama configuration utilities from the Windows build are
  Windows-only (WPF). On macOS, point your MCP client at mcp/LensHH.Mcp
  directly (stdio transport).
- Native engine: built by the NativeCore macOS CI (GitHub Actions,
  macos-14); managed apps cross-published for osx-arm64.
