#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
results="$root/Tools/DemographicStructure/Results"
project="$root/Tools/DemographicStructure/Anthropocene.DemographicStructure.csproj"
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT

mkdir -p "$results" "$scratch/stage1" "$scratch/stage2" "$scratch/final"
dotnet run -c Release --project "$project" -- --plan="$root/Tools/DemographicStructure/SweepPlan-stage1.json" --output="$scratch/stage1"
dotnet run -c Release --no-restore --project "$project" -- --plan="$root/Tools/DemographicStructure/SweepPlan-stage2.json" --output="$scratch/stage2"
dotnet run -c Release --no-restore --project "$project" -- --plan="$root/Tools/DemographicStructure/SweepPlan-final.json" --output="$scratch/final"
python3 "$root/Tools/DemographicStructure/Summarize.py" "$results" "$scratch/stage1" "$scratch/stage2" "$scratch/final"
