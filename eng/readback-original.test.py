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
from unittest import mock
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

    def execute(self, held=b"original", wrong_state=False, fetch_failure=False):
        outer = self
        class Fixture:
            def __init__(self):self.feedReads = 0;self.checks = []
            def get(self, path, **kwargs):
                if path.startswith("actions/runs/"):return outer.run
                if path.startswith("git/ref/"):return outer.tag
                return outer.raw[int(path.split("/")[2]) - 1]
            def population(self, path, field):return outer.jobs if field == "jobs" else outer.artifacts
            def check(self, *args):
                self.checks.append(args[0])
                result = subprocess.run([sys.executable, str(proof.CHECKS), *map(str, args)], capture_output=True)
                if result.returncode:raise ValueError("maintained checker refused")
            def fetch_org(self, version, user, target, token):
                self.feedReads += 1;target.write_bytes(held)
                if fetch_failure:raise ValueError("inert partial transfer refusal")
        fixture = Fixture()
        with tempfile.TemporaryDirectory() as temp:
            try:
                value = proof.prove(self.v, fixture, Path(temp), {"GITHUB_ACTOR": "fixture", "GITHUB_TOKEN": "inert-fixture", "GITHUB_SHA": "e" * 40, "GITHUB_RUN_ID": "456"})
                return value, fixture.feedReads
            except Exception:
                self.assertFalse((Path(temp) / "proof.json").exists())
                if fetch_failure:
                    self.assertEqual((Path(temp) / "github-held.nupkg").read_bytes(), held)
                    self.assertNotIn("compare-held", fixture.checks)
                raise

    def test_actual_maintained_state_and_whole_file_checks(self):
        result, calls = self.execute();self.assertTrue(result["wholeFileEqual"]);self.assertEqual(calls, 1)
        with self.assertRaises(ValueError):self.execute(held=b"different bytes")
        with self.assertRaises(ValueError):self.execute(held=b"partial", fetch_failure=True)
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


