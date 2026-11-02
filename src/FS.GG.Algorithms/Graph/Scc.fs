namespace FS.GG.Algorithms.Graph

open System.Collections.Generic

// Clean re-implementation of Tarjan's SCC (harvest 18, ADR-0098 §11), rewritten iteratively with an explicit
// frame stack. Provenance: FS-GG/.github src/FS.GG.Coord.Core/Blockers.fs `cycles` (recursive `strongconnect`).
// Reference: R. E. Tarjan, "Depth-First Search and Linear Graph Algorithms", SIAM Journal on Computing 1(2),
// 1972, doi:10.1137/0201010.

[<RequireQualifiedAccess>]
module Scc =

    /// Vertex table: the distinct nodes in first-occurrence order and the integer successor lists.
    let private table (nodes: 'N list) (successors: 'N -> 'N list) =
        let index = Dictionary<'N, int>(HashIdentity.Structural)
        let verts = ResizeArray<'N>()
        for n in nodes do
            if not (index.ContainsKey n) then
                index.[n] <- verts.Count
                verts.Add n
        let succ =
            Array.init verts.Count (fun v ->
                successors verts.[v]
                |> List.choose (fun w ->
                    match index.TryGetValue w with
                    | true, j -> Some j
                    | _ -> None)
                |> Array.ofList)
        verts, succ

    /// Iterative Tarjan over 0..n-1. Returns the components (vertex ids in discovery order) in completion order.
    let private components (succ: int[][]) =
        let n = succ.Length
        let index = Array.create n -1
        let low = Array.create n 0
        let onStack = Array.create n false
        let stack = ResizeArray<int>()
        let frameV = ResizeArray<int>() // the call stack: vertex and next successor position
        let frameP = ResizeArray<int>()
        let found = ResizeArray<int list>()
        let mutable counter = 0

        let visit v =
            index.[v] <- counter
            low.[v] <- counter
            counter <- counter + 1
            stack.Add v
            onStack.[v] <- true
            frameV.Add v
            frameP.Add 0

        for root in 0 .. n - 1 do
            if index.[root] < 0 then
                visit root
                while frameV.Count > 0 do
                    let top = frameV.Count - 1
                    let v = frameV.[top]
                    let p = frameP.[top]
                    if p < succ.[v].Length then
                        frameP.[top] <- p + 1
                        let w = succ.[v].[p]
                        if index.[w] < 0 then visit w
                        elif onStack.[w] then low.[v] <- min low.[v] index.[w]
                    else
                        frameV.RemoveAt top
                        frameP.RemoveAt top
                        if top > 0 then
                            let parent = frameV.[top - 1]
                            low.[parent] <- min low.[parent] low.[v]
                        if low.[v] = index.[v] then
                            // Pop the component; popping yields reverse discovery order, so prepend.
                            let mutable comp = []
                            let mutable popping = true
                            while popping do
                                let w = stack.[stack.Count - 1]
                                stack.RemoveAt(stack.Count - 1)
                                onStack.[w] <- false
                                comp <- w :: comp
                                if w = v then popping <- false
                            found.Add comp
        found

    let tarjan (nodes: 'N list) (successors: 'N -> 'N list) =
        let verts, succ = table nodes successors
        components succ |> Seq.map (List.map (fun v -> verts.[v])) |> List.ofSeq

    let cycles (nodes: 'N list) (successors: 'N -> 'N list) =
        let verts, succ = table nodes successors
        components succ
        |> Seq.filter (fun comp ->
            match comp with
            | [ single ] -> Array.contains single succ.[single]
            | _ -> true)
        |> Seq.map (List.map (fun v -> verts.[v]))
        |> List.ofSeq

    let componentOf (nodes: 'N list) (successors: 'N -> 'N list) =
        let verts, succ = table nodes successors
        let id = Array.create verts.Count 0
        components succ |> Seq.iteri (fun c comp -> for v in comp do id.[v] <- c)
        let index = Dictionary<'N, int>(HashIdentity.Structural)
        for v in 0 .. verts.Count - 1 do
            index.[verts.[v]] <- v
        nodes |> List.map (fun n -> id.[index.[n]])
