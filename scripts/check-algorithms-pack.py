#!/usr/bin/env python3
"""ADR0098 policy over the unchanged Atlas archive; Atlas remains the format validator."""
import argparse
import hashlib
import json
import pathlib
import re
import subprocess
import sys

HEADINGS = ['Idea', 'Complexity', 'Use when', 'Avoid when', 'Pitfalls', 'F# usage', 'Tests', 'Bench', 'Related', 'References']
FAMILIES = {'collections', 'random', 'graph', 'search.grid', 'search.mesh', 'search.multiagent', 'steering', 'decision', 'adversarial', 'learning', 'procgen', 'spatial', 'geometry', 'optimization', 'numerics', 'net', 'systems'}
IMPLEMENTATIONS = {'FS-GG/FS.GG.Algorithms': 'src/FS.GG.Algorithms/', 'FS-GG/FS.GG.Game': 'src/Game.Core/'}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def check(skill, atlas, sources):
    archive = skill / 'pack'
    # Reuse the delivered validator, including its canonical bytes, schema, graph and path checks.
    subprocess.run([sys.executable, '-I', '-B', str(atlas), '--archive', str(archive), 'validate'], check=True)
    files = [file for file in archive.rglob('*') if file.is_file()]
    size = sum(file.stat().st_size for file in files)
    require(size <= 4 * 1024 * 1024, 'pack exceeds 4 MiB')
    if size >= 2 * 1024 * 1024:
        print('pack-policy: warning: pack exceeds 2 MiB', file=sys.stderr)
    require((skill / 'SKILL.md').stat().st_size <= 16 * 1024, 'SKILL.md exceeds 16 KiB')
    require((skill / '.gitattributes').read_text().splitlines() == ['pack/** -text'], 'missing exact-byte pack attribute')
    catalog = json.loads((archive / 'catalog.json').read_bytes())
    records = {digest: json.loads((archive / 'records' / (digest + '.json')).read_bytes()) for digest in catalog['records']}
    hidden = {digest for record in records.values() for digest in record['supersedes']}
    active = [record for digest, record in records.items() if digest not in hidden]
    require(len({record['id'] for record in active}) == len(active), 'multiple active records for an ID')
    implemented = 0
    for digest, record in records.items():
        require(record['repository'] == 'FS-GG/FS.GG.Algorithms', 'wrong archive owner')
        parts = record['id'].split('/')
        require(len(parts) == 3 and parts[0] == 'algorithms' and parts[1] in FAMILIES, 'non-canonical technique ID')
        summary = record['summary']
        require(re.findall(r'^## (.+)$', summary, re.M) == HEADINGS, 'missing/reordered technique headings: ' + record['id'])
        tags = set(record['tags'])
        for prefix, choices in [('audience-', {'game', 'framework', 'both'}), ('grade-', {'lockstep-exact', 'portable', 'dotnet-only', 'unclassified'}), ('status-', {'pointer', 'harvested', 'new', 'entry-only'})]:
            selected = {tag for tag in tags if tag.startswith(prefix)}
            require(len(selected) == 1 and selected <= {prefix + value for value in choices}, 'invalid ' + prefix + 'tag')
        usage = summary.split('## F# usage\n', 1)[1].split('\n\n## Tests', 1)[0]
        snippets = re.findall(r'```fsharp\n(.*?)\n```', usage, re.S)
        require(len(snippets) == 1 and 0 < len(snippets[0].splitlines()) <= 15, 'missing/oversized original F# example')
        require('Package: FS.GG.' in usage and '`FS.GG.' in usage, 'usage lacks package/module/signature')
        require(not re.search(r'(?:TODO|FIXME|TBD|PRIVATE KEY|gh[pousr]_[A-Za-z0-9]{16})', summary), 'placeholder/private material marker')
        require(all(citation['repository'] in IMPLEMENTATIONS for citation in record['citations']), 'citation outside reviewed public producers')
        has_module = any(citation['path'].startswith(IMPLEMENTATIONS[citation['repository']]) and citation['path'].endswith(('.fsi', '.fs')) for citation in record['citations'])
        require(has_module, 'entry lacks an immutable public implementation citation')
        if digest not in hidden and record['kind'] == 'contract' and 'status-entry-only' not in tags:
            implemented += 1
        if (archive / 'records' / (digest + '.json')).stat().st_size > 6 * 1024:
            print('pack-policy: warning: record above 6 KiB target:', record['id'], file=sys.stderr)
        for citation in record['citations']:
            if citation['repository'] in sources:
                result = subprocess.run(['git', '-C', str(sources[citation['repository']]), 'show', citation['revision'] + ':' + citation['path']], check=True, capture_output=True)
                require(hashlib.sha256(result.stdout).hexdigest() == citation['sha256'], 'source digest mismatch: ' + citation['path'])
    require(implemented >= 15, 'fewer than 15 active implemented technique entries')
    print(f'pack-policy: {len(active)} active entries, {implemented} implemented entries, {size} archive bytes')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--skill', type=pathlib.Path, default=pathlib.Path(__file__).resolve().parents[1] / 'template/product-skills/fs-gg-algorithms')
    parser.add_argument('--atlas-script', type=pathlib.Path, required=True, help='existing delivered Atlas driver; never copied into this package')
    parser.add_argument('--source', action='append', default=[], metavar='REPOSITORY=LOCAL_GIT', help='optional offline exact citation-byte verification')
    args = parser.parse_args()
    try:
        sources = {repository: pathlib.Path(path) for repository, path in (value.split('=', 1) for value in args.source)}
        check(args.skill, args.atlas_script, sources)
    except (ValueError, KeyError, OSError, subprocess.CalledProcessError) as error:
        print('pack-policy:', error, file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
