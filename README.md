# 신시 · shinshi

고조선 건국 신화를 배경으로 한 PC 쿼터뷰 협동 액션 RPG. 공유 마을 신시에서 준비하고 1~4인으로 토벌과 보스전에 출정한다.

| 항목 | 내용 |
|---|---|
| 플랫폼 | Windows PC, 키보드·마우스 (클릭 이동, 커서 방향 공격) |
| 인원 | 솔로 또는 1~4인 파티 |
| 직업 | 무사 · 도술사 · 신령사 |

## 저장소 구성

| 경로 | 내용 |
|---|---|
| `Assets/` | Unity 프로젝트 (클라이언트, 전용 서버 코드, GameDB 데이터, 테스트) |
| `Backend/` | ASP.NET Core API, PostgreSQL 마이그레이션, 백엔드 테스트 ([안내](Backend/README.md)) |
| `Launcher/` | Windows 런처와 패치 게시 도구 ([안내](Launcher/README.md)) |
| `Web/` | 홈페이지 ([안내](Web/README.md)) |
| `deploy/`, `tools/` | 서버 배포 템플릿, 접속 시험 스크립트 ([배포 안내](deploy/README.md)) |
| `docs/` | 기획·기술 문서와 작업 규칙 |

## 개발 환경

- Unity 6000.4.7f1 (URP, Mirror). 처음 열 때는 [첫 실행 안내](docs/unity/UNITY_START_HERE.md)를 따른다.
- .NET 10 SDK, Docker Compose (백엔드)

## 테스트

Unity: **Window → General → Test Runner → EditMode → Run All**. 테스트는 `Assets/Tests/EditMode/`에 있다.

백엔드 (저장소 루트에서 실행):

```powershell
dotnet restore Backend
dotnet build Backend --no-restore

$taskPassword = Read-Host 'Local PostgreSQL bootstrap password' -AsSecureString
$env:POSTGRES_PASSWORD = [System.Net.NetworkCredential]::new('', $taskPassword).Password
docker compose -f Backend/docker-compose.yml up -d --wait

$env:ConnectionStrings__PostgresMigrator = '<마이그레이션 전용 연결 문자열>'
dotnet run --project Backend/src/Shinshi.Api -- --migrate
Remove-Item Env:ConnectionStrings__PostgresMigrator

$env:SHINSHI_TEST_CONNECTION_STRING = '<테스트 전용 DB 연결 문자열>'
dotnet test Backend --no-restore
```

`SHINSHI_TEST_CONNECTION_STRING`이 없으면 PostgreSQL 통합 테스트는 건너뛴다. 연결 문자열과 비밀번호는 저장소에 쓰지 않는다. DB 역할 분리와 API 실행은 [백엔드 안내](Backend/README.md)를 본다.

## 빌드

Unity 메뉴 **Shinshi → Build**에서 만든다.

| 메뉴 | 입력 씬 | 출력 |
|---|---|---|
| Windows Client (Development) | `Bootstrap`과 `Assets/00.Scenes/Release/`의 `Lobby`, `CharacterSelect`, `CharacterCreate`, `ShinshiTown` | `Builds/windows-client/` |
| Windows Server / Linux Server (Development) | `Bootstrap`, `Release/ShinshiTown` | `Builds/{대상}/` |
| Dev Movement → Windows Client / Windows Server (Development) | `Assets/00.Scenes/Dev_PlayerMovement.unity` | 위와 같은 위치 (덮어씀) |

명령줄 빌드:

```powershell
& '<Unity 설치 경로>\Editor\Unity.exe' -batchmode -projectPath (Get-Location).Path -executeMethod Shinshi.Editor.ShinshiBuild.BuildFromCommandLine -shinshiTarget windows-server -logFile 'server-build.log'
```

`-shinshiTarget`은 `windows-client`, `windows-server`, `linux-server`를 받고, `-shinshiRelease`를 붙이면 개발 옵션을 끈다. 개발 빌드 실행 파일은 `-dev-latency-ms <ms>`(보내는 메시지 지연)와 `-dev-autopilot`(클라이언트 자동 이동, 회피)을 받는다. Windows 서버 1개와 클라이언트 여러 개를 한 PC에서 띄우려면 [tools/Start-M2.ps1](tools/Start-M2.ps1)을 쓴다.

## 릴리즈 브랜치 실행

릴리즈 브랜치를 받은 PC에서 로비 → 캐릭터 선택 → 캐릭터 생성 → 마을까지 실제 전용 서버로 진행한다. Docker Desktop과 .NET 10 SDK가 필요하다. `Assets/99.ThirdParty/`의 외부 에셋은 Git에 없으므로 처음 받은 PC는 [에셋 대장](docs/licenses/ASSET_REGISTRY.md)대로 설치한 뒤 Unity로 한 번 열어 컴파일 오류가 없는지 확인한다.

```powershell
git switch release/0.1.0
.\tools\Start-ReleaseStack.ps1 -Build
```

- `-Build`는 Unity 에디터를 닫은 상태에서 Windows 서버·클라이언트 개발 빌드를 만든다. 이미 빌드가 있거나 메뉴로 만들었으면 `-Build`를 빼고 실행한다.
- 스크립트가 PostgreSQL 컨테이너(`shinshi-release-db`, 127.0.0.1:55440), 마이그레이션, API(5080), 마을 서버(7777, API 입장 토큰 검증)를 차례로 띄우고 클라이언트 1개를 연다. 클라이언트 수는 `-ClientCount`로 정한다.
- 처음에는 클라이언트 로비의 개발 로그인에 아이디, 비밀번호(8자 이상, 영문과 숫자), 이메일을 넣고 **가입 후 로그인**을 누른다. 다음부터는 **로그인**만 누른다. 이어서 캐릭터 생성, 게임 시작을 누르면 마을에 들어간다.
- 다른 PC에서 접속하려면 서버 PC에서 `-PublicAddress <서버 PC의 IP>`로 실행하고 방화벽에서 5080(TCP)과 7777(UDP)을 연다. 다른 PC는 `Builds\windows-client\Shinshi.exe -api-base-url http://<서버 PC의 IP>:5080`으로 실행한다.
- 비밀값(DB 비밀번호, 인증 키, 서버 서비스 토큰)은 `%LOCALAPPDATA%\Shinshi\release-stack`에 한 번 만들어 재사용하고 출력하지 않는다. 로그도 같은 곳에 남는다.
- 끝낼 때는 `.\tools\Start-ReleaseStack.ps1 -Stop`, DB까지 지우려면 `-Stop -RemoveDatabase`.
- 개발 빌드를 쓰는 이유: 출시 빌드 클라이언트는 런처가 넘긴 세션만 받는다. 리눅스 서버와 API 운영 배포는 [배포 안내](deploy/README.md)를 따른다.

에디터에서 시험할 때는 같은 스크립트를 `-ClientCount 0`으로 띄우고 메뉴 **Shinshi → Play → Client**로 플레이한다.

## 문서

처음에는 [문서 읽기 안내](docs/00-reading-guide.md)를 연다. 구현 상태는 [구현 로드맵](docs/05-implementation-roadmap.md)이 기준이다.

## 협업

작업 규칙은 [AGENTS.md](AGENTS.md), 커밋은 [커밋 컨벤션](docs/commit-convention.md), C# 코드는 [코딩 컨벤션](docs/coding-convention.md)을 따른다.
