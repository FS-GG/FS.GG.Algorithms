namespace FS.GG.Algorithms.Numerics

// Layer 0 (ADR-0098 §3 rule 6). Raw Q16.16 arithmetic on int32 only. Every intermediate below is exact on
// .NET and under Fable: products are of 16-bit halves (magnitude below 2^32), sums stay below 2^33, and the
// one product that can exceed 2^31 (the unsigned low halves) is read only through masks and shifts, which see
// the same bits whether .NET wrapped it or JavaScript kept it exact. See Internal/Bits.fs for the hazard.

module internal FixedRaw =

    [<Literal>]
    let MaxRaw = 2147483647

    [<Literal>]
    let MinRaw = -2147483648

    let add (a: int) (b: int) =
        if b > 0 then (if a > MaxRaw - b then MaxRaw else a + b)
        elif b < 0 then (if a < MinRaw - b then MinRaw else a + b)
        else a

    let sub (a: int) (b: int) =
        if b > 0 then (if a < MinRaw + b then MinRaw else a - b)
        elif b < 0 then (if a > MaxRaw + b then MaxRaw else a - b)
        else a

    let neg (a: int) = if a = MinRaw then MaxRaw else -a

    /// floor((a * b + 2^15) / 2^16), saturated. With a = ah * 2^16 + al (ah signed, al in [0, 2^16)):
    ///   a * b = ah*bh * 2^32 + (ah*bl + al*bh) * 2^16 + al*bl
    /// so the rounded result is ah*bh * 2^16 + ah*bl + al*bh + floor((al*bl + 2^15) / 2^16), accumulated as a
    /// high word (multiples of 2^16) and a low word.
    let mul (a: int) (b: int) =
        let ah = a >>> 16
        let al = a &&& 0xFFFF
        let bh = b >>> 16
        let bl = b &&& 0xFFFF
        let hh = ah * bh // |hh| <= 2^30
        let m1 = ah * bl // |m1| < 2^31
        let m2 = al * bh // |m2| < 2^31
        let q = al * bl // in [0, 2^32): wraps on .NET, exact in JS; only its bits are read
        let qHi = (q >>> 16) &&& 0xFFFF
        let qLo = q &&& 0xFFFF
        let t0 = qHi + ((qLo + 0x8000) >>> 16) // floor((al*bl + 2^15) / 2^16), in [0, 2^16]
        let lo = (m1 &&& 0xFFFF) + (m2 &&& 0xFFFF) + t0 // in [0, 4 * 2^16)
        let hi = hh + (m1 >>> 16) + (m2 >>> 16) + (lo >>> 16) // |hi| < 2^30 + 2^17
        if hi > 32767 then MaxRaw
        elif hi < -32768 then MinRaw
        else (hi <<< 16) ||| (lo &&& 0xFFFF)

    /// truncate(a * 2^16 / b), saturated; the division-by-zero rules of the Fixed type.
    let div (a: int) (b: int) =
        if b = 0 then
            (if a > 0 then MaxRaw elif a < 0 then MinRaw else 0)
        else
            let negative = (a < 0) <> (b < 0)
            if b = MinRaw then
                // |b| = 2^31: the quotient is floor(|a| / 2^15), at most 2^16, never saturated.
                let m = if a = MinRaw then 65536 else (if a < 0 then -a else a) >>> 15
                if negative then -m else m
            else
                let d = if b < 0 then -b else b // in [1, 2^31 - 1]
                let aIsMin = (a = MinRaw)
                let am = if aIsMin then 0 else (if a < 0 then -a else a)
                // Restoring long division over the 48 dividend bits |a| << 16, most significant first.
                // Invariant: 0 <= r < d, so r + bit and r + r + bit (when below d) fit in an int32.
                let mutable r = 0
                let mutable q = 0
                let mutable state = 0 // 0 running, 1 saturated, 2 exactly 2^31 (negative result only)
                let mutable i = 47
                while state = 0 && i >= 0 do
                    let bit =
                        if i < 16 then 0
                        elif aIsMin then (if i = 47 then 1 else 0)
                        else (am >>> (i - 16)) &&& 1
                    let gap = d - r // in [1, d]
                    let qbit =
                        if r + bit >= gap then
                            r <- r + bit - gap
                            1
                        else
                            r <- r + r + bit
                            0
                    if q >= 0x40000000 then
                        // 2q + qbit >= 2^31: in range only as the final bit of a negative quotient of 2^31.
                        if negative && q = 0x40000000 && qbit = 0 && i = 0 then state <- 2 else state <- 1
                    else
                        q <- q + q + qbit
                    i <- i - 1
                if state = 1 then (if negative then MinRaw else MaxRaw)
                elif state = 2 then MinRaw
                elif negative then -q
                else q

    /// floor(sqrt(a * 2^16)) for a > 0, digit by digit over the 48-bit radicand; 0 for a <= 0.
    let sqrt (a: int) =
        if a <= 0 then
            0
        else
            let mutable res = 0 // < 2^24
            let mutable rem = 0 // <= 2 * res, so (rem <<< 2) < 2^27
            let mutable pair = 23
            while pair >= 0 do
                let hiBit = 2 * pair + 1
                let loBit = 2 * pair
                let b1 = if hiBit >= 16 then (a >>> (hiBit - 16)) &&& 1 else 0
                let b0 = if loBit >= 16 then (a >>> (loBit - 16)) &&& 1 else 0
                rem <- (rem <<< 2) ||| (b1 <<< 1) ||| b0
                let trial = (res <<< 2) ||| 1
                if rem >= trial then
                    rem <- rem - trial
                    res <- (res <<< 1) ||| 1
                else
                    res <- res <<< 1
                pair <- pair - 1
            res

    let ofInt (n: int) =
        if n > 32767 then MaxRaw
        elif n < -32768 then MinRaw
        else n <<< 16

