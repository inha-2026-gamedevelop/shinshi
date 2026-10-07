# 게임 서버·API 실시간 모니터링과 경보

게임 서버 프로세스마다 Prometheus 텍스트 형식의 `GET /metrics`를 엽니다. Prometheus가 5초마다 이 주소를 수집하고, Grafana 대시보드 `Shinshi 게임 서버`에 보여 줍니다. API(`Shinshi.Api`)도 `GET /metrics`를 15초마다 수집해 `Shinshi API` 대시보드에 보여 주고, blackbox exporter가 `GET /health/ready`를 15초마다 확인합니다. 경보 규칙이 걸리면 Alertmanager가 Discord 호환 웹훅으로 보냅니다. 수집 대상 주소는 `prometheus/targets/`의 JSON 파일에 둡니다.

```text
게임 서버(마을 1, 던전 N) --GET /metrics-----------> Prometheus(보관, 경보 규칙) --> Grafana(대시보드)
API --GET /metrics--------------------------------> Prometheus                 \
API <--GET /health/ready-- blackbox exporter <------ Prometheus                  --> Alertmanager --> 웹훅(Discord)
```

지표 이름과 단위는 [LOGGING.md](../LOGGING.md)의 표를 따릅니다. 60초마다 남기는 `server.metrics` JSON 로그도 그대로 남습니다.

## 게임 서버 설정

| 환경변수 | CLI | 값 |
|---|---|---|
| `METRICS_PORT` | `-metrics-port` | 없거나 `0`이면 끕니다. 마을은 `9310`, 던전 슬롯 i는 `9320 + (i - 1)`을 권장합니다 |
| `METRICS_BIND_ADDRESS` | `-metrics-bind` | 기본값 `127.0.0.1`. Prometheus가 다른 호스트에 있을 때만 내부망 IP를 줍니다. 호스트 이름은 받지 않습니다 |
| `METRICS_MESSAGE_DETAIL` | `-metrics-message-detail` | `1`이면 Mirror 메시지 종류별 대역폭을 셉니다. 메시지를 보낼 때마다 할당이 생기므로 프로파일링할 때만 켭니다 |

- 설정 값이 틀리거나 포트를 열지 못해도 게임 서버는 계속 뜹니다. 대신 오류 로그를 남기고, Prometheus의 `ShinshiServerDown` 경보로 드러납니다.
- 지표 포트는 공개 보안 그룹에 열지 않습니다. 게임 포트(UDP)와 달리 Prometheus가 있는 호스트에서만 TCP로 접근하게 둡니다.
- 에디터와 개발 빌드에서도 `-metrics-port`를 주면 켜집니다.

## API 설정

API 쪽 계약은 Backend README의 "헬스 체크"와 "API Prometheus 지표"가 기준입니다. 지표 이름(`shinshi_api_*`)은 Backend 구현 기준이므로, Backend에서 이름이 바뀌면 [prometheus/alerts.yml](prometheus/alerts.yml)과 [grafana/dashboards/shinshi-api.json](grafana/dashboards/shinshi-api.json)을 함께 고칩니다.

| 경로 | 동작 |
|---|---|
| `GET /health/live` | 항상 200. DB를 보지 않습니다. Docker `HEALTHCHECK`가 씁니다 |
| `GET /health/ready` | DB `SELECT 1`이 성공하면 200, 실패하면 503. blackbox exporter가 확인합니다 |
| `GET /metrics` | `Metrics:AllowedNetworks`(환경 변수 `Metrics__AllowedNetworks__0` 등) 안의 주소에만 답하고 밖은 404입니다. 비어 있으면 루프백만 허용합니다 |

