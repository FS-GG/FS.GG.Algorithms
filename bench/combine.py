#!/usr/bin/env python3
"""Combine several hosted bench runs (bench/run.sh --hosted, one per GitHub-hosted runner) into one baseline.

  bench/combine.py --out FILE RUN.json RUN.json [RUN.json ...]

Every run must measure the same commit (clean tree), package version, runner class and declared cases, with
identical work counts. For each kernel, size and runtime the baseline records:
- medianNs and p95Ns: the median across runs of each run's own median and p95;
- spread: runs, the min and max per-run median, and cv (population standard deviation over mean).
`toolchain` records the runner images and the SDK, Node and Fable versions; admission.observation keeps each
run's own record. Hosted numbers are for regression comparison under the same runner class, not absolute
performance (bench/README in the repository README, FS.GG.Algorithms#1). Pure stdlib.
"""
import argparse
import json
import statistics
import sys


def fail(msg):
    print(f"combine: {msg}", file=sys.stderr)
    sys.exit(1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("runs", nargs="+")
    a = ap.parse_args()
    if len(a.runs) < 2:
        fail("combine at least two runs")
    docs = [json.load(open(p)) for p in a.runs]
    first = docs[0]
    for d in docs:
        if d.get("schema") != "fsgg.algorithms.bench-results/v1":
            fail("not a v1 bench result")
        if not d["admission"]["observation"].get("hosted"):
            fail("only hosted runs are combined here")
        if d["source"]["dirty"]:
            fail("a run measured a dirty tree")
        for key in ("package", "source"):
            if d[key] != first[key]:
                fail(f"runs differ in {key}: {d[key]} vs {first[key]}")
        if d["host"]["name"] != first["host"]["name"]:
            fail("runs come from different runner classes")
    obs = [d["admission"]["observation"] for d in docs]
    for field in ("dotnetSdk", "node", "fable"):
        if len({o.get(field) for o in obs}) != 1:
            fail(f"runs used different {field}: {sorted({str(o.get(field)) for o in obs})}")

    combined_runs = []
    for runtime in ("dotnet", "node"):
        per_run = [next(r for r in d["runs"] if r["runtime"] == runtime) for d in docs]
        keys = [sorted((x["kernel"], x["size"]) for x in r["results"]) for r in per_run]
        if any(k != keys[0] for k in keys):
            fail(f"{runtime}: runs cover different cases")
        results = []
        for kernel, size in keys[0]:
            rows = [next(x for x in r["results"] if x["kernel"] == kernel and x["size"] == size) for r in per_run]
            work = {x["workCount"] for x in rows}
            if len(work) != 1:
                fail(f"{runtime} {kernel}@{size}: work counts differ across runs {sorted(work)}")
            medians = [x["medianNs"] for x in rows]
            mean = statistics.fmean(medians)
            row = {
                "kernel": kernel,
                "size": size,
                "workCount": rows[0]["workCount"],
                "medianNs": statistics.median(medians),
                "p95Ns": statistics.median([x["p95Ns"] for x in rows]),
                "spread": {
                    "runs": len(rows),
                    "minMedianNs": min(medians),
                    "maxMedianNs": max(medians),
                    "cv": round(statistics.pstdev(medians) / mean, 4) if mean > 0 else 0.0,
                },
            }
            if all("allocatedBytesPerOp" in x for x in rows):
                row["allocatedBytesPerOp"] = statistics.median([x["allocatedBytesPerOp"] for x in rows])
            results.append(row)
        combined_runs.append({
            "runtime": runtime,
            "runtimeVersion": per_run[0]["runtimeVersion"],
            "protocol": {**per_run[0]["protocol"], "combinedRuns": len(per_run),
                         "statistic": "median across runs of each run's median and p95; spread over the per-run medians"},
            "results": results,
        })
    # Cross-runtime: work counts are deterministic and identical on .NET and Node.
    dn = {(x["kernel"], x["size"]): x["workCount"] for x in combined_runs[0]["results"]}
    nd = {(x["kernel"], x["size"]): x["workCount"] for x in combined_runs[1]["results"]}
    if dn != nd:
        fail("work counts differ between .NET and Node")
    doc = {
        "schema": "fsgg.algorithms.bench-results/v1",
        "package": first["package"],
        "source": first["source"],
        "host": {
            "name": first["host"]["name"],
            "os": first["host"]["os"],
            "cpu": " | ".join(sorted({d["host"].get("cpu", "") for d in docs})),
            "nproc": first["host"]["nproc"],
        },
        "recordedAt": max(d["recordedAt"] for d in docs),
        "admission": {
            "probe": first["admission"]["probe"],
            "verdict": "admit",
            "observation": {"hosted": True, "runs": obs},
        },
        "toolchain": {
            "runnerImages": sorted({f"{o.get('imageOs')} {o.get('imageVersion')}" for o in obs}),
            "dotnetSdk": obs[0].get("dotnetSdk"),
            "node": obs[0].get("node"),
            "fable": obs[0].get("fable"),
            "runIds": [o.get("runId") for o in obs],
            "note": "GitHub-hosted runner numbers are for regression comparison under the same runner class, not absolute performance.",
        },
        "runs": combined_runs,
    }
    with open(a.out, "w") as f:
        json.dump(doc, f, indent=2)
        f.write("\n")
    worst = max((x["spread"]["cv"], r["runtime"], x["kernel"], x["size"]) for r in combined_runs for x in r["results"])
    print(f"combine: wrote {a.out} from {len(docs)} runs ({sum(len(r['results']) for r in combined_runs)} results); "
          f"largest run-to-run cv {worst[0]} ({worst[1]} {worst[2]}@{worst[3]})")


if __name__ == "__main__":
    main()
