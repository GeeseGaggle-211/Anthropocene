#!/usr/bin/env python3
"""Summarize stationary planner diagnostics from the corrected C1 replay."""
import argparse
import json
import pathlib
import statistics

REPO = pathlib.Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', default='TestResults/CropHerdBalance/pastoral-C1-final')
    args = parser.parse_args(); root = (REPO / args.input).resolve()
    summary = []
    for t in (0, 25, 50):
        rows = [json.loads(line) for line in (root / f'census-{t}.jsonl').read_text().splitlines()]
        owners = [x for x in rows if x['choices']]
        def chosen(row):
            return next(c for c in row['choices'] if c['herders'] == row['selectedHerders'])
        crop = sum(f['cropCapacity'] for x in rows for f in x['fields'])
        wet_crop = sum(f['cropCapacity'] for x in rows for f in x['fields'] if f['cropSuitability'] >= .999)
        food_mismatch = sum(x['commonMealGap'] + x['commonReserveGap'] + x['commonGrowthGap']
                            > x['herdMealGap'] + x['herdReserveGap'] + x['herdGrowthGap'] + 1e-8 for x in owners)
        row = dict(postOpeningTurn=t, camps=len(rows), owners=len(owners),
                   medianAnimals=statistics.median(x['animals'] for x in owners),
                   medianLocalPastureCapacity=statistics.median(x['localCapacity'] for x in owners),
                   localPastureAbove100=sum(x['localCapacity'] >= 100 for x in owners),
                   breedingCapped=sum(x['selectedBreeding'] < x['recommendedBreeding'] - 1e-6 for x in owners),
                   belowForecastGrownHerd=sum(x['selectedTarget'] < x['grownAnimals'] for x in owners),
                   noFutureMealGap=sum(x['futureAnnualMealGap'] < 1e-8 for x in owners),
                   zeroCurrentValue=sum(abs(chosen(x)['currentValue']) < 1e-8 for x in owners),
                   zeroTotalHarvestSaving=sum(abs(chosen(x)['harvestSavings']) < 1e-8 for x in owners),
                   cropExpansion=sum(x['expansionFarmers'] > 0 for x in rows),
                   expansionWithNoValuedClaim=sum(x['expansionFarmers'] > 0 and x['candidateExpansionSteps'] == 0 for x in rows),
                   expansionWithUnpricedWorkers=sum(x['unpricedExpansionWorkersEstimate'] > 0 for x in rows),
                   positiveHerdSensitivityToFreeCurrentSpare=sum(any(c['freeSpareCurrentObjectiveDelta'] > 1e-8 for c in x['choices']) for x in owners),
                   currentFoodBucketMismatch=food_mismatch,
                   currentMealBucketMismatch=sum(x['commonMealGap'] > 1e-8 and x['herdMealGap'] < 1e-8 for x in owners),
                   cropCapacityOnFullySuitableTilesFraction=wet_crop / crop if crop else None)
        summary.append(row)
    (root / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n')
    print(json.dumps(summary, indent=2))


if __name__ == '__main__':
    main()
