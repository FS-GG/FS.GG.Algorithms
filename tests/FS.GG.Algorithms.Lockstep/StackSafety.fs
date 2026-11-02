/// ADR-0098 §3 rule 7: every recursive or graph-walking surface has a test over a 10^5-node chain, run under
/// Node's default stack in the Fable build. Each check prints one line; any failure exits non-zero.
module FS.GG.Algorithms.Lockstep.StackSafety

open FS.GG.Algorithms.Collections
open FS.GG.Algorithms.Graph
open FS.GG.Algorithms.Decision
open FS.GG.Algorithms.Search.Grid
open FS.GG.Algorithms.Spatial

let size = 100000

let private heap () =
    let h = BinaryHeap.create (fun (a: int) (b: int) -> compare a b)
    for i in 0 .. size - 1 do
        BinaryHeap.push ((i * 7919) % size) h
    let mutable prev = -1
    let mutable sorted = true
    for _ in 1..size do
        let x = BinaryHeap.pop h
        if x < prev then sorted <- false
        prev <- x
    sorted && BinaryHeap.isEmpty h

let private indexedHeap () =
    let h = IndexedHeap.create (fun (a: int) (b: int) -> compare a b)
    for k in 0 .. size - 1 do
        IndexedHeap.insert k (size - k) h
    for k in 0 .. size - 1 do
        IndexedHeap.decreaseKey k ((size - k) / 2) h
    let mutable count = 0
    let mutable prev = -1
    let mutable sorted = true
    let mutable next = IndexedHeap.tryPop h
    while next.IsSome do
        let (_, p) = next.Value
        if p < prev then sorted <- false
        prev <- p
        count <- count + 1
        next <- IndexedHeap.tryPop h
    sorted && count = size

let private chain () =
    let r = BestFirst.dijkstra (fun i -> if i < size - 1 then [ i + 1, 1 ] else []) compare (fun i -> i = size - 1) 0
    match r.Outcome with
    | Found(path, cost) -> cost = size - 1 && List.length path = size && r.Expanded = size
    | _ -> false

let private chainDistances () =
    let ds = BestFirst.distances (fun i -> if i < size - 1 then [ i + 1, 2 ] else []) compare 0
    List.length ds = size && snd (List.last ds) = 2 * (size - 1)

let private sccChain () =
    let comps = Scc.tarjan [ 0 .. size - 1 ] (fun v -> if v < size - 1 then [ v + 1 ] else [])
    List.length comps = size && List.head comps = [ size - 1 ]

let private sccRing () =
    Scc.cycles [ 0 .. size - 1 ] (fun v -> [ (v + 1) % size ]) |> List.map List.length = [ size ]

let private unionFindChain () =
    let uf = UnionFind.create size
    for i in size - 1 .. -1 .. 1 do
        UnionFind.union i (i - 1) uf |> ignore
    UnionFind.count uf = 1 && UnionFind.representative (size - 1) uf = 0

let private arbitrationQueue () =
    // 10^4 queued entries in one first-come band, drained by Finished: every step stays iterative.
    let n = 10000
    let policy: ArbitrationPolicy<int> =
        { BandOf = (fun _ -> 1)
          OrderOf = (fun _ -> BandOrder.EarliestFirst)
          MaxWaiting = (fun _ -> None) }
    let mutable s = Arbitration.empty<int, int, unit>
    for k in 1..n do
        s <- fst (Arbitration.step policy s (ArbitrationInput.Submit(0, k, (), SubmitMode.Queue)))
    let mutable drained = 0
    while s.Active.IsSome do
        s <- fst (Arbitration.step policy s (ArbitrationInput.Finished s.Active.Value.Key))
        drained <- drained + 1
    drained = n

let private topoChain () =
    let succ v = if v < size - 1 then [ v + 1 ] else []
    match TopoSort.sort compare [ size - 1 .. -1 .. 0 ] succ, TopoSort.criticalPath compare [ 0 .. size - 1 ] succ (fun _ -> 1) with
    | Ok order, Ok s -> List.head order = 0 && List.length order = size && s.Makespan = size && List.length s.CriticalPath = size
    | _ -> false

let private behaviourTreeDeep () =
    let mutable deep = BehaviourTree.Leaf 0
    for _ in 1..size do
        deep <- BehaviourTree.Invert deep
    let program = BehaviourTree.compile deep
    let s, _, () = BehaviourTree.tick program (fun _ () -> BtStatus.Success, ()) (BehaviourTree.initial program) ()
    let wide = BehaviourTree.compile (BehaviourTree.Selector(List.replicate size (BehaviourTree.Leaf 0)))
    let s2, _, () = BehaviourTree.tick wide (fun _ () -> BtStatus.Failure, ()) (BehaviourTree.initial wide) ()
    s = BtStatus.Success && program.NodeCount = size + 1 && s2 = BtStatus.Failure

