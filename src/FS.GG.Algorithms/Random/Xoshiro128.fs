namespace FS.GG.Algorithms.Random

open FS.GG.Algorithms.Internal

// Layer 0 (ADR-0098 §3 rule 6). Implemented from the published description: D. Blackman and S. Vigna,
// "Scrambled Linear Pseudorandom Number Generators", ACM Transactions on Mathematical Software 47(4), 2021,
// doi:10.1145/3460772. Every multiply goes through Bits.imul (16-bit halves), never a JavaScript double product.

[<Struct>]
type Xoshiro128 =
    private
        { S0: int
          S1: int
          S2: int
          S3: int }

    member x.Words = x.S0, x.S1, x.S2, x.S3

[<RequireQualifiedAccess>]
module Xoshiro128 =

    let private mix32 (z0: int) =
        let z1 = z0 ^^^ Bits.ushr z0 16
        let z2 = Bits.imul z1 0x85EBCA6B
        let z3 = z2 ^^^ Bits.ushr z2 13
        let z4 = Bits.imul z3 0xC2B2AE35
        z4 ^^^ Bits.ushr z4 16

    let private golden = 0x9E3779B9

    let private expand (seed: int) =
        { S0 = mix32 (seed + golden)
          S1 = mix32 (seed + Bits.imul 2 golden)
          S2 = mix32 (seed + Bits.imul 3 golden)
          S3 = mix32 (seed + Bits.imul 4 golden) }

    let private isZero (s: Xoshiro128) =
        s.S0 = 0 && s.S1 = 0 && s.S2 = 0 && s.S3 = 0

    let seed (seed: int) =
        let s = expand seed
        // Unreachable (mix32 is a bijection over four distinct inputs) but kept as the stated guard.
        if isZero s then expand 0 else s

    let ofState (s0: int) (s1: int) (s2: int) (s3: int) =
        if s0 = 0 && s1 = 0 && s2 = 0 && s3 = 0 then
            invalidArg "s0" "Xoshiro128.ofState: the all-zero state is not a valid xoshiro state"
        { S0 = s0; S1 = s1; S2 = s2; S3 = s3 }

    let state (rng: Xoshiro128) = rng.S0, rng.S1, rng.S2, rng.S3

    let private step (s: Xoshiro128) =
        let t = s.S1 <<< 9
        let s2 = s.S2 ^^^ s.S0
        let s3 = s.S3 ^^^ s.S1
        let s1 = s.S1 ^^^ s2
        let s0 = s.S0 ^^^ s3
        { S0 = s0
          S1 = s1
          S2 = s2 ^^^ t
          S3 = Bits.rotl s3 11 }

    let next (rng: Xoshiro128) =
        let result = Bits.imul (Bits.rotl (Bits.imul rng.S1 5) 7) 9
        struct (result, step rng)

    let private bitLength (n: int) =
        // n >= 1
        let mutable k = 0
        let mutable v = n
        while v <> 0 do
            v <- v >>> 1
            k <- k + 1
        k

    let nextBelow (bound: int) (rng: Xoshiro128) =
        if bound < 1 then invalidArg "bound" "Xoshiro128.nextBelow: bound must be at least 1"
        if bound = 1 then
            struct (0, rng)
        else
            let k = bitLength (bound - 1) // 1..31
            let mutable s = rng
            let mutable v = bound
            while v >= bound do
                let struct (x, s') = next s
                v <- Bits.ushr x (32 - k)
                s <- s'
            struct (v, s)

    let nextInRange (lo: int) (hiExclusive: int) (rng: Xoshiro128) =
        if hiExclusive <= lo then invalidArg "hiExclusive" "Xoshiro128.nextInRange: lo must be below hiExclusive"
        if lo < 0 && hiExclusive > System.Int32.MaxValue + lo then
            invalidArg "hiExclusive" "Xoshiro128.nextInRange: the width hiExclusive - lo must fit in an int32"
        let struct (v, s) = nextBelow (hiExclusive - lo) rng
        struct (lo + v, s)

    let nextFloat (rng: Xoshiro128) =
        let struct (x, s) = next rng
        struct (float (Bits.ushr x 8) / 16777216.0, s)

    let private splitConstants = [| 0x6A09E667; 0xBB67AE85; 0x3C6EF372; 0xA54FF53A |]

    let split (rng: Xoshiro128) =
        let struct (o0, r1) = next rng
        let struct (o1, r2) = next r1
        let struct (o2, r3) = next r2
        let struct (o3, r4) = next r3
        let child =
            { S0 = mix32 (o0 ^^^ splitConstants.[0])
              S1 = mix32 (o1 ^^^ splitConstants.[1])
              S2 = mix32 (o2 ^^^ splitConstants.[2])
              S3 = mix32 (o3 ^^^ splitConstants.[3]) }
        struct (r4, (if isZero child then expand 0 else child))

    let private jumpPolynomial = [| 0x8764000B; 0xF542D2D3; 0x6FA035C3; 0x77F2DB5B |]

    let jump (rng: Xoshiro128) =
        let mutable a0 = 0
        let mutable a1 = 0
        let mutable a2 = 0
        let mutable a3 = 0
        let mutable cur = rng
        for i in 0..3 do
            let word = jumpPolynomial.[i]
            for b in 0..31 do
                if (word &&& (1 <<< b)) <> 0 then
                    a0 <- a0 ^^^ cur.S0
                    a1 <- a1 ^^^ cur.S1
                    a2 <- a2 ^^^ cur.S2
                    a3 <- a3 ^^^ cur.S3
                cur <- step cur
        { S0 = a0; S1 = a1; S2 = a2; S3 = a3 }
