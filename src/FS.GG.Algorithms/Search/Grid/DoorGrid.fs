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

    let private overflow () =
        raise (System.OverflowException "DoorGrid.distance: the distance exceeds System.Int32.MaxValue")

    // a, b >= 0
    let private addChecked (a: int) (b: int) = if b > System.Int32.MaxValue - a then overflow () else a + b
    let private mulChecked (a: int) (b: int) = if a <> 0 && b > System.Int32.MaxValue / a then overflow () else a * b

    let distance (grid: DoorGrid) (a: Tile) (b: Tile) =
        check grid
        let dx0 = abs (a.X - b.X)
        let dy0 = abs (a.Y - b.Y)
        let dx = max dx0 dy0 // the longer axis
        let dy = min dx0 dy0
        let o = grid.OrthogonalCost
        let d = grid.DiagonalCost
        // The exact cheapest cost on the OPEN lattice (no walls, doors or transitions) with these step costs.
        // Walls only remove moves and doors only add cost, so it never overestimates; as a shortest-path
        // metric of a graph whose every move the real grid also offers at no lower cost, it is consistent.
        if not grid.Diagonal || d >= 2 * o then
            mulChecked o (addChecked dx dy)
        elif d >= o then
            // Octile: dy diagonal steps, then dx - dy straight steps.
            addChecked (mulChecked d dy) (mulChecked o (dx - dy))
        elif (dx - dy) % 2 = 0 then
            // Diagonals are cheaper than straight steps: a zigzag of dx diagonals reaches any target whose
            // dx - dy is even (every diagonal changes the parity of x + y by 0 or 2).
            mulChecked d dx
        else
            // An odd dx - dy needs at least one straight step; at least dx steps are needed in all.
            addChecked (mulChecked d (dx - 1)) o

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
