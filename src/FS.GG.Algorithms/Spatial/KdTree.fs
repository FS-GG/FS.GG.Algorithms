namespace FS.GG.Algorithms.Spatial

// A static 2-d tree (ADR-0098 §11 N14, the spatial-query harvest of FS-GG/.github#5224). Implemented from the
// published description: J. L. Bentley, "Multidimensional binary search trees used for associative
// searching", Communications of the ACM 18(9), 1975, doi:10.1145/361002.361007; J. H. Friedman, J. L. Bentley
// and R. A. Finkel, "An Algorithm for Finding Best Matches in Logarithmic Expected Time", ACM TOMS 3(3),
// 1977, doi:10.1145/355744.355745. The tree is implicit: each node is the median of a sorted segment of one
// array, and every walk uses an explicit stack.

type KdPoint<'T> =
    { X: int
      Y: int
      Value: 'T
      Index: int }

[<Sealed; Class>]
type KdTree<'T>(points: KdPoint<'T>[]) =
    member _.Count = points.Length
    member _.Points = points

[<RequireQualifiedAccess>]
module KdTree =

    [<Literal>]
    let MinCoordinate = -16383

    [<Literal>]
    let MaxCoordinate = 16383

    let private inRange (v: int) = v >= MinCoordinate && v <= MaxCoordinate

    let private squared (x1: int) (y1: int) (x2: int) (y2: int) =
        let dx = x1 - x2
        let dy = y1 - y2
        dx * dx + dy * dy

    let distanceSquared (x1: int) (y1: int) (x2: int) (y2: int) =
        if not (inRange x1 && inRange y1 && inRange x2 && inRange y2) then
            invalidArg "x1" "KdTree.distanceSquared: a coordinate is outside -16383..16383"
        let dx = x1 - x2
        let dy = y1 - y2
        dx * dx + dy * dy

    /// The total result key: (squared distance, X, Y, Index).
    let private before (d1: int) (a: KdPoint<'T>) (d2: int) (b: KdPoint<'T>) =
        if d1 <> d2 then d1 < d2
        elif a.X <> b.X then a.X < b.X
        elif a.Y <> b.Y then a.Y < b.Y
        else a.Index < b.Index

    let private axisCompare (axis: int) (a: KdPoint<'T>) (b: KdPoint<'T>) =
        let p, q = (if axis = 0 then a.X, b.X else a.Y, b.Y)
        if p <> q then compare p q
        else
            let r, s = (if axis = 0 then a.Y, b.Y else a.X, b.X)
            if r <> s then compare r s else compare a.Index b.Index

    let build (points: (int * int * 'T) list) =
        let arr =
            points
            |> List.mapi (fun i (x, y, v) ->
                if x < MinCoordinate || x > MaxCoordinate || y < MinCoordinate || y > MaxCoordinate then
                    invalidArg "points" "KdTree.build: a coordinate is outside -16383..16383"
                { X = x; Y = y; Value = v; Index = i })
            |> Array.ofList
        // Sort each segment by its depth's axis; its median is the node. Explicit stack of (lo, hi, depth).
        let stack = ResizeArray<struct (int * int * int)>()
        stack.Add(struct (0, arr.Length, 0))
        while stack.Count > 0 do
            let struct (lo, hi, depth) = stack.[stack.Count - 1]
            stack.RemoveAt(stack.Count - 1)
            if hi - lo > 1 then
                let axis = depth % 2
                let segment = arr.[lo .. hi - 1]
                Array.sortInPlaceWith (axisCompare axis) segment
                Array.blit segment 0 arr lo segment.Length
                let mid = lo + (hi - lo) / 2
                stack.Add(struct (lo, mid, depth + 1))
                stack.Add(struct (mid + 1, hi, depth + 1))
        KdTree<'T>(arr)

    /// Walks the tree, visiting a segment only when `keep (lower bound on its squared distance)` holds.
    /// `visit` sees each node point; segments are explored nearest side first.
    let private walk (tree: KdTree<'T>) (x: int) (y: int) (keep: int -> bool) (visit: KdPoint<'T> -> unit) =
        let arr = tree.Points
        let stack = ResizeArray<struct (int * int * int * int)>() // lo, hi, depth, bound
        stack.Add(struct (0, arr.Length, 0, 0))
        while stack.Count > 0 do
            let struct (lo, hi, depth, bound) = stack.[stack.Count - 1]
            stack.RemoveAt(stack.Count - 1)
            if hi > lo && keep bound then
                let mid = lo + (hi - lo) / 2
                let p = arr.[mid]
                visit p
                let axis = depth % 2
                let diff = if axis = 0 then x - p.X else y - p.Y
                let planeBound = max bound (diff * diff)
                // Push the far side first so the near side is explored first. A point exactly on the plane
                // can lie on either side (ties sort by the other coordinate), so both sides stay reachable.
                if diff < 0 then
                    stack.Add(struct (mid + 1, hi, depth + 1, planeBound))
                    stack.Add(struct (lo, mid, depth + 1, bound))
                else
                    stack.Add(struct (lo, mid, depth + 1, planeBound))
                    stack.Add(struct (mid + 1, hi, depth + 1, bound))

    let private checkQuery (x: int) (y: int) =
        if x < MinCoordinate || x > MaxCoordinate || y < MinCoordinate || y > MaxCoordinate then
            invalidArg "x" "KdTree: the query point is outside -16383..16383"

    let nearest (x: int) (y: int) (tree: KdTree<'T>) =
        checkQuery x y
        let mutable best: KdPoint<'T> option = None
        let mutable bestD = 0
        walk tree x y (fun bound -> best.IsNone || bound <= bestD) (fun p ->
            let d = squared x y p.X p.Y
            match best with
            | Some b when not (before d p bestD b) -> ()
            | _ ->
                best <- Some p
                bestD <- d)
        best

    let kNearest (k: int) (x: int) (y: int) (tree: KdTree<'T>) =
        if k < 0 then invalidArg "k" "KdTree.kNearest: k must be non-negative"
        checkQuery x y
        // The candidates, kept sorted by the result key (k is small: insertion is fine).
        let found = ResizeArray<struct (int * KdPoint<'T>)>()
        if k > 0 then
            walk tree x y (fun bound -> found.Count < k || bound <= (let struct (d, _) = found.[found.Count - 1] in d)) (fun p ->
                let d = squared x y p.X p.Y
                let mutable i = found.Count
                while i > 0 && (let struct (d2, q) = found.[i - 1] in before d p d2 q) do
                    i <- i - 1
                if i < k then
                    found.Insert(i, struct (d, p))
                    if found.Count > k then found.RemoveAt k)
        [ for struct (_, p) in found -> p ]

    let withinRadius (radius: int) (x: int) (y: int) (tree: KdTree<'T>) =
        if radius < 0 || radius > 46340 then invalidArg "radius" "KdTree.withinRadius: radius must be in 0..46340"
        checkQuery x y
        let r2 = radius * radius
        let found = ResizeArray<struct (int * KdPoint<'T>)>()
        walk tree x y (fun bound -> bound <= r2) (fun p ->
            let d = squared x y p.X p.Y
            if d <= r2 then found.Add(struct (d, p)))
        found
        |> Seq.sortWith (fun (struct (d1, a)) (struct (d2, b)) -> if before d1 a d2 b then -1 elif before d2 b d1 a then 1 else 0)
        |> Seq.map (fun (struct (_, p)) -> p)
        |> List.ofSeq

    let inRectangle (minX: int) (minY: int) (maxX: int) (maxY: int) (tree: KdTree<'T>) =
        let arr = tree.Points
        let found = ResizeArray<KdPoint<'T>>()
        let stack = ResizeArray<struct (int * int * int)>()
        stack.Add(struct (0, arr.Length, 0))
        while stack.Count > 0 do
            let struct (lo, hi, depth) = stack.[stack.Count - 1]
            stack.RemoveAt(stack.Count - 1)
            if hi > lo then
                let mid = lo + (hi - lo) / 2
                let p = arr.[mid]
                if p.X >= minX && p.X <= maxX && p.Y >= minY && p.Y <= maxY then found.Add p
                let v, lo', hi' = (if depth % 2 = 0 then p.X, minX, maxX else p.Y, minY, maxY)
                // Ties on the axis may sit on either side, so each side is searched when the range touches v.
                if lo' <= v then stack.Add(struct (lo, mid, depth + 1))
                if hi' >= v then stack.Add(struct (mid + 1, hi, depth + 1))
        found
        |> Seq.sortWith (fun a b -> if a.X <> b.X then compare a.X b.X elif a.Y <> b.Y then compare a.Y b.Y else compare a.Index b.Index)
        |> List.ofSeq
