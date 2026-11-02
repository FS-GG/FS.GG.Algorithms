# FS.GG.Algorithms

Shared, tested algorithms for FS.GG, decided by
[ADR-0098](https://github.com/FS-GG/.github/blob/main/docs/adr/0098-shared-algorithms-library-and-product-knowledge-pack.md).
The library depends on `FSharp.Core` only, and every module compiles under Fable. Surfaces graded
`LockstepExact` give byte-identical results on .NET and JavaScript.

This repository also owns the `fs-gg-algorithms` knowledge pack, delivered under ALGO-01.3
([.github#5225](https://github.com/FS-GG/.github/issues/5225)).

## What is here (tranche 1, foundations)

| Namespace | Surface | Grade | Oracle |
|---|---|---|---|
| `FS.GG.Algorithms.Collections` | `BinaryHeap`, `IndexedHeap` (decrease-key, update, remove) | `LockstepExact`, **frozen ordering contract** | sorted-list and map models; lazy-deletion equivalence |
| `FS.GG.Algorithms.Numerics` | `Fixed` (Q16.16: saturating, no `int64`) | `LockstepExact` (float conversions `Portable`) | exact `BigInteger` arithmetic |
| `FS.GG.Algorithms.Graph` | `BestFirst` (Dijkstra, A*, distances) on the indexed heap | `LockstepExact` for integer costs | BFS, Bellman-Ford, exhaustive enumeration |
| `FS.GG.Algorithms.Random` | `Xoshiro128` (xoshiro128\*\*, seed, split, jump) | `LockstepExact` (`nextFloat` `Portable`) | published reference vector; independent Python implementation |

The grades are recorded in
[`src/FS.GG.Algorithms/Fable/compatibility-profile.v1.json`](src/FS.GG.Algorithms/Fable/compatibility-profile.v1.json).
The public surface is declared in the `.fsi` signature files.

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

The SDK is pinned in `global.json` (10.0.403), and Fable 5.18.0 is pinned in `.config/dotnet-tools.json`.

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

## Licence

MIT ([LICENSE](LICENSE)). Code is implemented from papers. It is ported only under the allowlist in
[docs/licence-allowlist.md](docs/licence-allowlist.md), with the notice carried in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). No GPL or LGPL code, and no game data.
