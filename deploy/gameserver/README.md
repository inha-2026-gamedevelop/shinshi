# Linux 게임서버 유닛

마을 프로세스 하나와 던전 슬롯 `1..N`을 같은 Linux Dedicated Server 빌드로 실행하는 systemd 템플릿이다. 실제 서버 배포·클라우드 자원 생성·부하 측정은 아직 수행하지 않았다. 출시 인증, 서버 임대와 드레인이 구현된 것으로 가정하지 않는다.

| 파일 | 운영 서버의 경로·역할 |
|---|---|
| [shinshi-town.service](shinshi-town.service) | `/etc/systemd/system/shinshi-town.service`, 마을 한 개 |
| [shinshi-dungeon@.service](shinshi-dungeon@.service) | `/etc/systemd/system/shinshi-dungeon@.service`, `shinshi-dungeon@1`부터 `@N`까지 |
| [town/.env.example](town/.env.example) | `/etc/shinshi/gameserver/town.env`의 비밀값 없는 예시 |
| [dungeon/.env.example](dungeon/.env.example) | `/etc/shinshi/gameserver/dungeon-1.env`부터 `dungeon-N.env`까지의 비밀값 없는 예시 |

## 실행 계약과 포트

실제 계약은 [ServerLaunchConfig.cs](../../Assets/01.Scripts/98.Servers/01.Network/ServerLaunchConfig.cs)와 [Constants.network.cs](../../Assets/01.Scripts/00.Common/Constants/Constants.network.cs)다. CLI가 환경변수보다 우선한다. 각 유닛은 역할을 `-role town` 또는 `-role dungeon`으로 고정하고, 아래 나머지 설정을 환경파일에서 한 인자씩 전달한다. 환경파일의 `SERVER_ROLE`은 해당 역할과 맞춰 둔다.

| 환경변수 | CLI | 조건 |
|---|---|---|
| `SERVER_ROLE` | `-role` | `town` 또는 `dungeon`; 유닛에서 고정 |
| `GAME_PORT` | `-port` | 마을 `7777`, 던전 슬롯 i는 `7780 + (i - 1)`의 계산된 정수 |
| `INSTANCE_ID` | `-instance` | 전체 배포에서 유일한 GUID. API가 GUID로만 받는다. 값을 주지 않으면 역할과 포트로 만든 고정 GUID를 쓰며, 이는 개발 실행 전용이다 |
| `BACKEND_BASE_URL` | `-backend-base-url` | 운영 HTTPS URL; 자격 정보·쿼리·fragment·공백 없음 |
| `CONTENT_VERSION` | `-content-version` | 해당 서버 빌드와 배포 콘텐츠의 버전 |
| `DUNGEON_POOL_SIZE` | `-dungeon-pool-size` | Linux 프로세스 CPU·RSS·혼합 파티 부하로 결정한 양의 정수 N |

`INSTANCE_ID`는 GUID(`uuidgen`으로 슬롯마다 한 번 만들어 고정)다. `CONTENT_VERSION`은 영문·숫자로 시작하는 1~128자이며, 이후 영문·숫자·`.`·`_`·`-`·`+`만 쓴다. 파서는 HTTP도 허용하지만 운영에서는 HTTPS를 사용한다. `CONTENT_VERSION`은 접속 인증의 `protocolVersion`·`contentHash`·허용 `buildId`를 대신하지 않는다.

각 환경파일에 포트·ID·N을 명시한다. 풀 크기 값은 서버 프로세스를 생성하거나 API 풀을 자동 등록하지 않는다. 운영자가 `1..N` 유닛을 따로 설치·활성화하고, 후속 배정 구현과 같은 N을 사용해야 한다. 이 포트 배정으로 가능한 최대 N은 `65535 - 7780 + 1 = 57756`이고, 실제 N은 CPU·메모리와 부하 측정으로 훨씬 먼저 제한한다. 25개·100개를 고정 요구량으로 삼지 않는다.

