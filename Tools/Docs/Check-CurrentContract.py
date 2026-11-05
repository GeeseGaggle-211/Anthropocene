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
    ("FieldDomesticationEnabled", "boolean: cultivated-village attraction and sustained peaceful wild-herd acquisition", "new worlds enabled; absent saved field stays false (Docs/FieldDomestication.md)"),
    ("FieldDetectionRadius", "hexes from a village within which one eligible wild herd is selected", "default 6; actual contact also requires village vision (2 hexes); validated 1-12"),
    ("FieldContactTurns", "consecutive visible peaceful turns for the same herd and Society", "default 6 (120 years at the default horizon), selected against 5 by the issue-12 sweep; validated 2-100"),
    ("SharedCropHerdAllocationEnabled", "boolean: joint food ledger, field-operation and bounded herd portfolio under the Value rule", "plan of record since 5 October 2026: true for new worlds; saves without the field load false"),
    ("NeolithicFishingEnabled", "boolean: resident Watercraft Fishing and its shared goods-economy capability", "compatibility gate: false in the general opening and in saves missing the field"),
    ("CorrectedAquaticSeasonalPressureEnabled", "boolean: capped seasonal-pressure allocation for aquatic claims", "separate compatibility gate: false in the general opening and in saves missing the field; required by resident Fishing"),
    ("ElderSplitObjective", "kernel-scored Elder split objective: 0 person-years, 1 QoL-weighted person-years, 2 lexicographic (person-years within tolerance, then QoL), 3 terminal-population welfare, 4 deaths averted", "plan of record since 17 October 2026: 2 with `ElderKernelSplit`, `ElderMixedRoutes` and `SurplusFundedBirths` true (booleans, not listed); saves without the fields load the split and surplus births off (Docs/ClothingCoverageAndSurplusBirths.md)"),
    ("ElderSplitLexTolerance", "fraction of the best candidate person-years a lexicographic Elder split may give up for higher QoL", "plan of record since 17 October 2026: 0.05, the measured best of the objective sweep"),
    ("ReferenceTurnYears", "years per Neolithic reference turn", "reference scale"),
    ("MinimumCampSpacing", "hexes between resolved camp destinations", "shipped placement rule (saves without the field load 3)"),
    ("ContactExpiryTurns", "turns before an unrefreshed settlement contact is forgotten", "shipped contact-knowledge rule (saves without the field load 5)"),
    ("HinterlandReferencePopulation", "people at which a stationary camp's hinterland cap equals the vision radius (R_cap = VisionRadius sqrt(N / reference))", "shipped hinterland lever; equals StartingPopulation (area worked proportional to people); saves without it load 24"),
    ("HinterlandRingsPerTurn", "rings of hinterland a camp gains per turn it stays on its tile (a move resets to VisionRadius)", "shipped hinterland lever (Docs/ContactKnowledge.md); saves without it load 1"),
    ("HinterlandMaxRadius", "largest hinterland radius in hexes, validated VisionRadius to 8 (at most 217 tiles marked per camp per turn)", "shipped hinterland lever, performance-bounded; saves without it load 6"),
    ("ExploredMemoryTurns", "turns after which an explored 8x8 block not refreshed by vision or a hinterland is forgotten", "shipped decaying-memory rule, same order as ContactExpiryTurns (100 years); saves without it load 5"),
    ("SecondaryTechnologyThreshold", "post-breakthrough (Neolithic) practice points that grant a secondary technology; Paleolithic practice counts toward the first breakthrough only", "shipped discovery rule (Docs/HusbandrySettling.md); equals ExperienceThreshold; saves without it load 225"),
    ("TradePracticePerValue", "practice points credited to the receiving Society per unit of traded goods value (TechnologyDiscovery.RecordTradePractice)", "trade hook, called by food-trade settlement; 0 = inert; saves without it load 0"),
    ("SettledHerdGrowthPenalty", "fraction of a physically mobile herd-bearing camp's forecast herd food forfeited by becoming a village (legacy Value rule's settling cost)", "shipped settling lever from the measured 1.6x mobile-to-stationary herd support (Docs/MobileHusbandryValidation.md); first technology is irrelevant; saves without it load 0.35"),
    ("FissionGainThreshold", "expected-gain threshold G*(Neutral): the relative rise of the stayers' QoL a split must deliver (WP10c)", "shipped fission lever, analytically calibrated to the design onset sizes (Docs/Fission.md); saves without it load with fission disabled; `FissionEnabled` (plan of record: true by default and in OpeningConfig; saves without it load false) is a boolean, not listed"),
    ("FissionGainDiscourage", "expected-gain threshold G*(Discourage)", "shipped fission lever, calibrated with the 15 percent daughter; not monotone in policy because the policy also sets the daughter fraction"),
    ("FissionGainEncourage", "expected-gain threshold G*(Encourage)", "shipped fission lever, calibrated with the 40 percent daughter"),
    ("FissionSearchFraction", "fraction of G* at which a camp without a valid cached destination searches", "retained fission lever; Physical-v1 applies it only to a valid optimistic upper-bound request, never as a separate acceptance gate"),
    ("FissionDaughterMargin", "QoL the daughter may fall below the parent's before the split is blocked", "shipped fission lever"),
    ("FissionHousingRelief", "weight of the stayers' housing relief in the expected gain (1 = all, 0 = cohesion and clothing only)", "shipped fission lever; 0 by user decision (onset near the design sizes), 1 counts all housing relief and splits housing-short villages at a median of 141 (Docs/Fission.md)"),
    ("FissionDaughterBuilderShare", "share of a daughter's people counted as Builders when estimating the shelter she assembles in her first turn (Fission.DaughterShelter)", "shipped fission lever; 0.05 is a heavy but realistic crew against the measured planner Builder share (P90 4.5 percent; Docs/Fission.md); 0 credits carried shelter only; saves without it load the default"),
    ("FissionRadius", "hexes from the camp searched for a destination (r_fiss)", "shipped fission lever; 4 by measurement (3 saw only the distance-3 ring; 5 costs 2.8x the candidates for little extra; Docs/Fission.md), performance-bounded (validated 1-5)"),
    ("FissionMinimumDaughter", "people in the smallest daughter cohort", "shipped fission lever, uncalibrated"),
    ("FissionDaughterCooldownTurns", "additional complete post-founding turns before a Physical-v1 daughter may fission", "shipped daughter-maturity guard; 1 makes the daughter first eligible at the close of her second full economic/demographic turn; 0 reproduces next-turn eligibility; legacy-demography daughters remain immediately eligible"),
    ("ImmigrationRadius", "hexes from the camp searched for known destination camps (r_imm)", "shipped immigration lever, performance-bounded (validated 1-5); `ImmigrationEnabled` (plan of record: true by default and in OpeningConfig; saves without it load false) is a boolean, not listed"),
    ("ImmigrationQualityWeight", "weight w_Q on the destination-minus-source QoL gap", "shipped immigration lever (Docs/Immigration.md); saves without it load with immigration disabled"),
    ("ImmigrationDistanceCost", "utility per hex between source and destination (K_distance)", "shipped immigration lever, uncalibrated design baseline"),
    ("ImmigrationAttachmentCost", "utility lost by leaving the home camp (K_attachment)", "shipped immigration lever, uncalibrated design baseline"),
    ("ImmigrationToSedentaryCost", "utility barrier for a nomadic camp moving to a village (K_lifestyle)", "shipped immigration lever, design sweep 0 / 0.05 / 0.10"),
    ("ImmigrationToNomadicCost", "utility barrier for a village moving to a nomadic camp (K_lifestyle)", "shipped immigration lever, design sweep 0.10 / 0.15 / 0.25"),
    ("ImmigrationDeadband", "utility a move must exceed, and the ramp width of the group size (theta_imm)", "shipped immigration lever, design sweep 0.05 / 0.075 / 0.10"),
    ("ImmigrationMaxFraction", "largest fraction of a source's residents that leaves in one turn", "shipped immigration lever, uncalibrated"),
    ("ImmigrationMinimumGroup", "people in the smallest migrating group", "shipped immigration lever, uncalibrated"),
    ("ImmigrationSourceFloor", "residents a source always keeps", "shipped immigration lever, uncalibrated"),
    ("ImmigrationReturnCooldownTurns", "turns after a camp sends people during which a flow back to it pays the return penalty", "shipped immigration hysteresis lever (WP10b-2), retained by the matched v1.1 calibration sweep; 0 disables"),
    ("ImmigrationReturnPenalty", "utility added to the deadband for a flow back to a camp that just sent people, decaying linearly to 0 over the cooldown", "shipped immigration hysteresis lever (WP10b-2), retained after the matched v1.1 sweep; 0.20 remains exploratory pending counterfactual and held-out evidence; Docs/DemographyQoLValidation-v1.1.md"),
    ("TradeRange", "hexes between a camp and a remembered partner's recorded tile within which it may send a requisition", "shipped trade lever (Docs/Trade.md), validated 1-20; `TradeEnabled` (plan of record: true by default and in OpeningConfig) is a boolean, not listed; saves without these fields load with trade disabled"),
    ("TradeRequisitionsPerCamp", "requisitions a camp sends per turn (K), nearest partners first", "shipped trade lever, performance bound O(camps K)"),
    ("TradeListLength", "goods in a requisition's ranked want and give lists (W); the responder evaluates W x W pairs", "shipped trade lever, performance bound O(camps K W^2); food levers `TradeFoodEnabled` (true) and `TradeFoodSafeOnly` (false) are booleans, not listed"),
    ("TradeExportLabourShare", "fraction of a seller's resident turn that goods made to order for export may take (capacity bound; also bounded by spare food inputs)", "shipped 0 (stock-only trade until the planner prices tool time, Docs/PlannerTradeStorageDesign.md §3.11); 0.25 is the measured make-to-order setting"),
    ("GoodsBaseStoragePerResident", "goods units each resident stores without Pots", "phase S storage lever (Docs/PlannerTradeStorageDesign.md), uncalibrated; only while GoodsStorageEnabled (false by default and in OpeningConfig; saves without it load off)"),
    ("GoodsStorageSpacePerUnit", "food units of Pot space one stored goods unit takes", "phase S storage lever, uncalibrated; the same for every counted good"),
    ("TradeMargin", "gain over loss a side requires, (1 + m); kin use m (1 - affinity)", "shipped trade lever, uncalibrated design baseline"),
    ("TradeDistanceCostPerHex", "fraction of the received good's value lost per hex of distance", "shipped trade lever, uncalibrated design baseline"),
    ("BaseBirthRate", "annual continuous crude birth rate; `b = BaseBirthRate * realized food satisfaction` in the physical model", "locked v1.1 coefficient; old saves use the same field in their legacy model"),
    ("QoLMortalityElasticity", "legacy-only exponent in `d(Q) = d0 Q^-a`", "retained solely for `LegacyQoLNetGrowth`; it is not read by physical survival"),
    ("ExposureMortalityRate", "annual uncovered exposure hazard; clothing and housing each remove 45% linearly", "physical v1.1 coefficient"),
    ("CrowdingMortalityRateAt1000", "annual crowding hazard at 1,000 before mitigation", "physical v1.1 coefficient; exponential slope `CrowdingExponent = 0.00001`"),
    ("DiseaseMortalityRateAt1000", "annual disease hazard at 1,000 before mitigation", "physical v1.1 coefficient; exponential slope `DiseaseExponent = 0.000027161883008306556`"),
    ("DemographicLogBandWidth", "deterministic global width in log population", "v1.1 analytical propagator; nonconvergence fails explicitly"),
    ("MinimumHerdHerders", "herders the Value planner always keeps managed for a camp with animals (the seed herd, 1 herder = AnimalsPerHerder animals); the herd-labour lever `HerdLabourEnabled` (plan of record: true in OpeningConfig and the API default; saves without it load false) is a boolean, not listed", "shipped herd-labour seed floor (Docs/HerdLabour.md); saves without it load the default"),
    ("PlanningHorizonYears", "years", "shipped forecast horizon"),
    ("LandBiomassMultiplier", "relative land biomass multiplier", "shipped new-world calibration"),
    ("LandFoodRegeneration", "fraction per legacy BaseDays profile", "shipped land recovery input"),
    ("LandSafeUseRatio", "fraction of sustainable production", "shipped land extraction threshold"),
    ("CropMoistureReference", "local soil moisture at which crop suitability reaches one", "branch crop calibration; 0 disables the response in older saves"),
    ("CropMoistureExponent", "exponent in min(1, (soil moisture / reference)^exponent)", "branch crop calibration; applies only to cultivated food and managed renewal"),
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
    ("FishingToolFisherYearsPerUnit", "harvest-Fisher-years covered per Fishing Tool unit", "Fishing Tool service; coverage affects harvest only; absent saves receive 20"),
    ("FishingToolHarvestMultiplier", "catch multiplier for tool-covered harvest work", "Fishing Tool benefit; covered catch is 1.5x and tool service has no biology effect"),
    ("PotStorageFoodPerUnit", "food units per Pot", "shipped storage calibration"),
    ("PotDecayPerReferenceTurn", "fraction per 20-year reference turn", "shipped decay calibration"),
    ("CultivationRegenFlux", "managed recovery coefficient", "shipped cultivation calibration"),
    ("StoneHerdingToolAnimalsPerHerder", "animals per Herder", "shipped Husbandry calibration"),
    ("HerdingToolCoverageYearsPerUnit", "coverage years per tool unit", "shipped duration calibration"),
    ("SocialCohesionExponent", "exponent beta in S(N)", "shipped QoL cohesion calibration"),
    ("CohesionBandScale", "people (N_band: the cohesion scale of a camp with no housing)", "shipped derived-cohesion calibration (Docs/SocialCohesion.md); replaces the technology-labelled CohesionScale* fields, which old saves carry and ignore"),
    ("CohesionHousingGain", "dimensionless k in N_s = N_band (1 + k H)", "shipped derived-cohesion calibration (Docs/SocialCohesion.md); saves without it load the default"),
    ("LightHousingPermanence", "dimensionless p_tent: how much of a tent-sheltered person's housing counts in H (0 to 1)", "shipped derived-cohesion calibration (Docs/SocialCohesion.md); saves without it load the default"),
    ("LightHousingPeoplePerUnit", "people sheltered per Light Housing unit (tent); fixed Housing shelters HousingPeoplePerUnit = 4", "shipped tent capacity (Docs/SocialCohesion.md); saves without it load the default"),
    ("ValueCurveSteepness", "dimensionless lambda in exp(-lambda c)", "shipped value-rule calibration (used by the Value planner rule, the default PlannerFulfilment; ignored under Uniform)"),
    ("DiscountRatePerYear", "per year (r)", "shipped value-rule calibration (used by the Value planner rule, the default PlannerFulfilment; ignored under Uniform)"),
    ("PersonLifeYears", "years (L_person = 1 / (1 / PersonLifeYears + r))", "shipped value-rule calibration (used only by the Value rule's expansion valuation; ignored under Uniform)"),
    ("FoodRiskWeight", "fraction of food-security value (rho)", "risk placeholder (reserve-refill food in the expansion valuation; Pots in WP4a); Value rule only, calibration target once disasters exist"),
]

