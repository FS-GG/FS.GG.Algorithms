namespace FS.GG.Algorithms.Graph

/// Strongly connected components by Tarjan's algorithm, written iteratively (ADR-0098 §3 rule 5), so a
/// 10^5-node chain or ring runs under JavaScript's default stack.
///
/// The graph's vertices are `nodes`, compared by F# structural equality. A duplicate keeps its first
/// occurrence. An edge to a node outside `nodes` is ignored, never invented.
///
/// DETERMINISM (`LockstepExact` for a deterministic `successors`).
/// - Roots are tried in `nodes` order, and successors are followed in the order given.
/// - Components are returned in the order Tarjan completes them. That is reverse topological order of the
///   condensation: a component comes after every component it can reach.
/// - Each component lists its nodes in discovery order.
[<RequireQualifiedAccess>]
module Scc =

    /// Every strongly connected component, singletons included.
    val tarjan: nodes: 'N list -> successors: ('N -> 'N list) -> 'N list list when 'N: equality

    /// The components that contain a cycle: more than one node, or one node with an edge to itself. On an
    /// acyclic graph this is empty.
    val cycles: nodes: 'N list -> successors: ('N -> 'N list) -> 'N list list when 'N: equality

    /// The component index of each node in `nodes` order (indices as in `tarjan`'s output). Nodes in the same
    /// component share an index.
    val componentOf: nodes: 'N list -> successors: ('N -> 'N list) -> int list when 'N: equality
