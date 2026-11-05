"""Observable producer integrity controls; no kernel benchmark or library-suite rerun."""
import hashlib
import importlib.util
import json
import os
import pathlib
import re
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SKILL = 'template/product-skills/fs-gg-algorithms'
MANIFEST = 'template/skill-manifest/skill-manifest.json'
spec = importlib.util.spec_from_file_location('stage', ROOT / 'src/FS.GG.Algorithms.Skills/stage-skills.py')
stage = importlib.util.module_from_spec(spec)
spec.loader.exec_module(stage)


# Exact job prefix from the original zero-job failed workflow e36318b:63–68 env mapping.
# Retained inline so a protected squash/default/release checkout needs no unmerged Git object.
ORIGINAL_FAILED_JOB = "jobs:\n  verify:\n    name: Verify Algorithms skill producer\n    runs-on: ubuntu-24.04\n    timeout-minutes: 15\n    env:\n      NUGET_PACKAGES: ${{ runner.temp }}/algorithms-skills-packages\n      DOTNET_CLI_HOME: ${{ runner.temp }}/algorithms-skills-dotnet\n      DOTNET_GENERATE_ASPNET_CERTIFICATE: 'false'\n"
ORIGINAL_FAILED_JOB_SHA256 = '21b8e26c597f228d8064cf40d2d8d1e0f13ba411e2a31bc82b78665ca0c5af05'

def refuse_job_runner_context(workflow):
    """Focused check for the two maintained workflows' block-style job env mappings.

    This is not a YAML validator. GitHub's context-availability rule excludes runner from
    jobs.<job_id>.env; the same context IS allowed at step env/with. The original native
    zero-job compilation failure is the retained negative input for this narrow regression.
    """
    in_jobs = False
    in_job_env = False
    for number, line in enumerate(workflow.splitlines(), 1):
        if line == 'jobs:':
            in_jobs = True
            continue
        if not in_jobs or not line.strip() or line.lstrip().startswith('#'):
            continue
        indent = len(line) - len(line.lstrip())
        if indent <= 4:
            in_job_env = line.startswith('    env:')
        if in_job_env and re.search(r'\$\{\{[^}]*\brunner\.', line):
            raise ValueError(f'runner context is unavailable in job env at line {number}')


