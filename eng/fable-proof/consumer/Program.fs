module Consumer

open FS.GG.Algorithms.Collections
open FS.GG.Algorithms.Numerics
open FS.GG.Algorithms.Random
open FS.GG.Algorithms.Graph

[<EntryPoint>]
let main _ =
    let h = BinaryHeap.create (fun (a: int * string) (b: int * string) -> compare (fst a) (fst b))
    for x in [ 1, "a"; 0, "b"; 1, "c"; 0, "d" ] do
        BinaryHeap.push x h
    let order = List.init 4 (fun _ -> snd (BinaryHeap.pop h)) |> String.concat ""
    let product = (Fixed.ofRaw -2048144789 * Fixed.ofRaw 123456789).Raw
    let quotient = (Fixed.minValue / Fixed.ofInt -1).Raw
    let root = (Fixed.sqrt Fixed.maxValue).Raw
    let struct (r0, _) = Xoshiro128.next (Xoshiro128.ofState 1 2 3 4)
    let struct (s0, _) = Xoshiro128.next (Xoshiro128.seed 20261101)
    let path =
        match (BestFirst.dijkstra (fun i -> if i < 9 then [ i + 1, 2 ] else []) compare (fun i -> i = 9) 0).Outcome with
        | Found(p, c) -> sprintf "%d/%d" (List.length p) c
        | _ -> "none"
    printfn "heap=%s mul=%d div=%d sqrt=%d xo=%d seed=%d path=%s" order product quotient root r0 s0 path
    0