유닛의 `${GAME_PORT}` 형태는 하나의 인자로 확장한다. systemd의 실행 줄을 셸 스크립트로 취급하지 않으며, 환경파일 값 안에 `$N`, `${OTHER_VALUE}`, `$((...))`를 넣어 계산하거나 다른 값을 참조하지 않는다. `%i`는 던전 슬롯 번호로 환경파일·상태 디렉터리·로그 이름에만 사용한다. 실행 파일의 경로는 유닛 안의 절대 경로다. 근거: [systemd.service의 실행 줄 문법](https://github.com/systemd/systemd/blob/main/man/systemd.service.xml).

## 설치 전제와 설치 예시

운영 Linux에서 다음 전제를 먼저 갖춘다.

- `shinshi-game` 시스템 사용자·그룹이 존재하고 로그인 셸이 없다. 프로세스는 이 계정으로 실행한다.
- 검증한 Linux Dedicated Server 빌드 전체를 `/opt/shinshi/gameserver/releases/<buildId>/`에 두고, `/opt/shinshi/gameserver/current`가 선택한 릴리스를 가리킨다. 실행 파일의 배포 이름은 `ShinshiServer.x86_64`다. 실제 빌드 이름이 다르면 두 유닛의 `ExecStart`를 함께 맞춘다. 파일 소유자는 root이며 서비스 사용자는 읽기·실행만 할 수 있게 한다.
- Unity 런타임이 필요한 공유 라이브러리가 설치되어 있고, 필요한 서버 빌드 정의와 씬을 실제 Linux 빌드로 검증했다.
- `/etc/shinshi/gameserver/`는 root가 관리하고 환경파일은 root 소유 `0600`으로 둔다. 자리표시자를 모두 실제 비밀값 없는 설정으로 바꾼다.
- 게임 수신은 UDP 마을 `7777`, 던전 `7780..(7780 + N - 1)`만 연다. API HTTPS와 DB 포트는 분리한다. 상세 방화벽·TLS·비밀값 경계는 [공통 배포 안내](../README.md)를 따른다.

아래 명령은 운영 Linux에서 적용할 예시이며 이 작업에서 실행하지 않았다. 저장소 루트 기준이다.

```bash
sudo useradd --system --user-group --home-dir /var/lib/shinshi/gameserver --no-create-home --shell /usr/sbin/nologin shinshi-game
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

던전은 출정 하나가 끝나면 스스로 종료하고(종료 코드 0 재활용, 70 불변식 위반, 75 서버 풀 존재 상실), 새 bootId로 다시 떠야 하므로 `Restart=always`, 2초 뒤 재시작이다. 운영 알림은 70과 75에만 건다. 300초 동안 시작을 다섯 번으로 제한해 설정 오류나 지속 장애의 재시작 폭주를 막는다. 제한에 걸리면 원인을 해결한 뒤 해당 유닛의 `systemctl reset-failed`와 `start`로 재개한다. 명시적인 `systemctl stop`은 자동 재시작을 유발하지 않는다. `Restart=always`이므로 코드 0 종료도 재시작한다. 300초에 다섯 번 제한은 출정이 1분 안에 연달아 끝나는 경우에도 걸릴 수 있으니, 실측 뒤 던전 유닛의 `StartLimitBurst`를 조정한다.

Unity의 `-logFile -`와 stdout·stderr를 journald로 모은다. 던전은 유닛 번호와 `SyslogIdentifier`로 구분한다. 로그를 구조화 JSON으로 바꾸거나 공통 필드를 자동 추가하는 기능은 유닛에 없다. 로그 형식·보존·비밀값 제외는 [공통 로그 안내](../LOGGING.md)를 따른다.

```bash
sudo systemctl status shinshi-town.service shinshi-dungeon@1.service
sudo journalctl -u shinshi-town.service -f
sudo journalctl -u shinshi-dungeon@1.service -f
sudo ss -lunp
```

계획된 교체는 **신규 배정 중지 → 진행 세션 마감 → 정산 outbox 비우기 → 프로세스 종료** 순서다. 해당 절차를 구현한 제어 경로와 상태 확인이 마련되기 전에는 유닛의 중지만으로 안전한 세션 마감·정산을 보장할 수 없다. 현재 게임서버에 이 드레인·outbox·SIGTERM 연동이 구현됐다는 근거는 없다.

`systemctl stop`은 `SIGTERM`을 유닛의 프로세스 그룹에 전달하고 종료를 최대 300초 기다린다. 남은 프로세스는 `SIGKILL`로 종료한다. `TimeoutStopSec=300`은 임시 대기 상한이며, 드레인 절차를 수행하거나 세션 종료·정산 성공을 판정하지 않는다. 실제 최대 세션 마감 시간·저장 재시도 시간을 측정해 상한을 맞춘다. 미확정 경제 작업이 있거나 강제 종료됐으면 정상 마감으로 기록하지 않고 API의 장애 확인·복구 절차가 필요하다. 근거: [systemd.kill](https://github.com/systemd/systemd/blob/main/man/systemd.kill.xml).

## 비밀값과 남은 검증

예시는 서비스 자격·토큰·비밀번호를 포함하지 않는다. 그런 값을 실행 인자, URL, Unity 에셋·SO, 로그에 넣지 않는다. 게임서버의 API 서비스 자격 로딩 계약은 현재 실행 설정에 없으므로 임의 환경변수나 CLI 플래그를 추가하지 않았다. 후속 구현에서 외부 시크릿 공급과 실제 읽기 코드를 함께 연결한다. systemd의 `LoadCredential`을 선택할 경우에도 서버가 credential 파일을 읽는 코드와 해당 호스트 systemd 지원을 검증한 뒤 설정한다. 근거: [systemd.exec의 credential·환경 설정](https://github.com/systemd/systemd/blob/main/man/systemd.exec.xml).

대상 Linux에서 `systemd-analyze verify`, 비루트 실행·런타임 파일 쓰기, 마을+N 던전 UDP 바인딩, API HTTPS·서비스 인증, crash 재시작·폭주 제한, journald 출력, SIGTERM 처리·시간 초과, 실제 세션 드레인·outbox·재접속을 확인해야 한다. 이 작업은 파일·실행 계약의 정적 확인만 수행했으며 유닛 실행·Linux 서버 빌드·네트워크 통합·부하 측정은 미검증이다.

설계 근거: [기술 설계 3장·10장](../../docs/03-technical-design.md), [서버 운영 원칙 7장](../../docs/unity/NETWORK_AUTHORITY.md).
