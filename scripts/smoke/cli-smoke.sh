#!/usr/bin/env bash
# CLI smoke test for the packaged lazydotnet tool.
#
# Runs the published binary headlessly (no TUI / no TTY needed) and asserts the
# command surface works: --version and --help exit 0 with sane output, and an
# invalid solution path is rejected with a non-zero exit. This catches
# packaging/trimming/assembly-load and MSBuild-locator regressions on every OS,
# without driving the interactive dashboard.
#
# Usage: cli-smoke.sh <path-to-lazydotnet-binary>
#   On Windows the .exe suffix is auto-detected, so pass the suffixless path.

set -euo pipefail

CLI="${1:-${LAZYDOTNET_CLI:-}}"
if [ -z "$CLI" ]; then
  echo "usage: cli-smoke.sh <path-to-lazydotnet>" >&2
  exit 2
fi
# dotnet tool shims are <name> on unix and <name>.exe on Windows.
if [ ! -f "$CLI" ] && [ -f "$CLI.exe" ]; then
  CLI="$CLI.exe"
fi

fail() { echo "SMOKE FAIL: $1" >&2; exit 1; }

echo "== smoke: --version =="
ver="$("$CLI" --version)" || fail "--version exited non-zero"
[ -n "$ver" ] || fail "--version produced no output"
echo "version: $ver"
case "$ver" in
  *[0-9]*) ;;
  *) fail "--version output has no digit: $ver" ;;
esac

echo "== smoke: --help =="
help="$("$CLI" --help)" || fail "--help exited non-zero"
printf '%s\n' "$help" | grep -qiE "usage|lazydotnet" || fail "--help missing usage text"

echo "== smoke: invalid solution path is rejected =="
if "$CLI" ./does-not-exist.sln >/dev/null 2>&1; then
  fail "invalid solution path should exit non-zero"
fi

echo "ALL SMOKE PASSED"
