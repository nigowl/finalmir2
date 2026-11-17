#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
node -e 'require("playwright")' || { echo 'Provide installed Playwright through NODE_PATH.'; exit 1; }
TEST_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mir2-native.autolearn.XXXXXX")"
SOURCE="${MIR2_DATA_ROOT:-$ROOT/data/versions/default}"
export MIR2_DATA_ROOT="$TEST_ROOT/data" MIR2_RUNTIME_ROOT="$TEST_ROOT/data/runtime"
export MIR2_GAME_PORT="${MIR2_GAME_PORT:-17160}" MIR2_RESOURCE_PORT="${MIR2_RESOURCE_PORT:-15160}" MIR2_ADMIN_PORT="${MIR2_ADMIN_PORT:-15161}"
export MIR2_SERVER="http://127.0.0.1:$MIR2_RESOURCE_PORT"
SERVER_PID="" ADMIN_PID=""
cleanup() {
    local status=$?
    for pid in "$ADMIN_PID" "$SERVER_PID"; do
        if [ -n "$pid" ]; then kill -TERM "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; fi
    done
    if [ "$status" != 0 ] || [ "${MIR2_TEST_KEEP:-}" = 1 ]; then echo "Automatic learning fixture: $TEST_ROOT"; else find "$TEST_ROOT" -depth -delete; fi
}
trap cleanup EXIT
dotnet build tests/Compatibility/Compatibility.csproj -c Release --nologo --verbosity quiet -m:1 --disable-build-servers
dotnet build src/Admin/Mir2.Admin.csproj -c Release --nologo --verbosity quiet -m:1 --disable-build-servers
MIR2_DATA_ROOT="$TEST_ROOT/checks-data" MIR2_RUNTIME_ROOT="$TEST_ROOT/checks-runtime" dotnet tests/Compatibility/bin/Release/net8.0/Compatibility.dll --auto-learn-checks
MIR2_DATA_ROOT="$SOURCE" MIR2_RUNTIME_ROOT="$TEST_ROOT/seed-runtime" dotnet tests/Compatibility/bin/Release/net8.0/Compatibility.dll --fixture "$TEST_ROOT/data" --gm-fixture > "$TEST_ROOT/fixture.log" 2>&1
start_server() {
    dotnet src/Server/bin/Release/net8.0/Mir2.Server.dll "--mir2-instance=$ROOT:$MIR2_RUNTIME_ROOT:server" > "$TEST_ROOT/server-$1.log" 2>&1 & SERVER_PID=$!
    for ((attempt=0; attempt<90; attempt++)); do
        if rg -q 'Game server ready' "$TEST_ROOT/server-$1.log"; then return; fi
        if ! kill -0 "$SERVER_PID" 2>/dev/null; then cat "$TEST_ROOT/server-$1.log"; return 1; fi
        sleep 1
    done
    cat "$TEST_ROOT/server-$1.log"; return 1
}
start_server first
dotnet src/Admin/bin/Release/net8.0/Mir2.Admin.dll "--mir2-instance=$ROOT:$MIR2_RUNTIME_ROOT:admin" > "$TEST_ROOT/admin.log" 2>&1 & ADMIN_PID=$!
for ((attempt=0; attempt<40; attempt++)); do
    if curl -fsS "http://127.0.0.1:$MIR2_ADMIN_PORT/health" > /dev/null 2>&1; then break; fi
    if ! kill -0 "$ADMIN_PID" 2>/dev/null; then cat "$TEST_ROOT/admin.log"; exit 1; fi
    sleep 1
done
node tests/Management/auto-learn.cjs "$TEST_ROOT" | tee "$TEST_ROOT/browser.log"
dotnet tests/Compatibility/bin/Release/net8.0/Compatibility.dll --auto-learn-session "$MIR2_GAME_PORT" "$TEST_ROOT/record.json" | tee "$TEST_ROOT/tcp.log"
kill -TERM "$SERVER_PID"; wait "$SERVER_PID"; SERVER_PID=""
start_server restored
dotnet tests/Compatibility/bin/Release/net8.0/Compatibility.dll --auto-learn-session "$MIR2_GAME_PORT" "$TEST_ROOT/record.json" --verify-save | tee "$TEST_ROOT/restored.log"
kill -TERM "$SERVER_PID"; wait "$SERVER_PID"; SERVER_PID=""
python3 - "$MIR2_RUNTIME_ROOT/mir2.sqlite3" <<'CHECK'
import sqlite3, sys
with sqlite3.connect('file:'+sys.argv[1]+'?mode=ro', uri=True) as db:
    assert db.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
print('PASS isolated SQLite integrity')
CHECK
