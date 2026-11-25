#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
TEST_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mir2-native.publicservices.XXXXXX")"
SOURCE="${MIR2_DATA_ROOT:-$ROOT/data/versions/default}"
export MIR2_DATA_ROOT="$TEST_ROOT/data" MIR2_RUNTIME_ROOT="$TEST_ROOT/data/runtime"
export MIR2_GAME_PORT="${MIR2_GAME_PORT:-17182}" MIR2_RESOURCE_PORT="${MIR2_RESOURCE_PORT:-15182}"
export MIR2_SERVER="http://127.0.0.1:$MIR2_RESOURCE_PORT" MIR2_CLIENT_CACHE="$TEST_ROOT/cache" MIR2_CLIENT_SETTINGS="$TEST_ROOT/settings.json"
GODOT="${GODOT_BIN:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
SERVER_PID=""
cleanup() {
    local result=$?
    if [ -n "$SERVER_PID" ]; then kill -TERM "$SERVER_PID" 2>/dev/null || true; wait "$SERVER_PID" 2>/dev/null || true; fi
    if [ "$result" != 0 ] || [ "${MIR2_TEST_KEEP:-}" = 1 ]; then echo "Public services fixture: $TEST_ROOT"; else find "$TEST_ROOT" -depth -delete; fi
}
trap cleanup EXIT
dotnet build tests/Compatibility/Compatibility.csproj -c Release --nologo --verbosity quiet
dotnet build src/Client/Client.Godot.csproj --nologo --verbosity quiet
MIR2_DATA_ROOT="$TEST_ROOT/checks-data" MIR2_RUNTIME_ROOT="$TEST_ROOT/checks-runtime" dotnet tests/Compatibility/bin/Release/net8.0/Compatibility.dll --public-services-checks
MIR2_DATA_ROOT="$TEST_ROOT/checks-data" MIR2_RUNTIME_ROOT="$TEST_ROOT/checks-runtime" dotnet tests/Compatibility/bin/Release/net8.0/Compatibility.dll --settings-checks
MIR2_DATA_ROOT="$SOURCE" MIR2_RUNTIME_ROOT="$TEST_ROOT/seed" dotnet tests/Compatibility/bin/Release/net8.0/Compatibility.dll --fixture "$TEST_ROOT/data" --gm-fixture --public-services-fixture > "$TEST_ROOT/fixture.log" 2>&1
start_server() {
    dotnet src/Server/bin/Release/net8.0/Mir2.Server.dll "--mir2-instance=$ROOT:$MIR2_RUNTIME_ROOT:server" > "$TEST_ROOT/server-$1.log" 2>&1 & SERVER_PID=$!
    for ((attempt=0; attempt<90; attempt++)); do
        if rg -q 'Network Started' "$TEST_ROOT/server-$1.log" && curl -fsS "$MIR2_SERVER/health" > /dev/null; then return; fi
        if ! kill -0 "$SERVER_PID" 2>/dev/null; then cat "$TEST_ROOT/server-$1.log"; return 1; fi
        sleep 1
    done
    cat "$TEST_ROOT/server-$1.log"; return 1
}
stop_server() { kill -TERM "$SERVER_PID"; wait "$SERVER_PID"; SERVER_PID=""; }
start_server first
dotnet tests/Compatibility/bin/Release/net8.0/Compatibility.dll --public-services-session "$MIR2_GAME_PORT" "$TEST_ROOT/save.json"
stop_server
start_server restored
dotnet tests/Compatibility/bin/Release/net8.0/Compatibility.dll --public-services-session "$MIR2_GAME_PORT" "$TEST_ROOT/save.json" --verify-save
stop_server
start_server native
"$GODOT" --path "$ROOT/src/Client" --max-fps 60 -- --server="$MIR2_SERVER" --verify-public-services --screenshot="$TEST_ROOT/client.png" > "$TEST_ROOT/client.log" 2>&1
cat "$TEST_ROOT/client.log"
rg -q 'PASS native public services' "$TEST_ROOT/client.log"
stop_server
python3 - "$MIR2_RUNTIME_ROOT/mir2.sqlite3" <<'PY'
import sqlite3, sys
with sqlite3.connect('file:' + sys.argv[1] + '?mode=ro', uri=True) as db:
    assert db.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
print('PASS isolated public services SQLite integrity')
PY
