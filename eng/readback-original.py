#!/usr/bin/env python3
"""Read-only custody proof for an original Algorithms publish; no pack or feed write."""
import datetime
import hashlib
import io
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time
import urllib.parse
import zipfile

REPO = "FS-GG/FS.GG.Algorithms"
PRODUCER = ".github/workflows/release.yml"
CHECKS = Path(__file__).with_name("release-checks.py")


def require(value, reason):
    if not value:
        raise ValueError(reason)


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def inputs(env, now):
    values = {k: env.get("INPUT_" + k, "") for k in ("ORIGINAL_RUN", "SOURCE", "VERSION", "SHA256", "PRODUCER_SHA256", "DEADLINE_UTC")}
    require(re.fullmatch(r"[1-9][0-9]*", values["ORIGINAL_RUN"]), "original run must be numeric")
    require(re.fullmatch(r"[0-9a-f]{40}", values["SOURCE"]), "source must be an exact commit")
    require(re.fullmatch(r"0\.[0-9]+\.[0-9]+", values["VERSION"]), "version must be stable 0.x")
    for key in ("SHA256", "PRODUCER_SHA256"):
        require(re.fullmatch(r"[0-9a-f]{64}", values[key]), "expected digest must be SHA256")
    until = datetime.datetime.fromisoformat(values["DEADLINE_UTC"].replace("Z", "+00:00"))
    require(until.tzinfo is not None and until > now, "original deadline expired or lacks timezone")
    return values, min(480, (until - now).total_seconds())


def qualify_native(v, run, jobs, artifacts, tag):
    source, version, original = v["SOURCE"], v["VERSION"], int(v["ORIGINAL_RUN"])
    require(run["id"] == original and run["repository"]["full_name"] == REPO, "wrong original repository/run")
    require(run["path"] == PRODUCER and run["event"] == "push" and run["run_attempt"] == 1, "not an original tag publish attempt")
    require(run["head_sha"] == source and run["head_branch"] == "v" + version and run["status"] == "completed", "original source/ref incomplete")
    require(tag["object"]["sha"] == source and tag["object"]["type"] == "commit", "tag moved or unsupported tag type")
    require(len(jobs) == 3 and {j["name"] for j in jobs} == {"verify", "publish FS.GG.Algorithms [org-publication-writer]", "readback"}, "incomplete original job population")
    for job in jobs:
        require(job["run_id"] == original and job["run_attempt"] == 1 and job["head_sha"] == source and job["status"] == "completed", "original job identity differs")
    verify = next(j for j in jobs if j["name"] == "verify")
    publisher = next(j for j in jobs if "[org-publication-writer]" in j["name"])
    require(verify["conclusion"] == "success", "original verification did not succeed")
    for name in ("Verify the archive's sha256 against its recorded original", "Feed state before any push", "Push to the org GitHub Packages feed"):
        steps = [s for s in publisher["steps"] if s["name"] == name]
        require(len(steps) == 1 and steps[0]["conclusion"] == "success", "original publisher prerequisite missing: " + name)
    names = (f"fs-gg-algorithms-release-{version}-{source}", f"fs-gg-algorithms-publish-state-{version}-{original}-1")
    require(len(artifacts) == 2 and {a["name"] for a in artifacts} == set(names), "ambiguous or missing original artifacts")
    selected = [next(a for a in artifacts if a["name"] == name) for name in names]
    for artifact in selected:
        require(0 < artifact["size_in_bytes"] <= 8 * 1024 * 1024, "original artifact size unsupported")
        require(not artifact["expired"] and artifact["workflow_run"]["id"] == original and artifact["workflow_run"]["head_sha"] == source, "original artifact identity differs")
    require(datetime.datetime.fromisoformat(selected[0]["created_at"].replace("Z", "+00:00")) <= datetime.datetime.fromisoformat(publisher["started_at"].replace("Z", "+00:00")), "original archive was not retained before publisher")
    return selected


def artifact_files(raw, metadata):
    require("sha256:" + sha(raw) == metadata["digest"], "native artifact digest differs")
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        names = archive.namelist()
        require(len(names) == len(set(names)) and len(names) <= 4, "ambiguous artifact members")
        require(sum(i.file_size for i in archive.infolist()) <= 16 * 1024 * 1024, "artifact too large")
        require(all(not n.startswith("/") and ".." not in Path(n).parts for n in names), "unsafe artifact member")
        return {Path(n).name: archive.read(n) for n in names}


