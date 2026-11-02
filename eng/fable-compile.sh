# Sourced by eng/fable-proof.sh and bench/run.sh: the Fable compile gate and a strict build step.
#
#   fable_compile ROOT LOG FABLE-ARGS...
#
# Runs `dotnet fable FABLE-ARGS...` from ROOT (for the pinned tool manifest), keeps the full log at LOG, prints
# the summary and error lines, and returns 1 when Fable exits non-zero OR reports any error. Fable can write
# JavaScript for the files it could compile even when it rejects others, so neither a zero exit alone nor the
# presence of output proves a clean compile; both conditions are checked, and nothing is filtered away first.
fable_compile() {
  local root="$1" log="$2"
  shift 2
  local rc=0
  ( cd "$root" && dotnet fable "$@" ) >"$log" 2>&1 || rc=$?
  sed 's/\x1b\[[0-9;]*m//g' "$log" | grep -E "Fable compilation|error|Compilation failed" || true
  if [ "$rc" -ne 0 ]; then
    echo "fable-compile: FAILED, Fable exited $rc (log: $log)" >&2
    return 1
  fi
  if sed 's/\x1b\[[0-9;]*m//g' "$log" | grep -qE "error (FABLE|FSHARP)|Compilation failed"; then
    echo "fable-compile: FAILED, Fable reported errors although it exited 0 (log: $log)" >&2
    return 1
  fi
  return 0
}

# run_step LOG COMMAND...  Runs COMMAND with its output in LOG, prints the summary and error lines, and returns
# COMMAND's own exit status (never the filter's), so a failed build or pack stops the script.
run_step() {
  local log="$1"
  shift
  local rc=0
  "$@" >"$log" 2>&1 || rc=$?
  sed 's/\x1b\[[0-9;]*m//g' "$log" | grep -E " error |Build succeeded|Successfully created" || true
  if [ "$rc" -ne 0 ]; then
    echo "step: FAILED, '$*' exited $rc (log: $log)" >&2
  fi
  return "$rc"
}
