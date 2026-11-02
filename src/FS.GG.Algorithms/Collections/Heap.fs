namespace FS.GG.Algorithms.Collections

// Layer 0 (ADR-0098 §3 rule 6): uses nothing else in the library. Every loop is iterative.

/// Renumbers live stamps 0..n-1 in their existing order, so relative order (and so every pop) is unchanged.
/// Returns the next free stamp. Raises when the heap itself holds `limit` entries (2^31 - 1 in practice).
module private Stamps =
    let renumber (stamps: ResizeArray<int>) (limit: int) (onMove: int -> int -> unit) =
        let n = stamps.Count
        if n >= limit then invalidOp "heap: the number of live entries reached the insertion-stamp limit"
        let order = Array.init n id
        Array.sortInPlaceWith (fun a b -> compare stamps.[a] stamps.[b]) order
        for rank in 0 .. n - 1 do
            onMove order.[rank] rank
        n

[<Sealed; Class>]
type BinaryHeap<'T>(compare: 'T -> 'T -> int, stampLimit: int) =
    let items = ResizeArray<'T>()
    let stamps = ResizeArray<int>()
    let mutable nextStamp = 0

    // Strict "pops before": the caller's order, then the earlier stamp.
    let before i j =
        let c = compare items.[i] items.[j]
        if c <> 0 then c < 0 else stamps.[i] < stamps.[j]

    let swap i j =
        let t = items.[i]
        items.[i] <- items.[j]
        items.[j] <- t
        let s = stamps.[i]
        stamps.[i] <- stamps.[j]
        stamps.[j] <- s

    let siftUp start =
        let mutable i = start
        let mutable moving = true
        while moving && i > 0 do
            let parent = (i - 1) / 2
            if before i parent then
                swap i parent
                i <- parent
            else
                moving <- false

    let siftDown start =
        let n = items.Count
        let mutable i = start
        let mutable moving = true
        while moving do
            let left = 2 * i + 1
            if left >= n then
                moving <- false
            else
                let right = left + 1
                let child = if right < n && before right left then right else left
                if before child i then
                    swap i child
                    i <- child
                else
                    moving <- false

    member _.Count = items.Count

    member _.Push(item: 'T) =
        if items.Count = 0 then nextStamp <- 0
        if nextStamp >= stampLimit then
            nextStamp <- Stamps.renumber stamps stampLimit (fun i rank -> stamps.[i] <- rank)
        items.Add item
        stamps.Add nextStamp
        nextStamp <- nextStamp + 1
        siftUp (items.Count - 1)

    member _.TryPeek() = if items.Count = 0 then None else Some items.[0]

    member _.TryPop() =
        let n = items.Count
        if n = 0 then
            None
        else
            let top = items.[0]
            let last = n - 1
            if last > 0 then swap 0 last
            items.RemoveAt last
            stamps.RemoveAt last
            if last > 0 then siftDown 0
            Some top

    member _.Clear() =
        items.Clear()
        stamps.Clear()
        nextStamp <- 0

[<RequireQualifiedAccess>]
module BinaryHeap =

    let create (compare: 'T -> 'T -> int) = BinaryHeap<'T>(compare, System.Int32.MaxValue)

    let internal createWithStampLimit (stampLimit: int) (compare: 'T -> 'T -> int) = BinaryHeap<'T>(compare, stampLimit)

    let count (heap: BinaryHeap<'T>) = heap.Count

    let isEmpty (heap: BinaryHeap<'T>) = heap.Count = 0

    let push (item: 'T) (heap: BinaryHeap<'T>) = heap.Push item

    let tryPeek (heap: BinaryHeap<'T>) = heap.TryPeek()

    let tryPop (heap: BinaryHeap<'T>) = heap.TryPop()

    let pop (heap: BinaryHeap<'T>) =
        match heap.TryPop() with
        | Some x -> x
        | None -> invalidOp "BinaryHeap.pop: the heap is empty"

    let clear (heap: BinaryHeap<'T>) = heap.Clear()

[<Sealed; Class>]
type IndexedHeap<'P>(compare: 'P -> 'P -> int, stampLimit: int) =
    // Heap arrays, parallel by heap position.
    let keys = ResizeArray<int>()
    let prios = ResizeArray<'P>()
    let stamps = ResizeArray<int>()
    // Key -> heap position, or -1 when absent. Grows to the largest key seen.
    let position = ResizeArray<int>()
    let mutable nextStamp = 0

    let before i j =
        let c = compare prios.[i] prios.[j]
        if c <> 0 then c < 0 else stamps.[i] < stamps.[j]

    let swap i j =
        let k = keys.[i]
        keys.[i] <- keys.[j]
        keys.[j] <- k
        let p = prios.[i]
        prios.[i] <- prios.[j]
        prios.[j] <- p
        let s = stamps.[i]
        stamps.[i] <- stamps.[j]
        stamps.[j] <- s
        position.[keys.[i]] <- i
        position.[keys.[j]] <- j

    let siftUp start =
        let mutable i = start
        let mutable moving = true
        while moving && i > 0 do
            let parent = (i - 1) / 2
            if before i parent then
                swap i parent
                i <- parent
            else
                moving <- false
        i

    let siftDown start =
        let n = keys.Count
        let mutable i = start
        let mutable moving = true
        while moving do
            let left = 2 * i + 1
            if left >= n then
                moving <- false
            else
                let right = left + 1
                let child = if right < n && before right left then right else left
                if before child i then
                    swap i child
                    i <- child
                else
                    moving <- false

    let restore i =
        if siftUp i = i then siftDown i

    let stamp () =
        if keys.Count = 0 then nextStamp <- 0
        if nextStamp >= stampLimit then
            nextStamp <- Stamps.renumber stamps stampLimit (fun i rank -> stamps.[i] <- rank)
        let s = nextStamp
        nextStamp <- nextStamp + 1
        s

    let positionOf key =
        if key < 0 || key >= position.Count then -1 else position.[key]

    member _.Count = keys.Count

    member _.Contains(key: int) = positionOf key >= 0

    member _.TryPriority(key: int) =
        let i = positionOf key
        if i < 0 then None else Some prios.[i]

    member _.Insert(key: int, priority: 'P) =
        if key < 0 then invalidArg "key" "IndexedHeap: keys must be non-negative"
        if positionOf key >= 0 then invalidArg "key" "IndexedHeap.insert: the key is already present"
        while position.Count <= key do
            position.Add -1
        let s = stamp ()
        keys.Add key
        prios.Add priority
        stamps.Add s
        let i = keys.Count - 1
        position.[key] <- i
        siftUp i |> ignore

    member this.DecreaseKey(key: int, priority: 'P) =
        let i = positionOf key
        if i < 0 then invalidArg "key" "IndexedHeap.decreaseKey: the key is absent"
        if compare priority prios.[i] > 0 then
            invalidArg "priority" "IndexedHeap.decreaseKey: the new priority is greater than the current one"
        this.SetAt(i, priority)

    member this.Update(key: int, priority: 'P) =
        if key < 0 then invalidArg "key" "IndexedHeap: keys must be non-negative"
        let i = positionOf key
        if i < 0 then this.Insert(key, priority) else this.SetAt(i, priority)

    member _.SetAt(i: int, priority: 'P) =
        let s = stamp ()
        prios.[i] <- priority
        stamps.[i] <- s
        restore i

    member _.Remove(key: int) =
        let i = positionOf key
        if i < 0 then
            false
        else
            let last = keys.Count - 1
            if i <> last then swap i last
            keys.RemoveAt last
            prios.RemoveAt last
            stamps.RemoveAt last
            position.[key] <- -1
            if i < last then restore i
            true

    member _.TryPeek() =
        if keys.Count = 0 then None else Some(keys.[0], prios.[0])

    member this.TryPop() =
        if keys.Count = 0 then
            None
        else
            let top = keys.[0], prios.[0]
            this.Remove(keys.[0]) |> ignore
            Some top

    member _.Clear() =
        for k in keys do
            position.[k] <- -1
        keys.Clear()
        prios.Clear()
        stamps.Clear()
        nextStamp <- 0

[<RequireQualifiedAccess>]
module IndexedHeap =

    let create (compare: 'P -> 'P -> int) = IndexedHeap<'P>(compare, System.Int32.MaxValue)

    let internal createWithStampLimit (stampLimit: int) (compare: 'P -> 'P -> int) = IndexedHeap<'P>(compare, stampLimit)

    let count (heap: IndexedHeap<'P>) = heap.Count

    let isEmpty (heap: IndexedHeap<'P>) = heap.Count = 0

    let contains (key: int) (heap: IndexedHeap<'P>) = heap.Contains key

    let tryPriority (key: int) (heap: IndexedHeap<'P>) = heap.TryPriority key

    let insert (key: int) (priority: 'P) (heap: IndexedHeap<'P>) = heap.Insert(key, priority)

    let decreaseKey (key: int) (priority: 'P) (heap: IndexedHeap<'P>) = heap.DecreaseKey(key, priority)

    let update (key: int) (priority: 'P) (heap: IndexedHeap<'P>) = heap.Update(key, priority)

    let remove (key: int) (heap: IndexedHeap<'P>) = heap.Remove key

    let tryPeek (heap: IndexedHeap<'P>) = heap.TryPeek()

    let tryPop (heap: IndexedHeap<'P>) = heap.TryPop()

    let clear (heap: IndexedHeap<'P>) = heap.Clear()