TESTS = [
    ("Tools/Headless/AquaticSeasonalPressureChecks.cs", "--aquatic-pressure-only"),
    ("Tools/Headless/FishingObservationChecks.cs", "--fishing-observation-only"),
    ("Tools/Headless/FishingLongRunChecks.cs", "--fishing-50-no-movement"),
    ("Tools/Headless/FishingLongRunChecks.cs", "--fishing-50-all-features"),
    ("Tools/Headless/Issue26ForecastChecks.cs", "--issue26-forecast-only"),
    ("Tools/Headless/Issue28FoodTimebaseChecks.cs", "--issue28-food-timebase-only"),
    ("Tools/Headless/Issue27EcologyChecks.cs", "--issue27-ecology-only"),
    ("Tools/Headless/HorizonEcologyChecks.cs", "--horizon-only"),
    ("Tools/Headless/NeolithicChecks.cs", "--neolithic-only"),
    ("Tools/Headless/ExecutionChecks.cs", "--economy-only"),
    ("Tools/Headless/CampSpacingChecks.cs", "CampSpacingChecks.Run"),
    ("Tools/Headless/ContactKnowledgeChecks.cs", "ContactKnowledgeChecks.Run"),
    ("Tools/Headless/FissionChecks.cs", "FissionChecks.Run"),
    ("Tools/Headless/QoLCohesionChecks.cs", "QoLCohesionChecks.Run"),
    ("Tools/Headless/QoLMortalityChecks.cs", "QoLMortalityChecks.Run"),
    ("Tools/Headless/DemographyChecks.cs", "DemographyChecks.Run"),
    ("Tools/Headless/DemographyRegressionChecks.cs", "DemographyRegressionChecks.Run"),
    ("Tools/Headless/HerdLabourChecks.cs", "HerdLabourChecks.Run"),
    ("Tools/Headless/ImmigrationChecks.cs", "ImmigrationChecks.Run"),
    ("Tools/Headless/SettlingChecks.cs", "SettlingChecks.Run"),
    ("Tools/Headless/SharedAllocationChecks.cs", "SharedAllocationChecks.Run"),
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
    declarations = "\n".join(re.findall(r"public\s+(?:double|int|bool)\s+([^;\n]+);", model_text))
    derived = {"GrowthReserveYears": "GrowthReserveTurns", "MinimumStorageYears": "MinimumStorageGenerations",
               "MaximumStorageYears": "MaximumStorageGenerations"}
    def default_value(name):
        match = re.search(rf"\b{re.escape(name)}\s*=\s*(true|false|\.\d+|\d+(?:\.\d*)?)(?:[dDfF])?\b", declarations)
        if match:
            literal = match.group(1)
            return literal == "true" if literal in ("true", "false") else float(literal)
        # Some additive compatibility gates have their default in the
        # SimulationConfig constructor rather than in the field declaration.
        match = re.search(rf"\b{re.escape(name)}\s*=\s*(true|false|\.\d+|\d+(?:\.\d*)?)(?:[dDfF])?\b", model_text)
        if match:
            literal = match.group(1)
            return literal == "true" if literal in ("true", "false") else float(literal)
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
        if isinstance(value, bool):
            shown = str(value).lower()
        elif isinstance(value, float):
            shown = format(value, ".12g")
        else:
            shown = str(value)
        api_shown = str(api_default).lower() if isinstance(api_default, bool) else format(api_default, ".12g")
        rows.append(f"| `{name}` | {shown} | {api_shown} | {unit} | {row_status} |")
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
