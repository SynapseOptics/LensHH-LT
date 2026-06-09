; LensHH-LT Inno Setup Installer Script
; Requires Inno Setup 6.x

#define MyAppName "LensHH-LT"
#define MyAppVersion "1.0.119"
#define MyAppPublisher "Synapse Optics"
#define MyAppExeName "LensHH.App.exe"
#define MyAppURL "https://github.com/SynapseOptics/LensHH-LT"

; Paths relative to this .iss file
#define RepoRoot ".."
#define AppBin RepoRoot + "\src\LensHH.App\bin\Release\net8.0"
#define CliBin RepoRoot + "\src\LensHH.CLI\bin\Release\net8.0"
#define McpBin RepoRoot + "\src\LensHH.Mcp\bin\Release\net8.0"
#define OllamaBin RepoRoot + "\src\LensHH.OllamaBridge\bin\Release\net8.0"
#define ConfigBin RepoRoot + "\src\ConfigureLensHHMcp\bin\Release\net8.0-windows"
#define ConfigOllamaBin RepoRoot + "\src\ConfigureOllamaBridge\bin\Release\net8.0-windows"
#define RenderAppBin RepoRoot + "\src\LensHH.RenderApp\bin\Release\net8.0"
#define BenchBin RepoRoot + "\src\MeritEvalBench\bin\Release\net8.0"
#define Catalogs RepoRoot + "\catalogs"
#define Samples RepoRoot + "\samples"
#define Engine RepoRoot + "\engine"
#define Assets RepoRoot + "\src\LensHH.App\Assets"
#define Docs RepoRoot + "\docs"

