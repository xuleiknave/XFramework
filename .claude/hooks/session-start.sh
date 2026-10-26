#!/bin/bash
# 云端会话启动时安装 .NET SDK，用于跑内核测试：
#   dotnet run --project tests/Starline.Battle.Tests
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

if ! command -v dotnet >/dev/null 2>&1; then
  # 微软的下载站在云端网络策略里被拦截，改用 Ubuntu 官方源的 dotnet-sdk-8.0
  export DEBIAN_FRONTEND=noninteractive
  apt-get update -qq
  apt-get install -y -qq dotnet-sdk-8.0 >/dev/null
fi

echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1' >> "${CLAUDE_ENV_FILE:-/dev/null}"
echo 'export DOTNET_NOLOGO=1' >> "${CLAUDE_ENV_FILE:-/dev/null}"

cd "${CLAUDE_PROJECT_DIR:-.}"
dotnet build tests/Starline.Battle.Tests -nologo -v q
