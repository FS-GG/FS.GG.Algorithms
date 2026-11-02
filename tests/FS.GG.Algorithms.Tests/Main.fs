module FS.GG.Algorithms.Tests.Main

open Expecto

[<EntryPoint>]
let main argv =
    testList
        "FS.GG.Algorithms"
        [ HeapTests.tests
          FixedTests.tests
          XoshiroTests.tests
          BestFirstTests.tests
          PercentileTests.tests
          SccTests.tests
          UnionFindTests.tests
          TopoSortTests.tests
          ArbitrationTests.tests
          BehaviourTreeTests.tests ]
    |> runTestsWithCLIArgs [] argv
