module FS.GG.Algorithms.Lockstep.Program

open FS.GG.Algorithms.Lockstep.Canonical

/// cases.bin layout: u32 caseCount, then per case u32 caseId, u16 operationId, u32 payloadLength, payload.
let private runFixtures (hex: string) =
    let r = Reader(ofHex hex)
    let count = r.U32()
    [ for _ in 1..count do
          let caseId = r.U32()
          let operation = r.U16()
          let length = r.U32()
          let payload = r.Bytes length
          yield (Operations.runCase caseId operation payload).Hex() ]

/// Modes:
///   fixtures HEX...   the cases.bin bytes as hex (split across arguments to respect per-argument limits);
///                     prints one hex line per canonical output record
///   stack             the 10^5 stack-safety checks; exit 1 on failure
[<EntryPoint>]
let main argv =
    if argv.Length >= 1 && argv.[0] = "fixtures" then
        let hex = String.concat "" (Array.toList argv.[1..])
        for line in runFixtures hex do
            printfn "%s" line
        0
    elif argv.Length = 1 && argv.[0] = "stack" then
        let lines, ok = StackSafety.run ()
        for line in lines do
            printfn "%s" line
        if ok then 0 else 1
    else
        eprintfn "usage: fixtures HEX... | stack"
        2
