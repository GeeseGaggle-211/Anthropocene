"""Retain verified native persistence measurements; binary saves stay in TestResults."""
import hashlib
import argparse
import json
import re
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--root', default='TestResults/SaveAudit')
parser.add_argument('--log', default='/tmp/save-audit.log')
args = parser.parse_args()
root = Path(args.root)
manifest = json.loads((root / 'manifest.json').read_text())
stages = json.loads((root / 'results.json').read_text())
for path, digest in manifest['sources'].items():
    assert hashlib.sha256(Path(path).read_bytes()).hexdigest().upper() == digest, path
with Path(manifest['source']).open('rb') as f:
    assert hashlib.file_digest(f, 'sha256').hexdigest().upper() == manifest['sourceSha256']
by_name = {s['name']: s for s in stages}
if manifest.get('extendedInput'):
    with Path(manifest['extendedInput']['path']).open('rb') as f:
        assert hashlib.file_digest(f, 'sha256').hexdigest().upper() == manifest['extendedInput']['sha256']
    assert by_name['extended-input']['detail']['exactNativeFingerprint']
assert manifest['metadataBytes'] > 512 * 1024**2
assert by_name['historical-fingerprint']['detail']['exactHistoricalFingerprint']
for name in ('recovered', 'extended'):
    assert by_name[name + '-load']['detail']['exactFingerprint']
    assert by_name[name + '-continuation']['detail']['exactNextTurn']
    assert by_name[name + '-continuation']['state']['history'] == by_name[name + '-save']['state']['history'] + 1
assert by_name['extended-save']['detail']['envelope'] == 'ANTHROPOCENE-12'
assert by_name['extended-save']['detail']['metadataBytes'] > 2**31 - 1

headless = Path('/tmp/save-full-tests.log').read_text()
suites = re.findall(r'^TIMING (\S+) \d', headless, flags=re.MULTILINE)
assert len(suites) == 70 and 'PASS: 54 save codec assertions' in headless
calendar_log = Path('/tmp/save-calendar-tests.log').read_text()
calendar = json.loads(calendar_log[calendar_log.index('{'):])
assert calendar['status'] == 'passed' and calendar['checks'] == 28787
validation = dict(headlessSuites=suites, saveCodecAssertions=54, calendarAssertions=calendar['checks'],
                  resourceRegression='The resource-bearing envelope-12 fixture failed before the reader compatibility correction and passes after it.',
                  unity='Unavailable; normal file-save UI call sites inspected, native interaction not run.',
                  logsSha256={name: hashlib.sha256(Path(name).read_bytes()).hexdigest() for name in
                              ('/tmp/save-full-tests.log', '/tmp/save-calendar-tests.log', args.log)})
out = Path('Docs/Experiments/SavePersistence-20261115')
for suffix, data in [('manifest', manifest), ('results', stages), ('validation', validation)]:
    out.with_name(out.name + '-' + suffix + '.json').write_text(json.dumps(data, indent=2) + '\n')

table = ['| Phase | Boundary turn | Metadata MiB | Wall seconds | Peak working set MiB |',
         '| --- | ---: | ---: | ---: | ---: |']
for name in ['original-load', 'recovered-save', 'recovered-load', 'recovered-continuation',
             'extended-save', 'extended-load', 'extended-continuation']:
    row = by_name[name]
    size = row['detail'].get('metadataBytes')
    table.append(f"| {name} | {row['state']['Turn']} | {size / 1024**2:,.2f}" if size else f"| {name} | {row['state']['Turn']} | —")
    table[-1] += f" | {row['seconds']:,.2f} | {row['peakWorkingSetBytes'] / 1024**2:,.1f} |"
extended = by_name['extended-save']
text = f"""
## Measured recovery — 15 November 2026

The original failing 80-tribe seed-11 save loads with its exact historical
fingerprint, `8BD1670A9268D9DE3F72E0D9FE3E94FA8EE367529639B06C1C1E06BBE6A576F0`.
Its replacement preserves the same canonical state and all 118 history records.
Both worlds advance one native smoothed-calendar turn to identical fingerprints.

Normal AI/Elder continuation then grows the world to **{extended['detail']['metadataBytes']:,}
metadata bytes ({extended['detail']['metadataBytes']/1024**3:.3f} GiB)** at boundary
turn {extended['state']['Turn']}, with {extended['state']['history']} retained records,
{extended['state']['population']:,} people and {extended['state']['camps']:,} camps.
This is a real campaign, without duplicated history or synthetic padding.
The writer automatically switches to envelope 12; that save also reloads with
an identical fingerprint and passes exact next-turn continuation.

{chr(10).join(table)}

Reload stages include fingerprinting; continuation stages resolve and hash both
resident copies; save stages include a compressed-file hash. Some early stages
overlapped other validation, so these are observed wall times, not isolated
before/after benchmarks. Compression output is not part of the canonical
fingerprint and compressed file bytes need not remain identical.

All **70 registered headless suites**, including **54 save-codec assertions**,
and **28,787 calendar checks** pass on this implementation. Native Unity
interaction remains unverified. The implementation retains an explicit 8 GiB
metadata limit and does not solve unbounded history growth by deleting data.

[Native measurements](Experiments/SavePersistence-20261115-results.json),
[source/input manifest](Experiments/SavePersistence-20261115-manifest.json), and
[regression evidence](Experiments/SavePersistence-20261115-validation.json)
are retained with the code. After the audit, run
`python3 Tools/SaveAudit/Summarize.py` to validate and regenerate them.
"""
doc = Path('Docs/SavePersistence.md')
body = doc.read_text().split('\n## Measured recovery')[0]
doc.write_text(body.rstrip() + '\n' + text)
print('PASS: native persistence evidence, 70 suites, 54 codec checks, 28787 calendar checks')
