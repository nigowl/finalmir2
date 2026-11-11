#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"
if ! command -v dotnet >/dev/null 2>&1; then
  echo "请先安装 .NET 8 SDK：https://dotnet.microsoft.com/download/dotnet/8.0" >&2
  exit 1
fi
export MIR2_DATA_ROOT="${MIR2_DATA_ROOT:-$ROOT/Data/ref}"
export MIR2_RUNTIME_ROOT="${MIR2_RUNTIME_ROOT:-$ROOT/Data/runtime}"
dotnet build "$ROOT/Server.Console/Server.Console.csproj" --configuration Release --nologo --verbosity quiet
exec dotnet "$ROOT/Server.Console/bin/Release/net8.0/Server.Console.dll"
