#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
CLASS_TEST_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mir2-skills-combos-XXXXXX")"
export MIR2_DATA_ROOT="${MIR2_DATA_ROOT:-$ROOT/data/versions/default}" MIR2_RUNTIME_ROOT="$CLASS_TEST_ROOT/runtime"
export MIR2_GAME_PORT="${MIR2_GAME_PORT:-17122}" MIR2_RESOURCE_PORT="${MIR2_RESOURCE_PORT:-15122}" MIR2_ADMIN_PORT="${MIR2_ADMIN_PORT:-15123}"
export MIR2_CLIENT_CACHE="$CLASS_TEST_ROOT/cache" MIR2_CLIENT_SETTINGS="$CLASS_TEST_ROOT/settings.json"
CLASS_TEST_PID=""
cleanup() {
  if [ -n "$CLASS_TEST_PID" ]; then kill -TERM "$CLASS_TEST_PID" 2>/dev/null || true; wait "$CLASS_TEST_PID" 2>/dev/null || true; fi
  echo "Class combo fixture: $CLASS_TEST_ROOT"
}
trap cleanup EXIT
dotnet build src/Client/Client.Godot.csproj --nologo --verbosity quiet
dotnet build Tests/Compatibility/Compatibility.csproj --nologo --verbosity quiet
dotnet Tests/Compatibility/bin/Debug/net8.0/Compatibility.dll --client-checks
dotnet Tests/Compatibility/bin/Debug/net8.0/Compatibility.dll --fixture "$CLASS_TEST_ROOT/ref" --skill-fixture
export MIR2_DATA_ROOT="$CLASS_TEST_ROOT/ref"
./start-server.sh > "$CLASS_TEST_ROOT/backend.log" 2>&1 &
CLASS_TEST_PID=$!
for ((attempt=0; attempt<90; attempt++)); do
  if rg -q "Game server ready" "$CLASS_TEST_ROOT/backend.log"; then break; fi
  if ! kill -0 "$CLASS_TEST_PID" 2>/dev/null; then cat "$CLASS_TEST_ROOT/backend.log"; exit 1; fi
  sleep 1
done
GODOT="${GODOT_BIN:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
if ! "$GODOT" --path "$ROOT/src/Client" --max-fps 60 -- "--mir2-instance=$ROOT:$MIR2_RUNTIME_ROOT:client" --class-combos --verify-combo-flow "--server=http://127.0.0.1:$MIR2_RESOURCE_PORT" "--screenshot=$CLASS_TEST_ROOT/screens" "$@" > "$CLASS_TEST_ROOT/client.log" 2>&1; then
  cat "$CLASS_TEST_ROOT/client.log"; exit 1
fi
cat "$CLASS_TEST_ROOT/client.log"
