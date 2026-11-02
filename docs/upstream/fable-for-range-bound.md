# Draft upstream issue: `for ... in a .. b do` re-evaluates the upper bound on every iteration

Draft for Root to file at <https://github.com/fable-compiler/Fable/issues>. It has a minimal repro and no
private context.

## Versions

- Fable 5.18.0 (`dotnet tool` fable 5.18.0), target JavaScript (`--lang javascript`), ES modules
- Node.js v26.11.1
- .NET SDK 10.0.403, FSharp.Core 10.1.401
- Linux x64

## Summary

F# evaluates the bounds of a `for i in a .. b do` loop once, before the first iteration. Fable compiles the
loop to `for (let i = a; i <= b; i++)`, so JavaScript re-evaluates `b` before every iteration. When `b` has a
side effect, or depends on state the body changes, the program behaves differently:

- a side-effecting bound runs more than once;
- **the iteration count changes when the bound reads state that the loop body mutates.**

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

Generated JavaScript:

```js
for (let forLoopVar = 1; forLoopVar <= remaining; forLoopVar++) {
```

## Repro 2: a side-effecting bound is evaluated repeatedly

```fsharp
module P

let mutable calls = 0
let next () =
    calls <- calls + 1
    3

[<EntryPoint>]
let main _ =
    let mutable iterations = 0
    for _ in 1 .. next () do
        iterations <- iterations + 1
    printfn "iterations=%d boundCalls=%d" iterations calls
    0
```

- .NET: `iterations=3 boundCalls=1`
- Fable, then Node: `iterations=3 boundCalls=4`

Generated JavaScript: `for (let forLoopVar = 1; forLoopVar <= next(); forLoopVar++) {`

## Expected

Bind the evaluated bound to a temporary once, before the loop (as the F# specification and .NET do), for
example `const n = next(); for (let i = 1; i <= n; i++)`. The lower bound and any step expression likely
need the same treatment.

## How we found it

A cross-runtime fixture decoder read a count inside the loop header (`for _ in 1 .. reader.ReadUInt32() do`).
Under Node it consumed extra input and failed, while .NET was correct. The workaround is to bind the bound
to a local before the loop.
