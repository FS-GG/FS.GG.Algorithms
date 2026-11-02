/// Bench workloads shared by the BenchmarkDotNet project (bench/dotnet) and the Fable/Node harness
/// (bench/fable): the same F# source, the same seeded inputs, the same work counts on both runtimes
/// (ADR-0098 §4.3). `prepare*` builds the inputs once; the returned closure is the timed kernel and returns
/// its deterministic work count. Never shipped.
module FS.GG.Algorithms.Bench.Workloads

open FS.GG.Algorithms.Collections
open FS.GG.Algorithms.Numerics
open FS.GG.Algorithms.Random
open FS.GG.Algorithms.Graph
open FS.GG.Algorithms.Decision
open FS.GG.Algorithms.Search.Grid
open FS.GG.Algorithms.Spatial

/// The fixed seed of every bench input.
let seed = 20261101

let private ints (n: int) (bound: int) =
    let mutable rng = Xoshiro128.seed seed
    Array.init n (fun _ ->
        let struct (v, r) = Xoshiro128.nextBelow bound rng
        rng <- r
        v)

/// A result sink, so no runtime can discard a kernel's output.
let mutable sink = 0

/// collections/binary-heap: push n seeded priorities (with duplicates), then pop them all. Work: 2n operations.
let prepareHeap (n: int) : unit -> int =
    let input = ints n (n / 4 + 1)
    fun () ->
        let h = BinaryHeap.create (fun (a: int) (b: int) -> compare a b)
        for x in input do
            BinaryHeap.push x h
        let mutable acc = 0
        for _ in 1..n do
            acc <- acc ^^^ BinaryHeap.pop h
        sink <- acc
        2 * n

/// numerics/fixed-q16-16: n multiplies, n divides and n square roots over seeded raw values. Work: 3n.
let prepareFixed (n: int) : unit -> int =
    let a = ints n 0x7FFFFFFF |> Array.map (fun x -> Fixed.ofRaw (x - 0x3FFFFFFF))
    let b = ints (n + 1) 0x00FFFFFF |> Array.skip 1 |> Array.map (fun x -> Fixed.ofRaw (x + 1))
    fun () ->
        let mutable acc = 0
        for i in 0 .. n - 1 do
            acc <- acc ^^^ (Fixed.mul a.[i] b.[i]).Raw ^^^ (Fixed.div a.[i] b.[i]).Raw ^^^ (Fixed.sqrt a.[i]).Raw
        sink <- acc
        3 * n

/// random/xoshiro128: n draws. Work: n.
let prepareXoshiro (n: int) : unit -> int =
    fun () ->
        let mutable rng = Xoshiro128.seed seed
        let mutable acc = 0
        for _ in 1..n do
            let struct (x, r) = Xoshiro128.next rng
            acc <- acc ^^^ x
            rng <- r
        sink <- acc
        n

/// A seeded side x side grid with about 25% walls; the corners are open. Cells are (x, y).
let private grid (side: int) =
    let cells = ints (side * side) 100 |> Array.map (fun r -> r >= 25)
    cells.[0] <- true
    cells.[side * side - 1] <- true
    let neighbours (x: int, y: int) =
        [ x, y - 1; x + 1, y; x, y + 1; x - 1, y ]
        |> List.filter (fun (a, b) -> a >= 0 && b >= 0 && a < side && b < side && cells.[b * side + a])
        |> List.map (fun c -> c, 1)
    neighbours

let private tieBreak (ax: int, ay: int) (bx: int, by: int) =
    if ay <> by then compare ay by else compare ax bx

/// graph/best-first-search, A* (Manhattan) corner to corner on a side x side grid. Work: nodes expanded.
let prepareAstar (side: int) : unit -> int =
    let neighbours = grid side
    let goal = side - 1, side - 1
    let h (x: int, y: int) = (side - 1 - x) + (side - 1 - y)
    fun () -> (BestFirst.astar neighbours h tieBreak goal (0, 0)).Expanded

/// graph/best-first-search, Dijkstra corner to corner on the same grid. Work: nodes expanded.
let prepareDijkstra (side: int) : unit -> int =
    let neighbours = grid side
    let goal = side - 1, side - 1
    fun () -> (BestFirst.dijkstra neighbours tieBreak (fun c -> c = goal) (0, 0)).Expanded

/// A seeded random digraph on 0..n-1 with out-degree 0..3 per vertex.
let private digraph (n: int) =
    let degrees = ints n 4
    let targets = ints (3 * n) n
    Array.init n (fun v -> List.init degrees.[v] (fun k -> targets.[3 * v + k]))

/// graph/iterative-tarjan-scc on a seeded random digraph of n vertices. Work: the number of components.
let prepareScc (n: int) : unit -> int =
    let succ = digraph n
    let nodes = [ 0 .. n - 1 ]
    fun () -> List.length (Scc.tarjan nodes (fun v -> succ.[v]))

/// graph/union-find: n seeded unions over n elements. Work: the number of unions that merged two sets.
let prepareUnionFind (n: int) : unit -> int =
    let pairs = ints (2 * n) n
    fun () ->
        let uf = UnionFind.create n
        let mutable merged = 0
        for i in 0 .. n - 1 do
            if UnionFind.union pairs.[2 * i] pairs.[2 * i + 1] uf then merged <- merged + 1
        merged

/// graph/topological-sort-cpm: Kahn plus the critical-path method on a seeded random DAG of n activities
/// (edges only to higher ids, durations 0..9). Work: the makespan.
let prepareTopoCpm (n: int) : unit -> int =
    let succ = digraph n |> Array.mapi (fun v l -> l |> List.filter (fun w -> w > v))
    let durations = ints n 10
    let nodes = [ 0 .. n - 1 ]
    fun () ->
        match TopoSort.criticalPath compare nodes (fun v -> succ.[v]) (fun v -> durations.[v]) with
        | Ok s -> s.Makespan
        | Error _ -> -1

