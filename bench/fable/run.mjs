// Minimal Node timing harness for the Fable-compiled bench workloads (ADR-0098 §4.3).
//
//   node bench/fable/run.mjs COMPILED_DIR OUT.json [--smoke]
//
// COMPILED_DIR holds the Fable output of bench/fable/FS.GG.Algorithms.Bench.Fable.fsproj (shared/Workloads.js).
// Protocol: for each declared kernel and size, WARMUP untimed runs, then ITERATIONS timed runs with
// process.hrtime.bigint(); report the median and p95 by nearest rank, and the kernel's work count (which
// must equal the .NET work count). Writes one `run` object of bench/results.schema.v1.json.
import { writeFileSync } from "node:fs";
import { pathToFileURL } from "node:url";
import { join } from "node:path";

const [compiled, out, ...flags] = process.argv.slice(2);
if (!compiled || !out) {
  console.error("usage: node run.mjs COMPILED_DIR OUT.json [--smoke]");
  process.exit(2);
}
const smoke = flags.includes("--smoke");
const WARMUP = smoke ? 1 : 5;
const ITERATIONS = smoke ? 3 : 30;

const W = await import(pathToFileURL(join(compiled, "shared", "Workloads.js")).href);

// Nearest-rank percentile: the value at rank ceil(p/100 * n) of the sorted samples (1-based).
const nearestRank = (sorted, p) => sorted[Math.max(1, Math.ceil((p / 100) * sorted.length)) - 1];

// The F# list of (id, sizes, prepare) compiles to a Fable list; iterate it through its JS iterator.
const results = [];
for (const [id, sizes, prepare] of W.kernels) {
  for (const size of sizes) {
    const run = prepare(size);
    let work = 0;
    for (let i = 0; i < WARMUP; i++) work = run();
    const samples = [];
    for (let i = 0; i < ITERATIONS; i++) {
      const t0 = process.hrtime.bigint();
      work = run();
      samples.push(Number(process.hrtime.bigint() - t0));
    }
    samples.sort((a, b) => a - b);
    results.push({
      kernel: id,
      size,
      workCount: work,
      medianNs: nearestRank(samples, 50),
      p95Ns: nearestRank(samples, 95),
      samples: samples.length,
    });
    console.log(`node ${id}@${size}: work ${work}, median ${nearestRank(samples, 50)} ns, p95 ${nearestRank(samples, 95)} ns`);
  }
}

writeFileSync(
  out,
  JSON.stringify(
    {
      runtime: "node",
      runtimeVersion: process.version,
      protocol: { warmup: WARMUP, iterations: ITERATIONS, statistic: "median and p95 by nearest rank", clock: "process.hrtime.bigint", smoke },
      results,
    },
    null,
    2,
  ) + "\n",
);
