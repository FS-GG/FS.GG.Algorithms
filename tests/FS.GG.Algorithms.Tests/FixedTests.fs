module FS.GG.Algorithms.Tests.FixedTests

open Expecto
open FsCheck
open FsCheck.FSharp
open FS.GG.Algorithms.Numerics
open FS.GG.Algorithms.Tests.Support

/// Raw values weighted toward the places arithmetic breaks: zero, ±1, the extremes, exact integers, the
/// rounding boundary 0x8000, powers of two and their neighbours, and uniform int32.
let rawGen =
    let pow2 = Gen.map2 (fun k d -> (1 <<< k) + d) (Gen.choose (0, 30)) (Gen.choose (-2, 2))
    Gen.frequency
        [ 3, Gen.elements [ 0; 1; -1; 65536; -65536; 32768; -32768; 0x8000; 0x7FFF; 0x8001
                            System.Int32.MaxValue; System.Int32.MinValue; System.Int32.MaxValue - 1
                            System.Int32.MinValue + 1; 0x10000; 0xFFFF; -0x10000 ]
          3, Gen.map (fun n -> n <<< 16) (Gen.choose (-32768, 32767))
          3, Gen.map2 (fun n f -> (n <<< 16) ||| f) (Gen.choose (-200, 200)) (Gen.elements [ 0; 0x8000; 0x7FFF; 0x8001; 1; 0xFFFF ])
          3, Gen.oneof [ pow2; Gen.map (fun x -> -x) pow2 ]
          2, Gen.choose (-65536 * 4, 65536 * 4)
          6, Gen.map2 (fun (hi: int) (lo: int) -> (hi <<< 16) ||| (lo &&& 0xFFFF)) (Gen.choose (-32768, 32767)) (Gen.choose (0, 65535)) ]

let private pair = Arb.fromGen (Gen.zip rawGen rawGen)
let private one = Arb.fromGen rawGen
let private f = Fixed.ofRaw

