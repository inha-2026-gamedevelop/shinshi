# 신시 · shinshi

신화 시대의 신시를 중심으로 하는 PC 쿼터뷰 협동 액션 RPG. 공유 마을에서 준비하고 1~4인 토벌·보스전에 출정한다.

## 게임 소개

고조선 건국 신화의 초창기, 인간의 나라가 아직 서지 않은 때. 인간을 가르치던 여우 **월백**은 인간이 되겠다는 약속이 미뤄지자 산귀들을 이끌고 인간의 땅을 넘본다. 플레이어는 신시의 토벌대가 되어 산귀의 공세를 막고, 월백의 결계에 도전한다.

| 항목 | 내용 |
|---|---|
| 장르 | 3D 쿼터뷰 협동 액션 RPG |
| 플랫폼 | Windows PC, 키보드·마우스 (클릭 이동, 커서 방향 공격) |
| 인원 | 솔로 또는 1~4인 파티, 공유 마을 신시 |
| 직업 | 무사 · 도술사 · 신령사 (파티 내 중복 허용) |
| 미술 | 스타일화 3D, 부적·달빛·수묵의 인상 |

### 이렇게 플레이한다

1. **신시에서 준비한다.** 정기로 수련하고, 장비를 제작·강화하고, 부적으로 기술을 바꾼다. 생활 구역에서 약초를 캐 물약을 만든다.
2. **숲을 토벌한다.** 6웨이브, 한 판 5~6분. 정기·강화 재료·부적을 모으고, 낮은 확률로 보스 입장권이 나온다.
3. **보스에 도전한다.** 파티원이 입장권을 1장씩 써서 월백과 싸운다. 처치하면 기본 보상과 재료를 받고, 희귀 재료는 파티 경매로 나눈다.
4. **다시 신시로.** 얻은 재료로 장비와 부적을 키워 더 강한 빌드로 출정한다.

### 전투의 핵심

- 기본 공격과 직업별 일반 기술 4개, 무적 없는 이동 회피. 공격 예고를 읽고 위치로 살아남는다.
- 같은 기술도 어떤 부적을 붙이느냐에 따라 역할과 운용이 달라진다. 부적 구성은 신시에서만 바꿀 수 있어 출정 전 선택이 중요하다.
- 탱커·힐러가 필수가 아니다. 혼자서도, 같은 직업 넷이서도 출정할 수 있다.
- 레벨과 경험치 대신 정기 수련·장비·부적으로 성장한다.

### 현재 상태

첫 외부 체험판은 숲 전장 1개와 보스 월백(이지) 1종을 목표로 한다. 2026-10-02 작업 트리에는 서버 M-A의 인증·입장·재접속, 플레이어 이동·전투·스킬, 던전 세션·저장 큐·정산 코드와 GameDB 기반이 있다. `Backend/`에는 ASP.NET Core .NET 10 API, Npgsql+Dapper 저장소, PostgreSQL 마이그레이션 `0001`~`0007`이 있고, `deploy/`와 `tools/Start-M2.ps1`에는 배포·접속 시험 준비 파일이 있다. 전체 게임·UI·보스·운영 도구가 구현된 것은 아니며 항목별 범위는 [구현 로드맵](docs/05-implementation-roadmap.md)을 따른다.

제공된 검증 결과는 Unity Test Runner의 EditMode **780건 통과**, Docker의 실제 PostgreSQL 18을 사용한 백엔드 테스트 **396건 통과**다. 2026-10-02에는 EditMode **789건 통과**와 함께, 개발 이동 씬으로 만든 Windows 개발 서버 빌드 1개와 클라이언트 빌드 2개를 같은 PC에서 town 역할로 실행해 접속, 클릭 이동 예측, 회피를 지연 시뮬레이션 RTT 0/50/150ms에서 확인했다(M2, 자동 조작). Linux 서버·다른 PC(M3)·IL2CPP·배포·dungeon 역할 다인 접속은 실행하지 않았으므로 해당 경로는 **구현됨·실행 미검증**이다. 문서의 초기 밸런스 수치는 플레이테스트 대상이다.

## 빌드·테스트

명령은 저장소 루트에서 실행한다. 백엔드에는 .NET 10 SDK와 Docker Compose가 필요하다. DB 역할·권한 분리와 인증 설정은 [백엔드 안내](Backend/README.md)를 따른다.

