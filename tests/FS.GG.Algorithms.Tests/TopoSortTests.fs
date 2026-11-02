module FS.GG.Algorithms.Tests.TopoSortTests

open Expecto
open FsCheck
open FsCheck.FSharp
open FS.GG.Algorithms.Graph
open FS.GG.Algorithms.Tests.Support

type private G = { N: int; Succ: int list[] }

/// Random digraphs (cycles likely) and random DAGs (edges only to higher ids, so acyclic by construction).
let private graph (dag: bool) =
    gen {
        let! n = Gen.choose (0, 12)
        let! lists =
            Gen.arrayOfLength n (Gen.listOf (Gen.choose (0, max 0 (n - 1))) |> Gen.map (List.truncate 4))
        let succ =
            if dag then lists |> Array.mapi (fun v l -> l |> List.filter (fun w -> w > v)) else lists
        return { N = n; Succ = succ }
    }

let private anyGraph = Arb.fromGen (Gen.oneof [ graph true; graph false ])
let private nodes g = [ 0 .. g.N - 1 ]
let private succ g v = g.Succ.[v]

/// The list model of Kahn with a stamped ready set: least by `order`, then earliest to become ready.
let private kahnModel (order: int -> int -> int) (g: G) =
    let indeg = Array.create g.N 0
    for v in 0 .. g.N - 1 do
        for w in g.Succ.[v] do
            indeg.[w] <- indeg.[w] + 1
    let mutable ready = [ for v in 0 .. g.N - 1 do if indeg.[v] = 0 then yield v ] |> List.mapi (fun i v -> v, i)
    let mutable stamp = ready.Length
    let out = ResizeArray<int>()
    while not ready.IsEmpty do
        let (v, _) as first =
            ready |> List.reduce (fun (a, sa) (b, sb) -> let c = order a b in if c < 0 || (c = 0 && sa < sb) then (a, sa) else (b, sb))
        ready <- ready |> List.filter ((<>) first)
        out.Add v
        for w in g.Succ.[v] do
            indeg.[w] <- indeg.[w] - 1
            if indeg.[w] = 0 then
                ready <- ready @ [ w, stamp ]
                stamp <- stamp + 1
    if out.Count = g.N then Some(List.ofSeq out) else None

/// Brute force: the largest sum of durations over every path (single nodes included) of a DAG.
let private longestByEnumeration (g: G) (dur: int[]) =
    let rec best v = dur.[v] + (g.Succ.[v] |> List.map best |> List.fold max 0)
    if g.N = 0 then 0 else [ 0 .. g.N - 1 ] |> List.map best |> List.max

let private durations n = Gen.arrayOfLength n (Gen.choose (0, 9))

