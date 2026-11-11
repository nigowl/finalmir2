#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
TEST_RUNTIME="$(mktemp -d "${TMPDIR:-/tmp}/mir2-native.XXXXXX")"
export MIR2_DATA_ROOT="${MIR2_DATA_ROOT:-$ROOT/Data/ref}"
export MIR2_RUNTIME_ROOT="$TEST_RUNTIME/runtime"
export MIR2_GAME_PORT="${MIR2_GAME_PORT:-17010}"
export MIR2_RESOURCE_PORT="${MIR2_RESOURCE_PORT:-15090}"
export MIR2_CLIENT_CACHE="$TEST_RUNTIME/cache"
GODOT="${GODOT_BIN:-godot}"
if ! command -v "$GODOT" >/dev/null 2>&1 && [ -x /Applications/Godot_mono.app/Contents/MacOS/Godot ]; then
  GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot
fi
BACKEND_PID=""
cleanup() {
  if [ -n "$BACKEND_PID" ]; then kill -TERM "$BACKEND_PID" 2>/dev/null || true; wait "$BACKEND_PID" 2>/dev/null || true; fi
  # find never follows the reference-data symlinks created by the fixture.
  find "$TEST_RUNTIME" -depth -delete
}
trap cleanup EXIT
dotnet build "$ROOT/Mir2.Mac.sln" --configuration Debug --nologo --verbosity quiet
dotnet "$ROOT/Tests/MacCompatibility/bin/Debug/net8.0/MacCompatibility.dll" --fixture "$TEST_RUNTIME/ref"
export MIR2_DATA_ROOT="$TEST_RUNTIME/ref"
start_backend() {
  dotnet "$ROOT/Server.Console/bin/Debug/net8.0/Server.Console.dll" > "$TEST_RUNTIME/backend.log" 2>&1 &
  BACKEND_PID=$!
  for ((attempt=0; attempt<120; attempt++)); do
    if grep -q "Game server ready" "$TEST_RUNTIME/backend.log"; then return; fi
    if ! kill -0 "$BACKEND_PID" 2>/dev/null; then cat "$TEST_RUNTIME/backend.log"; return 1; fi
    sleep 1
  done
  cat "$TEST_RUNTIME/backend.log"; return 1
}
run_client() {
  if ! "$GODOT" --headless --path "$ROOT/Client.Godot" --max-fps 60 -- \
    "--server=http://127.0.0.1:$MIR2_RESOURCE_PORT" "--smoke-record=$TEST_RUNTIME/save.json" "$1" > "$TEST_RUNTIME/client.log" 2>&1; then
    cat "$TEST_RUNTIME/client.log" "$TEST_RUNTIME/backend.log"; return 1
  fi
  cat "$TEST_RUNTIME/client.log"
  grep -q 'PASS native' "$TEST_RUNTIME/client.log"
}
start_backend
run_client --smoke
kill -TERM "$BACKEND_PID"
wait "$BACKEND_PID"
BACKEND_PID=""
start_backend
run_client --verify-save
echo "Passed actual Godot gameplay and SQLite restart checks using disposable data."
