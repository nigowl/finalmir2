#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
OUTPUT="${1:-$ROOT/build}"
mkdir -p "$OUTPUT"; OUTPUT="$(cd -- "$OUTPUT" && pwd -P)"
case "$(uname -s):$(uname -m)" in
  Darwin:arm64) RID=osx-arm64 ;;
  Darwin:x86_64) RID=osx-x64 ;;
  *) echo "当前发布脚本面向 macOS。" >&2; exit 1 ;;
esac
GODOT="${GODOT_BIN:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
[[ -x "$GODOT" && "$("$GODOT" --version)" == 4.7.2.*mono* ]] || { echo "需要 Godot 4.7.2 .NET 应用。" >&2; exit 1; }
GODOT_APP="$(cd -- "$(dirname -- "$GODOT")/../.." && pwd -P)"
[ -f "$GODOT_APP/Contents/Info.plist" ] || { echo "GODOT_BIN 必须指向 .app 内的 Godot 可执行文件。" >&2; exit 1; }
python3 - "$OUTPUT" <<'PY'
import pathlib, subprocess, sys
root = pathlib.Path(sys.argv[1])
for command in subprocess.check_output(['ps', '-ax', '-o', 'command='], text=True).splitlines():
    if any(str(root / role) + '/' in command for role in ('server', 'admin', 'client')):
        raise SystemExit('发布目录中的程序仍在运行，请先正常停止后重新发布。')
PY
STAGE="$(mktemp -d "$OUTPUT/.publish.XXXXXX")"
INSTALLED=""
cleanup() {
  local status=$?
  if [ "$status" != 0 ]; then
    for role in server admin client; do
      if [ -d "$STAGE/previous-$role" ]; then
        if [ -d "$OUTPUT/$role" ]; then find "$OUTPUT/$role" -depth -delete; fi
        mv "$STAGE/previous-$role" "$OUTPUT/$role"
      elif [[ "$INSTALLED" == *" $role "* ]]; then find "$OUTPUT/$role" -depth -delete; fi
    done
  fi
  find "$STAGE" -depth -delete
}
trap cleanup EXIT
for role in Server Admin; do
  lower="$(printf '%s' "$role" | tr '[:upper:]' '[:lower:]')"
  dotnet publish "$ROOT/src/$role/Mir2.$role.csproj" -c Release -r "$RID" --self-contained true \
    -p:DebugType=None -p:DebugSymbols=false -o "$STAGE/$lower" --nologo -v quiet -m:1 --disable-build-servers
  if [ -f "$OUTPUT/$lower/appsettings.json" ]; then cp "$OUTPUT/$lower/appsettings.json" "$STAGE/$lower/appsettings.json";
  else printf '%s\n' '{ "Mir2": { "VersionDirectory": "../../data/versions/default", "ServerUrl": "http://127.0.0.1:5080" } }' > "$STAGE/$lower/appsettings.json"; fi
  cat > "$STAGE/$lower/start.sh" <<EOF
#!/usr/bin/env bash
set -euo pipefail
HERE="\$(cd -- "\$(dirname -- "\${BASH_SOURCE[0]}")" && pwd)"
exec "\$HERE/Mir2.$role" "\$@"
EOF
done
dotnet publish "$ROOT/src/Companion/Mir2.Companion.csproj" -c Release -r "$RID" --self-contained true \
  -p:DebugType=None -p:DebugSymbols=false -o "$STAGE/server/companion" --nologo -v quiet -m:1 --disable-build-servers
cp "$ROOT/LICENSE" "$ROOT/LICENSE-GPL-3.0.txt" "$ROOT/THIRD-PARTY-NOTICES.md" "$STAGE/server/companion/"
cp "$STAGE/server/Mir2.Shared.dll" "$STAGE/server/companion/Mir2.Shared.dll"
mkdir -p "$STAGE/client/.godot/mono/temp/bin/Debug"
# shortcut: 当前随包携带已安装的 .NET 宿主，具备标准导出模板后改为精简导出。
ditto "$GODOT_APP" "$STAGE/client/Godot.app"
"$GODOT" --headless --path "$ROOT/src/Client" --export-pack macOS "$STAGE/client/CrystalMir2.pck"
dotnet build "$ROOT/src/Client/Client.Godot.csproj" -c Debug -p:Optimize=true -p:DebugType=None \
  --nologo -v quiet -m:1 --disable-build-servers
cp "$ROOT/src/Client/.godot/mono/temp/bin/Debug/"*.dll "$ROOT/src/Client/.godot/mono/temp/bin/Debug/"*.json "$STAGE/client/.godot/mono/temp/bin/Debug/"
cat > "$STAGE/client/start.sh" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
HERE="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
exec "$HERE/Godot.app/Contents/MacOS/Godot" --path "$HERE" --main-pack "$HERE/CrystalMir2.pck" "$@"
EOF
for role in server admin client; do
  chmod +x "$STAGE/$role/start.sh"
  cp "$ROOT/LICENSE" "$ROOT/THIRD-PARTY-NOTICES.md" "$STAGE/$role/"
  if [ -d "$OUTPUT/$role" ]; then mv "$OUTPUT/$role" "$STAGE/previous-$role"; fi
  mv "$STAGE/$role" "$OUTPUT/$role"
  INSTALLED="$INSTALLED $role "
done
echo "已发布三端：$OUTPUT/{server,admin,client}"
echo "服务端、管理端在各自 appsettings.json 中配置 Mir2.VersionDirectory；相对路径以程序目录为准。"
