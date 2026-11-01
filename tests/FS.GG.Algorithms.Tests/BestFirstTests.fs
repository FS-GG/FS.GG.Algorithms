module FS.GG.Algorithms.Tests.BestFirstTests

open Expecto
open FsCheck
open FsCheck.FSharp
open FS.GG.Algorithms.Graph
open FS.GG.Algorithms.Tests.Oracles
open FS.GG.Algorithms.Tests.Support

// ---------------------------------------------------------------- synthetic unit-cost grids

type GridCase =
    { Grid: Grid
      Start: int * int
      Goal: int * int }

let private gridCase =
    gen {
        let! w = Gen.choose (1, 14)
        let! h = Gen.choose (1, 14)
        let! density = Gen.choose (0, 45)
        let! cells = Gen.arrayOfLength (w * h) (Gen.map (fun r -> r >= density) (Gen.choose (0, 99)))
        let openRows = Array.init h (fun y -> Array.init w (fun x -> cells.[y * w + x]))
        let! sx = Gen.choose (0, w - 1)
        let! sy = Gen.choose (0, h - 1)
        let! gx = Gen.choose (0, w - 1)
        let! gy = Gen.choose (0, h - 1)
        openRows.[sy].[sx] <- true
        openRows.[gy].[gx] <- true
        return
            { Grid = { Width = w; Height = h; Open = openRows }
              Start = sx, sy
              Goal = gx, gy }
    }
    |> Arb.fromGen

let private unitNeighbours (g: Grid) c = gridNeighbours g c |> List.map (fun n -> n, 1)
let private manhattan (gx: int, gy: int) (x: int, y: int) = abs (x - gx) + abs (y - gy)

let private isValidGridPath (c: GridCase) (path: (int * int) list) (cost: int) =
    List.head path = c.Start
    && List.last path = c.Goal
    && List.length path - 1 = cost
    && path |> List.pairwise |> List.forall (fun (a, b) -> List.contains b (gridNeighbours c.Grid a))

let private costOf result =
    match result.Outcome with
    | Found(_, cost) -> Some cost
    | _ -> None

// ---------------------------------------------------------------- synthetic weighted digraphs

type GraphCase =
    { Graph: Digraph
      Source: int
      Target: int }

let private graphCase maxN =
    gen {
        let! n = Gen.choose (1, maxN)
        let! m = Gen.choose (0, n * 3)
        let! edges = Gen.listOfLength m (Gen.zip3 (Gen.choose (0, n - 1)) (Gen.choose (0, n - 1)) (Gen.choose (0, 9)))
        let! s = Gen.choose (0, n - 1)
        let! t = Gen.choose (0, n - 1)
        return
            { Graph = { N = n; Edges = edges }
              Source = s
              Target = t }
    }
    |> Arb.fromGen

let private isValidGraphPath (c: GraphCase) (path: int list) (cost: int) =
    // Each step must use an edge, and the cheapest parallel edge per step must sum to the cost.
    List.head path = c.Source
    && List.last path = c.Target
    && (path
        |> List.pairwise
        |> List.map (fun (a, b) ->
            c.Graph.Edges |> List.filter (fun (x, y, _) -> x = a && y = b) |> List.map (fun (_, _, w) -> w))
        |> fun steps -> steps |> List.forall (List.isEmpty >> not) && steps |> List.sumBy List.min = cost)

