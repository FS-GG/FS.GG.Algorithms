namespace FS.GG.Algorithms.Decision

// Clean, generic re-implementation of priority-band arbitration (harvest 21, ADR-0098 §5 and §11): bands,
// a per-band order, pre-emption that suspends, resumption, cancel permissions, cancel-all and release. It
// carries no game data, controller kinds, goals, rate limits or protocol. Rate limiting composes from outside
// (systems/backoff-jitter-token-bucket, N10). Provenance (private; content not copied): FS-GG/FS.GG.RuneScape
// 1a0c532b src/FS.GG.RuneScape.Bots/Arbitration.fs and docs/decisions/0002-controller-arbitration.md.
// Background: subsumption-style priority arbitration, R. A. Brooks, "A Robust Layered Control System for a
// Mobile Robot", IEEE Journal on Robotics and Automation 2(1), 1986, doi:10.1109/JRA.1986.1087032.

[<RequireQualifiedAccess>]
type BandOrder =
    | NewestFirst
    | EarliestFirst

[<RequireQualifiedAccess>]
type SubmitMode =
    | Replace
    | Queue

[<RequireQualifiedAccess>]
type EntryStatus =
    | Pending
    | Active
    | Suspended

type ArbitrationEntry<'K, 'O, 'G> =
    { Key: 'K
      Owner: 'O
      Band: int
      Goal: 'G
      Seq: int
      Status: EntryStatus
      Started: bool }

[<RequireQualifiedAccess>]
type ArbitrationRefusal<'K> =
    | DuplicateKey of 'K
    | UnknownKey of 'K
    | NotPermitted of 'K
    | QueueFull

[<RequireQualifiedAccess>]
type EndReason<'O> =
    | Finished
    | Cancelled of by: 'O
    | Replaced
    | Released
    | CancelledAll of by: 'O

[<RequireQualifiedAccess>]
type ArbitrationDecision<'K, 'O> =
    | Accepted of 'K
    | Refused of ArbitrationRefusal<'K>
    | Activated of 'K * resumed: bool
    | Preempted of 'K * by: 'K
    | Ended of 'K * EndReason<'O>
    | CancelledAll of by: 'O * count: int

[<RequireQualifiedAccess>]
type ArbitrationInput<'K, 'O, 'G> =
    | Submit of owner: 'O * key: 'K * goal: 'G * mode: SubmitMode
    | Cancel of by: 'O * key: 'K
    | CancelAll of by: 'O
    | Release of owner: 'O
    | Finished of key: 'K

type ArbitrationPolicy<'O> =
    { BandOf: 'O -> int
      OrderOf: int -> BandOrder
      MaxWaiting: int -> int option }

[<Sealed; Class>]
type Arbiter<'K, 'O, 'G>(entries: ArbitrationEntry<'K, 'O, 'G> list, nextSeq: int) =
    member _.Entries = entries
    member _.NextSeq = nextSeq
    member _.Active = entries |> List.tryFind (fun e -> e.Status = EntryStatus.Active)

