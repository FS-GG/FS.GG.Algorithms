module FS.GG.Algorithms.Tests.Main

open Expecto
open FsCheck.FSharp

/// The CI negative control (FS-GG/FS.GG.Algorithms#34). With FSGG_ALGO_NEGATIVE_CONTROL=<n> set, n properties that
/// can never hold join the suite, so the run must report exactly n failures and exit non-zero. CI runs this beside
/// the normal step and requires it to finish within a bound: a failing property has to fail the run, not hang it.
/// The default run, with the variable unset, is unchanged.
let private negativeControl =
    match System.Environment.GetEnvironmentVariable "FSGG_ALGO_NEGATIVE_CONTROL" with
    | null
    | "" -> []
    | n ->
        [ testList
              "negative control"
              [ for i in 1 .. int n ->
                    Support.property (sprintf "failing property %d" i) (ArbMap.defaults |> ArbMap.arbitrary<int>) (fun x ->
                        x < 0 && x > 0) ] ]

[<EntryPoint>]
let main argv =
    testList
        "FS.GG.Algorithms"
        ([ HeapTests.tests
           FixedTests.tests
           XoshiroTests.tests
           BestFirstTests.tests
           PercentileTests.tests
           SccTests.tests
           UnionFindTests.tests
           TopoSortTests.tests
           ArbitrationTests.tests
           BehaviourTreeTests.tests
           KdTreeTests.tests
           DoorGridTests.tests ]
         @ negativeControl)
    |> runTestsWithCLIArgs [] argv
