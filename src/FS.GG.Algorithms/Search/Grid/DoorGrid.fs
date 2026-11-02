namespace FS.GG.Algorithms.Search.Grid

open FS.GG.Algorithms.Graph

// Grid A* with thin walls, doors and planes (the harvest item "A* with doors and plane handling",
// FS-GG/.github#5224), written generically on BestFirst (F3). Not derived from any private or third-party
// source. The technique of walls on edges and no corner cutting is standard grid pathfinding (Hart, Nilsson
// and Raphael 1968; see BestFirst).

[<Struct>]
type Tile = { Plane: int; X: int; Y: int }

[<RequireQualifiedAccess>]
type Passage =
    | Open
    | Door of extraCost: int
    | Blocked

type DoorGrid =
    { Walkable: Tile -> bool
      Edge: Tile -> int -> int -> Passage
      Transitions: Tile -> (Tile * int) list
      Diagonal: bool
      OrthogonalCost: int
      DiagonalCost: int }

[<RequireQualifiedAccess>]
module DoorGrid =

    let private check (grid: DoorGrid) =
        if grid.OrthogonalCost <= 0 || grid.DiagonalCost <= 0 then
            invalidArg "grid" "DoorGrid: step costs must be positive"

    let private at (t: Tile) dx dy = { t with X = t.X + dx; Y = t.Y + dy }

    let neighbours (grid: DoorGrid) (tile: Tile) =
        let t = tile
        check grid
        let orthogonal =
            [ for (dx, dy) in [ 0, -1; 1, 0; 0, 1; -1, 0 ] do
                  let n = at t dx dy
                  if grid.Walkable n then
                      match grid.Edge t dx dy with
                      | Passage.Open -> yield n, grid.OrthogonalCost
                      | Passage.Door extra ->
                          if extra < 0 then invalidArg "grid" "DoorGrid: a door's extra cost is negative"
                          yield n, grid.OrthogonalCost + extra
                      | Passage.Blocked -> () ]
        let diagonal =
            if not grid.Diagonal then
                []
            else
                [ for (dx, dy) in [ 1, -1; 1, 1; -1, 1; -1, -1 ] do
                      let n = at t dx dy
                      let sideX = at t dx 0
                      let sideY = at t 0 dy
                      let open' p = (p = Passage.Open)
                      if grid.Walkable n
                         && grid.Walkable sideX
                         && grid.Walkable sideY
                         && open' (grid.Edge t dx 0)
                         && open' (grid.Edge t 0 dy)
                         && open' (grid.Edge sideX 0 dy)
                         && open' (grid.Edge sideY dx 0) then
                          yield n, grid.DiagonalCost ]
        let transitions =
            grid.Transitions t
            |> List.map (fun (n, c) ->
                if c < 0 then invalidArg "grid" "DoorGrid: a transition cost is negative"
                n, c)
        orthogonal @ diagonal @ transitions

    let distance (grid: DoorGrid) (a: Tile) (b: Tile) =
        let dx = abs (a.X - b.X)
        let dy = abs (a.Y - b.Y)
        if grid.Diagonal then
            let d = min grid.DiagonalCost (2 * grid.OrthogonalCost)
            d * min dx dy + grid.OrthogonalCost * (max dx dy - min dx dy)
        else
            grid.OrthogonalCost * (dx + dy)

    let private tieBreak (a: Tile) (b: Tile) =
        if a.Plane <> b.Plane then compare a.Plane b.Plane
        elif a.Y <> b.Y then compare a.Y b.Y
        else compare a.X b.X

    let findPath (grid: DoorGrid) (heuristic: bool) (maxExpanded: int) (goal: Tile) (start: Tile) =
        check grid
        BestFirst.search
            { Start = start
              IsGoal = (fun t -> t = goal)
              Neighbours = neighbours grid
              Heuristic = (if heuristic then distance grid goal else fun _ -> 0)
              TieBreak = tieBreak
              MaxExpanded = maxExpanded }
