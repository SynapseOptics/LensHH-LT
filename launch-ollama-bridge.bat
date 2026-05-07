@echo off
REM ============================================================
REM  LensHH-LT Ollama Bridge — Launch Script
REM ============================================================
REM
REM  Prerequisites:
REM    1. Ollama running: ollama serve
REM    2. A model pulled: ollama pull qwen3:8b
REM    3. LensHH.Mcp built: dotnet build src\LensHH.Mcp
REM    4. Bridge built: dotnet build src\LensHH.OllamaBridge
REM
REM  Optional environment variables:
REM    OLLAMA_MODEL  - model name (skips selection prompt)
REM    OLLAMA_URL    - Ollama API URL (default: http://localhost:11434)
REM ============================================================

dotnet run --project src\LensHH.OllamaBridge
