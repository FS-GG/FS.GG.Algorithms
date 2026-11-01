module FS.GG.Algorithms.Tests.XoshiroTests

open Expecto
open FsCheck
open FsCheck.FSharp
open FS.GG.Algorithms.Random
open FS.GG.Algorithms.Tests.Support

let private draws (n: int) (rng: Xoshiro128) =
    let mutable s = rng
    [ for _ in 1..n do
          let struct (x, s') = Xoshiro128.next s
          s <- s'
          yield x ]

let private stateGen =
    Gen.map4 (fun a b c d -> a, b, c, d) (Gen.choose (System.Int32.MinValue, System.Int32.MaxValue))
        (Gen.choose (System.Int32.MinValue, System.Int32.MaxValue))
        (Gen.choose (System.Int32.MinValue, System.Int32.MaxValue))
        (Gen.choose (System.Int32.MinValue, System.Int32.MaxValue))
    |> Gen.filter (fun (a, b, c, d) -> a <> 0 || b <> 0 || c <> 0 || d <> 0)
    |> Gen.map (fun (a, b, c, d) -> Xoshiro128.ofState a b c d)

let private states = Arb.fromGen stateGen
let private seeds = Arb.fromGen (Gen.choose (System.Int32.MinValue, System.Int32.MaxValue))

/// Two-word windows (consecutive output pairs) of a stream: equal windows in two streams mean overlap.
let private windows (xs: int list) = xs |> List.pairwise |> Set.ofList

let tests =
    testList
        "Random.Xoshiro128"
        [ testCase "Published reference vector: state [1, 2, 3, 4] (rand_xoshiro Xoshiro128StarStar)"
          <| fun () ->
              let expected =
                  [ 11520u; 0u; 5927040u; 70819200u; 2031721883u; 1637235492u; 1287239034u; 3734860849u
                    3729100597u; 4258142804u ]
              let actual = draws 10 (Xoshiro128.ofState 1 2 3 4) |> List.map uint32
              Expect.equal actual expected "first ten outputs"

          testCase "ofState refuses the all-zero state"
          <| fun () ->
              Expect.isTrue (raises<System.ArgumentException> (fun () -> Xoshiro128.ofState 0 0 0 0 |> ignore)) "zero"

          property "seed is deterministic, never all zero, and state round-trips through ofState" seeds
          <| fun seed ->
              let s = Xoshiro128.seed seed
              let (a, b, c, d) = Xoshiro128.state s
              (a, b, c, d) <> (0, 0, 0, 0)
              && Xoshiro128.seed seed = s
              && Xoshiro128.ofState a b c d = s
              && draws 8 s = draws 8 (Xoshiro128.ofState a b c d)

          property "distinct seeds give distinct states" (Arb.fromGen (Gen.zip (Gen.choose (-100000, 100000)) (Gen.choose (-100000, 100000))))
          <| fun (x, y) -> x = y || Xoshiro128.seed x <> Xoshiro128.seed y

          property "jump commutes with next (both are powers of the same linear map)" states
          <| fun s ->
              let struct (_, s1) = Xoshiro128.next s
              Xoshiro128.jump s1 = (let struct (_, j1) = Xoshiro128.next (Xoshiro128.jump s) in j1)

          property "jump is linear over GF(2): jump (a xor b) = jump a xor jump b" (Arb.fromGen (Gen.zip stateGen stateGen))
          <| fun (a, b) ->
              let (a0, a1, a2, a3) = Xoshiro128.state a
              let (b0, b1, b2, b3) = Xoshiro128.state b
              (a0 ^^^ b0, a1 ^^^ b1, a2 ^^^ b2, a3 ^^^ b3) = (0, 0, 0, 0)
              || (let x = Xoshiro128.ofState (a0 ^^^ b0) (a1 ^^^ b1) (a2 ^^^ b2) (a3 ^^^ b3)
                  let (j0, j1, j2, j3) = Xoshiro128.state (Xoshiro128.jump a)
                  let (k0, k1, k2, k3) = Xoshiro128.state (Xoshiro128.jump b)
                  Xoshiro128.state (Xoshiro128.jump x) = (j0 ^^^ k0, j1 ^^^ k1, j2 ^^^ k2, j3 ^^^ k3))

          property "nextBelow stays in [0, bound) and a power-of-two bound takes the top bits of one draw"
              (Arb.fromGen (Gen.zip stateGen (Gen.choose (1, System.Int32.MaxValue))))
          <| fun (s, bound) ->
              let struct (v, s') = Xoshiro128.nextBelow bound s
              let inRange = v >= 0 && v < bound
              let pow2 = bound &&& (bound - 1) = 0
              let topBits =
                  not pow2 || bound = 1
                  || (let struct (x, s1) = Xoshiro128.next s
                      let k = System.Numerics.BitOperations.Log2(uint32 bound)
                      s1 = s' && v = int (uint32 x >>> (32 - k)))
              inRange && topBits

          property "nextInRange stays in [lo, hi)"
              (Arb.fromGen (Gen.zip3 stateGen (Gen.choose (-1000000, 1000000)) (Gen.choose (1, 1000000))))
          <| fun (s, lo, width) ->
              let struct (v, _) = Xoshiro128.nextInRange lo (lo + width) s
              v >= lo && v < lo + width

          testCase "nextBelow and nextInRange refusals"
          <| fun () ->
              let s = Xoshiro128.seed 1
              Expect.isTrue (raises<System.ArgumentException> (fun () -> Xoshiro128.nextBelow 0 s |> ignore)) "bound 0"
              Expect.isTrue (raises<System.ArgumentException> (fun () -> Xoshiro128.nextInRange 5 5 s |> ignore)) "empty"
              Expect.isTrue
                  (raises<System.ArgumentException> (fun () ->
                      Xoshiro128.nextInRange System.Int32.MinValue System.Int32.MaxValue s |> ignore))
                  "too wide"

          testCase "nextBelow 6 is uniform: chi-square over 60000 draws from a fixed seed"
          <| fun () ->
              let counts = Array.zeroCreate 6
              let mutable s = Xoshiro128.seed 20261101
              for _ in 1..60000 do
                  let struct (v, s') = Xoshiro128.nextBelow 6 s
                  counts.[v] <- counts.[v] + 1
                  s <- s'
              let chi = counts |> Array.sumBy (fun c -> let d = float c - 10000.0 in d * d / 10000.0)
              // 5 degrees of freedom: p = 0.001 at 20.5.
              Expect.isLessThan chi 20.5 (sprintf "chi-square %f, counts %A" chi counts)

          property "nextFloat is in [0, 1) (Portable)" states
          <| fun s ->
              let struct (x, _) = Xoshiro128.nextFloat s
              x >= 0.0 && x < 1.0

          property "split: the child differs from the parent and their streams share no two-word window" seeds
          <| fun seed ->
              let struct (parent, child) = Xoshiro128.split (Xoshiro128.seed seed)
              child <> parent
              && Set.intersect (windows (draws 256 parent)) (windows (draws 256 child)) |> Set.isEmpty

          property "split tree of depth 4: 16 leaves with pairwise distinct states and no shared window" seeds
          <| fun seed ->
              let rec leaves depth s =
                  if depth = 0 then
                      [ s ]
                  else
                      let struct (a, b) = Xoshiro128.split s
                      leaves (depth - 1) a @ leaves (depth - 1) b
              let ls = leaves 4 (Xoshiro128.seed seed)
              let ws = ls |> List.map (draws 64 >> windows)
              List.distinct ls |> List.length = 16
              && Set.count (Set.unionMany ws) = List.sumBy Set.count ws

          testCase "split: parent and child outputs are uncorrelated bitwise (xor balance over 4000 seeds)"
          <| fun () ->
              let mutable ones = 0L
              let mutable bits = 0L
              for seed in 1..4000 do
                  let struct (p, c) = Xoshiro128.split (Xoshiro128.seed seed)
                  for (x, y) in List.zip (draws 8 p) (draws 8 c) do
                      ones <- ones + int64 (System.Numerics.BitOperations.PopCount(uint32 (x ^^^ y)))
                      bits <- bits + 32L
              let ratio = float ones / float bits
              // 1,024,000 bits: a fair coin stays within 0.5 ± 0.003 at about six standard deviations.
              Expect.isTrue (abs (ratio - 0.5) < 0.003) (sprintf "xor one-bit ratio %f" ratio)

          property "split is deterministic" states
          <| fun s -> Xoshiro128.split s = Xoshiro128.split s ]
