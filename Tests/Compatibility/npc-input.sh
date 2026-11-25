#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
SOURCE="${MIR2_DATA_ROOT:-$ROOT/data/versions/default}"
TEST_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mir2-npc-input.XXXXXX")"
SERVER_PID=""
cleanup() {
    local result=$?
    if [ -n "$SERVER_PID" ]; then kill -TERM "$SERVER_PID" 2>/dev/null || true; wait "$SERVER_PID" 2>/dev/null || true; fi
    if [ "$result" != 0 ] || [ "${MIR2_TEST_KEEP:-}" = 1 ]; then echo "NPC input evidence: $TEST_ROOT"; else find "$TEST_ROOT" -depth -delete; fi
}
trap cleanup EXIT
# 正式实例和人工客户端可能引用当前输出，专项只在独立副本构建。
mkdir "$TEST_ROOT/source"
rsync -a --exclude=bin --exclude=obj --exclude=.godot --exclude=.DS_Store "$ROOT/src" "$ROOT/tests" "$ROOT/scripts" "$ROOT/Mir2.sln" "$TEST_ROOT/source/"
cd "$TEST_ROOT/source"
export MIR2_DATA_ROOT="$SOURCE" MIR2_RUNTIME_ROOT="$TEST_ROOT/checks-runtime"
dotnet build tests/Compatibility/Compatibility.csproj -c Release --nologo --verbosity quiet -m:1 --disable-build-servers
dotnet build src/Client/Client.Godot.csproj --nologo --verbosity quiet -m:1 --disable-build-servers
dotnet tests/Compatibility/bin/Release/net8.0/Compatibility.dll --npc-template-checks
dotnet tests/Compatibility/bin/Release/net8.0/Compatibility.dll --client-checks
"${GODOT_BIN:-/Applications/Godot_mono.app/Contents/MacOS/Godot}" --headless --editor --path "$PWD/src/Client" --quit > "$TEST_ROOT/godot-import.log" 2>&1
dotnet tests/Compatibility/bin/Release/net8.0/Compatibility.dll --fixture "$TEST_ROOT/data" --gm-fixture > "$TEST_ROOT/fixture.log" 2>&1
export MIR2_DATA_ROOT="$TEST_ROOT/data" MIR2_RUNTIME_ROOT="$TEST_ROOT/data/runtime"
export MIR2_GAME_PORT="${MIR2_GAME_PORT:-17196}" MIR2_RESOURCE_PORT="${MIR2_RESOURCE_PORT:-15196}"
export MIR2_CLIENT_CACHE="$TEST_ROOT/cache" MIR2_CLIENT_SETTINGS="$TEST_ROOT/settings.json"
dotnet src/Server/bin/Release/net8.0/Mir2.Server.dll --mir2-instance="$PWD:$MIR2_RUNTIME_ROOT:server" > "$TEST_ROOT/server.log" 2>&1 & SERVER_PID=$!
for ((attempt=0; attempt<90; attempt++)); do
    if rg -q 'Network Started' "$TEST_ROOT/server.log" && curl -fsS "http://127.0.0.1:$MIR2_RESOURCE_PORT/health" > /dev/null; then break; fi
    if ! kill -0 "$SERVER_PID" 2>/dev/null; then cat "$TEST_ROOT/server.log"; exit 1; fi
    sleep 1
done
"${GODOT_BIN:-/Applications/Godot_mono.app/Contents/MacOS/Godot}" --path "$PWD/src/Client" --max-fps 60 -- --server="http://127.0.0.1:$MIR2_RESOURCE_PORT" --verify-npc-input --screenshot="$TEST_ROOT/client.png" > "$TEST_ROOT/client.log" 2>&1
cat "$TEST_ROOT/client.log"
rg -q 'PASS native NPC input' "$TEST_ROOT/client.log"
kill -TERM "$SERVER_PID"; wait "$SERVER_PID"; SERVER_PID=""
python3 - "$MIR2_RUNTIME_ROOT/mir2.sqlite3" <<'PY'
import sqlite3, sys
with sqlite3.connect('file:' + sys.argv[1] + '?mode=ro', uri=True) as db:
    assert db.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
print('PASS NPC input isolated SQLite integrity')
PY
