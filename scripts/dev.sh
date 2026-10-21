#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [ -x /workspace/tools/dotnet/dotnet ]; then export PATH="/workspace/tools/dotnet:$PATH"; fi
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/workspace/tools/dotnet-home}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-/workspace/tools/nuget}"
export XDG_CACHE_HOME="${XDG_CACHE_HOME:-/tmp/frame-cache}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet run --project VideoEditor.csproj --urls "${ASPNETCORE_URLS:-http://0.0.0.0:5000}"
