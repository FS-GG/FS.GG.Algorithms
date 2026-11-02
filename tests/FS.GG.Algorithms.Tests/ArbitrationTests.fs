module FS.GG.Algorithms.Tests.ArbitrationTests

open Expecto
open FsCheck
open FsCheck.FSharp
open FS.GG.Algorithms.Decision
open FS.GG.Algorithms.Tests.Support

// Synthetic owners: six, two per band. Band 3 is a takeover band (newest first); bands 2 and 1 are first come,
// first served. Generic stand-ins only: no source game's kinds, names, goals or limits.
let private bandOf (owner: int) = 3 - owner / 2 // owners 0,1 -> 3; 2,3 -> 2; 4,5 -> 1
let private policy: ArbitrationPolicy<int> =
    { BandOf = bandOf
      OrderOf = (fun band -> if band = 3 then BandOrder.NewestFirst else BandOrder.EarliestFirst)
      MaxWaiting = (fun band -> if band = 3 then Some 16 else Some 6) }

type private Op =
    | OSubmit of owner: int * Mode: SubmitMode
    | OCancel of owner: int * pick: int
    | OCancelAll of owner: int
    | ORelease of owner: int
    | OFinish
    | OFinishStale of pick: int

let private ops =
    Gen.frequency
        [ 4, Gen.map2 (fun o q -> OSubmit(o, (if q then SubmitMode.Queue else SubmitMode.Replace))) (Gen.choose (0, 5)) (Gen.elements [ true; false; false ])
          1, Gen.map2 (fun o k -> OCancel(o, k)) (Gen.choose (0, 5)) (Gen.choose (0, 60))
          1, Gen.map OCancelAll (Gen.choose (0, 5))
          1, Gen.map ORelease (Gen.choose (0, 5))
          2, Gen.constant OFinish
          1, Gen.map OFinishStale (Gen.choose (0, 60)) ]
    |> Gen.listOf
    |> Gen.map (List.truncate 60)
    |> Arb.fromGen

/// The independent order: band descending, then newest first in band 3 and earliest first elsewhere, by the
/// test's own submission counter (not the arbiter's Seq).
let private oracleFirst (order: Map<int, int>) (entries: ArbitrationEntry<int, int, unit> list) =
    entries
    |> List.sortBy (fun e ->
        let n = order.[e.Key]
        -e.Band, (if e.Band = 3 then -n else n))
    |> List.tryHead

type private Run =
    { State: Arbiter<int, int, unit>
      Decisions: ArbitrationDecision<int, int> list
      Accepted: int list
      Errors: string list }

