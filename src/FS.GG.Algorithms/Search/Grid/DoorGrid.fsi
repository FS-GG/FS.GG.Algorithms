namespace FS.GG.Algorithms.Search.Grid

open FS.GG.Algorithms.Graph

/// A tile of a layered grid: a plane (floor or level) and integer coordinates.
[<Struct>]
type Tile = { Plane: int; X: int; Y: int }

/// What lies on the edge between a tile and an orthogonal neighbour. Walls and doors live on edges, not
/// in tiles.
[<RequireQualifiedAccess>]
type Passage =
    | Open
    /// Passable orthogonally; crossing costs the step plus `extraCost` (>= 0), for example the time to open
    /// it. A door is never crossed diagonally.
    | Door of extraCost: int
    | Blocked

/// A layered grid described by caller functions: the A* with doors and planes of FS-GG/.github#5224's
/// harvest list, written generically (ADR-0098 §5). It carries no game data, collision flags or constants.
type DoorGrid =
    { /// Whether a tile can be stood on (false outside the map).
      Walkable: Tile -> bool
      /// The passage from a tile towards its orthogonal neighbour (dx, dy), one of (0,-1), (1,0), (0,1) and
      /// (-1,0). The search asks only from the side it leaves; make the function symmetric if both
      /// directions must agree.
      Edge: Tile -> int -> int -> Passage
      /// Moves between tiles outside the grid steps, such as stairs and ladders, with their cost (>= 0).
      /// They may change plane. For A* to stay optimal, a transition must cost at least the in-plane move
      /// distance (`DoorGrid.distance`) between its two tiles; otherwise search with `heuristic = false`.
      Transitions: Tile -> (Tile * int) list
      /// Whether 8-way moves are allowed.
      Diagonal: bool
      /// The cost of an orthogonal step (> 0).
      OrthogonalCost: int
      /// The cost of a diagonal step (> 0), for example 14 against 10, or 1 against 1 for Chebyshev movement.
      DiagonalCost: int }

/// Grid pathfinding with thin walls, doors and plane transitions, on the general best-first search (F3).
///
/// MOVES, in this fixed neighbour order: north (0,-1), east, south, west; then, when `Diagonal`, north-east,
/// south-east, south-west and north-west; then the caller's transitions in their order.
/// - An orthogonal move needs a walkable target and a non-Blocked edge. A Door adds its extra cost.
/// - A diagonal move never cuts a corner. Both orthogonal intermediate tiles must be walkable, and all four
///   edges around the corner must be Open (no wall and no door).
///
/// DETERMINISM (`LockstepExact` for deterministic caller functions): integer costs, and BestFirst's frozen
/// order, with ties broken by (Plane, Y, X).
///
/// DOOR STATE is the caller's: re-closing doors per session means calling with a different `Edge`. The
/// search is a pure function of the grid it is given.
[<RequireQualifiedAccess>]
module DoorGrid =

    /// The successors of a tile with their costs, in the fixed order above. Raises
    /// `System.ArgumentException` for a non-positive step cost or a negative door cost.
    val neighbours: grid: DoorGrid -> tile: Tile -> (Tile * int) list

    /// The in-plane move distance between two tiles, ignoring planes, walls and doors. It is octile when
    /// `Diagonal` (min(DiagonalCost, 2 * OrthogonalCost) per diagonal step) and Manhattan otherwise, scaled by
    /// the step costs. It is the A* heuristic.
    val distance: grid: DoorGrid -> a: Tile -> b: Tile -> int

    /// The cheapest path from `start` to `goal`. With `heuristic` it is A* on `distance`, which is optimal
    /// under the transition rule above; without it, Dijkstra. Expansions are capped at `maxExpanded`.
    val findPath:
        grid: DoorGrid -> heuristic: bool -> maxExpanded: int -> goal: Tile -> start: Tile -> BestFirstResult<Tile>
