#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
SOURCE="${MIR2_DATA_ROOT:-$ROOT/data/versions/default}"
ACTION_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mir2-actions.XXXXXX")"
export MIR2_DATA_ROOT="$ACTION_ROOT/data" MIR2_RUNTIME_ROOT="$ACTION_ROOT/data/runtime"
export MIR2_GAME_PORT="${MIR2_GAME_PORT:-17187}" MIR2_RESOURCE_PORT="${MIR2_RESOURCE_PORT:-15187}"
export MIR2_CLIENT_CACHE="$ACTION_ROOT/cache" MIR2_CLIENT_SETTINGS="$ACTION_ROOT/settings.json"
SERVER_PID=""
cleanup() {
  if [ -n "$SERVER_PID" ]; then kill -TERM "$SERVER_PID" 2>/dev/null || true; wait "$SERVER_PID" || true; fi
  echo "Action/item evidence: $ACTION_ROOT"
}
trap cleanup EXIT
dotnet build tests/Compatibility/Compatibility.csproj -c Release --no-restore --verbosity quiet
dotnet build src/Client/Client.Godot.csproj --no-restore --verbosity quiet
dotnet tests/Compatibility/bin/Release/net8.0/Compatibility.dll --action-item-checks
MIR2_DATA_ROOT="$SOURCE" dotnet tests/Compatibility/bin/Release/net8.0/Compatibility.dll --fixture "$ACTION_ROOT/data" --skill-fixture --action-items-fixture > "$ACTION_ROOT/fixture.log" 2>&1
dotnet src/Server/bin/Release/net8.0/Mir2.Server.dll "--mir2-instance=$ROOT:$MIR2_RUNTIME_ROOT:server" > "$ACTION_ROOT/server.log" 2>&1 & SERVER_PID=$!
for ((attempt=0; attempt<90; attempt++)); do
  if curl -fsS "http://127.0.0.1:$MIR2_RESOURCE_PORT/health" > /dev/null 2>&1; then break; fi
  if ! kill -0 "$SERVER_PID" 2>/dev/null; then cat "$ACTION_ROOT/server.log"; exit 1; fi
  sleep 1
done
if [ "${MIR2_ACTION_TEST_SPEED:-}" = 1 ]; then
  python3 - <<'PY'
import hashlib, json, os, pathlib, urllib.request
base = f"http://127.0.0.1:{os.environ['MIR2_RESOURCE_PORT']}"
key = (pathlib.Path(os.environ['MIR2_RUNTIME_ROOT']) / 'admin.key').read_text().strip()
version = hashlib.sha256(os.path.normpath(os.environ['MIR2_DATA_ROOT']).encode()).hexdigest().upper()
headers = {'Authorization': 'Bearer ' + key, 'X-Mir2-Version': version, 'Content-Type': 'application/json'}
url = base + '/management/settings'
with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=30) as response:
    config = json.load(response)
config['values'].update(playerMoveMilliseconds=650, playerActionSpeedPercent=90)
body = json.dumps({'values': config['values'], 'version': config['version']}).encode()
with urllib.request.urlopen(urllib.request.Request(url, data=body, headers=headers, method='PUT'), timeout=60) as response:
    saved = json.load(response)['values']
assert (saved['playerMoveMilliseconds'], saved['playerActionSpeedPercent']) == (650, 90)
with urllib.request.urlopen(base + '/assets/manifest', timeout=10) as response:
    manifest = json.load(response)
assert (manifest['playerMoveMilliseconds'], manifest['playerActionSpeedPercent']) == (650, 90)
print('PASS settings save, engine restart and new timing manifest.')
PY
  export MIR2_EXPECT_MOVE_MS=650
fi
"${GODOT_BIN:-/Applications/Godot_mono.app/Contents/MacOS/Godot}" --path "$ROOT/src/Client" --max-fps 60 --log-file "$ACTION_ROOT/godot.log" -- --server="http://127.0.0.1:$MIR2_RESOURCE_PORT" "${MIR2_ACTION_VERIFY:---verify-action-items}" --screenshot="$ACTION_ROOT/client.png" > "$ACTION_ROOT/client.log" 2>&1
rg 'PASS native (action/items|locomotion|visual fixes)' "$ACTION_ROOT/client.log"
if [ -f "$ACTION_ROOT/settings.assets.json" ]; then
  kill -TERM "$SERVER_PID"; wait "$SERVER_PID"; SERVER_PID=""
  python3 - "$MIR2_RUNTIME_ROOT/mir2.sqlite3" <<'PY'
import sqlite3, sys
with sqlite3.connect('file:'+sys.argv[1]+'?mode=ro', uri=True) as db:
    assert db.execute('pragma integrity_check').fetchone()[0] == 'ok'
PY
  dotnet src/Server/bin/Release/net8.0/Mir2.Server.dll "--mir2-instance=$ROOT:$MIR2_RUNTIME_ROOT:server" > "$ACTION_ROOT/restarted.log" 2>&1 & SERVER_PID=$!
  for ((attempt=0; attempt<90; attempt++)); do
    if curl -fsS "http://127.0.0.1:$MIR2_RESOURCE_PORT/health" > /dev/null 2>&1; then break; fi
    if ! kill -0 "$SERVER_PID" 2>/dev/null; then cat "$ACTION_ROOT/restarted.log"; exit 1; fi
    sleep 1
  done
  dotnet tests/Compatibility/bin/Release/net8.0/Compatibility.dll --recycle-save "$MIR2_GAME_PORT" "$ACTION_ROOT/settings.assets.json"
fi
