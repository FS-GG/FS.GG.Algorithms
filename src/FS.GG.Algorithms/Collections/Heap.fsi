namespace FS.GG.Algorithms.Collections

/// A binary min-heap ordered by a caller-supplied comparison, with a frozen pop order for equal priorities.
///
/// ORDERING CONTRACT (frozen, `LockstepExact`, ADR-0098 §1 and §3 F1). This contract is what consumers such
/// as FS.GG.Game.Core pin; changing the pop order for any input is a breaking change with a new
/// compatibility identity, never a patch.
///
/// 1. `pop` returns an element `x` such that `compare x y <= 0` for every other element `y` in the heap.
/// 2. Among elements that compare equal (`compare x y = 0`), the element pushed EARLIEST pops first
///    (first in, first out). The heap stamps each push with an insertion sequence number; equal elements
///    pop in increasing stamp order.
/// 3. The pop order is therefore a function of the sequence of operations and the comparison alone. It
///    never depends on the heap's internal array layout, on hashing, or on the runtime.
///
/// `compare` must be a total preorder (reflexive, transitive, total) and must not change while elements
/// are in the heap. Callers that need a deterministic order among equal priorities beyond FIFO supply it
/// in `compare` (a tie-break key), which is the recommended use for lockstep code.
///
/// The heap is mutable and not thread-safe. Every operation is iterative: no recursion depends on size.
[<Sealed; Class>]
type BinaryHeap<'T> =
    /// The number of elements in the heap.
    member Count: int

/// Operations on `BinaryHeap`.
[<RequireQualifiedAccess>]
module BinaryHeap =

    /// An empty heap ordered by `compare` (smaller pops first).
    val create: compare: ('T -> 'T -> int) -> BinaryHeap<'T>

    /// The number of elements. O(1).
    val count: heap: BinaryHeap<'T> -> int

    /// Whether the heap is empty. O(1).
    val isEmpty: heap: BinaryHeap<'T> -> bool

    /// Adds an element. O(log n) amortised. The insertion stamp is an `int32`: it restarts when the heap
    /// becomes empty, and when it reaches 2^31 - 1 the live stamps are renumbered 0..n-1 in their existing
    /// order (O(n log n), once per ~2^31 pushes), so the pop order is unchanged and no push ever fails.
    val push: item: 'T -> heap: BinaryHeap<'T> -> unit

    /// Test hook: a heap whose stamps renumber at `stampLimit` instead of 2^31 - 1.
    val internal createWithStampLimit: stampLimit: int -> compare: ('T -> 'T -> int) -> BinaryHeap<'T>

    /// The element `pop` would return, without removing it. O(1).
    val tryPeek: heap: BinaryHeap<'T> -> 'T option

    /// Removes and returns the least element (earliest pushed among equals). O(log n).
    val tryPop: heap: BinaryHeap<'T> -> 'T option

    /// Removes and returns the least element. Raises `System.InvalidOperationException` when empty.
    val pop: heap: BinaryHeap<'T> -> 'T

    /// Removes every element.
    val clear: heap: BinaryHeap<'T> -> unit

/// A binary min-heap of integer keys with priorities, supporting decrease-key, update and removal by key.
///
/// ORDERING CONTRACT (frozen, `LockstepExact`). Keys are non-negative `int`s; each key is present at most
/// once. Every `insert`, `decreaseKey` and `update` stamps the key with a fresh sequence number.
///
/// 1. `pop` returns a key whose priority `p` satisfies `compare p q <= 0` for every other present priority.
/// 2. Among keys whose priorities compare equal, the key whose priority was SET EARLIEST (smallest stamp)
///    pops first. A key whose priority changes, even to an equal value, moves behind the keys already
///    holding that priority, exactly as if it were removed and inserted again.
/// 3. The pop order is therefore the order of a lazy-deletion heap fed the same operations; it never
///    depends on internal layout, hashing or runtime.
///
/// Memory grows with the largest key used (a position table indexed by key). The heap is mutable and not
/// thread-safe; every operation is iterative.
[<Sealed; Class>]
type IndexedHeap<'P> =
    /// The number of keys in the heap.
    member Count: int

/// Operations on `IndexedHeap`.
[<RequireQualifiedAccess>]
module IndexedHeap =

    /// An empty indexed heap ordered by `compare` on priorities (smaller pops first).
    val create: compare: ('P -> 'P -> int) -> IndexedHeap<'P>

    /// The number of keys present. O(1).
    val count: heap: IndexedHeap<'P> -> int

    /// Whether the heap is empty. O(1).
    val isEmpty: heap: IndexedHeap<'P> -> bool

    /// Whether `key` is present. O(1). A negative key is never present.
    val contains: key: int -> heap: IndexedHeap<'P> -> bool

    /// The current priority of `key`, if present. O(1).
    val tryPriority: key: int -> heap: IndexedHeap<'P> -> 'P option

    /// Test hook: an indexed heap whose stamps renumber at `stampLimit` instead of 2^31 - 1 (see
    /// `BinaryHeap.push`: renumbering keeps the order, so pops are unchanged).
    val internal createWithStampLimit: stampLimit: int -> compare: ('P -> 'P -> int) -> IndexedHeap<'P>

    /// Adds `key` with `priority`. O(log n). Raises `System.ArgumentException` when `key` is negative or
    /// already present.
    val insert: key: int -> priority: 'P -> heap: IndexedHeap<'P> -> unit

    /// Lowers the priority of a present `key` (a new priority equal to the old one is allowed and restamps
    /// the key). O(log n). Raises `System.ArgumentException` when `key` is absent or when `priority` compares
    /// greater than the current one.
    val decreaseKey: key: int -> priority: 'P -> heap: IndexedHeap<'P> -> unit

    /// Sets the priority of `key`, inserting it when absent. O(log n). Raises `System.ArgumentException`
    /// when `key` is negative.
    val update: key: int -> priority: 'P -> heap: IndexedHeap<'P> -> unit

    /// Removes `key` if present; returns whether it was present. O(log n).
    val remove: key: int -> heap: IndexedHeap<'P> -> bool

    /// The key and priority `pop` would return, without removing it. O(1).
    val tryPeek: heap: IndexedHeap<'P> -> (int * 'P) option

    /// Removes and returns the least key and its priority. O(log n).
    val tryPop: heap: IndexedHeap<'P> -> (int * 'P) option

    /// Removes every key.
    val clear: heap: IndexedHeap<'P> -> unit
