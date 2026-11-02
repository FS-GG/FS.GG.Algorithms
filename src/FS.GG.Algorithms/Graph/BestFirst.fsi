namespace FS.GG.Algorithms.Graph

/// How a best-first search ended.
type BestFirstOutcome<'N> =
    /// A cheapest path from the start to a goal (both included) and its total cost.
    | Found of path: 'N list * cost: int
    /// No goal is reachable.
    | Unreachable
    /// The expansion budget ran out before a goal was settled.
    | BudgetExhausted

/// The outcome and the deterministic work count of one search.
type BestFirstResult<'N> =
    { Outcome: BestFirstOutcome<'N>
      /// Nodes expanded (popped and settled), including the goal when found. A deterministic work count.
      Expanded: int }

/// A best-first search problem over caller-supplied nodes. Costs are non-negative `int`s.
type BestFirstProblem<'N> =
    { Start: 'N
      IsGoal: 'N -> bool
      /// The successors of a node with the cost of each edge (>= 0), in a deterministic order.
      Neighbours: 'N -> ('N * int) list
      /// An estimate (>= 0) of the remaining cost to the nearest goal. `fun _ -> 0` gives Dijkstra. An
      /// admissible heuristic (never above the true remaining cost) gives a cheapest path; a consistent one
      /// also expands each node at most once.
      Heuristic: 'N -> int
      /// A total order on nodes, used only when two open nodes have equal f and equal h.
      TieBreak: 'N -> 'N -> int
      /// The largest number of expansions allowed; `System.Int32.MaxValue` is unbounded.
      MaxExpanded: int }

/// General best-first search (ADR-0098 §3 F3, layer 1): Dijkstra and A* over caller-supplied neighbours,
/// edge costs, heuristic and tie-break, built on `Collections.IndexedHeap`.
///
/// DETERMINISM (`LockstepExact` for integer costs and a deterministic `Neighbours`, `Heuristic` and
/// `TieBreak`). The open set pops the node with the least f = g + h, then the least h (so, among equal f, the
/// deepest node), then the least node under `TieBreak`; any remaining tie falls to the heap's frozen FIFO
/// order. A node's parent changes only on a strictly cheaper g. Nodes are identified by F# structural
/// equality; hashing is used only for lookup, never for order.
///
/// Every loop is iterative, including path reconstruction, so search depth is bounded by memory, not stack.
/// A node reopened with a cheaper g (possible only with an inconsistent heuristic) is expanded again and
/// counted again in `Expanded`.
///
/// Raises `System.ArgumentException` for a negative edge cost or heuristic value, and
/// `System.OverflowException` when a path cost or f value would exceed `System.Int32.MaxValue`.
[<RequireQualifiedAccess>]
module BestFirst =

    /// A problem with `Heuristic = fun _ -> 0`, `TieBreak = compare` and an unbounded budget.
    val problem:
        start: 'N -> isGoal: ('N -> bool) -> neighbours: ('N -> ('N * int) list) -> BestFirstProblem<'N>
        when 'N: comparison

    /// Runs the search.
    val search: problem: BestFirstProblem<'N> -> BestFirstResult<'N> when 'N: equality

    /// Dijkstra's algorithm from `start` to the first settled node satisfying `isGoal`.
    val dijkstra:
        neighbours: ('N -> ('N * int) list) ->
        tieBreak: ('N -> 'N -> int) ->
        isGoal: ('N -> bool) ->
        start: 'N ->
            BestFirstResult<'N>
            when 'N: equality

    /// A* from `start` to `goal` with an admissible `heuristic`.
    val astar:
        neighbours: ('N -> ('N * int) list) ->
        heuristic: ('N -> int) ->
        tieBreak: ('N -> 'N -> int) ->
        goal: 'N ->
        start: 'N ->
            BestFirstResult<'N>
            when 'N: equality

    /// Every node reachable from `start` with its least cost, in the order Dijkstra settles them (cost, then
    /// `tieBreak`). Iterative; the start comes first with cost 0.
    val distances:
        neighbours: ('N -> ('N * int) list) -> tieBreak: ('N -> 'N -> int) -> start: 'N -> ('N * int) list
            when 'N: equality
