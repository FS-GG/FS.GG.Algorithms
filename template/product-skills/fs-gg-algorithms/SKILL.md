---
name: fs-gg-algorithms
description: Find and select FS.GG algorithm implementations for graph search, deterministic collections, numeric kernels, spatial queries and decision reducers. Use the packaged Atlas entries to check APIs, costs, determinism grades and tests before adopting a kernel.
---

# FS.GG algorithms

Search the pack before writing or selecting an algorithm. Retrieve the matching record and
read its immutable implementation citation before relying on an API or guarantee:

```sh
python3 .agents/skills/atlas/scripts/atlas.py --archive .agents/skills/fs-gg-algorithms/pack search 'grid doors'
python3 .agents/skills/atlas/scripts/atlas.py --archive .agents/skills/fs-gg-algorithms/pack get algorithms/search.grid/doors-and-thin-walls
```

Use the equivalent `.claude/skills/` root when working there. Resolve the workspace's
delivered `atlas` driver; this pack carries no second helper. If it is absent, report the
missing driver and search `pack/records/*.json` with `grep` for candidate IDs. Read the
catalog and supersession links to select the active record; historical matches can be stale.
A workspace archive may be searched alongside this one by repeating `--archive`.

Each record names its module, signature, costs, selection limits, original F# example,
tests and benchmark evidence or an explicitly owed measurement. Records describe their
cited revision; they do not prove that a package is installed, current or published.
Resolve the workspace's actual package pin before using the example.
An entry marked unreleased source requires its cited implementation; its example does not
imply the API exists in a public package.

Choose the grade for the **specific operation**, not the module name. LockstepExact may
depend on deterministic callbacks and integer costs. Portable does not promise identical
bytes across runtimes. DotNetOnly code is outside the supported JavaScript source view.
An unlisted operation remains Unclassified. Keep search-budget exhaustion distinct from
unreachability, and keep timeouts distinct from successful measurements.

For family selection, see [taxonomy](references/taxonomy.md). For adding an entry or
superseding stale knowledge, see [contributing](references/contributing.md).

The umbrella indexes existing owner skills rather than replacing them:

- `fs-gg-grids`: cell/hex grids and their game conventions.
- `fs-gg-line-drawing`: line traversal and line-of-sight use.
- `fs-gg-visibility`: field-of-view and visibility use.
- `fs-gg-collision`: collision queries and broad-phase selection.
- `fs-gg-game-core` and `fs-gg-ai`: engine integration and AI composition.

Game.Core remains the implementation of record for pointer entries. Use its kernels in
place; an index entry is no reason to extract or duplicate them. Framework coordination
models belong in the framework Atlas, not this product pack.
