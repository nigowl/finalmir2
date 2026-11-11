#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
TEST_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mir2-administration.XXXXXX")"
BACKEND_PID=""
cleanup() {
    if [ -n "$BACKEND_PID" ]; then kill -TERM "$BACKEND_PID" 2>/dev/null || true; wait "$BACKEND_PID" 2>/dev/null || true; fi
    if [ "${MIR2_TEST_KEEP:-}" = 1 ]; then echo "Test fixture: $TEST_ROOT"; else find "$TEST_ROOT" -depth -delete; fi
}
trap cleanup EXIT
dotnet build "$ROOT/Mir2.sln" -c Release --nologo --verbosity quiet -m:1 --disable-build-servers
MIR2_DATA_ROOT="${MIR2_DATA_ROOT:-$ROOT/data/versions/default}" dotnet "$ROOT/tests/Compatibility/bin/Release/net8.0/Compatibility.dll" --fixture "$TEST_ROOT/data"
export MIR2_DATA_ROOT="$TEST_ROOT/data" MIR2_RUNTIME_ROOT="$TEST_ROOT/data/runtime"
export MIR2_GAME_PORT="${MIR2_GAME_PORT:-17050}" MIR2_RESOURCE_PORT="${MIR2_RESOURCE_PORT:-15130}"
export MIR2_SERVER="http://127.0.0.1:$MIR2_RESOURCE_PORT"
export MIR2_CONFIGURATION=Release
start() {
    "$ROOT/start-server.sh" > "$TEST_ROOT/server.log" 2>&1 & BACKEND_PID=$!
    for ((i=0; i<120; i++)); do
        if grep -q 'Game server ready' "$TEST_ROOT/server.log"; then return; fi
        if ! kill -0 "$BACKEND_PID" 2>/dev/null; then cat "$TEST_ROOT/server.log"; return 1; fi
        sleep 1
    done
    cat "$TEST_ROOT/server.log"; return 1
}
start
if ! python3 "$ROOT/tests/Management/administration.py" "$MIR2_DATA_ROOT"; then cat "$TEST_ROOT/server.log"; exit 1; fi
kill -TERM "$BACKEND_PID"; wait "$BACKEND_PID"; BACKEND_PID=""
start
if ! python3 "$ROOT/tests/Management/administration.py" "$MIR2_DATA_ROOT" --verify; then cat "$TEST_ROOT/server.log"; exit 1; fi
echo 'Passed detailed administration and full-host SQLite persistence checks.'