- Prometheus 컨테이너가 API를 부르는 출발 주소가 `Metrics__AllowedNetworks__*`에 들어 있어야 합니다. 로컬 Docker Desktop에서는 API가 보는 출발 주소가 Docker 게이트웨이 대역이므로, 허용 대역을 넣고 Targets 화면에서 `shinshi-api`가 `UP`인지 확인합니다(허용 대역을 못 맞추면 404로 `ShinshiApiDown`이 울립니다). 운영에서는 Prometheus 호스트의 사설 IP 하나만 허용합니다. API가 다른 호스트라면 컨테이너를 루프백이 아니라 그 호스트의 사설 IP에 publish해야 합니다([api/docker-compose.example.yml](../api/docker-compose.example.yml)의 `API_BIND_ADDRESS`). 보안 그룹은 5080을 HTTPS 진입점과 Prometheus 호스트에만 열고, 대상 파일에는 그 사설 `IP:5080`을 적습니다.
- `/metrics`와 `/health/*`는 공개 HTTPS 프록시로 라우팅하지 않습니다.
- 대상 파일은 [prometheus/targets/api-metrics.json](prometheus/targets/api-metrics.json)(수집)과 [prometheus/targets/api-ready.json](prometheus/targets/api-ready.json)(준비 확인)이며 둘 다 `host:port`만 적습니다. 기본값은 개발 PC의 `host.docker.internal:5080`입니다. 운영에서는 API 호스트의 사설 주소로 바꿉니다. 복제본이 여럿이면 각각 한 줄씩 추가합니다. 게임 서버 job은 `api-*.json`을 읽지 않습니다.
- API 대상 파일을 비워 두면 API 경보는 아무것도 감시하지 않습니다(없는 대상은 `up`이 없어 `ShinshiApiDown`도 울리지 않음). 운영 배포 때 두 파일에 대상이 들어 있는지 확인합니다.

## 로컬 실행

Docker Desktop이 필요합니다.

```powershell
cd deploy/monitoring
Copy-Item .env.example .env   # GRAFANA_ADMIN_PASSWORD와 ALERT_WEBHOOK_URL을 채운다. .env는 Git에서 제외된다
docker compose --env-file .env up -d
```

- Prometheus는 <http://127.0.0.1:9090>, Alertmanager는 <http://127.0.0.1:9093>, Grafana는 <http://127.0.0.1:3000>에서 엽니다. Grafana 계정은 `.env`의 값입니다.
- blackbox exporter는 포트를 열지 않고 compose 네트워크 안에서 Prometheus만 부릅니다.
- 기본 대상 파일 [prometheus/targets/local.json](prometheus/targets/local.json)은 개발 PC의 마을 `9310`, 던전 1번 `9320`을 가리킵니다.
- 대상 상태는 Prometheus의 Status > Targets에서 확인합니다. 게임 서버와 API를 띄우지 않았다면 해당 대상은 `down`이고 1분 뒤 `ShinshiServerDown`·`ShinshiApiDown`이 울립니다.
- 기존 `.env`에 `ALERT_WEBHOOK_URL`이 없으면 `docker compose up`이 변수 미설정 오류로 실패합니다. 경보를 받을 곳이 없는 채로 켜지는 것을 막으려는 것입니다. 웹훅 없이 화면만 볼 때는 쓰지 않는 주소(예: `http://127.0.0.1:9/`)를 넣으면 되고, 그러면 Alertmanager 로그에 전송 실패만 남습니다.

## 운영 배치

1. Prometheus, Alertmanager, Grafana는 게임 서버·API와 같은 사설망의 별도 호스트에 둡니다. UI는 SSH 터널이나 VPN으로만 엽니다(compose가 `127.0.0.1`에만 바인드합니다). 경보 전달도 이 호스트가 맡으므로 이 호스트가 죽으면 경보도 멈춥니다. 체험판에서는 이 한계를 받아들이고, 필요해지면 외부 생존 확인(외부 업타임 서비스)을 더합니다.
2. 각 `town.env`와 `dungeon-i.env`에 `METRICS_PORT`를 넣습니다. Prometheus가 다른 호스트에 있으면 `METRICS_BIND_ADDRESS`에 게임 서버의 사설 IP를 넣습니다.
3. `prometheus/targets/`에 운영용 파일(예: `prod.json`)을 만들고 `local.json`은 지웁니다. 형식은 `local.json`과 같고, 라벨 `role`과 `slot`을 붙입니다. 파일을 바꾸면 Prometheus가 30초 안에 다시 읽습니다.
4. 보안 그룹은 Prometheus 호스트에서 지표 포트로 들어오는 TCP만 허용합니다. API도 Prometheus 호스트 출발만 `/metrics`·`/health/ready`에 닿게 하고 `Metrics__AllowedNetworks__0`을 그 호스트의 사설 IP로 둡니다.
5. 아웃바운드 HTTPS(443)로 웹훅 도메인(Discord라면 `discord.com`)에 나갈 수 있어야 합니다.

## 경보 전달 (Alertmanager)

