namespace FS.GG.Algorithms.Internal

/// 32-bit integer primitives whose results are bit-identical on .NET and under Fable (JavaScript).
///
/// Why this module exists. Fable 5.18.0 compiles an int32 multiply `a * b` to `(a * b) | 0`. JavaScript
/// computes `a * b` as a double first, so a product whose magnitude exceeds 2^53 has already lost its low
/// bits before `| 0` truncates it, while .NET wraps the exact product. Measured on 2026-11-01:
/// -2048144789 * 123456789 is -1367848505 on .NET and -1367848512 under Node. A `uint32` multiply is
/// emitted as `(a >>> 0) * (b >>> 0)` and is not even reduced modulo 2^32.
///
/// The rule this library follows: every multiply whose exact product can reach 2^53 goes through
/// `imul`, which multiplies 16-bit halves so that no partial product reaches 2^32. Adds and subtracts are
/// emitted as `(a + b) | 0` and are exact, because the double sum of two int32 values is exact. F# `>>>`
/// on `int` is an arithmetic shift on both runtimes; `ushr` below is the logical shift.
///
/// This module sits beneath layer 0 (ADR-0098 §3 rule 6): it holds no algorithm, only exact word arithmetic.
module internal Bits =

    /// Logical (unsigned) right shift of the 32-bit pattern, for 0 <= n <= 31.
    let inline ushr (x: int) (n: int) : int =
        if n = 0 then x else (x >>> n) &&& ((1 <<< (32 - n)) - 1)

    /// Left rotation of the 32-bit pattern, for 1 <= k <= 31.
    let inline rotl (x: int) (k: int) : int = (x <<< k) ||| ushr x (32 - k)

    /// The low 32 bits of the product, as two's complement: JavaScript's `Math.imul` semantics.
    /// Each partial product is below 2^32 in magnitude, so it is exact as a double before `| 0`.
    let imul (a: int) (b: int) : int =
        let a0 = a &&& 0xFFFF
        let a1 = ushr a 16
        let b0 = b &&& 0xFFFF
        let b1 = ushr b 16
        let cross = ((a1 * b0 + a0 * b1) &&& 0xFFFF) <<< 16
        (a0 * b0) + cross
