module FS.GG.Algorithms.Tests.Support

open Expecto
open FsCheck
open FsCheck.FSharp

/// The number of generated cases per property. FSGG_ALGO_MAXTEST overrides it (for a longer local soak).
let maxTest =
    match System.Environment.GetEnvironmentVariable "FSGG_ALGO_MAXTEST" with
    | null
    | "" -> 300
    | s -> int s

/// FsCheck's own runner (`Config.QuickThrowOnFailure`) prints through `printf` to `Console.Out`, and it does so on
/// every generated case, even to print an empty string. Expecto replaces `Console.Out` with its own writer, whose
/// internal lock is taken in the opposite order to the .NET console's stdout lock: a thread writing to `Console.Out`
/// holds the console lock and waits for Expecto's, while Expecto's logger holds its lock and waits for the console's.
/// When a property fails, Expecto logs while the other parallel properties are still writing, and the run
/// deadlocks at near-zero CPU instead of failing (FS-GG/FS.GG.Algorithms#34). This runner never touches the console:
/// a passing property is silent, and a failing one raises FsCheck's own report (the shrunk input and replay seed).
let private quietThrowingRunner =
    { new IRunner with
        member _.OnStartFixture _ = ()
        member _.OnArguments(_, _, _) = ()
        member _.OnShrink(_, _) = ()

        member _.OnFinished(name, result) =
            match result with
            | TestResult.Passed _ -> ()
            | _ -> failwith (Runner.onFinishedToString name result) }

let private config n =
    Config.QuickThrowOnFailure.WithMaxTest(n).WithRunner quietThrowingRunner

/// An FsCheck property as an Expecto test: `maxTest` generated cases; a counterexample fails the test with
/// FsCheck's shrunk input and replay seed.
let property name (arb: Arbitrary<'a>) (body: 'a -> bool) =
    testCase name (fun () -> Check.One(config maxTest, Prop.forAll arb body))

/// The same with an explicit case count.
let propertyN name (n: int) (arb: Arbitrary<'a>) (body: 'a -> bool) =
    testCase name (fun () -> Check.One(config n, Prop.forAll arb body))

/// Raises unless `f` raises an exception of type 'E.
let raises<'E when 'E :> exn> (f: unit -> unit) =
    try
        f ()
        false
    with
    | :? 'E -> true
    | _ -> false