let tests =
    testList
        "Numerics.Fixed (Q16.16)"
        [ property "add equals the exact sum, saturated" pair
          <| fun (a, b) -> (Fixed.add (f a) (f b)).Raw = Oracles.fixedAdd a b && (f a + f b).Raw = Oracles.fixedAdd a b

          property "sub equals the exact difference, saturated" pair
          <| fun (a, b) -> (Fixed.sub (f a) (f b)).Raw = Oracles.fixedSub a b && (f a - f b).Raw = Oracles.fixedSub a b

          property "mul equals floor((a*b + 2^15) / 2^16) over exact integers, saturated" pair
          <| fun (a, b) -> (Fixed.mul (f a) (f b)).Raw = Oracles.fixedMul a b && (f a * f b).Raw = Oracles.fixedMul a b

          property "mul error is at most half a step against the exact rational product (in range)" pair
          <| fun (a, b) ->
              let exact = System.Numerics.BigInteger a * System.Numerics.BigInteger b // value * 2^32
              let r = (Fixed.mul (f a) (f b)).Raw
              let saturated = r = System.Int32.MaxValue || r = System.Int32.MinValue
              // |r * 2^16 - exact| <= 2^15
              saturated
              || System.Numerics.BigInteger.Abs(System.Numerics.BigInteger r * System.Numerics.BigInteger 65536 - exact)
                 <= System.Numerics.BigInteger 32768

          property "mul is commutative" pair <| fun (a, b) -> Fixed.mul (f a) (f b) = Fixed.mul (f b) (f a)

          property "div equals truncate(a * 2^16 / b) over exact integers, saturated, with the zero rules" pair
          <| fun (a, b) -> (Fixed.div (f a) (f b)).Raw = Oracles.fixedDiv a b && (f a / f b).Raw = Oracles.fixedDiv a b

          property "div by zero: MaxValue for x > 0, MinValue for x < 0, 0 for 0" one
          <| fun a ->
              let r = (Fixed.div (f a) Fixed.zero).Raw
              r = (if a > 0 then System.Int32.MaxValue elif a < 0 then System.Int32.MinValue else 0)

          property "ofRatio n d equals truncate(n * 2^16 / d) for any int32 pair" pair
          <| fun (n, d) -> (Fixed.ofRatio n d).Raw = Oracles.fixedDiv n d

          property "sqrt equals floor(sqrt(a * 2^16)); 0 for negatives" one
          <| fun a -> (Fixed.sqrt (f a)).Raw = Oracles.fixedSqrt a

          property "sqrt is monotone" pair
          <| fun (a, b) ->
              let lo, hi = min a b, max a b
              (Fixed.sqrt (f lo)).Raw <= (Fixed.sqrt (f hi)).Raw

          property "neg and abs saturate only at MinValue" one
          <| fun a ->
              let n = (Fixed.neg (f a)).Raw
              let ab = (Fixed.abs (f a)).Raw
              n = Oracles.clamp (-(System.Numerics.BigInteger a))
              && ab = Oracles.clamp (System.Numerics.BigInteger.Abs(System.Numerics.BigInteger a))
              && (-(f a)).Raw = n

          property "ofInt saturates outside -32768..32767 and round-trips inside" (Arb.fromGen (Gen.choose (-70000, 70000)))
          <| fun n ->
              let x = Fixed.ofInt n
              if n > 32767 then x = Fixed.maxValue
              elif n < -32768 then x = Fixed.minValue
              else x.Raw = n * 65536 && Fixed.toIntFloor x = n && Fixed.toIntCeiling x = n && Fixed.toIntRound x = n

          property "toIntFloor, toIntCeiling and toIntRound agree with exact double arithmetic" one
          <| fun a ->
              let v = float a / 65536.0 // exact
              Fixed.toIntFloor (f a) = int (floor v)
              && Fixed.toIntCeiling (f a) = int (ceil v)
              && Fixed.toIntRound (f a) = int (floor (v + 0.5))

          property "toFloat is exact and ofFloat round-trips it (Portable)" one
          <| fun a -> Fixed.ofFloat (Fixed.toFloat (f a)) = f a

          property "ofFloat is within half a step for in-range doubles (Portable)" (Arb.fromGen (Gen.map (fun (x: int) -> float x / 1000.0) (Gen.choose (-32000000, 32000000))))
          <| fun x -> abs (Fixed.toFloat (Fixed.ofFloat x) - x) <= 0.5 / 65536.0

          property "compare, min and max order by the raw value" pair
          <| fun (a, b) ->
              sign (Fixed.compare (f a) (f b)) = sign (compare a b)
              && (Fixed.min (f a) (f b)).Raw = min a b
              && (Fixed.max (f a) (f b)).Raw = max a b
              && compare (f a) (f b) = compare a b

          testCase "ADR-0098 §3 edge-case vectors"
          <| fun () ->
              let mx = System.Int32.MaxValue
              let mn = System.Int32.MinValue
              let cases =
                  [ "1 * 1", (Fixed.mul Fixed.one Fixed.one).Raw, 65536
                    "-1 * -1", (Fixed.mul (Fixed.ofInt -1) (Fixed.ofInt -1)).Raw, 65536
                    "eps * eps rounds to 0", (Fixed.mul Fixed.epsilon Fixed.epsilon).Raw, 0
                    "0.5 * eps: tie rounds up", (Fixed.mul Fixed.half Fixed.epsilon).Raw, 1
                    "-0.5 * eps: tie rounds up (toward +inf)", (Fixed.mul (Fixed.neg Fixed.half) Fixed.epsilon).Raw, 0
                    "max * max saturates", (Fixed.mul Fixed.maxValue Fixed.maxValue).Raw, mx
                    "min * min saturates", (Fixed.mul Fixed.minValue Fixed.minValue).Raw, mx
                    "min * max saturates low", (Fixed.mul Fixed.minValue Fixed.maxValue).Raw, mn
                    "min * 1 is exact", (Fixed.mul Fixed.minValue Fixed.one).Raw, mn
                    "max + eps saturates", (Fixed.add Fixed.maxValue Fixed.epsilon).Raw, mx
                    "min - eps saturates", (Fixed.sub Fixed.minValue Fixed.epsilon).Raw, mn
                    "1 / 0", (Fixed.div Fixed.one Fixed.zero).Raw, mx
                    "-1 / 0", (Fixed.div (Fixed.ofInt -1) Fixed.zero).Raw, mn
                    "0 / 0", (Fixed.div Fixed.zero Fixed.zero).Raw, 0
                    "min / -1 saturates", (Fixed.div Fixed.minValue (Fixed.ofInt -1)).Raw, mx
                    "min / 1 is exactly min (representable)", (Fixed.div Fixed.minValue Fixed.one).Raw, mn
                    "-16384 / 0.5 is exactly min", (Fixed.div (Fixed.ofInt -16384) Fixed.half).Raw, mn
                    "16384 / 0.5 saturates", (Fixed.div (Fixed.ofInt 16384) Fixed.half).Raw, mx
                    "min / eps saturates", (Fixed.div Fixed.minValue Fixed.epsilon).Raw, mn
                    "min / min = 1", (Fixed.div Fixed.minValue Fixed.minValue).Raw, 65536
                    "1 / 3 truncates", (Fixed.div Fixed.one (Fixed.ofInt 3)).Raw, 21845
                    "-1 / 3 truncates toward zero", (Fixed.div (Fixed.ofInt -1) (Fixed.ofInt 3)).Raw, -21845
                    "sqrt 0", (Fixed.sqrt Fixed.zero).Raw, 0
                    "sqrt raw 1", (Fixed.sqrt Fixed.epsilon).Raw, 256
                    "sqrt max", (Fixed.sqrt Fixed.maxValue).Raw, 11863283
                    "sqrt 4 = 2", (Fixed.sqrt (Fixed.ofInt 4)).Raw, 131072
                    "sqrt -1 = 0", (Fixed.sqrt (Fixed.ofInt -1)).Raw, 0
                    "neg min saturates", (Fixed.neg Fixed.minValue).Raw, mx
                    "ofRatio 1 3", (Fixed.ofRatio 1 3).Raw, 21845
                    "ofRatio min -1 saturates", (Fixed.ofRatio mn -1).Raw, mx ]
              for name, actual, expected in cases do
                  Expect.equal actual expected name ]
