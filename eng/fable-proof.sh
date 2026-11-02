#!/usr/bin/env bash
# Fable proof for FS.GG.Algorithms (ADR-0069; ADR-0098 §3 and §4.2).
#
#   eng/fable-proof.sh [WORKDIR]      default WORKDIR: ${TMPDIR:-/tmp}/fsgg-algorithms-fable-proof
#
#  1. The input corpus is current (scripts/lockstep-fixtures.py check) and both fixture digests equal the ones
#     recorded in src/FS.GG.Algorithms/Fable/fixture-schema.v1.json.
#  2. expected.bin passes the independent Python oracle.
#  3. tests/FS.GG.Algorithms.Lockstep runs on .NET and, compiled by the pinned Fable, under Node. Both print
#     the canonical records as hex; the two outputs must be identical and must equal expected.bin.
#  4. The 10^5 stack-safety checks pass on .NET and under Node's DEFAULT stack (no --stack-size), and a Node
#     canary shows a naive 10^5 recursion does overflow there.
#  5. The library is packed; the nupkg depends on FSharp.Core only and carries fable/ with the project file and
#     every Compile item, with no Condition attribute (Fable ignores conditions in a packed project). A clean
#     consumer outside the repository, restoring only that package, prints the same line on .NET and under Fable.
# Needs: the pinned .NET SDK (global.json), node, python3 and nuget.org. WORKDIR is disposable; nothing is committed.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
work="${1:-${TMPDIR:-/tmp}/fsgg-algorithms-fable-proof}"
fixtures="$root/tests/FS.GG.Algorithms.Lockstep/fixtures/v1"
schema="$root/src/FS.GG.Algorithms/Fable/fixture-schema.v1.json"
case "$(cd / && realpath -m "$work")" in
  / | "$HOME" | "$root" | "$root"/*) echo "fable-proof: refusing WORKDIR $work (it is deleted first)" >&2; exit 2 ;;
esac
rm -rf "$work"
mkdir -p "$work"
command -v node >/dev/null || { echo "fable-proof: node is required" >&2; exit 2; }
strip() { sed 's/\x1b\[[0-9;]*m//g'; }
hexargs() { python3 -I -c 'import sys,textwrap; d=open(sys.argv[1],"rb").read().hex(); print("\n".join(textwrap.wrap(d,100000)))' "$1"; }

# shellcheck source=eng/fable-compile.sh
. "$root/eng/fable-compile.sh"
( cd "$root" && dotnet tool restore >/dev/null )
fable_version="$( (cd "$root" && dotnet fable --version) | strip | head -1)"
echo "fable: $fable_version; node: $(node --version); dotnet: $(dotnet --version)"
[ "$fable_version" = "5.26.0" ] || { echo "fable-proof: expected the pinned Fable 5.26.0" >&2; exit 1; }

echo "== 0. negative control: the gate must reject code Fable cannot compile"
negative="$work/negative-control"
mkdir -p "$negative"
cat >"$negative/Negative.fsproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Library</OutputType></PropertyGroup>
  <ItemGroup><Compile Include="Negative.fs" /></ItemGroup>
  <ItemGroup><ProjectReference Include="$root/src/FS.GG.Algorithms/FS.GG.Algorithms.fsproj" /></ItemGroup>
</Project>
XML
cat >"$negative/Negative.fs" <<'FS'
module Negative
// Deliberately not Fable-compatible: System.Threading is unsupported (ADR-0098 §3 rule 4).
let pause () = System.Threading.Thread.Sleep 1
let heap () = FS.GG.Algorithms.Collections.BinaryHeap.create compare<int>
FS
printf '<Project />\n' >"$negative/Directory.Build.props"
printf '<Project />\n' >"$negative/Directory.Packages.props"
if fable_compile "$root" "$work/negative-control.log" "$negative/Negative.fsproj" --lang javascript --outDir "$work/negative-js" >/dev/null 2>&1; then
  echo "fable-proof: FAILED, the negative control compiled: the Fable gate cannot fail" >&2
  exit 1
fi
echo "fable-proof: negative control rejected by the gate, as required ($(grep -c 'error FABLE' "$work/negative-control.log") Fable error(s))"

echo "== 1. corpus and digests"
python3 -I "$root/scripts/lockstep-fixtures.py" check
python3 -I - "$schema" "$fixtures" <<'PY'
import hashlib, json, sys
schema = json.load(open(sys.argv[1]))
for key, name in (("inputsSha256", "cases.bin"), ("oracleSha256", "expected.bin")):
    actual = hashlib.sha256(open(f"{sys.argv[2]}/{name}", "rb").read()).hexdigest()
    if actual != schema[key]:
        sys.exit(f"fable-proof: {name} sha256 {actual} differs from fixture-schema {key} {schema[key]}")
    print(f"fable-proof: {name} sha256 {actual} matches fixture-schema.v1.json")
PY

echo "== 2. independent oracle"
python3 -I "$root/scripts/lockstep-fixtures.py" oracle "$fixtures/expected.bin"

echo "== 3. .NET and Fable/Node canonical output"
run_step "$work/build-lockstep.log" dotnet build "$root/tests/FS.GG.Algorithms.Lockstep" -c Release --nologo -v q
dotnet run --project "$root/tests/FS.GG.Algorithms.Lockstep" -c Release --no-build -- fixtures $(hexargs "$fixtures/cases.bin") >"$work/dotnet.hex"
fable_compile "$root" "$work/fable-lockstep.log" "$root/tests/FS.GG.Algorithms.Lockstep" --lang javascript --outDir "$work/lockstep-js"
printf '{"type":"module"}\n' >"$work/lockstep-js/package.json"
node "$work/lockstep-js/Program.js" fixtures $(hexargs "$fixtures/cases.bin") >"$work/node.hex"
python3 -I "$root/scripts/lockstep-fixtures.py" hex-to-bin "$work/dotnet.hex" "$work/dotnet.bin"
python3 -I "$root/scripts/lockstep-fixtures.py" hex-to-bin "$work/node.hex" "$work/node.bin"
cmp "$work/dotnet.bin" "$work/node.bin" || { echo "fable-proof: FAILED, .NET and Node disagree" >&2; exit 1; }
cmp "$work/node.bin" "$fixtures/expected.bin" || { echo "fable-proof: FAILED, output differs from expected.bin" >&2; exit 1; }
echo "fable-proof: $(wc -l <"$work/node.hex") records, $(wc -c <"$work/node.bin") bytes, identical on .NET and Node and equal to expected.bin (sha256 $(sha256sum "$work/node.bin" | cut -c1-16)...)"

echo "== 4. stack safety (10^5)"
dotnet run --project "$root/tests/FS.GG.Algorithms.Lockstep" -c Release --no-build -- stack | sed 's/^/dotnet /'
node "$work/lockstep-js/Program.js" stack | sed 's/^/node   /'

echo "== 5. packed package and clean Fable consumer"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/src/FS.GG.Algorithms/FS.GG.Algorithms.fsproj")"
run_step "$work/pack.log" dotnet pack "$root/src/FS.GG.Algorithms" -c Release -o "$work/feed" --nologo -v q
nupkg="$work/feed/FS.GG.Algorithms.$version.nupkg"
python3 -I - "$nupkg" "$root/src/FS.GG.Algorithms/FS.GG.Algorithms.fsproj" <<'PY'
import re, sys, zipfile
import xml.etree.ElementTree as ET
z = zipfile.ZipFile(sys.argv[1])
names = set(z.namelist())
nuspec = next(n for n in names if n.endswith(".nuspec"))
deps = sorted(set(re.findall(r'<dependency id="([^"]+)"', z.read(nuspec).decode())))
if deps != ["FSharp.Core"]:
    sys.exit(f"fable-proof: package dependencies {deps}, expected FSharp.Core only")
proj = ET.parse(sys.argv[2]).getroot()
compiles = [e.get("Include") for e in proj.iter("Compile")]
missing = [c for c in compiles if f"fable/{c}" not in names]
if "fable/FS.GG.Algorithms.fsproj" not in names or missing:
    sys.exit(f"fable-proof: fable/ view incomplete; missing {missing}")
packed = ET.fromstring(z.read("fable/FS.GG.Algorithms.fsproj"))
conditioned = [e.tag for e in packed.iter() if "Condition" in e.attrib]
if conditioned:
    sys.exit(f"fable-proof: packed fable/FS.GG.Algorithms.fsproj carries Condition attributes on {conditioned}")
print(f"fable-proof: nupkg depends on {deps}; fable/ holds the project and {len(compiles)} sources, no Condition")
PY
consumer="$work/consumer"
cp -r "$root/eng/fable-proof/consumer" "$consumer"
cat >"$consumer/NuGet.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$work/feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
XML
printf '{ "sdk": { "version": "%s", "rollForward": "latestFeature" } }\n' "$(dotnet --version)" >"$consumer/global.json"
printf '<Project />\n' >"$consumer/Directory.Build.props"
printf '<Project />\n' >"$consumer/Directory.Packages.props"
sed -i "s/\$(AlgorithmsVersion)/$version/" "$consumer/Consumer.fsproj"
# An isolated package folder, so a re-pack of the same version is never shadowed by a cached copy.
export NUGET_PACKAGES="$work/nuget-packages"
( cd "$root" && dotnet tool restore >/dev/null )
( cd "$consumer" && dotnet run ) >"$work/consumer-dotnet.txt"
fable_compile "$root" "$work/fable-consumer.log" "$consumer/Consumer.fsproj" --lang javascript --outDir "$work/consumer-js"
printf '{"type":"module"}\n' >"$work/consumer-js/package.json"
node "$work/consumer-js/Program.js" >"$work/consumer-node.txt"
echo "dotnet $(cat "$work/consumer-dotnet.txt")"
echo "node   $(cat "$work/consumer-node.txt")"
cmp "$work/consumer-dotnet.txt" "$work/consumer-node.txt" || { echo "fable-proof: FAILED, the packed consumer differs" >&2; exit 1; }
grep -q "^heap=bdac mul=" "$work/consumer-node.txt" || { echo "fable-proof: FAILED, unexpected consumer output" >&2; exit 1; }
echo "fable-proof: ok"
