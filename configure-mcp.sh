#!/bin/bash
# ============================================================
#  LensHH-LT MCP Server — Configure for Claude Code / Desktop
# ============================================================

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

echo ""
echo "LensHH-LT MCP Server Configuration"
echo "===================================="
echo ""

# --- Find the MCP server ---
MCP_EXE=""

# On Linux/Mac, the executable is a DLL launched via dotnet
for config in Debug Release; do
    CANDIDATE="$SCRIPT_DIR/src/LensHH.Mcp/bin/$config/net8.0/LensHH.Mcp.dll"
    if [ -f "$CANDIDATE" ]; then
        MCP_EXE="$CANDIDATE"
        break
    fi
    # Windows exe (if running bash on Windows)
    CANDIDATE="$SCRIPT_DIR/src/LensHH.Mcp/bin/$config/net8.0/LensHH.Mcp.exe"
    if [ -f "$CANDIDATE" ]; then
        MCP_EXE="$CANDIDATE"
        break
    fi
done

if [ -z "$MCP_EXE" ]; then
    echo "ERROR: LensHH.Mcp not found."
    echo "Please build first: dotnet build src/LensHH.Mcp"
    exit 1
fi

echo "Found MCP server: $MCP_EXE"
echo ""

# Determine launch command
if [[ "$MCP_EXE" == *.dll ]]; then
    LAUNCH_CMD="dotnet"
    LAUNCH_ARGS="$MCP_EXE"
else
    LAUNCH_CMD="$MCP_EXE"
    LAUNCH_ARGS=""
fi

# --- Menu ---
echo "What would you like to configure?"
echo ""
echo "  1. Claude Code (recommended)"
echo "  2. Claude Desktop"
echo "  3. Both"
echo "  4. Remove from Claude Code"
echo ""
read -p "Enter choice (1-4): " CHOICE

configure_claude_code() {
    echo ""
    echo "Configuring Claude Code..."
    if [ -n "$LAUNCH_ARGS" ]; then
        claude mcp add --transport stdio --scope user lenshh-lt -- "$LAUNCH_CMD" "$LAUNCH_ARGS"
    else
        claude mcp add --transport stdio --scope user lenshh-lt -- "$LAUNCH_CMD"
    fi
    if [ $? -eq 0 ]; then
        echo "Success! LensHH-LT MCP server added to Claude Code."
        echo "Verify with: claude mcp list"
    else
        echo "ERROR: Failed. Is Claude Code CLI installed?"
    fi
}

configure_claude_desktop() {
    echo ""
    echo "Configuring Claude Desktop..."

    # Determine config location
    if [ "$(uname)" = "Darwin" ]; then
        CONFIG_DIR="$HOME/Library/Application Support/Claude"
    else
        CONFIG_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/Claude"
    fi
    CONFIG_FILE="$CONFIG_DIR/claude_desktop_config.json"

    mkdir -p "$CONFIG_DIR"

    if [ -f "$CONFIG_FILE" ]; then
        echo "Existing config found. Backing up..."
        cp "$CONFIG_FILE" "$CONFIG_FILE.bak"
    fi

    # Escape paths for JSON
    if [ -n "$LAUNCH_ARGS" ]; then
        cat > "$CONFIG_FILE" << JSONEOF
{
  "mcpServers": {
    "lenshh-lt": {
      "command": "$LAUNCH_CMD",
      "args": ["$LAUNCH_ARGS"]
    }
  }
}
JSONEOF
    else
        ESCAPED_CMD=$(echo "$LAUNCH_CMD" | sed 's/\\/\\\\/g')
        cat > "$CONFIG_FILE" << JSONEOF
{
  "mcpServers": {
    "lenshh-lt": {
      "command": "$ESCAPED_CMD",
      "args": []
    }
  }
}
JSONEOF
    fi

    echo "Success! Config written to $CONFIG_FILE"
    echo "Restart Claude Desktop to activate."
}

case "$CHOICE" in
    1) configure_claude_code ;;
    2) configure_claude_desktop ;;
    3) configure_claude_code; configure_claude_desktop ;;
    4) echo ""; echo "Removing LensHH-LT from Claude Code..."; claude mcp remove --scope user lenshh-lt; echo "Done." ;;
    *) echo "Invalid choice." ;;
esac

echo ""