let private checkStep (order: Map<int, int>) (before: Arbiter<int, int, unit>) (input: ArbitrationInput<int, int, unit>) (after: Arbiter<int, int, unit>) (ds: ArbitrationDecision<int, int> list) =
    [ let live = after.Entries
      let actives = live |> List.filter (fun e -> e.Status = EntryStatus.Active)
      if not (live.IsEmpty = actives.IsEmpty && actives.Length <= 1) then "not exactly one active entry while entries are live"
      match actives, oracleFirst order live with
      | [ a ], Some w when a.Key <> w.Key -> sprintf "running %d, the rules say %d" a.Key w.Key
      | _ -> ()
      // The top band always runs.
      match live |> List.map (fun e -> e.Band) |> List.sortDescending |> List.tryHead, after.Active with
      | Some top, Some a when a.Band <> top -> "a higher band is live but not active"
      | _ -> ()
      // Nothing from a lower band ends a higher-band entry; a Finished report ends only its own key.
      let bandBefore = before.Entries |> List.map (fun e -> e.Key, e.Band) |> Map.ofList
      let ended = ds |> List.choose (function ArbitrationDecision.Ended(k, _) -> Some k | _ -> None)
      match input with
      | ArbitrationInput.Submit(o, _, _, _) | ArbitrationInput.Release o ->
          if ended |> List.exists (fun k -> (before.Entries |> List.find (fun e -> e.Key = k)).Owner <> o) then "a submission or release ended another owner's entry"
      | ArbitrationInput.Cancel(o, key) ->
          match before.Entries |> List.tryFind (fun e -> e.Key = key) with
          | Some e when e.Owner <> o && bandOf o <= e.Band ->
              if not (ds = [ ArbitrationDecision.Refused(ArbitrationRefusal.NotPermitted key) ]) then "a cancel without ownership or a higher band was not refused"
              if not (live |> List.exists (fun x -> x.Key = key)) then "a refused cancel removed the entry"
          | _ -> ()
          if ended |> List.exists (fun k -> k <> key) then "a cancel ended another entry"
      | ArbitrationInput.CancelAll o ->
          if live |> List.exists (fun e -> e.Band <= bandOf o) then "cancel-all left an entry at or below the caller's band"
          if ended |> List.exists (fun k -> bandBefore.[k] > bandOf o) then "cancel-all ended a higher band"
          match before.Active with
          | Some a when a.Band <= bandOf o && not (List.contains a.Key ended) -> "cancel-all did not end the running entry in the same step"
          | _ -> ()
      | ArbitrationInput.Finished key ->
          if ended |> List.exists (fun k -> k <> key) then "a finished report ended another entry"
          if (before.Active |> Option.map (fun a -> a.Key)) <> Some key && not ds.IsEmpty then "a stale finished report changed something"
      // A takeover submission that is now first runs at once.
      match input, ds with
      | ArbitrationInput.Submit(o, key, _, _), ArbitrationDecision.Accepted _ :: _ when bandOf o = 3 ->
          if (after.Active |> Option.map (fun a -> a.Key)) <> Some key then "a takeover-band submission is not active at once"
      | _ -> ()
      // Suspension is never loss: every entry live before is live after unless an Ended decision names it.
      for e in before.Entries do
          if not (List.contains e.Key ended) && not (live |> List.exists (fun x -> x.Key = e.Key)) then
              sprintf "entry %d vanished without an Ended decision" e.Key
      // A resumed activation is exactly an entry that ran before.
      for d in ds do
          match d with
          | ArbitrationDecision.Activated(k, resumed) ->
              let was = before.Entries |> List.tryFind (fun e -> e.Key = k)
              if resumed <> (was |> Option.exists (fun e -> e.Started)) then sprintf "entry %d resumed flag wrong" k
          | _ -> () ]

let private run (opsList: Op list) =
    let mutable s = Arbitration.empty<int, int, unit>
    let mutable all = []
    let mutable accepted = []
    let mutable errors = []
    let mutable n = 0
    let mutable order = Map.empty
    for op in opsList do
        let input =
            match op with
            | OSubmit(o, mode) ->
                n <- n + 1
                order <- order.Add(n, n)
                Some(ArbitrationInput.Submit(o, n, (), mode))
            | OCancel(o, k) -> Some(ArbitrationInput.Cancel(o, (if n = 0 then k else 1 + k % n)))
            | OCancelAll o -> Some(ArbitrationInput.CancelAll o)
            | ORelease o -> Some(ArbitrationInput.Release o)
            | OFinish -> s.Active |> Option.map (fun a -> ArbitrationInput.Finished a.Key)
            | OFinishStale k -> Some(ArbitrationInput.Finished(1 + k % (max 1 n)))
        match input with
        | None -> ()
        | Some i ->
            let s2, ds = Arbitration.step policy s i
            errors <- errors @ (checkStep order s i s2 ds |> List.map (fun e -> sprintf "%s after %A" e i))
            accepted <- accepted @ (ds |> List.choose (function ArbitrationDecision.Accepted k -> Some k | _ -> None))
            all <- all @ ds
            s <- s2
    { State = s; Decisions = all; Accepted = accepted; Errors = errors }

