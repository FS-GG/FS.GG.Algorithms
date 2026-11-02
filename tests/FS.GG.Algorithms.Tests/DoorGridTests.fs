module FS.GG.Algorithms.Tests.DoorGridTests

open Expecto
open FsCheck
open FsCheck.FSharp
open FS.GG.Algorithms.Graph
open FS.GG.Algorithms.Search.Grid
open FS.GG.Algorithms.Tests.Support

/// A synthetic layered map: planes x side x side; open cells; undirected edge passages; stairs.
type private MapCase =
    { Planes: int
      Side: int
      Open: bool[]
      Edges: Map<(int * int * int) * (int * int * int), Passage>
      Stairs: ((int * int * int) * (int * int * int) * int) list
      Diagonal: bool
      Ortho: int
      Diag: int
      Start: int * int * int
      Goal: int * int * int }

let private cellIndex (m: MapCase) (p, x, y) = (p * m.Side + y) * m.Side + x
let private inside (m: MapCase) (p, x, y) = p >= 0 && p < m.Planes && x >= 0 && y >= 0 && x < m.Side && y < m.Side
let private walkable (m: MapCase) c = inside m c && m.Open.[cellIndex m c]
let private key a b = if compare a b <= 0 then a, b else b, a
let private passage (m: MapCase) a b = m.Edges.TryFind(key a b) |> Option.defaultValue Passage.Open

let private mapCase =
    gen {
        let! planes = Gen.choose (1, 3)
        let! side = Gen.choose (1, 7)
        let cells = planes * side * side
        let! openCells = Gen.arrayOfLength cells (Gen.frequency [ 4, Gen.constant true; 1, Gen.constant false ])
        let all = [ for p in 0 .. planes - 1 do for y in 0 .. side - 1 do for x in 0 .. side - 1 do yield p, x, y ]
        let pairs =
            [ for (p, x, y) in all do
                  if x + 1 < side then yield (p, x, y), (p, x + 1, y)
                  if y + 1 < side then yield (p, x, y), (p, x, y + 1) ]
        let! kinds = Gen.listOfLength pairs.Length (Gen.frequency [ 6, Gen.constant 0; 2, Gen.constant 1; 2, Gen.choose (2, 30) ])
        let edges =
            List.zip pairs kinds
            |> List.choose (fun (pr, k) ->
                match k with
                | 0 -> None
                | 1 -> Some(pr, Passage.Blocked)
                | c -> Some(pr, Passage.Door(c - 2)))
            |> Map.ofList
        let! stairCount = Gen.choose (0, 4)
        let! stairs =
            Gen.listOfLength stairCount (
                gen {
                    let! p = Gen.choose (0, planes - 1)
                    let! q = Gen.choose (0, planes - 1)
                    let! x = Gen.choose (0, side - 1)
                    let! y = Gen.choose (0, side - 1)
                    let! dx = Gen.choose (-1, 1)
                    let! extra = Gen.choose (0, 20)
                    return (p, x, y), (q, max 0 (min (side - 1) (x + dx)), y), extra
                })
        let! diagonal = Gen.elements [ true; false ]
        // Arbitrary positive cost pairs, including diagonal < orthogonal (zigzags beat straight lines) and
        // diagonal > 2 x orthogonal (diagonals are useless), plus the classic pairs.
        let! costs =
            Gen.frequency
                [ 1, Gen.elements [ 10, 14; 1, 1; 10, 1; 1, 3 ]
                  3, Gen.zip (Gen.choose (1, 20)) (Gen.choose (1, 45)) ]
        let! sp = Gen.choose (0, planes - 1)
        let! sx = Gen.choose (0, side - 1)
        let! sy = Gen.choose (0, side - 1)
        let! gp = Gen.choose (0, planes - 1)
        let! gx = Gen.choose (0, side - 1)
        let! gy = Gen.choose (0, side - 1)
        let openCells = Array.copy openCells
        let m0 = { Planes = planes; Side = side; Open = openCells; Edges = edges; Stairs = []; Diagonal = diagonal; Ortho = fst costs; Diag = snd costs; Start = sp, sx, sy; Goal = gp, gx, gy }
        openCells.[cellIndex m0 (sp, sx, sy)] <- true
        openCells.[cellIndex m0 (gp, gx, gy)] <- true
        // A stair costs its extra plus the in-plane move distance it covers, so the A* heuristic stays valid.
        let stairs = stairs |> List.map (fun (a, (q, bx, by), extra) -> let (_, ax, _) = a in a, (q, bx, by), extra + abs (bx - ax) * fst costs)
        return { m0 with Stairs = stairs }
    }
    |> Arb.fromGen

