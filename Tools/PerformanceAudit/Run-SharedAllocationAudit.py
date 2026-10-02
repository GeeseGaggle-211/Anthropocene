#!/usr/bin/env python3
"""Replay shared crop/herd allocation against the exact corrected ecology control."""
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
VARIANTS = ('control', 'candidate')
CONTROL = '03d1ed1'


def invoke(command, log):
    with log.open('w') as stream:
        subprocess.run(command, cwd=REPO, stdout=stream, stderr=subprocess.STDOUT, check=True)


def hashes(source):
    return {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in source.glob('*.cs')}


def build(root):
    recipe = json.loads((REPO / 'Tools/PerformanceAudit/Baselines/CropHerdBalance-source-hashes.json').read_text())
    source = root / 'control-source'; source.mkdir(exist_ok=True)
    for name in recipe['correctedSources']:
        path = 'Assets/_Anthropocene/Simulation/' + name
        (source / name).write_bytes(subprocess.check_output(['git', 'show', CONTROL + ':' + path], cwd=REPO))
    if hashes(source) != recipe['correctedSources']:
        raise RuntimeError('Control differs from the exact measured corrected source.')
    for variant in VARIANTS:
        source = root / (variant + '-source'); source.mkdir(exist_ok=True)
        if variant == 'candidate':
            for old in source.glob('*.cs'): old.unlink()
            for path in SIM.glob('*.cs'): shutil.copyfile(path, source / path.name)
        (root / (variant + '-provenance.json')).write_text(json.dumps(hashes(source), indent=2) + '\n')
        invoke(['dotnet', 'build', 'Tools/PerformanceAudit/Anthropocene.PerformanceAudit.csproj', '-c', 'Release', '-o', str(root / variant),
                '-p:SimulationSource=' + str(source)], root / (variant + '-build.log'))
    print('builds finished', flush=True)


def command(root, variant, warmup=1):
    settings = 'QoLMortalityElasticity=1'
    if variant == 'candidate': settings += ',SharedCropHerdAllocationEnabled=1'
    return ['dotnet', str(root / variant / 'Anthropocene.PerformanceAudit.dll'), '--workers=1', '--partition-size=64', '--fingerprints=0', '--metrics=0',
            '--warmup=' + str(warmup), '--herd-labour=1', '--qol-mortality=1', '--fission=1', '--set=' + settings, '--revision=' + variant]


def outcomes(root, jobs, resume):
    out = root / 'gameplay'; out.mkdir(exist_ok=True)
    def run(world):
        name, shape, seed = world
        checkpoint = out / ('control-' + name + '-opening.save')
        path = out / ('control-' + name + '.json')
        missing_diagnostics = path.exists() and 'priorCampCount' not in json.loads(path.read_text())['samples'][-1]
        if not resume or not path.exists() or not checkpoint.exists() or missing_diagnostics:
            args = command(root, 'control', 0) + ['--post-opening-turns=50', '--details=1', '--output=' + str(path)]
            if checkpoint.exists():
                args += ['--load=' + str(checkpoint), '--turns=0']
            else:
                args += ['--seed=' + str(seed), '--shape=' + shape, '--players=80', '--production-config=1', '--turns=12', '--save-finished=' + str(checkpoint)]
            invoke(args, out / ('control-' + name + '.log'))
        path = out / ('candidate-' + name + '.json')
        if not resume or not path.exists():
            args = command(root, 'candidate', 0) + ['--load=' + str(checkpoint), '--turns=0', '--post-opening-turns=50', '--details=1', '--output=' + str(path)]
            invoke(args, out / ('candidate-' + name + '.log'))
        data = json.loads(path.read_text())
        assert len([s for s in data['samples'] if s['phase'] == 'post-opening']) == 50
        assert data['inputSaveSha256'].lower() == hashlib.sha256(checkpoint.read_bytes()).hexdigest()
        print(name, 'finished', flush=True)
    with concurrent.futures.ThreadPoolExecutor(max_workers=jobs) as pool:
        futures = [pool.submit(run, world) for world in WORLDS]
        for future in concurrent.futures.as_completed(futures): future.result()


def performance(root, pairs, resume):
    out = root / 'performance'; out.mkdir(exist_ok=True)
    checkpoint = root / 'gameplay/control-C1-opening.save'
    for turns in (25, 50):
        for pair in range(pairs):
            # Timing runs are sequential AB/BA, with no gameplay/build contention.
            for variant in VARIANTS if pair % 2 == 0 else tuple(reversed(VARIANTS)):
                stem = f'{variant}-{turns}-{pair}'; path = out / (stem + '.json')
                if resume and path.exists(): continue
                args = command(root, variant) + ['--load=' + str(checkpoint), '--turns=0', '--post-opening-turns=' + str(turns), '--details=0', '--output=' + str(path)]
                invoke(args, out / (stem + '.log'))
                data = json.loads(path.read_text())
                assert len(data['samples']) == turns and all(s['phase'] == 'post-opening' for s in data['samples'])
                print(stem, 'finished', flush=True)


def profiles(root):
    out = root / 'profiles'; out.mkdir(exist_ok=True)
    checkpoint = root / 'gameplay/control-C1-opening.save'
    for variant in VARIANTS:
        args = [s.replace('--metrics=0', '--metrics=1') for s in command(root, variant)]
        args += ['--load=' + str(checkpoint), '--turns=0', '--post-opening-turns=50', '--details=1', '--output=' + str(out / (variant + '.json'))]
        invoke(args, out / (variant + '.log'))
        print(variant, 'profile finished', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', default='TestResults/SharedAllocation')
    parser.add_argument('--phase', choices=['build', 'gameplay', 'performance', 'profile', 'all'], default='all')
    parser.add_argument('--jobs', type=int, default=2)
    parser.add_argument('--pairs', type=int, default=3)
    parser.add_argument('--resume', action='store_true', help='Reuse complete runs only when their sources/settings are unchanged.')
    args = parser.parse_args(); root = (REPO / args.output).resolve(); root.mkdir(parents=True, exist_ok=True)
    if args.phase in ('build', 'all'): build(root)
    if args.phase in ('gameplay', 'all'): outcomes(root, args.jobs, args.resume)
    if args.phase in ('profile', 'all'): profiles(root)
    if args.phase in ('performance', 'all'): performance(root, args.pairs, args.resume)


if __name__ == '__main__': main()