설정은 [alertmanager/alertmanager.yml](alertmanager/alertmanager.yml)입니다. 받는 쪽은 Alertmanager 내장 `discord_configs`(0.25 이상, 이 구성은 0.28.1)이고, 웹훅 URL은 비밀값이라 파일에 쓰지 않습니다. `.env`의 `ALERT_WEBHOOK_URL`을 compose `secrets`가 컨테이너의 `/run/secrets/alert_webhook_url`로 마운트하고, 설정이 `webhook_url_file`로 읽습니다. `.env`는 Git에서 제외되고 `.env.example`에는 자리표시자만 있습니다. 웹훅 URL이 노출되면 Discord에서 웹훅을 지우고 다시 만듭니다.

| 항목 | 값 | 이유 |
|---|---|---|
| 묶는 기준 | `alertname` | 던전 슬롯 여러 개가 같은 경보로 한 메시지에 모입니다 |
| `group_wait` | 30초 | 한 사고에서 연달아 오는 경보를 한 번에 묶습니다 |
| `group_interval` | 5분 | 이미 보낸 묶음에 새 경보가 붙을 때의 최소 간격 |
| `repeat_interval` | critical 4시간, warning 12시간 | 해결 안 된 경보로 채널을 도배하지 않습니다 |
| `severity="info"` | 보내지 않음(`null` 수신자) | `ShinshiServerRestarted`는 던전이 출정 하나가 끝날 때마다 정상 재시작하므로 매번 울립니다. Prometheus·Alertmanager 화면에서만 봅니다 |
| 억제 | 같은 `instance`에서 `ShinshiApiDown`이 울리면 그 인스턴스의 `NotReady`·`OutboxBacklog`·`ReconciliationStale`·`DbMetricsUnavailable`을 숨김 | 복제본 하나가 죽어도 다른 복제본의 경보는 가리지 않습니다. `ErrorRate`·대사 불일치는 억제하지 않습니다 |

해결되면(`send_resolved`) 해결 메시지도 보냅니다. 값은 소규모 팀 기준의 초기값이며 알림이 너무 많거나 적으면 이 표의 값부터 조정합니다.

### 시험 경보

웹훅을 실제로 받는지 확인하는 방법입니다(`ShinshiTestAlert`는 규칙에 없는 이름이라 사고와 구분됩니다).

```powershell
docker compose exec alertmanager amtool alert add ShinshiTestAlert severity=warning --annotation=summary="test alert from OP-2" --alertmanager.url=http://localhost:9093
```

`group_wait`(30초) 뒤 웹훅 채널에 메시지가 와야 합니다. 안 오면 `docker compose logs alertmanager`에서 전송 오류를 봅니다.

Discord가 아닌 채널(Slack, 사내 메신저 등)로 바꿀 때는 `receivers`만 교체합니다. Slack은 `slack_configs`, 그 밖의 JSON 웹훅은 `webhook_configs`지만 본문 형식이 Alertmanager 고유라 받는 쪽 어댑터가 필요합니다.

## 대시보드

| 줄 | 패널 |
|---|---|
| 상태 | 응답 중인 서버, 응답 없는 서버, 접속 수 합계, 가동 시간 |
| 서버 틱 | 틱 시간 p50/p95/p99와 예산선, 예산 초과 틱(초당) |
| 네트워크 | 송신·수신 대역폭, 접속 1개당 송신, 메시지 종류별 상위 10(세부 집계를 켰을 때), 접속 수, 스폰된 개체 |
| 메모리와 저장 | RSS, 관리 힙, 던전 저장 큐(대기·가장 오래된 나이·재시도) |

분위수는 Prometheus가 히스토그램 칸에서 계산합니다. 인스턴스별 p95를 평균해서 전체 p95라고 읽지 않습니다.

`Shinshi API`([grafana/dashboards/shinshi-api.json](grafana/dashboards/shinshi-api.json)):

| 줄 | 패널 |
|---|---|
| 상태 | 지표 응답, 준비 상태(`/health/ready`), 마지막 대사 불일치, 마지막 대사 후 경과, 정산 outbox 대기, 요청률 |
| 요청 | 상태 분류별 요청률, 응답 시간 p50/p95/p99, 5xx 비율, 5xx 경로별 상위 10 |
| 정산·대사 | outbox 대기 추이, 대사 경과·불일치 추이 |

