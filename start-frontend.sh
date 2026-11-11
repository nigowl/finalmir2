#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"
GODOT="${GODOT_BIN:-godot}"
if ! command -v "$GODOT" >/dev/null 2>&1 && [ -x /Applications/Godot_mono.app/Contents/MacOS/Godot ]; then
  GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot
fi
if ! command -v dotnet >/dev/null 2>&1 || ! command -v "$GODOT" >/dev/null 2>&1; then
  echo "请安装 .NET 8 SDK 和 Godot 4.7.2 .NET 版，并将 godot 加入 PATH（或设置 GODOT_BIN）。" >&2
  exit 1
fi
if [[ "$("$GODOT" --version)" != 4.7.2.*mono* ]]; then
  echo "本项目需要 Godot 4.7.2 .NET（mono）版，以匹配 Client.Godot.csproj 的 Godot.NET.Sdk。" >&2
  exit 1
fi
dotnet build "$ROOT/Client.Godot/Client.Godot.csproj" --nologo --verbosity quiet
exec "$GODOT" --path "$ROOT/Client.Godot" "$@"
