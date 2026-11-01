/// The fixture operations of fixture-schema.v1.json. Each decodes its payload, runs the library and writes
/// the canonical output payload. The operation ids are frozen; a new operation takes a new id.
module FS.GG.Algorithms.Lockstep.Operations

open FS.GG.Algorithms.Collections
open FS.GG.Algorithms.Numerics
open FS.GG.Algorithms.Random
open FS.GG.Algorithms.Graph
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