[<Struct>]
type Fixed =
    { Raw: int }

    static member (+)(a: Fixed, b: Fixed) = { Raw = FixedRaw.add a.Raw b.Raw }
    static member (-)(a: Fixed, b: Fixed) = { Raw = FixedRaw.sub a.Raw b.Raw }
    static member (*)(a: Fixed, b: Fixed) = { Raw = FixedRaw.mul a.Raw b.Raw }
    static member (/)(a: Fixed, b: Fixed) = { Raw = FixedRaw.div a.Raw b.Raw }
    static member (~-)(a: Fixed) = { Raw = FixedRaw.neg a.Raw }
    static member Zero = { Raw = 0 }
    static member One = { Raw = 65536 }

[<RequireQualifiedAccess>]
module Fixed =

    [<Literal>]
    let FractionalBits = 16

    let zero = { Raw = 0 }
    let one = { Raw = 65536 }
    let half = { Raw = 32768 }
    let maxValue = { Raw = FixedRaw.MaxRaw }
    let minValue = { Raw = FixedRaw.MinRaw }
    let epsilon = { Raw = 1 }

    let ofRaw (raw: int) = { Raw = raw }
    let raw (x: Fixed) = x.Raw

    let ofInt (n: int) = { Raw = FixedRaw.ofInt n }
    let ofRatio (numerator: int) (denominator: int) = { Raw = FixedRaw.div numerator denominator }
    let toIntFloor (x: Fixed) = x.Raw >>> 16
    let toIntCeiling (x: Fixed) = (x.Raw >>> 16) + (if x.Raw &&& 0xFFFF <> 0 then 1 else 0)
    let toIntRound (x: Fixed) = (x.Raw >>> 16) + (if x.Raw &&& 0xFFFF >= 0x8000 then 1 else 0)

    let ofFloat (x: float) =
        if System.Double.IsNaN x then
            zero
        else
            let s = x * 65536.0
            if s >= 2147483647.0 then maxValue
            elif s <= -2147483648.0 then minValue
            else
                let f = System.Math.Floor s
                let r = if s - f >= 0.5 then f + 1.0 else f
                { Raw = int r }

    let toFloat (x: Fixed) = float x.Raw / 65536.0

    let add (a: Fixed) (b: Fixed) = { Raw = FixedRaw.add a.Raw b.Raw }
    let sub (a: Fixed) (b: Fixed) = { Raw = FixedRaw.sub a.Raw b.Raw }
    let mul (a: Fixed) (b: Fixed) = { Raw = FixedRaw.mul a.Raw b.Raw }
    let div (a: Fixed) (b: Fixed) = { Raw = FixedRaw.div a.Raw b.Raw }
    let neg (x: Fixed) = { Raw = FixedRaw.neg x.Raw }
    let abs (x: Fixed) = if x.Raw < 0 then { Raw = FixedRaw.neg x.Raw } else x
    let sqrt (x: Fixed) = { Raw = FixedRaw.sqrt x.Raw }
    let sign (x: Fixed) = if x.Raw > 0 then 1 elif x.Raw < 0 then -1 else 0
    let min (a: Fixed) (b: Fixed) = if b.Raw < a.Raw then b else a
    let max (a: Fixed) (b: Fixed) = if b.Raw > a.Raw then b else a
    let compare (a: Fixed) (b: Fixed) = if a.Raw < b.Raw then -1 elif a.Raw > b.Raw then 1 else 0
