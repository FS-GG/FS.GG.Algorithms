/// The fixture operations of fixture-schema.v1.json. Each decodes its payload, runs the library and writes
/// the canonical output payload. The operation ids are frozen; a new operation takes a new id.
module FS.GG.Algorithms.Lockstep.Operations

open FS.GG.Algorithms.Collections
open FS.GG.Algorithms.Numerics
open FS.GG.Algorithms.Random
open FS.GG.Algorithms.Graph
open FS.GG.Algorithms.Decision
open FS.GG.Algorithms.Search.Grid
open FS.GG.Algorithms.Spatial
open FS.GG.Algorithms.Lockstep.Canonical

/// Outcome tag 0: completed normally. Outcome tag 1: the operation refused its input (an exception).
let completed = 0
let refused = 1

/// Operation 1: a BinaryHeap script over (priority, value) ordered by priority only, so equal priorities
/// exercise the frozen FIFO pop order.
let binaryHeap (r: Reader) (w: Writer) =
    let h = BinaryHeap.create (fun (a: int * int) (b: int * int) -> compare (fst a) (fst b))
    let n = r.U32()
    let out = Writer()
    let mutable observations = 0
    for _ in 1..n do
        match r.U8() with
        | 0 ->
            let p = r.I32()
            let v = r.I32()
            BinaryHeap.push (p, v) h
        | tag ->
            let x = if tag = 1 then BinaryHeap.tryPop h else BinaryHeap.tryPeek h
            observations <- observations + 1
            match x with
            | Some(p, v) ->
                out.U8 1
                out.I32 p
                out.I32 v
            | None -> out.U8 0
    w.U32 observations
    w.Append out
    w.U32(BinaryHeap.count h)

/// Operation 2: an IndexedHeap script. One observation per op: u8 status (0 done, 1 refused) then data.
let indexedHeap (r: Reader) (w: Writer) =
    let h = IndexedHeap.create (fun (a: int) (b: int) -> compare a b)
    let n = r.U32()
    w.U32 n
    for _ in 1..n do
        let tag = r.U8()
        let attempt (f: unit -> unit) =
            try
                f ()
                w.U8 0
            with _ ->
                w.U8 1
        match tag with
        | 0 ->
            let k = r.I32()
            let p = r.I32()
            attempt (fun () -> IndexedHeap.insert k p h)
        | 1 ->
            let k = r.I32()
            let p = r.I32()
            attempt (fun () -> IndexedHeap.decreaseKey k p h)
        | 2 ->
            let k = r.I32()
            let p = r.I32()
            attempt (fun () -> IndexedHeap.update k p h)
        | 3 ->
            let k = r.I32()
            w.U8 0
            w.Bool(IndexedHeap.remove k h)
        | 4
        | 5 ->
            w.U8 0
            match (if tag = 4 then IndexedHeap.tryPop h else IndexedHeap.tryPeek h) with
            | Some(k, p) ->
                w.U8 1
                w.I32 k
                w.I32 p
            | None -> w.U8 0
        | 6 ->
            let k = r.I32()
            w.U8 0
            w.Bool(IndexedHeap.contains k h)
        | _ ->
            w.U8 0
            w.U32(IndexedHeap.count h)

/// Operation 3: Q16.16 vectors. Each vector is (u8 function, i32 a, i32 b) and yields one i32.
let fixedVectors (r: Reader) (w: Writer) =
    let n = r.U32()
    w.U32 n
    for _ in 1..n do
        let fn = r.U8()
        let a = Fixed.ofRaw (r.I32())
        let b = Fixed.ofRaw (r.I32())
        let result =
            match fn with
            | 0 -> (Fixed.add a b).Raw
            | 1 -> (Fixed.sub a b).Raw
            | 2 -> (Fixed.mul a b).Raw
            | 3 -> (Fixed.div a b).Raw
            | 4 -> (Fixed.sqrt a).Raw
            | 5 -> (Fixed.neg a).Raw
            | 6 -> (Fixed.abs a).Raw
            | 7 -> (Fixed.ofRatio a.Raw b.Raw).Raw
            | 8 -> (Fixed.ofInt a.Raw).Raw
            | 9 -> Fixed.toIntFloor a
            | 10 -> Fixed.toIntCeiling a
            | 11 -> Fixed.toIntRound a
            | 12 -> Fixed.compare a b
            | 13 -> (a * b).Raw
            | 14 -> (a / b).Raw
            | _ -> failwithf "fixed: unknown function %d" fn
        w.I32 result