```powershell
dotnet restore Backend
dotnet build Backend --no-restore

$taskPassword = Read-Host 'Local PostgreSQL bootstrap password' -AsSecureString
$env:POSTGRES_PASSWORD = [System.Net.NetworkCredential]::new('', $taskPassword).Password
docker compose -f Backend/docker-compose.yml up -d --wait

$env:ConnectionStrings__PostgresMigrator = '<마이그레이션 전용 연결 문자열을 환경/시크릿에서 공급>'
dotnet run --project Backend/src/Shinshi.Api -- --migrate
Remove-Item Env:ConnectionStrings__PostgresMigrator

$env:SHINSHI_TEST_CONNECTION_STRING = '<스키마 생성 권한이 있는 테스트 전용 DB 연결 문자열>'
dotnet test Backend --no-restore
```

마이그레이션은 번호순 SQL과 체크섬·이력을 트랜잭션으로 적용하며 API 실행 시 자동 적용하지 않는다. API 런타임에는 별도 DML 전용 `ConnectionStrings__Postgres`를 공급한다. `SHINSHI_TEST_CONNECTION_STRING`이 없으면 PostgreSQL 통합 사례를 건너뛰므로 그 결과를 DB 검증으로 기록하지 않는다. 실제 연결 문자열·비밀번호는 저장소에 쓰지 않는다.

Unity는 6000.4.7f1로 프로젝트를 연 뒤 **Window → General → Test Runner → EditMode → Run All**에서 실행한다. 테스트는 `Assets/Tests/EditMode/`에 있다. 결과와 실행 환경을 함께 기록한다.

클라이언트·서버 빌드는 **Shinshi → Build → Windows Client (Development) / Windows Server (Development) / Linux Server (Development)** 메뉴를 사용한다. 빌드 입력은 `Assets/00.Scenes/Bootstrap.unity`, Windows 출력은 `Builds/windows-client/Shinshi.exe`와 `Builds/windows-server/Shinshi.exe`다. 메뉴의 명령줄 진입점도 구현되어 있다.

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.4.7f1\Editor\Unity.exe' -batchmode -projectPath (Get-Location).Path -executeMethod Shinshi.Editor.ShinshiBuild.BuildFromCommandLine -shinshiTarget windows-server -logFile 'server-build.log'
```

이동 예측과 회피를 시험하는 빌드는 **Shinshi → Build → Dev Movement → Windows Server / Windows Client (Development)** 메뉴로 만든다. 입력 씬은 `Assets/00.Scenes/Dev_PlayerMovement.unity`(바닥, 구운 내비 메시, 쿼터뷰 카메라, 지연 시뮬레이션)이고 출력 위치는 위와 같아 `Bootstrap` 빌드를 덮어쓴다. 개발 빌드는 `-dev-latency-ms <ms>`(그 프로세스가 보내는 메시지의 지연, 서버와 클라이언트에 왕복의 절반씩)와 `-dev-autopilot`(클라이언트가 무작위 이동, 회피를 반복하고 예측 보정 통계를 로그로 남김)을 받는다.

Unity 설치 위치는 환경에 맞춘다. `-shinshiTarget`은 `windows-client`, `windows-server`, `linux-server`를 받으며 `-shinshiRelease`를 추가하면 개발 옵션을 끈다. 현재 `SessionConfig.asset`의 장애 취소 정책 값은 `Undecided`여서 `GameDBBuildValidator`가 비개발 빌드를 차단한다. 확정 정책과 설정 잔여는 [로드맵](docs/05-implementation-roadmap.md)을 확인한다. 이 절의 서버 빌드 명령은 실행 미검증이다. Windows 서버·클라이언트 빌드 뒤 [M2 실행 도구](tools/Start-M2.ps1)를 사용할 수 있으며, 프로세스 실행만으로 접속 검증이 끝나지는 않는다. Linux 배포 전제는 [배포 안내](deploy/README.md)에 있다.

## 기획·기술 문서

처음에는 [문서 읽기 안내](docs/00-reading-guide.md)를 연다.

| 문서 | 내용 |
|---|---|
| [시스템 개요](docs/01-system-design.md) | 세계관·핵심 루프·체험판 범위·팀 역할 |
| [게임 기획서](docs/02-game-design.md) | 전투·성장·보상·생활·부적·세계관 상세 |
| [기술설계서](docs/03-technical-design.md) | 기술 선택·서버 권한·API·저장·장애 처리 |
| [아키텍처](docs/04-architecture.md) | 모듈·씬·데이터·상태 전이·요구사항 대응 |
| [구현 로드맵](docs/05-implementation-roadmap.md) | 선행 관계·담당·검증·체험판 배포 기준 |

## 협업

프로젝트 설정과 협업 규칙은 [AGENTS.md](AGENTS.md), [첫 실행 안내](docs/unity/UNITY_START_HERE.md), [AI 작업 문서](docs/ai/README.md)를 따른다. 커밋 메시지는 [커밋 컨벤션](docs/commit-convention.md), C# 코드는 [코딩 컨벤션](docs/coding-convention.md)을 따른다.
