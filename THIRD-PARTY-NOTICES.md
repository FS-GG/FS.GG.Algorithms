# Third-party notices

FS.GG.Algorithms contains **no ported third-party code**. Every algorithm is implemented from its published
description. Any code ported later under [the allowlist](docs/licence-allowlist.md) adds its licence notice
here.

## References (ideas, not code)

- **Binary heap.** J. W. J. Williams, "Algorithm 232: Heapsort", *Communications of the ACM* 7(6), 1964.
- **Dijkstra.** E. W. Dijkstra, "A note on two problems in connexion with graphs", *Numerische Mathematik* 1,
  1959.
- **A\*.** P. E. Hart, N. J. Nilsson and B. Raphael, "A Formal Basis for the Heuristic Determination of
  Minimum Cost Paths", *IEEE Transactions on Systems Science and Cybernetics* 4(2), 1968,
  doi:10.1109/TSSC.1968.300136.
- **xoshiro128\*\*.** D. Blackman and S. Vigna, "Scrambled Linear Pseudorandom Number Generators", *ACM
  Transactions on Mathematical Software* 47(4), 2021, doi:10.1145/3460772. The jump polynomial constants are
  taken from that publication.
- **Seed expander.** The 32-bit finalizer of MurmurHash3 (A. Appleby, public domain), used SplitMix-style
  after G. L. Steele, D. Lea and C. H. Flood, "Fast Splittable Pseudorandom Number Generators", OOPSLA 2014,
  doi:10.1145/2660193.2660195.

- **Strongly connected components.** R. E. Tarjan, "Depth-First Search and Linear Graph Algorithms", *SIAM
  Journal on Computing* 1(2), 1972, doi:10.1137/0201010. The library's version is iterative.
- **Union-find.** B. A. Galler and M. J. Fischer, "An improved equivalence algorithm", *Communications of the
  ACM* 7(5), 1964. R. E. Tarjan, "Efficiency of a Good But Not Linear Set Union Algorithm", *Journal of the
  ACM* 22(2), 1975, doi:10.1145/321879.321884.
- **Topological sort.** A. B. Kahn, "Topological sorting of large networks", *Communications of the ACM*
  5(11), 1962, doi:10.1145/368996.369025.
- **Critical-path method.** J. E. Kelley and M. R. Walker, "Critical-path planning and scheduling", *Proc.
  Eastern Joint Computer Conference*, 1959, doi:10.1145/1460299.1460318.
- **Nearest-rank percentile.** NIST/SEMATECH *e-Handbook of Statistical Methods*, §7.2.6.2 (percentiles).
- **Priority arbitration.** R. A. Brooks, "A Robust Layered Control System for a Mobile Robot", *IEEE
  Journal on Robotics and Automation* 2(1), 1986, doi:10.1109/JRA.1986.1087032.

## Harvest provenance (FS-GG code, cleanly re-implemented; ADR-0098 §5)

These are FS-GG's own generic algorithms. Each was re-implemented here with its own tests on synthetic
inputs. Nothing was copied, and no game data or constants were carried.

| Entry | Provenance (repository, revision, path, SHA-256) |
|---|---|
| `graph/iterative-tarjan-scc` | FS-GG/.github `8ca8f936`, `src/FS.GG.Coord.Core/Blockers.fs`, `214a0c87c8ca19ab3577ae1dc5a1e2912a6529f4c879cef3a9d8f30ec1e0c5ba` (recursive there) |
| `graph/union-find` | FS-GG/.github `8ca8f936`, `src/FS.GG.Coord.Core/Lanes.fs`, `af4aa5b979d15554ed72c0b438839bd48464dfff5a06c791079ff0a3c10651e6` (recursive `find` there) |
| `numerics/nearest-rank-percentile` | FS-GG/FS.GG.Governance `3b8543e8`, `src/FS.GG.Governance.Adapters.SddHandoff/Performance.fs`, `88acdb58438397ff2bbb1626a610650288a4e2058b54e6fc352d845ff70b6c82` (a double-precision rank there; its 0.95 and 0.99 uses are unaffected) |
| `decision/priority-arbitration` | FS-GG/FS.GG.RuneScape (private) `1a0c532b`, `src/FS.GG.RuneScape.Bots/Arbitration.fs`, `4c7e8fd9a850a33c2688dea1ee48c7ab96873b103e30b57a57c23de63899d40a`, and `docs/decisions/0002-controller-arbitration.md`, `aede538fce5c4079e47b4a5a5c44532e3706627dcc6668e6d75358651a6b6f49`. This is provenance only. Readers without access treat it as a retrieval gap |

## Test data

The xoshiro128\*\* reference output for the state [1, 2, 3, 4] is checked as published test data. It is the
first ten outputs, as published by the `rand_xoshiro` crate's tests (MIT OR Apache-2.0). The vector is a
list of numbers, and no code was taken. An independent Python implementation in
`scripts/lockstep-fixtures.py` reproduces it.
