#!/usr/bin/env python3
"""Stage the closed schema-v2 owner payload, following the existing Skills package route."""
import hashlib
import json
import pathlib
import re
import shutil
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]


def canonical_digest(raw):
    return hashlib.sha256(raw.decode('utf-8-sig').replace('\r\n', '\n').encode()).hexdigest()


def safe_path(text):
    if not isinstance(text, str) or '\\' in text:
        raise ValueError('invalid relative path')
    parts = text.split('/')
    if any(part in ('', '.', '..') for part in parts) or ':' in text:
        raise ValueError('invalid relative path: ' + text)
    return pathlib.Path(*parts)


def payload(root):
    """Validate every source before emitting any staged payload; no undeclared members."""
    manifest = root / 'template/skill-manifest/skill-manifest.json'
    raw = manifest.read_bytes()
    document = json.loads(raw)
    if document.get('schemaVersion') != 2 or len(document.get('skills', [])) != 1:
        raise ValueError('expected the schema-v2 Algorithms owner row')
    row = document['skills'][0]
    if row.get('id') != 'fs-gg-algorithms' or row.get('scope') != 'product':
        raise ValueError('unexpected owner identity/scope')
    if row.get('supplied-by') != 'template/product-skills/fs-gg-algorithms/':
        raise ValueError('unexpected source directory')
    source = root / row['supplied-by']
    if source.is_symlink():
        raise ValueError('symbolic source directory')
    indexed = {}
    for entry in row.get('files', []):
        relative = entry['path']
        path = safe_path(relative)
        if relative in indexed or not re.fullmatch('[0-9a-f]{64}', entry['sha256']):
            raise ValueError('duplicate file or invalid digest')
        file = source / path
        if any(part.is_symlink() for part in [file, *file.parents] if part != root):
            raise ValueError('symbolic member: ' + relative)
        body = file.read_bytes()
        if canonical_digest(body) != entry['sha256']:
            raise ValueError('stale manifest digest: ' + relative)
        # Atlas has an exact-byte hash domain, unlike SkillMirror's normalized text domain.
        if relative.startswith('pack/') and (b'\r' in body or body.startswith(b'\xef\xbb\xbf')):
            raise ValueError('pack must retain canonical LF UTF-8 bytes: ' + relative)
        indexed[relative] = body
    actual_paths = list(source.rglob('*'))
    if any(path.is_symlink() for path in actual_paths):
        raise ValueError('symbolic member in closed source inventory')
    actual = {str(path.relative_to(source)).replace('\\', '/') for path in actual_paths if path.is_file()}
    if set(indexed) != actual or 'SKILL.md' not in indexed:
        raise ValueError('closed manifest/source inventory mismatch')
    if canonical_digest(indexed['SKILL.md']) != row.get('sha256'):
        raise ValueError('row-level skill digest mismatch')
    return {'skill-manifest.json': raw, **{'skills/fs-gg-algorithms/' + path: body for path, body in indexed.items()}}


def stage(root, output):
    members = payload(root)
    output = output.absolute()
    # The existing owner stager replaces its generated directory each pack. Refuse source or
    # ancestor destinations so a misconfigured pack cannot erase authored input.
    resolved = output.resolve()
    expected = (root / 'src/FS.GG.Algorithms.Skills/obj/skills-staging').resolve()
    if output.is_symlink() or resolved == root.resolve() or resolved in root.resolve().parents or (resolved.is_relative_to(root.resolve()) and resolved != expected):
        raise ValueError('refuse a source/ancestor staging destination')
    if output.exists():
        shutil.rmtree(output)
    output.mkdir(parents=True)
    for relative, body in members.items():
        path = output / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(body)
    return len(members)


if __name__ == '__main__':
    try:
        if len(sys.argv) != 2:
            raise ValueError('usage: stage-skills.py <generated-output-directory>')
        print('stage-skills:', stage(ROOT, pathlib.Path(sys.argv[1])), 'verified files')
    except (ValueError, OSError, KeyError, UnicodeError) as error:
        print('stage-skills:', error, file=sys.stderr)
        sys.exit(2)