class TransportTests(unittest.TestCase):
    def test_expired_budget_starts_no_transport_or_checker(self):
        with tempfile.TemporaryDirectory() as temp, mock.patch.object(proof.subprocess, "run") as child:
            native = proof.Native(proof.time.monotonic() - 1)
            with self.assertRaises(ValueError):native.fetch_org("0.2.0", "fixture", Path(temp) / "held", "inert")
            with self.assertRaises(ValueError):native.check("compare-held", "held", "original")
            child.assert_not_called()

    def test_checker_failure_diagnostics_never_replay_child_output(self):
        import contextlib
        for value in (subprocess.CompletedProcess([], 1, b"inert-secret URL", b"inert-secret header"), subprocess.TimeoutExpired([], 1, stderr=b"inert-secret URL")):
            output = io.StringIO()
            with mock.patch.object(proof.subprocess, "run", side_effect=value if isinstance(value, Exception) else None, return_value=value), contextlib.redirect_stdout(output):
                with self.assertRaises(ValueError):proof.Native(proof.time.monotonic() + 10).check("compare-held", "held", "original")
            self.assertNotIn("inert-secret", output.getvalue())
            row = json.loads(output.getvalue().split("checker ", 1)[1])
            self.assertEqual(row["phase"], "compare-held")
            self.assertIn(row["outcome"], ("refused", "timeout"))

    def exercise(self, failure=None, address="https://nuget.pkg.github.com/FS-GG/download", index_body=None):
        calls = []
        with tempfile.TemporaryDirectory() as temp:
            target = Path(temp) / "github-held.nupkg"
            def child(args, **kwargs):
                phase = "org-index" if not calls else "org-package"
                calls.append(args)
                self.assertEqual(args[0:3], ["curl", "-q", "-fsSL"])
                self.assertNotIn("--location-trusted", args)
                self.assertEqual(args[args.index("--proto-redir") + 1], "=https")
                self.assertEqual(args[args.index("--proto") + 1], "=https")
                self.assertNotIn("inert-secret", " ".join(args))
                self.assertEqual(kwargs["input"], b'user = "fixture:inert-secret"\n')
                self.assertGreater(kwargs["timeout"], 0)
                self.assertLessEqual(kwargs["timeout"], 30)
                out = Path(args[args.index("--output") + 1])
                body = json.dumps({"resources": [{"@type": "PackageBaseAddress/3.0.0", "@id": address}]}).encode() if phase == "org-index" else b"original"
                out.write_bytes(index_body if phase == "org-index" and index_body is not None else body)
                if failure and failure[0] == phase:
                    if failure[1] == "timeout":raise subprocess.TimeoutExpired(args, 30, stderr=b"inert-secret raw-url")
                    if failure[1] == "missing-tool":raise FileNotFoundError("inert-secret path")
                    return subprocess.CompletedProcess(args, failure[1], failure[2], b"inert-secret raw-url header")
                return subprocess.CompletedProcess(args, 0, b"200", b"")
            native = proof.Native(proof.time.monotonic() + 30)
            with mock.patch.object(proof.subprocess, "run", side_effect=child):
                try:
                    native.fetch_org("0.2.0", "fixture", target, "inert-secret")
                    result = "success"
                except ValueError:result = "refused"
            diagnostics = (Path(temp) / "transport-status.json").read_text()
            self.assertNotIn("inert-secret", diagnostics);self.assertNotIn("http", diagnostics.replace("httpStatus", ""))
            self.assertFalse((Path(temp) / "org-index.json").exists())
            self.assertFalse((Path(temp) / "proof.json").exists())
            return result, len(calls), json.loads(diagnostics)

    def test_one_get_each_success_without_credential_argv(self):
        result, calls, rows = self.exercise()
        self.assertEqual((result, calls), ("success", 2))
        self.assertEqual([r["httpStatus"] for r in rows], [200, 200])

    def test_http_network_timeout_and_malformed_status_stop_without_retry(self):
        for phase in ("org-index", "org-package"):
            for failure in ((22, b"401"), (22, b"403"), (22, b"404"), (22, b"500"), (7, b"000"), (28, b"000"), (0, b"302"), (0, b"not-status"), ("timeout", b""), ("missing-tool", b"")):
                with self.subTest(phase=phase, failure=failure):
                    result, calls, rows = self.exercise((phase, *failure))
                    self.assertEqual(result, "refused")
                    self.assertEqual(calls, 1 if phase == "org-index" else 2)
                    self.assertNotEqual(rows[-1]["outcome"], "success")

    def test_index_refuses_untrusted_initial_auth_destination(self):
        for address in ("https://elsewhere.invalid/download", "http://nuget.pkg.github.com/download", "https://fixture:secret@nuget.pkg.github.com/download", "https://nuget.pkg.github.com/download?capability=fixture", "https://nuget.pkg.github.com/download#fragment"):
            with self.subTest(address=address):
                result, calls, rows = self.exercise(address=address)
                self.assertEqual((result, calls), ("refused", 1))
                self.assertEqual(rows[-1]["phase"], "org-index-schema")
        result, calls, _ = self.exercise(index_body=b"not JSON")
        self.assertEqual((result, calls), ("refused", 1))

    def test_curl_default_redirect_does_not_forward_basic_to_another_host(self):
        # Real curl, inert loopback servers and synthetic credentials only. Production
        # additionally restricts both initial and redirect protocols to HTTPS.
        import http.server
        import threading
        seen = []
        class Sink(http.server.BaseHTTPRequestHandler):
            def log_message(self, *args):pass
            def do_GET(self):
                seen.append(("sink", self.headers.get("Authorization")))
                self.send_response(200);self.end_headers();self.wfile.write(b"fixture")
        sink = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Sink)
        class Redirect(Sink):
            def do_GET(self):
                seen.append(("source", self.headers.get("Authorization")))
                self.send_response(302);self.send_header("Location", f"http://localhost:{sink.server_port}/fixture");self.end_headers()
        source = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Redirect)
        threads = [threading.Thread(target=s.serve_forever, daemon=True) for s in (source, sink)]
        for thread in threads:thread.start()
        try:
            result = subprocess.run(["curl", "-fsSL", "--noproxy", "*", "--max-time", "5", "--config", "-", f"http://127.0.0.1:{source.server_port}/fixture"], input=b'user = "fixture:inert-secret"\n', capture_output=True, timeout=6)
            self.assertEqual(result.returncode, 0)
            self.assertEqual(result.stdout, b"fixture")
            self.assertEqual(seen, [("source", "Basic Zml4dHVyZTppbmVydC1zZWNyZXQ="), ("sink", None)])
            seen.clear()
            denied = subprocess.run(["curl", "-fsSL", "--noproxy", "*", "--proto-redir", "=https", "--max-time", "5", "--config", "-", f"http://127.0.0.1:{source.server_port}/fixture"], input=b'user = "fixture:inert-secret"\n', capture_output=True, timeout=6)
            self.assertNotEqual(denied.returncode, 0)
            self.assertEqual([row[0] for row in seen], ["source"])
        finally:
            for server in (source, sink):server.shutdown();server.server_close()
            for thread in threads:thread.join(timeout=2)


if __name__ == "__main__":unittest.main()
