// ADR-0098: the owner manifest is the closed file inventory consumed by SDD.
open System
open System.IO
open System.Text
open System.Security.Cryptography
open System.Text.Json

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let source = "template/product-skills/fs-gg-algorithms/"
let manifest = Path.Combine(root, "template/skill-manifest/skill-manifest.json")
let digest (bytes: byte[]) = SHA256.HashData bytes |> Convert.ToHexString |> _.ToLowerInvariant()
let canonicalBody (bytes: byte[]) =
    // SkillMirror's text domain; pack records themselves are already canonical LF UTF-8.
    let text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Replace("\r\n", "\n")
    Encoding.UTF8.GetBytes text

let files =
    Directory.GetFiles(Path.Combine(root, source), "*", SearchOption.AllDirectories)
    |> Array.map (fun path ->
        if File.GetAttributes(path).HasFlag FileAttributes.ReparsePoint then
            failwithf "skill-manifest: symbolic links are not owner bytes: %s" path
        {| path = Path.GetRelativePath(Path.Combine(root, source), path).Replace('\\', '/')
           sha256 = File.ReadAllBytes path |> canonicalBody |> digest |})
    |> Array.sortBy _.path

let skill = files |> Array.find (fun file -> file.path = "SKILL.md")
let row =
    {| id = "fs-gg-algorithms"
       scope = "product"
       sha256 = skill.sha256
       resolvablePath = ".agents/skills/fs-gg-algorithms/SKILL.md"
       ``materializes-when`` = "profile in [game, sample-pack] or template == fable-game"
       ``supplied-by`` = source
       files = files |}
let body = JsonSerializer.Serialize({| schemaVersion = 2; skills = [| row |] |}, JsonSerializerOptions(WriteIndented = true)) + "\n"

if Array.contains "--check" fsi.CommandLineArgs then
    if not (File.Exists manifest) || File.ReadAllText manifest <> body then
        eprintfn "skill-manifest: stale; run dotnet fsi scripts/generate-skill-manifest.fsx"
        exit 1
    printfn "skill-manifest: current (%d files)" files.Length
else
    Directory.CreateDirectory(Path.GetDirectoryName manifest) |> ignore
    File.WriteAllText(manifest, body, UTF8Encoding(false))
    printfn "skill-manifest: wrote %d files" files.Length
