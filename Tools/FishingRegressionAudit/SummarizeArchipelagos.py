#!/usr/bin/env python3
"""Reduce complete matched Archipelagos runs without treating first tech as exclusive subsistence."""
import hashlib
import json
import pathlib
import statistics
import sys

root = pathlib.Path(__file__).resolve().parents[2]
directory = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else root / "TestResults/FishingArchipelagos")
output = pathlib.Path(sys.argv[2] if len(sys.argv) > 2 else root / "Docs/FishingArchipelagosEvidence.json")
cases = []

for seed in (23, 101):
    for variant in ("control", "fishing"):
        path = directory / f"{variant}-{seed}.jsonl"
        meta = json.loads(path.with_suffix(".jsonl.metadata.json").read_text())
        assert meta["artifactSha256"].lower() == hashlib.sha256(path.read_bytes()).hexdigest()
        assert meta["shape"] == "Archipelagos" and meta["seed"] == seed
        assert (meta["Width"], meta["Height"], meta["Players"]) == (1024, 512, 80)
        assert meta["requestedPostOpeningTurns"] == meta["measuredPostOpeningTurns"] == 40
        assert meta["endTurn"] - meta["openingCompletedTurn"] == 40
        assert not meta["NeolithicSailingEnabled"]
        assert meta["NeolithicFishingEnabled"] == meta["CorrectedAquaticSeasonalPressureEnabled"] == (variant == "fishing")
        rows = [json.loads(line) for line in path.read_text().splitlines()]
        post = [r for r in rows if r["turn"] > meta["openingCompletedTurn"]]
        assert len({r["turn"] for r in post}) == 40
        final = [r for r in rows if r["turn"] == meta["endTurn"]]
        assert len(final) == len({r["Id"] for r in final})
        population = sum(r["Population"] for r in final)
        pure = [r for r in post if not r["technologies"][1] and not r["technologies"][2]]
        granted = [r for r in pure if r["allocation"] and r["allocation"]["Ceilings"].get("Cloth", 0) > 1e-6]
        def made(row, good):
            return sum(p["Output"] for p in row["production"] if p["Good"] == good)
        fish_food = sum(r["receipts"].get("FisheryHarvest", 0) for r in post)
        case = {
            "seed": seed, "variant": variant, "metadata": meta,
            "finalSettlements": len(final), "finalResidents": population,
            "weightedQoL": sum(r["Population"] * r["QoL"] for r in final) / population,
            "medianSettlementPopulation": statistics.median(r["Population"] for r in final),
            "finalFounders": sum(r["parentSociety"] <= 0 for r in final),
            "finalFounderResidents": sum(r["Population"] for r in final if r["parentSociety"] <= 0),
            "finalWithCultivation": sum(r["technologies"][2] for r in final),
            "finalWithHusbandry": sum(r["technologies"][1] for r in final),
            "finalActualResidentFishing": sum(r["receipts"].get("FisheryHarvest", 0) > 1e-6 for r in final),
            "finalMixedFarmerFisher": sum(any(a["Role"] == "Farmer" and a["Population"] > 0 for a in r["assignments"])
                                         and any(a["Role"] == "Fisher" and a["Population"] > 0 for a in r["assignments"]) for r in final),
            "postOpeningResidentFishingFood": fish_food,
            "postOpeningFissions": sum(r["demography"] is not None and r["demography"]["SplitOff"] > 0 for r in post),
            "pureWatercraftCampTurns": len(pure), "pureWatercraftClothGrantTurns": len(granted),
            "pureWatercraftClothGrantWithActualClothTurns": sum(made(r, 2) > 1e-6 for r in granted),
            "pureWatercraftClothProducingCamps": len({r["Id"] for r in pure if made(r, 2) > 1e-6}),
            "pureWatercraftCloth": sum(made(r, 2) for r in pure),
            "pureWatercraftHandTools": sum(made(r, 0) for r in pure),
            "pureWatercraftHousing": sum(made(r, 4) for r in pure),
            "pureWatercraftMaterialWorkerIntervals": sum(t["WorkerIntervals"] for r in pure for t in r["materials"]),
            "foodReceipts": {},
        }
        for r in post:
            for kind, amount in r["receipts"].items():
                case["foodReceipts"][kind] = case["foodReceipts"].get(kind, 0) + amount
        cases.append(case)

comparisons = []
for seed in (23, 101):
    off, on = [c for c in cases if c["seed"] == seed]
    def opening_fish(c):
        return next(x for x in c["metadata"]["openingCensus"] if x["technology"] == "Watercraft")
    assert opening_fish(off)["founders"] == opening_fish(on)["founders"]
    comparisons.append({"seed": seed, "openingWatercraftFounders": opening_fish(on)["founders"],
                        "populationChangePercent": 100 * (on["finalResidents"] / off["finalResidents"] - 1),
                        "settlementChange": on["finalSettlements"] - off["finalSettlements"],
                        "weightedQoLChange": on["weightedQoL"] - off["weightedQoL"]})

result = {"simulationCommit": "b6a690ad2785409ff41bcfd393c74630463ad392",
          "method": "Production-config Archipelagos/1024x512/80; seeds 23 and 101; exactly 40 turns after each completed opening. Only resident Fishing and corrected aquatic pressure differ within a pair. Sailing off; material-work correction applied to all cases. First technology labels ancestry, not exclusive economic activity.",
          "cases": cases, "comparisons": comparisons,
          "limits": ["Two seeds are evidence about these worlds, not general balance sign-off.",
                     "No native Unity playtest or new five-world 50-turn replay is claimed."]}
output.write_text(json.dumps(result, indent=2) + "\n")
for c in cases:
    print(c["seed"], c["variant"], c["finalSettlements"], c["finalResidents"], c["weightedQoL"],
          "pure Cloth grants/actual", c["pureWatercraftClothGrantTurns"], c["pureWatercraftClothGrantWithActualClothTurns"])
for comparison in comparisons:
    print(comparison)