class Native:
    def __init__(self, deadline):
        self.deadline = deadline

    def call(self, args, *, binary=False):
        left = self.deadline - time.monotonic()
        require(left > 0, "original proof read budget expired")
        result = subprocess.run(args, capture_output=True, timeout=min(30, left))
        require(result.returncode == 0, "read-only native command failed; no retry")
        return result.stdout if binary else json.loads(result.stdout)

    def get(self, path, pages=False, binary=False):
        return self.call(["gh", "api", "--method", "GET"] + (["--paginate", "--slurp"] if pages else []) + [f"repos/{REPO}/" + path], binary=binary)

    def population(self, path, field):
        pages = self.get(path + "?per_page=100", pages=True)
        rows = [r for page in pages for r in page[field]]
        require(len(rows) == pages[0]["total_count"], "incomplete native pagination")
        return rows

    def check(self, *args):
        left = self.deadline - time.monotonic()
        require(left > 0, "original proof budget expired")
        try:
            result = subprocess.run([sys.executable, str(CHECKS), *map(str, args)], capture_output=True, timeout=min(65, left))
        except subprocess.TimeoutExpired:
            print("org-original-proof: checker " + json.dumps({"phase": str(args[0]), "outcome": "timeout", "exitCode": None}), flush=True)
            raise ValueError("maintained release check timeout: " + str(args[0])) from None
        if result.returncode:
            # These fields cannot contain checker stderr, URLs, headers or credentials.
            print("org-original-proof: checker " + json.dumps({"phase": str(args[0]), "outcome": "refused", "exitCode": result.returncode}), flush=True)
        require(result.returncode == 0, "maintained release check refused: " + str(args[0]))
        # Successful checker output contains public package identities/digests, never credentials.
        print(result.stdout.decode(), end="", flush=True)

    def fetch_org(self, version, user, target, token):
        """Use the maintained release readback's curl -fsSL Basic semantics, once per GET.

        Credentials enter curl through stdin, never argv. Curl's default redirect policy
        strips authentication on cross-host redirects; location-trusted is never enabled.
        Only a same-origin HTTPS PackageBaseAddress may receive the initial Basic auth.
        """
        diagnostics = []

        def record(phase, outcome, status=None, exit_code=None):
            row = {"phase": phase, "outcome": outcome, "httpStatus": status, "curlExit": exit_code}
            diagnostics.append(row)
            (target.parent / "transport-status.json").write_text(json.dumps(diagnostics, indent=2) + "\n")
            print("org-original-proof: transport " + json.dumps(row), flush=True)

        def get(phase, url, out):
            left = self.deadline - time.monotonic()
            require(left > 0, "original proof read budget expired")
            # Inputs are never shell code or curl config directives.
            require(user and token and all(c not in user + token for c in '\r\n\x00'), "unsupported credential characters")
            credential = (user + ":" + token).replace("\\", "\\\\").replace('"', '\\"')
            # Disable implicit curlrc options, which could otherwise weaken redirect auth policy.
            args = ["curl", "-q", "-fsSL", "--proto", "=https", "--proto-redir", "=https",
                    "--max-time", str(left), "--connect-timeout", str(min(10, left)),
                    "--config", "-", "--output", str(out), "--write-out", "%{http_code}", url]
            try:
                result = subprocess.run(args, input=('user = "' + credential + '"\n').encode(),
                                        capture_output=True, timeout=left)
            except subprocess.TimeoutExpired:
                record(phase, "timeout")
                raise ValueError("org transport timeout: " + phase) from None
            except OSError:
                record(phase, "unavailable")
                raise ValueError("org transport unavailable: " + phase) from None
            status = int(result.stdout) if re.fullmatch(rb"[0-9]{3}", result.stdout) else None
            outcome = "success" if result.returncode == 0 and status == 200 else "timeout" if result.returncode == 28 else "refused"
            record(phase, outcome, status, result.returncode)
            require(outcome == "success", "org transport refused: " + phase)

        index = target.parent / "org-index.json"
        try:
            get("org-index", "https://nuget.pkg.github.com/FS-GG/index.json", index)
            index_raw = index.read_bytes()
        finally:
            # A service index can contain protocol-internal URLs. Never retain it as custody.
            index.unlink(missing_ok=True)
        try:
            resources = json.loads(index_raw)["resources"]
            addresses = [r["@id"] for r in resources if str(r["@type"]).startswith("PackageBaseAddress")]
            require(len(addresses) == 1 and isinstance(addresses[0], str), "ambiguous org package base address")
            base = urllib.parse.urlsplit(addresses[0])
            require(base.scheme == "https" and base.netloc == "nuget.pkg.github.com" and not base.query and not base.fragment,
                    "org package base address is not same-origin HTTPS")
        except (KeyError, TypeError, ValueError):
            record("org-index-schema", "refused")
            raise ValueError("org index schema refused") from None
        get("org-package", addresses[0].rstrip("/") + f"/fs.gg.algorithms/{version}/fs.gg.algorithms.{version}.nupkg", target)
        if not target.is_file() or target.stat().st_size == 0:
            record("org-package-body", "refused")
            raise ValueError("org package was empty")


