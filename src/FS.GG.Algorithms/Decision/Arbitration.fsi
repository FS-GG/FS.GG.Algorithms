namespace FS.GG.Algorithms.Decision

/// How a band orders its own entries.
[<RequireQualifiedAccess>]
type BandOrder =
    /// The newest submission runs first: a takeover band, where the latest request wins at once and the
    /// earlier one is suspended until it ends.
    | NewestFirst
    /// The earliest submission runs first: first come, first served, so no waiting entry starves behind later
    /// arrivals in its band.
    | EarliestFirst

/// How a submission treats its owner's other entries.
[<RequireQualifiedAccess>]
type SubmitMode =
    /// End the owner's other entries first: the owner's newest intent replaces the old ones.
    | Replace
    /// Keep them; the new entry waits its turn under the band's order.
    | Queue

[<RequireQualifiedAccess>]
type EntryStatus =
    | Pending
    | Active
    /// Pre-empted by a higher-ranked entry; it becomes active again (resumed) when that one ends.
    | Suspended

/// One live entry.
type ArbitrationEntry<'K, 'O, 'G> =
    { Key: 'K
      Owner: 'O
      Band: int
      Goal: 'G
      /// The submission sequence number (1, 2, ...); orders entries within a band.
      Seq: int
      Status: EntryStatus
      /// Whether it has ever been active, so a later activation is a resumption.
      Started: bool }

[<RequireQualifiedAccess>]
type ArbitrationRefusal<'K> =
    /// A live entry already has this key.
    | DuplicateKey of 'K
    /// No live entry has this key.
    | UnknownKey of 'K
    /// The caller neither owns the entry nor outranks its band.
    | NotPermitted of 'K
    /// The band already has its maximum number of waiting entries.
    | QueueFull

[<RequireQualifiedAccess>]
type EndReason<'O> =
    | Finished
    | Cancelled of by: 'O
    | Replaced
    | Released
    | CancelledAll of by: 'O

/// Every arbitration decision. A host logs each one and acts on it. Nothing else changes which entry runs.
[<RequireQualifiedAccess>]
type ArbitrationDecision<'K, 'O> =
    | Accepted of 'K
    | Refused of ArbitrationRefusal<'K>
    /// Start running this entry; `resumed` when it ran before and was suspended.
    | Activated of 'K * resumed: bool
    /// Stop the running entry now; it stays live as Suspended.
    | Preempted of 'K * by: 'K
    /// The entry ended for good.
    | Ended of 'K * EndReason<'O>
    | CancelledAll of by: 'O * count: int

/// What the arbiter is told.
[<RequireQualifiedAccess>]
type ArbitrationInput<'K, 'O, 'G> =
    | Submit of owner: 'O * key: 'K * goal: 'G * mode: SubmitMode
    /// Cancel one entry: allowed for its owner, or for an owner in a strictly higher band.
    | Cancel of by: 'O * key: 'K
    /// End every entry in the caller's band or below, in this same step.
    | CancelAll of by: 'O
    /// End every entry of this owner (hand back control).
    | Release of owner: 'O
    /// The running entry finished. Ignored unless that key is the active entry.
    | Finished of key: 'K

/// The fixed rules of an arbiter.
type ArbitrationPolicy<'O> =
    { /// The band of an owner. Bands are integers; a higher band always outranks a lower one.
      BandOf: 'O -> int
      /// How each band orders its entries.
      OrderOf: int -> BandOrder
      /// The most entries a band may have waiting (not active) when a new one is submitted; None is unlimited.
      MaxWaiting: int -> int option }

/// The arbiter's state: an immutable value, so every step can be logged, replayed and compared.
[<Sealed; Class>]
type Arbiter<'K, 'O, 'G> =
    /// The live entries in submission order.
    member Entries: ArbitrationEntry<'K, 'O, 'G> list
    /// The running entry, if any.
    member Active: ArbitrationEntry<'K, 'O, 'G> option

/// Priority-band arbitration with pre-emption, suspension and resumption (ADR-0098 §11, harvest 21).
///
/// RULES (`LockstepExact` for deterministic policy functions and key and owner equality).
/// 1. While any entry is live, exactly one is Active. It is the first by precedence: the highest band, then
///    within the band the newest (`NewestFirst`) or earliest (`EarliestFirst`) submission.
/// 2. An entry outranked by a new or remaining entry is Preempted and kept as Suspended, never discarded. It
///    is Activated with `resumed = true` when it is first in precedence again.
/// 3. An entry is ended only by its own Finished report, its owner (Cancel, Release, or a Replace
///    submission), or an owner in a higher band (Cancel), or in an equal or higher band (CancelAll).
/// 4. Keys are global: callers that need per-owner namespaces qualify their keys. A key is refused only while
///    it is live. A host that must also refuse recently ended keys (as the harvested source does, with a
///    bounded memory of recent ids) keeps that memory in a wrapper around `step`; it is policy, not arbitration.
///
/// `step` is pure: it maps a state and one input to the next state and the decisions, in this order. A
/// submission yields Accepted, then any Ended by Replace, then the settling Preempted and Activated. Cancel and
/// Release yield their Ended, then the settling decisions. CancelAll yields CancelledAll, then its Ended, then
/// the settling decisions.
[<RequireQualifiedAccess>]
module Arbitration =

    /// No live entries.
    val empty<'K, 'O, 'G> : Arbiter<'K, 'O, 'G>

    /// Applies one input. Raises `System.InvalidOperationException` after 2^31 - 1 submissions.
    val step:
        policy: ArbitrationPolicy<'O> ->
        state: Arbiter<'K, 'O, 'G> ->
        input: ArbitrationInput<'K, 'O, 'G> ->
            Arbiter<'K, 'O, 'G> * ArbitrationDecision<'K, 'O> list
            when 'K: equality and 'O: equality

    /// Precedence as a comparison: negative when `a` runs before `b`.
    val precedence:
        policy: ArbitrationPolicy<'O> -> a: ArbitrationEntry<'K, 'O, 'G> -> b: ArbitrationEntry<'K, 'O, 'G> -> int
