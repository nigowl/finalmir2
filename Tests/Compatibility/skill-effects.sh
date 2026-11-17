#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
SKILL_TEST_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mir2-skills-XXXXXX")"
export MIR2_DATA_ROOT="${MIR2_DATA_ROOT:-$ROOT/data/versions/default}"
export MIR2_RUNTIME_ROOT="$SKILL_TEST_ROOT/runtime"
export MIR2_GAME_PORT="${MIR2_GAME_PORT:-17012}"
export MIR2_RESOURCE_PORT="${MIR2_RESOURCE_PORT:-15092}"
export MIR2_CLIENT_CACHE="$SKILL_TEST_ROOT/cache"
export MIR2_CLIENT_SETTINGS="$SKILL_TEST_ROOT/settings.json"
SKILL_TEST_PID=""
cleanup() {
  if [ -n "$SKILL_TEST_PID" ]; then kill -TERM "$SKILL_TEST_PID" 2>/dev/null || true; wait "$SKILL_TEST_PID" 2>/dev/null || true; fi
  if [ "${MIR2_TEST_KEEP:-}" = 1 ]; then echo "Skill fixture: $SKILL_TEST_ROOT"; else find "$SKILL_TEST_ROOT" -depth -delete; fi
}
trap cleanup EXIT
dotnet build Mir2.sln -c Debug --nologo --verbosity quiet -m:1 --disable-build-servers
dotnet tests/Compatibility/bin/Debug/net8.0/Compatibility.dll --skill-assets
dotnet tests/Compatibility/bin/Debug/net8.0/Compatibility.dll --fixture "$SKILL_TEST_ROOT/ref" --skill-fixture
export MIR2_DATA_ROOT="$SKILL_TEST_ROOT/ref"
./start-server.sh > "$SKILL_TEST_ROOT/backend.log" 2>&1 &
SKILL_TEST_PID=$!
for ((attempt=0; attempt<120; attempt++)); do
  if rg -q "Game server ready" "$SKILL_TEST_ROOT/backend.log"; then break; fi
  if ! kill -0 "$SKILL_TEST_PID" 2>/dev/null; then cat "$SKILL_TEST_ROOT/backend.log"; exit 1; fi
  sleep 1
done
GODOT="${GODOT_BIN:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
if ! "$GODOT" --path "$ROOT/src/Client" --max-fps 60 -- "--mir2-instance=$ROOT:$MIR2_RUNTIME_ROOT:client" --spell-effects "--server=http://127.0.0.1:$MIR2_RESOURCE_PORT" "--screenshot=$SKILL_TEST_ROOT/screens" > "$SKILL_TEST_ROOT/client.log" 2>&1; then
  cat "$SKILL_TEST_ROOT/client.log" "$SKILL_TEST_ROOT/backend.log"; exit 1
fi
cat "$SKILL_TEST_ROOT/client.log"
rg -q 'PASS native skill effects' "$SKILL_TEST_ROOT/client.log"
