module FS.GG.Algorithms.Tests.UnionFindTests

open Expecto
open FsCheck
open FsCheck.FSharp
open FS.GG.Algorithms.Graph
open FS.GG.Algorithms.Tests.Oracles
open FS.GG.Algorithms.Tests.Support

let private unions =
    gen {
        let! n = Gen.choose (0, 30)
        let! edges = if n = 0 then Gen.constant [] else Gen.listOf (Gen.zip (Gen.choose (0, n - 1)) (Gen.choose (0, n - 1)))
        return n, edges
    }
    |> Arb.fromGen

let tests =
    testList
        "Graph.UnionFind"
        [ property "sets equal BFS components (groups by smallest member, members ascending)" unions
          <| fun (n, edges) ->
              let uf = UnionFind.create n
              for (a, b) in edges do
                  UnionFind.union a b uf |> ignore
              UnionFind.sets uf = bfsComponents n edges && UnionFind.count uf = (bfsComponents n edges).Length

          property "representative is the smallest member, whatever the union order" unions
          <| fun (n, edges) ->
              let uf = UnionFind.create n
              for (a, b) in List.rev edges do
                  UnionFind.union a b uf |> ignore
              bfsComponents n edges
              |> List.forall (fun g -> g |> List.forall (fun v -> UnionFind.representative v uf = List.head g))

          property "union reports whether two sets merged; connected agrees with the oracle" unions
          <| fun (n, edges) ->
              let uf = UnionFind.create n
              let mutable ok = true
              let mutable seen = []
              for (a, b) in edges do
                  let before = bfsComponents n seen
                  let separate = before |> List.exists (fun g -> List.contains a g && not (List.contains b g))
                  if UnionFind.union a b uf <> separate then ok <- false
                  seen <- (a, b) :: seen
              let comps = bfsComponents n edges
              ok
              && Seq.forall
                  (fun (a, b) -> UnionFind.connected a b uf = (comps |> List.exists (fun g -> List.contains a g && List.contains b g)))
                  [ for a in 0 .. n - 1 do for b in 0 .. n - 1 do yield a, b ]

          property "partition by a conflict predicate equals BFS components of the conflict graph" (Arb.fromGen (Gen.listOf (Gen.choose (0, 40))))
          <| fun items ->
              // Conflict: values within 2 of each other.
              let conflicts (a: int) (b: int) = abs (a - b) <= 2
              let arr = Array.ofList items
              let edges = [ for i in 0 .. arr.Length - 1 do for j in i + 1 .. arr.Length - 1 do if conflicts arr.[i] arr.[j] then yield i, j ]
              let expected = bfsComponents arr.Length edges |> List.map (List.map (fun i -> arr.[i]))
              UnionFind.partition conflicts items = expected

          testCase "refusals and the empty forest"
          <| fun () ->
              Expect.isTrue (raises<System.ArgumentException> (fun () -> UnionFind.create -1 |> ignore)) "negative n"
              let uf = UnionFind.create 3
              Expect.isTrue (raises<System.ArgumentException> (fun () -> UnionFind.representative 3 uf |> ignore)) "out of range"
              Expect.equal (UnionFind.sets (UnionFind.create 0)) [] "empty"

          testCase "Stack safety: 10^5 chained unions (iterative find and compression)"
          <| fun () ->
              let n = 100000
              let uf = UnionFind.create n
              for i in n - 1 .. -1 .. 1 do
                  UnionFind.union i (i - 1) uf |> ignore
              Expect.equal (UnionFind.count uf) 1 "one set"
              Expect.equal (UnionFind.representative (n - 1) uf) 0 "smallest member" ]