/// Reactive composites 10^5 deep (a cursor at every level) and 10^5 wide; an interrupt then halts the whole
/// running branch in one tick.
let private behaviourTreeReactive () =
    let mutable deep = BehaviourTree.Leaf 2
    for _ in 1..size do
        deep <- BehaviourTree.ReactiveSequence [ BehaviourTree.Leaf 1; deep ]
    let program = BehaviourTree.compile (BehaviourTree.ReactiveFallback [ BehaviourTree.Leaf 0; deep ])
    let leaf (interrupt: bool) (l: int) () =
        match l with
        | 0 -> (if interrupt then BtStatus.Success else BtStatus.Failure), ()
        | 1 -> BtStatus.Success, ()
        | _ -> BtStatus.Running, ()
    let s1, st1, () = BehaviourTree.tick program (leaf false) (BehaviourTree.initial program) ()
    let s2, st2, () = BehaviourTree.tick program (leaf true) st1 ()
    let wide = BehaviourTree.compile (BehaviourTree.ReactiveFallback(List.init size BehaviourTree.Leaf))
    let leafW (first: BtStatus) (l: int) () = (if l = 0 then first elif l = size - 1 then BtStatus.Running else BtStatus.Failure), ()
    let w1, wst1, () = BehaviourTree.tick wide (leafW BtStatus.Failure) (BehaviourTree.initial wide) ()
    let w2, wst2, () = BehaviourTree.tick wide (leafW BtStatus.Success) wst1 ()
    s1 = BtStatus.Running && List.sum st1.Cursors = size + 1
    && s2 = BtStatus.Success && st2 = BehaviourTree.initial program
    && w1 = BtStatus.Running && List.head wst1.Cursors = size - 1
    && w2 = BtStatus.Success && wst2 = BehaviourTree.initial wide

let private doorCorridor () =
    let g =
        { Walkable = (fun t -> t.Plane = 0 && t.Y = 0 && t.X >= 0 && t.X < size)
          Edge = (fun _ _ _ -> Passage.Open)
          Transitions = (fun _ -> [])
          Diagonal = true
          OrthogonalCost = 1
          DiagonalCost = 1 }
    match (DoorGrid.findPath g true System.Int32.MaxValue { Plane = 0; X = size - 1; Y = 0 } { Plane = 0; X = 0; Y = 0 }).Outcome with
    | Found(path, cost) -> List.length path = size && cost = size - 1
    | _ -> false

let private kdDegenerate () =
    let line = KdTree.build [ for i in 0 .. size - 1 -> i % 32767 - 16383, 5, i ]
    let dup = KdTree.build [ for i in 0 .. size - 1 -> 3, 3, i ]
    (KdTree.nearest 0 5 line |> Option.map (fun p -> p.X)) = Some 0
    && (KdTree.kNearest 3 0 0 dup |> List.map (fun p -> p.Index)) = [ 0; 1; 2 ]

#if FABLE_COMPILER
/// Canary (Node only): a non-tail recursion over the same 10^5 depth must overflow Node's default stack,
/// which shows the checks above run where unbounded recursion would fail. Not run on .NET, where a stack
/// overflow cannot be caught.
let rec private naiveDepth (n: int) = if n = 0 then 0 else 1 + naiveDepth (n - 1)

let canary () =
    try
        naiveDepth size |> ignore
        "stack-safety: canary: a naive 10^5 recursion did NOT overflow (the stack is not Node's default)", false
    with _ ->
        "stack-safety: canary: a naive 10^5 recursion overflows Node's default stack, as expected", true
#endif

/// Runs every check; returns the printed lines and whether all passed.
let run () =
    let checks =
        [ "BinaryHeap 10^5 push/pop", heap
          "IndexedHeap 10^5 insert/decreaseKey/pop", indexedHeap
          "BestFirst.dijkstra 10^5-node chain", chain
          "BestFirst.distances 10^5-node chain", chainDistances
          "Scc.tarjan 10^5-node chain", sccChain
          "Scc.cycles 10^5-node ring", sccRing
          "UnionFind 10^5 chained unions", unionFindChain
          "Arbitration 10^4 queued entries drained", arbitrationQueue
          "TopoSort.sort and criticalPath 10^5-node chain", topoChain
          "BehaviourTree 10^5 levels deep and 10^5 wide", behaviourTreeDeep
          "BehaviourTree reactive 10^5 deep and wide, halting the running branch", behaviourTreeReactive
          "DoorGrid 10^5-tile corridor", doorCorridor
          "KdTree 10^5 collinear and duplicate points", kdDegenerate ]
    let results = checks |> List.map (fun (name, f) -> name, f ())
    let lines =
        results |> List.map (fun (name, ok) -> "stack-safety: " + name + ": " + (if ok then "ok" else "FAILED"))
#if FABLE_COMPILER
    let canaryLine, canaryOk = canary ()
    lines @ [ canaryLine ], canaryOk && (results |> List.forall snd)
#else
    lines, results |> List.forall snd
#endif
