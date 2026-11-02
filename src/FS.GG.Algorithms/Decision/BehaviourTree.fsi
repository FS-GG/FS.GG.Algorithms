namespace FS.GG.Algorithms.Decision

/// The result of ticking a node.
[<RequireQualifiedAccess>]
type BtStatus =
    | Success
    | Failure
    /// Not finished; the next tick resumes here.
    | Running

/// A behaviour tree over caller-defined leaves 'L (actions and conditions alike). The tree is plain data;
/// what a leaf does is the `leaf` function given to `tick`.
[<RequireQualifiedAccess>]
type BehaviourTree<'L> =
    | Leaf of 'L
    /// Ticks children in order until one fails (Failure) or runs (Running); Success when all succeed.
    /// An empty sequence succeeds.
    | Sequence of BehaviourTree<'L> list
    /// Ticks children in order until one succeeds (Success) or runs (Running); Failure when all fail.
    /// An empty selector fails.
    | Selector of BehaviourTree<'L> list
    /// Ticks every unfinished child each tick. Success once `successAt` children have succeeded, else Failure
    /// once `failureAt` have failed, else Failure when all have finished without either, else Running.
    /// Requires 1 <= successAt, failureAt <= the number of children.
    | Parallel of successAt: int * failureAt: int * children: BehaviourTree<'L> list
    /// Swaps Success and Failure; Running passes through.
    | Invert of BehaviourTree<'L>
    /// Success for any finished child; Running passes through.
    | ForceSuccess of BehaviourTree<'L>
    /// Failure for any finished child; Running passes through.
    | ForceFailure of BehaviourTree<'L>
    /// Re-ticks the child, within the same tick, until it has succeeded `times` times (Success) or fails
    /// (Failure). Requires times >= 1.
    | Repeat of times: int * BehaviourTree<'L>
    /// Re-ticks the child, within the same tick, until it succeeds (Success) or has failed `attempts` times
    /// (Failure). Requires attempts >= 1.
    | Retry of attempts: int * BehaviourTree<'L>
    /// A Sequence without memory (Colledanchise-Ögren's default Sequence): every tick starts again at the first
    /// child, so its earlier children (typically conditions) are re-checked while a later child runs. Ticks
    /// children in order until one fails (Failure) or runs (Running); Success when all succeed. When an
    /// earlier child fails or runs, the child that was running is halted. An empty one succeeds.
    | ReactiveSequence of BehaviourTree<'L> list
    /// A Selector without memory (Colledanchise-Ögren's default Fallback): every tick starts again at the first
    /// child, so a higher-priority child that now succeeds or runs interrupts a lower one. Ticks children in
    /// order until one succeeds (Success) or runs (Running); Failure when all fail. When an earlier child
    /// succeeds or runs, the child that was running is halted. An empty one fails.
    | ReactiveFallback of BehaviourTree<'L> list

/// A validated, compiled tree: nodes numbered in preorder (the root is 0).
[<Sealed; Class>]
type BtProgram<'L> =
    /// The number of nodes.
    member NodeCount: int

/// The tree's memory between ticks: an immutable value with structural equality, so a running tree can be
/// saved, compared and replayed in lockstep code.
[<Sealed; Class>]
type BtState =
    /// Per node in preorder (0 when idle): a Sequence or Selector's child cursor, a reactive composite's
    /// running child, or the Repeat/Retry counter.
    member Cursors: int list
    /// Per node in preorder: how the node finished within an active Parallel parent (0 not yet, 1 success,
    /// 2 failure).
    member Finished: int list
    override Equals: obj -> bool
    override GetHashCode: unit -> int

/// A behaviour tree as a pure tick reducer (ADR-0098 §11 N2).
///
/// SEMANTICS.
/// - Sequence and Selector have memory. One whose child returned Running resumes at that child on the next
///   tick, without re-ticking the children before it. A Parallel keeps the results of children that already
///   finished, and re-ticks only the unfinished ones.
/// - ReactiveSequence and ReactiveFallback have no memory of progress (the Colledanchise-Ögren defaults).
///   They re-tick from the first child on every tick, so an earlier child preempts a running later one: a
///   priority interrupt. A Fallback's higher-priority child that now succeeds or runs takes over; a
///   Sequence's guard condition that now fails aborts the action after it.
/// - Halting. A running branch that is abandoned without finishing is HALTED: its whole subtree's memory is
///   cleared, so its next activation starts fresh. Two things abandon a running branch: a Parallel that
///   finishes (its still-running children), and a reactive composite whose earlier child takes over (the
///   child that was running). Halting is silent: no leaf is called. A leaf that returned Running and is not
///   ticked on the next tick has been halted, either by that next tick or, on its own tick, by a Parallel
///   that finished.
/// - Invariant (halt invariant): after every tick, non-idle memory exists only on the live path, the nodes
///   that returned Running with every ancestor Running. A node with a non-zero cursor or counter is on it; a
///   non-zero `Finished` entry belongs to a child of a live Parallel. In particular, after a node finishes
///   (Success or Failure) its whole subtree is idle, and no stale running child survives a preemption.
/// - A tick is atomic. It works on copies, so the state passed in is never changed; if `leaf` raises, the
///   exception propagates and the caller still holds the state from before the tick.
/// - The blackboard 'B is the caller's immutable value, threaded through the leaves in tick order and
///   returned. A tick touches nothing else: equal (program, state, blackboard) give equal results.
///
/// SCOPE: memory composites (Sequence, Selector) and reactive composites (ReactiveSequence, ReactiveFallback,
/// FS-GG/FS.GG.Algorithms#8). Use a reactive composite where a higher-priority branch must be able to interrupt
/// a running lower one. There is no halt callback (see Halting).
///
/// DETERMINISM (`LockstepExact` for a deterministic `leaf`). The memory is integers, and the tick order is the
/// fixed child order. The interpreter is an explicit stack machine, so a tree 10^5 levels deep ticks under
/// JavaScript's default stack, and halting a 10^5-node branch is a loop. A reactive composite re-ticks its
/// earlier children every tick, so its tick costs grow with the children before the running one.
///
/// RELATION. FS.GG.RuneScape's bots compose effect-proving behaviours sequentially. That code is private,
/// and none of it is used here; such a behaviour step fits as a leaf. GOAP (N21) produces plans that run as
/// a Sequence, and HTN is a later family that decomposes into the same shapes.
[<RequireQualifiedAccess>]
module BehaviourTree =

    /// Validates and compiles a tree. Raises `System.ArgumentException` for a Parallel with no children or a
    /// threshold outside 1..children, and for a Repeat or Retry count below 1. Iterative: any depth.
    val compile: tree: BehaviourTree<'L> -> BtProgram<'L>

    /// The idle state: every cursor, counter and parallel result cleared.
    val initial: program: BtProgram<'L> -> BtState

    /// Ticks the tree once from `state`: returns the root's status, the next state and the blackboard after
    /// every leaf that ran.
    val tick:
        program: BtProgram<'L> ->
        leaf: ('L -> 'B -> BtStatus * 'B) ->
        state: BtState ->
        blackboard: 'B ->
            BtStatus * BtState * 'B

    /// `tick`, also returning the preorder indices of the leaves ticked, in the order they ran.
    val tickTraced:
        program: BtProgram<'L> ->
        leaf: ('L -> 'B -> BtStatus * 'B) ->
        state: BtState ->
        blackboard: 'B ->
            BtStatus * BtState * 'B * int list
