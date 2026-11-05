# Contributing a technique

Keep one stable `algorithms/<family>/<technique>` identity per technique. The binary and
indexed heaps share `collections/binary-heap`; Dijkstra and A* over the general graph
surface share `graph/best-first-search`. A new variant is not automatically a new entry.

Use the existing delivered Atlas `capture` command and unmodified `fsgg.atlas/1` schema.
The owner is `FS-GG/FS.GG.Algorithms`, including Game.Core pointers. Cite immutable public
Git revisions and exact SHA256 source bytes for the implementation, tests and applicable
benchmark baseline. Put literature in References rather than Git citations. Never copy
external prose, pseudocode or private source/data into the pack; examples are original F#,
at most 15 lines. A source reference proves only the cited implementation.

Use these headings in order: Idea, Complexity, Use when, Avoid when, Pitfalls, F# usage,
Tests, Bench, Related, References. Name the package, module and actual signature. Explain
callback/order assumptions, floating-point bounds and per-operation grade exceptions.
State missing evidence as owed, with an accountable issue; never invent benchmark values.
Benchmark timing needs the normal quiet-host admission and is never a CI threshold.

Use one audience tag (`audience-game`, `audience-framework`, `audience-both`), one grade tag
(`grade-lockstep-exact`, `grade-portable`, `grade-unclassified`, `grade-dotnet-only`) and
one status tag (`status-pointer`, `status-harvested`, `status-new`, `status-entry-only`).
Keep extra family/alias tags within Atlas's 20-tag total. Public kernels use kind `contract`;
knowledge-only lessons do not count as implemented kernels.

Capture a changed record with `supersedes` pointing to its prior digest. Retain original
bytes and history, and keep one active record per ID. The repository archive is not the
separate architecture/lesson archive under `docs/atlas`.

Before delivery, run the actual Atlas validator and producer policy gate, regenerate the
closed schema-v2 skill manifest, and check the content-only package against its source
files. Records target 6 KiB; pack warning/refusal limits are 2/4 MiB, SKILL.md 16 KiB,
summary 8000 characters, record 32 KiB. At 3 MiB, compact only the shipped view: re-capture
active entries without absent-history links while retaining complete repository history.

Changing these source files does not update a consumer. Distribution follows the existing
skills package, SDD pin/release, scaffold and `fsgg-sdd upgrade` route. A release or installed
workspace claim needs evidence from that route.
