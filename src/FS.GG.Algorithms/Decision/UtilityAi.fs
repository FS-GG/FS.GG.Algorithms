namespace FS.GG.Algorithms.Decision

open FS.GG.Algorithms.Numerics

[<Struct>]
type UtilityScore = private UtilityScore of Fixed

[<RequireQualifiedAccess>]
type UtilityCurve =
    | Linear
    | Quadratic
    | SquareRoot

[<RequireQualifiedAccess>]
module UtilityAi =
    let score value = UtilityScore(Fixed.max Fixed.zero (Fixed.min Fixed.one value))
    let value (UtilityScore value) = value
    let evaluate curve input =
        let x = value (score input)
        match curve with
        | UtilityCurve.Linear -> UtilityScore x
        | UtilityCurve.Quadratic -> UtilityScore(Fixed.mul x x)
        | UtilityCurve.SquareRoot -> UtilityScore(Fixed.sqrt x)
    let invert (UtilityScore x) = UtilityScore(Fixed.sub Fixed.one x)
    let product (considerations: UtilityScore list) =
        let mutable result = Fixed.one
        let mutable remaining = considerations
        while not remaining.IsEmpty do
            result <- Fixed.mul result (value remaining.Head)
            remaining <- remaining.Tail
        UtilityScore result
    let choose (actions: ('Action * UtilityScore) list) =
        let mutable best = None
        let mutable remaining = actions
        while not remaining.IsEmpty do
            let candidate = remaining.Head
            match best with
            | None -> best <- Some candidate
            | Some(_, current) when value (snd candidate) > value current -> best <- Some candidate
            | _ -> ()
            remaining <- remaining.Tail
        best
