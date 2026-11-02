namespace FS.GG.Algorithms.Spatial

/// A point stored in a `KdTree`: integer coordinates, a payload, and its position in the input list.
type KdPoint<'T> =
    { X: int
      Y: int
      Value: 'T
      /// The point's position in the list given to `KdTree.build` (a stable identity for ties).
      Index: int }

/// A static 2-d tree over integer points (ADR-0098 §11 N14; the spatial-query harvest of FS-GG/.github#5224).
[<Sealed; Class>]
type KdTree<'T> =
    /// The number of points.
    member Count: int

/// Nearest-neighbour, k-nearest, radius and rectangle queries over a static 2-d tree.
///
/// COORDINATES, of stored points and of query points alike, are integers in -16383..16383, so a squared
/// distance (at most 2 * 32766^2) fits in an `int32`. Queries raise `System.ArgumentException` for a query
/// point outside that range. `LockstepExact`: there is no floating point anywhere.
///
/// DETERMINISM. Every result is ordered by a total key: squared Euclidean distance, then X, then Y, then the
/// input index. Equal-distance ties therefore never depend on the tree's shape or the runtime. Pruning keeps
/// any subtree that could hold a tie (it uses `>`, never `>=`), so results equal a brute-force scan with the
/// same key.
///
/// Building and every query are iterative, with explicit stacks, so even 10^5 collinear or duplicate points
/// are stack-safe. Building is O(n log^2 n); a nearest query is about O(log n) on spread-out points.
///
/// This index replaces a full-table scan for nearest queries. It is not a moving-object index: rebuild it,
/// or use a uniform grid (Game.Core `SpatialGrid`), for data that changes every frame.
[<RequireQualifiedAccess>]
module KdTree =

    /// The smallest and largest allowed coordinate.
    [<Literal>]
    val MinCoordinate: int = -16383

    [<Literal>]
    val MaxCoordinate: int = 16383

    /// Builds a tree. Raises `System.ArgumentException` for a coordinate outside -16383..16383.
    val build: points: (int * int * 'T) list -> KdTree<'T>

    /// The nearest point to (x, y), least by (squared distance, X, Y, Index); `None` for an empty tree.
    val nearest: x: int -> y: int -> tree: KdTree<'T> -> KdPoint<'T> option

    /// The k nearest points, nearest first, by the same key. Raises `System.ArgumentException` for k < 0.
    val kNearest: k: int -> x: int -> y: int -> tree: KdTree<'T> -> KdPoint<'T> list

    /// Every point within Euclidean distance `radius` of (x, y), boundary included, nearest first. Raises
    /// `System.ArgumentException` for a radius outside 0..46340, so that radius^2 fits in an `int32` (46340
    /// already exceeds the diagonal of the coordinate square).
    val withinRadius: radius: int -> x: int -> y: int -> tree: KdTree<'T> -> KdPoint<'T> list

    /// Every point with minX <= X <= maxX and minY <= Y <= maxY, ordered by (X, Y, Index).
    val inRectangle: minX: int -> minY: int -> maxX: int -> maxY: int -> tree: KdTree<'T> -> KdPoint<'T> list

    /// The squared Euclidean distance between two points, exact in `int32`. Raises `System.ArgumentException`
    /// for a coordinate outside -16383..16383, where the result could overflow.
    val distanceSquared: x1: int -> y1: int -> x2: int -> y2: int -> int