let tests =
    testList
        "Graph.TopoSort"
        [ property "Ok exactly when Tarjan finds no cycle; Error carries exactly Scc.cycles" anyGraph
          <| fun g ->
              match TopoSort.sort compare (nodes g) (succ g), Scc.cycles (nodes g) (succ g) with
              | Ok _, [] -> true
              | Error cs, cycles -> cs = cycles && not cycles.IsEmpty
              | Ok _, _ -> false

          property "an order is a permutation in which every edge points forward" anyGraph
          <| fun g ->
              match TopoSort.sort compare (nodes g) (succ g) with
              | Error _ -> true
              | Ok order ->
                  let pos = order |> List.mapi (fun i v -> v, i) |> Map.ofList
                  List.sort order = nodes g
                  && Seq.forall (fun (u, v) -> pos.[u] < pos.[v]) [ for u in 0 .. g.N - 1 do for v in g.Succ.[u] do yield u, v ]

          property "the order equals the list model (least by order, then earliest ready), for total orders and total preorders with ties"
              (Arb.fromGen (Gen.zip (Gen.oneof [ graph true; graph false ]) (Gen.elements [ 0; 1; 2 ])))
          <| fun (g, which) ->
              let order: int -> int -> int =
                  match which with
                  | 0 -> compare
                  | 1 -> fun a b -> compare b a
                  | _ -> fun a b -> compare (a % 3) (b % 3) // a total preorder with ties: ties go to readiness order
              match TopoSort.sort order (nodes g) (succ g), kahnModel order g with
              | Ok o, Some m -> o = m
              | Error _, None -> true
              | _ -> false

          property "critical path: the makespan equals brute-force longest path on small DAGs"
              (Arb.fromGen (gen {
                  let! g = graph true
                  let! d = durations g.N
                  return g, d }))
          <| fun (g, d) ->
              match TopoSort.criticalPath compare (nodes g) (succ g) (fun v -> d.[v]) with
              | Ok s -> s.Makespan = longestByEnumeration g d
              | Error _ -> false

          property "critical path: the CPM times hold, slack >= 0, and the path is a zero-slack chain summing to the makespan"
              (Arb.fromGen (gen {
                  let! g = graph true
                  let! d = durations g.N
                  return g, d }))
          <| fun (g, d) ->
              match TopoSort.criticalPath compare (nodes g) (succ g) (fun v -> d.[v]) with
              | Error _ -> false
              | Ok s ->
                  let a = s.Activities |> List.map (fun x -> x.Node, x) |> Map.ofList
                  let preds v = [ for u in 0 .. g.N - 1 do if List.contains v g.Succ.[u] then yield u ]
                  let timesHold =
                      s.Activities
                      |> List.forall (fun x ->
                          x.EarliestStart = (preds x.Node |> List.map (fun p -> a.[p].EarliestFinish) |> List.fold max 0)
                          && x.EarliestFinish = x.EarliestStart + x.Duration
                          && x.LatestFinish = (g.Succ.[x.Node] |> List.map (fun w -> a.[w].LatestStart) |> List.fold min s.Makespan)
                          && x.LatestStart = x.LatestFinish - x.Duration
                          && x.Slack = x.LatestStart - x.EarliestStart
                          && x.Slack >= 0)
                  let p = s.CriticalPath
                  let chain =
                      g.N = 0 && p.IsEmpty
                      || (not p.IsEmpty
                          && (p |> List.sumBy (fun v -> d.[v])) = s.Makespan
                          && p |> List.forall (fun v -> a.[v].Slack = 0)
                          && p |> List.pairwise |> List.forall (fun (u, v) -> List.contains v g.Succ.[u])
                          && a.[List.last p].EarliestFinish = s.Makespan)
                  timesHold && chain

          property "critical path end and trace rules: it ends at the first activity (topological order) finishing at the makespan, and each step back is the first predecessor finishing when the current one starts"
              (Arb.fromGen (gen {
                  let! g = graph true
                  let! d = Gen.arrayOfLength g.N (Gen.choose (0, 3)) // small durations: many ties
                  return g, d }))
          <| fun (g, d) ->
              match TopoSort.criticalPath compare (nodes g) (succ g) (fun v -> d.[v]) with
              | Error _ -> false
              | Ok s when g.N = 0 -> s.CriticalPath.IsEmpty
              | Ok s ->
                  let acts = Array.ofList s.Activities
                  let rank v = acts |> Array.findIndex (fun a -> a.Node = v)
                  let ef v = acts.[rank v].EarliestFinish
                  let es v = acts.[rank v].EarliestStart
                  let preds v = [ for u in 0 .. g.N - 1 do if List.contains v g.Succ.[u] then yield u ]
                  let expectedEnd = (acts |> Array.find (fun a -> a.EarliestFinish = s.Makespan)).Node
                  let rec trace cur acc =
                      match preds cur |> List.filter (fun p -> ef p = es cur) |> List.sortBy rank with
                      | p :: _ -> trace p (p :: acc)
                      | [] -> acc
                  s.CriticalPath = trace expectedEnd [ expectedEnd ]

          property "critical path refuses a cyclic graph with the same cycles as sort" (Arb.fromGen (graph false))
          <| fun g ->
              match TopoSort.criticalPath compare (nodes g) (succ g) (fun _ -> 1), TopoSort.sort compare (nodes g) (succ g) with
              | Error a, Error b -> a = b
              | Ok _, Ok _ -> true
              | _ -> false

          testCase "duplicates keep the first occurrence; edges outside the node set are ignored; the trace rule"
          <| fun () ->
              let succ v = match v with | "a" -> [ "c"; "zz" ] | "b" -> [ "c" ] | _ -> []
              Expect.equal (TopoSort.sort compare [ "b"; "a"; "c"; "a" ] succ) (Ok [ "a"; "b"; "c" ]) "sort"
              let dur v = match v with | "a" -> 3 | "b" -> 3 | _ -> 1
              match TopoSort.criticalPath compare [ "a"; "b"; "c" ] succ dur with
              | Ok s ->
                  Expect.equal s.Makespan 4 "makespan"
                  Expect.equal s.CriticalPath [ "a"; "c" ] "first predecessor in topological order"
              | Error e -> failtestf "unexpected %A" e

          testCase "refusal: a negative duration"
          <| fun () ->
              Expect.isTrue
                  (raises<System.ArgumentException> (fun () -> TopoSort.criticalPath compare [ 1 ] (fun _ -> []) (fun _ -> -1) |> ignore))
                  "negative"

          testCase "Stack safety: a 10^5-node chain"
          <| fun () ->
              let n = 100000
              let succ v = if v < n - 1 then [ v + 1 ] else []
              match TopoSort.sort compare [ n - 1 .. -1 .. 0 ] succ with
              | Ok o -> Expect.equal o [ 0 .. n - 1 ] "chain order"
              | Error _ -> failtest "no cycle expected"
              match TopoSort.criticalPath compare [ 0 .. n - 1 ] succ (fun _ -> 1) with
              | Ok s ->
                  Expect.equal s.Makespan n "makespan"
                  Expect.equal (List.length s.CriticalPath) n "path"
              | Error _ -> failtest "no cycle expected" ]
