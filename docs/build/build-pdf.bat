@echo off
rem Build LensHH-LT-UserGuide.pdf from docs\*.md.
rem Usage: build-pdf.bat [version-string]
rem   version-string defaults to "dev" if omitted.
rem
rem Requires:
rem   - Node.js on PATH (uses `npm` to install `marked` locally on first run).
rem   - Microsoft Edge (msedge.exe) in Program Files for --print-to-pdf.
rem
rem Output: ..\LensHH-LT-UserGuide.pdf (i.e. docs\LensHH-LT-UserGuide.pdf)

setlocal
set "VERSION=%~1"
if "%VERSION%"=="" set "VERSION=dev"

pushd "%~dp0"

echo Installing marked + lunr (if missing)...
if not exist node_modules\marked (
    call npm install --silent marked 1>nul 2>nul
    if errorlevel 1 (
        echo npm install failed.
        popd
        exit /b 1
    )
)
if not exist node_modules\lunr (
    call npm install --silent lunr 1>nul 2>nul
    if errorlevel 1 (
        echo npm install lunr failed.
        popd
        exit /b 1
    )
)

echo Rendering markdown to HTML...
call node build.js .. LensHH-LT-docs.html %VERSION%
if errorlevel 1 (
    echo HTML generation failed.
    popd
    exit /b 1
)

set "EDGE=%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"
if not exist "%EDGE%" set "EDGE=%ProgramFiles%\Microsoft\Edge\Application\msedge.exe"
if not exist "%EDGE%" (
    echo Microsoft Edge not found. Please install Edge or edit this script to point at Chrome.
    popd
    exit /b 1
)

echo Rendering HTML to PDF via headless Edge...
set "HTMLURL=file:///%CD:\=/%/LensHH-LT-docs.html"
"%EDGE%" --headless=new --disable-gpu --no-pdf-header-footer ^
    --print-to-pdf="%~dp0..\LensHH-LT-UserGuide.pdf" ^
    "%HTMLURL%"
if errorlevel 1 (
    echo PDF generation failed.
    popd
    exit /b 1
)

echo Building searchable HTML help bundle...
call node build-help.js .. ..\html\LensHH-LT-Help.html %VERSION%
if errorlevel 1 (
    echo HTML help generation failed.
    popd
    exit /b 1
)

popd
echo Docs build complete:
echo   docs\LensHH-LT-UserGuide.pdf
echo   docs\html\LensHH-LT-Help.html
endlocal
exit /b 0
