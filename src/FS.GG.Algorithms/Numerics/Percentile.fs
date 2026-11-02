namespace FS.GG.Algorithms.Numerics

// Clean re-implementation of the nearest-rank percentile (harvest 20, ADR-0098 §11), with the rank made exact.
// Provenance: FS-GG/FS.GG.Governance src/FS.GG.Governance.Adapters.SddHandoff/Performance.fs `nearestRank`,
// which uses a double rank; its own 0.95 and 0.99 calls are unaffected by the pitfall noted in Percentile.fsi.
// Reference: the nearest-rank method, as in NIST/SEMATECH e-Handbook of Statistical Methods §7.2.6.2.

[<RequireQualifiedAccess>]
module Percentile =

    let rank (numerator: int) (denominator: int) (count: int) =
        if count < 1 then invalidArg "count" "Percentile.rank: count must be at least 1"
        if denominator < 1 || denominator > 46340 then
            invalidArg "denominator" "Percentile.rank: denominator must be in 1..46340"
        if numerator < 0 || numerator > denominator then
            invalidArg "numerator" "Percentile.rank: numerator must be in 0..denominator"
        // ceil(num * count / den) = (count / den) * num + ceil((count % den) * num / den); every term fits int32:
        // (count / den) * num <= count, and (count % den) * num < den^2 <= 46340^2 < 2^31.
        let whole = (count / denominator) * numerator
        let part = (count % denominator) * numerator
        let r = whole + (part + denominator - 1) / denominator
        if r < 1 then 1 else r

    let ofSorted (numerator: int) (denominator: int) (sorted: 'T[]) =
        if sorted.Length = 0 then invalidArg "sorted" "Percentile.ofSorted: no samples"
        sorted.[rank numerator denominator sorted.Length - 1]

    let percent (percent: int) (samples: 'T list) =
        if List.isEmpty samples then invalidArg "samples" "Percentile.percent: no samples"
        if percent < 0 || percent > 100 then invalidArg "percent" "Percentile.percent: percent must be in 0..100"
        ofSorted percent 100 (samples |> List.sort |> Array.ofList)
