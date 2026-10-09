#!/usr/bin/env bash
# WSL 에서 빌드해서 Windows 의 %LOCALAPPDATA%\Programs\SMCalendar 에 설치하고 실행한다.
#   ./build.sh          빌드 + 설치 + 실행
#   ./build.sh --demo   데모 데이터로 실행 (로그인 없이 화면 확인)
set -euo pipefail
cd "$(dirname "$0")"

DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

"$DOTNET" publish -c Release -o dist | grep -E "error|warn|->" || true

if [[ "${1:-}" == "--release" ]]; then
  # 다른 PC 용 zip (client_secret 등 개인 파일은 넣지 않는다)
  VERSION=$(grep -oP '(?<=<Version>)[^<]+' SMCalendar.csproj)
  mkdir -p release
  python3 - "$VERSION" <<'PY'
import sys, zipfile, pathlib
v = sys.argv[1]
out = pathlib.Path(f"release/SMCalendar-{v}-win-x64.zip")
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for f in ["SMCalendar.exe", "SMCalendar.dll", "SMCalendar.deps.json", "SMCalendar.runtimeconfig.json"]:
        z.write(f"dist/{f}", f"SMCalendar/{f}")
    for f in pathlib.Path("dist/fonts").iterdir():
        z.write(f, f"SMCalendar/fonts/{f.name}")
    z.writestr("SMCalendar/설치방법.txt",
        "1. .NET 10 Desktop Runtime (x64) 설치: https://dotnet.microsoft.com/download/dotnet/10.0\r\n"
        "2. SMCalendar 폴더를 %LOCALAPPDATA%\\Programs 에 복사 (다른 곳도 가능)\r\n"
        "3. SMCalendar.exe 실행 -> '구글 계정 연결하기' -> client_secret JSON 선택 -> 브라우저 로그인\r\n"
        "자세한 내용: https://github.com/1ovoz/SM_calendar\r\n")
print(out, out.stat().st_size // 1024, "KB")
PY
  exit 0
fi

WIN_LOCAL=$(cmd.exe /c "echo %LOCALAPPDATA%" 2>/dev/null | tr -d '\r')
DEST=$(wslpath "$WIN_LOCAL")/Programs/SMCalendar

powershell.exe -NoProfile -Command "Stop-Process -Name SMCalendar -ErrorAction SilentlyContinue" || true
sleep 1
mkdir -p "$DEST/fonts"
cp dist/SMCalendar.exe dist/SMCalendar.dll dist/SMCalendar.deps.json dist/SMCalendar.runtimeconfig.json "$DEST/"
cp dist/fonts/* "$DEST/fonts/"
echo "설치됨: $WIN_LOCAL\\Programs\\SMCalendar"

ARGS=""
[[ "${1:-}" == "--demo" ]] && ARGS="-ArgumentList '--demo'"
powershell.exe -NoProfile -Command "Start-Process \"$WIN_LOCAL\\Programs\\SMCalendar\\SMCalendar.exe\" $ARGS"
