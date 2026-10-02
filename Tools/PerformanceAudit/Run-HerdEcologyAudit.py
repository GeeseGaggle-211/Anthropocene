#!/usr/bin/env python3
"""Freeze four source variants, build the audit, and run the five-world factorial."""
import argparse
import concurrent.futures
import io
import pathlib
import shutil
import subprocess
import tarfile

REPO = pathlib.Path(__file__).resolve().parents[2]
SIM = pathlib.Path('Assets/_Anthropocene/Simulation')
WORLDS = [('C1', 'Continents', 1), ('C2', 'Continents', 2), ('C3', 'Continents', 3), ('P1', 'Pangaea', 1), ('A1', 'Archipelagos', 1)]
CHANGES = {
    'baseline': [],
    'ecology-only': ['FoodEcology.cs', 'WorldGenerator.cs', 'OasesAndHerds.cs', 'Model.cs'],
    'herd-only': ['HerdLabour.cs', 'ProductionDemand.cs', 'EconomicPlanner.cs', 'GenerationalEconomy.cs'],
    'both': ['FoodEcology.cs', 'WorldGenerator.cs', 'OasesAndHerds.cs', 'Model.cs', 'HerdLabour.cs', 'ProductionDemand.cs', 'EconomicPlanner.cs', 'GenerationalEconomy.cs'],
}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline', default='ddbecbf')
    parser.add_argument('--output', default='TestResults/HerdEcology')
    parser.add_argument('--jobs', type=int, default=2, help='Outcome lanes; use 1 for uncontended timing.')
    parser.add_argument('--variants', nargs='+', choices=list(CHANGES), default=list(CHANGES))
    args = parser.parse_args()
    output = (REPO / args.output).resolve()
    (output / 'runs').mkdir(parents=True, exist_ok=True)
    archive = subprocess.run(['git', 'archive', args.baseline, str(SIM)], cwd=REPO, check=True, capture_output=True).stdout
    for variant in args.variants:
        source = output / (variant + '-source')
        with tarfile.open(fileobj=io.BytesIO(archive)) as files:
            for entry in files:
                if entry.isfile() and pathlib.PurePosixPath(entry.name).parent == pathlib.PurePosixPath(str(SIM)):
                    target = source / entry.name
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(files.extractfile(entry).read())
        for name in CHANGES[variant]:
            shutil.copyfile(REPO / SIM / name, source / SIM / name)
        with (output / (variant + '-build.log')).open('w') as log:
            subprocess.run(['dotnet', 'build', 'Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj', '-c', 'Release', '-o', str(output / variant), '-p:SimulationSource=' + str(source / SIM)], cwd=REPO, check=True, stdout=log, stderr=subprocess.STDOUT)

    def run(variant, world):
        name, shape, seed = world
        stem = variant + '-' + name
        command = ['dotnet', str(output / variant / 'Anthropocene.PerformanceAudit.dll'), '--players=80', '--seed=' + str(seed), '--shape=' + shape, '--production-config=1', '--turns=12', '--post-opening-turns=50', '--workers=1', '--partition-size=64', '--warmup=0', '--details=1', '--metrics=0', '--fingerprints=0', '--herd-labour=1', '--qol-mortality=1', '--fission=1', '--set=QoLMortalityElasticity=1', '--output=' + str(output / 'runs' / (stem + '.json')), '--save-finished=' + str(output / 'runs' / (stem + '-opening.save')), '--revision=' + args.baseline + '+' + variant]
        with (output / 'runs' / (stem + '.log')).open('w') as log:
            subprocess.run(command, cwd=REPO, check=True, stdout=log, stderr=subprocess.STDOUT)
        return stem

    with concurrent.futures.ThreadPoolExecutor(max_workers=args.jobs) as pool:
        tasks = [pool.submit(run, variant, world) for variant in args.variants for world in WORLDS]
        for task in concurrent.futures.as_completed(tasks):
            print('COMPLETED ' + task.result(), flush=True)

if __name__ == '__main__':
    main()
