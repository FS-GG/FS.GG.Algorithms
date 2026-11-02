namespace FS.GG.Algorithms.Graph

/// A disjoint-set forest over the elements 0..n-1: union by size with path compression, both iterative.
///
/// DETERMINISM (`LockstepExact`). `representative` is always the SMALLEST element of the set, whatever the
/// order of unions, so set identities never depend on the internal forest shape.
[<Sealed; Class>]
type UnionFind =
    /// The number of elements.
    member Size: int
    /// The number of disjoint sets.
    member Count: int

/// Operations on `UnionFind`, and partitioning by a conflict predicate.
[<RequireQualifiedAccess>]
module UnionFind =

    /// n singleton sets {0} .. {n-1}. Raises `System.ArgumentException` when `n < 0`.
    val create: n: int -> UnionFind

    /// The smallest element of the set containing `i`. Amortised near-constant. Raises
    /// `System.ArgumentException` when `i` is outside 0..n-1.
    val representative: i: int -> uf: UnionFind -> int

    /// Merges the sets of `i` and `j`; returns whether they were separate.
    val union: i: int -> j: int -> uf: UnionFind -> bool

    /// Whether `i` and `j` are in the same set.
    val connected: i: int -> j: int -> uf: UnionFind -> bool

    /// The number of disjoint sets.
    val count: uf: UnionFind -> int

    /// Every set, each as its elements in ascending order, the sets ordered by their smallest element.
    val sets: uf: UnionFind -> int list list

    /// Partitions `items` into the connected components of the graph whose edges are the pairs `conflicts`
    /// holds for. The predicate is asked once for each unordered pair, i < j, as `conflicts items[i] items[j]`.
    /// Groups keep input order inside, and are ordered by their first member's input position. O(n^2) predicate
    /// calls: meant for hundreds of items.
    val partition: conflicts: ('T -> 'T -> bool) -> items: 'T list -> 'T list list
