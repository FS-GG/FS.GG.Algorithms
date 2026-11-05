#!/usr/bin/env bash
# Existing owner-package derive/pack/restore route; Atlas is resolved from its published driver.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
mode=pack
archive=
output="${FSGG_SKILLS_OUTPUT:-$root/artifacts/skills}"
case "${1:-}" in
  --check-source) mode=source; shift ;;
  --archive) mode=archive; archive="${2:?--archive requires an existing nupkg}"; shift 2 ;;
  '') ;;
  *) echo 'usage: verify-package.sh [--check-source | --archive <existing-nupkg>]' >&2; exit 2 ;;
esac
[ "$#" = 0 ] || { echo 'unexpected verification arguments' >&2; exit 2; }
work="$(mktemp -d "${TMPDIR:-${RUNNER_TEMP:-/tmp}}/algorithms-skills-check.XXXXXX")"
trap 'rm -rf -- "$work"' EXIT
config="${FSGG_SKILLS_NUGET_CONFIG:-$work/NuGet.Config}"
if [ -z "${FSGG_SKILLS_NUGET_CONFIG:-}" ]; then
  cat > "$config" <<'XML'
<configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>
XML
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0

# Refuse manifest drift before restore, packing or any downstream consumer workload.
if [ "$mode" != archive ]; then
  dotnet fsi scripts/generate-skill-manifest.fsx --check
fi
dotnet restore tests/skills/AtlasDriver.csproj --locked-mode --configfile "$config"
drivers="$(dotnet msbuild tests/skills/AtlasDriver.csproj -getProperty:FsggDriversContentDir)"
export FSGG_ATLAS_SCRIPT="$drivers/skills/atlas/scripts/atlas.py"
test -f "$FSGG_ATLAS_SCRIPT" || { echo 'missing Atlas helper in the restored Drivers content handle' >&2; exit 1; }
dotnet restore src/FS.GG.Algorithms.Skills/FS.GG.Algorithms.Skills.csproj --locked-mode --configfile "$config"
if [ "$mode" != archive ]; then
  python3 -I -B scripts/check-algorithms-pack.py --atlas-script "$FSGG_ATLAS_SCRIPT" --source "FS-GG/FS.GG.Algorithms=$root"
  python3 -B -m unittest discover -s tests/skills -p test_delivery.py -v
  python3 -I -B eng/release-checks.test.py
fi
if [ "$mode" = source ]; then
  echo 'producer source checks passed; no archive was packed'
  exit 0
fi
version="$(dotnet msbuild src/FS.GG.Algorithms.Skills/FS.GG.Algorithms.Skills.csproj -getProperty:Version)"
if [ "$mode" = pack ]; then
  mkdir -p "$output"
  dotnet pack src/FS.GG.Algorithms.Skills/FS.GG.Algorithms.Skills.csproj --no-restore -c Release -p:UseSharedCompilation=false -nodeReuse:false -o "$output"
  archive="$output/FS.GG.Algorithms.Skills.$version.nupkg"
