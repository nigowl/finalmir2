#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
TEST_RUNTIME="$(mktemp -d "${TMPDIR:-/tmp}/mir2-native.XXXXXX")"
export MIR2_DATA_ROOT="${MIR2_DATA_ROOT:-$ROOT/data/versions/default}"
export MIR2_RUNTIME_ROOT="$TEST_RUNTIME/runtime"
export MIR2_GAME_PORT="${MIR2_GAME_PORT:-17010}"
export MIR2_RESOURCE_PORT="${MIR2_RESOURCE_PORT:-15090}"
export MIR2_CLIENT_CACHE="$TEST_RUNTIME/cache"
export MIR2_CLIENT_SETTINGS="$TEST_RUNTIME/client-settings.json"
export MIR2_ADMIN_PORT="${MIR2_ADMIN_PORT:-15091}"
GODOT="${GODOT_BIN:-godot}"
if ! command -v "$GODOT" >/dev/null 2>&1 && [ -x /Applications/Godot_mono.app/Contents/MacOS/Godot ]; then
  GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot
fi
BACKEND_PID=""
cleanup() {
  if [ -n "$BACKEND_PID" ]; then kill -TERM "$BACKEND_PID" 2>/dev/null || true; wait "$BACKEND_PID" 2>/dev/null || true; fi
  # find never follows the reference-data symlinks created by the fixture.
  if [ "${MIR2_TEST_KEEP:-}" = 1 ]; then echo "Test fixture: $TEST_RUNTIME"; else find "$TEST_RUNTIME" -depth -delete; fi
}
trap cleanup EXIT
dotnet build "$ROOT/Mir2.sln" --configuration Debug --nologo --verbosity quiet -m:1 --disable-build-servers
dotnet "$ROOT/tests/Compatibility/bin/Debug/net8.0/Compatibility.dll" --fixture "$TEST_RUNTIME/ref" --guild-fixture
export MIR2_DATA_ROOT="$TEST_RUNTIME/ref"
start_backend() {
  "$ROOT/start-server.sh" > "$TEST_RUNTIME/backend.log" 2>&1 &
  BACKEND_PID=$!
  for ((attempt=0; attempt<120; attempt++)); do
    if grep -q "Game server ready" "$TEST_RUNTIME/backend.log"; then return; fi
    if ! kill -0 "$BACKEND_PID" 2>/dev/null; then cat "$TEST_RUNTIME/backend.log"; return 1; fi
    sleep 1
  done
  cat "$TEST_RUNTIME/backend.log"; return 1
}
run_client() {
  local args=(--path "$ROOT/src/Client" --max-fps 60)
  if [ "${MIR2_NATIVE_VISIBLE:-}" != 1 ]; then args=(--headless "${args[@]}"); fi
  args+=(-- "--server=http://127.0.0.1:$MIR2_RESOURCE_PORT" "--smoke-record=$TEST_RUNTIME/save.json" "$1")
  if [ "${MIR2_TEST_SCREENSHOTS:-}" = 1 ]; then args+=("--screenshot=$TEST_RUNTIME/client${1}.png"); fi
  if ! "$GODOT" "${args[@]}" > "$TEST_RUNTIME/client.log" 2>&1; then
    cat "$TEST_RUNTIME/client.log" "$TEST_RUNTIME/backend.log"; return 1
  fi
  cat "$TEST_RUNTIME/client.log"
  grep -q 'PASS native' "$TEST_RUNTIME/client.log"
}
start_backend
MIR2_SERVER="http://127.0.0.1:$MIR2_RESOURCE_PORT" python3 "$ROOT/tests/Management/verify.py" "$MIR2_DATA_ROOT"
run_client --smoke
kill -TERM "$BACKEND_PID"
wait "$BACKEND_PID"
BACKEND_PID=""
# Corrupt one cached map; the micro-client must detect it and fetch the server copy.
python3 - "$MIR2_CLIENT_CACHE" <<'PYCODE'
import hashlib, pathlib, sys
name=hashlib.sha256(b'map/0').hexdigest().upper()
files=list(pathlib.Path(sys.argv[1]).rglob(name))
assert files, 'No native map cache was produced'
for file in files: file.write_bytes(b'corrupt cached map')
PYCODE
start_backend
run_client --verify-save
echo "Passed actual Godot gameplay and SQLite restart checks using disposable data."