/// decision/behaviour-tree: n ticks of a fixed 31-node tree (selectors over sequences, a parallel, repeat and
/// retry) whose leaves read a seeded table by their call count. Work: the leaves ticked.
let prepareBehaviourTree (n: int) : unit -> int =
    let table = ints 4096 3
    let leafTree k = BehaviourTree.Leaf k
    let branch k =
        BehaviourTree.Sequence
            [ leafTree k; BehaviourTree.Retry(2, leafTree (k + 1)); BehaviourTree.Invert(leafTree (k + 2)) ]
    let tree =
        BehaviourTree.Selector
            [ branch 0
              BehaviourTree.Parallel(2, 2, [ branch 3; branch 6; BehaviourTree.Repeat(2, leafTree 9) ])
              branch 10
              BehaviourTree.Sequence [ branch 13; branch 16 ] ]
    let program = BehaviourTree.compile tree
    let leaf (_: int) (count: int) =
        let s =
            match table.[count &&& 4095] with
            | 0 -> BtStatus.Success
            | 1 -> BtStatus.Failure
            | _ -> BtStatus.Running
        s, count + 1
    fun () ->
        let mutable state = BehaviourTree.initial program
        let mutable count = 0
        for _ in 1..n do
            let _, next, c = BehaviourTree.tick program leaf state count
            state <- next
            count <- c
        count

/// spatial/kd-tree: 1000 nearest-neighbour queries against n seeded points (built once). Work: the sum of the
/// returned points' indices modulo 2^31 (deterministic).
let prepareKdNearest (n: int) : unit -> int =
    let coords = ints (2 * n + 2000) 32767
    let tree = KdTree.build [ for i in 0 .. n - 1 -> coords.[2 * i] - 16383, coords.[2 * i + 1] - 16383, i ]
    fun () ->
        let mutable acc = 0
        for q in 0..999 do
            match KdTree.nearest (coords.[2 * n + 2 * q] - 16383) (coords.[2 * n + 2 * q + 1] - 16383 + 0) tree with
            | Some p -> acc <- (acc + p.Index) &&& 0x7FFFFFFF
            | None -> ()
        acc

/// search.grid/doors-and-planes: corner to corner on a seeded side x side, two-plane grid with walls, doors
/// and stairs, 8-way. Work: nodes expanded.
let prepareDoorGrid (side: int) : unit -> int =
    let cells = ints (2 * side * side) 100
    let edgeKinds = ints (4 * side * side) 100
    let index (t: Tile) = (t.Plane * side + t.Y) * side + t.X
    let grid =
        { Walkable =
            (fun t ->
                t.Plane >= 0 && t.Plane < 2 && t.X >= 0 && t.Y >= 0 && t.X < side && t.Y < side
                && (cells.[index t] >= 15 || (t.X = 0 && t.Y = 0) || (t.X = side - 1 && t.Y = side - 1)))
          Edge =
            (fun t dx dy ->
                // One record per undirected edge: the lower or left tile owns it.
                let o = if dx < 0 || dy < 0 then { t with X = t.X + dx; Y = t.Y + dy } else t
                let k = edgeKinds.[2 * index o + (if dx <> 0 then 0 else 1)]
                if k < 5 then Passage.Blocked elif k < 12 then Passage.Door(k - 5) else Passage.Open)
          Transitions =
            (fun t -> if t.X % 16 = 0 && t.Y % 16 = 0 then [ { t with Plane = 1 - t.Plane }, 20 ] else [])
          Diagonal = true
          OrthogonalCost = 10
          DiagonalCost = 14 }
    fun () ->
        (DoorGrid.findPath grid true System.Int32.MaxValue { Plane = 1; X = side - 1; Y = side - 1 } { Plane = 0; X = 0; Y = 0 }).Expanded

/// The nearest-rank sample of ascending timings, for the Node harness's summaries (harvest 20: the library's
/// own percentile, exact integer rank).
let nearestRank (percent: int) (sortedSamples: float[]) = Percentile.ofSorted percent 100 sortedSamples

/// The declared kernels: id, sizes (three each, ADR-0098 §4.3), and the preparation.
let kernels: (string * int list * (int -> unit -> int)) list =
    [ "collections/binary-heap/push-pop", [ 1000; 10000; 100000 ], prepareHeap
      "numerics/fixed-q16-16/mul-div-sqrt", [ 1000; 10000; 100000 ], prepareFixed
      "random/xoshiro128/next", [ 1000; 10000; 100000 ], prepareXoshiro
      "graph/best-first-search/astar-grid", [ 32; 128; 512 ], prepareAstar
      "graph/best-first-search/dijkstra-grid", [ 32; 128; 512 ], prepareDijkstra
      "graph/iterative-tarjan-scc/random-digraph", [ 1000; 10000; 100000 ], prepareScc
      "graph/union-find/random-unions", [ 1000; 10000; 100000 ], prepareUnionFind
      "graph/topological-sort-cpm/random-dag", [ 1000; 10000; 100000 ], prepareTopoCpm
      "decision/behaviour-tree/ticks", [ 1000; 10000; 100000 ], prepareBehaviourTree
      "spatial/kd-tree/nearest-1000-queries", [ 1000; 10000; 100000 ], prepareKdNearest
      "search.grid/doors-and-planes/corner-to-corner", [ 32; 128; 512 ], prepareDoorGrid ]
