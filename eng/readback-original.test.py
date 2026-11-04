#!/usr/bin/env python3
"""Causal custody boundaries using synthetic native metadata and local byte fixtures only."""
import copy
import datetime
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location("proof", Path(__file__).with_name("readback-original.py"))
proof = importlib.util.module_from_spec(spec);spec.loader.exec_module(proof)


def archive(files):
    target = io.BytesIO()
    with zipfile.ZipFile(target, "w") as z:
        for name, data in files.items():z.writestr(name, data)
    return target.getvalue()


class CustodyTests(unittest.TestCase):
    def setUp(self):
        self.source = "a" * 40
        self.v = {"ORIGINAL_RUN": "123", "SOURCE": self.source, "VERSION": "0.2.0", "SHA256": proof.sha(b"original"), "PRODUCER_SHA256": "b" * 64, "DEADLINE_UTC": "2030-01-01T00:00:00Z"}
        self.run = {"id": 123, "repository": {"full_name": proof.REPO}, "path": proof.PRODUCER, "event": "push", "run_attempt": 1, "head_sha": self.source, "head_branch": "v0.2.0", "status": "completed", "conclusion": "failure"}
        self.tag = {"object": {"sha": self.source, "type": "commit"}}
        names = ("verify", "publish FS.GG.Algorithms [org-publication-writer]", "readback")
        self.jobs = [{"name": name, "run_id": 123, "run_attempt": 1, "head_sha": self.source, "status": "completed", "conclusion": "failure" if name == "readback" else "success", "started_at": "2030-01-01T00:00:10Z"} for name in names]
        self.jobs[1]["steps"] = [{"name": n, "conclusion": "success"} for n in ("Verify the archive's sha256 against its recorded original", "Feed state before any push", "Push to the org GitHub Packages feed")]
        self.state = {"package": "FS.GG.Algorithms", "version": "0.2.0", "commit": self.source, "archiveSha256": self.v["SHA256"], "archiveRun": "123", "run": "123", "mode": "publish", "githubPackagesPush": "success", "nugetOrgPush": "success"}
        bodies = [{"FS.GG.Algorithms.0.2.0.nupkg": b"original", "ORIGINAL_SHA256": self.v["SHA256"].encode() + b"  FS.GG.Algorithms.0.2.0.nupkg\n"}, {"publish-state.json": json.dumps(self.state).encode()}]
        self.raw = [archive(b) for b in bodies]
        self.artifacts = [{"id": i + 1, "name": name, "digest": "sha256:" + proof.sha(self.raw[i]), "expired": False, "size_in_bytes": len(self.raw[i]), "workflow_run": {"id": 123, "head_sha": self.source}, "created_at": "2030-01-01T00:00:00Z"} for i, name in enumerate((f"fs-gg-algorithms-release-0.2.0-{self.source}", "fs-gg-algorithms-publish-state-0.2.0-123-1"))]

    def qualify(self):return proof.qualify_native(self.v, self.run, self.jobs, self.artifacts, self.tag)

    def test_original_failed_readback_remains_valid_custody(self):
        self.assertEqual(len(self.qualify()), 2)

    def test_original_identity_guards(self):
        for field, bad in (("event", "workflow_dispatch"), ("run_attempt", 2), ("head_sha", "c" * 40), ("head_branch", "main"), ("path", "other.yml")):
            with self.subTest(field=field):
                old = self.run[field];self.run[field] = bad
                with self.assertRaises(ValueError):self.qualify()
                self.run[field] = old

    def test_missing_guard_step_or_expired_ambiguous_archive(self):
        original = copy.deepcopy(self.artifacts)
        for transform in (lambda a: a.append(copy.deepcopy(a[0])), lambda a: a.pop(), lambda a: a[0].update(expired=True), lambda a: a[0].update(size_in_bytes=16 * 1024 * 1024), lambda a: a[0].update(created_at="2030-01-01T00:00:11Z")):
            self.artifacts = copy.deepcopy(original);transform(self.artifacts)
            with self.assertRaises(ValueError):self.qualify()
        self.artifacts = original;self.jobs[1]["steps"][0]["conclusion"] = "skipped"
        with self.assertRaises(ValueError):self.qualify()

    def test_tag_and_native_artifact_digest_are_load_bearing(self):
        self.tag["object"]["sha"] = "d" * 40
        with self.assertRaises(ValueError):self.qualify()
        with self.assertRaises(ValueError):proof.artifact_files(self.raw[0] + b"corrupt", self.artifacts[0])

    def execute(self, held=b"original", wrong_state=False):
        outer = self
        class Fixture:
            def __init__(self):self.feedReads = 0
            def get(self, path, **kwargs):
                if path.startswith("actions/runs/"):return outer.run
                if path.startswith("git/ref/"):return outer.tag
                return outer.raw[int(path.split("/")[2]) - 1]
            def population(self, path, field):return outer.jobs if field == "jobs" else outer.artifacts
            def check(self, *args):
                if args[0] == "fetch-github":self.feedReads += 1;Path(args[3]).write_bytes(held);return
                result = subprocess.run([sys.executable, str(proof.CHECKS), *map(str, args)], capture_output=True)
                if result.returncode:raise ValueError("maintained checker refused")
        fixture = Fixture()
        with tempfile.TemporaryDirectory() as temp:
            try:
                value = proof.prove(self.v, fixture, Path(temp), {"GITHUB_ACTOR": "fixture", "GITHUB_SHA": "e" * 40, "GITHUB_RUN_ID": "456"})
                return value, fixture.feedReads
            except Exception:
                self.assertFalse((Path(temp) / "proof.json").exists())
                raise

    def test_actual_maintained_state_and_whole_file_checks(self):
        result, calls = self.execute();self.assertTrue(result["wholeFileEqual"]);self.assertEqual(calls, 1)
        with self.assertRaises(ValueError):self.execute(held=b"different bytes")
        self.state["mode"] = "resume";self.raw[1] = archive({"publish-state.json": json.dumps(self.state).encode()});self.artifacts[1]["digest"] = "sha256:" + proof.sha(self.raw[1])
        with self.assertRaises(ValueError):self.execute()

    def test_selected_archive_sha_cannot_be_replaced(self):
        self.v["SHA256"] = "f" * 64
        with self.assertRaises(ValueError):self.execute()

    def test_deadline_and_input_ranges(self):
        env = {"INPUT_" + k: v for k, v in self.v.items()};now = datetime.datetime(2029, 1, 1, tzinfo=datetime.timezone.utc)
        self.assertEqual(proof.inputs(env, now)[1], 480)
        for key, bad in (("ORIGINAL_RUN", "123;echo"), ("SOURCE", "a" * 39), ("VERSION", "0.2.0-preview"), ("SHA256", "g" * 64), ("DEADLINE_UTC", "2020-01-01T00:00:00Z")):
            changed = {**env, "INPUT_" + key: bad}
            with self.subTest(key=key), self.assertRaises(ValueError):proof.inputs(changed, now)


if __name__ == "__main__":unittest.main()
