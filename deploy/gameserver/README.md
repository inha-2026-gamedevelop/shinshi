# Linux 게임서버 유닛

마을 프로세스 하나와 던전 슬롯 `1..N`을 같은 Linux Dedicated Server 빌드로 실행하는 systemd 템플릿이다. 실제 서버 배포·클라우드 자원 생성·부하 측정은 아직 수행하지 않았다. 출시 인증·서버 임대·출정 종료 시 자동 드레인 코드는 있지만 이 유닛의 실제 실행 증거는 아니다. 협조 종료 helper와 개발 서버의 PID 요청 파일·`DrainWaiting` 경로는 격리 Linux 환경에서 실행했다. 실제 systemd `ExecStop` 연동과 운영 출정 종료는 미검증이다. SIGTERM의 Unity 종료 연동·정산 outbox 드레인은 별도 미확인이다.

| 파일 | 운영 서버의 경로·역할 |
|---|---|
| [shinshi-town.service](shinshi-town.service) | `/etc/systemd/system/shinshi-town.service`, 마을 한 개 |
| [shinshi-dungeon@.service](shinshi-dungeon@.service) | `/etc/systemd/system/shinshi-dungeon@.service`, `shinshi-dungeon@1`부터 `@N`까지 |
| [town/.env.example](town/.env.example) | `/etc/shinshi/gameserver/town.env`의 비밀값 없는 예시 |
| [dungeon/.env.example](dungeon/.env.example) | `/etc/shinshi/gameserver/dungeon-1.env`부터 `dungeon-N.env`까지의 비밀값 없는 예시 |
| [cooperative-stop.sh](cooperative-stop.sh) | `/opt/shinshi/gameserver/cooperative-stop.sh`(root 소유 `0755`), 두 유닛의 `ExecStop`이 부르는 협조 종료 요청 스크립트 |
| [journald-shinshi.conf](journald-shinshi.conf) | `/etc/systemd/journald.conf.d/shinshi.conf`, 로그 보존(90일·4G 상한)과 재부팅 뒤 보존 |

## 실행 계약과 포트

실제 계약은 [ServerLaunchConfig.cs](../../Assets/01.Scripts/98.Servers/01.Network/Bootstrap/ServerLaunchConfig.cs)와 [Constants.network.cs](../../Assets/01.Scripts/00.Common/Constants/Constants.network.cs)다. CLI가 환경변수보다 우선한다. 각 유닛은 역할을 `-role town` 또는 `-role dungeon`으로 고정하고, 아래 나머지 설정을 환경파일에서 한 인자씩 전달한다. 환경파일의 `SERVER_ROLE`은 해당 역할과 맞춰 둔다.

| 환경변수 | CLI | 조건 |
|---|---|---|
| `SERVER_ROLE` | `-role` | `town` 또는 `dungeon`; 유닛에서 고정 |
| `GAME_PORT` | `-port` | 마을 `7777`, 던전 슬롯 i는 `7780 + (i - 1)`의 계산된 정수 |
| `INSTANCE_ID` | `-instance` | 전체 배포에서 유일한 GUID. API가 GUID로만 받는다. 값을 주지 않으면 역할과 포트로 만든 고정 GUID를 쓰며, 이는 개발 실행 전용이다 |
| `BACKEND_BASE_URL` | `-backend-base-url` | 운영 HTTPS URL; 자격 정보·쿼리·fragment·공백 없음 |
| `CONTENT_VERSION` | `-content-version` | 해당 서버 빌드와 배포 콘텐츠의 버전 |
| `DUNGEON_POOL_SIZE` | `-dungeon-pool-size` | Linux 프로세스 CPU·RSS·혼합 파티 부하로 결정한 양의 정수 N |
| `BOSS_ID` | `-boss-id` | 선택, 던전만 읽는다. 보스전에서 여는 보스 정의 ID이며 없으면 `boss_wolbaek`. 보스 카탈로그에 없으면 시작하지 않는다. 유닛 실행 줄에는 넣지 않았으므로 환경파일에 둔다 |
| `METRICS_PORT` | `-metrics-port` | 선택. Prometheus `/metrics` TCP 포트, 없거나 `0`이면 끈다. 마을 `9310`, 던전 슬롯 i는 `9320 + (i - 1)`. 공개 보안 그룹에 열지 않는다. 세부는 [모니터링](../monitoring/README.md) |
| `METRICS_BIND_ADDRESS` | `-metrics-bind` | 선택. 기본 `127.0.0.1`, Prometheus가 다른 호스트에 있을 때만 사설 IP. 호스트 이름은 받지 않는다 |
| `METRICS_MESSAGE_DETAIL` | `-metrics-message-detail` | 선택. `1`이면 Mirror 메시지 종류별 대역폭을 센다. 보낼 때마다 할당이 생겨 프로파일링할 때만 켠다 |

