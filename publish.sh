#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
OUTPUT="$ROOT/build"; RID=""; SERVER_ONLY=0; OUTPUT_SET=0
while [ "$#" -gt 0 ]; do
  case "$1" in
    --server-only) SERVER_ONLY=1 ;;
    --runtime) [ "$#" -ge 2 ] || { echo "--runtime 缺少目标平台。" >&2; exit 1; }; RID="$2"; shift ;;
    --help) echo "用法：./publish.sh [输出目录] [--server-only] [--runtime linux-x64|linux-arm64|osx-arm64|osx-x64]"; exit 0 ;;
    -*) echo "未知参数：$1" >&2; exit 1 ;;
    *) [ "$OUTPUT_SET" = 0 ] || { echo "只能指定一个输出目录。" >&2; exit 1; }; OUTPUT="$1"; OUTPUT_SET=1 ;;
  esac
  shift
done
mkdir -p "$OUTPUT"; OUTPUT="$(cd -- "$OUTPUT" && pwd -P)"
[ "$OUTPUT" != "$ROOT" ] || { echo "输出目录不能是源码根目录。" >&2; exit 1; }
if [ -z "$RID" ]; then
  case "$(uname -s):$(uname -m)" in
    Darwin:arm64) RID=osx-arm64 ;;
    Darwin:x86_64) RID=osx-x64 ;;
    Linux:x86_64) RID=linux-x64 ;;
    Linux:aarch64) RID=linux-arm64 ;;
    *) echo "请用 --runtime 指定支持的目标平台。" >&2; exit 1 ;;
  esac
fi
case "$RID" in linux-x64|linux-arm64|osx-arm64|osx-x64) ;; *) echo "不支持的目标平台：$RID" >&2; exit 1 ;; esac
ROLES=(server admin)
if [ "$SERVER_ONLY" = 0 ]; then
  [[ "$(uname -s)" = Darwin && "$RID" = osx-* ]] || { echo "客户端发布目前仅支持 macOS；服务器发布请加 --server-only。" >&2; exit 1; }
  GODOT="${GODOT_BIN:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
  [[ -x "$GODOT" && "$("$GODOT" --version)" == 4.7.2.*mono* ]] || { echo "需要 Godot 4.7.2 .NET 应用。" >&2; exit 1; }
  GODOT_APP="$(cd -- "$(dirname -- "$GODOT")/../.." && pwd -P)"
  [ -f "$GODOT_APP/Contents/Info.plist" ] || { echo "GODOT_BIN 必须指向 .app 内的 Godot 可执行文件。" >&2; exit 1; }
  ROLES+=(client)
fi
python3 - "$OUTPUT" "${ROLES[@]}" <<'PY'
import pathlib, subprocess, sys
root = pathlib.Path(sys.argv[1])
for command in subprocess.check_output(['ps', '-ax', '-o', 'command='], text=True).splitlines():
    if any(str(root / role) + '/' in command for role in sys.argv[2:]):
        raise SystemExit('发布目录中的程序仍在运行，请先正常停止后重新发布。')
