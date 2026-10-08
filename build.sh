#!/usr/bin/env bash
# WSL 에서 빌드해서 Windows 의 %LOCALAPPDATA%\Programs\SMCalendar 에 설치하고 실행한다.
#   ./build.sh          빌드 + 설치 + 실행
#   ./build.sh --demo   데모 데이터로 실행 (로그인 없이 화면 확인)
set -euo pipefail
cd "$(dirname "$0")"

DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

"$DOTNET" publish -c Release -o dist | grep -E "error|warn|->" || true

WIN_LOCAL=$(cmd.exe /c "echo %LOCALAPPDATA%" 2>/dev/null | tr -d '\r')
DEST=$(wslpath "$WIN_LOCAL")/Programs/SMCalendar

powershell.exe -NoProfile -Command "Stop-Process -Name SMCalendar -ErrorAction SilentlyContinue" || true
sleep 1
mkdir -p "$DEST"
cp dist/SMCalendar.exe dist/SMCalendar.dll dist/SMCalendar.deps.json dist/SMCalendar.runtimeconfig.json "$DEST/"
echo "설치됨: $WIN_LOCAL\\Programs\\SMCalendar"

ARGS=""
[[ "${1:-}" == "--demo" ]] && ARGS="-ArgumentList '--demo'"
powershell.exe -NoProfile -Command "Start-Process \"$WIN_LOCAL\\Programs\\SMCalendar\\SMCalendar.exe\" $ARGS"
