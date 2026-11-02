namespace FS.GG.Algorithms.Graph

open System.Collections.Generic
open FS.GG.Algorithms.Collections

// Layer 1 (ADR-0098 §3 rule 6): the general best-first search, on the layer-0 indexed heap. Every
// priority-queue search in the library builds on it. Implemented from the published descriptions:
// E. W. Dijkstra, "A note on two problems in connexion with graphs", Numerische Mathematik 1, 1959;
// P. E. Hart, N. J. Nilsson and B. Raphael, "A Formal Basis for the Heuristic Determination of Minimum Cost
// Paths", IEEE Transactions on Systems Science and Cybernetics 4(2), 1968, doi:10.1109/TSSC.1968.300136.

type BestFirstOutcome<'N> =
    | Found of path: 'N list * cost: int
    | Unreachable
    | BudgetExhausted

type BestFirstResult<'N> =
    { Outcome: BestFirstOutcome<'N>
      Expanded: int }

type BestFirstProblem<'N> =
    { Start: 'N
      IsGoal: 'N -> bool
      Neighbours: 'N -> ('N * int) list
      Heuristic: 'N -> int
      TieBreak: 'N -> 'N -> int
      MaxExpanded: int }

[<RequireQualifiedAccess>]
module BestFirst =

    let private addChecked (a: int) (b: int) =
        // a, b >= 0
        if b > System.Int32.MaxValue - a then
            raise (System.OverflowException "BestFirst: a path cost or f value exceeds System.Int32.MaxValue")
        a + b

    let private heuristicOf (p: BestFirstProblem<'N>) (n: 'N) =
        let h = p.Heuristic n
        if h < 0 then invalidArg "Heuristic" "BestFirst: the heuristic returned a negative value"
        h

    /// The engine. `stopAtGoal = false` settles everything reachable (for `distances`).
    let private run
        (p: BestFirstProblem<'N>)
        (stopAtGoal: bool)
        (settled: ResizeArray<'N * int>)
        : BestFirstResult<'N> =
        let index = Dictionary<'N, int>(HashIdentity.Structural)
        let nodes = ResizeArray<'N>()
        let g = ResizeArray<int>()
        let h = ResizeArray<int>()
        let parent = ResizeArray<int>()

        let order (struct (f1: int, h1: int, n1: 'N)) (struct (f2: int, h2: int, n2: 'N)) =
            if f1 <> f2 then (if f1 < f2 then -1 else 1)
            elif h1 <> h2 then (if h1 < h2 then -1 else 1)
            else p.TieBreak n1 n2

        let heap = IndexedHeap.create order

        let discover (n: 'N) (cost: int) (from: int) =
            let j = nodes.Count
            let hn = heuristicOf p n
            index.[n] <- j
            nodes.Add n
            g.Add cost
            h.Add hn
            parent.Add from
            IndexedHeap.insert j (struct (addChecked cost hn, hn, n)) heap

        let pathTo (i: int) =
            let mutable acc = []
            let mutable k = i
            while k >= 0 do
                acc <- nodes.[k] :: acc
                k <- parent.[k]
            acc

        discover p.Start 0 -1
        let mutable outcome = None
        let mutable expanded = 0
        while outcome.IsNone do
            match IndexedHeap.tryPop heap with
            | None -> outcome <- Some Unreachable
            | Some(i, _) ->
                if expanded >= p.MaxExpanded then
                    outcome <- Some BudgetExhausted
                else
                    expanded <- expanded + 1
                    let n = nodes.[i]
                    let gi = g.[i]
                    settled.Add((n, gi))
                    if stopAtGoal && p.IsGoal n then
                        outcome <- Some(Found(pathTo i, gi))
                    else
                        for (m, c) in p.Neighbours n do
                            if c < 0 then invalidArg "Neighbours" "BestFirst: an edge cost is negative"
                            let ng = addChecked gi c
                            match index.TryGetValue m with
                            | true, j ->
                                if ng < g.[j] then
                                    g.[j] <- ng
                                    parent.[j] <- i
                                    IndexedHeap.update j (struct (addChecked ng h.[j], h.[j], m)) heap
                            | _ -> discover m ng i

        { Outcome = outcome.Value
          Expanded = expanded }

    let problem (start: 'N) (isGoal: 'N -> bool) (neighbours: 'N -> ('N * int) list) =
        { Start = start
          IsGoal = isGoal
          Neighbours = neighbours
          Heuristic = (fun _ -> 0)
          TieBreak = compare
          MaxExpanded = System.Int32.MaxValue }

    let search (problem: BestFirstProblem<'N>) = run problem true (ResizeArray())

    let dijkstra neighbours tieBreak isGoal (start: 'N) =
        run
            { Start = start
              IsGoal = isGoal
              Neighbours = neighbours
              Heuristic = (fun _ -> 0)
              TieBreak = tieBreak
              MaxExpanded = System.Int32.MaxValue }
            true
            (ResizeArray())

    let astar neighbours heuristic tieBreak (goal: 'N) (start: 'N) =
        run
            { Start = start
              IsGoal = (fun n -> n = goal)
              Neighbours = neighbours
              Heuristic = heuristic
              TieBreak = tieBreak
              MaxExpanded = System.Int32.MaxValue }
            true
            (ResizeArray())

    let distances neighbours tieBreak (start: 'N) =
        let settled = ResizeArray()
        run
            { Start = start
              IsGoal = (fun _ -> false)
              Neighbours = neighbours
              Heuristic = (fun _ -> 0)
              TieBreak = tieBreak
              MaxExpanded = System.Int32.MaxValue }
            false
            settled
        |> ignore
        List.ofSeq settled
