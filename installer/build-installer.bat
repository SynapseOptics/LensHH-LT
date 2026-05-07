@echo off
setlocal
cd /d "%~dp0\.."

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
rem the installer version without duplicating the constant.
set "PDF_VERSION=dev"
for /f "tokens=2 delims==" %%A in ('findstr /b "#define MyAppVersion" installer\LensHH-LT.iss') do (
    for /f "tokens=* delims= " %%B in ("%%~A") do set "PDF_VERSION=%%~B"
)
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
pause
