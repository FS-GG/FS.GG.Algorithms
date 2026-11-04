module FS.GG.Algorithms.Tests.BehaviourTreeTests

open Expecto
open FsCheck
open FsCheck.FSharp
open FS.GG.Algorithms.Decision
open FS.GG.Algorithms.Tests.Support

type private Tree = BehaviourTree<int>

// ---------------------------------------------------------------- the reference interpreter (recursive)

/// An independent, recursive interpreter of the documented semantics. Nodes are numbered in preorder by its
/// own walk into an indexed copy of the tree; memory lives in two dictionaries; a finished node clears its
/// subtree. A reactive composite halts by clearing EVERY child after the one that took over (the library
/// halts only the child at its cursor), so the two agree only if that child is the only non-idle one. The
/// model also records each node's last status in the tick, from which `Live` is the live path.
type private Indexed =
    { Index: int
      End: int
      Node: Tree
      Kids: Indexed list }

type private Model(tree: Tree) =
    let cursor = System.Collections.Generic.Dictionary<int, int>()
    let finished = System.Collections.Generic.Dictionary<int, int>()
    let kids (t: Tree) =
        match t with
        | BehaviourTree.Leaf _ -> []
        | BehaviourTree.Sequence k
        | BehaviourTree.Selector k
        | BehaviourTree.ReactiveSequence k
        | BehaviourTree.ReactiveFallback k
        | BehaviourTree.Parallel(_, _, k) -> k
        | BehaviourTree.Invert c
        | BehaviourTree.ForceSuccess c
        | BehaviourTree.ForceFailure c
        | BehaviourTree.Repeat(_, c)
        | BehaviourTree.Retry(_, c) -> [ c ]
    let rec index (start: int) (t: Tree) : Indexed =
        let mutable next = start + 1
        let ks =
            [ for c in kids t do
                  let ic = index next c
                  next <- ic.End
                  yield ic ]
        { Index = start; End = next; Node = t; Kids = ks }
    let root = index 0 tree
    let last = System.Collections.Generic.Dictionary<int, BtStatus>()
    // Independent observation of effect lifetimes: remember Running leaf occurrences, removing them on
    // completion or a subtree clear. This does not infer running leaves from the library's cursor encoding.
    let runningLeaves = System.Collections.Generic.HashSet<int>()
    let halted = ResizeArray<int>()
    let get (d: System.Collections.Generic.Dictionary<int, int>) k = match d.TryGetValue k with | true, v -> v | _ -> 0
    let clear (t: Indexed) =
        for k in t.Index .. t.End - 1 do
            if runningLeaves.Remove k then halted.Add k
            cursor.Remove k |> ignore
            if k > t.Index then finished.Remove k |> ignore
    /// The node count of the model's own numbering.
    member _.NodeCount = root.End
    /// The memory in the library's encoding: per node in preorder, the cursor and the parallel result.
    member _.Cursors = [ for k in 0 .. root.End - 1 -> get cursor k ]
    member _.Finished = [ for k in 0 .. root.End - 1 -> get finished k ]
    /// After a tick: the nodes whose last status in that tick was Running with every ancestor's too, and each
    /// such node's kind tag (true for a Parallel).
    member _.Live =
        let live = System.Collections.Generic.Dictionary<int, bool>()
        let rec walk (t: Indexed) =
            match last.TryGetValue t.Index with
            | true, BtStatus.Running ->
                live.[t.Index] <- (match t.Node with BehaviourTree.Parallel _ -> true | _ -> false)
                for c in t.Kids do
                    walk c
            | _ -> ()
        walk root
        live
    /// The parent of each node in preorder (-1 for the root).
    member _.Parents =
        let parent = Array.create root.End -1
        let rec walk (t: Indexed) =
            for c in t.Kids do
                parent.[c.Index] <- t.Index
                walk c
        walk root
        parent
    member this.Tick(leaf: int -> 'B -> BtStatus * 'B, bb: 'B) =
        let mutable b = bb
        let trace = ResizeArray<int>()
        last.Clear()
        halted.Clear()
        let rec eval (t: Indexed) : BtStatus =
            let s = evalNode t
            last.[t.Index] <- s
            s
        and evalNode (t: Indexed) : BtStatus =
            let i = t.Index
            let k = t.Kids
            let done' (s: BtStatus) =
                if s <> BtStatus.Running then clear t
                s
            match t.Node with
            | BehaviourTree.Leaf l ->
                let s, nb = leaf l b
                b <- nb
                trace.Add i
                if s = BtStatus.Running then runningLeaves.Add i |> ignore
                else runningLeaves.Remove i |> ignore
                done' s
            | BehaviourTree.Sequence _
            | BehaviourTree.Selector _ ->
                let isSeq = (match t.Node with BehaviourTree.Sequence _ -> true | _ -> false)
                let rec loop () =
                    let c = get cursor i
                    if c >= k.Length then done' (if isSeq then BtStatus.Success else BtStatus.Failure)
                    else
                        match eval k.[c] with
                        | BtStatus.Running -> BtStatus.Running
                        | s when (s = BtStatus.Success) = isSeq ->
                            cursor.[i] <- c + 1
                            loop ()
                        | s -> done' s
                loop ()
            | BehaviourTree.Parallel(sa, fa, _) ->
                for c in k do
                    if get finished c.Index = 0 then
                        match eval c with
                        | BtStatus.Success -> finished.[c.Index] <- 1
                        | BtStatus.Failure -> finished.[c.Index] <- 2
                        | BtStatus.Running -> ()
                let s = k |> List.filter (fun c -> get finished c.Index = 1) |> List.length
                let f = k |> List.filter (fun c -> get finished c.Index = 2) |> List.length
                if s >= sa then done' BtStatus.Success
                elif f >= fa then done' BtStatus.Failure
                elif s + f = k.Length then done' BtStatus.Failure
                else BtStatus.Running
            | BehaviourTree.Invert _ ->
                match eval k.Head with
                | BtStatus.Running -> BtStatus.Running
                | BtStatus.Success -> done' BtStatus.Failure
                | BtStatus.Failure -> done' BtStatus.Success
            | BehaviourTree.ForceSuccess _ ->
                (match eval k.Head with BtStatus.Running -> BtStatus.Running | _ -> done' BtStatus.Success)
            | BehaviourTree.ForceFailure _ ->
                (match eval k.Head with BtStatus.Running -> BtStatus.Running | _ -> done' BtStatus.Failure)
            | BehaviourTree.ReactiveSequence _
            | BehaviourTree.ReactiveFallback _ ->
                let go = (match t.Node with BehaviourTree.ReactiveSequence _ -> BtStatus.Success | _ -> BtStatus.Failure)
                let rec loop (c: int) =
                    if c >= k.Length then done' go
                    else
                        let s = eval k.[c]
                        if s = go then loop (c + 1)
                        else
                            // Child c takes over: every later child is abandoned, so halt them all.
                            for later in List.skip (c + 1) k do
                                clear later
                            if s = BtStatus.Running then
                                cursor.[i] <- c
                                s
                            else done' s
                loop 0
            | BehaviourTree.Repeat(n, _)
            | BehaviourTree.Retry(n, _) ->
                let counted = (match t.Node with BehaviourTree.Repeat _ -> BtStatus.Success | _ -> BtStatus.Failure)
                let rec loop () =
                    match eval k.Head with
                    | BtStatus.Running -> BtStatus.Running
                    | s when s = counted ->
                        cursor.[i] <- get cursor i + 1
                        if get cursor i >= n then done' counted else loop ()
                    | s -> done' s
                loop ()
        let status = eval root
        status, b, List.ofSeq trace, List.ofSeq halted

// ---------------------------------------------------------------- generators

/// Random trees over every node kind, up to `maxDepth` deep with up to `maxKids` children per composite and
/// Repeat/Retry counts up to `maxCount`. Every accepted shape is reachable: empty Sequence, Selector and
/// reactive composites, and Parallel thresholds anywhere in 1..children.
let private treeOf (maxDepth: int) (maxKids: int) (maxCount: int) : Gen<Tree> =
    let rec tree depth =
        if depth = 0 then
            Gen.map BehaviourTree.Leaf (Gen.choose (0, 5))
        else
            let sub = tree (depth - 1)
            let kids = Gen.choose (0, maxKids) |> Gen.bind (fun n -> Gen.listOfLength n sub)
            Gen.frequency
                [ 3, Gen.map BehaviourTree.Leaf (Gen.choose (0, 5))
                  3, Gen.map BehaviourTree.Sequence kids
                  3, Gen.map BehaviourTree.Selector kids
                  3, Gen.map BehaviourTree.ReactiveSequence kids
                  3, Gen.map BehaviourTree.ReactiveFallback kids
                  2,
                  gen {
                      let! n = Gen.choose (1, maxKids)
                      let! k = Gen.listOfLength n sub
                      let! sa = Gen.choose (1, n)
                      let! fa = Gen.choose (1, n)
                      return BehaviourTree.Parallel(sa, fa, k)
                  }
                  1, Gen.map BehaviourTree.Invert sub
                  1, Gen.map BehaviourTree.ForceSuccess sub
                  1, Gen.map BehaviourTree.ForceFailure sub
                  1, Gen.map2 (fun n c -> BehaviourTree.Repeat(n, c)) (Gen.choose (1, maxCount)) sub
                  1, Gen.map2 (fun n c -> BehaviourTree.Retry(n, c)) (Gen.choose (1, maxCount)) sub ]
    Gen.choose (0, maxDepth) |> Gen.bind tree

let private treeGen: Gen<Tree> = treeOf 4 4 3

/// The invariant fuzz's wider range: deeper, wider, larger counts.
let private fuzzTreeGen: Gen<Tree> = treeOf 6 5 4

let private status = Gen.elements [ BtStatus.Success; BtStatus.Failure; BtStatus.Running ]

/// Per leaf id 0..5, a script of statuses cycled by that leaf's own call count. The blackboard is the
/// per-leaf call counts plus a log of (leaf id) calls, so every leaf both reads and writes it.
let private scriptsOf (maxLength: int) =
    Gen.arrayOfLength 6 (Gen.choose (1, maxLength) |> Gen.bind (fun n -> Gen.listOfLength n status) |> Gen.map Array.ofList)

let private scripts = scriptsOf 4

type private Bb = { Calls: Map<int, int>; Log: int list }

let private scriptedLeaf (scripts: BtStatus[][]) (l: int) (bb: Bb) =
    let n = bb.Calls.TryFind l |> Option.defaultValue 0
    let s = scripts.[l].[n % scripts.[l].Length]
    s, { Calls = bb.Calls.Add(l, n + 1); Log = l :: bb.Log }

let private emptyBb = { Calls = Map.empty; Log = [] }

/// The halt invariant over a state in the library's encoding: a non-zero cursor or counter only on the live
/// path, and a non-zero parallel result only on a child of a live Parallel.
let private haltInvariant (live: System.Collections.Generic.Dictionary<int, bool>) (parents: int[]) (cursors: int list) (finished: int list) =
    let c = Array.ofList cursors
    let f = Array.ofList finished
    let liveParallel (n: int) =
        n >= 0 && (match live.TryGetValue n with | true, isParallel -> isParallel | _ -> false)
    c.Length = parents.Length
    && f.Length = parents.Length
    && Seq.forall (fun n -> (c.[n] = 0 || live.ContainsKey n) && (f.[n] = 0 || liveParallel parents.[n])) (seq { 0 .. c.Length - 1 })

/// Runs library and model in lockstep for `ticks` ticks: status, blackboard, trace and the whole state must
/// agree, and both states must keep the halt invariant after every tick.
let private lockstep (tree: Tree) (sc: BtStatus[][]) (ticks: int) =
    let program = BehaviourTree.compile tree
    let model = Model(tree)
    let parents = model.Parents
    let mutable state = BehaviourTree.initial program
    let mutable bbLib = emptyBb
    let mutable bbModel = emptyBb
    let mutable ok = model.NodeCount = program.NodeCount
    for _ in 1..ticks do
        let s1, st, b1, tr1, h1 = BehaviourTree.tickTracedWithHalts program (scriptedLeaf sc) state bbLib
        let s2, b2, tr2, h2 = model.Tick(scriptedLeaf sc, bbModel)
        let oldS, oldSt, oldBb, oldTrace = BehaviourTree.tickTraced program (scriptedLeaf sc) state bbLib
        if (oldS, oldSt, oldBb, oldTrace) <> (s1, st, b1, tr1) then ok <- false
        let live = model.Live
        if s1 <> s2 || b1 <> b2 || tr1 <> tr2 || h1 <> h2 then ok <- false
        if st.Cursors <> model.Cursors || st.Finished <> model.Finished then ok <- false
        if not (haltInvariant live parents st.Cursors st.Finished) then ok <- false
        if not (haltInvariant live parents model.Cursors model.Finished) then ok <- false
        if (s1 = BtStatus.Running) <> live.ContainsKey 0 then ok <- false
        state <- st
        bbLib <- b1
        bbModel <- b2
    ok

// ---------------------------------------------------------------- tests

let tests =
    testList
        "Decision.BehaviourTree"
        [ property "equals the recursive reference interpreter over 8 ticks (status, blackboard, visited/halted leaves and state)"
              (Arb.fromGen (Gen.zip treeGen scripts))
          <| fun (tree, sc) -> lockstep tree sc 8

          // The invariant fuzz: thousands of random trees (every kind, reactive composites included) x 24 ticks.
          // After every tick the library equals the model and both keep the halt invariant.
          propertyN "invariant fuzz: halt invariant and model equality after every tick (reactive composites included)"
              (max 3000 (10 * maxTest))
              (Arb.fromGen (Gen.zip fuzzTreeGen (scriptsOf 6)))
          <| fun (tree, sc) -> lockstep tree sc 24

          property "a Sequence fails fast and a Selector succeeds fast (no later sibling is ticked)"
              (Arb.fromGen (Gen.zip (Gen.elements [ true; false ]) (Gen.listOfLength 6 status)))
          <| fun (isSeq, statuses) ->
              let leaves = statuses |> List.mapi (fun i _ -> BehaviourTree.Leaf i)
              let tree = if isSeq then BehaviourTree.Sequence leaves else BehaviourTree.Selector leaves
              let program = BehaviourTree.compile tree
              let st = Array.ofList statuses
              let result, _, (), trace = BehaviourTree.tickTraced program (fun l () -> st.[l], ()) (BehaviourTree.initial program) ()
              let stop = if isSeq then BtStatus.Failure else BtStatus.Success
              let firstStop = statuses |> List.tryFindIndex (fun s -> s = stop || s = BtStatus.Running)
              match firstStop with
              | Some k -> trace = [ 1 .. k + 1 ] && result = statuses.[k]
              | None -> trace = [ 1..6 ] && result = (if isSeq then BtStatus.Success else BtStatus.Failure)

          property "tick idempotence: with constant leaves and no blackboard change, the second tick is a fixed point"
              (Arb.fromGen (Gen.zip treeGen (Gen.arrayOfLength 6 status)))
          <| fun (tree, constant) ->
              let program = BehaviourTree.compile tree
              let leaf (l: int) () = constant.[l], ()
              let s1, st1, () = BehaviourTree.tick program leaf (BehaviourTree.initial program) ()
              let s2, st2, (), tr2 = BehaviourTree.tickTraced program leaf st1 ()
              let s3, st3, (), tr3 = BehaviourTree.tickTraced program leaf st2 ()
              s2 = s1 && st2 = st1 && s3 = s2 && st3 = st2 && tr3 = tr2

          property "a finished root leaves the idle state; only Running keeps memory"
              (Arb.fromGen (Gen.zip treeGen scripts))
          <| fun (tree, sc) ->
              let program = BehaviourTree.compile tree
              let s, st, _ = BehaviourTree.tick program (scriptedLeaf sc) (BehaviourTree.initial program) emptyBb
              s = BtStatus.Running || st = BehaviourTree.initial program

          testCase "running-state resumption: a Sequence resumes at its running child"
          <| fun () ->
              let program = BehaviourTree.compile (BehaviourTree.Sequence [ BehaviourTree.Leaf "check"; BehaviourTree.Leaf "walk"; BehaviourTree.Leaf "open" ])
              let mutable walkTicks = 0
              let leaf l () =
                  match l with
                  | "walk" ->
                      walkTicks <- walkTicks + 1
                      (if walkTicks < 3 then BtStatus.Running else BtStatus.Success), ()
                  | _ -> BtStatus.Success, ()
              let s1, st1, (), t1 = BehaviourTree.tickTraced program leaf (BehaviourTree.initial program) ()
              let s2, st2, (), t2 = BehaviourTree.tickTraced program leaf st1 ()
              let s3, st3, (), t3 = BehaviourTree.tickTraced program leaf st2 ()
              Expect.equal (s1, t1) (BtStatus.Running, [ 1; 2 ]) "tick 1"
              Expect.equal (s2, t2) (BtStatus.Running, [ 2 ]) "tick 2 resumes at walk"
              Expect.equal (s3, t3) (BtStatus.Success, [ 2; 3 ]) "tick 3 finishes"
              Expect.equal st3 (BehaviourTree.initial program) "idle after success"

          testCase "a Parallel that finishes halts its still-running children"
          <| fun () ->
              let tree = BehaviourTree.Parallel(1, 2, [ BehaviourTree.Leaf 0; BehaviourTree.Sequence [ BehaviourTree.Leaf 1; BehaviourTree.Leaf 2 ] ])
              let program = BehaviourTree.compile tree
              let leaf l () = (match l with | 0 -> BtStatus.Success | 1 -> BtStatus.Success | _ -> BtStatus.Running), ()
              let s, st, () = BehaviourTree.tick program leaf (BehaviourTree.initial program) ()
              Expect.equal s BtStatus.Success "one success is enough"
              Expect.equal st (BehaviourTree.initial program) "the running sequence was reset"

          testCase "halt trace excludes completed and never-visited leaves in an abandoned branch"
          <| fun () ->
              let p = BehaviourTree.compile (
                  BehaviourTree.Parallel(1, 2,
                      [ BehaviourTree.Leaf "success"
                        BehaviourTree.Sequence [ BehaviourTree.Leaf "complete"; BehaviourTree.Leaf "running"; BehaviourTree.Leaf "idle" ] ]))
              let mutable calls = []
              let leaf l () =
                  calls <- l :: calls
                  (if l = "running" then BtStatus.Running else BtStatus.Success), ()
              let s, st, (), trace, halted = BehaviourTree.tickTracedWithHalts p leaf (BehaviourTree.initial p) ()
              Expect.equal (s, trace, halted) (BtStatus.Success, [ 1; 3; 4 ], [ 4 ]) "only the running occurrence is cancelled"
              Expect.equal (List.rev calls) [ "success"; "complete"; "running" ] "halting never invokes a leaf"
              Expect.isTrue (st = BehaviourTree.initial p) "idle after finishing"

          testCase "halt order follows abandonment events, then preorder; equal leaf values stay distinct"
          <| fun () ->
              // The inner Parallel halts node 6 first; then the root halts node 1. Both carry leaf value 0.
              let p = BehaviourTree.compile (
                  BehaviourTree.Parallel(1, 2,
                      [ BehaviourTree.Leaf 0
                        BehaviourTree.Sequence
                            [ BehaviourTree.Leaf 1
                              BehaviourTree.Parallel(1, 2, [ BehaviourTree.Leaf 2; BehaviourTree.Leaf 0 ]) ] ]))
              let leaf l () = (if l = 0 then BtStatus.Running else BtStatus.Success), ()
              let s, _, (), _, halted = BehaviourTree.tickTracedWithHalts p leaf (BehaviourTree.initial p) ()
              Expect.equal (s, halted) (BtStatus.Success, [ 6; 1 ]) "event order rather than globally sorted indices"

          testCase "repeated activations can halt the same occurrence twice in one tick"
          <| fun () ->
              let p = BehaviourTree.compile (BehaviourTree.Repeat(2, BehaviourTree.Parallel(1, 2, [ BehaviourTree.Leaf 0; BehaviourTree.Leaf 1 ])))
              let leaf l () = (if l = 0 then BtStatus.Running else BtStatus.Success), ()
              let s, _, (), trace, halted = BehaviourTree.tickTracedWithHalts p leaf (BehaviourTree.initial p) ()
              Expect.equal (s, trace, halted) (BtStatus.Success, [ 2; 3; 2; 3 ], [ 2; 2 ]) "one halt event per activation"

          testCase "preemption halts only active Parallel paths in preorder and preserves the supplied state"
          <| fun () ->
              let p = BehaviourTree.compile (
                  BehaviourTree.ReactiveFallback
                      [ BehaviourTree.Leaf 0
                        BehaviourTree.Parallel(3, 3,
                            [ BehaviourTree.Leaf 1
                              BehaviourTree.Leaf 2
                              BehaviourTree.Sequence [ BehaviourTree.Leaf 3; BehaviourTree.Leaf 4; BehaviourTree.Leaf 5 ] ]) ])
              let leaf interrupt l () =
                  (match l with
                   | 0 -> if interrupt then BtStatus.Success else BtStatus.Failure
                   | 1 | 4 -> BtStatus.Running
                   | _ -> BtStatus.Success), ()
              // An old API creates the running state; the new API must not require hidden tracing history.
              let _, before, () = BehaviourTree.tick p (leaf false) (BehaviourTree.initial p) ()
              let snapshot = before.Cursors, before.Finished
              let s, after, (), trace, halted = BehaviourTree.tickTracedWithHalts p (leaf true) before ()
              Expect.equal (s, trace, halted) (BtStatus.Success, [ 1 ], [ 3; 7 ]) "only running paths, including a zero-cursor leaf"
              Expect.equal (before.Cursors, before.Finished) snapshot "input remains immutable"
              Expect.isTrue (after = BehaviourTree.initial p) "abandoned state cleared"
              let _, _, (), _, retryHalts = BehaviourTree.tickTracedWithHalts p (leaf true) before ()
              Expect.equal retryHalts halted "saved states reproduce halt reports"
              let _, _, (), _, idleHalts = BehaviourTree.tickTracedWithHalts p (leaf true) after ()
              Expect.equal idleHalts [] "a fresh success does not halt idle branches"

          testCase "priority interrupt: a reactive Fallback's higher-priority branch preempts and halts the running one"
          <| fun () ->
              // ReactiveFallback [ ReactiveSequence [danger?; flee]; Sequence [walkA; walkB] ] in preorder:
              // 0 root, 1 guarded flee, 2 danger, 3 flee, 4 patrol (a memory Sequence), 5 walkA, 6 walkB.
              let tree =
                  BehaviourTree.ReactiveFallback
                      [ BehaviourTree.ReactiveSequence [ BehaviourTree.Leaf "danger"; BehaviourTree.Leaf "flee" ]
                        BehaviourTree.Sequence [ BehaviourTree.Leaf "walkA"; BehaviourTree.Leaf "walkB" ] ]
              let program = BehaviourTree.compile tree
              let mutable danger = false
              let leaf l () =
                  match l with
                  | "danger" -> (if danger then BtStatus.Success else BtStatus.Failure), ()
                  | "flee" -> BtStatus.Running, ()
                  | "walkA" -> BtStatus.Success, ()
                  | _ -> BtStatus.Running, ()
              let s1, st1, (), t1 = BehaviourTree.tickTraced program leaf (BehaviourTree.initial program) ()
              Expect.equal (s1, t1) (BtStatus.Running, [ 2; 5; 6 ]) "tick 1: no danger, patrol runs walkB"
              Expect.equal st1.Cursors [ 1; 0; 0; 0; 1; 0; 0 ] "the patrol branch is running at walkB"
              let s2, st2, (), t2 = BehaviourTree.tickTraced program leaf st1 ()
              Expect.equal (s2, t2) (BtStatus.Running, [ 2; 6 ]) "tick 2: re-checked danger, then resumed at walkB"
              Expect.equal st2 st1 "same memory"
              danger <- true
              let s3, st3, (), t3 = BehaviourTree.tickTraced program leaf st2 ()
              Expect.equal (s3, t3) (BtStatus.Running, [ 2; 3 ]) "tick 3: danger preempts; flee runs; walkB is not ticked"
              Expect.equal st3.Cursors [ 0; 1; 0; 0; 0; 0; 0 ] "the patrol branch is halted: its cursor is cleared"
              danger <- false
              let s4, st4, (), t4 = BehaviourTree.tickTraced program leaf st3 ()
              Expect.equal (s4, t4) (BtStatus.Running, [ 2; 5; 6 ]) "tick 4: the danger guard fails, flee is halted, and patrol restarts at walkA, not at a stale walkB"
              Expect.equal st4 st1 "back to the patrol state"

          testCase "a reactive Sequence's failing guard aborts the running action and halts it"
          <| fun () ->
              // ReactiveSequence [ batteryOk?; Sequence [goto; Repeat(3, work)] ]: 0 root, 1 guard, 2 seq, 3 goto, 4 repeat, 5 work.
              let tree =
                  BehaviourTree.ReactiveSequence
                      [ BehaviourTree.Leaf 0
                        BehaviourTree.Sequence [ BehaviourTree.Leaf 1; BehaviourTree.Repeat(3, BehaviourTree.Leaf 2) ] ]
              let program = BehaviourTree.compile tree
              let mutable battery = true
              let mutable workCalls = 0
              let leaf l () =
                  match l with
                  | 0 -> (if battery then BtStatus.Success else BtStatus.Failure), ()
                  | 1 -> BtStatus.Success, ()
                  | _ ->
                      workCalls <- workCalls + 1
                      (if workCalls % 2 = 0 then BtStatus.Running else BtStatus.Success), ()
              let s1, st1, (), t1 = BehaviourTree.tickTraced program leaf (BehaviourTree.initial program) ()
              Expect.equal (s1, t1) (BtStatus.Running, [ 1; 3; 5; 5 ]) "tick 1: work succeeded once, then runs"
              Expect.equal st1.Cursors [ 1; 0; 1; 0; 1; 0 ] "memory on the live path: root, sequence, repeat counter"
              battery <- false
              let s2, st2, (), t2 = BehaviourTree.tickTraced program leaf st1 ()
              Expect.equal (s2, t2) (BtStatus.Failure, [ 1 ]) "tick 2: the guard fails, the action is not ticked"
              Expect.equal st2 (BehaviourTree.initial program) "the running action was halted: idle"

          property "a reactive composite re-ticks from its first child on every tick"
              (Arb.fromGen (Gen.zip (Gen.elements [ true; false ]) (Gen.listOfLength 6 status)))
          <| fun (isSeq, statuses) ->
              let leaves = statuses |> List.mapi (fun i _ -> BehaviourTree.Leaf i)
              let tree = if isSeq then BehaviourTree.ReactiveSequence leaves else BehaviourTree.ReactiveFallback leaves
              let program = BehaviourTree.compile tree
              let st = Array.ofList statuses
              let go = if isSeq then BtStatus.Success else BtStatus.Failure
              let expected =
                  match statuses |> List.tryFindIndex (fun s -> s <> go) with
                  | Some k -> [ 1 .. k + 1 ], statuses.[k]
                  | None -> [ 1..6 ], go
              let mutable state = BehaviourTree.initial program
              let mutable ok = true
              for _ in 1..3 do
                  let result, next, (), trace = BehaviourTree.tickTraced program (fun l () -> st.[l], ()) state ()
                  if (trace, result) <> expected then ok <- false
                  state <- next
              ok

          property "a tick is atomic: a leaf that raises mid-tick leaves the caller's state, and a retry resumes exactly"
              (Arb.fromGen (Gen.zip3 treeGen scripts (Gen.choose (1, 40))))
          <| fun (tree, sc, raiseAt) ->
              let program = BehaviourTree.compile tree
              // The uninterrupted run: per tick, the result.
              let reference =
                  let mutable state = BehaviourTree.initial program
                  let mutable bb = emptyBb
                  [ for _ in 1..8 do
                        let s, next, nb, trace, halted = BehaviourTree.tickTracedWithHalts program (scriptedLeaf sc) state bb
                        state <- next
                        bb <- nb
                        yield s, next, nb, trace, halted ]
              // The same run with the raiseAt-th leaf call raising once; the failed tick is retried from the state
              // and blackboard held before it, with a leaf that no longer raises.
              let mutable calls = 0
              let flaky l (bb: Bb) =
                  calls <- calls + 1
                  if calls = raiseAt then failwith "leaf raised" else scriptedLeaf sc l bb
              let mutable state = BehaviourTree.initial program
              let mutable bb = emptyBb
              let mutable ok = true
              for (s, refNext, refBb, refTrace, refHalts) in reference do
                  let before = state.Cursors, state.Finished
                  let result =
                      try
                          BehaviourTree.tickTracedWithHalts program flaky state bb
                      with _ ->
                          if (state.Cursors, state.Finished) <> before then ok <- false
                          BehaviourTree.tickTracedWithHalts program (scriptedLeaf sc) state bb
                  let s2, next, nb, trace, halted = result
                  if (s2, next, nb, trace, halted) <> (s, refNext, refBb, refTrace, refHalts) then ok <- false
                  state <- next
                  bb <- nb
              ok

          testCase "empty reactive composites: a ReactiveSequence succeeds, a ReactiveFallback fails"
          <| fun () ->
              let p1 = BehaviourTree.compile (BehaviourTree.ReactiveSequence [])
              let p2 = BehaviourTree.compile (BehaviourTree.ReactiveFallback [])
              let s1, _, (), t1 = BehaviourTree.tickTraced p1 (fun _ () -> BtStatus.Running, ()) (BehaviourTree.initial p1) ()
              let s2, _, (), t2 = BehaviourTree.tickTraced p2 (fun _ () -> BtStatus.Running, ()) (BehaviourTree.initial p2) ()
              Expect.equal (s1, t1, s2, t2) (BtStatus.Success, [], BtStatus.Failure, []) "empty"

          testCase "refusals"
          <| fun () ->
              let bad t = raises<System.ArgumentException> (fun () -> BehaviourTree.compile t |> ignore)
              Expect.isTrue (bad (BehaviourTree.Parallel(1, 1, []))) "empty parallel"
              Expect.isTrue (bad (BehaviourTree.Parallel(2, 1, [ BehaviourTree.Leaf 0 ]))) "success threshold"
              Expect.isTrue (bad (BehaviourTree.Parallel(1, 0, [ BehaviourTree.Leaf 0 ]))) "failure threshold"
              Expect.isTrue (bad (BehaviourTree.Repeat(0, BehaviourTree.Leaf 0))) "repeat"
              Expect.isTrue (bad (BehaviourTree.Retry(0, BehaviourTree.Leaf 0))) "retry"
              let p = BehaviourTree.compile (BehaviourTree.Leaf 0)
              let other = BehaviourTree.initial (BehaviourTree.compile (BehaviourTree.Sequence [ BehaviourTree.Leaf 0 ]))
              Expect.isTrue (raises<System.ArgumentException> (fun () -> BehaviourTree.tick p (fun _ () -> BtStatus.Success, ()) other () |> ignore)) "foreign state"

          testCase "Stack safety: a tree 10^5 levels deep and a sequence 10^5 wide"
          <| fun () ->
              let n = 100000
              let mutable deep = BehaviourTree.Leaf 0
              for _ in 1..n do
                  deep <- BehaviourTree.Invert deep
              let p = BehaviourTree.compile deep
              let s, _, () = BehaviourTree.tick p (fun _ () -> BtStatus.Success, ()) (BehaviourTree.initial p) ()
              Expect.equal s BtStatus.Success "an even number of inverts"
              Expect.equal p.NodeCount (n + 1) "nodes"
              let wide = BehaviourTree.compile (BehaviourTree.Sequence(List.replicate n (BehaviourTree.Leaf 0)))
              let mutable calls = 0
              let s2, _, () = BehaviourTree.tick wide (fun _ () -> calls <- calls + 1; BtStatus.Success, ()) (BehaviourTree.initial wide) ()
              Expect.equal (s2, calls) (BtStatus.Success, n) "every leaf once"

          testCase "Stack safety: reactive composites 10^5 deep and 10^5 wide, with a halt of the whole running branch"
          <| fun () ->
              let n = 100000
              // Leaf 1 succeeds and leaf 2 runs, so every level's cursor is 1: the deep branch holds 10^5 cursors.
              let mutable deep = BehaviourTree.Leaf 2
              for _ in 1..n do
                  deep <- BehaviourTree.ReactiveSequence [ BehaviourTree.Leaf 1; deep ]
              let p = BehaviourTree.compile (BehaviourTree.ReactiveFallback [ BehaviourTree.Leaf 0; deep ])
              let mutable interrupt = false
              let leaf l () =
                  match l with
                  | 0 -> (if interrupt then BtStatus.Success else BtStatus.Failure), ()
                  | 1 -> BtStatus.Success, ()
                  | _ -> BtStatus.Running, ()
              let s1, st1, () = BehaviourTree.tick p leaf (BehaviourTree.initial p) ()
              Expect.equal s1 BtStatus.Running "the deep branch runs"
              Expect.equal (st1.Cursors |> List.sum) (n + 1) "a cursor at every level and at the root"
              interrupt <- true
              let s2, st2, (), t2, h2 = BehaviourTree.tickTracedWithHalts p leaf st1 ()
              Expect.equal (s2, t2, h2) (BtStatus.Success, [ 1 ], [ 2 * n + 2 ]) "the interrupt preempts only the deepest running leaf"
              // Booleans, not the 10^5-element states: a failure message stays small.
              Expect.isTrue (st2 = BehaviourTree.initial p) "the 10^5-deep running branch is halted"
              let wide = BehaviourTree.compile (BehaviourTree.ReactiveFallback(List.init n BehaviourTree.Leaf))
              let mutable first = BtStatus.Failure
              let leafW l () = (if l = 0 then first elif l = n - 1 then BtStatus.Running else BtStatus.Failure), ()
              let w1, wst1, () = BehaviourTree.tick wide leafW (BehaviourTree.initial wide) ()
              Expect.equal (w1, List.head wst1.Cursors) (BtStatus.Running, n - 1) "the last child runs"
              first <- BtStatus.Success
              let w2, wst2, (), wt2, wh2 = BehaviourTree.tickTracedWithHalts wide leafW wst1 ()
              Expect.equal (w2, wt2, wh2) (BtStatus.Success, [ 1 ], [ n ]) "the first child preempts only the running last leaf"
              Expect.isTrue (wst2 = BehaviourTree.initial wide) "the running last child is halted" ]
