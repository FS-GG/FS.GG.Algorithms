/// Independent oracles. Nothing here calls the code under test.
module FS.GG.Algorithms.Tests.Oracles

open System.Numerics

// ---------------------------------------------------------------- exact Q16.16 arithmetic (BigInteger)

let private bigMax = BigInteger System.Int32.MaxValue
let private bigMin = BigInteger System.Int32.MinValue
let private two16 = BigInteger 65536

let clamp (x: BigInteger) =
    if x > bigMax then System.Int32.MaxValue
    elif x < bigMin then System.Int32.MinValue
    else int x

/// floor(a / b) for b > 0.
let floorDiv (a: BigInteger) (b: BigInteger) =
    let q = BigInteger.Divide(a, b) // truncates toward zero
    if a.Sign < 0 && q * b <> a then q - BigInteger.One else q

let fixedAdd (a: int) (b: int) = clamp (BigInteger a + BigInteger b)
let fixedSub (a: int) (b: int) = clamp (BigInteger a - BigInteger b)

/// floor((a*b + 2^15) / 2^16): round to nearest, ties toward +infinity.
let fixedMul (a: int) (b: int) =
    clamp (floorDiv (BigInteger a * BigInteger b + BigInteger 32768) two16)

/// truncate(a * 2^16 / b), with the ADR's division-by-zero rules.
let fixedDiv (a: int) (b: int) =
    if b = 0 then
        (if a > 0 then System.Int32.MaxValue elif a < 0 then System.Int32.MinValue else 0)
    else
        clamp (BigInteger.Divide(BigInteger a * two16, BigInteger b))

/// floor(sqrt(a * 2^16)) by Newton's method on BigInteger; 0 for a <= 0.
let fixedSqrt (a: int) =
    if a <= 0 then
        0
    else
        let n = BigInteger a * two16
        let mutable x = n
        let mutable y = (x + BigInteger.One) / BigInteger 2
        while y < x do
            x <- y
            y <- (x + n / x) / BigInteger 2
        int x

// ---------------------------------------------------------------- graphs

/// A synthetic grid: open.[y].[x] is true for a walkable cell. 4-neighbourhood, unit cost.
type Grid =
    { Width: int
      Height: int
      Open: bool[][] }

let gridNeighbours (g: Grid) ((x, y): int * int) =
    [ x, y - 1; x + 1, y; x, y + 1; x - 1, y ]
    |> List.filter (fun (a, b) -> a >= 0 && b >= 0 && a < g.Width && b < g.Height && g.Open.[b].[a])

/// Breadth-first hop distance from start to goal, if reachable.
let bfs (g: Grid) (start: int * int) (goal: int * int) =
    let dist = System.Collections.Generic.Dictionary<int * int, int>()
    let queue = System.Collections.Generic.Queue<int * int>()
    dist.[start] <- 0
    queue.Enqueue start
    while queue.Count > 0 do
        let c = queue.Dequeue()
        for n in gridNeighbours g c do
            if not (dist.ContainsKey n) then
                dist.[n] <- dist.[c] + 1
                queue.Enqueue n
    match dist.TryGetValue goal with
    | true, d -> Some d
    | _ -> None

/// A synthetic weighted digraph on nodes 0..N-1 with non-negative integer edge costs.
type Digraph =
    { N: int
      Edges: (int * int * int) list } // from, to, cost

let digraphNeighbours (g: Digraph) (u: int) =
    g.Edges |> List.filter (fun (a, _, _) -> a = u) |> List.map (fun (_, b, c) -> b, c)

/// Bellman-Ford single-source distances (None = unreachable).
let bellmanFord (g: Digraph) (source: int) =
    let d: int option[] = Array.create g.N None
    d.[source] <- Some 0
    for _ in 1 .. max 1 (g.N - 1) do
        for (a, b, c) in g.Edges do
            match d.[a] with
            | Some da ->
                match d.[b] with
                | Some db when db <= da + c -> ()
                | _ -> d.[b] <- Some(da + c)
            | None -> ()
    d

/// The reversed digraph (for distances TO a node).
let reverse (g: Digraph) =
    { g with
        Edges = g.Edges |> List.map (fun (a, b, c) -> b, a, c) }

/// The least cost over every simple path from source to target, by exhaustive enumeration (small N only).
let exhaustive (g: Digraph) (source: int) (target: int) =
    let mutable best = None
    let rec walk (u: int) (visited: Set<int>) (cost: int) =
        if u = target then
            best <-
                match best with
                | Some b when b <= cost -> Some b
                | _ -> Some cost
        else
            for (v, c) in digraphNeighbours g u do
                if not (visited.Contains v) then walk v (visited.Add v) (cost + c)
    walk source (Set.singleton source) 0
    best

// ---------------------------------------------------------------- SCC and components (PR 2)

/// Recursive Tarjan over vertices 0..n-1, as the harvested source is written (test oracle only; recursion is
/// fine at test sizes). Same conventions as the library: roots in vertex order, successors in list order,
/// components in completion order, each in discovery order.
let recursiveTarjan (n: int) (succ: int -> int list) =
    let index = Array.create n -1
    let low = Array.create n 0
    let onStack = Array.create n false
    let mutable stack = []
    let mutable counter = 0
    let found = ResizeArray<int list>()
    let rec strongconnect v =
        index.[v] <- counter
        low.[v] <- counter
        counter <- counter + 1
        stack <- v :: stack
        onStack.[v] <- true
        for w in succ v do
            if index.[w] < 0 then
                strongconnect w
                low.[v] <- min low.[v] low.[w]
            elif onStack.[w] then
                low.[v] <- min low.[v] index.[w]
        if low.[v] = index.[v] then
            let mutable comp = []
            let mutable popping = true
            while popping do
                let w = List.head stack
                stack <- List.tail stack
                onStack.[w] <- false
                comp <- w :: comp
                if w = v then popping <- false
            found.Add comp
    for v in 0 .. n - 1 do
        if index.[v] < 0 then strongconnect v
    List.ofSeq found

/// reach.[u].[v]: v is reachable from u (reflexive), by BFS from every vertex.
let reachability (n: int) (succ: int -> int list) =
    Array.init n (fun u ->
        let seen = Array.create n false
        let queue = System.Collections.Generic.Queue<int>()
        seen.[u] <- true
        queue.Enqueue u
        while queue.Count > 0 do
            for w in succ (queue.Dequeue()) do
                if not seen.[w] then
                    seen.[w] <- true
                    queue.Enqueue w
        seen)

/// Connected components of an undirected graph by BFS: groups ordered by smallest member, members ascending.
let bfsComponents (n: int) (edges: (int * int) list) =
    let adj = Array.create n []
    for (a, b) in edges do
        adj.[a] <- b :: adj.[a]
        adj.[b] <- a :: adj.[b]
    let label = Array.create n -1
    for s in 0 .. n - 1 do
        if label.[s] < 0 then
            let queue = System.Collections.Generic.Queue<int>()
            label.[s] <- s
            queue.Enqueue s
            while queue.Count > 0 do
                for w in adj.[queue.Dequeue()] do
                    if label.[w] < 0 then
                        label.[w] <- s
                        queue.Enqueue w
    [ for s in 0 .. n - 1 do
          let members = [ for v in 0 .. n - 1 do if label.[v] = s then yield v ]
          if not members.IsEmpty then yield members ]
