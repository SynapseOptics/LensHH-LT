@echo off
setlocal
cd /d "%~dp0\.."

echo === Ensure VC++ Redistributable is downloaded ===
REM The .iss bundles installer\redist\vc_redist.x64.exe and chain-installs
REM it during setup so end users on clean Windows machines don't hit
REM "specified module could not be found" when lenshh_native.dll loads.
REM The file is gitignored (~25 MB), so download it on demand.
if not exist "installer\redist\vc_redist.x64.exe" (
    echo Downloading vc_redist.x64.exe from aka.ms ...
    if not exist "installer\redist" mkdir "installer\redist"
    curl -sL -o "installer\redist\vc_redist.x64.exe" "https://aka.ms/vs/17/release/vc_redist.x64.exe"
    if errorlevel 1 (
        echo Failed to download vc_redist.x64.exe. Aborting.
        exit /b 1
    )
)

echo === Building LensHH-LT Release ===
REM dotnet's incremental build can leave LensHH.App/bin's transitive copies
REM (LensHH.IO.dll, LensHH.Rendering.dll) stale even after their source
REM projects rebuild — the App's own .cs files didn't change so it skips
REM the dependent-copy step. The installer then ships the App's bin folder
REM with old transitive DLLs. dotnet clean forces a fresh App build that
REM re-copies the dependents.
dotnet clean -c Release
dotnet build -c Release
dotnet build src\ConfigureLensHHMcp\ConfigureLensHHMcp.csproj -c Release
if errorlevel 1 (
    echo Build failed!
    pause
    exit /b 1
)

echo.
echo === Generating PDF documentation ===
rem Extract MyAppVersion from the .iss so the PDF cover page matches
rem the installer version without duplicating the constant. The .iss line is:
rem   #define MyAppVersion "1.0.106"
rem Split on default whitespace, take token 3 (the quoted version), then
rem strip the quotes via string substitution.
set "PDF_VERSION=dev"
for /f "tokens=3" %%A in ('findstr /b /c:"#define MyAppVersion" installer\LensHH-LT.iss') do set "PDF_VERSION_RAW=%%A"
if defined PDF_VERSION_RAW set "PDF_VERSION=%PDF_VERSION_RAW:"=%"
echo PDF cover-page version: %PDF_VERSION%
call docs\build\build-pdf.bat %PDF_VERSION%
if errorlevel 1 (
    echo PDF build failed!
    pause
    exit /b 1
)

echo.
echo === Compiling Installer ===
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" (
    "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" installer\LensHH-LT.iss
) else (
    echo Inno Setup 6 not found at default location.
    echo Please run ISCC.exe manually on installer\LensHH-LT.iss
    pause
    exit /b 1
)

echo.
echo === Done! ===
echo Installer output: installer\Output\
