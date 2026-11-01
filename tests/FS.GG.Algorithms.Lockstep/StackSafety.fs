/// ADR-0098 §3 rule 7: every recursive or graph-walking surface has a test over a 10^5-node chain, run under
/// Node's default stack in the Fable build. Each check prints one line; any failure exits non-zero.
module FS.GG.Algorithms.Lockstep.StackSafety

open FS.GG.Algorithms.Collections
open FS.GG.Algorithms.Graph

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
          "BestFirst.distances 10^5-node chain", chainDistances ]
    let results = checks |> List.map (fun (name, f) -> name, f ())
    let lines =
        results |> List.map (fun (name, ok) -> "stack-safety: " + name + ": " + (if ok then "ok" else "FAILED"))
#if FABLE_COMPILER
    let canaryLine, canaryOk = canary ()
    lines @ [ canaryLine ], canaryOk && (results |> List.forall snd)
#else
    lines, results |> List.forall snd
#endif
