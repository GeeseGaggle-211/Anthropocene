#!/usr/bin/env python3
"""Compare the preserved candidate and corrected crop/herd economy at 25/50 turns."""
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
VARIANTS = ('pre-moisture', 'corrected')


def invoke(command, log):
    with log.open('w') as stream:
        subprocess.run(command, cwd=REPO, stdout=stream, stderr=subprocess.STDOUT, check=True)


def build(root):
    # The tracked reverse recipe reconstructs the measured pre-package source
    # on a fresh checkout. Validate exact hashes before and after applying it.
    provenance = root / 'pre-moisture-provenance.json'
    baseline = root / 'pre-moisture-source'
    recipe_dir = REPO / 'Tools/PerformanceAudit/Baselines'
    recipe = json.loads((recipe_dir / 'CropHerdBalance-source-hashes.json').read_text())
    def hashes(source):
        return {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in source.glob('*.cs')}
    if not provenance.exists():
        if hashes(SIM) != recipe['correctedSources']:
            raise RuntimeError('Source differs from the measured candidate; supply the preserved pre-moisture snapshot instead of manufacturing a baseline.')
        baseline.mkdir(exist_ok=True)
        for path in SIM.glob('*.cs'):
            shutil.copyfile(path, baseline / path.name)
        subprocess.run(['patch', '--batch', '--forward', '--strip=1', '--directory=' + str(baseline),
                        '--input=' + str(recipe_dir / 'CropHerdBalance-pre-moisture.patch')], check=True, stdout=subprocess.DEVNULL)
        if hashes(baseline) != recipe['preMoistureSources']:
            raise RuntimeError('Reconstructed baseline does not match measured source hashes.')
        provenance.write_text(json.dumps(hashes(baseline), indent=2) + '\n')
    if hashes(baseline) != json.loads(provenance.read_text()):
        raise RuntimeError('Preserved baseline source changed; refusing to compare it as the original control.')
    source = root / 'corrected-source'
    source.mkdir(exist_ok=True)
    for path in SIM.glob('*.cs'):
        shutil.copyfile(path, source / path.name)
    (root / 'corrected-provenance.json').write_text(json.dumps({p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in source.glob('*.cs')}, indent=2) + '\n')
    for variant in VARIANTS:
        invoke(['dotnet', 'build', 'Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj', '-c', 'Release', '-o', str(root / variant),
                '-p:SimulationSource=' + str(root / (variant + '-source'))], root / (variant + '-build.log'))
    print('builds finished', flush=True)


def command(root, variant):
    return ['dotnet', str(root / variant / 'Anthropocene.PerformanceAudit.dll'), '--workers=1', '--partition-size=64', '--fingerprints=0', '--metrics=0',
            '--warmup=1', '--herd-labour=1', '--qol-mortality=1', '--fission=1', '--set=QoLMortalityElasticity=1', '--revision=' + variant]


def outcomes(root, jobs):
    out = root / 'gameplay'; out.mkdir(exist_ok=True)
    def run(variant, world):
        name, shape, seed = world
        stem = variant + '-' + name
        args = command(root, variant) + ['--seed=' + str(seed), '--shape=' + shape, '--players=80', '--production-config=1', '--turns=12', '--post-opening-turns=50',
               '--details=1', '--output=' + str(out / (stem + '.json')), '--save-finished=' + str(out / (stem + '-opening.save'))]
        invoke(args, out / (stem + '.log'))
        data = json.loads((out / (stem + '.json')).read_text())
        assert len([s for s in data['samples'] if s['phase'] == 'post-opening']) == 50
        print(stem, 'finished', flush=True)
    with concurrent.futures.ThreadPoolExecutor(max_workers=jobs) as pool:
        futures = [pool.submit(run, variant, world) for world in WORLDS for variant in VARIANTS]
        for future in concurrent.futures.as_completed(futures):
            future.result()


def performance(root, pairs):
    out = root / 'performance'; out.mkdir(exist_ok=True)
    checkpoint = root / 'gameplay/pre-moisture-C1-opening.save'
    for turns in (25, 50):
        for pair in range(pairs):
            # Uncontended sequential AB/BA pairs; no concurrent gameplay or builds.
            order = VARIANTS if pair % 2 == 0 else tuple(reversed(VARIANTS))
            for variant in order:
                stem = f'{variant}-{turns}-{pair}'
                args = command(root, variant)
                if variant == 'corrected':
                    args = [s for s in args if not s.startswith('--set=')]
                    args += ['--set=QoLMortalityElasticity=1,CropMoistureReference=.5,CropMoistureExponent=2,CropHarvestingOnly=1']
                args += ['--load=' + str(checkpoint), '--turns=0', '--post-opening-turns=' + str(turns), '--details=0', '--output=' + str(out / (stem + '.json'))]
                invoke(args, out / (stem + '.log'))
                data = json.loads((out / (stem + '.json')).read_text())
                assert len(data['samples']) == turns and all(s['phase'] == 'post-opening' for s in data['samples'])
                print(stem, 'finished', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', default='TestResults/CropHerdBalance')
    parser.add_argument('--phase', choices=['build', 'gameplay', 'performance', 'all'], default='all')
    parser.add_argument('--jobs', type=int, default=2)
    parser.add_argument('--pairs', type=int, default=3)
    args = parser.parse_args(); root = (REPO / args.output).resolve(); root.mkdir(parents=True, exist_ok=True)
    if args.phase in ('build', 'all'): build(root)
    if args.phase in ('gameplay', 'all'): outcomes(root, args.jobs)
    if args.phase in ('performance', 'all'): performance(root, args.pairs)


if __name__ == '__main__':
    main()
