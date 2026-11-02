namespace FS.GG.Algorithms.Numerics

/// Nearest-rank percentiles with an exact integer rank (`LockstepExact`).
///
/// The nearest-rank percentile of n sorted samples at fraction q = numerator / denominator is the sample
/// at 1-based rank `max(1, ceil(q * n))`. The rank is computed in integers, never as `ceil(p * float n)`.
/// That double form is a pitfall for SOME fractions only: p/100 has no exact binary value, and when p * n is
/// an exact integer the double product can land just above it, so `ceil` over-ranks by one (it never
/// under-ranks). `ceil(0.07 * 100.0)` is 8, not 7. Measured over every count up to 10^5, the affected whole
/// percents are 7, 14, 17, 27, 28, 34, 54, 55, 56, 67, 68 and 81; 50, 90, 95 and 99 are unaffected.
[<RequireQualifiedAccess>]
module Percentile =

    /// The 1-based nearest rank `max(1, ceil(numerator * count / denominator))`, exact for every input,
    /// with no intermediate above `denominator^2`. Raises `System.ArgumentException` unless
    /// `count >= 1`, `1 <= denominator <= 46340` and `0 <= numerator <= denominator`.
    val rank: numerator: int -> denominator: int -> count: int -> int

    /// The nearest-rank sample of an array already sorted ascending. Raises `System.ArgumentException` for
    /// an empty array or a fraction `rank` refuses.
    val ofSorted: numerator: int -> denominator: int -> sorted: 'T[] -> 'T

    /// The nearest-rank sample at `percent` (0..100) of unsorted samples, sorted with `compare` (a stable
    /// sort, so equal samples keep their order). Raises `System.ArgumentException` for an empty list or a
    /// percent outside 0..100.
    val percent: percent: int -> samples: 'T list -> 'T when 'T: comparison