def prove(v, api, work, env):
    original, version, source = v["ORIGINAL_RUN"], v["VERSION"], v["SOURCE"]
    run = api.get("actions/runs/" + original)
    jobs = api.population(f"actions/runs/{original}/attempts/1/jobs", "jobs")
    artifacts = api.population(f"actions/runs/{original}/artifacts", "artifacts")
    tag = api.get("git/ref/tags/v" + version)
    selected = qualify_native(v, run, jobs, artifacts, tag)
    members = [artifact_files(api.get(f"actions/artifacts/{a['id']}/zip", binary=True), a) for a in selected]
    filename = f"FS.GG.Algorithms.{version}.nupkg"
    require(set(members[0]) == {filename, "ORIGINAL_SHA256"} and set(members[1]) == {"publish-state.json"}, "original archive layout differs")
    package = members[0][filename]
    require(sha(package) == v["SHA256"] and members[0]["ORIGINAL_SHA256"].decode().split()[0] == v["SHA256"], "original package digest differs from selected custody")
    original_path, hash_path, state_path, held = work / filename, work / "ORIGINAL_SHA256", work / "publish-state.json", work / "github-held.nupkg"
    original_path.write_bytes(package); hash_path.write_bytes(members[0]["ORIGINAL_SHA256"]); state_path.write_bytes(members[1]["publish-state.json"])
    api.check("validate-source", state_path, hash_path, original, version, source)
    api.fetch_org(version, env["GITHUB_ACTOR"], held, env["GITHUB_TOKEN"])
    api.check("compare-held", held, original_path)
    after = api.get("git/ref/tags/v" + version)
    require(after["object"]["sha"] == source and after["object"]["type"] == "commit", "tag changed during proof")
    run_after = api.get("actions/runs/" + original)
    require(run_after["run_attempt"] == 1 and run_after["head_sha"] == source and run_after["status"] == "completed", "original run changed during proof")
    proof = {"repository": REPO, "version": version, "tag": "v" + version, "source": source, "originalRun": int(original), "originalAttempt": 1,
             "originalArchiveArtifact": selected[0]["id"], "originalArchiveArtifactDigest": selected[0]["digest"], "originalStateArtifact": selected[1]["id"], "originalStateArtifactDigest": selected[1]["digest"],
             "originalArchiveSha256": sha(package), "orgWholeFileSha256": sha(held.read_bytes()), "originalBytes": len(package), "orgBytes": held.stat().st_size, "wholeFileEqual": True,
             "proofWorkflowSource": env["GITHUB_SHA"], "proofRun": env["GITHUB_RUN_ID"], "proofAttempt": 1, "packageWrites": False, "originalDeadlineUtc": v["DEADLINE_UTC"]}
    pending = work / "proof.tmp"
    pending.write_text(json.dumps(proof, indent=2) + "\n")
    pending.replace(work / "proof.json")
    return proof


def main():
    env = os.environ
    require(env.get("GITHUB_REPOSITORY") == REPO and env.get("GITHUB_EVENT_NAME") == "workflow_dispatch" and env.get("GITHUB_REF") == "refs/heads/main" and env.get("GITHUB_RUN_ATTEMPT") == "1", "requires a fresh protected-main proof dispatch")
    v, seconds = inputs(env, datetime.datetime.now(datetime.timezone.utc))
    deadline = time.monotonic() + seconds
    require(subprocess.run(["git", "merge-base", "--is-ancestor", v["SOURCE"], "HEAD"], capture_output=True, timeout=15).returncode == 0, "publication source is not protected-main ancestry")
    producer = subprocess.run(["git", "show", v["SOURCE"] + ":" + PRODUCER], capture_output=True, check=True, timeout=15).stdout
    require(sha(producer) == v["PRODUCER_SHA256"], "original producer source digest differs")
    work = Path(env["PROOF_WORKDIR"]);work.mkdir(exist_ok=False)
    result = prove(v, Native(deadline), work, env)
    print("org-original-proof: exact original whole-file match " + result["orgWholeFileSha256"])


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        # Closed errors only: never print transport diagnostics, URLs or credentials.
        print("org-original-proof: REFUSED: " + (str(error) if isinstance(error, ValueError) else type(error).__name__), file=sys.stderr)
        raise SystemExit(1)
