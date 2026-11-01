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

/// An FsCheck property as an Expecto test: `maxTest` generated cases; a counterexample fails the test with
/// FsCheck's shrunk input and replay seed.
let property name (arb: Arbitrary<'a>) (body: 'a -> bool) =
    testCase name (fun () -> Check.One(Config.QuickThrowOnFailure.WithMaxTest(maxTest), Prop.forAll arb body))

/// The same with an explicit case count.
let propertyN name (n: int) (arb: Arbitrary<'a>) (body: 'a -> bool) =
    testCase name (fun () -> Check.One(Config.QuickThrowOnFailure.WithMaxTest(n), Prop.forAll arb body))

/// Raises unless `f` raises an exception of type 'E.
let raises<'E when 'E :> exn> (f: unit -> unit) =
    try
        f ()
        false
    with
    | :? 'E -> true
    | _ -> false