/// Operation 4: best-first search on a synthetic grid. Cell byte 0 is a wall; otherwise the cost of
/// entering the cell. Mode 0 Dijkstra, 1 A* (Manhattan or Chebyshev times the least cell cost), 2 distances.
let bestFirst (r: Reader) (w: Writer) =
    let width = r.U16()
    let height = r.U16()
    let cells = r.Bytes(width * height)
    let sx = r.I32()
    let sy = r.I32()
    let gx = r.I32()
    let gy = r.I32()
    let mode = r.U8()
    let eight = r.U8() = 8
    let budget = r.I32()
    let cost (x, y) = cells.[y * width + x]
    let minCost = cells |> Array.filter (fun c -> c > 0) |> Array.fold min 255
    let steps =
        if eight then
            [ 0, -1; 1, -1; 1, 0; 1, 1; 0, 1; -1, 1; -1, 0; -1, -1 ]
        else
            [ 0, -1; 1, 0; 0, 1; -1, 0 ]
    let neighbours (x: int, y: int) =
        steps
        |> List.choose (fun (dx, dy) ->
            let nx = x + dx
            let ny = y + dy
            if nx >= 0 && ny >= 0 && nx < width && ny < height && cost (nx, ny) > 0 then
                Some((nx, ny), cost (nx, ny))
            else
                None)
    let heuristic (x: int, y: int) =
        let dx = abs (x - gx)
        let dy = abs (y - gy)
        (if eight then max dx dy else dx + dy) * minCost
    // Row-major total order: y, then x.
    let tieBreak (ax: int, ay: int) (bx: int, by: int) =
        if ay <> by then compare ay by else compare ax bx
    let writeCell (x: int, y: int) =
        w.I32 x
        w.I32 y
    if mode = 2 then
        let ds = BestFirst.distances neighbours tieBreak (sx, sy)
        w.U32(List.length ds)
        for (c, d) in ds do
            writeCell c
            w.I32 d
    else
        let problem =
            { Start = (sx, sy)
              IsGoal = (fun c -> c = (gx, gy))
              Neighbours = neighbours
              Heuristic = (if mode = 1 then heuristic else fun _ -> 0)
              TieBreak = tieBreak
              MaxExpanded = budget }
        let result = BestFirst.search problem
        match result.Outcome with
        | Found(path, c) ->
            w.U8 0
            w.I32 c
            w.U32(List.length path)
            List.iter writeCell path
        | Unreachable -> w.U8 1
        | BudgetExhausted -> w.U8 2
        w.U32 result.Expanded