PY
mkdir -p "$ROOT/build"
LOCK="$ROOT/build/.publish.lock"
mkdir "$LOCK" 2>/dev/null || { echo "已有发布正在进行；确认无发布进程后才可移除 ${LOCK}。" >&2; exit 1; }
STAGE=""; INSTALLED=""; VERSION_TEMP=""
cleanup() {
  local status=$?
  if [ -n "$STAGE" ]; then
    if [ "$status" != 0 ]; then
      for role in "${ROLES[@]}"; do
        if [ -d "$STAGE/previous-$role" ]; then
          if [ -d "$OUTPUT/$role" ]; then rm -rf -- "$OUTPUT/$role"; fi
          mv "$STAGE/previous-$role" "$OUTPUT/$role"
        elif [[ "$INSTALLED" == *" $role "* ]]; then rm -rf -- "$OUTPUT/$role"; fi
      done
    fi
    rm -rf -- "$STAGE"
  fi
  if [ -n "$VERSION_TEMP" ]; then rm -f -- "$VERSION_TEMP"; fi
  rmdir "$LOCK"
  exit "$status"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
VERSION="$(python3 - "$ROOT/VERSION" <<'PY'
import pathlib, re, sys
value = pathlib.Path(sys.argv[1]).read_text().strip()
if not re.fullmatch(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)', value):
    raise SystemExit('VERSION 必须为三个非负整数，例如 1.0.0。')
major, minor, patch = map(int, value.split('.'))
if max(major, minor, patch + 1) > 65534: raise SystemExit('程序版本超出程序集允许范围，请先调整 VERSION。')
print(f'{major}.{minor}.{patch + 1}')
PY
)"
STAGE="$(mktemp -d "$OUTPUT/.publish.XXXXXX")"
SOURCE="$STAGE/source"
# 正在运行的开发实例可能引用源码输出，发布始终在独立副本编译。
EXCLUDES=(--exclude=/.git/ --exclude=/data/ --exclude=/build/ --exclude=bin/ --exclude=obj/ --exclude=.godot/ --exclude=.mono/)
if [[ "$OUTPUT" == "$ROOT/"* ]]; then EXCLUDES+=("--exclude=/${OUTPUT#"$ROOT/"}/"); fi
rsync -a "${EXCLUDES[@]}" "$ROOT/" "$SOURCE/"
printf '%s\n' "$VERSION" > "$SOURCE/VERSION"
export MIR2_DATA_ROOT="$(python3 -c 'import os, sys; print(os.path.abspath(sys.argv[1]))' "${MIR2_DATA_ROOT:-$ROOT/data/versions/default}")"
cd "$SOURCE"
echo "正在发布程序 v${VERSION}（${RID}）：${ROLES[*]}"
for role in Server Admin; do
  lower="$(printf '%s' "$role" | tr '[:upper:]' '[:lower:]')"
  dotnet publish "$SOURCE/src/$role/Mir2.$role.csproj" -c Release -r "$RID" --self-contained true -p:Version="$VERSION" \
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
dotnet publish "$SOURCE/src/Companion/Mir2.Companion.csproj" -c Release -r "$RID" --self-contained true -p:Version="$VERSION" \
  -p:DebugType=None -p:DebugSymbols=false -o "$STAGE/server/companion" --nologo -v quiet -m:1 --disable-build-servers
cp "$ROOT/LICENSE" "$ROOT/LICENSE-GPL-3.0.txt" "$ROOT/THIRD-PARTY-NOTICES.md" "$STAGE/server/companion/"
cp "$STAGE/server/Mir2.Shared.dll" "$STAGE/server/companion/Mir2.Shared.dll"
if [ "$SERVER_ONLY" = 0 ]; then
mkdir -p "$STAGE/client/.godot/mono/temp/bin/Debug"
# shortcut: 当前随包携带已安装的 .NET 宿主，具备标准导出模板后改为精简导出。
ditto "$GODOT_APP" "$STAGE/client/Godot.app"
"$GODOT" --headless --path "$SOURCE/src/Client" --export-pack macOS "$STAGE/client/CrystalMir2.pck"
dotnet build "$SOURCE/src/Client/Client.Godot.csproj" -c Debug -p:Version="$VERSION" -p:Optimize=true -p:DebugType=None -p:RequireClientStartupAssets=true \
  --nologo -v quiet -m:1 --disable-build-servers
cp "$SOURCE/src/Client/.godot/mono/temp/bin/Debug/"*.dll "$SOURCE/src/Client/.godot/mono/temp/bin/Debug/"*.json "$STAGE/client/.godot/mono/temp/bin/Debug/"
cat > "$STAGE/client/start.sh" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
HERE="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
exec "$HERE/Godot.app/Contents/MacOS/Godot" --path "$HERE" --main-pack "$HERE/CrystalMir2.pck" "$@"
EOF
fi
for role in "${ROLES[@]}"; do
  chmod +x "$STAGE/$role/start.sh"
  printf '%s\n' "$VERSION" > "$STAGE/$role/VERSION"
  cp "$ROOT/LICENSE" "$ROOT/THIRD-PARTY-NOTICES.md" "$STAGE/$role/"
  if [ -d "$OUTPUT/$role" ]; then mv "$OUTPUT/$role" "$STAGE/previous-$role"; fi
  mv "$STAGE/$role" "$OUTPUT/$role"
  INSTALLED="$INSTALLED $role "
done
VERSION_TEMP="$(mktemp "$ROOT/.VERSION.XXXXXX")"
printf '%s\n' "$VERSION" > "$VERSION_TEMP"
chmod 644 "$VERSION_TEMP"
mv "$VERSION_TEMP" "$ROOT/VERSION"
VERSION_TEMP=""
echo "已发布程序 v${VERSION}：${OUTPUT}（${ROLES[*]}）"
echo "服务端、管理端在各自 appsettings.json 中配置 Mir2.VersionDirectory；相对路径以程序目录为准。"