class DeliveryTests(unittest.TestCase):

    def test_workflow_runner_context_and_candidate_checkout_regression(self):
        workflow_path = '.github/workflows/skills-package.yml'
        current = (ROOT / workflow_path).read_text()
        release = (ROOT / '.github/workflows/release-skills.yml').read_text()
        # Immutable exact first failing job prefix; no historical feature-ref dependency after squash.
        self.assertEqual(hashlib.sha256(ORIGINAL_FAILED_JOB.encode()).hexdigest(),
                         ORIGINAL_FAILED_JOB_SHA256)
        original = ORIGINAL_FAILED_JOB
        with self.assertRaisesRegex(ValueError, 'runner context is unavailable in job env'):
            refuse_job_runner_context(original)
        refuse_job_runner_context(current)
        refuse_job_runner_context(release)
        # Preserve both the actual candidate checkout and the private temporary package consumer path.
        self.assertIn('ref: ${{ github.event.pull_request.head.sha || github.sha }}', current)
        self.assertIn('fetch-depth: 0', current)
        self.assertIn('        env:\n          NUGET_PACKAGES: ${{ runner.temp }}/algorithms-skills-packages\n'
                      '          DOTNET_CLI_HOME: ${{ runner.temp }}/algorithms-skills-dotnet', current)
        self.assertIn('run: bash src/FS.GG.Algorithms.Skills/verify-package.sh', current)
        # A regression moving the corrected block back to job scope must refuse again.
        planted = current.replace('        env:\n          NUGET_PACKAGES:',
                                  '    env:\n      NUGET_PACKAGES:', 1)
        with self.assertRaisesRegex(ValueError, 'runner context is unavailable in job env'):
            refuse_job_runner_context(planted)
        # The release workflow's runner.temp is already legal at steps.upload-artifact.with.path.
        self.assertIn('          path: ${{ runner.temp }}/readback/*.nupkg', release)

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = pathlib.Path(self.temporary.name) / 'repo'
        shutil.copytree(ROOT / SKILL, self.root / SKILL)
        (self.root / MANIFEST).parent.mkdir(parents=True)
        shutil.copyfile(ROOT / MANIFEST, self.root / MANIFEST)

    def change_manifest(self, change):
        document = json.loads((self.root / MANIFEST).read_bytes())
        change(document['skills'][0])
        (self.root / MANIFEST).write_text(json.dumps(document))

    def test_complete_staging_preserves_exact_record_bytes_and_removes_retired_output(self):
        output = pathlib.Path(self.temporary.name) / 'staged'
        output.mkdir()
        (output / 'removed-upstream-file').write_text('old output')
        stage.stage(self.root, output)
        self.assertFalse((output / 'removed-upstream-file').exists())
        expected = stage.payload(self.root)
        actual = {path.relative_to(output).as_posix(): path.read_bytes() for path in output.rglob('*') if path.is_file()}
        self.assertEqual(actual, expected)
        self.assertGreaterEqual(len(list((output / 'skills/fs-gg-algorithms/pack/records').glob('*.json'))), 16)

    def test_tamper_refuses_before_any_output_mutation(self):
        (self.root / SKILL / 'SKILL.md').write_text('changed')
        output = pathlib.Path(self.temporary.name) / 'staged'
        output.mkdir()
        (output / 'prior').write_text('retained')
        with self.assertRaisesRegex(ValueError, 'stale manifest'):
            stage.stage(self.root, output)
        self.assertEqual((output / 'prior').read_text(), 'retained')

    def test_undeclared_sidecar_refuses(self):
        (self.root / SKILL / 'extra.md').write_text('not inventoried')
        with self.assertRaisesRegex(ValueError, 'inventory mismatch'):
            stage.payload(self.root)

    def test_omitted_and_duplicate_manifest_member_refuse(self):
        self.change_manifest(lambda row: row['files'].pop())
        with self.assertRaisesRegex(ValueError, 'inventory mismatch'):
            stage.payload(self.root)
        shutil.copyfile(ROOT / MANIFEST, self.root / MANIFEST)
        self.change_manifest(lambda row: row['files'].append(row['files'][0]))
        with self.assertRaisesRegex(ValueError, 'duplicate'):
            stage.payload(self.root)

    def test_relative_escape_and_absolute_file_refuse(self):
        for path in ['../outside', '/outside', 'C:/outside', 'pack\\outside']:
            with self.subTest(path=path), self.assertRaises(ValueError):
                stage.safe_path(path)

    def test_symbolic_member_refuses(self):
        target = self.root / SKILL / 'SKILL.md'
        outside = pathlib.Path(self.temporary.name) / 'outside'
        outside.write_bytes(target.read_bytes())
        target.unlink()
        target.symlink_to(outside)
        with self.assertRaisesRegex(ValueError, 'symbolic'):
            stage.payload(self.root)

    def test_pack_crlf_refuses_even_when_skillmirror_digest_is_unchanged(self):
        file = self.root / SKILL / 'pack/catalog.json'
        file.write_bytes(file.read_bytes().replace(b'\n', b'\r\n'))
        with self.assertRaisesRegex(ValueError, 'canonical LF'):
            stage.payload(self.root)

    def test_undeclared_symbolic_directory_refuses_even_when_empty(self):
        outside = pathlib.Path(self.temporary.name) / 'empty-outside'
        outside.mkdir()
        (self.root / SKILL / 'linked-directory').symlink_to(outside, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, 'symbolic'):
            stage.payload(self.root)

    def test_source_directory_cannot_be_a_staging_destination(self):
        with self.assertRaisesRegex(ValueError, 'staging destination'):
            stage.stage(self.root, self.root / SKILL)
        self.assertTrue((self.root / SKILL / 'SKILL.md').is_file())

    def test_actual_atlas_and_policy_refuse_altered_hash_and_reduced_seed(self):
        atlas = os.environ.get('FSGG_ATLAS_SCRIPT')
        if not atlas:
            self.skipTest('set FSGG_ATLAS_SCRIPT to the existing delivered helper')
        command = [sys.executable, '-I', '-B', str(ROOT / 'scripts/check-algorithms-pack.py'), '--atlas-script', atlas, '--skill', str(self.root / SKILL)]
        baseline = subprocess.run(command, capture_output=True, text=True)
        self.assertEqual(baseline.returncode, 0, baseline.stdout + baseline.stderr)
        archive = self.root / SKILL / 'pack'
        record = next((archive / 'records').glob('*.json'))
        original = record.read_bytes()
        record.write_bytes(original + b' ')
        altered = subprocess.run(command, capture_output=True, text=True)
        self.assertNotEqual(altered.returncode, 0)
        record.write_bytes(original)
        catalog = json.loads((archive / 'catalog.json').read_bytes())
        # Remove whole unlinked active entries, retaining a valid Atlas archive with 14 active techniques.
        records = {digest: json.loads((archive / 'records' / (digest + '.json')).read_bytes()) for digest in catalog['records']}
        predecessors = {digest for record in records.values() for digest in record['supersedes']}
        active_count = sum(digest not in predecessors for digest in records)
        remove_count = active_count - 14
        removable = [digest for digest, record in records.items() if not record['supersedes'] and digest not in predecessors][:remove_count]
        self.assertEqual(len(removable), remove_count)
        for digest in removable:
            (archive / 'records' / (digest + '.json')).unlink()
        catalog['records'] = [digest for digest in catalog['records'] if digest not in removable]
        (archive / 'catalog.json').write_bytes((json.dumps(catalog, sort_keys=True, separators=(',', ':'), ensure_ascii=False) + '\n').encode())
        reduced = subprocess.run(command, capture_output=True, text=True)
        self.assertNotEqual(reduced.returncode, 0)
        self.assertIn('fewer than 15', reduced.stderr)

    def test_policy_refuses_a_valid_atlas_record_without_the_technique_contract(self):
        atlas = os.environ.get('FSGG_ATLAS_SCRIPT')
        if not atlas:
            self.skipTest('set FSGG_ATLAS_SCRIPT to the existing delivered helper')
        archive = self.root / SKILL / 'pack'
        catalog = json.loads((archive / 'catalog.json').read_bytes())
        records = {digest: json.loads((archive / 'records' / (digest + '.json')).read_bytes()) for digest in catalog['records']}
        digest, record = next((digest, record) for digest, record in records.items() if record['id'] == 'algorithms/graph/best-first-search')
        record['summary'] = record['summary'].replace('## Pitfalls', '## Notes')
        body = (json.dumps(record, sort_keys=True, separators=(',', ':'), ensure_ascii=False) + '\n').encode()
        new = hashlib.sha256(body).hexdigest()
        (archive / 'records' / (digest + '.json')).unlink()
        (archive / 'records' / (new + '.json')).write_bytes(body)
        catalog['records'] = sorted(new if old == digest else old for old in catalog['records'])
        (archive / 'catalog.json').write_bytes((json.dumps(catalog, sort_keys=True, separators=(',', ':'), ensure_ascii=False) + '\n').encode())
        valid = subprocess.run([sys.executable, '-I', '-B', atlas, '--archive', str(archive), 'validate'], capture_output=True, text=True)
        self.assertEqual(valid.returncode, 0, valid.stderr)
        refused = subprocess.run([sys.executable, '-I', '-B', str(ROOT / 'scripts/check-algorithms-pack.py'), '--atlas-script', atlas, '--skill', str(self.root / SKILL)], capture_output=True, text=True)
        self.assertNotEqual(refused.returncode, 0)
        self.assertIn('headings', refused.stderr)


if __name__ == '__main__':
    unittest.main()