let private toTile (p, x, y) = { Plane = p; X = x; Y = y }
let private ofTile (t: Tile) = t.Plane, t.X, t.Y

let private grid (m: MapCase) =
    { Walkable = ofTile >> walkable m
      Edge = (fun t dx dy -> let a = ofTile t in let (p, x, y) = a in passage m a (p, x + dx, y + dy))
      Transitions = (fun t -> m.Stairs |> List.filter (fun (a, _, _) -> a = ofTile t) |> List.map (fun (_, b, c) -> toTile b, c))
      Diagonal = m.Diagonal
      OrthogonalCost = m.Ortho
      DiagonalCost = m.Diag }

/// The oracle's own adjacency, written from the rules independently of DoorGrid.neighbours.
let private oracleMoves (m: MapCase) (c: int * int * int) =
    let (p, x, y) = c
    let steps =
        [ for (dx, dy) in [ 0, -1; 1, 0; 0, 1; -1, 0 ] do
              let n = p, x + dx, y + dy
              if walkable m n then
                  match passage m c n with
                  | Passage.Open -> yield n, m.Ortho
                  | Passage.Door e -> yield n, m.Ortho + e
                  | Passage.Blocked -> ()
          if m.Diagonal then
              for (dx, dy) in [ 1, -1; 1, 1; -1, 1; -1, -1 ] do
                  let n = p, x + dx, y + dy
                  let sx = p, x + dx, y
                  let sy = p, x, y + dy
                  let isOpen a b = passage m a b = Passage.Open
                  if walkable m n && walkable m sx && walkable m sy && isOpen c sx && isOpen c sy && isOpen sx n && isOpen sy n then
                      yield n, m.Diag ]
    steps @ (m.Stairs |> List.filter (fun (a, _, _) -> a = c) |> List.map (fun (_, b, k) -> b, k))

/// Dijkstra by a plain list scan over the explicit state space (the oracle): every reachable tile's distance.
let private oracleAll (m: MapCase) =
    let dist = System.Collections.Generic.Dictionary<int * int * int, int>()
    let settled = System.Collections.Generic.HashSet<int * int * int>()
    dist.[m.Start] <- 0
    let mutable go = true
    while go do
        let open' = dist |> Seq.filter (fun kv -> not (settled.Contains kv.Key)) |> Seq.toList
        if open'.IsEmpty then go <- false
        else
            let kv = open' |> List.minBy (fun kv -> kv.Value)
            settled.Add kv.Key |> ignore
            for (n, c) in oracleMoves m kv.Key do
                match dist.TryGetValue n with
                | true, d when d <= kv.Value + c -> ()
                | _ -> dist.[n] <- kv.Value + c
    dist

/// Dijkstra by a plain list scan over the explicit state space (the oracle).
let private oracleDistance (m: MapCase) =
    let dist = System.Collections.Generic.Dictionary<int * int * int, int>()
    let settled = System.Collections.Generic.HashSet<int * int * int>()
    dist.[m.Start] <- 0
    let mutable go = true
    while go do
        let open' = dist |> Seq.filter (fun kv -> not (settled.Contains kv.Key)) |> Seq.toList
        if open'.IsEmpty then go <- false
        else
            let kv = open' |> List.minBy (fun kv -> kv.Value)
            settled.Add kv.Key |> ignore
            for (n, c) in oracleMoves m kv.Key do
                match dist.TryGetValue n with
                | true, d when d <= kv.Value + c -> ()
                | _ -> dist.[n] <- kv.Value + c
    match dist.TryGetValue m.Goal with
    | true, d -> Some d
    | _ -> None

