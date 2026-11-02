#!/usr/bin/env python3
"""Assemble one bench baseline (bench/results.schema.v1.json) from a BenchmarkDotNet full-JSON report, the .NET
work counts, the Node harness run and the admitting probe line.

  bench/assemble.py --bdn REPORT.json --workcounts WC.json --node NODE.json --admission PROBE.json \
                    --version V --commit SHA [--dirty] --host NAME --out FILE

Refuses (exit 1) when a work count differs between .NET and Node, or between the work-count pass and the
declared kernels: work counts are deterministic and identical across runtimes by construction. Pure stdlib.
"""
import argparse
import datetime
import json
import os
import platform
import sys


HOSTED_PROBE = ("github-hosted runner (no quiet-host probe): numbers are for regression comparison under the same "
                "runner class, not absolute performance")


def fail(msg):
    print(f"assemble: {msg}", file=sys.stderr)
    sys.exit(1)


def main():
    ap = argparse.ArgumentParser()
    for name in ("bdn", "workcounts", "node", "admission", "version", "commit", "host", "out"):
        ap.add_argument(f"--{name}", required=True)
    ap.add_argument("--dirty", action="store_true")
    a = ap.parse_args()

    admission = json.load(open(a.admission))
    if admission.get("verdict") != "admit":
        fail("the probe did not admit this invocation; a held probe waits, it is not overridden")
    work = {(r["kernel"], r["size"]): r["workCount"] for r in json.load(open(a.workcounts))}
    bdn = json.load(open(a.bdn))
    dotnet = []
    for b in bdn["Benchmarks"]:
        # FullName is untruncated: ...Kernels.Run(Case: "graph/best-first-search/astar-grid@32")
        case = b["FullName"].split('Case: "', 1)[1].split('"', 1)[0]
        kernel, size = case.rsplit("@", 1)
        stats = b.get("Statistics")
        if not stats:
            fail(f"BenchmarkDotNet recorded no measurement for {case}; see its log in the artifacts directory")
        row = {
            "kernel": kernel,
            "size": int(size),
            "workCount": work[(kernel, int(size))],
            "medianNs": stats["Median"],
            "p95Ns": stats["Percentiles"]["P95"],
            "meanNs": stats["Mean"],
            "samples": stats["N"],
        }
        mem = b.get("Memory") or {}
        if "BytesAllocatedPerOperation" in mem:
            row["allocatedBytesPerOp"] = mem["BytesAllocatedPerOperation"]
        dotnet.append(row)
    node = json.load(open(a.node))
    for r in node["results"]:
        key = (r["kernel"], r["size"])
        if work.get(key) != r["workCount"]:
            fail(f"work count differs for {key}: .NET {work.get(key)}, Node {r['workCount']}")
    if {(r["kernel"], r["size"]) for r in dotnet} != set(work) or {(r["kernel"], r["size"]) for r in node["results"]} != set(work):
        fail("the .NET and Node runs do not cover the same declared cases")
    env = bdn.get("HostEnvironmentInfo", {})
    doc = {
        "schema": "fsgg.algorithms.bench-results/v1",
        "package": {"id": "FS.GG.Algorithms", "version": a.version},
        "source": {"repository": "FS-GG/FS.GG.Algorithms", "commit": a.commit, "dirty": a.dirty},
        "host": {"name": a.host, "os": platform.platform(), "cpu": env.get("ProcessorName", ""), "nproc": os.cpu_count()},
        "recordedAt": datetime.datetime.now(datetime.timezone.utc).replace(microsecond=0).isoformat(),
        "admission": {
            "probe": (HOSTED_PROBE if admission.get("hosted") else "fdev-lane-capacity.py --max-load-per-core 0.25"),
            "verdict": "admit",
            "observation": admission,
        },
        "runs": [
            {
                "runtime": "dotnet",
                "runtimeVersion": env.get("RuntimeVersion", ""),
                "protocol": {"tool": f"BenchmarkDotNet {env.get('BenchmarkDotNetVersion', '')}", "launches": 1, "warmup": 3,
                             "iterations": 10, "diagnoser": "MemoryDiagnoser", "toolchain": "default"},
                "results": sorted(dotnet, key=lambda r: (r["kernel"], r["size"])),
            },
            {
                "runtime": "node",
                "runtimeVersion": node["runtimeVersion"],
                "protocol": node["protocol"],
                "results": sorted(node["results"], key=lambda r: (r["kernel"], r["size"])),
            },
        ],
    }
    with open(a.out, "w") as f:
        json.dump(doc, f, indent=2)
        f.write("\n")
    print(f"assemble: wrote {a.out} ({len(dotnet)} .NET and {len(node['results'])} Node results, work counts equal)")


if __name__ == "__main__":
    main()
