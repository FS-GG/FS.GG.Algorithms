# Families and selection

ADR0098 uses these families across namespaces, records and search tags:

| Family | Scope |
| --- | --- |
| collections | Heaps and reusable data structures |
| search.grid | Grid paths, reachability and flow fields |
| search.mesh | Navigation meshes and funnel paths |
| search.multiagent | Coordinated paths and reservations |
| graph | General graph search, components, ordering and scheduling |
| geometry | Coordinates, lines, visibility and geometric queries |
| spatial | Spatial indices and neighbour queries |
| numerics | Fixed arithmetic, percentiles and numeric kernels |
| random | Seeded generators and reproducible streams |
| procgen | Procedural maps and sampling |
| steering | Local motion and avoidance |
| decision | Arbitration, behaviour trees and planning |
| adversarial | Game-tree search |
| learning | Learning and offline tuning |
| optimization | Assignment, constraints and mathematical optimization |
| net | Replication and delivery kernels |
| systems | Queues, backoff, rate and work controls |

The seed is an index of existing implementations, not a promise that every family has a
shipped kernel. Search the pack to see actual coverage and read each record's source status.

For general non-negative-cost graphs use BestFirst; choose zero heuristic for Dijkstra and
an admissible heuristic for A*. Use DoorGrid for doors, costly crossings and plane transitions;
its tie order differs from Game.Core Edges. Use Game.Core's grid/hex kernels where those
engine types and contracts already fit. Never claim an optimal path after a budget exhaustion.

Use a uniform spatial hash for bucketed floating-point broad-phase queries; use the integer
k-d tree for bounded-coordinate nearest/radius queries. Verify its coordinate limits before
building an index. SpatialGrid is Portable; it is not a lockstep byte-equality contract.

Use SCC for cycles, UnionFind for equivalence components, and topological sort/CPM for a DAG's
precedence and critical path. Use priority arbitration for ownership/pre-emption and a behaviour
tree for execution flow; reactive and memory composites have different interruption semantics.
Utility AI scores a caller-defined set of actions; it does not discover a plan or execute effects.
Its current entry requires unreleased protected source, so check availability before adoption.