let tests =
    testList
        "Graph.BestFirst"
        [ property "Dijkstra cost equals BFS hop count on unit grids, and the path is valid" gridCase
          <| fun c ->
              let r = BestFirst.dijkstra (unitNeighbours c.Grid) compare (fun n -> n = c.Goal) c.Start
              match r.Outcome, bfs c.Grid c.Start c.Goal with
              | Found(path, cost), Some d -> cost = d && isValidGridPath c path cost
              | Unreachable, None -> true
              | _ -> false

          property "A* (Manhattan) cost equals BFS hop count on unit grids, and the path is valid" gridCase
          <| fun c ->
              let r = BestFirst.astar (unitNeighbours c.Grid) (manhattan c.Goal) compare c.Goal c.Start
              match r.Outcome, bfs c.Grid c.Start c.Goal with
              | Found(path, cost), Some d -> cost = d && isValidGridPath c path cost
              | Unreachable, None -> true
              | _ -> false

          property "A* (Manhattan) never expands more nodes than Dijkstra on unit grids" gridCase
          <| fun c ->
              let a = BestFirst.astar (unitNeighbours c.Grid) (manhattan c.Goal) compare c.Goal c.Start
              let d = BestFirst.dijkstra (unitNeighbours c.Grid) compare (fun n -> n = c.Goal) c.Start
              a.Expanded <= d.Expanded

          property "Any tie-break gives the same cost (reversed order)" gridCase
          <| fun c ->
              let fwd = BestFirst.astar (unitNeighbours c.Grid) (manhattan c.Goal) compare c.Goal c.Start
              let rev = BestFirst.astar (unitNeighbours c.Grid) (manhattan c.Goal) (fun a b -> compare b a) c.Goal c.Start
              costOf fwd = costOf rev

          property "distances equals Bellman-Ford on weighted digraphs (zero-cost edges included)" (graphCase 12)
          <| fun c ->
              let bf = bellmanFord c.Graph c.Source
              let ds = BestFirst.distances (digraphNeighbours c.Graph) compare c.Source
              let reachable = [ for v in 0 .. c.Graph.N - 1 do if bf.[v].IsSome then yield v, bf.[v].Value ]
              List.sort ds = List.sort reachable
              && List.length ds = List.length (List.distinctBy fst ds)
              && List.head ds = (c.Source, 0)
              && (ds |> List.map snd |> List.pairwise |> List.forall (fun (a, b) -> a <= b))

          property "Dijkstra cost equals Bellman-Ford, and the path is valid" (graphCase 12)
          <| fun c ->
              let r = BestFirst.dijkstra (digraphNeighbours c.Graph) compare (fun n -> n = c.Target) c.Source
              match r.Outcome, (bellmanFord c.Graph c.Source).[c.Target] with
              | Found(path, cost), Some d -> cost = d && isValidGraphPath c path cost
              | Unreachable, None -> true
              | _ -> false

          property "Dijkstra cost equals exhaustive simple-path enumeration on small digraphs" (graphCase 6)
          <| fun c ->
              let r = BestFirst.dijkstra (digraphNeighbours c.Graph) compare (fun n -> n = c.Target) c.Source
              costOf r = exhaustive c.Graph c.Source c.Target

          property "A* with a consistent heuristic (scaled true distance) is optimal" (Arb.fromGen (Gen.zip (graphCase 12).Generator (Gen.choose (0, 4))))
          <| fun (c, k) ->
              let toTarget = bellmanFord (reverse c.Graph) c.Target
              let h v = toTarget.[v] |> Option.map (fun d -> d * k / 4) |> Option.defaultValue 0
              let r = BestFirst.astar (digraphNeighbours c.Graph) h compare c.Target c.Source
              costOf r = (bellmanFord c.Graph c.Source).[c.Target]

          property "A* with an admissible but inconsistent heuristic (reopening) is optimal" (Arb.fromGen (Gen.zip (graphCase 10).Generator (Gen.arrayOfLength 10 (Gen.choose (0, 100)))))
          <| fun (c, fractions) ->
              let toTarget = bellmanFord (reverse c.Graph) c.Target
              let h v = toTarget.[v] |> Option.map (fun d -> d * fractions.[v] / 100) |> Option.defaultValue 0
              let r = BestFirst.astar (digraphNeighbours c.Graph) h compare c.Target c.Source
              costOf r = (bellmanFord c.Graph c.Source).[c.Target]

          property "A budget bounds the work count, and a found result equals the unbounded one" (Arb.fromGen (Gen.zip gridCase.Generator (Gen.choose (0, 60))))
          <| fun (c, budget) ->
              let p =
                  { BestFirst.problem c.Start (fun n -> n = c.Goal) (unitNeighbours c.Grid) with
                      Heuristic = manhattan c.Goal
                      MaxExpanded = budget }
              let bounded = BestFirst.search p
              let unbounded = BestFirst.search { p with MaxExpanded = System.Int32.MaxValue }
              bounded.Expanded <= budget
              && (match bounded.Outcome with
                  | BudgetExhausted -> bounded.Expanded = budget && unbounded.Expanded > budget
                  | _ -> bounded = unbounded)

          property "The search is deterministic: equal problems give equal results" gridCase
          <| fun c ->
              let run () = BestFirst.astar (unitNeighbours c.Grid) (manhattan c.Goal) compare c.Goal c.Start
              run () = run ()

          testCase "Refusals: negative edge cost and negative heuristic"
          <| fun () ->
              Expect.isTrue
                  (raises<System.ArgumentException> (fun () ->
                      BestFirst.dijkstra (fun n -> if n = 0 then [ 1, -1 ] else []) compare (fun n -> n = 1) 0 |> ignore))
                  "negative cost"
              Expect.isTrue
                  (raises<System.ArgumentException> (fun () ->
                      BestFirst.astar (fun n -> if n = 0 then [ 1, 1 ] else []) (fun _ -> -1) compare 1 0 |> ignore))
                  "negative heuristic"
              Expect.isTrue
                  (raises<System.OverflowException> (fun () ->
                      BestFirst.dijkstra
                          (fun n -> if n < 3 then [ n + 1, System.Int32.MaxValue / 2 ] else [])
                          compare
                          (fun n -> n = 3)
                          0
                      |> ignore))
                  "cost overflow"

          testCase "Stack safety: a 10^5-node chain (iterative expansion and path reconstruction)"
          <| fun () ->
              let n = 100000
              let r = BestFirst.dijkstra (fun i -> if i < n - 1 then [ i + 1, 1 ] else []) compare (fun i -> i = n - 1) 0
              match r.Outcome with
              | Found(path, cost) ->
                  Expect.equal cost (n - 1) "cost"
                  Expect.equal (List.length path) n "path length"
                  Expect.equal r.Expanded n "expanded"
              | other -> failtestf "expected Found, got %A" other ]