fi
archive="$(realpath "$archive")"
python3 -I -B - "$root" "$archive" "$version" "$work" <<'PY'
import hashlib, importlib.util, json, pathlib, subprocess, sys, xml.etree.ElementTree as ET, zipfile
root, archive, version, work = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2]), sys.argv[3], pathlib.Path(sys.argv[4])
spec = importlib.util.spec_from_file_location('stage', root / 'src/FS.GG.Algorithms.Skills/stage-skills.py')
stage = importlib.util.module_from_spec(spec); spec.loader.exec_module(stage)
payload = stage.payload(root)
expected = {'algorithms-skills/' + name: body for name, body in payload.items()}
expected['README.md'] = (root / 'src/FS.GG.Algorithms.Skills/README.md').read_bytes()
expected['build/FS.GG.Algorithms.Skills.props'] = (root / 'src/FS.GG.Algorithms.Skills/build/FS.GG.Algorithms.Skills.props').read_bytes()
with zipfile.ZipFile(archive) as package:
    names = package.namelist()
    assert len(names) == len(set(names)), 'duplicate ZIP members'
    for name in names:
        assert not pathlib.PurePosixPath(name).is_absolute() and '..' not in pathlib.PurePosixPath(name).parts and '\\' not in name, 'unsafe ZIP member'
    metadata = {'[Content_Types].xml', '_rels/.rels', 'FS.GG.Algorithms.Skills.nuspec', '.signature.p7s'}
    extra = {name for name in names if name not in expected and name not in metadata and not name.startswith('package/services/metadata/core-properties/')}
    assert not extra, ('unexpected package payload', extra)
    for name, body in expected.items():
        assert package.read(name) == body, ('source/archive byte mismatch', name)
    ns = {'n': 'http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd'}
    nuspec = ET.fromstring(package.read('FS.GG.Algorithms.Skills.nuspec'))
    assert nuspec.find('n:metadata/n:id', ns).text == 'FS.GG.Algorithms.Skills'
    assert nuspec.find('n:metadata/n:version', ns).text == version
    assert not any(element.tag.endswith('dependency') for element in nuspec.iter()), 'content package acquired a dependency'
    commit = subprocess.check_output(['git', '-C', str(root), 'rev-parse', 'HEAD'], text=True).strip()
    assert nuspec.find('n:metadata/n:repository', ns).attrib['commit'] == commit, 'archive names another source revision'
(work / 'expected.json').write_text(json.dumps({name.removeprefix('algorithms-skills/'): hashlib.sha256(body).hexdigest() for name, body in expected.items() if name.startswith('algorithms-skills/')}))
# Real NuGet restore of this archive, including evaluation of its imported content handle.
feed = work / 'feed'; feed.mkdir()
(feed / ('FS.GG.Algorithms.Skills.' + version + '.nupkg')).write_bytes(archive.read_bytes())
configuration = ET.Element('configuration'); sources = ET.SubElement(configuration, 'packageSources'); ET.SubElement(sources, 'clear'); ET.SubElement(sources, 'add', key='this-archive', value=str(feed))
ET.ElementTree(configuration).write(work / 'consumer.config', encoding='utf-8')
project = ET.Element('Project', Sdk='Microsoft.NET.Sdk'); properties = ET.SubElement(project, 'PropertyGroup'); ET.SubElement(properties, 'TargetFramework').text = 'net10.0'
items = ET.SubElement(project, 'ItemGroup'); ET.SubElement(items, 'PackageReference', Include='FS.GG.Algorithms.Skills', Version=version)
ET.ElementTree(project).write(work / 'Consumer.csproj', encoding='utf-8')
print('archive source/closed-payload audit passed:', len(payload), 'owner files;', commit)
PY
NUGET_PACKAGES="$work/consumer-packages" dotnet restore "$work/Consumer.csproj" --configfile "$work/consumer.config"
restored="$(NUGET_PACKAGES="$work/consumer-packages" dotnet msbuild "$work/Consumer.csproj" -getProperty:FsggAlgorithmsSkillsContentDir)"
python3 -I -B - "$restored" "$work/expected.json" <<'PY'
import hashlib, json, pathlib, sys
root = pathlib.Path(sys.argv[1]); expected = json.loads(pathlib.Path(sys.argv[2]).read_bytes())
actual = {file.relative_to(root).as_posix(): hashlib.sha256(file.read_bytes()).hexdigest() for file in root.rglob('*') if file.is_file()}
assert actual == expected, 'restored content-handle inventory/bytes differ'
print('real restored content handle matched every owner byte:', root)
PY
python3 -I -B "$FSGG_ATLAS_SCRIPT" --archive "$restored/skills/fs-gg-algorithms/pack" validate
python3 -I -B "$FSGG_ATLAS_SCRIPT" --archive "$restored/skills/fs-gg-algorithms/pack" search 'utility scoring' > "$work/search.json"
python3 -I -B - "$work/search.json" <<'PY'
import json, pathlib, sys
hits = json.loads(pathlib.Path(sys.argv[1]).read_text())
assert any(hit['id'] == 'algorithms/decision/utility-ai' for hit in hits), 'restored pack search missed utility entry'
print('actual packaged Atlas search returned the restored source-only utility entry')
PY
echo 'Skills package qualification passed (package consumer only; no SDD scaffold/upgrade/publication claim).'
