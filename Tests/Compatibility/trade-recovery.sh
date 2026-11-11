#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
TRADE_TEST="$(mktemp -d "${TMPDIR:-/tmp}/mir2-trade-recovery.XXXXXX")"
export MIR2_DATA_ROOT="${MIR2_DATA_ROOT:-$ROOT/data/versions/default}" MIR2_RUNTIME_ROOT="$TRADE_TEST/runtime"
export MIR2_GAME_PORT="${MIR2_GAME_PORT:-17080}" MIR2_RESOURCE_PORT="${MIR2_RESOURCE_PORT:-15160}"
BACKEND_PID=""; CLIENT_PID=""
cleanup() {
  if [ -n "$CLIENT_PID" ]; then kill -TERM "$CLIENT_PID" 2>/dev/null || true; wait "$CLIENT_PID" 2>/dev/null || true; fi
  if [ -n "$BACKEND_PID" ]; then kill -TERM "$BACKEND_PID" 2>/dev/null || true; wait "$BACKEND_PID" 2>/dev/null || true; fi
  if [ "${MIR2_TEST_KEEP:-}" = 1 ]; then echo "Test fixture: $TRADE_TEST"; else find "$TRADE_TEST" -depth -delete; fi
}
trap cleanup EXIT
dotnet build "$ROOT/Mir2.sln" -c Debug --nologo -v quiet -m:1 --disable-build-servers
TEST_DLL="$ROOT/tests/Compatibility/bin/Debug/net8.0/Compatibility.dll"
dotnet "$TEST_DLL" --fixture "$TRADE_TEST/ref" --trade-fixture --guild-fixture
export MIR2_DATA_ROOT="$TRADE_TEST/ref"
start_backend() {
  "$ROOT/start-server.sh" > "$TRADE_TEST/backend.log" 2>&1 & BACKEND_PID=$!
  for ((attempt=0; attempt<120; attempt++)); do
    if rg -q 'Game server ready' "$TRADE_TEST/backend.log"; then return; fi
    if ! kill -0 "$BACKEND_PID" 2>/dev/null; then cat "$TRADE_TEST/backend.log"; return 1; fi
    sleep 1
  done
  cat "$TRADE_TEST/backend.log"; return 1
}
run_session() { dotnet "$TEST_DLL" --trade-recovery "$1" "$MIR2_GAME_PORT" "http://127.0.0.1:$MIR2_RESOURCE_PORT" "$TRADE_TEST/expected.json"; }
start_backend
run_session prepare > "$TRADE_TEST/client.log" 2>&1 & CLIENT_PID=$!
for ((attempt=0; attempt<90; attempt++)); do
  if [ -f "$TRADE_TEST/expected.json.ready" ]; then break; fi
  if ! kill -0 "$CLIENT_PID" 2>/dev/null; then cat "$TRADE_TEST/client.log" "$TRADE_TEST/backend.log"; exit 1; fi
  sleep 1
done
if [ ! -f "$TRADE_TEST/expected.json.ready" ]; then cat "$TRADE_TEST/client.log"; exit 1; fi
# This PID belongs to the disposable server started above, never to the default runtime.
kill -KILL "$BACKEND_PID"; wait "$BACKEND_PID" 2>/dev/null || true; BACKEND_PID=""
wait "$CLIENT_PID"; CLIENT_PID=""; cat "$TRADE_TEST/client.log"
for ((pass=0; pass<2; pass++)); do
  start_backend
  run_session verify
  kill -TERM "$BACKEND_PID"; wait "$BACKEND_PID"; BACKEND_PID=""
done
python3 - "$MIR2_RUNTIME_ROOT/mir2.sqlite3" <<'PY'
import sqlite3, sys
with sqlite3.connect(sys.argv[1]) as db:
    assert db.execute('pragma integrity_check').fetchone()[0] == 'ok'
print('PASS SQLite integrity after forced exit and two recovery restarts.')
PY
