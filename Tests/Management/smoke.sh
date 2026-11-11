#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
TEST_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mir2-launchers.XXXXXX")"
cleanup() { find "$TEST_ROOT" -depth -delete; }
trap cleanup EXIT
dotnet build "$ROOT/Mir2.sln" --nologo --verbosity quiet -m:1 --disable-build-servers
MIR2_DATA_ROOT="${MIR2_DATA_ROOT:-$ROOT/data/versions/default}" dotnet "$ROOT/tests/Compatibility/bin/Debug/net8.0/Compatibility.dll" --fixture "$TEST_ROOT/data"
export MIR2_DATA_ROOT="$TEST_ROOT/data"
export MIR2_RUNTIME_ROOT="$TEST_ROOT/data/runtime"
export MIR2_GAME_PORT="${MIR2_GAME_PORT:-17030}"
export MIR2_RESOURCE_PORT="${MIR2_RESOURCE_PORT:-15110}"
export MIR2_ADMIN_PORT="${MIR2_ADMIN_PORT:-15111}"
export MIR2_SERVER="http://127.0.0.1:$MIR2_RESOURCE_PORT"
if ! python3 "$ROOT/tests/Management/restart.py" "$TEST_ROOT/data"; then
  find "$TEST_ROOT" -name 'restart-*.log' -type f -exec cat {} \;
  exit 1
fi
echo 'Passed all three managed launchers and authenticated Blazor smoke checks.'
