#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

exec dotnet run -c Release \
  --project "$project_root/Tools/Headless/Anthropocene.Headless.csproj" \
  -- "$@"
