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

## Test data

The xoshiro128\*\* reference output for the state [1, 2, 3, 4] is checked as published test data. It is the
first ten outputs, as published by the `rand_xoshiro` crate's tests (MIT OR Apache-2.0). The vector is a
list of numbers, and no code was taken. An independent Python implementation in
`scripts/lockstep-fixtures.py` reproduces it.
