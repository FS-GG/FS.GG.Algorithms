namespace FS.GG.Algorithms.Numerics

/// A Q16.16 fixed-point number: a two's-complement `int32` raw value with 16 fractional bits, so the value
/// is `Raw / 65536`. The range is -32768 to 32767.99998474, and the smallest step is 1/65536.
///
/// SEMANTICS (ADR-0098 §3 F2). Every operation below is `LockstepExact` (bit-identical on .NET and under
/// Fable/JavaScript) except the floating-point conversions, which are `Portable`.
///
/// - No `int64` anywhere: `int64` is a BigInt under Fable. Multiply and divide split each operand into a
///   signed high half and an unsigned low 16-bit half, and every intermediate stays exact on both runtimes.
/// - Overflow saturates to `MaxValue` or `MinValue` for add, subtract, negate, abs, multiply and divide. It
///   never wraps.
/// - Multiply rounds to nearest, ties upward (toward +infinity): `floor((a * b + 0x8000) / 2^16)` on raw
///   values, computed with two-word cross-product accumulation.
/// - Divide rounds toward zero: `truncate(a * 2^16 / b)` on raw values, by long division over the 48-bit
///   dividend. It saturates only when the exact quotient is outside the range; a quotient of exactly -32768
///   (raw -2^31) is representable and returned.
/// - `x / 0` is `MaxValue` for `x > 0`, `MinValue` for `x < 0`, and `0` for `x = 0`.
///   `MinValue / -1` saturates to `MaxValue`.
/// - `sqrt` is bitwise (digit by digit) and exact to the floor; the square root of a negative value is `0`.
///
/// RANGE WARNING. A squared distance overflows (saturates) above about 181.02 units (the square root of
/// 32768). Compare squared distances in scaled units, or keep worlds within that radius.
///
/// Equality and comparison are those of the raw value, so `compare` orders numerically.
[<Struct>]
type Fixed =
    { /// The raw two's-complement representation: value * 65536.
      Raw: int }

    /// Saturating addition.
    static member (+): a: Fixed * b: Fixed -> Fixed
    /// Saturating subtraction.
    static member (-): a: Fixed * b: Fixed -> Fixed
    /// Saturating multiplication, rounded to nearest with ties upward.
    static member (*): a: Fixed * b: Fixed -> Fixed
    /// Saturating division, rounded toward zero; see the type's division-by-zero rules.
    static member (/): a: Fixed * b: Fixed -> Fixed
    /// Saturating negation: `-MinValue` is `MaxValue`.
    static member (~-): a: Fixed -> Fixed
    /// 0.
    static member Zero: Fixed
    /// 1.0 (raw 65536).
    static member One: Fixed

/// Operations on Q16.16 `Fixed` values. All `LockstepExact` unless marked `Portable`.
[<RequireQualifiedAccess>]
module Fixed =

    /// The number of fractional bits: 16.
    [<Literal>]
    val FractionalBits: int = 16

    /// 0.
    val zero: Fixed
    /// 1.0 (raw 65536).
    val one: Fixed
    /// 0.5 (raw 32768).
    val half: Fixed
    /// The largest value, 32767.99998474 (raw 2^31 - 1).
    val maxValue: Fixed
    /// The smallest value, -32768 (raw -2^31).
    val minValue: Fixed
    /// The smallest positive step, 1/65536 (raw 1).
    val epsilon: Fixed

    /// The value with this raw representation.
    val ofRaw: raw: int -> Fixed
    /// The raw representation.
    val raw: x: Fixed -> int

    /// The integer `n`, saturating outside -32768..32767.
    val ofInt: n: int -> Fixed
    /// The exact quotient `numerator / denominator` rounded toward zero, saturating outside the range.
    /// Division by zero follows the division rules. `ofRatio n d` equals `ofInt n / ofInt d` whenever both
    /// integers are in range, and is exact for any `int32` pair.
    val ofRatio: numerator: int -> denominator: int -> Fixed
    /// The greatest integer not above `x`.
    val toIntFloor: x: Fixed -> int
    /// The least integer not below `x`.
    val toIntCeiling: x: Fixed -> int
    /// `x` rounded to the nearest integer, ties upward.
    val toIntRound: x: Fixed -> int

    /// `Portable`: the nearest value to the double `x` (ties upward), saturating; NaN is 0.
    val ofFloat: x: float -> Fixed
    /// `Portable`: the double equal to `x` (exact, since every Q16.16 value is a double).
    val toFloat: x: Fixed -> float

    /// Saturating addition.
    val add: a: Fixed -> b: Fixed -> Fixed
    /// Saturating subtraction `a - b`.
    val sub: a: Fixed -> b: Fixed -> Fixed
    /// Saturating multiplication, rounded to nearest with ties upward.
    val mul: a: Fixed -> b: Fixed -> Fixed
    /// Saturating division `a / b`, rounded toward zero.
    val div: a: Fixed -> b: Fixed -> Fixed
    /// Saturating negation.
    val neg: x: Fixed -> Fixed
    /// Saturating absolute value: `abs MinValue` is `MaxValue`.
    val abs: x: Fixed -> Fixed
    /// The square root, exact to the floor; `0` for a negative argument.
    val sqrt: x: Fixed -> Fixed
    /// -1, 0 or 1.
    val sign: x: Fixed -> int
    /// The smaller of two values.
    val min: a: Fixed -> b: Fixed -> Fixed
    /// The larger of two values.
    val max: a: Fixed -> b: Fixed -> Fixed
    /// Numeric comparison: negative, zero or positive.
    val compare: a: Fixed -> b: Fixed -> int