let tests =
    testList
        "Search.Grid.DoorGrid"
        [ property "neighbours equal the oracle's independently written adjacency (walls, doors, no corner cutting, stairs)" mapCase
          <| fun m ->
              let g = grid m
              [ for p in 0 .. m.Planes - 1 do for y in 0 .. m.Side - 1 do for x in 0 .. m.Side - 1 do yield p, x, y ]
              |> List.filter (walkable m)
              |> List.forall (fun c -> (DoorGrid.neighbours g (toTile c) |> List.map (fun (t, k) -> ofTile t, k)) = oracleMoves m c)

          property "A* and Dijkstra cost equal the oracle's shortest distance, and the path uses only oracle moves" mapCase
          <| fun m ->
              let g = grid m
              let expected = oracleDistance m
              [ true; false ]
              |> List.forall (fun h ->
                  match (DoorGrid.findPath g h System.Int32.MaxValue (toTile m.Goal) (toTile m.Start)).Outcome, expected with
                  | Found(path, cost), Some d ->
                      cost = d
                      && List.head path = toTile m.Start
                      && List.last path = toTile m.Goal
                      && (path
                          |> List.pairwise
                          |> List.sumBy (fun (a, b) ->
                              oracleMoves m (ofTile a) |> List.filter (fun (n, _) -> n = ofTile b) |> List.map snd |> List.min)) = cost
                  | Unreachable, None -> true
                  | _ -> false)

          property "the heuristic is a lower bound of the true remaining cost" mapCase
          <| fun m ->
              let g = grid m
              match oracleDistance m with
              | Some d -> DoorGrid.distance g (toTile m.Start) (toTile m.Goal) <= d
              | None -> true

          property "A* equals Dijkstra and the oracle on open grids with arbitrary cost pairs (the review's 490/1500 class)" mapCase
          <| fun m0 ->
              let m = { m0 with Planes = 1; Open = Array.create (m0.Side * m0.Side) true; Edges = Map.empty; Stairs = []; Start = (0, 0, 0); Goal = (0, m0.Side - 1, (m0.Side - 1) / 2) }
              let g = grid m
              let cost h = match (DoorGrid.findPath g h System.Int32.MaxValue (toTile m.Goal) (toTile m.Start)).Outcome with | Found(_, c) -> Some c | _ -> None
              cost true = oracleDistance m && cost false = oracleDistance m

          property "the heuristic is consistent for every cost pair: h(goal) = 0 and h(u) <= c(u, v) + h(v) for every move" mapCase
          <| fun m ->
              let g = grid m
              let h c = DoorGrid.distance g (toTile c) (toTile m.Goal)
              h m.Goal = 0
              && ([ for p in 0 .. m.Planes - 1 do for y in 0 .. m.Side - 1 do for x in 0 .. m.Side - 1 do yield p, x, y ]
                  |> List.filter (walkable m)
                  |> List.forall (fun u -> oracleMoves m u |> List.forall (fun (v, c) -> h u <= c + h v)))

          property "the heuristic is tight: on an open single-plane grid it equals the oracle's distance to every tile" mapCase
          <| fun m0 ->
              // Same costs and size, but no walls, doors, closed cells or stairs.
              let m = { m0 with Planes = 1; Open = Array.create (m0.Side * m0.Side) true; Edges = Map.empty; Stairs = []; Start = (0, 0, 0); Goal = (0, 0, 0) }
              let g = grid m
              let all = oracleAll m
              all |> Seq.forall (fun kv -> DoorGrid.distance g (toTile m.Start) (toTile kv.Key) = kv.Value)

          testCase "regression: cheap diagonals (orthogonal 10, diagonal 1) keep A* optimal (review of FS.GG.Algorithms#6)"
          <| fun () ->
              let m = { Planes = 1; Side = 4; Open = Array.create 16 true; Edges = Map.empty; Stairs = []; Diagonal = true; Ortho = 10; Diag = 1; Start = 0, 0, 0; Goal = 0, 3, 1 }
              let costOf h = match (DoorGrid.findPath (grid m) h 1000000 (toTile m.Goal) (toTile m.Start)).Outcome with | Found(_, c) -> c | o -> failwithf "%A" o
              Expect.equal (costOf true, costOf false) (3, 3) "A* equals Dijkstra"
              Expect.equal (DoorGrid.distance (grid m) (toTile (0, 0, 0)) (toTile (0, 3, 1))) 3 "a zigzag of three diagonals"
              Expect.equal (DoorGrid.distance (grid m) (toTile (0, 0, 0)) (toTile (0, 3, 0))) 12 "odd parity: two diagonals and one straight step"

          testCase "the heuristic refuses overflow instead of wrapping"
          <| fun () ->
              let m = { Planes = 1; Side = 1; Open = [| true |]; Edges = Map.empty; Stairs = []; Diagonal = false; Ortho = 1000000; Diag = 1; Start = 0, 0, 0; Goal = 0, 0, 0 }
              Expect.isTrue (raises<System.OverflowException> (fun () -> DoorGrid.distance (grid m) { Plane = 0; X = 0; Y = 0 } { Plane = 0; X = 5000; Y = 0 } |> ignore)) "overflow"

          testCase "a door costs its extra; a diagonal never passes a door or a wall corner"
          <| fun () ->
              let doorAt = ((0, 0, 0), (0, 1, 0))
              let m =
                  { Planes = 1; Side = 2; Open = Array.create 4 true; Edges = Map.ofList [ doorAt, Passage.Door 7 ]; Stairs = []
                    Diagonal = true; Ortho = 10; Diag = 14; Start = 0, 0, 0; Goal = 0, 1, 1 }
              let ns = DoorGrid.neighbours (grid m) (toTile (0, 0, 0)) |> List.map (fun (t, k) -> ofTile t, k)
              Expect.equal ns [ (0, 1, 0), 17; (0, 0, 1), 10 ] "the door costs 10 + 7; the diagonal is refused"
              match (DoorGrid.findPath (grid m) true 1000 (toTile (0, 1, 1)) (toTile (0, 0, 0))).Outcome with
              | Found(_, cost) -> Expect.equal cost 20 "around the door, not through the corner"
              | o -> failtestf "unexpected %A" o

          testCase "planes: a stair is the only way up"
          <| fun () ->
              let m =
                  { Planes = 2; Side = 3; Open = Array.create 18 true; Edges = Map.empty; Stairs = [ (0, 2, 2), (1, 2, 2), 5 ]
                    Diagonal = false; Ortho = 1; Diag = 1; Start = 0, 0, 0; Goal = 1, 0, 0 }
              match (DoorGrid.findPath (grid m) true 1000 (toTile m.Goal) (toTile m.Start)).Outcome with
              | Found(path, cost) ->
                  Expect.equal cost (4 + 5 + 4) "walk to the stair, climb, walk back"
                  Expect.contains path { Plane = 1; X = 2; Y = 2 } "via the stair top"
              | o -> failtestf "unexpected %A" o

          testCase "refusals and the budget"
          <| fun () ->
              let m =
                  { Planes = 1; Side = 2; Open = Array.create 4 true; Edges = Map.empty; Stairs = []; Diagonal = false; Ortho = 0; Diag = 1; Start = 0, 0, 0; Goal = 0, 1, 1 }
              Expect.isTrue (raises<System.ArgumentException> (fun () -> DoorGrid.neighbours (grid m) (toTile (0, 0, 0)) |> ignore)) "zero cost"
              let ok = { m with Ortho = 1 }
              Expect.equal (DoorGrid.findPath (grid ok) true 0 (toTile ok.Goal) (toTile ok.Start)).Outcome BudgetExhausted "budget 0"

          testCase "Stack safety: a 10^5-tile corridor"
          <| fun () ->
              let n = 100000
              let g =
                  { Walkable = (fun t -> t.Plane = 0 && t.Y = 0 && t.X >= 0 && t.X < n)
                    Edge = (fun _ _ _ -> Passage.Open)
                    Transitions = (fun _ -> [])
                    Diagonal = true
                    OrthogonalCost = 1
                    DiagonalCost = 1 }
              match (DoorGrid.findPath g true System.Int32.MaxValue { Plane = 0; X = n - 1; Y = 0 } { Plane = 0; X = 0; Y = 0 }).Outcome with
              | Found(path, cost) -> Expect.equal (List.length path, cost) (n, n - 1) "corridor"
              | o -> failtestf "unexpected %A" o ]