let tests =
    testList
        "Decision.Arbitration"
        [ propertyN "invariants hold after every input (one active, the rules' order, the top band runs, permissions, cancel-all immediate, no silent loss)" 400 ops
          <| fun opsList ->
              let r = run opsList
              if not r.Errors.IsEmpty then failwithf "%s" (String.concat "; " r.Errors)
              true

          propertyN "no starvation: draining by Finished runs every live entry in the rules' order and ends each accepted entry exactly once" 300 ops
          <| fun opsList ->
              let r = run opsList
              let order = r.Accepted |> List.map (fun k -> k, k) |> Map.ofList
              let expectedDrain =
                  r.State.Entries
                  |> List.sortBy (fun e -> -e.Band, (if e.Band = 3 then -order.[e.Key] else order.[e.Key]))
                  |> List.map (fun e -> e.Key)
              let mutable s = r.State
              let mutable drained = []
              let mutable decisions = r.Decisions
              while s.Active.IsSome do
                  let k = s.Active.Value.Key
                  drained <- drained @ [ k ]
                  let s2, ds = Arbitration.step policy s (ArbitrationInput.Finished k)
                  decisions <- decisions @ ds
                  s <- s2
              let endedCounts = decisions |> List.choose (function ArbitrationDecision.Ended(k, _) -> Some k | _ -> None) |> List.countBy id
              drained = expectedDrain
              && s.Entries.IsEmpty
              && endedCounts |> List.forall (fun (_, c) -> c = 1)
              && List.sort (List.map fst endedCounts) = List.sort r.Accepted

          testCase "pre-emption chain: band 1 -> band 2 -> band 3, then releases resume each in turn"
          <| fun () ->
              let steps =
                  [ ArbitrationInput.Submit(4, 1, (), SubmitMode.Replace)
                    ArbitrationInput.Submit(2, 2, (), SubmitMode.Replace)
                    ArbitrationInput.Submit(0, 3, (), SubmitMode.Replace)
                    ArbitrationInput.Release 0
                    ArbitrationInput.Release 2 ]
              let s, ds =
                  steps
                  |> List.fold (fun (s, acc) i -> let s2, d = Arbitration.step policy s i in s2, acc @ d) (Arbitration.empty, [])
              let expected =
                  [ ArbitrationDecision.Accepted 1
                    ArbitrationDecision.Activated(1, false)
                    ArbitrationDecision.Accepted 2
                    ArbitrationDecision.Preempted(1, 2)
                    ArbitrationDecision.Activated(2, false)
                    ArbitrationDecision.Accepted 3
                    ArbitrationDecision.Preempted(2, 3)
                    ArbitrationDecision.Activated(3, false)
                    ArbitrationDecision.Ended(3, EndReason.Released)
                    ArbitrationDecision.Activated(2, true)
                    ArbitrationDecision.Ended(2, EndReason.Released)
                    ArbitrationDecision.Activated(1, true) ]
              Expect.equal ds expected "decisions"
              Expect.equal (s.Active |> Option.map (fun e -> e.Key)) (Some 1) "active"

          testCase "the takeover band is newest-first; the other bands are first come, first served"
          <| fun () ->
              let go inputs = inputs |> List.fold (fun s i -> fst (Arbitration.step policy s i)) Arbitration.empty
              let t = go [ ArbitrationInput.Submit(0, 1, (), SubmitMode.Queue); ArbitrationInput.Submit(1, 2, (), SubmitMode.Queue) ]
              Expect.equal (t.Active |> Option.map (fun e -> e.Key)) (Some 2) "newest wins in band 3"
              let f = go [ ArbitrationInput.Submit(2, 1, (), SubmitMode.Queue); ArbitrationInput.Submit(3, 2, (), SubmitMode.Queue) ]
              Expect.equal (f.Active |> Option.map (fun e -> e.Key)) (Some 1) "earliest wins in band 2"

          testCase "refusals: duplicate key, unknown key, queue full"
          <| fun () ->
              let s1, _ = Arbitration.step policy Arbitration.empty (ArbitrationInput.Submit(4, 7, (), SubmitMode.Queue))
              Expect.equal (snd (Arbitration.step policy s1 (ArbitrationInput.Submit(5, 7, (), SubmitMode.Queue)))) [ ArbitrationDecision.Refused(ArbitrationRefusal.DuplicateKey 7) ] "duplicate"
              Expect.equal (snd (Arbitration.step policy s1 (ArbitrationInput.Cancel(4, 99)))) [ ArbitrationDecision.Refused(ArbitrationRefusal.UnknownKey 99) ] "unknown"
              let full =
                  [ 1..7 ] |> List.fold (fun s k -> fst (Arbitration.step policy s (ArbitrationInput.Submit(5, 100 + k, (), SubmitMode.Queue)))) s1
              Expect.equal (snd (Arbitration.step policy full (ArbitrationInput.Submit(4, 200, (), SubmitMode.Queue)))) [ ArbitrationDecision.Refused ArbitrationRefusal.QueueFull ] "queue full" ]