[Setup]
AppId={{E7A3F2B1-4C5D-4E6F-8A9B-1C2D3E4F5A6B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=Output
OutputBaseFilename=LensHH-LT-Setup-{#MyAppVersion}
SetupIconFile={#Assets}\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
LicenseFile=
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
ChangesAssociations=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "fileassoc"; Description: "Associate .lhlt files with {#MyAppName}"; GroupDescription: "File associations:"; Flags: checkedonce

[Files]
; Main application (exclude non-Windows runtimes and PDB files)
Source: "{#AppBin}\*.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppBin}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppBin}\*.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppBin}\runtimes\win-x64\*"; DestDir: "{app}\runtimes\win-x64"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#AppBin}\runtimes\win-x86\*"; DestDir: "{app}\runtimes\win-x86"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#AppBin}\runtimes\win-arm64\*"; DestDir: "{app}\runtimes\win-arm64"; Flags: ignoreversion recursesubdirs createallsubdirs

; CLI tool — every DLL the project pulls in (engine, IO, rendering,
; Spectre.Console, the native ray-trace DLL) lives in the build folder
; alongside LensHH.CLI.exe. Earlier installs only copied LensHH.CLI.dll
; itself, so the exe failed to start with "could not load LensHH.Core".
; Same runtimes\ note as the MCP server below — defensive copy in case
; a future package adds a platform-specific assembly the host probes.
Source: "{#CliBin}\LensHH.CLI.exe"; DestDir: "{app}\cli"; Flags: ignoreversion
Source: "{#CliBin}\*.dll"; DestDir: "{app}\cli"; Flags: ignoreversion
Source: "{#CliBin}\*.json"; DestDir: "{app}\cli"; Flags: ignoreversion
Source: "{#CliBin}\runtimes\*"; DestDir: "{app}\cli\runtimes"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

; MCP server — same fix as the CLI: copy the whole net8.0 publish
; folder so Microsoft.Extensions.* / LensHH.Core / native deps land
; next to LensHH.Mcp.exe. Includes the runtimes\ subtree because
; some Microsoft.Extensions.* packages (notably .Logging.EventLog)
; ship a Windows-specific assembly under runtimes\win\lib\net8.0
; that the .NET host probes at startup; without it MCP throws
; "Could not load file or assembly 'System.Diagnostics.EventLog'".
Source: "{#McpBin}\LensHH.Mcp.exe"; DestDir: "{app}\mcp"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#McpBin}\*.dll"; DestDir: "{app}\mcp"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#McpBin}\*.json"; DestDir: "{app}\mcp"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#McpBin}\runtimes\*"; DestDir: "{app}\mcp\runtimes"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

; RenderApp — Avalonia/Skia helper subprocess that renders analysis
; PNGs and shows a tabbed window for the LLM-driven workflow. Invoked
; by the MCP server (via pipe) and the CLI (analysis render-png).
; Without this folder, MCP rendering tools throw FileNotFoundException
; and the CLI's --png path can't produce images.
Source: "{#RenderAppBin}\LensHH.RenderApp.exe"; DestDir: "{app}\renderapp"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#RenderAppBin}\*.dll"; DestDir: "{app}\renderapp"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#RenderAppBin}\*.json"; DestDir: "{app}\renderapp"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#RenderAppBin}\runtimes\win-x64\*"; DestDir: "{app}\renderapp\runtimes\win-x64"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#RenderAppBin}\runtimes\win-x86\*"; DestDir: "{app}\renderapp\runtimes\win-x86"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#RenderAppBin}\runtimes\win-arm64\*"; DestDir: "{app}\renderapp\runtimes\win-arm64"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

; Ollama Bridge (local-LLM REPL — drives the MCP server from a tool-calling
; Ollama model running on the user's machine; no cloud / API key required).
Source: "{#OllamaBin}\LensHH.OllamaBridge.exe"; DestDir: "{app}\ollama"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#OllamaBin}\*.dll"; DestDir: "{app}\ollama"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#OllamaBin}\*.json"; DestDir: "{app}\ollama"; Flags: ignoreversion skipifsourcedoesntexist

; MeritEvalBench — merit-function timing tool (value / jacobian / GPU). Ships
; in tools\bench as a self-contained folder (its own LensHH.Core.dll,
; lenshh_native.dll and catalogs\), so on any installed machine you can run:
;   tools\bench\MeritEvalBench.exe --lens <a.lhlt> --csv out.csv
Source: "{#BenchBin}\*"; DestDir: "{app}\tools\bench"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

; Claude MCP Configuration Utility
Source: "{#ConfigBin}\ConfigureLensHHMcp.exe"; DestDir: "{app}\config"; Flags: ignoreversion
Source: "{#ConfigBin}\ConfigureLensHHMcp.dll"; DestDir: "{app}\config"; Flags: ignoreversion
Source: "{#ConfigBin}\ConfigureLensHHMcp.runtimeconfig.json"; DestDir: "{app}\config"; Flags: ignoreversion
Source: "{#ConfigBin}\ConfigureLensHHMcp.deps.json"; DestDir: "{app}\config"; Flags: ignoreversion

; Ollama Bridge Configuration Utility (WPF helper that locates the bundled
; LensHH.Mcp.exe + LensHH.OllamaBridge.exe, lists Ollama models, and
; produces a Desktop shortcut / batch file with the chosen model baked in).
Source: "{#ConfigOllamaBin}\ConfigureOllamaBridge.exe"; DestDir: "{app}\config"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#ConfigOllamaBin}\ConfigureOllamaBridge.dll"; DestDir: "{app}\config"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#ConfigOllamaBin}\ConfigureOllamaBridge.runtimeconfig.json"; DestDir: "{app}\config"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#ConfigOllamaBin}\ConfigureOllamaBridge.deps.json"; DestDir: "{app}\config"; Flags: ignoreversion skipifsourcedoesntexist

; Glass catalogs
Source: "{#Catalogs}\Glass\*.AGF"; DestDir: "{app}\catalogs\Glass"; Flags: ignoreversion
Source: "{#Catalogs}\FilteredGlassCatalogues\*"; DestDir: "{app}\catalogs\FilteredGlassCatalogues"; Flags: ignoreversion skipifsourcedoesntexist

; Stock-lens catalog — SQLite index + per-vendor .lhlt prescription tree.
; Required for the MCP stock-lens workflow (search_stock_lenses,
; find_matching_stock, insert_stock_lens, replace_element, sasian_design's
; stock-substitution phase). StockLensCatalog.ResolveDbPath looks for the
; sqlite at {app}\catalogs\, and ResolveLhltPath then descends into
; Lenses\<vendor>\... using the relative path stored in the DB.
; *.lhlt pattern (with recursesubdirs) excludes the build-time .zmx /
; .zar / .seq / .xlsx originals that live alongside in the repo tree.
Source: "{#Catalogs}\stock-lens-catalog.sqlite"; DestDir: "{app}\catalogs"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#Catalogs}\Lenses\*.lhlt"; DestDir: "{app}\catalogs\Lenses"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

; Sample lenses (entire tree, including UserGuide subfolders)
Source: "{#Samples}\*"; DestDir: "{app}\samples"; Flags: ignoreversion recursesubdirs createallsubdirs

; Documentation (markdown files — GitHub-flavored, renderable in any text editor
; or markdown viewer). Index starts at docs\README.md.
Source: "{#Docs}\*.md"; DestDir: "{app}\docs"; Flags: ignoreversion
; Bundled PDF user guide — regenerated from the markdown sources on each
; installer build (see installer\build-installer.bat).
Source: "{#Docs}\LensHH-LT-UserGuide.pdf"; DestDir: "{app}\docs"; Flags: ignoreversion skipifsourcedoesntexist
; Searchable single-file HTML help bundle (lunr.js indexed, self-
; contained — opens in any browser on any OS).
Source: "{#Docs}\html\LensHH-LT-Help.html"; DestDir: "{app}\docs\html"; Flags: ignoreversion skipifsourcedoesntexist

; Native engine
Source: "{#Engine}\LensHH.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Engine}\win-x64\lenshh_native.dll"; DestDir: "{app}"; Flags: ignoreversion

; Icon
Source: "{#Assets}\icon.ico"; DestDir: "{app}"; Flags: ignoreversion

; Bundled VC++ 2015-2022 Redistributable (x64). Chain-installed during
; setup via the [Run] section below, gated by VCRedistNeedsInstall in
; [Code]. Without this, lenshh_native.dll fails to load on machines that
; don't already have the MSVC runtime — common on clean Windows installs.
; Downloaded from https://aka.ms/vs/17/release/vc_redist.x64.exe by the
; build-installer pipeline (gitignored).
Source: "redist\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: VCRedistNeedsInstall

[Registry]
; File association for .lhlt files
Root: HKA; Subkey: "Software\Classes\.lhlt"; ValueType: string; ValueName: ""; ValueData: "LensHH.LensFile"; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\LensHH.LensFile"; ValueType: string; ValueName: ""; ValueData: "LensHH-LT Lens File"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\LensHH.LensFile\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\icon.ico,0"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\LensHH.LensFile\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: fileassoc

[Icons]
; Start Menu
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\icon.ico"
Name: "{group}\LensHH-LT Claude Configure"; Filename: "{app}\config\ConfigureLensHHMcp.exe"; IconFilename: "{app}\icon.ico"
Name: "{group}\LensHH-LT Ollama Configure"; Filename: "{app}\config\ConfigureOllamaBridge.exe"; IconFilename: "{app}\icon.ico"
; Console-app shortcuts — launched via cmd /k so the console window
; stays open if the app exits with an error message, instead of the
; default Windows behavior of closing the window before the user can
; read it.
Name: "{group}\LensHH-LT CLI"; Filename: "{cmd}"; Parameters: "/k """"{app}\cli\LensHH.CLI.exe"""""; WorkingDir: "{app}\cli"; IconFilename: "{app}\icon.ico"
Name: "{group}\Ollama Bridge (Local LLM)"; Filename: "{cmd}"; Parameters: "/k """"{app}\ollama\LensHH.OllamaBridge.exe"""""; WorkingDir: "{app}\ollama"; IconFilename: "{app}\icon.ico"
Name: "{group}\Sample Lenses"; Filename: "{app}\samples"
Name: "{group}\User Guide (PDF)"; Filename: "{app}\docs\LensHH-LT-UserGuide.pdf"
Name: "{group}\Help (Searchable)"; Filename: "{app}\docs\html\LensHH-LT-Help.html"
Name: "{group}\Documentation"; Filename: "{app}\docs"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
; Desktop
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\icon.ico"; Tasks: desktopicon

[Run]
; Install VC++ Redistributable first (only if not already present). Silent,
; no reboot prompt. Required for lenshh_native.dll to load.
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; \
  StatusMsg: "Installing Microsoft Visual C++ Redistributable..."; \
  Check: VCRedistNeedsInstall
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
function VCRedistNeedsInstall: Boolean;
var
  Installed: Cardinal;
begin
  // Microsoft VC++ 2015-2022 Redistributable writes
  //   HKLM\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64\Installed = 1
  // when present (works for both per-machine and per-user installs because
  // the registry value is in HKLM either way). Returning False here suppresses
  // both the [Files] copy and the [Run] launch, so end users who already have
  // the runtime see no extra step or progress UI.
  Result := True;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64', 'Installed', Installed) then
    if Installed = 1 then
      Result := False;
end;

function IsDotNet8Installed: Boolean;
var
  ResultCode: Integer;
begin
  // Check if dotnet.exe can find .NET 8.0 desktop runtime
  Result := Exec('dotnet', '--list-runtimes', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
            and (ResultCode = 0);
  if Result then
  begin
    // More specific check: look for Microsoft.NETCore.App 8.x
    Result := RegKeyExists(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App\8.0.0') or
              FileExists(ExpandConstant('{pf}\dotnet\shared\Microsoft.NETCore.App\8.0.0\dotnet.dll'));
  end;
end;

function DotNet8DesktopRuntimeExists: Boolean;
var
  FindRec: TFindRec;
  Path: String;
begin
  Result := False;
  Path := ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if FindFirst(Path + '\8.*', FindRec) then
  begin
    Result := True;
    FindClose(FindRec);
  end;
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;

  if not DotNet8DesktopRuntimeExists then
  begin
    if MsgBox('LensHH-LT requires the .NET 8.0 Desktop Runtime.'#13#10#13#10 +
              'Would you like to download it now?'#13#10 +
              '(You can install LensHH-LT first and download .NET later.)',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open',
        'https://dotnet.microsoft.com/en-us/download/dotnet/8.0',
        '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;
  end;
end;