`INSTANCE_ID`는 GUID(`uuidgen`으로 슬롯마다 한 번 만들어 고정)다. `CONTENT_VERSION`은 영문·숫자로 시작하는 1~128자이며, 이후 영문·숫자·`.`·`_`·`-`·`+`만 쓴다. 파서는 HTTP도 허용하지만 운영에서는 HTTPS를 사용한다. `CONTENT_VERSION`은 접속 인증의 `protocolVersion`·`contentHash`·허용 `buildId`를 대신하지 않는다.

각 환경파일에 포트·ID·N을 명시한다. 풀 크기 값은 서버 프로세스를 생성하거나 API 풀을 자동 등록하지 않는다. 운영자가 `1..N` 유닛을 따로 설치·활성화하고, 후속 배정 구현과 같은 N을 사용해야 한다. 이 포트 배정으로 가능한 최대 N은 `65535 - 7780 + 1 = 57756`이고, 실제 N은 CPU·메모리와 부하 측정으로 훨씬 먼저 제한한다. 25개·100개를 고정 요구량으로 삼지 않는다.

유닛의 `${GAME_PORT}` 형태는 하나의 인자로 확장한다. systemd의 실행 줄을 셸 스크립트로 취급하지 않으며, 환경파일 값 안에 `$N`, `${OTHER_VALUE}`, `$((...))`를 넣어 계산하거나 다른 값을 참조하지 않는다. `%i`는 던전 슬롯 번호로 환경파일·상태 디렉터리·로그 이름에만 사용한다. 실행 파일의 경로는 유닛 안의 절대 경로다. 근거: [systemd.service의 실행 줄 문법](https://github.com/systemd/systemd/blob/main/man/systemd.service.xml).

## 설치 전제와 설치 예시

운영 Linux에서 다음 전제를 먼저 갖춘다.

- `shinshi-game` 시스템 사용자·그룹이 존재하고 로그인 셸이 없다. 프로세스는 이 계정으로 실행한다.
- 검증한 Linux Dedicated Server 빌드 전체를 `/opt/shinshi/gameserver/releases/<buildId>/`에 두고, `/opt/shinshi/gameserver/current`가 선택한 릴리스를 가리킨다. 실행 파일의 배포 이름은 `Shinshi.x86_64`다. 실제 빌드 이름이 다르면 두 유닛의 `ExecStart`를 함께 맞춘다. 파일 소유자는 root이며 서비스 사용자는 읽기·실행만 할 수 있게 한다.
- Unity 런타임이 필요한 공유 라이브러리가 설치되어 있고, 필요한 서버 빌드 정의와 씬을 실제 Linux 빌드로 검증했다. 서버 PlayerSettings에는 `APPUI_PLATFORM_DISABLED`를 둬 App UI의 데스크톱 네이티브 초기화를 사용하지 않는다. 클라이언트 정의와 패키지 원본은 바꾸지 않는다. 이 정의만으로 네이티브 플러그인 파일 자체가 빌드에서 제외되지는 않는다.
- `/etc/shinshi/gameserver/`는 root가 관리하고 환경파일은 root 소유 `0600`으로 둔다. 자리표시자를 모두 실제 비밀값 없는 설정으로 바꾼다.
- 게임 수신은 UDP 마을 `7777`, 던전 `7780..(7780 + N - 1)`만 연다. API HTTPS와 DB 포트는 분리한다. 상세 방화벽·TLS·비밀값 경계는 [공통 배포 안내](../README.md)를 따른다.

아래 명령은 운영 Linux에서 적용할 예시이며 이 작업에서 실행하지 않았다. 저장소 루트 기준이다.

```bash
sudo useradd --system --user-group --home-dir /var/lib/shinshi/gameserver --no-create-home --shell /usr/sbin/nologin shinshi-game
sudo install -d -m 0755 -o root -g root /opt/shinshi/gameserver
sudo install -m 0755 -o root -g root deploy/gameserver/cooperative-stop.sh /opt/shinshi/gameserver/cooperative-stop.sh
sudo install -d -m 0750 -o root -g root /etc/shinshi/gameserver
sudo install -m 0600 -o root -g root deploy/gameserver/town/.env.example /etc/shinshi/gameserver/town.env
sudo install -m 0600 -o root -g root deploy/gameserver/dungeon/.env.example /etc/shinshi/gameserver/dungeon-1.env
sudo install -m 0644 -o root -g root deploy/gameserver/shinshi-town.service /etc/systemd/system/shinshi-town.service
sudo install -m 0644 -o root -g root deploy/gameserver/shinshi-dungeon@.service /etc/systemd/system/shinshi-dungeon@.service
```

설정 편집이 끝난 뒤 측정한 N개 모두의 `dungeon-i.env`를 준비한다. 예를 들어 슬롯 2는 `GAME_PORT=7781`, `INSTANCE_ID`에 슬롯 고유 GUID를 넣는다. N 자체는 예시에서 결정하지 않는다. 기존 계정이 있다면 사용자 생성 명령을 반복하지 않는다. `StateDirectory`가 유닛별 `/var/lib/shinshi/gameserver/town`과 `dungeon-i`를 생성한다. `HOME`도 그곳으로 지정해 Unity의 사용자 설정·런타임 파일 쓰기를 빌드 디렉터리와 분리한다. 이 디렉터리가 정산 outbox를 구현하지는 않는다.

```bash
sudo systemd-analyze verify /etc/systemd/system/shinshi-town.service /etc/systemd/system/shinshi-dungeon@.service
sudo systemctl daemon-reload
sudo systemctl enable --now shinshi-town.service
sudo systemctl enable --now shinshi-dungeon@1.service
```

슬롯 `2..N`은 각각 활성화한다. 유닛의 활성 상태는 프로세스 실행 상태만 의미한다. API 연결, 인증, 배정 준비 여부는 별도로 확인한다. 검증되지 않은 빌드에 운영 트래픽을 붙이지 않는다. `ProtectSystem=strict`, `ProtectHome=yes`, `PrivateTmp=yes`, `NoNewPrivileges=yes`가 적용되므로 Unity 런타임과 이후 영속 파일이 이 제한 안에서 동작하는지도 대상 Linux에서 확인한다.

## 재시작·로그·중지

던전은 출정 하나가 끝나면 스스로 종료하고(종료 코드 0 재활용, 70 불변식 위반, 75 서버 풀 존재 상실), 새 bootId로 다시 떠야 하므로 `Restart=always`, 2초 뒤 재시작이다. 운영 알림은 70, 75와 운영 종료 기한 초과(76)에 건다. 300초 동안 시작을 다섯 번으로 제한해 설정 오류나 지속 장애의 재시작 폭주를 막는다. 제한에 걸리면 원인을 해결한 뒤 해당 유닛의 `systemctl reset-failed`와 `start`로 재개한다. 명시적인 `systemctl stop`은 자동 재시작을 유발하지 않는다. `Restart=always`이므로 코드 0 종료도 재시작한다. 300초에 다섯 번 제한은 출정이 1분 안에 연달아 끝나는 경우에도 걸릴 수 있으니, 실측 뒤 던전 유닛의 `StartLimitBurst`를 조정한다.

Unity의 `-logFile -`와 stdout·stderr를 journald로 모은다(유닛의 `StandardOutput=journal`, `StandardError=journal`). 던전은 유닛 번호와 `SyslogIdentifier`(`shinshi-dungeon-<슬롯>`)로 구분한다. 로그를 구조화 JSON으로 바꾸거나 공통 필드를 자동 추가하는 기능은 유닛에 없다(서버 빌드의 `ServerJsonLogHandler`가 JSON 한 줄로 바꾼다). 프로세스가 재시작해도 journald에 남지만, 호스트 재부팅 뒤에도 남고 보존 기간·디스크 상한이 정해지려면 보존 설정을 설치해야 한다. 기본 설정은 호스트에 따라 메모리에만 두고 재부팅하면 사라진다.

```bash
sudo install -d /etc/systemd/journald.conf.d
sudo install -m 0644 -o root -g root deploy/gameserver/journald-shinshi.conf /etc/systemd/journald.conf.d/shinshi.conf
sudo systemctl restart systemd-journald
journalctl --disk-usage
```

[journald-shinshi.conf](journald-shinshi.conf)는 `Storage=persistent`, 최대 90일·4G·여유 2G 유지다. 기술 로그 30일·행동 로그 90일 원칙을 journald는 종류별로 나누지 못하므로 상한을 90일로 두고 디스크 상한이 실제로 먼저 걸린다. 정산 하나를 `sessionId`·`operationId`로 추적하는 조회는 [공통 로그 안내](../LOGGING.md#보존과-정산-추적-op-6b)에 있다. 로그 형식·비밀값 제외도 그 문서를 따른다. 보존 파일은 이 작업에서 Linux 호스트에 적용해 보지 못했다.

```bash
sudo systemctl status shinshi-town.service shinshi-dungeon@1.service
sudo journalctl -u shinshi-town.service -f
sudo journalctl -u shinshi-dungeon@1.service -f
sudo ss -lunp
```

계획된 교체는 **신규 배정 중지 → 진행 세션 마감 → 연결 정리·flush → 프로세스 종료** 순서다. 정산 outbox 비우기는 이 경로에 없다(미구현). `ServerShutdownController`의 출정 종료·풀 이탈에 따른 내부 드레인→연결 정리→종료 경로와, 운영 종료 요청을 받는 `DrainWaiting` 단계는 구현됨·실행 미검증이다.

운영 종료 요청은 두 입구가 같은 `DrainWaiting` 상태로 모인다. (1) Unity `Application.wantsToQuit`: 일반 종료 요청을 막고 `Exited`일 때만 허용한다(에디터에서는 등록하지 않는다). Linux `SIGTERM`이 이 이벤트를 부른다고 가정하지 않았고 확인하지 못했다. (2) 협조 요청 파일: 유닛이 `SHINSHI_SHUTDOWN_REQUEST_FILE`을 서비스 전용 `RuntimeDirectory`(`/run/shinshi/gameserver/town`, `dungeon-<슬롯>`, 모드 `0700`)에 지정하고, `ExecStop`이 [cooperative-stop.sh](cooperative-stop.sh)로 메인 PID(`$MAINPID`) 한 줄을 그 파일에 쓴 뒤 최대 270초 동안 프로세스가 스스로 내려가기를 기다린다. 서버는 이 환경변수가 있을 때만 1초 주기로 파일을 확인하고, 자기 PID와 다른 내용(이전 프로세스가 남긴 파일)은 지우고 무시한다. 같은 서비스 사용자의 로컬 파일이며 인증을 우회하는 공개 경로가 아니다. 스크립트가 쓰지 못하거나 종료가 지연되면 systemd가 `SIGTERM`, 이어서 `SIGKILL`로 내리는 기존 동작으로 돌아간다.

`DrainWaiting`은 서버 풀 드레인을 보내 신규 배정을 막고 응답(최대 30초)을 확인한 뒤, 마을은 곧바로, 던전은 진행 중이거나 예약만 된 출정이 끝날 때까지 최대 210초 기다려 연결 정리로 넘어간다. 기한을 넘기면 정상 정산으로 처리하지 않고 종료 코드 76과 `server.shutdown.forced` 오류 로그로 남긴 뒤 연결을 끊는다. 이 경우 API의 장애 확인·복구 절차가 정산을 맡는다. 종료 코드 76은 운영 알림 대상에 추가한다(기존 70, 75와 같은 취급). 210초 + 연결 정리 시간 + 안전 여유 30초는 `TimeoutStopSec=300` 안에 들어가게 잡았다. 값은 `ServerShutdownController`의 상수이며 `TimeoutStopSec`을 바꾸면 함께 맞춘다.

`systemctl stop`은 `ExecStop`이 끝나거나 실패한 뒤에도 프로세스가 남아 있으면 `SIGTERM`을 유닛의 프로세스 그룹에 전달하고, 전체 중지가 300초를 넘기면 `SIGKILL`로 종료한다. `TimeoutStopSec=300`은 `ExecStop` 대기를 포함한 전체 상한이다. 실제 최대 세션 마감 시간·저장 재시도 시간을 측정해 상한을 맞춘다. 미확정 경제 작업이 있거나 강제 종료됐으면 정상 마감으로 기록하지 않고 API의 장애 확인·복구 절차가 필요하다. 협조 요청 직전에 막 예약된 던전 배정은 서버가 마지막으로 받은 인스턴스 상태로만 가리므로 경합 창이 남는다. 근거: [systemd.kill](https://github.com/systemd/systemd/blob/main/man/systemd.kill.xml), [systemd.service의 ExecStop·RuntimeDirectory](https://github.com/systemd/systemd/blob/main/man/systemd.service.xml).

## 비밀값과 남은 검증

예시는 서비스 자격·토큰·비밀번호를 포함하지 않는다. 그런 값을 실행 인자, URL, Unity 에셋·SO, 로그에 넣지 않는다. 게임서버의 API 서비스 자격 로딩 계약은 현재 실행 설정에 없으므로 임의 환경변수나 CLI 플래그를 추가하지 않았다. 후속 구현에서 외부 시크릿 공급과 실제 읽기 코드를 함께 연결한다. systemd의 `LoadCredential`을 선택할 경우에도 서버가 credential 파일을 읽는 코드와 해당 호스트 systemd 지원을 검증한 뒤 설정한다. 근거: [systemd.exec의 credential·환경 설정](https://github.com/systemd/systemd/blob/main/man/systemd.exec.xml).

협조 종료 스크립트는 2026-10-11에 로컬 `postgres:18` 이미지의 일회성 Linux 컨테이너에서 9개 시나리오를 실행해 모두 통과했다. 이미지 다운로드·네트워크·DB 실행 없이 스크립트를 읽기 전용으로 마운트하고 컨테이너 안에서 만든 `sleep` 프로세스만 사용했다. `sh -n`, 누락·잘못된 PID의 즉시 반환, 이미 종료된 PID의 즉시 반환, 살아 있는 PID의 요청 파일 발행과 종료 대기, 1초 제한, 상대 경로 거절, 정수가 아니거나 음수인 대기 시간 거절, 0초 제한, 요청 파일 쓰기 실패 시 반환을 확인했다. 발행된 요청은 PID 한 줄과 `0600` 권한을 확인했고 발행 후 임시 파일이 남지 않았다. 시간 제한은 살아 있는 프로세스를 직접 종료하지 않고 systemd 후속 처리를 위해 반환했다.

대상 Linux에서 `systemd-analyze verify`, 비루트 실행·런타임 파일 쓰기, 마을+N 던전 UDP 바인딩, API HTTPS·서비스 인증, crash 재시작·폭주 제한, journald 출력과 보존 설정 적용·재부팅 뒤 잔존·정산 추적 조회, 실제 `systemctl stop`의 `ExecStop`·`RuntimeDirectory` 동작과 요청 파일의 PID가 서버 프로세스 PID와 같은지(`$MAINPID`), `SIGTERM`의 Unity 종료 요청 연동 여부, 시간 초과, 실제 세션 드레인·outbox·재접속을 확인해야 한다. 협조 종료 코드(`ServerShutdownController`, `ServerShutdownRequestWatcher`, 시험 `ServerGracefulShutdownTests`)의 Unity·Linux 서버 실행은 미검증이다. 스크립트 단독 실행을 제외한 실제 유닛 실행·Linux 서버 빌드·네트워크 통합·부하 측정도 미검증이다.

설계 근거: [기술 설계 3장·10장](../../docs/03-technical-design.md), [서버 운영 원칙 7장](../../docs/unity/NETWORK_AUTHORITY.md).
