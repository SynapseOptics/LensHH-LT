@echo off
REM ============================================================
REM  LensHH-LT MCP Server — Configure for Claude Code / Desktop
REM ============================================================
setlocal enabledelayedexpansion

echo.
echo  LensHH-LT MCP Server Configuration
echo  ====================================
echo.

REM --- Find the MCP server executable ---
set "MCP_EXE="

REM Check Debug build
set "CANDIDATE=%~dp0src\LensHH.Mcp\bin\Debug\net8.0\LensHH.Mcp.exe"
if exist "%CANDIDATE%" set "MCP_EXE=%CANDIDATE%"

REM Check Release build
if "%MCP_EXE%"=="" (
    set "CANDIDATE=%~dp0src\LensHH.Mcp\bin\Release\net8.0\LensHH.Mcp.exe"
    if exist "!CANDIDATE!" set "MCP_EXE=!CANDIDATE!"
)

REM Check published output
if "%MCP_EXE%"=="" (
    set "CANDIDATE=%~dp0src\LensHH.Mcp\bin\Release\net8.0\publish\LensHH.Mcp.exe"
    if exist "!CANDIDATE!" set "MCP_EXE=!CANDIDATE!"
)

if "%MCP_EXE%"=="" (
    echo  ERROR: LensHH.Mcp.exe not found.
    echo  Please build first: dotnet build src\LensHH.Mcp
    echo.
    pause
    exit /b 1
)

echo  Found MCP server: %MCP_EXE%
echo.

REM --- Menu ---
echo  What would you like to configure?
echo.
echo    1. Claude Code (recommended)
echo    2. Claude Desktop
echo    3. Both
echo    4. Remove from Claude Code
echo.
set /p CHOICE="  Enter choice (1-4): "

if "%CHOICE%"=="1" goto :claude_code
if "%CHOICE%"=="2" goto :claude_desktop
if "%CHOICE%"=="3" goto :both
if "%CHOICE%"=="4" goto :remove
echo  Invalid choice.
goto :end

:claude_code
echo.
echo  Configuring Claude Code...
claude mcp add --transport stdio --scope user lenshh-lt -- "%MCP_EXE%"
if %errorlevel%==0 (
    echo  Success! LensHH-LT MCP server added to Claude Code.
    echo  Verify with: claude mcp list
) else (
    echo  ERROR: Failed. Is Claude Code CLI installed?
    echo  Install: https://docs.anthropic.com/en/docs/claude-code
)
goto :end

:claude_desktop
echo.
echo  Configuring Claude Desktop...

set "CONFIG_DIR=%APPDATA%\Claude"
set "CONFIG_FILE=%CONFIG_DIR%\claude_desktop_config.json"

if not exist "%CONFIG_DIR%" mkdir "%CONFIG_DIR%"

REM Escape backslashes for JSON
set "MCP_PATH=%MCP_EXE:\=\\%"

if exist "%CONFIG_FILE%" (
    echo  Existing config found. Backing up to claude_desktop_config.json.bak
    copy /Y "%CONFIG_FILE%" "%CONFIG_FILE%.bak" >nul
)

REM Write config (will overwrite mcpServers — warn user)
echo  Writing config to %CONFIG_FILE%
echo  NOTE: This will set the mcpServers section. If you have other
echo  MCP servers configured, please manually merge the JSON.
echo.

(
echo {
echo   "mcpServers": {
echo     "lenshh-lt": {
echo       "command": "%MCP_PATH%",
echo       "args": []
echo     }
echo   }
echo }
) > "%CONFIG_FILE%"

echo  Success! Restart Claude Desktop to activate.
goto :end

:both
call :claude_code
call :claude_desktop
goto :end

:remove
echo.
echo  Removing LensHH-LT from Claude Code...
claude mcp remove --scope user lenshh-lt
echo  Done.
goto :end

:end
echo.
pause
