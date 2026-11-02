#!/usr/bin/env python3
"""Release safety checks for .github/workflows/release.yml (FS.GG.Algorithms). Kept out of inline shell so
eng/release-checks.test.py can exercise every route against mocked responses.

  release-checks.py feed-state VERSION GITHUB_USER            prints "github=<bool> nuget=<bool>"
       (env GITHUB_TOKEN). A feed holds the version only on HTTP 200 and lacks it only on HTTP 404; any
       other status, an auth failure or a network error FAILS (exit 1). An unreadable feed is never absent.
  release-checks.py fresh-publish-allowed GITHUB NUGET        exit 1 unless both are "false"
  release-checks.py validate-source STATE_JSON ORIGINAL_SHA256 SOURCE_RUN VERSION COMMIT
       the resume source must be a fresh publish run of this version and commit whose recorded archive is
       the one being resumed: mode "publish", archiveRun == run == SOURCE_RUN, archiveSha256 equal to the
       ORIGINAL_SHA256 file's digest, and githubPackagesPush "success" (a refused publish is never a source)
  release-checks.py fetch-github VERSION GITHUB_USER OUT      download the copy GitHub Packages holds to OUT
       (env GITHUB_TOKEN); HTTP 200 only, anything else FAILS
  release-checks.py compare-held HELD_NUPKG ARCHIVE_NUPKG     exit 1 unless the bytes are identical

Exit codes: 0 ok, 1 refused, 2 usage. Pure stdlib.
"""
from __future__ import annotations

import hashlib
import json
import os
import sys
import urllib.error
import urllib.request

NUGET_INDEX = "https://api.nuget.org/v3/index.json"
GITHUB_INDEX = "https://nuget.pkg.github.com/FS-GG/index.json"
PACKAGE = "fs.gg.algorithms"


def refuse(message: str) -> "None":
    print(f"release-checks: REFUSED: {message}", file=sys.stderr)
    raise SystemExit(1)


def http_status(url: str, auth: tuple[str, str] | None = None) -> tuple[int, bytes]:
    """(status, body) for a GET; a network failure is status 0. Tests replace this function."""
    request = urllib.request.Request(url)
    if auth:
        import base64
        token = base64.b64encode(f"{auth[0]}:{auth[1]}".encode()).decode()
        request.add_header("Authorization", f"Basic {token}")
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            return response.status, response.read()
    except urllib.error.HTTPError as error:
        return error.code, b""
    except (urllib.error.URLError, OSError):
        return 0, b""


def base_address(index: str, auth: tuple[str, str] | None) -> str:
    status, body = http_status(index, auth)
    if status != 200:
        refuse(f"service index {index} answered {status or 'no response'}")
    try:
        resources = json.loads(body)["resources"]
        return next(r["@id"] for r in resources if str(r["@type"]).startswith("PackageBaseAddress")).rstrip("/")
    except (ValueError, KeyError, StopIteration):
        refuse(f"service index {index} has no PackageBaseAddress")


def holds(index: str, version: str, auth: tuple[str, str] | None) -> bool:
    v = version.lower()
    url = f"{base_address(index, auth)}/{PACKAGE}/{v}/{PACKAGE}.{v}.nupkg"
    status, _ = http_status(url, auth)
    if status == 200:
        return True
    if status == 404:
        return False
    refuse(f"{url} answered {status or 'no response'}: neither present (200) nor absent (404)")


def main(argv: list[str]) -> int:
    if len(argv) == 4 and argv[1] == "feed-state":
        version, user = argv[2], argv[3]
        token = os.environ.get("GITHUB_TOKEN", "")
        if not token:
            refuse("GITHUB_TOKEN is not set")
        github = holds(GITHUB_INDEX, version, (user, token))
        nuget = holds(NUGET_INDEX, version, None)
        print(f"github={str(github).lower()} nuget={str(nuget).lower()}")
        return 0
    if len(argv) == 4 and argv[1] == "fresh-publish-allowed":
        if argv[2] != "false" or argv[3] != "false":
            refuse(f"the version is already on a feed (github={argv[2]} nuget.org={argv[3]}): never re-pack; "
                   "dispatch mode resume with the source_run_id of the publish run whose archive was pushed")
        return 0
    if len(argv) == 7 and argv[1] == "validate-source":
        state_path, sha_path, source, version, commit = argv[2:]
        try:
            state = json.load(open(state_path))
        except (OSError, ValueError) as error:
            refuse(f"cannot read the source run's publish-state.json: {error}")
        recorded = open(sha_path).read().split()[0]
        checks = {
            "mode is publish": state.get("mode") == "publish",
            "run is the source run": str(state.get("run")) == source,
            "archiveRun is the source run": str(state.get("archiveRun")) == source,
            "version matches": state.get("version") == version,
            "commit matches": state.get("commit") == commit,
            "archiveSha256 equals the archive's recorded sha256": state.get("archiveSha256") == recorded,
            # A refused or failed publish never pushed anything, so it cannot be the original of what a feed holds.
            "the GitHub Packages push succeeded": state.get("githubPackagesPush") == "success",
        }
        failed = [name for name, ok in checks.items() if not ok]
        if failed:
            refuse("the resume source is not the original publish of this archive: " + "; ".join(failed))
        print(f"release-checks: source run {source} is the original publish of {version} (archive {recorded})")
        return 0
    if len(argv) == 5 and argv[1] == "fetch-github":
        version, user, out = argv[2:]
        token = os.environ.get("GITHUB_TOKEN", "")
        if not token:
            refuse("GITHUB_TOKEN is not set")
        v = version.lower()
        url = f"{base_address(GITHUB_INDEX, (user, token))}/{PACKAGE}/{v}/{PACKAGE}.{v}.nupkg"
        status, body = http_status(url, (user, token))
        if status != 200 or not body:
            refuse(f"{url} answered {status or 'no response'} while fetching the held copy")
        open(out, "wb").write(body)
        print(f"release-checks: fetched the GitHub Packages copy ({len(body)} bytes)")
        return 0
    if len(argv) == 4 and argv[1] == "compare-held":
        held, archive = (hashlib.sha256(open(p, "rb").read()).hexdigest() for p in argv[2:])
        if held != archive:
            refuse(f"GitHub Packages holds {held}, not the archive {archive}: a re-pack must never be pushed")
        print(f"release-checks: GitHub Packages holds the archive's exact bytes ({archive})")
        return 0
    print(__doc__, file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
