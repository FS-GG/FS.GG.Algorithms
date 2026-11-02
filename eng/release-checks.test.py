#!/usr/bin/env python3
"""A dry harness for eng/release-checks.py: every route against mocked HTTP responses and synthetic files.
No network. Run: python3 -I eng/release-checks.test.py"""
import importlib.util
import io
import json
import os
import sys
import tempfile
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("release_checks", HERE / "release-checks.py")
rc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(rc)

INDEX = json.dumps({"resources": [{"@type": "PackageBaseAddress/3.0.0", "@id": "https://feed/flat/"}]}).encode()
failures = []
checks = 0


def run(argv, responses=None, env=None):
    """Exit code and output of main(argv), with http_status answering from `responses` (url -> status)."""
    responses = responses or {}

    def fake(url, auth=None):
        if url.endswith("index.json"):
            return responses.get("index", 200), INDEX
        status = responses.get(url.rsplit("/", 1)[-1], 404)
        return status, (b"held bytes" if status == 200 else b"")

    rc.http_status = fake
    old = dict(os.environ)
    os.environ.update(env or {"GITHUB_TOKEN": "t"})
    out, err = io.StringIO(), io.StringIO()
    try:
        with redirect_stdout(out), redirect_stderr(err):
            code = rc.main(["release-checks.py", *argv])
    except SystemExit as e:
        code = e.code
    finally:
        os.environ.clear()
        os.environ.update(old)
    return code, out.getvalue() + err.getvalue()


def expect(name, cond):
    global checks
    checks += 1
    if not cond:
        failures.append(name)


nupkg = "fs.gg.algorithms.0.1.0.nupkg"
# feed-state: only 200 is present, only 404 is absent, anything else fails closed (on both feeds).
code, out = run(["feed-state", "0.1.0", "u"], {nupkg: 404})
expect("404 on both feeds is absent", code == 0 and "github=false nuget=false" in out)
code, out = run(["feed-state", "0.1.0", "u"], {nupkg: 200})
expect("200 is present", code == 0 and "github=true nuget=true" in out)
for status in (500, 502, 401, 403, 0, 301):
    code, out = run(["feed-state", "0.1.0", "u"], {nupkg: status})
    expect(f"status {status} fails closed", code == 1 and "REFUSED" in out)
code, out = run(["feed-state", "0.1.0", "u"], {"index": 503})
expect("an unreadable service index fails closed", code == 1)
code, out = run(["feed-state", "0.1.0", "u"], {nupkg: 404}, env={"GITHUB_TOKEN": ""})
expect("a missing token fails closed", code == 1)

# fresh-publish-allowed
expect("fresh publish on empty feeds", run(["fresh-publish-allowed", "false", "false"])[0] == 0)
expect("fresh publish refused when GitHub holds it", run(["fresh-publish-allowed", "true", "false"])[0] == 1)
expect("fresh publish refused when nuget.org holds it", run(["fresh-publish-allowed", "false", "true"])[0] == 1)

# validate-source and compare-held
with tempfile.TemporaryDirectory() as d:
    d = Path(d)
    archive = d / "a.nupkg"
    archive.write_bytes(b"original archive bytes")
    import hashlib
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    (d / "ORIGINAL_SHA256").write_text(f"{digest}  FS.GG.Algorithms.0.1.0.nupkg\n")
    good = {"mode": "publish", "run": "77", "archiveRun": "77", "version": "0.1.0", "commit": "c0ffee", "archiveSha256": digest}

    def source(state, run_id="77"):
        (d / "state.json").write_text(json.dumps(state))
        return run(["validate-source", str(d / "state.json"), str(d / "ORIGINAL_SHA256"), run_id, "0.1.0", "c0ffee"])[0]

    expect("the original publish run is accepted", source(good) == 0)
    expect("a login-preflight run is refused", source({**good, "mode": "login-preflight"}) == 1)
    expect("a resume run is refused as a source", source({**good, "mode": "resume"}) == 1)
    expect("another run's state is refused", source(good, run_id="78") == 1)
    expect("an archive from another run is refused", source({**good, "archiveRun": "12"}) == 1)
    expect("another commit is refused", source({**good, "commit": "beef"}) == 1)
    expect("another version is refused", source({**good, "version": "0.1.1"}) == 1)
    expect("a different archive (re-pack) is refused", source({**good, "archiveSha256": "0" * 64}) == 1)
    (d / "missing.json").unlink(missing_ok=True)
    expect("a missing publish-state is refused",
           run(["validate-source", str(d / "missing.json"), str(d / "ORIGINAL_SHA256"), "77", "0.1.0", "c0ffee"])[0] == 1)

    held = d / "held.nupkg"
    held.write_bytes(archive.read_bytes())
    expect("identical bytes on GitHub Packages pass", run(["compare-held", str(held), str(archive)])[0] == 0)
    held.write_bytes(b"a payload-identical re-pack with other bytes")
    expect("a re-pack on GitHub Packages is refused", run(["compare-held", str(held), str(archive)])[0] == 1)

with tempfile.TemporaryDirectory() as d:
    out_file = Path(d) / "held.nupkg"
    code, _ = run(["fetch-github", "0.1.0", "u", str(out_file)], {nupkg: 200})
    expect("fetch-github writes the held copy on 200", code == 0 and out_file.read_bytes() == b"held bytes")
    for status in (404, 500, 401, 0):
        expect(f"fetch-github fails on {status}", run(["fetch-github", "0.1.0", "u", str(out_file)], {nupkg: status})[0] == 1)

expect("usage errors exit 2", run(["nonsense"])[0] == 2)
print(f"release-checks tests: {checks} checks, {len(failures)} failed")
for f in failures:
    print(f"  FAILED: {f}")
sys.exit(1 if failures else 0)
