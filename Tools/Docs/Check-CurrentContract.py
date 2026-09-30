#!/usr/bin/env python3
"""Generate and validate the current-contract index without building simulation."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
INDEX = ROOT / "Docs" / "CurrentSimulationContract.md"
CONFIG = ROOT / "Assets" / "_Anthropocene" / "Resources" / "OpeningConfig.json"
MODEL = ROOT / "Assets" / "_Anthropocene" / "Simulation" / "NeolithicConfig.cs"
SIM_MODEL = ROOT / "Assets" / "_Anthropocene" / "Simulation" / "Model.cs"
PROGRAM = ROOT / "Tools" / "Headless" / "Program.cs"
START = "<!-- CURRENT_CONFIG_TABLE:BEGIN -->"
END = "<!-- CURRENT_CONFIG_TABLE:END -->"

# JSON key -> (unit / meaning, status). Keep this registry small and purposeful:
# it catches prose drift for current values that have historically conflicted.
FIELDS = [
    ("ReferenceTurnYears", "years per Neolithic reference turn", "reference scale"),
    ("PlanningHorizonYears", "years", "shipped forecast horizon"),
    ("LandBiomassMultiplier", "relative land biomass multiplier", "shipped new-world calibration"),
    ("LandFoodRegeneration", "fraction per legacy BaseDays profile", "shipped land recovery input"),
    ("LandSafeUseRatio", "fraction of sustainable production", "shipped land extraction threshold"),
    ("RichGatherableFraction", "fraction", "shipped new-world accessibility"),
    ("GrasslandGatherableFraction", "fraction", "shipped new-world accessibility"),
    ("AridGatherableFraction", "fraction", "shipped new-world accessibility"),
    ("TundraGatherableFraction", "fraction", "shipped new-world accessibility"),
    ("MaterialReserveHorizonYears", "years", "shipped new-world duration"),
    ("UsefulFinishedGoodsReserveYears", "years", "shipped new-world duration"),
    ("GrowthReserveYears", "years", "shipped physical reserve target"),
    ("MinimumStorageYears", "years", "shipped physical storage floor"),
    ("MaximumStorageYears", "years", "shipped physical storage ceiling"),
    ("DecayHalfLifeYears", "years", "shipped cultivation capital half-life"),
    ("CultivationMarginalEffortMultiplierPerTenth", "multiplier per 0.1 q above baseline", "shipped new-world calibration"),
    ("PrimitiveAgricultureSupportedIntensity", "q", "shipped primitive intensity anchor"),
    ("StoneFarmToolsSupportedIntensity", "q", "shipped Stone Farm Tool tier"),
    ("FarmToolFarmerYearsPerUnit", "Farmer-years per tool", "shipped consumption calibration"),
    ("PotStorageFoodPerUnit", "food units per Pot", "shipped storage calibration"),
    ("PotDecayPerReferenceTurn", "fraction per 20-year reference turn", "shipped decay calibration"),
    ("CultivationRegenFlux", "managed recovery coefficient", "shipped cultivation calibration"),
    ("StoneHerdingToolAnimalsPerHerder", "animals per Herder", "shipped Husbandry calibration"),
    ("HerdingToolCoverageYearsPerUnit", "coverage years per tool unit", "shipped duration calibration"),
    ("SocialCohesionExponent", "exponent beta in S(N)", "shipped QoL cohesion calibration"),
    ("CohesionScalePaleolithic", "people (N_s)", "shipped QoL cohesion calibration"),
    ("CohesionScaleNomadic", "people (N_s)", "shipped QoL cohesion calibration"),
    ("CohesionScaleAgricultural", "people (N_s)", "shipped QoL cohesion calibration"),
    ("ValueCurveSteepness", "dimensionless lambda in exp(-lambda c)", "shipped value-rule calibration (used by the Value planner rule, the default PlannerFulfilment; ignored under Uniform)"),
    ("DiscountRatePerYear", "per year (r)", "shipped value-rule calibration (used by the Value planner rule, the default PlannerFulfilment; ignored under Uniform)"),
    ("PersonLifeYears", "years (L_person = 1 / (1 / PersonLifeYears + r))", "shipped value-rule calibration (used only by the Value rule's expansion valuation; ignored under Uniform)"),
    ("FoodRiskWeight", "fraction of food-security value (rho)", "risk placeholder (reserve-refill food in the expansion valuation; Pots in WP4a); Value rule only, calibration target once disasters exist"),
]

TESTS = [
    ("Tools/Headless/Issue26ForecastChecks.cs", "--issue26-forecast-only"),
    ("Tools/Headless/Issue28FoodTimebaseChecks.cs", "--issue28-food-timebase-only"),
    ("Tools/Headless/Issue27EcologyChecks.cs", "--issue27-ecology-only"),
    ("Tools/Headless/HorizonEcologyChecks.cs", "--horizon-only"),
    ("Tools/Headless/NeolithicChecks.cs", "--neolithic-only"),
    ("Tools/Headless/ExecutionChecks.cs", "--economy-only"),
]
API_ONLY_FIELDS = {"CultivationRegenFlux"}


def current_value(config: dict, name: str):
    if name in config.get("Neolithic", {}):
        return config["Neolithic"][name]
    if name in config:
        return config[name]
    return None


def render_table(config: dict) -> str:
    rows = [
        "| Setting | OpeningConfig value | C# API default | Unit / interpretation | Status |",
        "| --- | ---: | ---: | --- | --- |",
    ]
    model_text = MODEL.read_text(encoding="utf-8") + SIM_MODEL.read_text(encoding="utf-8")
    # Read field initializers only. Migration assignments inside callbacks are
    # legacy defaults, not the defaults of a newly constructed config.
    declarations = "\n".join(re.findall(r"public\s+double\s+([^;\n]+);", model_text))
    derived = {"GrowthReserveYears": "GrowthReserveTurns", "MinimumStorageYears": "MinimumStorageGenerations",
               "MaximumStorageYears": "MaximumStorageGenerations"}
    def default_value(name):
        match = re.search(rf"\b{re.escape(name)}\s*=\s*(\.\d+|\d+(?:\.\d*)?)(?:[dDfF])?\b", declarations)
        if match:
            return float(match.group(1))
        if name in derived and f"{name} = {derived[name]} * referenceYears;" in model_text:
            if "double referenceYears = Neolithic.ReferenceTurnYears;" not in model_text:
                raise ValueError("Physical-food constructor reference changed; audit its derivation")
            return default_value(derived[name]) * default_value("ReferenceTurnYears")
        raise KeyError(f"C# API default for {name} was not found")
    for name, unit, status in FIELDS:
        api_default = default_value(name)
        value = current_value(config, name)
        if value is None:
            if name not in API_ONLY_FIELDS:
                raise KeyError(f"OpeningConfig.json is missing required current setting {name}")
            value = api_default
            status += "; API default (not serialized in OpeningConfig)"
        comparable = float(value)
        row_status = status
        if abs(api_default - comparable) > max(1e-10, abs(comparable) * 1e-10):
            raise ValueError(f"Undocumented OpeningConfig/API default mismatch: {name} {comparable} != {api_default}")
        if isinstance(value, float):
            shown = format(value, ".12g")
        else:
            shown = str(value)
        rows.append(f"| `{name}` | {shown} | {format(api_default, '.12g')} | {unit} | {row_status} |")
    return "\n".join(rows)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--write", action="store_true", help="refresh the generated table in the index")
    args = parser.parse_args()
    errors: list[str] = []
    config = json.loads(CONFIG.read_text(encoding="utf-8"))
    expected = render_table(config)
    content = INDEX.read_text(encoding="utf-8")
    if content.count(START) != 1 or content.count(END) != 1:
        errors.append("index must contain exactly one generated-table marker pair")
    else:
        pattern = re.compile(re.escape(START) + r".*?" + re.escape(END), re.S)
        replacement = f"{START}\n{expected}\n{END}"
        updated = pattern.sub(replacement, content)
        if args.write:
            INDEX.write_text(updated, encoding="utf-8", newline="\n")
            content = updated
            print("Updated generated config table.")
        elif updated != content:
            errors.append("generated config table is stale; run with --write")

    model_text = MODEL.read_text(encoding="utf-8") + SIM_MODEL.read_text(encoding="utf-8")
    for name, _, _ in FIELDS:
        if not re.search(rf"\b{re.escape(name)}\b", model_text):
            errors.append(f"NeolithicConfig.cs no longer declares or migrates {name}")

    program_text = PROGRAM.read_text(encoding="utf-8")
    for relative, flag in TESTS:
        if not (ROOT / relative).is_file():
            errors.append(f"claimed test file is missing: {relative}")
        if flag not in program_text:
            errors.append(f"claimed headless entry point is missing from Program.cs: {flag}")

    for target in re.findall(r"\[[^]]+\]\(([^)]+)\)", content):
        path = target.split("#", 1)[0]
        if not path or "://" in path or path.startswith("mailto:"):
            continue
        if not (INDEX.parent / path).resolve().is_file():
            errors.append(f"broken relative document link: {target}")

    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        return 1
    print(f"Current contract checks passed ({len(FIELDS)} JSON/API config values, {len(TESTS)} test claims, document links).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
