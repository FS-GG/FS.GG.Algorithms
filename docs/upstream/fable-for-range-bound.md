# Draft upstream issue: the limit of a unit-step `for` loop is re-evaluated on every iteration

Draft for Root to file at <https://github.com/fable-compiler/Fable/issues>. It has a minimal repro and no
private context.

## Versions

- Fable 5.18.0 (`dotnet tool` fable 5.18.0), target JavaScript (`--lang javascript`), ES modules
- Node.js v26.11.1
- .NET SDK 10.0.403, FSharp.Core 10.1.401
- Linux x64

## Summary

F# evaluates the bounds of a `for` loop once, before the first iteration. For **unit-step loops in either
direction**, Fable puts the evaluated **limit** inside the JavaScript loop condition, so it is re-evaluated
before every iteration:

| F# loop | Fable 5.18.0 output | Limit evaluations (.NET / Node) |
|---|---|---|
| `for _ in 1 .. three ()` | `for (let i = 1; i <= three(); i++)` | 1 / 4 |
| `for _ in 5 .. -1 .. three ()` | `for (let i = 5; i >= three(); i--)` | 1 / 4 |
| `for i = 1 to three ()` | `for (let i = 1; i <= three(); i++)` | 1 / 4 |
| `for i = 5 downto three ()` | `for (let i = 5; i >= three(); i--)` | 1 / 4 |

These are **not** affected (each evaluates its bounds once, as on .NET):

- the **lower bound**: `for _ in three () .. 5` emits `for (let i = three(); ...)`;
- **general stepped ranges**: `for _ in 1 .. 2 .. three ()` iterates `rangeDouble(1, 2, three())`.

So a side-effecting limit runs more than once. More importantly, **the iteration count changes when the limit
reads state that the loop body mutates.**

## Repro 1: the iteration count changes

```fsharp
module P

[<EntryPoint>]
let main _ =
    let mutable remaining = 3
    let mutable iterations = 0
    for _ in 1 .. remaining do
        iterations <- iterations + 1
        remaining <- remaining - 1
    printfn "iterations=%d" iterations
    0
```

- .NET (`dotnet run`): `iterations=3`
- Fable, then Node: `iterations=2`

Generated JavaScript: `for (let forLoopVar = 1; forLoopVar <= remaining; forLoopVar++) {`

## Repro 2: the limit is evaluated repeatedly, in both directions

```fsharp
module P

let mutable calls = 0
let three () =
    calls <- calls + 1
    3

let count label (f: unit -> int) =
    calls <- 0
    let n = f ()
    printfn "%s: iterations=%d limitCalls=%d" label n calls

[<EntryPoint>]
let main _ =
    count "up" (fun () -> let mutable n = 0 in (for _ in 1 .. three () do n <- n + 1); n)
    count "down" (fun () -> let mutable n = 0 in (for _ in 5 .. -1 .. three () do n <- n + 1); n)
    count "lower" (fun () -> let mutable n = 0 in (for _ in three () .. 5 do n <- n + 1); n)
    count "stepped" (fun () -> let mutable n = 0 in (for _ in 1 .. 2 .. three () do n <- n + 1); n)
    0
```

| Line | .NET | Fable, then Node |
|---|---|---|
| `up` | iterations=3, limitCalls=1 | iterations=3, limitCalls=4 |
| `down` | iterations=3, limitCalls=1 | iterations=3, limitCalls=4 |
| `lower` | iterations=3, limitCalls=1 | the same |
| `stepped` | iterations=2, limitCalls=1 | the same |

## Expected

Evaluate the limit once into a temporary before the loop, as the F# specification and .NET do. For example:
`const limit = three(); for (let i = 1; i <= limit; i++)`.

## How we found it

A cross-runtime fixture decoder read a count in the loop header (`for _ in 1 .. reader.ReadUInt32() do`).
Under Node it consumed extra input and failed, while .NET was correct. The workaround is to bind the limit to
a local before the loop.
