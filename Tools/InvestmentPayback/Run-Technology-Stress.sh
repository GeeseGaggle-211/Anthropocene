#!/usr/bin/env bash
set -euo pipefail

tool_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$tool_root/../.." && pwd)"
dotnet_bin="${DOTNET_BIN:-$repo_root/.tools/dotnet/dotnet}"
output="${1:-$repo_root/TestResults/InvestmentPaybackTechnologyStress}"
shift || true

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/tmp/anthropocene-dotnet-home}"

"$dotnet_bin" run -c Release \
  --project "$tool_root/Anthropocene.InvestmentPayback.csproj" \
  -- --technology-stress --repo "$repo_root" --out "$output" "$@"

MPLCONFIGDIR="${MPLCONFIGDIR:-/tmp/anthropocene-matplotlib}" \
  python3 "$tool_root/PlotTechnologyStress.py" \
  --input "$output" --output "$output/plots"
