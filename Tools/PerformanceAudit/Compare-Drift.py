"""Compare matched post-opening outcome drift between two PerformanceAudit runs.

Each directory holds `base-<world>.json` and `cand-<world>.json` reports from
the same checkpoint (`--load`) and turn count. For population, prior-cohort
weighted QoL and realized food yield the script prints, per world:

- the largest per-turn relative deviation (sensitive to trajectory divergence);
- the mean signed deviation over all turns, and over the first ten and the
  remaining turns (bias).

Pass a second directory produced by a same-method control (for example the
baseline with one more search iteration) to print the noise floor next to the
candidate. Per-turn maxima are only meaningful relative to that control.

Usage: python Compare-Drift.py <candidate-dir> [<control-dir>] [--tolerance 0.05]
"""
import json
import os
import sys

METRICS = ["population", "priorWeightedQol", "foodYield"]
WORLDS = ["80-11", "80-23", "80-1729", "320-1729"]


def samples(path):
    with open(path, encoding="utf-8") as handle:
        return [s for s in json.load(handle)["samples"] if s["postOpening"]]


def summarize(directory, world):
    base = os.path.join(directory, f"base-{world}.json")
    cand = os.path.join(directory, f"cand-{world}.json")
    if not (os.path.exists(base) and os.path.exists(cand)):
        return None
    b, c = samples(base), samples(cand)
    n = min(len(b), len(c))
    result = {"turns": n}
    for key in METRICS:
        dev = [(c[i][key] / b[i][key] - 1) * 100 for i in range(n) if b[i][key]]
        early, late = dev[:10], dev[10:]
        result[key] = {
            "max": max(abs(x) for x in dev),
            "mean": sum(dev) / len(dev),
            "early": sum(early) / len(early) if early else 0.0,
            "late": sum(late) / len(late) if late else 0.0,
        }
    return result


def main(argv):
    tolerance = 0.05
    if "--tolerance" in argv:
        at = argv.index("--tolerance")
        tolerance = float(argv[at + 1])
        del argv[at:at + 2]
    candidate = argv[1]
    control = argv[2] if len(argv) > 2 else None
    within = True
    for world in WORLDS:
        run = summarize(candidate, world)
        if run is None:
            continue
        noise = summarize(control, world) if control else None
        print(f"{world} ({run['turns']} turns)")
        for key in METRICS:
            r = run[key]
            line = (f"  {key:17} max {r['max']:6.2f}%  mean {r['mean']:+6.2f}% "
                    f"[{r['early']:+6.2f}% | {r['late']:+6.2f}%]")
            if noise:
                line += f"   control max {noise[key]['max']:6.2f}% mean {noise[key]['mean']:+6.2f}%"
            print(line)
            within &= abs(r["mean"]) <= tolerance * 100
    print(f"mean signed deviation {'within' if within else 'EXCEEDS'} +/-{tolerance * 100:.0f}%")
    return 0 if within else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