let private writeDraws (w: Writer) (n: int) (rng: Xoshiro128) =
    let mutable s = rng
    for _ in 1..n do
        let struct (x, s') = Xoshiro128.next s
        w.I32 x
        s <- s'
    s

let private writeState (w: Writer) (rng: Xoshiro128) =
    let (a, b, c, d) = Xoshiro128.state rng
    w.I32 a
    w.I32 b
    w.I32 c
    w.I32 d

/// Operation 5: xoshiro128** vectors.
let xoshiro (r: Reader) (w: Writer) =
    match r.U8() with
    | 0 ->
        // seed: state words, then n outputs
        let s = Xoshiro128.seed (r.I32())
        let n = r.U32()
        writeState w s
        writeDraws w n s |> ignore
    | 1 ->
        // ofState: n outputs (refused for the all-zero state)
        let s0 = r.I32()
        let s1 = r.I32()
        let s2 = r.I32()
        let s3 = r.I32()
        let n = r.U32()
        writeDraws w n (Xoshiro128.ofState s0 s1 s2 s3) |> ignore
    | 2 ->
        // split: parent continuation state and n outputs, child state and n outputs
        let s = Xoshiro128.seed (r.I32())
        let n = r.U32()
        let struct (parent, child) = Xoshiro128.split s
        writeState w parent
        writeDraws w n parent |> ignore
        writeState w child
        writeDraws w n child |> ignore
    | 3 ->
        // nextBelow: n values for one bound
        let mutable s = Xoshiro128.seed (r.I32())
        let bound = r.I32()
        let n = r.U32()
        for _ in 1..n do
            let struct (v, s') = Xoshiro128.nextBelow bound s
            w.I32 v
            s <- s'
        writeState w s
    | 4 ->
        // jump: the jumped state and n outputs
        let s = Xoshiro128.jump (Xoshiro128.seed (r.I32()))
        let n = r.U32()
        writeState w s
        writeDraws w n s |> ignore
    | 5 ->
        // nextInRange: n values
        let mutable s = Xoshiro128.seed (r.I32())
        let lo = r.I32()
        let hi = r.I32()
        let n = r.U32()
        for _ in 1..n do
            let struct (v, s') = Xoshiro128.nextInRange lo hi s
            w.I32 v
            s <- s'
    | sub -> failwithf "xoshiro: unknown sub-operation %d" sub

/// The low 32 bits of a product from 16-bit halves (exact on both runtimes; the library's own is internal).
let private imul (a: int) (b: int) =
    let a0 = a &&& 0xFFFF
    let b0 = b &&& 0xFFFF
    let a1 = (a >>> 16) &&& 0xFFFF
    let b1 = (b >>> 16) &&& 0xFFFF
    (a0 * b0) + (((a1 * b0 + a0 * b1) &&& 0xFFFF) <<< 16)

/// Operation 10: a fixed-seed Q16.16 sweep. Input: i32 seed, u32 pairs. Each pair is a = x >>> (y & 15),
/// b = z >>> (t & 15) from four xoshiro128** draws. Output: u32 pairs, then one FNV-1a-style digest
/// (h = imul(h xor r, 16777619) from 0x811C9DC5) per function: add, sub, mul, div, ofRatio, sqrt, neg,
/// toIntRound, compare.
let fixedSweep (r: Reader) (w: Writer) =
    let mutable rng = Xoshiro128.seed (r.I32())
    let pairs = r.U32()
    let draw () =
        let struct (x, s) = Xoshiro128.next rng
        rng <- s
        x
    let h = Array.create 9 0x811C9DC5
    let mix k (v: int) = h.[k] <- imul (h.[k] ^^^ v) 16777619
    for _ in 1..pairs do
        let a0 = draw ()
        let a = a0 >>> (draw () &&& 15)
        let b0 = draw ()
        let b = b0 >>> (draw () &&& 15)
        let fa = Fixed.ofRaw a
        let fb = Fixed.ofRaw b
        mix 0 (Fixed.add fa fb).Raw
        mix 1 (Fixed.sub fa fb).Raw
        mix 2 (Fixed.mul fa fb).Raw
        mix 3 (Fixed.div fa fb).Raw
        mix 4 (Fixed.ofRatio a b).Raw
        mix 5 (Fixed.sqrt fa).Raw
        mix 6 (Fixed.neg fa).Raw
        mix 7 (Fixed.toIntRound fa)
        mix 8 (Fixed.compare fa fb)
    w.U32 pairs
    Array.iter w.I32 h
/// Operation 6: Tarjan SCC over vertices given as an explicit list (duplicates allowed) with successor lists.
/// Output: the components (in order, members in order), then the cycles, then componentOf in node-list order.
let scc (r: Reader) (w: Writer) =
    let nodeCount = r.U32()
    let nodes = List.init nodeCount (fun _ -> r.I32())
    let vertexCount = r.U32()
    let table = Array.init vertexCount (fun _ -> List.init (r.U32()) (fun _ -> r.I32()))
    let succ (v: int) = if v >= 0 && v < table.Length then table.[v] else []
    let writeLists (ls: int list list) =
        w.U32(List.length ls)
        for l in ls do
            w.U32(List.length l)
            List.iter w.I32 l
    writeLists (Scc.tarjan nodes succ)
    writeLists (Scc.cycles nodes succ)
    let ids = Scc.componentOf nodes succ
    w.U32(List.length ids)
    List.iter w.I32 ids

/// Operation 7: union-find. Output: per union the merged flag, then representatives 0..n-1, count, and the sets.
let unionFind (r: Reader) (w: Writer) =
    let n = r.U32()
    let uf = UnionFind.create n
    let m = r.U32()
    w.U32 m
    for _ in 1..m do
        let a = r.I32()
        let b = r.I32()
        w.Bool(UnionFind.union a b uf)
    for i in 0 .. n - 1 do
        w.I32(UnionFind.representative i uf)
    w.U32(UnionFind.count uf)
    let sets = UnionFind.sets uf
    w.U32(List.length sets)
    for set in sets do
        w.U32(List.length set)
        List.iter w.I32 set

/// Operation 8: nearest-rank percentiles. Input: u32 count of (i32 numerator, i32 denominator, i32 count)
/// triples, then a sample list and u32 count of percents. Output: the ranks, then the percent values.
let percentile (r: Reader) (w: Writer) =
    let k = r.U32()
    w.U32 k
    for _ in 1..k do
        let num = r.I32()
        let den = r.I32()
        let n = r.I32()
        w.I32(Percentile.rank num den n)
    let samples = List.init (r.U32()) (fun _ -> r.I32())
    let ps = r.U32()
    w.U32 ps
    for _ in 1..ps do
        w.I32(Percentile.percent (r.I32()) samples)

/// The fixture policy for operation 9: owners 0..5, two per band; band 3 newest-first, bands 2 and 1
/// earliest-first; at most 16 waiting in band 3 and 6 in the others.
let arbitrationPolicy: ArbitrationPolicy<int> =
    { BandOf = (fun owner -> 3 - owner / 2)
      OrderOf = (fun band -> if band = 3 then BandOrder.NewestFirst else BandOrder.EarliestFirst)
      MaxWaiting = (fun band -> if band = 3 then Some 16 else Some 6) }

/// Operation 9: priority arbitration. Input: u32 count of inputs; u8 tag 0 submit (i32 owner, i32 key, u8 replace)
/// | 1 cancel (i32 by, i32 key) | 2 cancelAll (i32 by) | 3 release (i32 owner) | 4 finished (i32 key).
/// Output: per input u32 decision count and decisions (u8 tag, fields), then the final live entries.
let arbitration (r: Reader) (w: Writer) =
    let reasonCode (reason: EndReason<int>) =
        match reason with
        | EndReason.Finished -> 0, -1
        | EndReason.Cancelled by -> 1, by
        | EndReason.Replaced -> 2, -1
        | EndReason.Released -> 3, -1
        | EndReason.CancelledAll by -> 4, by
    let mutable state = Arbitration.empty<int, int, unit>
    let n = r.U32()
    w.U32 n
    for _ in 1..n do
        let input =
            match r.U8() with
            | 0 ->
                let owner = r.I32()
                let key = r.I32()
                let mode = if r.U8() = 1 then SubmitMode.Replace else SubmitMode.Queue
                ArbitrationInput.Submit(owner, key, (), mode)
            | 1 ->
                let by = r.I32()
                ArbitrationInput.Cancel(by, r.I32())
            | 2 -> ArbitrationInput.CancelAll(r.I32())
            | 3 -> ArbitrationInput.Release(r.I32())
            | _ -> ArbitrationInput.Finished(r.I32())
        let next, decisions = Arbitration.step arbitrationPolicy state input
        state <- next
        w.U32(List.length decisions)
        for d in decisions do
            match d with
            | ArbitrationDecision.Accepted k ->
                w.U8 0
                w.I32 k
            | ArbitrationDecision.Refused refusal ->
                w.U8 1
                match refusal with
                | ArbitrationRefusal.DuplicateKey k ->
                    w.U8 0
                    w.I32 k
                | ArbitrationRefusal.UnknownKey k ->
                    w.U8 1
                    w.I32 k
                | ArbitrationRefusal.NotPermitted k ->
                    w.U8 2
                    w.I32 k
                | ArbitrationRefusal.QueueFull ->
                    w.U8 3
                    w.I32 -1
            | ArbitrationDecision.Activated(k, resumed) ->
                w.U8 2
                w.I32 k
                w.Bool resumed
            | ArbitrationDecision.Preempted(k, by) ->
                w.U8 3
                w.I32 k
                w.I32 by
            | ArbitrationDecision.Ended(k, reason) ->
                let code, by = reasonCode reason
                w.U8 4
                w.I32 k
                w.U8 code
                w.I32 by
            | ArbitrationDecision.CancelledAll(by, count) ->
                w.U8 5
                w.I32 by
                w.U32 count
    w.U32(List.length state.Entries)
    for e in state.Entries do
        w.I32 e.Key
        w.I32 e.Owner
        w.I32 e.Band
        w.I32 e.Seq
        w.U8(
            match e.Status with
            | EntryStatus.Pending -> 0
            | EntryStatus.Active -> 1
            | EntryStatus.Suspended -> 2
        )
        w.Bool e.Started

/// Operation 11: TopoSort.sort and TopoSort.criticalPath with `compare` on int nodes. Input: list<i32> nodes,
/// u32 vertexCount and per vertex list<i32> successors, then u32 count and i32 durations by vertex.
/// Output: u8 sorted (1 ok: list<i32> order | 0 cycles: list<list<i32>>); when sorted, i32 makespan,
/// list<i32> critical path, and per activity in order i32 node, duration, es, ef, ls, lf, slack.
let topoSort (r: Reader) (w: Writer) =
    let nodes = List.init (r.U32()) (fun _ -> r.I32())
    let table = Array.init (r.U32()) (fun _ -> List.init (r.U32()) (fun _ -> r.I32()))
    let durations = Array.init (r.U32()) (fun _ -> r.I32())
    let succ (v: int) = if v >= 0 && v < table.Length then table.[v] else []
    let dur (v: int) = if v >= 0 && v < durations.Length then durations.[v] else 0
    let writeList (l: int list) =
        w.U32(List.length l)
        List.iter w.I32 l
    match TopoSort.sort compare nodes succ with
    | Error cycles ->
        w.U8 0
        w.U32(List.length cycles)
        List.iter writeList cycles
    | Ok order ->
        w.U8 1
        writeList order
        match TopoSort.criticalPath compare nodes succ dur with
        | Error _ -> failwith "criticalPath disagrees with sort"
        | Ok s ->
            w.I32 s.Makespan
            writeList s.CriticalPath
            for a in s.Activities do
                w.I32 a.Node
                w.I32 a.Duration
                w.I32 a.EarliestStart
                w.I32 a.EarliestFinish
                w.I32 a.LatestStart
                w.I32 a.LatestFinish
                w.I32 a.Slack

/// Decodes a behaviour tree in preorder: u8 kind (0 leaf i32 id | 1 sequence | 2 selector: u32 n children |
/// 3 parallel: i32 successAt, i32 failureAt, u32 n children | 4 invert, 5 forceSuccess, 6 forceFailure: child |
/// 7 repeat, 8 retry: i32 count, child | 9 reactive sequence, 10 reactive fallback: u32 n children). Fixture
/// trees are shallow, so the decoder may recurse.
let rec private decodeTree (r: Reader) : BehaviourTree<int> =
    match r.U8() with
    | 0 -> BehaviourTree.Leaf(r.I32())
    | 1 -> BehaviourTree.Sequence(List.init (r.U32()) (fun _ -> decodeTree r))
    | 2 -> BehaviourTree.Selector(List.init (r.U32()) (fun _ -> decodeTree r))
    | 3 ->
        let sa = r.I32()
        let fa = r.I32()
        BehaviourTree.Parallel(sa, fa, List.init (r.U32()) (fun _ -> decodeTree r))
    | 4 -> BehaviourTree.Invert(decodeTree r)
    | 5 -> BehaviourTree.ForceSuccess(decodeTree r)
    | 6 -> BehaviourTree.ForceFailure(decodeTree r)
    | 7 ->
        let n = r.I32()
        BehaviourTree.Repeat(n, decodeTree r)
    | 8 ->
        let n = r.I32()
        BehaviourTree.Retry(n, decodeTree r)
    | 9 -> BehaviourTree.ReactiveSequence(List.init (r.U32()) (fun _ -> decodeTree r))
    | 10 -> BehaviourTree.ReactiveFallback(List.init (r.U32()) (fun _ -> decodeTree r))
    | k -> failwithf "unknown behaviour-tree kind %d" k

/// Operation 12: BehaviourTree ticks. Input: the tree, u32 leafCount and per leaf id a list<u8> status script
/// (0 success, 1 failure, 2 running), cycled by that leaf's own call count; u32 ticks. The blackboard is the
/// immutable per-leaf call counts. Output: u32 ticks, then per tick u8 status, list<i32> trace (preorder),
/// leafCount i32 call counts, list<i32> cursors and list<i32> finished.
let behaviourTree (r: Reader) (w: Writer) =
    let program = BehaviourTree.compile (decodeTree r)
    let leafCount = r.U32()
    let scripts = Array.init leafCount (fun _ -> Array.init (r.U32()) (fun _ -> r.U8()))
    let ticks = r.U32()
    let code (s: BtStatus) =
        match s with
        | BtStatus.Success -> 0
        | BtStatus.Failure -> 1
        | BtStatus.Running -> 2
    let leaf (l: int) (calls: Map<int, int>) =
        let n = calls.TryFind l |> Option.defaultValue 0
        let script = scripts.[l]
        let s =
            match script.[n % script.Length] with
            | 0 -> BtStatus.Success
            | 1 -> BtStatus.Failure
            | _ -> BtStatus.Running
        s, calls.Add(l, n + 1)
    let writeList (l: int list) =
        w.U32(List.length l)
        List.iter w.I32 l
    let mutable state = BehaviourTree.initial program
    let mutable calls = Map.empty
    w.U32 ticks
    for _ in 1..ticks do
        let s, next, bb, trace = BehaviourTree.tickTraced program leaf state calls
        state <- next
        calls <- bb
        w.U8(code s)
        writeList trace
        for l in 0 .. leafCount - 1 do
            w.I32(calls.TryFind l |> Option.defaultValue 0)
        writeList state.Cursors
        writeList state.Finished

/// Operation 13: DoorGrid. Input: u8 planes, u8 side, planes*side*side u8 walkable; u32 edges, each i32 plane,
/// x, y, u8 direction (0 east, 1 south), u8 kind (0 open, 1 blocked, 2 door), i32 extra; u32 stairs, each
/// i32 p, x, y, q, x2, y2, cost; u8 diagonal; i32 orthogonalCost, diagonalCost; i32 start p, x, y; i32 goal p,
/// x, y; u8 heuristic; i32 maxExpanded. Output: per walkable tile in (plane, y, x) order the neighbour list
/// (i32 p, x, y, cost); then u8 outcome (0 found: i32 cost, list<i32 p, x, y> | 1 unreachable | 2 budget),
/// u32 expanded.
let doorGrid (r: Reader) (w: Writer) =
    let planes = r.U8()
    let side = r.U8()
    let walk = r.Bytes(planes * side * side)
    let edges = System.Collections.Generic.Dictionary<struct (int * int * int * int * int * int), Passage>()
    // Bind the count first: Fable 5.18.0 re-evaluates a side-effecting `for` upper bound on every iteration,
    // where F# on .NET evaluates it once (found by this proof: `for _ in 1 .. r.U32()` read past the end).
    let edgeCount = r.U32()
    for _ in 1..edgeCount do
        let p = r.I32()
        let x = r.I32()
        let y = r.I32()
        let dir = r.U8()
        let kind = r.U8()
        let extra = r.I32()
        let passage = if kind = 0 then Passage.Open elif kind = 1 then Passage.Blocked else Passage.Door extra
        let x2, y2 = if dir = 0 then x + 1, y else x, y + 1
        edges.[struct (p, x, y, p, x2, y2)] <- passage
        edges.[struct (p, x2, y2, p, x, y)] <- passage
    let stairs = List.init (r.U32()) (fun _ -> { Plane = r.I32(); X = r.I32(); Y = r.I32() }, { Plane = r.I32(); X = r.I32(); Y = r.I32() }, r.I32())
    let diagonal = r.U8() = 1
    let ortho = r.I32()
    let diag = r.I32()
    let start = { Plane = r.I32(); X = r.I32(); Y = r.I32() }
    let goal = { Plane = r.I32(); X = r.I32(); Y = r.I32() }
    let heuristic = r.U8() = 1
    let budget = r.I32()
    let walkable (t: Tile) =
        t.Plane >= 0 && t.Plane < planes && t.X >= 0 && t.Y >= 0 && t.X < side && t.Y < side
        && walk.[(t.Plane * side + t.Y) * side + t.X] = 1
    let grid =
        { Walkable = walkable
          Edge =
            (fun t dx dy ->
                match edges.TryGetValue(struct (t.Plane, t.X, t.Y, t.Plane, t.X + dx, t.Y + dy)) with
                | true, p -> p
                | _ -> Passage.Open)
          Transitions = (fun t -> stairs |> List.filter (fun (a, _, _) -> a = t) |> List.map (fun (_, b, c) -> b, c))
          Diagonal = diagonal
          OrthogonalCost = ortho
          DiagonalCost = diag }
    let writeTile (t: Tile) =
        w.I32 t.Plane
        w.I32 t.X
        w.I32 t.Y
    for p in 0 .. planes - 1 do
        for y in 0 .. side - 1 do
            for x in 0 .. side - 1 do
                let t = { Plane = p; X = x; Y = y }
                if walkable t then
                    let ns = DoorGrid.neighbours grid t
                    w.U32(List.length ns)
                    for (n, c) in ns do
                        writeTile n
                        w.I32 c
    let result = DoorGrid.findPath grid heuristic budget goal start
    match result.Outcome with
    | Found(path, cost) ->
        w.U8 0
        w.I32 cost
        w.U32(List.length path)
        List.iter writeTile path
    | Unreachable -> w.U8 1
    | BudgetExhausted -> w.U8 2
    w.U32 result.Expanded

/// Operation 14: KdTree. Input: list of (i32 x, i32 y) points (the value is the index), then u32 queries,
/// each u8 kind (0 nearest | 1 kNearest | 2 withinRadius | 3 inRectangle), i32 a, b, c, d (kind 0: x, y;
/// 1: k, x, y; 2: radius, x, y; 3: minX, minY, maxX, maxY). Output: per query list<i32 x, y, index>.
let kdTree (r: Reader) (w: Writer) =
    let points = List.init (r.U32()) (fun i -> let x = r.I32() in let y = r.I32() in x, y, i)
    let tree = KdTree.build points
    let n = r.U32()
    w.U32 n
    for _ in 1..n do
        let kind = r.U8()
        let a = r.I32()
        let b = r.I32()
        let c = r.I32()
        let d = r.I32()
        let result =
            match kind with
            | 0 -> KdTree.nearest a b tree |> Option.toList
            | 1 -> KdTree.kNearest a b c tree
            | 2 -> KdTree.withinRadius a b c tree
            | _ -> KdTree.inRectangle a b c d tree
        w.U32(List.length result)
        for p in result do
            w.I32 p.X
            w.I32 p.Y
            w.I32 p.Index

/// Runs one case and returns its canonical record:
/// u32 recordByteCount (bytes after this field), u32 caseId, u16 operationId, u16 outcomeTag, payload.
let runCase (caseId: int) (operation: int) (payload: int[]) =
    let body = Writer()
    let outcome =
        try
            let r = Reader payload
            match operation with
            | 1 -> binaryHeap r body
            | 2 -> indexedHeap r body
            | 3 -> fixedVectors r body
            | 4 -> bestFirst r body
            | 5 -> xoshiro r body
            | 10 -> fixedSweep r body
            | 11 -> topoSort r body
            | 12 -> behaviourTree r body
            | 13 -> doorGrid r body
            | 14 -> kdTree r body
            | 6 -> scc r body
            | 7 -> unionFind r body
            | 8 -> percentile r body
            | 9 -> arbitration r body
            | _ -> failwithf "unknown operation %d" operation
            completed
        with _ ->
            refused
    let record = Writer()
    let payloadBytes = if outcome = completed then body else Writer()
    record.U32(8 + payloadBytes.Count)
    record.U32 caseId
    record.U16 operation
    record.U16 outcome
    record.Append payloadBytes
    record
