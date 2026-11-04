# FS.GG.Algorithms

Shared, tested algorithms for FS.GG, decided by
[ADR-0098](https://github.com/FS-GG/.github/blob/main/docs/adr/0098-shared-algorithms-library-and-product-knowledge-pack.md).
The library depends on `FSharp.Core` only, and every module compiles under Fable. Surfaces graded
`LockstepExact` give byte-identical results on .NET and JavaScript.

This repository also owns the `fs-gg-algorithms` knowledge pack, delivered under ALGO-01.3
([.github#5225](https://github.com/FS-GG/.github/issues/5225)).

## What is here (tranche 1: foundations and harvest)

| Namespace | Surface | Grade | Oracle |
|---|---|---|---|
| `FS.GG.Algorithms.Collections` | `BinaryHeap`, `IndexedHeap` (decrease-key, update, remove) | `LockstepExact`, **frozen ordering contract** | sorted-list and map models; lazy-deletion equivalence |
| `FS.GG.Algorithms.Numerics` | `Fixed` (Q16.16: saturating, no `int64`) | `LockstepExact` (float conversions `Portable`) | exact `BigInteger` arithmetic |
| `FS.GG.Algorithms.Graph` | `BestFirst` (Dijkstra, A*, distances) on the indexed heap | `LockstepExact` for integer costs | BFS, Bellman-Ford, exhaustive enumeration |
| `FS.GG.Algorithms.Random` | `Xoshiro128` (xoshiro128\*\*, seed, split, jump) | `LockstepExact` (`nextFloat` `Portable`) | published reference vector; independent Python implementation |
| `FS.GG.Algorithms.Numerics` | `Percentile` (nearest rank, exact integer rank) | `LockstepExact` | exhaustive small domain; `BigInteger` |
| `FS.GG.Algorithms.Graph` | `Scc` (iterative Tarjan: components, cycles, componentOf) | `LockstepExact` | a recursive Tarjan; reachability closure |
| `FS.GG.Algorithms.Graph` | `UnionFind` (smallest-member representative, `partition` by a conflict predicate) | `LockstepExact` | BFS components |
| `FS.GG.Algorithms.Graph` | `TopoSort` (Kahn, least-first under a caller order; critical-path method) | `LockstepExact` | Tarjan cycles; a list model; brute-force longest path |
| `FS.GG.Algorithms.Search.Grid` | `DoorGrid` (layered-grid A*: thin walls, doors, planes and stairs, no corner cutting) | `LockstepExact` | an independently written adjacency plus Dijkstra |
| `FS.GG.Algorithms.Spatial` | `KdTree` (nearest, k-nearest, radius and rectangle queries over integer points) | `LockstepExact` | a brute-force scan |
| `FS.GG.Algorithms.Decision` | `Arbitration` (priority bands, pre-emption, suspend and resume) | `LockstepExact` | invariants after every input; no-starvation drain; a Python model |
| `FS.GG.Algorithms.Decision` | `BehaviourTree` (a pure tick reducer: memory composites, and reactive Sequence and Fallback for priority interrupts that halt the abandoned running branch; parallel, decorators, blackboard) | `LockstepExact` | a recursive reference interpreter; an invariant fuzz of the halt invariant; fail-fast and idempotence properties |

The grades are recorded in
[`src/FS.GG.Algorithms/Fable/compatibility-profile.v1.json`](src/FS.GG.Algorithms/Fable/compatibility-profile.v1.json).
The public surface is declared in the `.fsi` signature files.

### 0.2.0 API additions

`BehaviourTree.tickTracedWithHalts` adds a fifth return value to the traced result: the preorder indices of
leaves halted by preemption or a finishing Parallel. Cancel those actions after the tick returns. The
indices identify tree occurrences, so equal leaf values can appear at different indices. Halts follow
abandonment-event order, then preorder within each abandoned branch; repeated activations can repeat an
index. Completed and idle leaves are excluded. Existing `tick` and `tickTraced` calls retain their results.

```fsharp
let status, nextState, nextBlackboard, visited, halted =
    BehaviourTree.tickTracedWithHalts program leaf state blackboard
```

**The heap ordering contract is frozen.** Among equal priorities, `BinaryHeap` pops in push order and
`IndexedHeap` pops in the order the priorities were set. FS.GG.Game.Core pins this contract (ADR-0098
§1). Changing the pop order for any input is a breaking change with a new compatibility identity.

## Layout

```text
src/FS.GG.Algorithms/            the library (packs its sources under fable/ for Fable consumers)
  Fable/                         compatibility profile, fixture schema, toolchain manifest
tests/FS.GG.Algorithms.Tests/    FsCheck properties against oracles in the test project only
tests/FS.GG.Algorithms.Lockstep/ the cross-runtime fixture program and fixtures/v1/{cases,expected}.bin
scripts/lockstep-fixtures.py     corpus generator and independent Python oracle
eng/fable-proof.sh               the Fable proof (ADR-0069)
bench/                           BenchmarkDotNet, the Node harness, the results schema and baselines
docs/licence-allowlist.md        what may be ported, and from where
```

## Build and verify

The SDK is pinned in `global.json` (10.0.403), and Fable 5.26.0 is pinned in `.config/dotnet-tools.json`.

```sh
dotnet restore FS.GG.Algorithms.slnx --locked-mode
dotnet build FS.GG.Algorithms.slnx --no-restore
dotnet run --project tests/FS.GG.Algorithms.Tests --no-build     # FSGG_ALGO_MAXTEST=5000 for a longer soak
eng/fable-proof.sh                                                # needs node
```

The Fable proof checks five things:

- The same canonical input bytes run on .NET and under Node, and both outputs are byte-identical to
  `expected.bin`.
- An independent Python oracle re-derives every record.
- The 10^5-element stack-safety checks pass under Node's default stack.
- A clean consumer compiled by Fable from the packed package prints the same line as on .NET.
- The package depends on `FSharp.Core` only.

After changing the corpus generator, run `scripts/lockstep-fixtures.py generate`. Then regenerate
`expected.bin` from the .NET fixture program, check it with `scripts/lockstep-fixtures.py oracle`, and
update both digests in `fixture-schema.v1.json`. A changed `expected.bin` for an existing case is a
change to a `LockstepExact` surface.

## Benchmarks

`bench/run.sh` times every kernel on .NET (BenchmarkDotNet) and on Node (the Fable output, `bench/fable/run.mjs`).
It uses fixed seeded inputs at three declared sizes and records deterministic work counts. Results go to
`bench/baselines/<host>-<date>-<version>.json` against
[`bench/results.schema.v1.json`](bench/results.schema.v1.json).

A recording run is admitted only by FS-GG/.github's `fdev-lane-capacity.py --max-load-per-core 0.25`. A held
probe waits and is never overridden, and a recording run refuses a dirty tree. `bench/run.sh --smoke` proves the
harness without recording anything.
Work counts may gate regressions; timings never do.

Because fdev is rarely quiet while review lanes run, the baseline for FS-GG/.github#5224 is recorded on a
**GitHub-hosted runner**. The `bench` workflow (`workflow_dispatch`, `ubuntu-latest`) runs the full bench with
`bench/run.sh --hosted`. Three such runs on clean `main` are combined by `bench/combine.py` into one baseline,
with the per-kernel median across runs and the run-to-run spread (min, max and CV). The baseline also records
the runner image, the .NET SDK, Node and Fable. **Hosted-runner numbers are for regression comparison under the
same runner class, not absolute performance.** A quiet-fdev baseline remains an optional refinement
(FS.GG.Algorithms#1).

## Release

`.github/workflows/release.yml` publishes the library. It pushes the byte-identical `.nupkg` to the org GitHub
Packages feed first, then to nuget.org through Trusted Publishing (OIDC). It has three jobs:

- **`verify`** (read-only) runs the gates on the exact commit: locked restore, build, property tests and the
  Fable proof. For a fresh publish it also does the one pack, retained as an artifact with its sha256.
- **`publish`** holds the credentials, behind the org publication writer lock (ADR-0097). It verifies the
  archive's sha256, then pushes. A feed counts as holding a version only on HTTP 200 and as lacking it only on
  HTTP 404; any other answer fails closed (`eng/release-checks.py`, tested by `eng/release-checks.test.py`). It refuses to re-push a version that a feed already holds, because a
  re-pack is not byte-identical; it always records `publish-state.json`.
- **`readback`** (read-only) checks that both feeds match the archive, apart from feed signatures and core
  properties. It then restores a clean consumer from nuget.org alone and runs it on .NET and under Fable.

| Trigger | Ref | Effect |
|---|---|---|
| push tag `v<version>` | the tag | publish; the tag must equal `<Version>` |
| dispatch `dry-run` | any | verify and pack only |
| dispatch `login-preflight` | any | verify, pack and the nuget.org token exchange, with **no push** |
| dispatch `publish` | `refs/tags/v<version>` | publish |
| dispatch `resume` + `source_run_id` | `refs/tags/v<version>` | re-push the **original** archive of a partial publish. The source must be the fresh `publish` run of this commit and version, with its `publish-state.json` naming the archive's sha256, and any copy GitHub Packages holds must be byte-identical; nothing is re-packed |
| dispatch `readback` | `refs/tags/v<version>` | readback only, after a readback timeout |

## Licence

MIT ([LICENSE](LICENSE)). Code is implemented from papers. It is ported only under the allowlist in
[docs/licence-allowlist.md](docs/licence-allowlist.md), with the notice carried in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). No GPL or LGPL code, and no game data.
