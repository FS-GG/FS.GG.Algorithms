namespace FS.GG.Algorithms.Graph

open System.Collections.Generic
open FS.GG.Algorithms.Collections

// Kahn's algorithm with a heap-ordered ready set (ADR-0098 §11 N1, built on F1 and on Scc for cycle reports)
// and the critical-path method. Implemented from the published descriptions: A. B. Kahn, "Topological
// sorting of large networks", Communications of the ACM 5(11), 1962, doi:10.1145/368996.369025;
// J. E. Kelley and M. R. Walker, "Critical-path planning and scheduling", Proc. Eastern Joint Computer
// Conference, 1959, doi:10.1145/1460299.1460318.

type CpmActivity<'N> =
    { Node: 'N
      Duration: int
      EarliestStart: int
      EarliestFinish: int
      LatestStart: int
      LatestFinish: int
      Slack: int }

type CpmSchedule<'N> =
    { Activities: CpmActivity<'N> list
      Makespan: int
      CriticalPath: 'N list }

[<RequireQualifiedAccess>]
module TopoSort =

    /// Vertices (first occurrence) and integer successor lists, ignoring edges that leave the vertex set.
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

    /// Kahn over 0..n-1: the vertex ids in topological order, or None when a cycle blocks some vertex.
    let private kahn (order: 'N -> 'N -> int) (verts: ResizeArray<'N>) (succ: int[][]) =
        let n = verts.Count
        let indegree = Array.create n 0
        for v in 0 .. n - 1 do
            for w in succ.[v] do
                indegree.[w] <- indegree.[w] + 1
        let ready = BinaryHeap.create (fun (a: int) (b: int) -> order verts.[a] verts.[b])
        for v in 0 .. n - 1 do
            if indegree.[v] = 0 then BinaryHeap.push v ready
        let out = ResizeArray<int>()
        while not (BinaryHeap.isEmpty ready) do
            let v = BinaryHeap.pop ready
            out.Add v
            for w in succ.[v] do
                indegree.[w] <- indegree.[w] - 1
                if indegree.[w] = 0 then BinaryHeap.push w ready
        if out.Count = n then Some out else None

    let sort (order: 'N -> 'N -> int) (nodes: 'N list) (successors: 'N -> 'N list) =
        let verts, succ = table nodes successors
        match kahn order verts succ with
        | Some ids -> Ok(ids |> Seq.map (fun v -> verts.[v]) |> List.ofSeq)
        | None -> Error(Scc.cycles nodes successors)

    let private addChecked (a: int) (b: int) =
        // a, b >= 0
        if b > System.Int32.MaxValue - a then
            raise (System.OverflowException "TopoSort.criticalPath: a time exceeds System.Int32.MaxValue")
        a + b

    let criticalPath (order: 'N -> 'N -> int) (nodes: 'N list) (successors: 'N -> 'N list) (duration: 'N -> int) =
        let verts, succ = table nodes successors
        match kahn order verts succ with
        | None -> Error(Scc.cycles nodes successors)
        | Some topo ->
            let n = verts.Count
            let dur =
                Array.init n (fun v ->
                    let d = duration verts.[v]
                    if d < 0 then invalidArg "duration" "TopoSort.criticalPath: a duration is negative"
                    d)
            let es = Array.create n 0
            let ef = Array.create n 0
            let mutable makespan = 0
            // Forward pass in topological order.
            for v in topo do
                ef.[v] <- addChecked es.[v] dur.[v]
                if ef.[v] > makespan then makespan <- ef.[v]
                for w in succ.[v] do
                    if ef.[v] > es.[w] then es.[w] <- ef.[v]
            // Backward pass in reverse topological order.
            let lf = Array.create n makespan
            let ls = Array.create n 0
            for k in n - 1 .. -1 .. 0 do
                let v = topo.[k]
                for w in succ.[v] do
                    if ls.[w] < lf.[v] then lf.[v] <- ls.[w]
                ls.[v] <- lf.[v] - dur.[v]
            // Predecessor lists in topological order, for tracing the critical path backwards.
            let rank = Array.create n 0
            for k in 0 .. n - 1 do
                rank.[topo.[k]] <- k
            let preds = Array.create n []
            for k in n - 1 .. -1 .. 0 do
                let v = topo.[k]
                for w in succ.[v] do
                    preds.[w] <- v :: preds.[w]
            let path =
                if n = 0 then
                    []
                else
                    let mutable last = -1
                    for v in topo do
                        if last < 0 && ef.[v] = makespan then last <- v
                    let mutable acc = [ last ]
                    let mutable cur = last
                    let mutable tracing = true
                    while tracing do
                        // The first predecessor, in topological order, that finishes exactly when cur starts.
                        let mutable next = -1
                        for p in preds.[cur] do
                            if ef.[p] = es.[cur] && (next < 0 || rank.[p] < rank.[next]) then next <- p
                        if next < 0 then
                            tracing <- false
                        else
                            acc <- next :: acc
                            cur <- next
                    acc |> List.map (fun v -> verts.[v])
            let activities =
                [ for v in topo ->
                      { Node = verts.[v]
                        Duration = dur.[v]
                        EarliestStart = es.[v]
                        EarliestFinish = ef.[v]
                        LatestStart = ls.[v]
                        LatestFinish = lf.[v]
                        Slack = ls.[v] - es.[v] } ]
            Ok
                { Activities = activities
                  Makespan = makespan
                  CriticalPath = path }