[<RequireQualifiedAccess>]
module Arbitration =

    let empty<'K, 'O, 'G> : Arbiter<'K, 'O, 'G> = Arbiter<'K, 'O, 'G>([], 1)

    let precedence (policy: ArbitrationPolicy<'O>) (a: ArbitrationEntry<'K, 'O, 'G>) (b: ArbitrationEntry<'K, 'O, 'G>) =
        if a.Band <> b.Band then
            (if a.Band > b.Band then -1 else 1)
        else
            let c = compare a.Seq b.Seq
            match policy.OrderOf a.Band with
            | BandOrder.NewestFirst -> -c
            | BandOrder.EarliestFirst -> c

    /// The first entry by precedence (iterative scan; Seq is unique, so there are no ties).
    let private winner policy (entries: ArbitrationEntry<'K, 'O, 'G> list) =
        let mutable best = None
        for e in entries do
            match best with
            | Some b when precedence policy b e <= 0 -> ()
            | _ -> best <- Some e
        best

    /// Make the winner active: Preempted for a displaced active entry, Activated for the new one.
    let private settle policy (entries: ArbitrationEntry<'K, 'O, 'G> list) =
        let current = entries |> List.tryFind (fun e -> e.Status = EntryStatus.Active)
        match current, winner policy entries with
        | _, None -> entries, []
        | Some c, Some w when c.Key = w.Key -> entries, []
        | cur, Some w ->
            let preempt =
                match cur with
                | Some c -> [ ArbitrationDecision.Preempted(c.Key, w.Key) ]
                | None -> []
            let entries =
                entries
                |> List.map (fun e ->
                    if e.Key = w.Key then
                        { e with Status = EntryStatus.Active; Started = true }
                    elif cur |> Option.exists (fun c -> c.Key = e.Key) then
                        { e with Status = EntryStatus.Suspended }
                    else
                        e)
            entries, preempt @ [ ArbitrationDecision.Activated(w.Key, w.Started) ]

    let private endWhere (reason: EndReason<'O>) (pick: ArbitrationEntry<'K, 'O, 'G> -> bool) entries =
        let ended, kept = entries |> List.partition pick
        kept, ended |> List.map (fun e -> ArbitrationDecision.Ended(e.Key, reason))

    let step
        (policy: ArbitrationPolicy<'O>)
        (state: Arbiter<'K, 'O, 'G>)
        (input: ArbitrationInput<'K, 'O, 'G>)
        : Arbiter<'K, 'O, 'G> * ArbitrationDecision<'K, 'O> list =
        let entries = state.Entries
        let refuse r = state, [ ArbitrationDecision.Refused r ]
        match input with
        | ArbitrationInput.Submit(owner, key, goal, mode) ->
            if entries |> List.exists (fun e -> e.Key = key) then
                refuse (ArbitrationRefusal.DuplicateKey key)
            else
                let band = policy.BandOf owner
                let kept, ended =
                    match mode with
                    | SubmitMode.Replace -> endWhere EndReason.Replaced (fun e -> e.Owner = owner) entries
                    | SubmitMode.Queue -> entries, []
                let waiting =
                    kept |> List.filter (fun e -> e.Band = band && e.Status <> EntryStatus.Active) |> List.length
                match policy.MaxWaiting band with
                | Some limit when waiting >= limit -> refuse ArbitrationRefusal.QueueFull
                | _ ->
                    if state.NextSeq = System.Int32.MaxValue then
                        invalidOp "Arbitration: more than 2^31 - 1 submissions; the sequence number would overflow"
                    let entry =
                        { Key = key
                          Owner = owner
                          Band = band
                          Goal = goal
                          Seq = state.NextSeq
                          Status = EntryStatus.Pending
                          Started = false }
                    let settled, decisions = settle policy (kept @ [ entry ])
                    Arbiter(settled, state.NextSeq + 1),
                    (ArbitrationDecision.Accepted key :: ended) @ decisions
        | ArbitrationInput.Cancel(by, key) ->
            match entries |> List.tryFind (fun e -> e.Key = key) with
            | None -> refuse (ArbitrationRefusal.UnknownKey key)
            | Some e when e.Owner <> by && policy.BandOf by <= e.Band -> refuse (ArbitrationRefusal.NotPermitted key)
            | Some e ->
                let kept, ended = endWhere (EndReason.Cancelled by) (fun x -> x.Key = e.Key) entries
                let settled, decisions = settle policy kept
                Arbiter(settled, state.NextSeq), ended @ decisions
        | ArbitrationInput.CancelAll by ->
            let band = policy.BandOf by
            let kept, ended = endWhere (EndReason.CancelledAll by) (fun e -> e.Band <= band) entries
            let settled, decisions = settle policy kept
            Arbiter(settled, state.NextSeq),
            (ArbitrationDecision.CancelledAll(by, ended.Length) :: ended) @ decisions
        | ArbitrationInput.Release owner ->
            let kept, ended = endWhere EndReason.Released (fun e -> e.Owner = owner) entries
            let settled, decisions = settle policy kept
            Arbiter(settled, state.NextSeq), ended @ decisions
        | ArbitrationInput.Finished key ->
            match state.Active with
            | Some a when a.Key = key ->
                let kept, ended = endWhere EndReason.Finished (fun e -> e.Key = key) entries
                let settled, decisions = settle policy kept
                Arbiter(settled, state.NextSeq), ended @ decisions
            | _ -> state, []
