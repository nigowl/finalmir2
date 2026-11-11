#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
ROLE="${1:?server, client or admin required}"; shift
cd "$ROOT"
export MIR2_PROJECT_ROOT="$ROOT"
export MIR2_DATA_ROOT="${MIR2_DATA_ROOT:-$ROOT/data/versions/default}"
mkdir -p "$MIR2_DATA_ROOT"
export MIR2_DATA_ROOT="$(cd -- "$MIR2_DATA_ROOT" && pwd -P)"
RUNTIME_ALIAS="${MIR2_RUNTIME_ROOT:-$MIR2_DATA_ROOT/runtime}"
export MIR2_RUNTIME_ROOT="$RUNTIME_ALIAS"
mkdir -p "$MIR2_RUNTIME_ROOT"
export MIR2_RUNTIME_ROOT="$(cd -- "$MIR2_RUNTIME_ROOT" && pwd -P)"
STATE="$MIR2_RUNTIME_ROOT/processes"; mkdir -p "$STATE"
IDENTITY="--mir2-instance=$ROOT:$MIR2_RUNTIME_ROOT:$ROLE"
ALIAS_IDENTITY="--mir2-instance=$ROOT:$RUNTIME_ALIAS:$ROLE"
LOCK="$STATE/$ROLE.starting"
if ! mkdir "$LOCK" 2>/dev/null; then
  # Another launcher may have created the directory just before writing its PID.
  for ((attempt=0; attempt<20; attempt++)); do [ ! -d "$LOCK" ] || [ -s "$LOCK/pid" ] && break; sleep 0.1; done
  OWNER="$(cat "$LOCK/pid" 2>/dev/null || true)"
  OWNER_COMMAND=""
  if [[ "$OWNER" =~ ^[0-9]+$ ]]; then OWNER_COMMAND="$(ps -ww -p "$OWNER" -o command= 2>/dev/null || true)"; fi
  if [[ "$OWNER_COMMAND" == *"$ROOT/scripts/launch.sh $ROLE"* ]]; then echo "$ROLE 正在启动，请稍候。" >&2; exit 1; fi
  rm -rf -- "$LOCK"; mkdir "$LOCK"
fi
printf '%s\n' "$$" > "$LOCK/pid"
trap 'rm -rf -- "$LOCK"' EXIT
command -v dotnet >/dev/null 2>&1 || { echo "请安装 .NET 8 SDK。" >&2; exit 1; }
case "$ROLE" in
  server) PROJECT="$ROOT/src/Server/Mir2.Server.csproj"; DLL="$ROOT/src/Server/bin/Release/net8.0/Mir2.Server.dll"; CONFIG=Release ;;
  admin) PROJECT="$ROOT/src/Admin/Mir2.Admin.csproj"; DLL="$ROOT/src/Admin/bin/Release/net8.0/Mir2.Admin.dll"; CONFIG=Release ;;
  client)
    PROJECT="$ROOT/src/Client/Client.Godot.csproj"; CONFIG=Debug
    GODOT="${GODOT_BIN:-godot}"
    if ! command -v "$GODOT" >/dev/null 2>&1 && [ -x /Applications/Godot_mono.app/Contents/MacOS/Godot ]; then GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot; fi
    command -v "$GODOT" >/dev/null 2>&1 || { echo "请安装 Godot 4.7.2 .NET 版。" >&2; exit 1; }
    [[ "$("$GODOT" --version)" == 4.7.2.*mono* ]] || { echo "需要 Godot 4.7.2 .NET（mono）版。" >&2; exit 1; }
    ;;
  *) echo "Unknown role: $ROLE" >&2; exit 1 ;;
esac
# Build first, so a compilation failure never stops a working instance.
dotnet build "$PROJECT" --configuration "$CONFIG" --nologo --verbosity quiet -m:1 --disable-build-servers
owned() {
  local pid="$1" command executable
  [[ "$pid" =~ ^[0-9]+$ ]] && [ "$pid" != "$$" ] || return 1
  command="$(ps -ww -p "$pid" -o command= 2>/dev/null || true)"
  executable="$(ps -p "$pid" -o comm= 2>/dev/null || true)"
  [[ "$command" == *"$IDENTITY"* || "$command" == *"$ALIAS_IDENTITY"* ]] && return 0
  # Resolve aliases such as /tmp and /private/tmp before adopting an older launcher PID.
  if [[ "$command" == *"--mir2-instance=$ROOT:"* ]]; then
    local marker="${command#*--mir2-instance=$ROOT:}"
    marker="${marker%:$ROLE*}"
    if [ -d "$marker" ] && [[ "$(cd -- "$marker" && pwd -P)" == "$MIR2_RUNTIME_ROOT" ]]; then return 0; fi
  fi
  # Adopt a manually started default instance, but never another configured runtime.
  [[ "$MIR2_RUNTIME_ROOT" == "$ROOT/data/versions/default/runtime" && "$command" != *--mir2-instance=* ]] || return 1
  case "$ROLE:$executable" in
    server:*dotnet*) [[ "$command" == *"$DLL"* || "$command" == *"$ROOT/Server.Console/bin/"* ]] ;;
    admin:*dotnet*) [[ "$command" == *"$DLL"* ]] ;;
    client:*Godot*|client:*godot*) [[ "$command" == *"$ROOT/src/Client"* || "$command" == *"$ROOT/Client.Godot"* ]] ;;
    *) return 1 ;;
  esac
}
PIDS="$(cat "$STATE/$ROLE.pid" 2>/dev/null || true)"
while read -r candidate command; do
  [[ "$command" == *"$ROOT/"* ]] || continue
  if owned "$candidate"; then PIDS="$PIDS $candidate"; fi
done < <(ps -ww -axo pid=,command=)
for pid in $(printf '%s\n' $PIDS | sort -un); do
  if ! owned "$pid"; then continue; fi
  echo "正在停止旧 ${ROLE} 进程 ${pid}（先正常退出并保存）…"
  kill -TERM "$pid" 2>/dev/null || true
  for ((attempt=0; attempt<600; attempt++)); do
    if ! kill -0 "$pid" 2>/dev/null || ! owned "$pid"; then break; fi
    sleep 0.1
  done
  if owned "$pid"; then echo "旧进程 60 秒内未退出，强制结束 ${pid}。" >&2; kill -KILL "$pid" 2>/dev/null || true; fi
done
printf '%s\n' "$$" > "$STATE/$ROLE.pid"
rm -rf -- "$LOCK"; trap - EXIT
if [ "$ROLE" = client ]; then
  HAS_SEPARATOR=false
  for argument in "$@"; do [ "$argument" != -- ] || HAS_SEPARATOR=true; done
  if "$HAS_SEPARATOR"; then exec "$GODOT" --path "$ROOT/src/Client" "$@" "$IDENTITY";
  else exec "$GODOT" --path "$ROOT/src/Client" "$@" -- "$IDENTITY"; fi
else
  exec dotnet "$DLL" "$IDENTITY" "$@"
fi
