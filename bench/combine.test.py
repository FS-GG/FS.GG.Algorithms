#!/usr/bin/env python3
"""Synthetic tests for bench/combine.py: three fake hosted runs, the median and spread arithmetic, and every
refusal. No .NET, no network. Run: python3 -I bench/combine.test.py"""
import copy
import json
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
checks, failures = 0, []


def expect(name, cond):
    global checks
    checks += 1
    if not cond:
        failures.append(name)


def run_doc(medians, run_id, work=7, image="20261101.1", sdk="10.0.403", dirty=False):
    def results(scale):
        return [{"kernel": "k/a", "size": 10, "workCount": work, "medianNs": medians[0] * scale, "p95Ns": medians[0] * scale * 2},
                {"kernel": "k/b", "size": 20, "workCount": 3, "medianNs": medians[1] * scale, "p95Ns": medians[1] * scale * 2}]
    return {
        "schema": "fsgg.algorithms.bench-results/v1",
        "package": {"id": "FS.GG.Algorithms", "version": "0.1.0"},
        "source": {"repository": "FS-GG/FS.GG.Algorithms", "commit": "a" * 40, "dirty": dirty},
        "host": {"name": "github-hosted-ubuntu24", "os": "Linux", "cpu": "AMD EPYC", "nproc": 4},
        "recordedAt": f"2026-11-02T0{run_id}:00:00+00:00",
        "admission": {"probe": "github-hosted runner", "verdict": "admit",
                      "observation": {"hosted": True, "imageOs": "ubuntu24", "imageVersion": image, "runId": str(run_id),
                                      "dotnetSdk": sdk, "node": "v26.11.1", "fable": "5.18.0"}},
        "runs": [{"runtime": "dotnet", "runtimeVersion": ".NET 10", "protocol": {"tool": "BDN"}, "results": results(1)},
                 {"runtime": "node", "runtimeVersion": "v26", "protocol": {"warmup": 5}, "results": results(10)}],
    }


def combine(docs):
    with tempfile.TemporaryDirectory() as d:
        paths = []
        for i, doc in enumerate(docs):
            p = Path(d) / f"run{i}.json"
            p.write_text(json.dumps(doc))
            paths.append(str(p))
        out = Path(d) / "out.json"
        r = subprocess.run([sys.executable, "-I", str(HERE / "combine.py"), "--out", str(out), *paths], capture_output=True, text=True)
        return r.returncode, (json.loads(out.read_text()) if r.returncode == 0 else None), r.stderr


good = [run_doc([100, 1000], 1), run_doc([110, 900], 2, image="20261101.2"), run_doc([130, 950], 3)]
code, doc, _ = combine(good)
expect("three good runs combine", code == 0)
if doc:
    a = next(x for x in doc["runs"][0]["results"] if x["kernel"] == "k/a")
    expect("median of the per-run medians", a["medianNs"] == 110)
    expect("median of the per-run p95s", a["p95Ns"] == 220)
    expect("spread min and max", a["spread"]["minMedianNs"] == 100 and a["spread"]["maxMedianNs"] == 130)
    expect("spread runs", a["spread"]["runs"] == 3)
    expect("cv is pstdev over mean", abs(a["spread"]["cv"] - round(12.472191289246473 / 113.33333333333333, 4)) < 1e-9)
    expect("toolchain records both runner images", doc["toolchain"]["runnerImages"] == ["ubuntu24 20261101.1", "ubuntu24 20261101.2"])
    expect("toolchain records the SDK, Node and Fable", (doc["toolchain"]["dotnetSdk"], doc["toolchain"]["node"], doc["toolchain"]["fable"]) == ("10.0.403", "v26.11.1", "5.18.0"))
    expect("each run's admission is kept", len(doc["admission"]["observation"]["runs"]) == 3)
    expect("the note says regression comparison only", "regression comparison" in doc["toolchain"]["note"])

def refused(docs):
    return combine(docs)[0] == 1

bad = copy.deepcopy(good); bad[1]["runs"][0]["results"][0]["workCount"] = 8
expect("differing work counts across runs are refused", refused(bad))
bad = copy.deepcopy(good); bad[2]["source"]["commit"] = "b" * 40
expect("another commit is refused", refused(bad))
expect("a dirty tree is refused", refused([good[0], run_doc([1, 1], 2, dirty=True)]))
expect("another SDK is refused", refused([good[0], run_doc([1, 1], 2, sdk="10.0.500")]))
bad = copy.deepcopy(good); bad[1]["admission"]["observation"]["hosted"] = False
expect("a non-hosted run is refused", refused(bad))
bad = copy.deepcopy(good); bad[1]["runs"][1]["results"][0]["workCount"] = 9; bad[0]["runs"][1]["results"][0]["workCount"] = 9; bad[2]["runs"][1]["results"][0]["workCount"] = 9
expect(".NET and Node work counts must agree", refused(bad))
expect("a single run is refused", refused(good[:1]))
bad = copy.deepcopy(good); bad[1]["host"]["name"] = "github-hosted-ubuntu22"
expect("another runner class is refused", refused(bad))

print(f"combine tests: {checks} checks, {len(failures)} failed")
for f in failures:
    print(f"  FAILED: {f}")
sys.exit(1 if failures else 0)
