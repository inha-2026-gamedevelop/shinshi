# 배포 기반

범위는 영역 17의 API 컨테이너, Linux 게임 서버 프로세스 설정, 포트·보안 그룹 계획, 로그·지표 규약이다. 실제 클라우드 자원 생성·외부 배포·TLS 설정·시크릿 공급·서명 게시 도구는 포함하지 않는다. 게임 판정은 Unity 서버가 맡고 DB 쓰기는 API만 한다.

기준은 [기술 설계서 3장·10장](../docs/03-technical-design.md), [네트워크 권위 7장](../docs/unity/NETWORK_AUTHORITY.md#7-데디케이티드-서버-운영-원칙)이다. 호스팅은 로컬 개발 → Linux EC2 한두 대와 비공개 RDS → 실측 후 풀/컨테이너 확장 순서다. EC2/RDS 사양과 풀 크기는 이번 파일에서 확정하지 않는다.

## 파일

| 경로 | 용도 |
|---|---|
| [api/Dockerfile](api/Dockerfile) | .NET 10 SDK에서 Backend 솔루션 빌드, API publish, ASP.NET runtime에서 비루트 실행 |
| [api/Dockerfile.dockerignore](api/Dockerfile.dockerignore) | 저장소 루트 context에서 빌드 소스만 허용 |
| [api/.env.example](api/.env.example) | API 환경 키와 자리표시자 |
| [api/docker-compose.example.yml](api/docker-compose.example.yml) | API 컨테이너 실행 예시와 로그 보존(`journald` 드라이버 또는 `json-file` 회전) |
| [gameserver/shinshi-town.service](gameserver/shinshi-town.service) | 마을 프로세스 1개 |
| [gameserver/shinshi-dungeon@.service](gameserver/shinshi-dungeon@.service) | 던전 프로세스 인스턴스 1개씩 |
| [gameserver/README.md](gameserver/README.md) | 환경 예시, Linux 설치 전제, 시작/종료·드레인 절차 |
| [gameserver/journald-shinshi.conf](gameserver/journald-shinshi.conf) | 호스트 journald 로그 보존(90일·4G 상한, 재부팅 뒤 보존) |
| [LOGGING.md](LOGGING.md) | JSON 공통 키·지표 이름·단위, 로그 보존, 정산 하나를 추적하는 조회 |
| [monitoring/README.md](monitoring/README.md) | 게임 서버·API `/metrics`를 Prometheus로 수집하고 `/health/ready`를 확인해, Grafana 대시보드와 경보 규칙, Alertmanager 웹훅 전달까지 보는 구성 |

## API 컨테이너

저장소 루트에서 실행한다. Docker BuildKit과 Linux 컨테이너 엔진이 필요하다.

```text
docker build -f deploy/api/Dockerfile .
```

context가 루트 `.`이므로 `deploy/api/.dockerignore`는 이 명령에 적용되지 않는다. 루트 파일을 추가하지 않고 Docker가 지원하는 **Dockerfile 옆 `Dockerfile.dockerignore`**를 사용한다. [Docker 공식 문서](https://docs.docker.com/build/concepts/context/#filename-and-location)의 파일별 ignore 규칙을 따른다. Backend 소스·프로젝트·SQL과 `Assets/01.Scripts/00.Common/Contracts/**/*.cs`만 허용하고, Unity 에셋·Library·개인 기록·Git·로컬 NuGet 캐시·bin/obj·환경 파일·키 파일은 context에서 제외한다. 새 빌드 입력이 생기면 허용 목록을 확인한다.

`Shinshi.Contracts.csproj`는 Unity DTO를 상대 경로로 링크하므로 컨테이너 안에서도 `Backend/`와 `Assets/01.Scripts/00.Common/Contracts/` 위치를 유지한다. 솔루션 restore/build는 테스트 프로젝트도 빌드하지만 테스트를 실행하지 않는다. 최종 이미지에는 API publish 결과만 포함한다. NuGet restore, 기본 이미지 pull, curl 설치에 네트워크가 필요하다. SDK/runtime은 `10.0-noble` 계열을 사용하며 운영 게시 전 검증한 이미지 digest를 고정한다. 비루트 사용자 `APP_UID`는 [공식 .NET Linux 이미지](https://learn.microsoft.com/en-us/dotnet/core/docker/container-images)가 제공한다.

런타임은 `dotnet Shinshi.Api.dll`, 컨테이너 HTTP 포트는 8080이다. HTTPS는 외부 진입점/프록시에서 종료하고 8080은 그 진입점에만 연결한다. 예를 들어 같은 호스트 프록시는 `127.0.0.1:5080:8080`으로 publish한다. 컨테이너 자체에서 TLS가 설정되었다고 가정하지 않는다.

DB 없이 HTTP 기동과 내장 상태 검사를 확인하는 로컬 예시다. 아래 명령은 이번 작업에서 실행하지 않았다.

```text
docker build -t shinshi-api:local -f deploy/api/Dockerfile .
docker run --rm --name shinshi-api-local --publish 127.0.0.1:5080:8080 shinshi-api:local
```

다른 터미널에서:

```powershell
Invoke-RestMethod http://127.0.0.1:5080/v1/service-state
docker inspect --format '{{json .State.Health}}' shinshi-api-local
```

`HEALTHCHECK`는 30초 간격으로 `GET /health/live`의 HTTP 성공 여부를 본다. 이 경로는 DB에 접근하지 않고 프로세스가 응답하면 200이다. 따라서 HTTP 프로세스 생존 확인이며 DB readiness·로그인 가능·경제 저장 성공을 보장하지 않는다. `/health/ready`를 쓰지 않는 이유는 DB 장애가 컨테이너 `unhealthy`로 번져 unhealthy 컨테이너를 재시작하는 자동 복구 도구나 오케스트레이터가 멀쩡한 API를 DB 없이 계속 재시작하는 연쇄를 막기 위해서다. 재시작은 DB를 고치지 못하고 진행 중 요청·정산 작업만 끊으며 재접속 폭주를 더한다. DB 준비 상태는 외부에서 Prometheus(blackbox)가 `/health/ready`를 확인해 `ShinshiApiNotReady` 경보로 본다([모니터링](monitoring/README.md)). 이 경로는 Backend에 `/health/live`가 들어간 빌드부터 있으므로, 그 전 빌드의 이미지는 `unhealthy`로 표시된다. Docker의 `unhealthy` 상태 자체는 컨테이너를 자동 재시작하지 않는다.

API 로그는 Docker 기본 `json-file` 드라이버가 회전 없이 계속 쌓고 컨테이너를 지우면 함께 사라진다. 운영에서는 `journald` 드라이버(호스트 journald 보존 설정 공유, 컨테이너 재생성 뒤에도 남음)나 `json-file` 회전을 쓴다. 예시는 [api/docker-compose.example.yml](api/docker-compose.example.yml)이고, 보존 기간과 정산 추적 조회는 [로그 규약](LOGGING.md#보존과-정산-추적-op-6b)에 있다.

API 설정은 [.env.example](api/.env.example)에 적힌 키를 프로세스 환경으로 공급한다. 실제 DB 연결 문자열·토큰·서비스 자격을 저장소나 환경 파일에 쓰지 않는다. 기존 프로그램은 외부 시크릿 파일을 자동 읽지 않으므로 시크릿 공급자가 환경으로 전달하는 연결은 별도 구현/운영 작업이다. `--build-arg`로 비밀값을 전달하지 않는다.

마이그레이션은 실행 중 API와 분리하여 **정확히 하나의 인자 `--migrate`**로 실행한다. 마이그레이션 환경에는 `ConnectionStrings__PostgresMigrator`만, API 환경에는 DML 전용 `ConnectionStrings__Postgres`만 준다. 마이그레이션 실패(exit 1) 시 API 교체를 중단한다. 예를 들어 비밀값이 이미 현재 셸 환경에 공급되어 있고 실제 비공개 DB 네트워크가 준비된 경우의 명령 형태는 아래와 같다.

```text
docker run --rm --network REPLACE_WITH_PRIVATE_DB_NETWORK --env ConnectionStrings__PostgresMigrator shinshi-api:local --migrate
```

시작 시 자동 마이그레이션은 없다. 운영자는 이전 API와 호환되는 스키마 변경인지 확인하고 마이그레이션 → API 교체 순서를 지킨다. 마이그레이션 역할을 API/게임 서버에 공급하지 않는다.

## 게임 서버와 포트 계획

Linux Dedicated Server 빌드 하나가 역할 인자로 마을/던전을 고른다. 마을은 프로세스 1개, 던전은 미리 실행한 프로세스 N개이고 파티마다 1개를 배정한다. 풀 수 N은 프로세스 RSS·CPU·틱 시간과 실제 혼합 부하를 측정해 정한다. 25개는 100명이 모두 4인 파티인 경우이며 솔로 100명이면 100개가 필요할 수 있다. 부족하면 대기열을 적용한다.

| 대상 | 유닛/번호 | UDP 포트 | ID 예시 |
|---|---|---:|---|
| 마을 | `shinshi-town.service` | 7777 | `town-1` |
| 던전 첫 인스턴스 | `shinshi-dungeon@1.service` | 7780 | `dungeon-1` |
| 던전 i번째 | `shinshi-dungeon@i.service`, `1 ≤ i ≤ N` | `7780 + i - 1` | `dungeon-i` |
| 던전 마지막 | `shinshi-dungeon@N.service` | `7780 + N - 1` | `dungeon-N` |

7778·7779는 이 계획에서 열지 않는다. UDP 포트 상한 때문에 한 호스트에서 이 범위를 사용하려면 `1 ≤ N ≤ 57756`이어야 하지만 이는 자원 수용량이 아니다. 실제 상한은 훨씬 앞에서 부하 시험으로 결정한다. 유닛은 산술을 수행하지 않으므로 인스턴스별 환경 설정에 계산한 `GAME_PORT`를 직접 넣고 중복을 확인한다. 호스트가 여러 개면 모든 프로세스의 `INSTANCE_ID`를 전체 풀에서 유일하게 부여한다.

실제 실행 계약은 [ServerLaunchConfig.cs](../Assets/01.Scripts/98.Servers/01.Network/ServerLaunchConfig.cs)와 [Constants.network.cs](../Assets/01.Scripts/00.Common/Constants/Constants.network.cs)다.

| 실행 인자 | 환경 키 | 유닛 사용 |
|---|---|---|
| `-role` | `SERVER_ROLE` | `town`/`dungeon`으로 유닛에서 고정 |
| `-port` | `GAME_PORT` | 7777 또는 인스턴스별 던전 포트 |
| `-instance` | `INSTANCE_ID` | 전체 풀에서 유일한 ID |
| `-backend-base-url` | `BACKEND_BASE_URL` | 자격 정보·query·fragment 없는 절대 URL. 배포 시 HTTPS |
| `-content-version` | `CONTENT_VERSION` | 빌드에 포함된 GameDB 버전과 정확히 일치 |
| `-dungeon-pool-size` | `DUNGEON_POOL_SIZE` | 실제 시작한/등록할 풀 크기 N, 정수 1 이상 |
| `-boss-id` | `BOSS_ID` | 선택. 던전 서버가 보스전에서 여는 보스 정의 ID. 없으면 `boss_wolbaek`. 보스 카탈로그에 없는 ID면 서버를 시작하지 않는다. 출정 계약에 보스 ID가 생기면 그 값으로 바뀐다 |

실행 인자가 환경 변수보다 우선한다. `INSTANCE_ID`와 `CONTENT_VERSION`은 영문/숫자로 시작하는 1~128자이며 영문·숫자·`.`·`_`·`-`·`+`만 허용된다. 각 환경 예시는 자리표시자를 교체하기 전 실행하지 않는다. `-batchmode -nographics -logFile -`는 Unity 헤드리스/표준 출력 옵션이다. 서비스 자격·서명 키를 실행 인자로 넣지 않는다.

자동 재시작과 SIGTERM/종료 제한 시간은 [게임 서버 운영 설명](gameserver/README.md)에 적었다. systemd는 API 배정 중지, 세션 마감, 정산 outbox 확인을 구현하지 않는다. 배포 순서는 신규 배정 중지 → 진행 세션 마감 대기 → 미확정 저장/정산 outbox 확인 → SIGTERM → 빌드 교체 → 검증 후 신규 배정 재개다. 제한 시간 경과 뒤 강제 종료된 프로세스를 정상 정산으로 표시하지 않는다.

## 보안 그룹 규칙 계획

아래는 향후 설정할 규칙 표이며 실제 보안 그룹을 생성하지 않는다. `CLIENT_CIDR`는 허용 체험 참가자의 CIDR 자리표시자다. 공개 서비스로 전환할 때만 승인된 공개 범위로 바꾼다. `GAME_SG`, `HTTPS_ENTRY_SG`, `API_SG`, `DB_SG`는 역할을 나타내며 실제 AWS ID가 아니다.

| 방향/대상 | 프로토콜·포트 | 허용 출발지/목적지 | 조건 |
|---|---|---|---|
| inbound → 게임 호스트 `GAME_SG` | UDP 7777 | `CLIENT_CIDR` | 마을 KCP. TCP로 대체하지 않음 |
| inbound → 게임 호스트 `GAME_SG` | UDP 7780~`7780+N-1` | `CLIENT_CIDR` | 실제 풀의 연속 범위만 허용 |
| inbound → HTTPS 진입점 `HTTPS_ENTRY_SG` | TCP 443 | `CLIENT_CIDR`, 게임 서버 `GAME_SG` | 클라이언트/API 및 게임 서버/API HTTPS |
| inbound → API `API_SG` | TCP 8080 | `HTTPS_ENTRY_SG` | 진입점이 별도 호스트인 경우만. 같은 호스트면 loopback publish하고 SG 규칙 불필요 |
| inbound → DB `DB_SG` | TCP 5432 | API `API_SG` | RDS 비공개 subnet, public access 비활성. 클라이언트/게임 SG/인터넷 허용 금지 |
| outbound 게임 호스트 | TCP 443 | API HTTPS 진입점 | 역할별 서비스 자격 검증 필요 |
| outbound API | TCP 5432 | DB `DB_SG` | 런타임 DML 자격만 사용 |

80·22·5432의 인터넷 공개 규칙은 이 계획에 없다. DNS·호스트 업데이트·관리 접속 정책은 운영 환경 결정 후 필요한 범위로 별도 구성한다. IPv6를 사용할 때도 같은 역할 경계를 명시하고 무조건 전체 공개하지 않는다. API와 게임 서버가 같은 EC2/SG를 공유하면 SG는 프로세스를 구분하지 못한다. 게임 프로세스에 DB 자격을 공급하지 않고, 세분화가 필요하면 API 호스트/SG를 분리한다. 마이그레이션은 별도 허가된 실행 경로를 마련한다.

KCP 기본 전송은 암호화하지 않는다. 공개 전에 일회용 접속 토큰의 한 번 교환·참가자/인스턴스 결합과 서버 서비스 자격 검증을 연결해야 한다. 보안 그룹만으로 인증을 대신하지 않는다.

## 검증 상태와 후속 작업

검증 명령은 저장소 루트에서 `docker build -f deploy/api/Dockerfile .`이며, Linux 검증 호스트에서는 환경/실행 파일 전제를 마련한 뒤 아래를 실행한다.

```text
systemd-analyze verify deploy/gameserver/shinshi-town.service deploy/gameserver/shinshi-dungeon@.service
```

템플릿 검증기가 실제 인스턴스명을 요구하면 `shinshi-dungeon@1.service`로 검증한다. 실행 파일·환경 경로가 없다는 진단도 원인 그대로 기록하고 문법 성공으로 처리하지 않는다. 이번 환경의 실제 명령 결과는 작업 보고에 남긴다.

부모/통합 담당이 이어서 할 작업:

1. Docker 데몬 접근 가능한 Linux 환경에서 이미지 build/run, 비루트 UID·상태 검사(`/health/live`)·SIGTERM을 확인한다. 실제 DB 통합 검증은 Backend 범위다.
2. Linux Dedicated Server 빌드와 Bootstrap/GameDB 배치를 만들고 각 역할의 UDP 수신·재시작·동시 인스턴스 실행을 확인한다. 현재 `ServerBootstrap.CreateJoinTokenVerifier`는 출시 빌드에서 `null`을 반환하므로 실제 출시 인증기를 연결해야 한다.
3. 역할별 서비스 자격과 시크릿 환경 공급, API HTTPS 도메인·TLS, 비공개 DB/migrator 권한을 연결한다. 이 파일들은 실제 비밀값을 담지 않는다.
4. 서버 풀 등록·heartbeat, 신규 배정 중지, 세션 마감/저장 큐/outbox 드레인을 구현하고 장애·강제 종료 때 경제 작업을 복구한다.
5. [로그 규약](LOGGING.md)은 API/Unity 로거에 연결되어 있고 호스트 journald·Docker 드라이버 보존 설정 파일이 있다. Linux 호스트에 적용하고 정산 추적 조회를 실제 로그로 확인한다. 실제 웹훅 채널을 정해 시험 경보를 받아 본다(OP-2). 부하 시험으로 RSS·CPU·틱 시간·API p95·저장 큐를 측정해 N/호스팅 사양을 정한다. 필요 시 `ethtool -S`의 `pps_allowance_exceeded`, `bw_*_allowance_exceeded`, `conntrack_allowance_exceeded`도 확인한다.
6. 빌드 신원/허용 `buildId`, 드레인 뒤 교체, 서명 매니페스트·게시 sequence·stable 채널 검증 등 영역 17의 남은 작업을 별도로 진행한다.
