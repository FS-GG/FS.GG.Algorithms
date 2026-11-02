#!/usr/bin/env bash
# Kernel micro-bench on two runtimes (ADR-0098 §4.3).
#
#   bench/run.sh            record bench/baselines/<host>-<yyyy-mm-dd>-<version>.json (fdev only)
#   bench/run.sh --smoke    prove the harness (BenchmarkDotNet Dry job, 3 Node iterations); records nothing
#
# Quiet host (FS.GG.Game#780): a recording run is admitted only by
#   fdev-lane-capacity.py --max-load-per-core 0.25
# from FS-GG/.github (FSGG_CAPACITY_PROBE, default ../.github/scripts/fdev-lane-capacity.py beside this
# checkout). A held probe WAITS (re-probing every 60 s, up to FSGG_BENCH_WAIT_MINUTES, default 60); it is
# never overridden. If the host never quiets, the run exits 3 and the bench stays owed (ADR-0098 §4.3).
# Needs: the pinned .NET SDK, node, python3. Work files go to ${TMPDIR:-/tmp}/fsgg-algorithms-bench.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
work="${TMPDIR:-/tmp}/fsgg-algorithms-bench"
smoke=false
[ "${1:-}" = "--smoke" ] && smoke=true
probe="${FSGG_CAPACITY_PROBE:-$root/../.github/scripts/fdev-lane-capacity.py}"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/src/FS.GG.Algorithms/FS.GG.Algorithms.fsproj")"
rm -rf "$work"
mkdir -p "$work"

admit() {
  local deadline=$(( $(date +%s) + 60 * ${FSGG_BENCH_WAIT_MINUTES:-60} ))
  while :; do
    if python3 -I "$probe" --max-load-per-core 0.25 >"$work/admission.json"; then
      echo "bench: admitted: $(cat "$work/admission.json")"
      return 0
    fi
    echo "bench: held: $(python3 -I -c 'import json,sys; print(json.load(open(sys.argv[1]))["reasons"])' "$work/admission.json")"
    if [ "$(date +%s)" -ge "$deadline" ]; then
      echo "bench: the host did not quiet within ${FSGG_BENCH_WAIT_MINUTES:-60} minutes; the bench stays owed" >&2
      exit 3
    fi
    sleep 60
  done
}

if $smoke; then
  printf '{"verdict":"admit","smoke":true}\n' >"$work/admission.json"
else
  [ -f "$probe" ] || { echo "bench: no capacity probe at $probe (set FSGG_CAPACITY_PROBE); recording runs are fdev-only" >&2; exit 2; }
  # A recorded baseline must name the exact source it measured; only a smoke run (which records nothing) may be dirty.
  [ -z "$(git -C "$root" status --porcelain)" ] || { echo "bench: refusing to record a baseline from a dirty tree; commit or stash first" >&2; exit 2; }
  admit
fi

( cd "$root" && dotnet tool restore >/dev/null )
# shellcheck source=eng/fable-compile.sh
. "$root/eng/fable-compile.sh"
run_step "$work/build-bench.log" dotnet build "$root/bench/dotnet" -c Release --nologo -v q
dotnet run --project "$root/bench/dotnet" -c Release --no-build -- workcounts "$work/workcounts.json"
fable_compile "$root" "$work/fable-bench.log" bench/fable/FS.GG.Algorithms.Bench.Fable.fsproj --lang javascript --outDir "$work/bench-js"
printf '{"type":"module"}\n' >"$work/bench-js/package.json"

$smoke || admit
# BenchmarkDotNet finds the project from the working directory, so run it from bench/dotnet.
( cd "$root/bench/dotnet" && dotnet run -c Release --no-build -- run "$work/bdn" $($smoke && echo --smoke) )
$smoke || admit
node "$root/bench/fable/run.mjs" "$work/bench-js" "$work/node.json" $($smoke && echo --smoke)

report="$(ls "$work"/bdn/results/*-report-full.json | head -1)"
host="$(hostname -s 2>/dev/null || uname -n)"
commit="$(git -C "$root" rev-parse HEAD)"
dirty=""
if [ -n "$(git -C "$root" status --porcelain)" ]; then
  dirty="--dirty"
fi
if $smoke; then
  out="$work/smoke-baseline.json"
else
  out="$root/bench/baselines/$host-$(date -u +%Y-%m-%d)-$version.json"
fi
python3 -I "$root/bench/assemble.py" --bdn "$report" --workcounts "$work/workcounts.json" --node "$work/node.json" \
  --admission "$work/admission.json" --version "$version" --commit "$commit" $dirty --host "$host" --out "$out"
echo "bench: $($smoke && echo 'smoke ok (nothing recorded)' || echo "recorded $out")"
