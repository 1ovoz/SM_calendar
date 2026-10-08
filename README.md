# SM Calendar

Google 캘린더와 양방향 동기화되는 가벼운 Windows 바탕화면 달력 위젯입니다. Me Calendar 를 참고해 만들었고, 광고가 없습니다.

- **가벼움**: .NET 10 WinForms를 쓰고, 컨트롤 없이 레이어드 창에 직접 그립니다. 시작해서 창이 뜨기까지 약 0.3초가 걸리고, 전용 메모리는 약 13MB이며, 대기 중에는 CPU를 거의 쓰지 않습니다.
- **바탕화면 고정**: 다른 창 아래에 깔리고, Win+D(바탕화면 보기)를 눌러도 사라지지 않습니다.
- **읽기/쓰기**: 일정을 추가·수정·삭제할 수 있고, 종일 일정과 반복 일정(해당 회차만)도 다룹니다. 다른 캘린더로 옮길 수도 있습니다.
- **빠른 부팅**: 마지막으로 받은 일정을 디스크에 캐시해 두기 때문에, 네트워크가 연결되기 전에도 바로 표시합니다. 네트워크가 없으면 15초부터 최대 5분 간격으로 자동 재시도합니다.

## 사용법

| 동작 | 방법 |
|---|---|
| 새 일정 | 날짜 칸을 **더블클릭**하거나 머리글의 `+` 를 누름 |
| 일정 수정/삭제 | 일정을 **클릭** |
| 하루 일정 전체 보기 | 날짜 숫자나 `+N개` 를 클릭 |
| 이전/다음 달 | `‹ ›` 버튼 또는 **마우스 휠** |
| 이동 | 머리글(제목 줄)을 드래그 |
| 크기 조절 | 오른쪽 아래 모서리 점을 드래그 |
| 설정 메뉴 | `⋯` 버튼, 위젯 우클릭, 트레이 아이콘 우클릭 |
| 다른 창에 가려졌을 때 | 트레이 아이콘을 좌클릭하면 잠깐 맨 앞으로 나옴 |
| 편집기에서 저장 | `Ctrl+Enter` |

메뉴에서 바꿀 수 있는 설정은 다음과 같습니다.

- 표시할 캘린더
- 배경 불투명도 (0~100%)
- 어두운/밝은 테마
- 주 시작 요일
- 동기화 주기
- 바탕화면 고정
- 위치/크기 잠금
- Windows 시작 시 실행

처음 실행하면 **Windows 시작 시 실행이 자동으로 켜집니다**. 끄려면 메뉴에서 해제하세요.

## Google 연동 설정 (최초 1회)

Google 은 개인 앱에도 OAuth 클라이언트를 요구하기 때문에, 본인 Google Cloud 프로젝트에서 클라이언트를 하나 만들어야 합니다. 비용은 들지 않습니다.

1. <https://console.cloud.google.com/> 에서 새 프로젝트를 만듭니다. 예: `SM Calendar`
2. **API 및 서비스 → 라이브러리**에서 `Google Calendar API` 를 검색해 **사용**을 누릅니다.
3. **Google 인증 플랫폼(OAuth 동의 화면)**에서 **시작하기**를 누르고 다음을 입력합니다.
   - 앱 이름, 사용자 지원 이메일
   - 대상: **외부(External)**
   - 연락처 이메일
4. **대상(Audience)** 화면에서 **앱 게시(Publish app)**를 눌러 상태를 **프로덕션**으로 바꿉니다.
   - ⚠️ "테스트" 상태로 두면 Google 이 로그인을 **7일마다 만료**시킵니다.
   - 프로덕션으로 바꾸면 로그인할 때 "확인되지 않은 앱" 경고가 뜹니다. 본인만 쓰는 앱이므로 **고급 → (앱 이름)(으)로 이동**을 눌러 진행하면 됩니다.
5. **클라이언트(Clients) → 클라이언트 만들기**로 이동합니다.
   - 애플리케이션 유형을 **데스크톱 앱**으로 고르고 만듭니다.
   - **JSON 다운로드**를 눌러 파일을 받습니다 (`client_secret_….json`).
6. 위젯 아래쪽의 **구글 계정 연결하기**를 클릭하고, 받은 JSON 파일을 선택합니다.
7. 브라우저에서 로그인한 뒤 권한을 허용하면 연결이 끝납니다.

요청하는 권한은 일정 읽기/쓰기(`calendar.events`)와 캘린더 목록 읽기(`calendar.readonly`), 두 가지뿐입니다.

## 빌드 / 설치

WSL 에서 빌드합니다. .NET 10 SDK 가 `~/.dotnet` 에 있어야 합니다.

```bash
./build.sh          # 빌드 → %LOCALAPPDATA%\Programs\SMCalendar 에 설치 → 실행
./build.sh --demo   # 로그인 없이 샘플 일정으로 실행
```

Windows 에서 직접 빌드하려면 다음 명령을 씁니다.

```powershell
dotnet publish -c Release -o dist
```

실행에는 .NET 10 Desktop Runtime 이 필요합니다. 이 PC 에는 이미 설치되어 있습니다.

## 파일 위치

데이터는 `%APPDATA%\SMCalendar\` 에 저장됩니다.

| 파일 | 내용 |
|---|---|
| `settings.json` | 위치, 크기, 테마 등 설정 |
| `cache.json` | 일정 캐시 (오프라인·부팅 직후 표시용) |
| `token.dat` | Google refresh token. DPAPI 로 암호화되어 현재 Windows 사용자만 복호화 가능 |
| `client_secret.json` | 가져온 OAuth 클라이언트 |

## 팁

- Windows 는 시작 프로그램을 로그인 후 몇 초 늦게 실행합니다. 이 지연을 없애려면 아래 레지스트리 값을 추가합니다.
  - 키: `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize`
  - 값: `StartupDelayInMSec` (DWORD) = `0`
- 대한민국 공휴일을 빨간 날로 표시하려면 Google 캘린더에서 **"대한민국의 휴일"** 캘린더를 구독하세요.

## 구조

```
src/
  Program.cs                 진입점 (단일 인스턴스)
  Core/
    GoogleAuth.cs            OAuth 2.0 루프백 + PKCE, DPAPI 토큰 저장
    CalendarApi.cs           Calendar REST v3 직접 호출 (gzip, 필요한 필드만 요청)
    CalendarService.cs       동기화 스케줄링, 쓰기 작업
    EventStore.cs            메모리 + 디스크 캐시
    AppSettings.cs, Models.cs, Native.cs ...
  UI/
    CalendarWidget.cs        레이어드 창 위젯 (그리기, 입력, 바탕화면 고정)
    EventEditorForm.cs       일정 편집기
    DayListForm.cs           하루 일정 목록
    TrayContext.cs           트레이 아이콘, 메뉴, 앱 수명
```
