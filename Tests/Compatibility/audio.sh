#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
SOURCE="${MIR2_DATA_ROOT:-$ROOT/data/versions/default}"
AUDIO_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mir2-audio.XXXXXX")"
export MIR2_DATA_ROOT="$AUDIO_ROOT/data" MIR2_RUNTIME_ROOT="$AUDIO_ROOT/data/runtime"
export MIR2_GAME_PORT="${MIR2_GAME_PORT:-17186}" MIR2_RESOURCE_PORT="${MIR2_RESOURCE_PORT:-15186}"
export MIR2_CLIENT_CACHE="$AUDIO_ROOT/cache" MIR2_CLIENT_SETTINGS="$AUDIO_ROOT/settings.json"
SERVER_PID=""
cleanup() {
  if [ -n "$SERVER_PID" ]; then kill -TERM "$SERVER_PID" 2>/dev/null || true; wait "$SERVER_PID" || true; fi
  echo "Audio evidence: $AUDIO_ROOT"
}
trap cleanup EXIT
dotnet build tests/Compatibility/Compatibility.csproj -c Release --no-restore --verbosity quiet
dotnet build src/Client/Client.Godot.csproj --no-restore --verbosity quiet
MIR2_DATA_ROOT="$SOURCE" dotnet tests/Compatibility/bin/Release/net8.0/Compatibility.dll --audio-checks --audio-report "$AUDIO_ROOT/audit.json"
MIR2_DATA_ROOT="$SOURCE" dotnet tests/Compatibility/bin/Release/net8.0/Compatibility.dll --fixture "$AUDIO_ROOT/data" --skill-fixture > "$AUDIO_ROOT/fixture.log" 2>&1
dotnet src/Server/bin/Release/net8.0/Mir2.Server.dll "--mir2-instance=$ROOT:$MIR2_RUNTIME_ROOT:server" > "$AUDIO_ROOT/server.log" 2>&1 & SERVER_PID=$!
for ((attempt=0; attempt<90; attempt++)); do
  if curl -fsS "http://127.0.0.1:$MIR2_RESOURCE_PORT/health" > /dev/null 2>&1; then break; fi
  if ! kill -0 "$SERVER_PID" 2>/dev/null; then cat "$AUDIO_ROOT/server.log"; exit 1; fi
  sleep 1
done
"${GODOT_BIN:-/Applications/Godot_mono.app/Contents/MacOS/Godot}" --path "$ROOT/src/Client" --max-fps 60 -- --server="http://127.0.0.1:$MIR2_RESOURCE_PORT" --verify-audio --audio-live --audio-assets="$SOURCE/assets/sounds" > "$AUDIO_ROOT/client.log" 2>&1
rg 'PASS native audio' "$AUDIO_ROOT/client.log"
