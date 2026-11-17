#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
if [ "$#" -gt 0 ] && [[ "$1" != -* ]]; then
  if [ "$#" -lt 2 ]; then echo '用法：./start-client.sh [账号 密码] [Godot 参数]' >&2; exit 2; fi
  export MIR2_LOGIN_ACCOUNT="$1" MIR2_LOGIN_PASSWORD="$2"
  shift 2
fi
exec "$ROOT/scripts/launch.sh" client "$@"
