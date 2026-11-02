# Licence allowlist and porting rules

This page restates the binding rules of
[ADR-0098](https://github.com/FS-GG/.github/blob/main/docs/adr/0098-shared-algorithms-library-and-product-knowledge-pack.md)
§5. The ADR wins on any disagreement. Licences were verified in Root's prior-art survey on 2026-11-01
([.github#5223](https://github.com/FS-GG/.github/issues/5223#issuecomment-6564547547)).

## Default: implement from the paper

Every algorithm is a clean implementation from its published description, with the paper named in the
source and, later, under the pack entry's **References**.

Copyrighted books are summarised and cited, never copied, and that includes their pseudo-code. Examples are
*The Algorithmic Beauty of Plants*, AIMA, CLRS, RTCD and the Game AI Pro chapters.

## Allowlist (porting code)

Code may be ported only under these SPDX ids, with the licence notice added to
[THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) and the licence named in the entry:

| SPDX id | Known sources (survey) |
|---|---|
| `MIT` | WFC (mxgmn: the code only, never its sample tilesets), GGPO, FastNoiseLite, rbush, MiniSat; rs-sdk and LostCity (prefer a clean re-implementation) |
| `ISC` | Delaunator, kdbush, flatbush; the rsmod pathfinder (cited as a reference only) |
| `BSD-2-Clause` | |
| `BSD-3-Clause` | pycma, libccd |
| `Apache-2.0` | RVO2 (carry any NOTICE file it has) |
| `Zlib` | Recast, DotRecast |
| `CC0-1.0` | OpenSimplex2 |

Dual MIT/Apache-2.0 sources, such as GGRS, are allowed. Prefer implementing from the paper even when the
source is permissive.

Any other licence needs a recorded Root decision before porting. Examples are `MPL-2.0`, `CC-BY-SA-*` and
`AGPL-3.0`.

## Never

- **GPL or LGPL source**, of any version. Examples are Stockfish (GPL-3.0) and OpenAL Soft (LGPL). Search
  techniques that Stockfish uses are implemented from their papers instead.
- **Excluded upstreams:** RecoilEngine and HighBarV3.
- **Game data in any public artefact.** That covers the library, the pack, bench baselines and examples. It
  includes asset-derived or cache-derived values and game-specific constants, such as RuneScape collision
  flags, coordinates or ids.
- **Private product code that is not an algorithm.** Generic algorithm code that FS-GG wrote in a private
  repository may be cleanly re-implemented here, with its own tests on synthetic inputs. Its private origin
  is cited only as provenance: repository, revision, path and digest, never content.
- **Shipping a test oracle.** OR-Tools (Apache-2.0) may serve as a test oracle in a .NET test project only.
  It never ships, because its native dependency cannot cross Fable.

## Unknown licence

Where a repository's licence is unstated (FourD, SC2.Client, Unity.Client), FS-GG authorship is the basis
for harvest. `Game.Core/Physics.fs` is not harvested until its author confirms it is not derived from
Box2D-Lite or another source.

## Tests and benches

Tests and benches use synthetic inputs only. A harvested entry never tests against its source game's data.
