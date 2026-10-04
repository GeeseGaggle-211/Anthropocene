#!/usr/bin/env python3
"""Phase S goods-storage audit (Docs/PlannerTradeStorageDesign.md §6): default-off exactness against the commit before
storage, then storage on at several base allowances x (goods units per resident) and spaces s (food units per goods unit).

Trade and immigration stay off, so storage is measured alone. All runs use the census configuration (fission, herd
labour, QoL mortality, shared allocation) on the same five Standard/80 opening checkpoints, saved by the current source.
"""
import argparse
import concurrent.futures
import hashlib
import json
import pathlib
import shutil
import subprocess

REPO = pathlib.Path(__file__).resolve().parents[2]
SIM = REPO / 'Assets/_Anthropocene/Simulation'
WORLDS = [('C1', 'Continents', 1), ('C2', 'Continents', 2), ('C3', 'Continents', 3), ('P1', 'Pangaea', 1), ('A1', 'Archipelagos', 1)]
REFERENCE = 'a3466a1'  # the last commit before goods storage
# variant -> (binary, extra settings)
RUNS = {
    'reference': ('reference', ''),
    'off': ('current', ''),
    'x1': ('current', 'GoodsStorageEnabled=1,GoodsBaseStoragePerResident=1,GoodsStorageSpacePerUnit=20'),
    'x2': ('current', 'GoodsStorageEnabled=1,GoodsBaseStoragePerResident=2,GoodsStorageSpacePerUnit=20'),
    'x4': ('current', 'GoodsStorageEnabled=1,GoodsBaseStoragePerResident=4,GoodsStorageSpacePerUnit=20'),
    'x2s5': ('current', 'GoodsStorageEnabled=1,GoodsBaseStoragePerResident=2,GoodsStorageSpacePerUnit=5'),
}


def invoke(command, log):
    with log.open('w') as stream:
        subprocess.run(command, cwd=REPO, stdout=stream, stderr=subprocess.STDOUT, check=True)


def hashes(source):
    return {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(source.glob('*.cs'))}


def build(root):
    reference = root / 'reference-source'; current = root / 'current-source'
    for d in (reference, current):
        if d.exists(): shutil.rmtree(d)
        d.mkdir()
    names = subprocess.check_output(['git', 'ls-tree', '--name-only', REFERENCE, 'Assets/_Anthropocene/Simulation/'], cwd=REPO, text=True).split()
    for path in names:
        if path.endswith('.cs'):
            (reference / pathlib.Path(path).name).write_bytes(subprocess.check_output(['git', 'show', REFERENCE + ':' + path], cwd=REPO))
    for path in SIM.glob('*.cs'): shutil.copyfile(path, current / path.name)
    for variant, source in (('reference', reference), ('current', current)):
        (root / (variant + '-provenance.json')).write_text(json.dumps(hashes(source), indent=2) + '\n')
        invoke(['dotnet', 'build', 'Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj', '-c', 'Release', '-o', str(root / variant),
                '-p:SimulationSource=' + str(source)], root / (variant + '-build.log'))
    print('builds finished', flush=True)


def command(root, variant):
    binary, extra = RUNS[variant]
    settings = 'QoLMortalityElasticity=1,SharedCropHerdAllocationEnabled=1' + (',' + extra if extra else '')
    return ['dotnet', str(root / binary / 'Anthropocene.PerformanceAudit.dll'), '--workers=1', '--partition-size=64', '--fingerprints=0', '--metrics=1',
            '--warmup=0', '--herd-labour=1', '--qol-mortality=1', '--fission=1', '--immigration=0', '--set=' + settings, '--revision=' + variant]


def outcomes(root, jobs, resume, variants, turns):
    out = root / 'gameplay'; out.mkdir(exist_ok=True)
    def opening(world):
        name, shape, seed = world
        checkpoint = out / (name + '-opening.save')
        if not (resume and checkpoint.exists()):
            args = command(root, 'off') + ['--seed=' + str(seed), '--shape=' + shape, '--players=80', '--production-config=1', '--turns=12',
                                           '--post-opening-turns=0', '--details=0', '--save-finished=' + str(checkpoint), '--output=' + str(out / (name + '-opening.json'))]
            invoke(args, out / (name + '-opening.log'))
        return checkpoint
    def run(world, variant):
        name = world[0]; checkpoint = out / (name + '-opening.save')
        path = out / (variant + '-' + name + '.json')
        if not (resume and path.exists()):
            args = command(root, variant) + ['--load=' + str(checkpoint), '--turns=0', '--post-opening-turns=' + str(turns), '--details=1', '--output=' + str(path)]
            invoke(args, out / (variant + '-' + name + '.log'))
        data = json.loads(path.read_text())
        assert len([s for s in data['samples'] if s['phase'] == 'post-opening']) == turns
        assert data['inputSaveSha256'].lower() == hashlib.sha256(checkpoint.read_bytes()).hexdigest()
        print(variant, name, 'finished', flush=True)
    with concurrent.futures.ThreadPoolExecutor(max_workers=jobs) as pool:
        for f in [pool.submit(opening, w) for w in WORLDS]: f.result()
        futures = [pool.submit(run, w, v) for w in WORLDS for v in variants]
        for f in concurrent.futures.as_completed(futures): f.result()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', default='TestResults/Storage')
    parser.add_argument('--phase', choices=['build', 'gameplay', 'all'], default='all')
    parser.add_argument('--variants', default=','.join(RUNS))
    parser.add_argument('--turns', type=int, default=50)
    parser.add_argument('--jobs', type=int, default=4)
    parser.add_argument('--resume', action='store_true')
    args = parser.parse_args(); root = (REPO / args.output).resolve(); root.mkdir(parents=True, exist_ok=True)
    if args.phase in ('build', 'all'): build(root)
    if args.phase in ('gameplay', 'all'): outcomes(root, args.jobs, args.resume, args.variants.split(','), args.turns)


if __name__ == '__main__': main()
