module FS.GG.Algorithms.Bench.Program

open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Configs
open BenchmarkDotNet.Diagnosers
open BenchmarkDotNet.Exporters.Json
open BenchmarkDotNet.Jobs
open BenchmarkDotNet.Running

/// One benchmark per declared (kernel, size): "kernel@size".
type Kernels() =
    let mutable run: unit -> int = fun () -> 0

    static member Cases =
        [| for (id, sizes, _) in Workloads.kernels do
               for size in sizes do
                   yield sprintf "%s@%d" id size |]

    [<ParamsSource("Cases")>]
    member val Case = "" with get, set

    [<GlobalSetup>]
    member this.Setup() =
        let id, size =
            match this.Case.Split('@') with
            | [| id; size |] -> id, int size
            | _ -> failwithf "bad case %s" this.Case
        let _, _, prepare = Workloads.kernels |> List.find (fun (k, _, _) -> k = id)
        run <- prepare size

    [<Benchmark>]
    member _.Run() = run ()

/// The declared job (ADR-0098 §4.3): default toolchain, one launch, 3 warm-up and 10 measured iterations,
/// MemoryDiagnoser. `--smoke` swaps in BenchmarkDotNet's Dry job to prove the harness, not to measure.
let private config (artifacts: string) (smoke: bool) =
    let job =
        if smoke then
            Job.Dry
        else
            Job.Default.WithLaunchCount(1).WithWarmupCount(3).WithIterationCount(10)
    ManualConfig
        .CreateEmpty()
        .AddJob(job)
        .AddDiagnoser(MemoryDiagnoser.Default)
        .AddExporter(JsonExporter.Full)
        .AddLogger(BenchmarkDotNet.Loggers.ConsoleLogger.Default)
        .AddColumnProvider(BenchmarkDotNet.Columns.DefaultColumnProviders.Instance)
        .WithArtifactsPath(artifacts)

/// Usage:
///   workcounts OUT.json            run each declared case once and write its work count
///   run ARTIFACTS [--smoke] [--filter GLOB]
[<EntryPoint>]
let main argv =
    match List.ofArray argv with
    | [ "workcounts"; out ] ->
        let rows =
            [ for (id, sizes, prepare) in Workloads.kernels do
                  for size in sizes do
                      yield sprintf "    {\"kernel\": \"%s\", \"size\": %d, \"workCount\": %d}" id size (prepare size ()) ]
        System.IO.File.WriteAllText(out, "[\n" + String.concat ",\n" rows + "\n]\n")
        0
    | "run" :: artifacts :: rest ->
        let smoke = List.contains "--smoke" rest
        let passThrough = rest |> List.filter (fun a -> a <> "--smoke") |> Array.ofList
        BenchmarkSwitcher.FromTypes([| typeof<Kernels> |]).Run(Array.append [| "--filter"; "*" |] passThrough, config artifacts smoke)
        |> ignore
        0
    | _ ->
        eprintfn "usage: workcounts OUT.json | run ARTIFACTS [--smoke]"
        2
