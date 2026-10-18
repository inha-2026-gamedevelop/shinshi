# 서버 재시작과 경보 대응

게임 서버는 `systemctl`과 `journalctl`로, 업무 변경은 `shinshi-admin`으로 다룬다([도구 지도](README.md#도구-지도)). 유닛 이름과 경로는 [deploy/gameserver/README.md](../gameserver/README.md)와 유닛 파일 기준이다. 실행한 적 없는 정적 작성본이며 Linux 서버 빌드, 실제 서버 종료 신호 처리, 드레인은 모두 미검증이다.

## 서버 재시작과 드레인

### 현재 가능한 것과 없는 것

| 항목 | 사실(근거) |
|---|---|
| 유닛 | 마을 `shinshi-town.service` 하나, 던전 `shinshi-dungeon@<슬롯>.service` 슬롯 1..N(`shinshi-dungeon@1.service`부터). 포트는 마을 UDP 7777, 던전 슬롯 i는 7780+i-1 |
| 던전 수명 | 출정 하나가 끝나면 스스로 종료(종료 코드 0 재활용, 70 불변식 위반, 75 서버 풀 존재 상실)하고 `Restart=always`, `RestartSec=2`로 새 bootId와 함께 다시 뜬다. 300초 안에 시작 5회를 넘으면 systemd가 시작을 멈춘다 |
| 중지 | `systemctl stop`은 프로세스 그룹에 SIGTERM을 보내고 최대 300초(`TimeoutStopSec`) 기다린 뒤 SIGKILL. 이 시간은 임시 상한일 뿐 세션 마감·정산 성공을 판정하지 않는다 |
| 드레인 | **운영자가 시작하는 드레인, SIGTERM 연동, 정산 outbox 드레인은 구현됐다는 근거가 없다.** `shinshi-admin`에 서버를 드레인·재시작하는 명령은 없다. 아래 "계획된 교체"는 문서화된 순서를 수동 절차로 근사한 것이다 |
| 강제 종료된 서버 | 제한 시간 뒤 SIGKILL된 프로세스를 정상 정산으로 취급하지 않는다. 미확정 경제 작업은 [장애 세션 복구](incident-response.md#장애-세션-복구)가 필요하다 |

### 기본 확인 명령

```bash
sudo systemctl status shinshi-town.service shinshi-dungeon@1.service
sudo systemctl list-units 'shinshi-dungeon@*' --all
sudo journalctl -u shinshi-town.service -n 200 --no-pager
sudo journalctl -u shinshi-dungeon@1.service -f
sudo ss -lunp
```

### 계획된 교체·재시작 (마을 또는 던전 전체)

**언제.** 서버 빌드 교체, `CONTENT_VERSION`·환경파일 변경, 계획된 재시작.

**사전 확인.**

- 교체할 릴리스가 `/opt/shinshi/gameserver/releases/<buildId>/`에 있고 검증됐다. `CONTENT_VERSION`이 해당 빌드의 GameDB와 정확히 일치한다(불일치면 서버가 시작하지 않는다).
- `shinshi-admin service-state show`로 현재 점검·차단·허용 빌드·`revision`을 기록한다.

**절차.**

1. 신규 진입 차단. 공지(`notices create`)를 올리고 점검을 켠다: `shinshi-admin service-state set --maintenance on --reason "서버 교체"`. 점검은 새 출정 예약을 막지만 이미 로그인한 접속자의 마을 접속을 끊지 않는다([service-control.md](service-control.md#점검-시작)). 마을 접속자를 비우는 수단은 없다(공지와 대기).
2. 진행 세션 마감 대기. `shinshi-admin sessions --state Running --all`, `--state Auction`, `--state Settling`이 빌 때까지. 던전은 출정이 끝나면 스스로 종료·재기동한다. 오래 안 끝나는 세션은 서버가 살아 있는지 확인한다(`systemctl status`).
3. 미확정 저장·정산 outbox 확인. Grafana `Shinshi 게임 서버`의 던전 저장 큐(대기, 가장 오래된 나이, 재시도)가 0인지, API 로그의 `api.metrics`(60초마다) 값 `settlement_outbox_pending`을 본다. 경보 `ShinshiStorageQueueStuck`, `ShinshiOutboxBacklog`가 없어야 한다. outbox 소비가 실제로 연결돼 있는지는 미확인이다(조사 기록은 `PostgresOutboxDispatcher`가 DI 등록만 됐다고 함). 이 값이 안 줄면 서버를 끄기 전에 원인을 먼저 본다.
4. 중지. `sudo systemctl stop shinshi-town.service`(던전은 슬롯마다 `shinshi-dungeon@<i>.service`). 최대 300초 걸린다. 종료 로그를 `journalctl`로 확인한다. 300초를 넘겨 SIGKILL된 흔적(`journalctl`의 종료 사유)이 있으면 정상 정산이 아니다. 5번 전에 [장애 세션 복구](incident-response.md#장애-세션-복구)를 먼저 한다.
5. 교체. 새 릴리스를 `releases/<buildId>/`에 두고 `current`가 그 릴리스를 가리키게 한다(링크 방식은 gameserver README 서술을 따른다. 정확한 명령은 환경에서 확인). 환경파일(`/etc/shinshi/gameserver/*.env`)의 `CONTENT_VERSION`을 맞춘다. 유닛 파일을 바꿨다면 `sudo systemctl daemon-reload`.
6. 시작. `sudo systemctl start shinshi-town.service`, 던전 `sudo systemctl start shinshi-dungeon@<i>.service`.
7. 검증. 아래 "확인". 새 클라이언트 `buildId`를 허용 목록에 더한다(`service-state allow-build add`).
8. 신규 진입 재개. `shinshi-admin service-state set --maintenance off --reason "서버 교체 완료"`.

**확인.**

- `sudo systemctl status ...`가 `active (running)`이다. 이것은 프로세스 실행 상태일 뿐 API 연결·인증·배정 준비를 뜻하지 않는다.
- 로그에 서버 시작 로그가 있고 오류가 없다: `sudo journalctl -u shinshi-town.service -n 100`.
- `sudo ss -lunp`에 마을 UDP 7777, 던전 슬롯의 UDP 포트가 있다.
- Prometheus Targets에서 대상이 up이고 `ShinshiServerDown`이 없다.
- API의 서버 풀 등록 확인은 DB의 `server_instances`를 읽는 방법이 있지만(로컬 스택 스크립트가 쓴다) 운영 DB 직접 접근 권한은 정해지지 않았다(미정).
- 시험 계정으로 마을 입장과 출정 한 번.

**되돌리기.** `current`를 이전 릴리스로 되돌리고 `CONTENT_VERSION`을 이전 값으로 맞춰 다시 시작한다([빌드 롤백](data-and-release.md#빌드-롤백)).

### 던전 슬롯 하나만 재시작

**언제.** 한 슬롯이 `failed`이거나 시작 제한에 걸렸을 때, 설정을 바꿨을 때.

**사전 확인.** 그 슬롯에 진행 중 세션이 있는지: `shinshi-admin sessions --state Running --all`의 "서버" 열이 해당 인스턴스인지. 진행 중이면 끝나길 기다린다. 끝났으면 던전은 어차피 스스로 종료·재기동한다.

**명령.**

```bash
sudo systemctl restart shinshi-dungeon@3.service
# 시작 제한(300초에 5회)에 걸려 failed 일 때, 원인을 고친 뒤
sudo systemctl reset-failed shinshi-dungeon@3.service
sudo systemctl start shinshi-dungeon@3.service
```

**확인.** `systemctl status`, `journalctl`, `ss -lunp`. 진행 중이던 세션을 끊었다면 서버 소실 판정(수 초~수십 초, 정확한 시간 미확인) 뒤 `sessions`에서 `서버 소실`이 `예`가 되면 `recover --action service-cancel`.

**되돌리기.** 재시작 자체는 되돌릴 수 없다.

### 마을 비계획 재시작·장애

마을 프로세스가 죽으면 systemd가 `Restart=always`로 다시 띄운다(`ShinshiServerRestarted`, 필요하면 `ShinshiServerDown`). 접속자는 연결이 끊긴다. 재접속 동작과 마을 중계 중이던 성장·생활 요청의 영향은 실행 검증이 없어 미확인이다. 재기동 뒤 `journalctl`에서 시작 로그와 API 등록 오류를 확인하고, 확정되지 않은 작업은 같은 operationId로 재시도하는 설계(클라이언트 쪽)를 따른다.

### API 컨테이너 재시작·교체

API 컨테이너 이름과 배포 방식은 배포 환경 결정 전이다. `docker ps`로 이름을 확인해 `docker restart <이름>`(기본 10초 뒤 SIGKILL)이나 이미지 교체를 한다. 마이그레이션은 실행 중인 API와 분리해 `--migrate` 하나의 인자로 실행하고, 시작 때 자동 실행하지 않는다([data-and-release.md](data-and-release.md#빌드-롤백)). API가 멈춘 동안 던전 저장 큐가 쌓이고 `ShinshiStorageQueueStuck`이 뜰 수 있다. API가 돌아오면 큐가 줄어드는지 Grafana로 본다(재시도 동작은 실행 미검증).

## 경보 대응

경보 규칙은 [prometheus/alerts.yml](../monitoring/prometheus/alerts.yml)이다. **Alertmanager가 연결되기 전에는 경보가 Prometheus의 Alerts 화면에서만 보이고 누구에게도 전달되지 않는다.** 아래는 이 문서를 쓴 2026-10-07 작업 트리 기준이며, API 경보 6종과 Alertmanager는 다른 작업이 추가 중인 미커밋 변경이다(이름과 조건이 바뀔 수 있다). 임계값은 모두 초기값이고 Linux 부하 시험 뒤에 다시 정한다.

공통 순서: 경보 이름과 대상(`instance`, `role`)을 사고 기록에 적는다 → 아래 표의 확인 → 서버·DB를 건드리기 전에 [점검](service-control.md#점검-시작)이나 [기능 차단](service-control.md#기능-차단)이 필요한지 판단한다 → 조치 → 해소 확인.

### 기존 게임 서버 경보

| 경보 | 조건 | 심각도 | 대응 |
|---|---|---|---|
| `ShinshiServerDown` | 게임 서버 `/metrics`가 1분 응답 없음 | critical | `sudo systemctl status`(해당 유닛), `journalctl -u`로 프로세스가 죽었는지, 시작 제한(`failed`)에 걸렸는지 확인한다. 프로세스는 살아 있는데 지표만 안 나오는 경우도 있다(지표 포트 설정 오류는 서버를 멈추지 않고 오류 로그만 남긴다): 이때는 `ss -lunp`로 게임 포트가 열려 있는지 보고 `METRICS_PORT`·`METRICS_BIND_ADDRESS`·보안 그룹을 점검한다. 던전이 죽었다면 `sessions`로 서버 소실 세션을 찾아 [복구](incident-response.md#장애-세션-복구). 제한에 걸렸으면 원인 수정 뒤 `reset-failed`와 `start` |
| `ShinshiTickP95OverBudget` | 틱 p95가 틱 예산을 2분 초과 | warning | Grafana `Shinshi 게임 서버`의 틱·접속 수·RSS·네트워크를 본다. 한 인스턴스인지 전체인지 구분한다. 즉시 재시작하지 않는다. 지속되면 새 출정을 막는 기능 차단(`dungeon_entry`)이나 점검을 판단하고 기록한다. 부하 시험 전 임계값이라 오탐 가능 |
| `ShinshiTickOverrunRate` | 예산 초과 틱이 5분간 초당 1회 초과 | warning | 위와 같다. 같은 시각의 배포·맵 이벤트·접속 급증을 확인한다 |
| `ShinshiStorageQueueStuck` | 던전 저장 큐의 가장 오래된 이벤트가 30초 초과(1분 지속) | critical | **API가 세션 이벤트를 확정하지 못하는 상태**다. 먼저 API와 DB를 본다(`ShinshiApiDown`, `ShinshiApiNotReady`, API 로그). 서버 프로세스를 먼저 재시작하지 않는다(재시작이 큐의 미확정 이벤트를 잃게 하는지 미확인, 강제 종료 세션은 정상 정산이 아니다). API가 회복되면 큐가 줄어드는지 본다. 서버가 이미 죽었으면 [세션 복구](incident-response.md#장애-세션-복구) |
| `ShinshiFrameOverrunRate` | 전체 프레임 간격이 틱 예산의 1.5배를 넘은 횟수가 5분간 초당 0.2회 초과 | warning | 전투 틱 지표(`ShinshiTickOverrunRate`)와 별개다. 틱 지표는 전투 Step만 재므로 가비지 수집, 물리, 네트워크 틱, 로그 쓰기 정지는 여기서만 보인다. Grafana `Shinshi 게임 서버`의 "전체 프레임 간격"과 관리 힙, `log_queue_depth`를 같은 시각으로 맞춰 본다. 임계값은 부하 시험 전이라 오탐 가능 |
| `ShinshiFrameStall` | 최근 1~2분 안에 프레임 간격이 500ms를 넘은 적이 있음(1분 지속) | warning | `server_frame_interval_max_ms`는 직전과 현재 60초 창 중 큰 값이다. 한 번의 긴 정지(가비지 수집, 디스크·표준 출력 대기, 스레드 풀 고갈)가 이유다. `server_frame_stall_max_1s_ms`는 마지막 1초 창의 최댓값이라 수집 주기에 따라 놓칠 수 있다 |
| `ShinshiKcpSendBacklog` | 한 연결의 KCP 송신 큐와 송신 버퍼 합이 경고 임계값(기본 2000)을 넘음(1분 지속) | warning | 느린 클라이언트나 서버의 과다 송신이다. 서버 로그의 `[Kcp] 연결 송신 대기열이 임계값을 넘었다` 줄이 연결 번호를 알려 준다. 서버는 이 값으로 연결을 끊지 않는다. Mirror는 수신·송신 큐·버퍼 합이 10000이 되면 그 연결을 끊는다. `network_message_send_bytes_total`(메시지 상세를 켠 경우)로 보내는 쪽을 확인한다. 이 지표는 `KcpTransport.server`를 리플렉션으로 읽어 만든다(코드 제거가 필드를 지우면 지표가 없다) |
| `ShinshiBackendGatewayBacklog` | 백엔드 응답 대기 요청이 200개 초과(상한 256) 또는 메인 스레드 전달을 기다리는 결과가 500개 초과(1분 지속) | warning | API가 느리거나 응답 처리가 프레임당 상한에 막힌 상태다. API 경보(`ShinshiApiNotReady`, 오류율)와 `ShinshiStorageQueueStuck`을 함께 본다. 상한에 닿으면 새 요청이 `Overloaded`로 거절된다 |
| `ShinshiLogLinesDropped` | 로그 쓰기 큐가 가득 차 5분 안에 줄이 버려짐 | warning | 표준 출력을 읽는 쪽(systemd-journald 등)이 느리거나 로그가 폭주한다. 버려진 개수는 `log_dropped_total`과 로그의 `로그 큐가 가득 차 줄을 버렸다` 줄에 남는다. 사건 로그가 일부 없을 수 있다는 뜻이므로 장애 분석 때 감안한다 |
| `ShinshiServerRestarted` | 10분 안에 프로세스 재시작 | info | 던전은 출정마다 정상적으로 재시작한다. `systemctl status`/`journalctl`의 종료 코드를 본다: 0은 정상, **70(불변식 위반)과 75(서버 풀 존재 상실)는 운영 알림 대상**이다. 같은 슬롯이 반복 재시작하면 시작 제한과 `ShinshiServerDown`으로 이어진다 |

### API·DB·대사 경보 (추가 중)

| 경보 | 조건(작업 트리 정의) | 심각도 | 대응 |
|---|---|---|---|
| `ShinshiApiDown` | `up{job="shinshi-api"} == 0`, 1분. API `/metrics`가 응답 없음(프로세스 중단, 네트워크, `Metrics:AllowedNetworks`가 Prometheus를 막는 경우 포함) | critical | API 컨테이너 상태(`docker ps`, `docker logs`)와 프록시·포트를 본다. Prometheus Status > Targets의 오류 메시지가 원인을 구분해 준다. 컨테이너가 죽었으면 재시작하고 `GET /v1/service-state`와 `/health/ready`로 확인. 게임 서버 저장 큐 경보가 같이 뜰 수 있다 |
| `ShinshiApiNotReady` | `probe_success{job="shinshi-api-ready"} == 0`, 1분. `/health/ready` 실패(503이면 DB `SELECT 1` 실패) | critical | DB를 본다: 인스턴스·컨테이너 상태, 연결 수, 디스크, 네트워크. API는 DB가 죽어도 마지막 성공 값으로 `GET /v1/service-state`에 200을 줄 수 있으므로 **200이 DB 정상을 뜻하지 않는다**. 점검·공지 저장도 DB 쓰기라 DB가 죽어 있는 동안은 `shinshi-admin`의 쓰기가 실패할 수 있다(실행 미검증). 읽기에 실패하면 API는 마지막 성공 상태를 유지하다가 첫 읽기 전에는 점검 중으로 본다. DB가 돌아온 뒤 [점검](service-control.md#점검-시작)과 공지를 판단한다. 복구 불능이면 [DB 복원](data-and-release.md#db-복원) |
| `ShinshiApiErrorRate` | 5xx가 5분 평균 요청의 5% 초과(요청이 일정 이상일 때만) | warning | API 로그의 `api.request.completed`에서 5xx 경로와 `api.metrics`의 p95를 본다. 최근 배포·마이그레이션이 원인이면 [빌드 롤백](data-and-release.md#빌드-롤백)을 판단한다. 특정 기능이면 [기능 차단](service-control.md#기능-차단) |
| `ShinshiReconciliationMismatch` | 마지막 일일 대사의 불일치 수 > 0, 5분 지속 | critical | 일일 대사가 원장 합계와 현재 잔액이 다른 계정·자산을 찾았다. API 로그의 `Economy ledger mismatch on <날짜>: account ..., character ..., asset ...`(ID만, 금액 없음)로 대상을 찾는다. `ledger --account <id> ... --reason`의 합계와 `accounts show <id> --reason`의 잔액을 비교하고 최근 지급·조정·복구·경매를 `operation`/`session-events`로 추적한다. 중복 지급이 원인이면 [경제 사고 1차 대응](incident-response.md#경제-사고-1차-대응-경매-중복-지급-의심). 대사는 읽기 전용이라 자동으로 고치지 않는다. DB 복원 직후라면 복원 시점과 현재 사이 차이도 원인일 수 있다 |
| `ShinshiReconciliationStale` | 마지막 대사가 26시간 이상 전 | warning | 일일 대사는 API 시작 때 한 번, 이후 15분마다 오늘(UTC) 실행을 선점해 한 번만 돈다. API가 계속 떠 있었는지, 로그에 `Economy reconciliation failed`(예외 형식과 SQLSTATE)가 있는지 본다. 선점이 끝나지 않은 채 2시간이 지나면 다른 인스턴스가 이어받는다. DB에서 `SELECT * FROM economy_reconciliation_runs ORDER BY run_date DESC LIMIT 3;`로 `finished_at`, `mismatch_count`를 볼 수 있다(운영 DB 접근 권한은 미정). API 재시작은 즉시 한 번 시도한다 |
| `ShinshiOutboxBacklog` | 미전달 정산 outbox가 20개 초과, 10분 지속 | warning | API 로그 `api.metrics`의 `settlement_outbox_pending`도 같은 값이다. outbox 소비 스케줄러 연결 여부가 확인되지 않았다(조사 기록은 미연결). 미연결이면 이 경보는 정산 장애가 아니라 소비자 미구현 신호일 수 있다. 정산 자체가 멈췄는지는 `sessions`(Settling 정체)와 `ShinshiStorageQueueStuck`으로 따로 본다. 원인 불명이면 서버를 재시작하기 전에 개발자에게 확인한다 |

### 경보를 받은 뒤 공통 마무리

- 해소 조건(경보가 사라졌고 원인을 확인했는가)을 사고 기록에 적는다.
- 공지가 필요하면 [공지](service-control.md#공지-작성과-종료)를 올린다.
- 경보 시험(실제로 메시지를 받아 보는 것)은 Alertmanager 연결 뒤 [rehearsal.md](rehearsal.md)의 체크리스트에 있다. 현재 미실행이다.
