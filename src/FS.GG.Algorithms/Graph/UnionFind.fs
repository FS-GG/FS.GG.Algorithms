namespace FS.GG.Algorithms.Graph

// Clean re-implementation of union-find (harvest 19, ADR-0098 §11), generic over the conflict predicate and
// stack-safe. Provenance: FS-GG/.github src/FS.GG.Coord.Core/Lanes.fs `partition` (its `find` recurses).
// References: B. A. Galler and M. J. Fischer, "An improved equivalence algorithm", Communications of the ACM
// 7(5), 1964; R. E. Tarjan, "Efficiency of a Good But Not Linear Set Union Algorithm", Journal of the ACM
// 22(2), 1975, doi:10.1145/321879.321884.

[<Sealed; Class>]
type UnionFind(n: int) =
    let parent = Array.init n id
    let size = Array.create n 1
    let least = Array.init n id // the smallest element of each root's set
    let mutable sets = n

    member _.Size = n
    member _.Count = sets

    member _.Root(i: int) =
        if i < 0 || i >= n then invalidArg "i" "UnionFind: element outside 0..n-1"
        let mutable r = i
        while parent.[r] <> r do
            r <- parent.[r]
        // Path compression, second pass: every node on the path now points at the root.
        let mutable c = i
        while parent.[c] <> r do
            let next = parent.[c]
            parent.[c] <- r
            c <- next
        r

    member this.Representative(i: int) = least.[this.Root i]

    member this.Union(i: int, j: int) =
        let ri = this.Root i
        let rj = this.Root j
        if ri = rj then
            false
        else
            let big, small = if size.[ri] >= size.[rj] then ri, rj else rj, ri
            parent.[small] <- big
            size.[big] <- size.[big] + size.[small]
            least.[big] <- min least.[big] least.[small]
            sets <- sets - 1
            true

[<RequireQualifiedAccess>]
module UnionFind =

    let create (n: int) =
        if n < 0 then invalidArg "n" "UnionFind.create: n must be non-negative"
        UnionFind(n)

    let representative (i: int) (uf: UnionFind) = uf.Representative i

    let union (i: int) (j: int) (uf: UnionFind) = uf.Union(i, j)

    let connected (i: int) (j: int) (uf: UnionFind) = uf.Root i = uf.Root j

    let count (uf: UnionFind) = uf.Count

    let sets (uf: UnionFind) =
        let groups = Array.create uf.Size []
        for i in uf.Size - 1 .. -1 .. 0 do
            let r = uf.Representative i
            groups.[r] <- i :: groups.[r]
        [ for g in groups do
              if not (List.isEmpty g) then yield g ]

    let partition (conflicts: 'T -> 'T -> bool) (items: 'T list) =
        let arr = Array.ofList items
        let uf = UnionFind(arr.Length)
        for i in 0 .. arr.Length - 1 do
            for j in i + 1 .. arr.Length - 1 do
                if conflicts arr.[i] arr.[j] then uf.Union(i, j) |> ignore
        sets uf |> List.map (List.map (fun i -> arr.[i]))
