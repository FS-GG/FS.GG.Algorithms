namespace FS.GG.Algorithms.Decision

open FS.GG.Algorithms.Numerics

/// A utility in the inclusive interval 0..1, represented by F2 Q16.16. Construct through UtilityAi.score.
[<Struct>]
type UtilityScore = private UtilityScore of Fixed

/// Monotone increasing responses on normalized input. No floating-point transcendentals.
[<RequireQualifiedAccess>]
type UtilityCurve =
    | Linear
    | Quadratic
    | SquareRoot

/// Deterministic utility scoring and selection (ADR-0098 N3). All operations are LockstepExact.
/// Inputs are normalized by the caller; arbitrary Fixed values clamp to 0..1. No effects or randomness.
[<RequireQualifiedAccess>]
module UtilityAi =
    /// Clamp a Fixed value to 0..1. Every int32 raw representation is accepted.
    val score: value: Fixed -> UtilityScore
    /// Return the bounded Q16.16 value.
    val value: score: UtilityScore -> Fixed
    /// Clamp input, then apply the response. Quadratic uses F2 nearest/ties-up multiplication;
    /// square root uses F2 floor square root. Endpoints are exactly 0 and 1.
    val evaluate: curve: UtilityCurve -> input: Fixed -> UtilityScore
    /// Complement 1-score; turns an increasing response into a decreasing one, exactly.
    val invert: score: UtilityScore -> UtilityScore
    /// Multiply considerations in input order, rounding after each multiply by F2 rules. Empty means 1;
    /// any zero vetoes. This is an ordered multiplicative policy, without compensation or weighting.
    /// Rounding makes reassociation/permutation potentially observable. O(n) time, O(1) extra space.
    val product: considerations: UtilityScore list -> UtilityScore
    /// Highest score, keeping the first input on ties (including all zero). Empty means None.
    /// Caller orders actions deterministically; ids need no equality/comparison. O(n) time/O(1) space.
    val choose: actions: ('Action * UtilityScore) list -> ('Action * UtilityScore) option
