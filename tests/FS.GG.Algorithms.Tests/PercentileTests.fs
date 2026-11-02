module FS.GG.Algorithms.Tests.PercentileTests

open System.Numerics
open Expecto
open FsCheck
open FsCheck.FSharp
open FS.GG.Algorithms.Numerics
open FS.GG.Algorithms.Tests.Support

/// The definition: r = max(1, ceil(num * n / den)), checked as (r - 1) * den < num * n <= r * den.
let private isNearestRank (num: int) (den: int) (n: int) (r: int) =
    let p = BigInteger num * BigInteger n
    if p.IsZero then r = 1
    else BigInteger(r - 1) * BigInteger den < p && p <= BigInteger r * BigInteger den

let tests =
    testList
        "Numerics.Percentile"
        [ testCase "exhaustive: every count 1..300, denominator 1..100 and numerator 0..denominator"
          <| fun () ->
              let mutable checkedCount = 0
              for n in 1..300 do
                  for den in 1..100 do
                      for num in 0..den do
                          let r = Percentile.rank num den n
                          if not (isNearestRank num den n r) then failtestf "rank %d/%d of %d = %d" num den n r
                          checkedCount <- checkedCount + 1
              Expect.equal checkedCount 1545000 "cases"

          property "rank is exact for large counts and denominators up to 46340 (BigInteger oracle)"
              (Arb.fromGen (gen {
                  let! den = Gen.choose (1, 46340)
                  let! num = Gen.choose (0, den)
                  let! n = Gen.choose (1, System.Int32.MaxValue)
                  return num, den, n }))
          <| fun (num, den, n) -> isNearestRank num den n (Percentile.rank num den n)

          property "percent equals the nearest-rank element of the sorted samples" (Arb.fromGen (Gen.zip (Gen.choose (0, 100)) (Gen.nonEmptyListOf (Gen.choose (-50, 50)))))
          <| fun (p, samples) ->
              let sorted = List.sort samples
              let r = max 1 (int (ceil (float (p * samples.Length) / 100.0))) // exact here: small integers
              Percentile.percent p samples = sorted.[r - 1]

          testCase "the double-formula pitfall is confined to the documented percents (counts 1..10^4)"
          <| fun () ->
              let affected =
                  [ for p in 1..99 do
                        let f = float p / 100.0
                        if Seq.exists (fun n -> int (ceil (f * float n)) <> Percentile.rank p 100 n) [ 1..10000 ] then
                            yield p ]
              Expect.equal affected [ 7; 14; 17; 27; 28; 34; 54; 55; 56; 67; 68; 81 ] "affected percents"

          testCase "the double-formula pitfall: ceil(0.07 * 100.0) is 8, the exact rank is 7"
          <| fun () ->
              Expect.equal (int (ceil (0.07 * 100.0))) 8 "the double formula over-ranks"
              Expect.equal (Percentile.rank 7 100 100) 7 "exact"
              Expect.equal (Percentile.percent 7 [ 1..100 ]) 7 "the 7th percentile of 1..100"

          testCase "edges: p0 is the minimum, p100 the maximum, one sample is every percentile"
          <| fun () ->
              Expect.equal (Percentile.percent 0 [ 5; 3; 9 ]) 3 "p0"
              Expect.equal (Percentile.percent 100 [ 5; 3; 9 ]) 9 "p100"
              Expect.equal (Percentile.percent 50 [ 4; 1; 3; 2 ]) 2 "p50 of four is the 2nd"
              Expect.equal (Percentile.percent 95 [ 42 ]) 42 "single"

          testCase "refusals"
          <| fun () ->
              Expect.isTrue (raises<System.ArgumentException> (fun () -> Percentile.rank 1 2 0 |> ignore)) "count 0"
              Expect.isTrue (raises<System.ArgumentException> (fun () -> Percentile.rank 3 2 5 |> ignore)) "num > den"
              Expect.isTrue (raises<System.ArgumentException> (fun () -> Percentile.rank 1 46341 5 |> ignore)) "den too large"
              Expect.isTrue (raises<System.ArgumentException> (fun () -> Percentile.percent 101 [ 1 ] |> ignore)) "percent"
              Expect.isTrue (raises<System.ArgumentException> (fun () -> Percentile.percent 50 ([]: int list) |> ignore)) "empty" ]
