/// Canonical little-endian encoding shared by both runtimes (fixture-schema.v1.json).
module FS.GG.Algorithms.Lockstep.Canonical

let private hexDigits = "0123456789abcdef"

/// An append-only byte buffer.
type Writer() =
    let bytes = ResizeArray<int>()
    member _.Count = bytes.Count
    member _.U8(v: int) = bytes.Add(v &&& 0xFF)
    member w.Bool(v: bool) = w.U8(if v then 1 else 0)
    member w.U16(v: int) =
        w.U8 v
        w.U8(v >>> 8)
    member w.I32(v: int) =
        w.U8 v
        w.U8(v >>> 8)
        w.U8(v >>> 16)
        w.U8(v >>> 24)
    /// An unsigned 32-bit count (always non-negative here).
    member w.U32(v: int) = w.I32 v
    member _.Bytes = bytes
    member w.Append(other: Writer) =
        for b in other.Bytes do
            w.U8 b
    member _.Hex() =
        let sb = System.Text.StringBuilder()
        for b in bytes do
            sb.Append(hexDigits.[b >>> 4]).Append(hexDigits.[b &&& 0xF]) |> ignore
        sb.ToString()

/// A cursor over decoded bytes.
type Reader(bytes: int[]) =
    let mutable pos = 0
    member _.AtEnd = pos >= bytes.Length
    member _.U8() =
        if pos >= bytes.Length then failwith "canonical: read past the end"
        let v = bytes.[pos]
        pos <- pos + 1
        v
    member r.Bool() = r.U8() <> 0
    member r.U16() =
        let b0 = r.U8()
        let b1 = r.U8()
        b0 ||| (b1 <<< 8)
    member r.I32() =
        let b0 = r.U8()
        let b1 = r.U8()
        let b2 = r.U8()
        let b3 = r.U8()
        b0 ||| (b1 <<< 8) ||| (b2 <<< 16) ||| (b3 <<< 24)
    member r.U32() = r.I32()
    member r.Bytes(n: int) = Array.init n (fun _ -> r.U8())

let private nibble (c: char) =
    if c >= '0' && c <= '9' then int c - int '0'
    elif c >= 'a' && c <= 'f' then int c - int 'a' + 10
    elif c >= 'A' && c <= 'F' then int c - int 'A' + 10
    else failwithf "canonical: not a hex digit: %c" c

/// Bytes (0..255 each) from a hex string.
let ofHex (hex: string) =
    if hex.Length % 2 <> 0 then failwith "canonical: odd hex length"
    Array.init (hex.Length / 2) (fun i -> (nibble hex.[2 * i] <<< 4) ||| nibble hex.[2 * i + 1])