요청 패널은 헬스·지표 경로를 뺍니다(프로브가 트래픽과 오류율을 흐리지 않게). 대시보드 파일은 `grafana/dashboards/`에 두면 Grafana가 시작할 때 읽습니다.

## 경보 규칙

[prometheus/alerts.yml](prometheus/alerts.yml)의 초기값입니다. Linux 부하 시험과 실제 트래픽 뒤에 다시 정합니다. 위 Alertmanager로 전달되며 Prometheus의 Alerts 화면에서도 봅니다. API 규칙은 단위 시험 [prometheus/alerts.test.yml](prometheus/alerts.test.yml)로 확인합니다.

```powershell
docker run --rm -v "${PWD}/prometheus:/etc/prometheus:ro" --entrypoint promtool prom/prometheus:v3.5.0 test rules /etc/prometheus/alerts.test.yml
```

| 경보 | 조건 |
|---|---|
| `ShinshiServerDown` | 지표 주소가 1분 동안 응답하지 않음 |
| `ShinshiTickP95OverBudget` | 틱 p95가 틱 예산을 2분 동안 넘음 |
| `ShinshiTickOverrunRate` | 예산 초과 틱이 5분 동안 초당 1회를 넘음 |
| `ShinshiStorageQueueStuck` | 던전 저장 큐의 가장 오래된 이벤트가 30초를 넘음 |
| `ShinshiServerRestarted` | 10분 안에 프로세스가 다시 시작됨(info, 채널로 보내지 않음) |
| `ShinshiApiDown` | API `/metrics` 수집이 1분 동안 실패(프로세스 중단, 네트워크, `Metrics:AllowedNetworks`가 Prometheus를 막음). critical |
| `ShinshiApiNotReady` | `/health/ready`가 1분 동안 실패(503은 DB `SELECT 1` 실패). critical |
| `ShinshiApiErrorRate` | 헬스·지표를 뺀 요청의 5xx 비율이 5분 동안 5% 초과이고 요청률이 0.1/초(5분에 약 30건) 초과. warning |
| `ShinshiApiMonitoringMissing` | `shinshi-api` 수집 결과나 `shinshi-api-ready` 프로브 결과 시리즈가 5분 동안 아예 없음(대상 파일이 비었거나 blackbox가 죽음). critical |
| `ShinshiApiProbeUnavailable` | blackbox exporter가 2분 동안 응답하지 않아 `/health/ready`를 감시하지 못함. critical |
| `ShinshiApiDbMetricsUnavailable` | outbox·대사 게이지가 NaN이거나(DB 조회 실패) 살아 있는 API에서 사라진 상태가 5분 지속(대사 기록이 없어 mismatch만 NaN인 경우는 제외). warning |
| `ShinshiReconciliationMismatch` | 마지막 완료 대사의 불일치 수가 5분 동안 0 초과. critical |
| `ShinshiReconciliationStale` | 마지막 대사 완료로부터 26시간 초과가 10분 지속(대사가 한 번도 없으면 timestamp 0이라 해당). warning |
| `ShinshiOutboxBacklog` | 정산 outbox 대기 행이 20 초과인 상태가 10분 지속. warning |

API 지표 이름은 Backend 구현 기준입니다. 이름이 바뀌면 이 표의 규칙을 같이 고칩니다. 대사·outbox 게이지는 DB 조회 실패 시 `NaN`이라 위 비교가 거짓이 되므로, DB 장애는 `ShinshiApiNotReady`가 맡습니다. 불일치는 마지막 완료 대사의 값이라 다음 대사가 0건으로 끝나거나 원인을 해결할 때까지 경보가 남습니다(`repeat_interval`마다 다시 알림). 불일치의 세부는 API 로그와 어드민 조회로 봅니다.

## 아직 없는 것

- 실제 채널로 시험 경보를 받아 본 확인. 구성은 로컬 웹훅 수신 서버로 끝까지 확인했지만 실제 Discord(또는 정해질 채널) 웹훅은 아직 연결하지 않았습니다.
- 경보 호스트 자체의 생존 확인(외부 업타임), 장기 보관, 로그 검색 스택(OP-12). 로그는 journald·Docker 로그 드라이버로 남깁니다([LOGGING.md](../LOGGING.md)).
- 게임 서버의 정산 불일치·`storage_failure_total` 지표.
- 클라이언트 쪽 지표(프레임, 핑). 클라이언트는 화면 안의 `NetworkOverlayView`로 봅니다.
