module FS.GG.Algorithms.Tests.SccTests

open Expecto
open FsCheck
open FsCheck.FSharp
open FS.GG.Algorithms.Graph
open FS.GG.Algorithms.Tests.Oracles
open FS.GG.Algorithms.Tests.Support

type DigraphCase = { N: int; Succ: int list[] }

let private digraph =
    gen {
        let! n = Gen.choose (1, 16)
        let! density = Gen.choose (0, 30)
        let! lists =
            Gen.arrayOfLength n (Gen.listOf (Gen.zip (Gen.choose (0, n - 1)) (Gen.choose (0, 99)))
                                 |> Gen.map (List.filter (fun (_, r) -> r < density * 3) >> List.map fst))
        return { N = n; Succ = lists }
    }
    |> Arb.fromGen

let private nodes c = [ 0 .. c.N - 1 ]
let private succ c v = c.Succ.[v]

let tests =
    testList
        "Graph.Scc"
        [ property "iterative Tarjan equals the recursive version exactly (components, order and member order)" digraph
          <| fun c -> Scc.tarjan (nodes c) (succ c) = recursiveTarjan c.N (succ c)

          property "components equal the reachability closure: u and v share one iff each reaches the other" digraph
          <| fun c ->
              let reach = reachability c.N (succ c)
              let comp = Scc.componentOf (nodes c) (succ c) |> Array.ofList
              Seq.forall
                  (fun (u, v) -> (comp.[u] = comp.[v]) = (reach.[u].[v] && reach.[v].[u]))
                  [ for u in 0 .. c.N - 1 do for v in 0 .. c.N - 1 do yield u, v ]

          property "components are a partition and come in reverse topological order of the condensation" digraph
          <| fun c ->
              let comps = Scc.tarjan (nodes c) (succ c)
              let comp = Scc.componentOf (nodes c) (succ c) |> Array.ofList
              List.sort (List.concat comps) = nodes c
              && Seq.forall
                  (fun (u, v) -> comp.[u] = comp.[v] || comp.[u] > comp.[v])
                  [ for u in 0 .. c.N - 1 do for v in succ c u do yield u, v ]

          property "cycles are exactly the components with more than one node or a self-edge" digraph
          <| fun c ->
              let expected =
                  Scc.tarjan (nodes c) (succ c)
                  |> List.filter (fun comp -> comp.Length > 1 || List.contains comp.Head (succ c comp.Head))
              Scc.cycles (nodes c) (succ c) = expected

          property "an acyclic graph (edges only to higher ids) has no cycles and only singletons" digraph
          <| fun c ->
              let dag v = c.Succ.[v] |> List.filter (fun w -> w > v)
              Scc.cycles (nodes c) dag = [] && Scc.tarjan (nodes c) dag |> List.forall (fun comp -> comp.Length = 1)

          testCase "duplicates keep the first occurrence; edges outside the node set are ignored"
          <| fun () ->
              let succ v = match v with | "a" -> [ "b"; "zz" ] | "b" -> [ "a" ] | _ -> []
              Expect.equal (Scc.tarjan [ "a"; "b"; "a"; "c" ] succ) [ [ "a"; "b" ]; [ "c" ] ] "components"
              Expect.equal (Scc.cycles [ "a"; "b"; "c" ] succ) [ [ "a"; "b" ] ] "cycles"
              Expect.equal (Scc.componentOf [ "a"; "b"; "c"; "a" ] succ) [ 0; 0; 1; 0 ] "componentOf"

          testCase "Stack safety: a 10^5-node chain and a 10^5-node ring"
          <| fun () ->
              let n = 100000
              let chain = Scc.tarjan [ 0 .. n - 1 ] (fun v -> if v < n - 1 then [ v + 1 ] else [])
              Expect.equal chain.Length n "chain: n singletons"
              Expect.equal chain.Head [ n - 1 ] "chain: the sink completes first"
              let ring = Scc.cycles [ 0 .. n - 1 ] (fun v -> [ (v + 1) % n ])
              Expect.equal (ring |> List.map List.length) [ n ] "ring: one component of n" ]
