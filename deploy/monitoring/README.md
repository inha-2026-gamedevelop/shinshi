# 게임 서버 실시간 모니터링

게임 서버 프로세스마다 Prometheus 텍스트 형식의 `GET /metrics`를 엽니다. Prometheus가 5초마다 이 주소를 수집하고, Grafana 대시보드 `Shinshi 게임 서버`에 보여 줍니다. 수집 대상 주소는 `prometheus/targets/*.json`에 둡니다.

```text
게임 서버(마을 1, 던전 N) --GET /metrics--> Prometheus(보관, 경보 규칙) --> Grafana(대시보드)
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

## 로컬 실행

Docker Desktop이 필요합니다.

```powershell
cd deploy/monitoring
Copy-Item .env.example .env   # GRAFANA_ADMIN_PASSWORD를 바꾼다. .env는 Git에서 제외된다
docker compose --env-file .env up -d
```

- Prometheus는 <http://127.0.0.1:9090>, Grafana는 <http://127.0.0.1:3000>에서 엽니다. Grafana 계정은 `.env`의 값입니다.
- 기본 대상 파일 [prometheus/targets/local.json](prometheus/targets/local.json)은 개발 PC의 마을 `9310`, 던전 1번 `9320`을 가리킵니다.
- 대상 상태는 Prometheus의 Status > Targets에서 확인합니다.

## 운영 배치

1. Prometheus와 Grafana는 게임 서버와 같은 사설망의 별도 호스트에 둡니다. UI는 SSH 터널이나 VPN으로만 엽니다(compose가 `127.0.0.1`에만 바인드합니다).
2. 각 `town.env`와 `dungeon-i.env`에 `METRICS_PORT`를 넣습니다. Prometheus가 다른 호스트에 있으면 `METRICS_BIND_ADDRESS`에 게임 서버의 사설 IP를 넣습니다.
3. `prometheus/targets/`에 운영용 파일(예: `prod.json`)을 만들고 `local.json`은 지웁니다. 형식은 `local.json`과 같고, 라벨 `role`과 `slot`을 붙입니다. 파일을 바꾸면 Prometheus가 30초 안에 다시 읽습니다.
4. 보안 그룹은 Prometheus 호스트에서 지표 포트로 들어오는 TCP만 허용합니다.

## 대시보드

| 줄 | 패널 |
|---|---|
| 상태 | 응답 중인 서버, 응답 없는 서버, 접속 수 합계, 가동 시간 |
| 서버 틱 | 틱 시간 p50/p95/p99와 예산선, 예산 초과 틱(초당) |
| 네트워크 | 송신·수신 대역폭, 접속 1개당 송신, 메시지 종류별 상위 10(세부 집계를 켰을 때), 접속 수, 스폰된 개체 |
| 메모리와 저장 | RSS, 관리 힙, 던전 저장 큐(대기·가장 오래된 나이·재시도) |

분위수는 Prometheus가 히스토그램 칸에서 계산합니다. 인스턴스별 p95를 평균해서 전체 p95라고 읽지 않습니다.

## 경보 규칙

[prometheus/alerts.yml](prometheus/alerts.yml)의 초기값입니다. Linux 부하 시험 뒤에 다시 정합니다. Alertmanager(메신저 알림 전송)는 아직 연결하지 않았으므로 Prometheus의 Alerts 화면에서만 보입니다.

| 경보 | 조건 |
|---|---|
| `ShinshiServerDown` | 지표 주소가 1분 동안 응답하지 않음 |
| `ShinshiTickP95OverBudget` | 틱 p95가 틱 예산을 2분 동안 넘음 |
| `ShinshiTickOverrunRate` | 예산 초과 틱이 5분 동안 초당 1회를 넘음 |
| `ShinshiStorageQueueStuck` | 던전 저장 큐의 가장 오래된 이벤트가 30초를 넘음 |
| `ShinshiServerRestarted` | 10분 안에 프로세스가 다시 시작됨 |

## 아직 없는 것

- API(`Shinshi.Api`)의 `/metrics`. API는 같은 이름의 지표를 60초 `api.metrics` 로그로만 남깁니다.
- Alertmanager 알림 전송, 장기 보관.
- 클라이언트 쪽 지표(프레임, 핑). 클라이언트는 화면 안의 `NetworkOverlayView`로 봅니다.
