/// Bench workloads shared by the BenchmarkDotNet project (bench/dotnet) and the Fable/Node harness
/// (bench/fable): the same F# source, the same seeded inputs, the same work counts on both runtimes
/// (ADR-0098 §4.3). `prepare*` builds the inputs once; the returned closure is the timed kernel and returns
/// its deterministic work count. Never shipped.
module FS.GG.Algorithms.Bench.Workloads

open FS.GG.Algorithms.Collections
open FS.GG.Algorithms.Numerics
open FS.GG.Algorithms.Random
open FS.GG.Algorithms.Graph

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

/// The declared kernels: id, sizes (three each, ADR-0098 §4.3), and the preparation.
let kernels: (string * int list * (int -> unit -> int)) list =
    [ "collections/binary-heap/push-pop", [ 1000; 10000; 100000 ], prepareHeap
      "numerics/fixed-q16-16/mul-div-sqrt", [ 1000; 10000; 100000 ], prepareFixed
      "random/xoshiro128/next", [ 1000; 10000; 100000 ], prepareXoshiro
      "graph/best-first-search/astar-grid", [ 32; 128; 512 ], prepareAstar
      "graph/best-first-search/dijkstra-grid", [ 32; 128; 512 ], prepareDijkstra ]
