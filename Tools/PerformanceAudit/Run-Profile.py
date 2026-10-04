#!/usr/bin/env python3
"""Full-system 50-turn profile runs (Docs/EconomicControlContractAddendumA.md §A7; Docs/PlannerTradeStorageDesign.md §3.11).

Builds the current simulation source into <output>/bin, then runs the named variants on the five census opening
checkpoints (made by Run-TradeAudit.py or Run-StorageAudit.py) with per-camp details, for Summarize-Profile.py and
Summarize-Lineages.py. All variants use the census configuration (fission, herd labour, QoL mortality, shared allocation).
"""
import argparse, concurrent.futures, pathlib, subprocess

REPO = pathlib.Path(__file__).resolve().parents[2]
WORLDS = ['C1', 'C2', 'C3', 'P1', 'A1']
BASE = 'QoLMortalityElasticity=1,SharedCropHerdAllocationEnabled=1'
# variant -> (immigration, extra settings)
VARIANTS = {
    'none': (0, ''),
    'none-imm': (1, ''),
    'trade': (0, ',TradeEnabled=1'),
    'all': (1, ',TradeEnabled=1'),
    'all-mto': (1, ',TradeEnabled=1,TradeExportLabourShare=0.25'),
    'trade-mto': (0, ',TradeEnabled=1,TradeExportLabourShare=0.25'),
    # DemandControl (Docs/EconomicControlContract.md slice 1) against the Legacy variants above.
    'none-dc': (0, ',EconomicAllocation=DemandControl'),
    'all-dc': (1, ',TradeEnabled=1,EconomicAllocation=DemandControl'),
}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--openings', default='TestResults/Trade-V1/gameplay', help='directory holding <world>-opening.save')
    parser.add_argument('--output', default='TestResults/Profile')
    parser.add_argument('--variants', default='all,none-imm')
    parser.add_argument('--turns', type=int, default=50)
    parser.add_argument('--workers', type=int, default=1)
    parser.add_argument('--jobs', type=int, default=4)
    args = parser.parse_args()
    out = (REPO / args.output).resolve(); (out / 'gameplay').mkdir(parents=True, exist_ok=True)
    subprocess.run(['dotnet', 'build', 'Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj', '-c', 'Release', '-o', str(out / 'bin')],
                   cwd=REPO, check=True, stdout=open(out / 'build.log', 'w'), stderr=subprocess.STDOUT)
    def run(v, w):
        imm, extra = VARIANTS[v]
        cmd = ['dotnet', str(out / 'bin/Anthropocene.PerformanceAudit.dll'), f'--workers={args.workers}', '--partition-size=64', '--fingerprints=0', '--metrics=1',
               '--warmup=0', '--herd-labour=1', '--qol-mortality=1', '--fission=1', f'--immigration={imm}', '--set=' + BASE + extra, '--revision=' + v,
               '--load=' + str((REPO / args.openings / f'{w}-opening.save').resolve()), '--turns=0', f'--post-opening-turns={args.turns}', '--details=1',
               '--output=' + str(out / f'gameplay/{v}-{w}.json')]
        with open(out / f'gameplay/{v}-{w}.log', 'w') as log: subprocess.run(cmd, cwd=REPO, stdout=log, stderr=subprocess.STDOUT, check=True)
        print(v, w, 'finished', flush=True)
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.jobs) as pool:
        for f in [pool.submit(run, v, w) for v in args.variants.split(',') for w in WORLDS]: f.result()

if __name__ == '__main__': main()
