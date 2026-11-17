#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
ACCOUNT_TEST_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mir2-accounts-XXXXXX")"
export MIR2_DATA_ROOT="${MIR2_DATA_ROOT:-$ROOT/data/versions/default}" MIR2_RUNTIME_ROOT="$ACCOUNT_TEST_ROOT/runtime"
export MIR2_GAME_PORT="${MIR2_GAME_PORT:-17124}" MIR2_RESOURCE_PORT="${MIR2_RESOURCE_PORT:-15124}" MIR2_ADMIN_PORT="${MIR2_ADMIN_PORT:-15125}"
export MIR2_CLIENT_CACHE="$ACCOUNT_TEST_ROOT/cache" MIR2_CLIENT_SETTINGS="$ACCOUNT_TEST_ROOT/settings.json"
ACCOUNT_TEST_PID=""
cleanup() {
  if [ -n "$ACCOUNT_TEST_PID" ]; then kill -TERM "$ACCOUNT_TEST_PID" 2>/dev/null || true; wait "$ACCOUNT_TEST_PID" 2>/dev/null || true; fi
  echo "Account form fixture: $ACCOUNT_TEST_ROOT"
}
trap cleanup EXIT
dotnet build src/Client/Client.Godot.csproj --nologo --verbosity quiet
dotnet build Tests/Compatibility/Compatibility.csproj --nologo --verbosity quiet
dotnet Tests/Compatibility/bin/Debug/net8.0/Compatibility.dll --fixture "$ACCOUNT_TEST_ROOT/ref"
export MIR2_DATA_ROOT="$ACCOUNT_TEST_ROOT/ref"
./start-server.sh > "$ACCOUNT_TEST_ROOT/backend.log" 2>&1 &
ACCOUNT_TEST_PID=$!
for ((attempt=0; attempt<90; attempt++)); do
  if rg -q "Game server ready" "$ACCOUNT_TEST_ROOT/backend.log"; then break; fi
  if ! kill -0 "$ACCOUNT_TEST_PID" 2>/dev/null; then cat "$ACCOUNT_TEST_ROOT/backend.log"; exit 1; fi
  sleep 1
done
GODOT="${GODOT_BIN:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
if ! "$GODOT" --path "$ROOT/src/Client" --max-fps 60 -- "--mir2-instance=$ROOT:$MIR2_RUNTIME_ROOT:client" --verify-accounts "--server=http://127.0.0.1:$MIR2_RESOURCE_PORT" "--screenshot=$ACCOUNT_TEST_ROOT/accounts.png" > "$ACCOUNT_TEST_ROOT/client.log" 2>&1; then
  cat "$ACCOUNT_TEST_ROOT/client.log"; exit 1
fi
cat "$ACCOUNT_TEST_ROOT/client.log"
rg -q 'PASS native account forms' "$ACCOUNT_TEST_ROOT/client.log"
