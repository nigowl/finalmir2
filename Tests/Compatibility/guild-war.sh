#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
SOURCE="${MIR2_DATA_ROOT:-$ROOT/data/versions/default}"
SOAK_SECONDS="${MIR2_GUILD_WAR_SOAK_SECONDS:-0}"
if ! [[ "$SOAK_SECONDS" =~ ^[0-9]{1,5}$ ]] || [ "$SOAK_SECONDS" -gt 86400 ]; then
    echo "MIR2_GUILD_WAR_SOAK_SECONDS 须为0–86400秒；0只运行行会战争专项。" >&2
    exit 2
fi
TEST_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mir2-guild-war.XXXXXX")"
SERVER_PID=""
cleanup() {
    local result=$?
    if [ -n "$SERVER_PID" ]; then kill -TERM "$SERVER_PID" 2>/dev/null || true; wait "$SERVER_PID" 2>/dev/null || true; fi
    if [ "$result" != 0 ] || [ "${MIR2_TEST_KEEP:-}" = 1 ]; then echo "Guild war evidence: $TEST_ROOT"; else find "$TEST_ROOT" -depth -delete; fi
}
trap cleanup EXIT
mkdir "$TEST_ROOT/source"
rsync -a --exclude=bin --exclude=obj --exclude=.godot --exclude=.DS_Store "$ROOT/src" "$ROOT/tests" "$ROOT/scripts" "$ROOT/Mir2.sln" "$TEST_ROOT/source/"
cd "$TEST_ROOT/source"
export MIR2_DATA_ROOT="$SOURCE" MIR2_RUNTIME_ROOT="$TEST_ROOT/checks-runtime"
dotnet build tests/Compatibility/Compatibility.csproj -c Debug --nologo --verbosity quiet -m:1 --disable-build-servers
dotnet build src/Client/Client.Godot.csproj --nologo --verbosity quiet -m:1 --disable-build-servers
dotnet tests/Compatibility/bin/Debug/net8.0/Compatibility.dll --guild-war-checks
dotnet tests/Compatibility/bin/Debug/net8.0/Compatibility.dll --client-checks
dotnet tests/Compatibility/bin/Debug/net8.0/Compatibility.dll --npc-template-checks
GODOT="${GODOT_BIN:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
"$GODOT" --headless --editor --path "$PWD/src/Client" --quit > "$TEST_ROOT/godot-import.log" 2>&1
dotnet tests/Compatibility/bin/Debug/net8.0/Compatibility.dll --fixture "$TEST_ROOT/data" --guild-war-fixture > "$TEST_ROOT/fixture.log" 2>&1
export MIR2_DATA_ROOT="$TEST_ROOT/data" MIR2_RUNTIME_ROOT="$TEST_ROOT/data/runtime"
export MIR2_GAME_PORT="${MIR2_GAME_PORT:-17199}" MIR2_RESOURCE_PORT="${MIR2_RESOURCE_PORT:-15199}"
export MIR2_CLIENT_CACHE="$TEST_ROOT/cache" MIR2_CLIENT_SETTINGS="$TEST_ROOT/settings.json"
phases=(first restored)
if [ "$SOAK_SECONDS" -gt 0 ]; then phases+=(soak soak-restored); fi
for phase in "${phases[@]}"; do
    dotnet src/Server/bin/Debug/net8.0/Mir2.Server.dll "--mir2-instance=$PWD:$MIR2_RUNTIME_ROOT:server" > "$TEST_ROOT/server-$phase.log" 2>&1 & SERVER_PID=$!
    for ((attempt=0; attempt<90; attempt++)); do
        if rg -q 'Network Started' "$TEST_ROOT/server-$phase.log" && curl -fsS "http://127.0.0.1:$MIR2_RESOURCE_PORT/health" > /dev/null; then break; fi
        if ! kill -0 "$SERVER_PID" 2>/dev/null; then cat "$TEST_ROOT/server-$phase.log"; exit 1; fi
        sleep 1
    done
    curl -fsS "http://127.0.0.1:$MIR2_RESOURCE_PORT/health" > /dev/null
    if [[ "$phase" = soak* ]]; then
        duration="$SOAK_SECONDS"
        if [ "$phase" = soak-restored ]; then duration=0; fi
        dotnet tests/Compatibility/bin/Debug/net8.0/Compatibility.dll --guild-war-soak "$MIR2_GAME_PORT" "$duration" "$TEST_ROOT/soak.json" | tee "$TEST_ROOT/$phase.log"
    else
        args=(--verify-guild-war)
        if [ "$phase" = restored ]; then args+=(--verify-guild-war-save); fi
        if ! "$GODOT" --path "$PWD/src/Client" --max-fps 60 -- --server="http://127.0.0.1:$MIR2_RESOURCE_PORT" "--mir2-instance=$PWD:$MIR2_RUNTIME_ROOT:client" "${args[@]}" --guild-war-record="$TEST_ROOT/data/guild-war.json" --screenshot="$TEST_ROOT/$phase.png" > "$TEST_ROOT/client-$phase.log" 2>&1; then
            tail -n 65 "$TEST_ROOT/client-$phase.log"; exit 1
        fi
        rg 'PASS native guild war' "$TEST_ROOT/client-$phase.log"
        if rg -q '^ERROR:' "$TEST_ROOT/client-$phase.log"; then tail -n 65 "$TEST_ROOT/client-$phase.log"; exit 1; fi
    fi
    kill -TERM "$SERVER_PID"; wait "$SERVER_PID"; SERVER_PID=""
done
python3 - "$MIR2_RUNTIME_ROOT/mir2.sqlite3" <<'PY'
import sqlite3, sys
with sqlite3.connect('file:' + sys.argv[1] + '?mode=ro', uri=True) as db:
    assert db.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
print('PASS guild war isolated SQLite integrity')
PY
