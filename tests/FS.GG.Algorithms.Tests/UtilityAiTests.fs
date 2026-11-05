module FS.GG.Algorithms.Tests.UtilityAiTests

open Expecto
open FsCheck
open FsCheck.FSharp
open FS.GG.Algorithms.Numerics
open FS.GG.Algorithms.Decision
open FS.GG.Algorithms.Tests.Support

let private clamp x = max 0 (min 65536 x)
let private raw x = UtilityAi.value x |> Fixed.raw
// Independent exact integer formulas; no production Fixed arithmetic in these oracles.
let private multiply a b = int ((int64 a * int64 b + 32768L) / 65536L)
let private root x =
    let target = int64 x * 65536L
    let mutable lo, hi = 0, 65537
    while hi - lo > 1 do
        let mid = (lo + hi) / 2
        if int64 mid * int64 mid <= target then lo <- mid else hi <- mid
    lo
let private curves = [UtilityCurve.Linear; UtilityCurve.Quadratic; UtilityCurve.SquareRoot]
let private oracle curve x =
    let x = clamp x
    match curve with
    | UtilityCurve.Linear -> x
    | UtilityCurve.Quadratic -> multiply x x
    | UtilityCurve.SquareRoot -> root x
let private score x = UtilityAi.score (Fixed.ofRaw x)

let tests =
    testList "Decision.UtilityAi"
        [ property "score clamps full int32 range" (Arb.fromGen FixedTests.rawGen)
          <| fun x -> raw (score x) = clamp x
          property "responses equal independent exact integer formulas over int32" (Arb.fromGen FixedTests.rawGen)
          <| fun x -> curves |> List.forall (fun c -> raw (UtilityAi.evaluate c (Fixed.ofRaw x)) = oracle c x)
          property "complement exact and involutive over int32" (Arb.fromGen FixedTests.rawGen)
          <| fun x -> raw (UtilityAi.invert (score x)) = 65536 - clamp x && UtilityAi.invert (UtilityAi.invert (score x)) = score x
          property "ordered product equals independent fold, bounded by each factor" (Arb.fromGen (Gen.listOf FixedTests.rawGen))
          <| fun xs ->
              let result = raw (UtilityAi.product (List.map score xs))
              result = List.fold (fun acc x -> multiply acc (clamp x)) 65536 xs
              && result >= 0 && result <= 65536 && (xs |> List.forall (fun x -> result <= clamp x))
          property "choose equals independent descending stable ordering, including ties" (Arb.fromGen (Gen.listOf (Gen.choose (-2, 8))))
          <| fun xs ->
              let actions = xs |> List.mapi (fun i x -> i, score (x * 16384))
              let expected = actions |> List.sortBy (fun (i, s) -> -raw s, i) |> List.tryHead
              UtilityAi.choose actions = expected
          testCase "all 65537 inputs: oracle, range, endpoints and increasing/decreasing monotonicity"
          <| fun () ->
              for curve in curves do
                  let mutable previous = -1
                  let mutable previousInverse = 65537
                  for x in 0..65536 do
                      let actual = UtilityAi.evaluate curve (Fixed.ofRaw x)
                      let y = raw actual
                      let inverse = raw (UtilityAi.invert actual)
                      Expect.equal y (oracle curve x) "exact response"
                      Expect.isTrue (y >= previous && y <= 65536 && inverse <= previousInverse && inverse >= 0) "bounded monotone responses"
                      previous <- y
                      previousInverse <- inverse
                  Expect.equal (raw (UtilityAi.evaluate curve Fixed.zero)) 0 "zero endpoint"
                  Expect.equal previous 65536 "one endpoint"
          testCase "empty, veto, first tie and input order rounding boundaries"
          <| fun () ->
              Expect.equal (raw (UtilityAi.product [])) 65536 "empty product"
              Expect.equal (UtilityAi.choose ([]: (int * UtilityScore) list)) None "no actions"
              Expect.equal (raw (UtilityAi.product [score 65536; score 0; score 65536])) 0 "veto"
              Expect.equal (UtilityAi.choose ["first", score 0; "second", score 0]) (Some("first", score 0)) "first zero tie"
              // A half-step multiply rounds upward and cannot be replaced with truncation.
              Expect.equal (raw (UtilityAi.product [score 1; score 32768])) 1 "tie up"
              Expect.equal (UtilityAi.choose ["low", score 1; "high", score 65536; "tie", score 65536]) (Some("high",score 65536)) "strict greater"
          testCase "100000 considerations/actions are iterative and preserve stable ordering"
          <| fun () ->
              let ones = List.replicate 100000 (score 65536)
              Expect.equal (raw (UtilityAi.product ones)) 65536 "large product"
              let actions = List.init 100000 (fun i -> i, score 65536)
              Expect.equal (UtilityAi.choose actions) (Some(0,score 65536)) "large tie" ]
