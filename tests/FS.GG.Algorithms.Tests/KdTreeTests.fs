module FS.GG.Algorithms.Tests.KdTreeTests

open Expecto
open FsCheck
open FsCheck.FSharp
open FS.GG.Algorithms.Spatial
open FS.GG.Algorithms.Tests.Support

/// Points in a small window (many exact ties and duplicates) or across the whole coordinate range.
let private coord small = if small then Gen.choose (-6, 6) else Gen.choose (KdTree.MinCoordinate, KdTree.MaxCoordinate)

let private case =
    gen {
        let! small = Gen.elements [ true; true; false ]
        let! pts = Gen.listOf (Gen.zip (coord small) (coord small)) |> Gen.map (List.truncate 60)
        let! qx = coord small
        let! qy = coord small
        return pts |> List.mapi (fun i (x, y) -> x, y, i * 7), (qx, qy)
    }
    |> Arb.fromGen

/// The brute-force oracle: every point with its key (d2, x, y, index), sorted.
let private byKey (pts: (int * int * int) list) (qx: int, qy: int) =
    pts
    |> List.mapi (fun i (x, y, v) -> (x - qx) * (x - qx) + (y - qy) * (y - qy), x, y, i, v)
    |> List.sort

let private asTuple (p: KdPoint<int>) = p.X, p.Y, p.Index, p.Value

let tests =
    testList
        "Spatial.KdTree"
        [ property "nearest equals a brute-force scan by (distance^2, X, Y, Index)" case
          <| fun (pts, q) ->
              let t = KdTree.build pts
              let expected = byKey pts q |> List.tryHead |> Option.map (fun (_, x, y, i, v) -> x, y, i, v)
              (KdTree.nearest (fst q) (snd q) t |> Option.map asTuple) = expected

          property "kNearest equals the first k of the brute-force order, for every k" case
          <| fun (pts, q) ->
              let t = KdTree.build pts
              let all = byKey pts q |> List.map (fun (_, x, y, i, v) -> x, y, i, v)
              [ 0; 1; 2; 5; pts.Length; pts.Length + 3 ]
              |> List.forall (fun k -> (KdTree.kNearest k (fst q) (snd q) t |> List.map asTuple) = List.truncate k all)

          property "withinRadius equals the brute-force filter, boundary included, nearest first"
              (Arb.fromGen (Gen.zip case.Generator (Gen.choose (0, 12))))
          <| fun ((pts, q), r) ->
              let t = KdTree.build pts
              let expected = byKey pts q |> List.filter (fun (d, _, _, _, _) -> d <= r * r) |> List.map (fun (_, x, y, i, v) -> x, y, i, v)
              (KdTree.withinRadius r (fst q) (snd q) t |> List.map asTuple) = expected

          property "inRectangle equals the brute-force filter ordered by (X, Y, Index)"
              (Arb.fromGen (Gen.zip case.Generator (Gen.zip (Gen.choose (-8, 8)) (Gen.choose (-8, 8)))))
          <| fun ((pts, (qx, qy)), (w, h)) ->
              let t = KdTree.build pts
              let x1, x2 = min qx (qx + w), max qx (qx + w)
              let y1, y2 = min qy (qy + h), max qy (qy + h)
              let expected =
                  pts
                  |> List.mapi (fun i (x, y, v) -> x, y, i, v)
                  |> List.filter (fun (x, y, _, _) -> x >= x1 && x <= x2 && y >= y1 && y <= y2)
                  |> List.sort
              (KdTree.inRectangle x1 y1 x2 y2 t |> List.map asTuple) = expected

          testCase "extremes: corner-to-corner distance fits in int32; the range is enforced"
          <| fun () ->
              let lo, hi = KdTree.MinCoordinate, KdTree.MaxCoordinate
              let t = KdTree.build [ lo, lo, "a"; hi, hi, "b" ]
              Expect.equal (KdTree.distanceSquared lo lo hi hi) (2 * 32766 * 32766) "max distance"
              Expect.equal ((KdTree.nearest hi (hi - 1) t).Value.Value) "b" "nearest"
              Expect.equal (KdTree.withinRadius 46340 lo lo t |> List.length) 2 "radius covers the diagonal"
              Expect.isTrue (raises<System.ArgumentException> (fun () -> KdTree.build [ hi + 1, 0, "x" ] |> ignore)) "point range"
              Expect.isTrue (raises<System.ArgumentException> (fun () -> KdTree.nearest (lo - 1) 0 t |> ignore)) "query range"
              Expect.isTrue (raises<System.ArgumentException> (fun () -> KdTree.withinRadius 46341 0 0 t |> ignore)) "radius"
              Expect.isTrue (raises<System.ArgumentException> (fun () -> KdTree.kNearest -1 0 0 t |> ignore)) "k"
              Expect.isTrue (raises<System.ArgumentException> (fun () -> KdTree.distanceSquared (hi + 1) 0 0 0 |> ignore)) "distanceSquared range"
              Expect.equal (KdTree.nearest 0 0 (KdTree.build ([]: (int * int * int) list))) None "empty"

          testCase "Stack safety: 10^5 collinear points and 10^5 duplicates"
          <| fun () ->
              let n = 100000
              let line = KdTree.build [ for i in 0 .. n - 1 -> i % 32767 - 16383, 5, i ]
              Expect.equal (KdTree.nearest 0 5 line |> Option.map (fun p -> p.X)) (Some 0) "collinear"
              let dup = KdTree.build [ for i in 0 .. n - 1 -> 3, 3, i ]
              Expect.equal (KdTree.nearest 0 0 dup |> Option.map (fun p -> p.Index)) (Some 0) "lowest index wins a full tie"
              Expect.equal (KdTree.kNearest 3 0 0 dup |> List.map (fun p -> p.Index)) [ 0; 1; 2 ] "k ties by index" ]
