#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
node -e 'require("playwright")' || { echo 'Provide installed Playwright through NODE_PATH.'; exit 1; }
TEST_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/mir2-admin-ui.XXXXXX")"
SERVER_PID="" ADMIN_PID=""
cleanup() {
    local status=$?
    for pid in "$ADMIN_PID" "$SERVER_PID"; do
        if [ -n "$pid" ]; then kill -TERM "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; fi
    done
    if [ "$status" != 0 ] || [ "${MIR2_TEST_KEEP:-}" = 1 ]; then echo "UI fixture and screenshots: $TEST_ROOT"; else find "$TEST_ROOT" -depth -delete; fi
}
trap cleanup EXIT
dotnet build "$ROOT/Mir2.sln" -c Release --nologo --verbosity quiet -m:1 --disable-build-servers
SOURCE="${MIR2_DATA_ROOT:-$ROOT/data/versions/default}"
MIR2_DATA_ROOT="$SOURCE" dotnet tests/Compatibility/bin/Release/net8.0/Compatibility.dll --fixture "$TEST_ROOT/data" --catalog-fixture
python3 - "$TEST_ROOT/data" "$SOURCE" <<'PY'
import gzip, os, pathlib, struct, sys
root, source = map(pathlib.Path, sys.argv[1:])
libraries = root/'assets/libraries'
libraries.unlink()
libraries.mkdir()
# 只读硬链接复用已有素材；只在独立的 Tests 目录创建测试素材。
for original in (source/'assets/libraries').rglob('*.Lib'):
    target = libraries/original.relative_to(source/'assets/libraries')
    target.parent.mkdir(parents=True, exist_ok=True)
    os.link(original, target)
folder = libraries/'Tests'
folder.mkdir()
for count in (0, 1, 20, 21, 41):
    frames, offsets, position = [], [], 8+count*4
    for index in range(count):
        w, h = 48, 24+(index % 5)*24
        data = gzip.compress(bytes((index*5, 150, 90, 255))*w*h)
        frame = struct.pack('<hhhh', w, h, 0, 0)+bytes(5)+struct.pack('<i', len(data))+data
        offsets.append(position); frames.append(frame); position += len(frame)
    (folder/f'Count{count}.Lib').write_bytes(struct.pack('<ii', 2, count)+b''.join(struct.pack('<i', i) for i in offsets)+b''.join(frames))
PY
export MIR2_DATA_ROOT="$TEST_ROOT/data" MIR2_RUNTIME_ROOT="$TEST_ROOT/data/runtime"
export MIR2_GAME_PORT="${MIR2_GAME_PORT:-17240}" MIR2_RESOURCE_PORT="${MIR2_RESOURCE_PORT:-15320}" MIR2_ADMIN_PORT="${MIR2_ADMIN_PORT:-15321}"
export MIR2_SERVER="http://127.0.0.1:$MIR2_RESOURCE_PORT"
if [ "${MIR2_MONSTER_UI:-}" = 1 ]; then
    mkdir -p "$MIR2_DATA_ROOT/world/Envir/Drops/公共/怪物/测试分类"
    python3 - "$MIR2_DATA_ROOT/world/Envir/Drops/公共/怪物/测试分类/测试掉落.txt" <<'PY'
import pathlib, sys
pathlib.Path(sys.argv[1]).write_text(';目录选择测试\n')
PY
fi
dotnet src/Server/bin/Release/net8.0/Mir2.Server.dll "--mir2-instance=$ROOT:$MIR2_RUNTIME_ROOT:server" > "$TEST_ROOT/server.log" 2>&1 & SERVER_PID=$!
dotnet src/Admin/bin/Release/net8.0/Mir2.Admin.dll "--mir2-instance=$ROOT:$MIR2_RUNTIME_ROOT:admin" > "$TEST_ROOT/admin.log" 2>&1 & ADMIN_PID=$!
if [ "${MIR2_SETTINGS_UI:-}" = 1 ]; then
    node tests/Management/settings.cjs "$TEST_ROOT"
elif [ "${MIR2_MONSTER_UI:-}" = 1 ]; then
    node tests/Management/monster-drops.cjs "$TEST_ROOT"
    kill -TERM "$SERVER_PID"; wait "$SERVER_PID"; SERVER_PID=""
    python3 tests/Management/drop-startup.py "$TEST_ROOT"
else
    node tests/Management/ui.cjs "$TEST_ROOT"
fi
