#!/usr/bin/env python3
"""Matched current-source #41 cases; no simulator tuning or RL checkout."""
import argparse
import pathlib
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument("--dotnet", default="dotnet")
parser.add_argument("--output", default="TestResults/FishingUnity")
args = parser.parse_args()
root = pathlib.Path(__file__).resolve().parents[2]
output = pathlib.Path(args.output).resolve()
output.mkdir(parents=True, exist_ok=True)
subprocess.run([args.dotnet, "build", "-c", "Release",
                "Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj"], cwd=root, check=True)
revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
binary = root / "Tools/PerformanceAudit/bin/Release/net8.0/Anthropocene.PerformanceAudit.dll"
for seed in (23, 101):
    for variant, enabled in (("control", 0), ("fishing", 1)):
        stem = output / f"{variant}-{seed}"
        if stem.with_suffix(".json").exists():
            raise SystemExit(f"Refusing to overwrite existing evidence: {stem}.json")
        command = [args.dotnet, str(binary), "--players=80", f"--seed={seed}",
                   "--shape=Continents", "--production-config=1", "--turns=12",
                   "--post-opening-turns=40", "--workers=4", "--partition-size=64",
                   "--warmup=0", "--details=1", "--metrics=1", "--fingerprints=0",
                   f"--revision={revision}",
                   f"--set=NeolithicFishingEnabled={enabled},CorrectedAquaticSeasonalPressureEnabled={enabled},NeolithicSailingEnabled=0",
                   f"--output={stem}.json"]
        with stem.with_suffix(".log").open("w") as log:
            subprocess.run(command, cwd=root, stdout=log, stderr=subprocess.STDOUT, check=True)
        print(f"Completed {variant} seed {seed}", flush=True)
subprocess.run(["python3", str(root / "Tools/PerformanceAudit/Summarize-FishingUnity.py"),
                str(output)], check=True)
