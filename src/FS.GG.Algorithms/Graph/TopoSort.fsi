namespace FS.GG.Algorithms.Graph

/// One activity of a critical-path schedule. Times are integers; an activity occupies
/// [EarliestStart, EarliestFinish) when it starts as early as its predecessors allow.
type CpmActivity<'N> =
    { Node: 'N
      Duration: int
      EarliestStart: int
      EarliestFinish: int
      LatestStart: int
      LatestFinish: int
      /// LatestStart - EarliestStart: how far the activity can slip without delaying the makespan.
      Slack: int }

/// A critical-path (CPM) schedule.
type CpmSchedule<'N> =
    { /// Every activity, in the topological order `TopoSort.sort` returns.
      Activities: CpmActivity<'N> list
      /// The length of the longest path: the earliest time every activity can be finished.
      Makespan: int
      /// One longest path, first activity first. Every activity on it has zero slack, and its durations sum
      /// to `Makespan`. Empty when there are no nodes.
      CriticalPath: 'N list }

/// Topological sorting by Kahn's algorithm, and the critical-path method (ADR-0098 §11 N1).
///
/// The graph's vertices are `nodes` (F# structural equality; a duplicate keeps its first occurrence), and
/// `successors u` lists the nodes that must come after u. An edge to a node outside `nodes` is ignored,
/// never invented. A repeated edge counts once per repetition on both ends, so it changes nothing.
///
/// DETERMINISM (`LockstepExact` for deterministic `successors`, `order` and `duration`). Kahn's ready set is
/// the F1 `BinaryHeap` ordered by the caller's `order`, so among nodes whose predecessors are all placed,
/// the least by `order` comes next. With a total `order` the result is the unique least topological order
/// under it. Any tie left by a non-total order falls to the heap's frozen FIFO: the node that became ready
/// first comes first (the initial sources become ready in `nodes` order, the others when their last
/// predecessor is placed, in that predecessor's successor-list order).
///
/// CYCLES. A graph with a cycle has no topological order. The result is then `Error` with exactly the
/// components `Scc.cycles` reports: every strongly connected component with more than one node or a
/// self-edge.
///
/// Every loop is iterative, so a 10^5-node chain runs under JavaScript's default stack.
[<RequireQualifiedAccess>]
module TopoSort =

    /// The least topological order under `order`, or the cycles.
    val sort:
        order: ('N -> 'N -> int) -> nodes: 'N list -> successors: ('N -> 'N list) -> Result<'N list, 'N list list>
            when 'N: equality

    /// The critical-path schedule with `duration` per node (>= 0), or the cycles.
    ///
    /// - EarliestStart is the largest EarliestFinish of the node's predecessors (0 for a source).
    /// - LatestFinish is the smallest LatestStart of its successors (`Makespan` for a sink).
    /// - The critical path ends at the first activity, in topological order, whose EarliestFinish equals
    ///   `Makespan`. It is traced backwards, each time through the first predecessor, in topological order,
    ///   whose EarliestFinish equals the current EarliestStart.
    ///
    /// Raises `System.ArgumentException` for a negative duration, and `System.OverflowException` when a time
    /// would exceed `System.Int32.MaxValue`.
    val criticalPath:
        order: ('N -> 'N -> int) ->
        nodes: 'N list ->
        successors: ('N -> 'N list) ->
        duration: ('N -> int) ->
            Result<CpmSchedule<'N>, 'N list list>
            when 'N: equality
