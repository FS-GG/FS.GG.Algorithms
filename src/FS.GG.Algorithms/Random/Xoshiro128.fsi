namespace FS.GG.Algorithms.Random

/// The state of a xoshiro128** generator (Blackman and Vigna): 128 bits as four `int32` words, never all zero.
///
/// The generator is an immutable value: every draw returns the next state. Equal states produce equal
/// streams on every runtime, so a state can be saved, compared and restored in lockstep code.
///
/// GRADES (ADR-0098 §3 F4). `seed`, `ofState`, `state`, `next`, `nextBelow`, `nextInRange`, `split` and
/// `jump` are `LockstepExact`: they use only 32-bit shifts, rotations, xors, exact adds, and multiplies by
/// constants computed with `Math.imul` semantics from 16-bit halves (never a JavaScript double product). No
/// `int64` is used. `nextFloat` is `Portable`.
///
/// Not cryptographic.
[<Struct>]
type Xoshiro128 =
    private
        { S0: int
          S1: int
          S2: int
          S3: int }

    /// The four state words.
    member Words: int * int * int * int

/// Operations on `Xoshiro128` states.
[<RequireQualifiedAccess>]
module Xoshiro128 =

    /// A state expanded from a 32-bit seed by a SplitMix-style expander: word k (k = 0..3) is
    /// `mix32 (seed + (k + 1) * 0x9E3779B9)`, where `mix32` is the MurmurHash3 32-bit finalizer
    /// (xor-shift 16, multiply 0x85EBCA6B, xor-shift 13, multiply 0xC2B2AE35, xor-shift 16). `mix32` is a
    /// bijection and the four inputs are distinct, so the state is never all zero; the guard in `ofState`
    /// still applies.
    ///
    /// SEED OVERLAP (a property of every SplitMix-style expander): word k of `seed s` equals word k+1 of
    /// `seed (s - 0x9E3779B9)`, so seeds that differ by a small multiple of 0x9E3779B9 share state words, in
    /// shifted positions. The streams still differ, but do not derive related streams by adding the golden
    /// constant to a seed. Derive independent streams with `split` (or `jump`), or mix distinct seeds first.
    val seed: seed: int -> Xoshiro128

    /// The state with these four words. Raises `System.ArgumentException` when all four are zero, the one
    /// state xoshiro cannot leave.
    val ofState: s0: int -> s1: int -> s2: int -> s3: int -> Xoshiro128

    /// The four state words, for saving and restoring with `ofState`.
    val state: rng: Xoshiro128 -> int * int * int * int

    /// The next 32 output bits (as a two's-complement `int32`) and the next state.
    val next: rng: Xoshiro128 -> struct (int * Xoshiro128)

    /// A uniform integer in [0, bound) and the next state, for 1 <= bound <= 2^31 - 1, by bitmask
    /// rejection on the high bits (no modulo bias). `bound = 1` returns 0 without drawing. Raises
    /// `System.ArgumentException` when `bound < 1`.
    val nextBelow: bound: int -> rng: Xoshiro128 -> struct (int * Xoshiro128)

    /// A uniform integer in [lo, hiExclusive) and the next state. Raises `System.ArgumentException` unless
    /// `lo < hiExclusive` and the width `hiExclusive - lo` fits in a positive `int32`.
    val nextInRange: lo: int -> hiExclusive: int -> rng: Xoshiro128 -> struct (int * Xoshiro128)

    /// `Portable`: a double in [0, 1) from the high 24 bits of one draw (`bits / 2^24`, exact).
    val nextFloat: rng: Xoshiro128 -> struct (float * Xoshiro128)

    /// Splits a stream in two: returns the parent's continuation and an independent child. The parent
    /// draws four outputs; child word k is `mix32 (output_k xor C_k)`, where C_0..C_3 are the first four
    /// SHA-256 initial hash words (0x6A09E667, 0xBB67AE85, 0x3C6EF372, 0xA54FF53A), so the
    /// child is a well-mixed fresh state rather than a shifted copy of the parent's stream. An all-zero
    /// child is replaced by `seed 0`. Deterministic: equal states split equally.
    val split: rng: Xoshiro128 -> struct (Xoshiro128 * Xoshiro128)

    /// Advances the state by 2^64 draws (the published xoshiro128 jump polynomial). Use it to hand out up to
    /// 2^64 non-overlapping subsequences of 2^64 draws each from one state.
    val jump: rng: Xoshiro128 -> Xoshiro128
