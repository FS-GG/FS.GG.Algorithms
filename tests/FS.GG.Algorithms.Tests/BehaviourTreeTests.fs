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
/// subtree.
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
    let get (d: System.Collections.Generic.Dictionary<int, int>) k = match d.TryGetValue k with | true, v -> v | _ -> 0
    let clear (t: Indexed) =
        for k in t.Index .. t.End - 1 do
            cursor.Remove k |> ignore
            if k > t.Index then finished.Remove k |> ignore
    member this.Tick(leaf: int -> 'B -> BtStatus * 'B, bb: 'B) =
        let mutable b = bb
        let trace = ResizeArray<int>()
        let rec eval (t: Indexed) : BtStatus =
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
        status, b, List.ofSeq trace

// ---------------------------------------------------------------- generators

let private treeGen: Gen<Tree> =
    let rec tree depth =
        if depth = 0 then
            Gen.map BehaviourTree.Leaf (Gen.choose (0, 5))
        else
            let sub = tree (depth - 1)
            let kids = Gen.choose (0, 4) |> Gen.bind (fun n -> Gen.listOfLength n sub)
            Gen.frequency
                [ 3, Gen.map BehaviourTree.Leaf (Gen.choose (0, 5))
                  3, Gen.map BehaviourTree.Sequence kids
                  3, Gen.map BehaviourTree.Selector kids
                  2,
                  gen {
                      let! n = Gen.choose (1, 4)
                      let! k = Gen.listOfLength n sub
                      let! sa = Gen.choose (1, n)
                      let! fa = Gen.choose (1, n)
                      return BehaviourTree.Parallel(sa, fa, k)
                  }
                  1, Gen.map BehaviourTree.Invert sub
                  1, Gen.map BehaviourTree.ForceSuccess sub
                  1, Gen.map BehaviourTree.ForceFailure sub
                  1, Gen.map2 (fun n c -> BehaviourTree.Repeat(n, c)) (Gen.choose (1, 3)) sub
                  1, Gen.map2 (fun n c -> BehaviourTree.Retry(n, c)) (Gen.choose (1, 3)) sub ]
    Gen.choose (0, 4) |> Gen.bind tree

let private status = Gen.elements [ BtStatus.Success; BtStatus.Failure; BtStatus.Running ]

/// Per leaf id 0..5, a script of statuses cycled by that leaf's own call count. The blackboard is the
/// per-leaf call counts plus a log of (leaf id) calls, so every leaf both reads and writes it.
let private scripts = Gen.arrayOfLength 6 (Gen.nonEmptyListOf status |> Gen.map (List.truncate 4 >> Array.ofList))

type private Bb = { Calls: Map<int, int>; Log: int list }

let private scriptedLeaf (scripts: BtStatus[][]) (l: int) (bb: Bb) =
    let n = bb.Calls.TryFind l |> Option.defaultValue 0
    let s = scripts.[l].[n % scripts.[l].Length]
    s, { Calls = bb.Calls.Add(l, n + 1); Log = l :: bb.Log }

let private emptyBb = { Calls = Map.empty; Log = [] }

// ---------------------------------------------------------------- tests

let tests =
    testList
        "Decision.BehaviourTree"
        [ property "equals the recursive reference interpreter over 8 ticks (status, blackboard and leaf trace)"
              (Arb.fromGen (Gen.zip treeGen scripts))
          <| fun (tree, sc) ->
              let program = BehaviourTree.compile tree
              let model = Model(tree)
              let mutable state = BehaviourTree.initial program
              let mutable bbLib = emptyBb
              let mutable bbModel = emptyBb
              let mutable ok = true
              for _ in 1..8 do
                  let s1, st, b1, tr1 = BehaviourTree.tickTraced program (scriptedLeaf sc) state bbLib
                  let s2, b2, tr2 = model.Tick(scriptedLeaf sc, bbModel)
                  if s1 <> s2 || b1 <> b2 || tr1 <> tr2 then ok <- false
                  state <- st
                  bbLib <- b1
                  bbModel <- b2
              ok

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
              Expect.equal (s2, calls) (BtStatus.Success, n) "every leaf once" ]
