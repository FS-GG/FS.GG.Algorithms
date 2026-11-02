module FS.GG.Algorithms.Tests.HeapTests

open Expecto
open FsCheck
open FsCheck.FSharp
open FS.GG.Algorithms.Collections
open FS.GG.Algorithms.Tests.Support

// ---------------------------------------------------------------- BinaryHeap against a sorted-list model

type HeapOp =
    | Push of priority: int * value: int
    | Pop
    | Peek

let private heapOps =
    Gen.frequency
        [ 5, Gen.map2 (fun p v -> Push(p, v)) (Gen.choose (-4, 4)) (Gen.choose (0, 1000))
          3, Gen.constant Pop
          1, Gen.constant Peek ]
    |> Gen.listOf
    |> Arb.fromGen

/// The model: a list of (priority, value, stamp). pop takes the least (priority, stamp): the frozen contract.
let private runModel (ops: HeapOp list) =
    let mutable items = []
    let mutable stamp = 0
    [ for op in ops do
          match op with
          | Push(p, v) ->
              items <- items @ [ p, v, stamp ]
              stamp <- stamp + 1
          | Pop ->
              match items |> List.sortBy (fun (p, _, s) -> p, s) with
              | [] -> yield None
              | (p, v, s) :: _ ->
                  items <- items |> List.filter (fun (_, _, s') -> s' <> s)
                  yield Some(p, v)
          | Peek ->
              match items |> List.sortBy (fun (p, _, s) -> p, s) with
              | [] -> yield None
              | (p, v, _) :: _ -> yield Some(p, v) ]

let private runHeap (ops: HeapOp list) =
    let h = BinaryHeap.create (fun (a: int * int) (b: int * int) -> compare (fst a) (fst b))
    [ for op in ops do
          match op with
          | Push(p, v) -> BinaryHeap.push (p, v) h
          | Pop -> yield BinaryHeap.tryPop h
          | Peek -> yield BinaryHeap.tryPeek h ]

// ---------------------------------------------------------------- IndexedHeap against a map model

type IndexedOp =
    | Insert of key: int * priority: int
    | Decrease of key: int * priority: int
    | Update of key: int * priority: int
    | Remove of key: int
    | IPop
    | IPeek

let private indexedOps =
    let key = Gen.choose (0, 12)
    let prio = Gen.choose (0, 6)
    Gen.frequency
        [ 4, Gen.map2 (fun k p -> Insert(k, p)) key prio
          2, Gen.map2 (fun k p -> Decrease(k, p)) key prio
          2, Gen.map2 (fun k p -> Update(k, p)) key prio
          1, Gen.map Remove key
          3, Gen.constant IPop
          1, Gen.constant IPeek ]
    |> Gen.listOf
    |> Arb.fromGen

type Observation =
    | Refused
    | Done
    | Removed of bool
    | Popped of (int * int) option

let private runIndexedModel (ops: IndexedOp list) =
    let mutable present: Map<int, int * int> = Map.empty // key -> (priority, stamp)
    let mutable stamp = 0
    let set k p =
        present <- present.Add(k, (p, stamp))
        stamp <- stamp + 1
    let least () =
        present
        |> Map.toList
        |> List.sortBy (fun (_, (p, s)) -> p, s)
        |> List.tryHead
        |> Option.map (fun (k, (p, _)) -> k, p)
    [ for op in ops do
          match op with
          | Insert(k, p) ->
              if present.ContainsKey k then
                  yield Refused
              else
                  set k p
                  yield Done
          | Decrease(k, p) ->
              match present.TryFind k with
              | Some(cur, _) when p <= cur ->
                  set k p
                  yield Done
              | _ -> yield Refused
          | Update(k, p) ->
              set k p
              yield Done
          | Remove k ->
              yield Removed(present.ContainsKey k)
              present <- present.Remove k
          | IPop ->
              let top = least ()
              top |> Option.iter (fun (k, _) -> present <- present.Remove k)
              yield Popped top
          | IPeek -> yield Popped(least ()) ]

let private runIndexed (ops: IndexedOp list) =
    let h = IndexedHeap.create compare
    let attempt f =
        try
            f ()
            Done
        with :? System.ArgumentException ->
            Refused
    [ for op in ops do
          match op with
          | Insert(k, p) -> yield attempt (fun () -> IndexedHeap.insert k p h)
          | Decrease(k, p) -> yield attempt (fun () -> IndexedHeap.decreaseKey k p h)
          | Update(k, p) -> yield attempt (fun () -> IndexedHeap.update k p h)
          | Remove k -> yield Removed(IndexedHeap.remove k h)
          | IPop -> yield Popped(IndexedHeap.tryPop h)
          | IPeek -> yield Popped(IndexedHeap.tryPeek h) ]

let tests =
    testList
        "Collections.Heap"
        [ property "BinaryHeap: every operation sequence matches the sorted-list model (frozen FIFO among equals)" heapOps
          <| fun ops -> runHeap ops = runModel ops

          property
              "BinaryHeap: pushing then popping everything is a stable sort by priority"
              (Arb.fromGen (Gen.listOf (Gen.zip (Gen.choose (-3, 3)) (Gen.choose (0, 99)))))
          <| fun items ->
              let h = BinaryHeap.create (fun (a: int * int) (b: int * int) -> compare (fst a) (fst b))
              items |> List.iter (fun x -> BinaryHeap.push x h)
              let popped = List.init items.Length (fun _ -> BinaryHeap.pop h)
              popped = List.sortWith (fun a b -> compare (fst a) (fst b)) items && BinaryHeap.isEmpty h

          property "BinaryHeap: count tracks pushes and pops" heapOps
          <| fun ops ->
              let h = BinaryHeap.create compare
              let mutable expected = 0
              ops
              |> List.forall (fun op ->
                  match op with
                  | Push(p, _) ->
                      BinaryHeap.push p h
                      expected <- expected + 1
                  | Pop ->
                      if BinaryHeap.tryPop h |> Option.isSome then expected <- expected - 1
                  | Peek -> ()
                  BinaryHeap.count h = expected)

          testCase "BinaryHeap: pop on empty raises InvalidOperationException"
          <| fun () ->
              let h = BinaryHeap.create compare<int>
              Expect.isTrue (raises<System.InvalidOperationException> (fun () -> BinaryHeap.pop h |> ignore)) "pop"

          testCase "BinaryHeap: equal priorities pop in push order, after clear too"
          <| fun () ->
              let h = BinaryHeap.create (fun (a: int * string) (b: int * string) -> compare (fst a) (fst b))
              for x in [ 1, "a"; 0, "b"; 1, "c"; 0, "d"; 1, "e" ] do
                  BinaryHeap.push x h
              let order = List.init 5 (fun _ -> snd (BinaryHeap.pop h))
              Expect.equal order [ "b"; "d"; "a"; "c"; "e" ] "FIFO among equals"
              BinaryHeap.clear h
              BinaryHeap.push (5, "x") h
              BinaryHeap.push (5, "y") h
              Expect.equal (snd (BinaryHeap.pop h)) "x" "FIFO after clear"

          property "IndexedHeap: every operation sequence matches the map model (insert/decrease/update/remove/pop)" indexedOps
          <| fun ops -> runIndexed ops = runIndexedModel ops

          property "IndexedHeap: pop order equals a lazy-deletion BinaryHeap fed the same updates" indexedOps
          <| fun ops ->
              // Lazy deletion: push (priority, stamp-ordered) entries; skip stale ones on pop.
              let ih = IndexedHeap.create compare
              let lazyHeap = BinaryHeap.create (fun (p1: int, _: int, _: int) (p2, _, _) -> compare p1 p2)
              let current = System.Collections.Generic.Dictionary<int, int>() // key -> version
              let mutable version = 0
              let rec lazyPop () =
                  match BinaryHeap.tryPop lazyHeap with
                  | None -> None
                  | Some(p, k, v) ->
                      match current.TryGetValue k with
                      | true, cur when cur = v ->
                          current.Remove k |> ignore
                          Some(k, p)
                      | _ -> lazyPop ()
              let set k p =
                  version <- version + 1
                  current.[k] <- version
                  BinaryHeap.push (p, k, version) lazyHeap
              ops
              |> List.forall (fun op ->
                  match op with
                  | Insert(k, p) when not (IndexedHeap.contains k ih) ->
                      IndexedHeap.insert k p ih
                      set k p
                      true
                  | Update(k, p) ->
                      IndexedHeap.update k p ih
                      set k p
                      true
                  | Decrease(k, p) when (IndexedHeap.tryPriority k ih |> Option.exists (fun c -> p <= c)) ->
                      IndexedHeap.decreaseKey k p ih
                      set k p
                      true
                  | Remove k ->
                      let was = IndexedHeap.remove k ih
                      let lazyWas = current.Remove k
                      was = lazyWas
                  | IPop -> IndexedHeap.tryPop ih = lazyPop ()
                  | _ -> true)

          testCase "IndexedHeap: refusals (negative key, duplicate insert, absent or increasing decreaseKey)"
          <| fun () ->
              let h = IndexedHeap.create compare<int>
              Expect.isTrue (raises<System.ArgumentException> (fun () -> IndexedHeap.insert -1 0 h)) "negative key"
              IndexedHeap.insert 3 5 h
              Expect.isTrue (raises<System.ArgumentException> (fun () -> IndexedHeap.insert 3 1 h)) "duplicate"
              Expect.isTrue (raises<System.ArgumentException> (fun () -> IndexedHeap.decreaseKey 4 1 h)) "absent"
              Expect.isTrue (raises<System.ArgumentException> (fun () -> IndexedHeap.decreaseKey 3 6 h)) "increase"
              Expect.isTrue (raises<System.ArgumentException> (fun () -> IndexedHeap.update -2 0 h)) "update negative"
              Expect.isFalse (IndexedHeap.contains -1 h) "negative never present"
              Expect.equal (IndexedHeap.tryPop h) (Some(3, 5)) "still intact"

          testCase "IndexedHeap: a restamped key moves behind keys already holding an equal priority"
          <| fun () ->
              let h = IndexedHeap.create compare<int>
              IndexedHeap.insert 0 7 h
              IndexedHeap.insert 1 7 h
              IndexedHeap.insert 2 7 h
              IndexedHeap.decreaseKey 0 7 h // equal priority: restamped, now last among the 7s
              let order = List.init 3 (fun _ -> fst (IndexedHeap.tryPop h).Value)
              Expect.equal order [ 1; 2; 0 ] "restamp is remove-and-insert"

          property "BinaryHeap: renumbering stamps at a small limit leaves every pop unchanged" heapOps
          <| fun ops ->
              // Keep fewer than `limit` live entries (a push at the cap becomes a pop) so renumbering always fits.
              let limit = 6
              let mutable live = 0
              let bounded =
                  ops
                  |> List.map (fun op ->
                      match op with
                      | Push _ when live >= limit - 1 ->
                          live <- live - 1
                          Pop
                      | Push _ ->
                          live <- live + 1
                          op
                      | Pop ->
                          live <- max 0 (live - 1)
                          op
                      | Peek -> op)
              let h = BinaryHeap.createWithStampLimit limit (fun (a: int * int) (b: int * int) -> compare (fst a) (fst b))
              let observed =
                  [ for op in bounded do
                        match op with
                        | Push(p, v) -> BinaryHeap.push (p, v) h
                        | Pop -> yield BinaryHeap.tryPop h
                        | Peek -> yield BinaryHeap.tryPeek h ]
              observed = runModel bounded

          property "IndexedHeap: renumbering stamps at a small limit leaves every pop unchanged" indexedOps
          <| fun ops ->
              // 13 keys fit under the limit, so renumbering always has room; it fires many times per run.
              let limit = 16
              let h = IndexedHeap.createWithStampLimit limit compare
              let attempt f =
                  try
                      f ()
                      Done
                  with :? System.ArgumentException ->
                      Refused
              let observed =
                  [ for op in ops do
                        match op with
                        | Insert(k, p) -> yield attempt (fun () -> IndexedHeap.insert k p h)
                        | Decrease(k, p) -> yield attempt (fun () -> IndexedHeap.decreaseKey k p h)
                        | Update(k, p) -> yield attempt (fun () -> IndexedHeap.update k p h)
                        | Remove k -> yield Removed(IndexedHeap.remove k h)
                        | IPop -> yield Popped(IndexedHeap.tryPop h)
                        | IPeek -> yield Popped(IndexedHeap.tryPeek h) ]
              observed = runIndexedModel ops

          testCase "Stack safety: 10^5 pushes and pops (iterative sift, no recursion)"
          <| fun () ->
              let n = 100000
              let h = BinaryHeap.create compare<int>
              for i in 0 .. n - 1 do
                  BinaryHeap.push ((i * 7919) % n) h
              let mutable prev = -1
              let mutable ok = true
              for _ in 1..n do
                  let x = BinaryHeap.pop h
                  if x < prev then ok <- false
                  prev <- x
              Expect.isTrue ok "sorted" ]
