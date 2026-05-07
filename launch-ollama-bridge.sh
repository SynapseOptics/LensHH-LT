#!/bin/bash
# LensHH-LT Ollama Bridge — Launch Script
#
# Prerequisites:
#   1. Ollama running: ollama serve
#   2. A model pulled: ollama pull qwen3:8b
#   3. LensHH.Mcp built: dotnet build src/LensHH.Mcp
#   4. Bridge built: dotnet build src/LensHH.OllamaBridge
#
# Optional environment variables:
#   OLLAMA_MODEL  - model name (skips selection prompt)
#   OLLAMA_URL    - Ollama API URL (default: http://localhost:11434)

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
dotnet run --project "$SCRIPT_DIR/src/LensHH.OllamaBridge"
