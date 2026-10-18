# 배포 로그와 지표 규약

적용 서비스는 `api`, `town`, `dungeon`이다. UTF-8 JSON 객체 하나를 한 줄로 표준 출력에 기록하고 오류도 같은 형식을 사용한다. systemd의 journald 또는 컨테이너 로그 수집기는 이 줄을 보존한다. 수집 시각으로 발생 시각을 덮어쓰지 않는다.

이 문서는 구현할 공통 규약이다. API Dockerfile은 `shinshi-json` 콘솔 포매터(`Backend/src/Shinshi.Api/Observability`)를 켜고, 이 포매터가 아래 공통 키로 한 줄씩 쓴다. `instanceId`, `buildId`, `contentVersion`은 실행 환경의 `Observability__InstanceId`, `Observability__BuildId`, `Observability__ContentVersion`에서 오며 없으면 `null`이다. 프레임워크(Microsoft.*) 로그는 메시지 값을 버리고 `data.template`만 남긴다. 요청마다 `api.request.completed`(경로 템플릿, 메서드, 상태, 소요 ms), 60초마다 `api.metrics`(`data.metrics`)를 남긴다. 게임 서버 빌드(`UNITY_SERVER`)는 `ServerBootstrap`이 `ServerJsonLogHandler`(`Assets/01.Scripts/98.Servers/01.Network/Observability/`)를 설치해 Unity `Debug.Log*` 문자열을 `unity.log`(예외는 `unity.exception`) 이벤트의 공통 JSON 한 줄로 바꾼다. 메시지는 Bearer 값을 가린 뒤 `data.message`에 넣고, `buildId`는 빌드 신원 파일을 읽은 뒤부터 채운다(그 전 줄은 `null`). 서버 시작은 `server.started`, 드레인·강제 종료는 `server.drain.requested`·`server.shutdown.forced`, 60초마다 `server.metrics`를 남긴다. 에디터와 클라이언트 빌드는 설치하지 않아 평문 그대로다. API의 `--migrate`·관리자 부트스트랩 실행기가 직접 stdout/stderr에 쓰는 문장은 여전히 공통 JSON으로 자동 변환되지 않는다. 내장 포맷이나 journald를 켰다는 이유만으로 이 규약과 지표가 구현되었다고 판정하지 않는다.

## 공통 키

모든 행에 아래 키를 포함한다. 적용할 수 없는 상관 ID는 JSON `null`로 남기며 빈 문자열·`0`·새 임의 ID를 만들지 않는다. ID는 문자열이고 서버/API가 확정한 내부 ID만 쓴다.

| 키 | 형식·의미 |
|---|---|
| `ts` | UTC RFC 3339 문자열, 예: `2026-10-01T00:00:00.000Z`. 이벤트 발생 시각 |
| `level` | `trace`, `debug`, `info`, `warn`, `error`, `critical` 중 하나 |
| `service` | `api`, `town`, `dungeon` 중 하나 |
| `instanceId` | 프로세스/서비스 복제본 ID. 게임 서버는 실행 설정 `INSTANCE_ID`와 동일; API는 별도 로거/수집기 설정으로 부여 |
| `sessionId` | 해당 출정/게임 세션 ID, 없으면 `null` |
| `participantId` | 해당 세션 참가자 ID, 없으면 `null` |
| `operationId` | 멱등 원자 작업 ID. 재시도/결과 조회도 최초 ID를 유지; 없으면 `null` |
| `event` | 고정된 이벤트 이름. 예: `server.started`, `api.request.completed`, `storage.retry`, `server.drain.completed` |
| `buildId` | 해당 바이너리의 유일한 빌드 ID. 로거 연결 전에는 `null`; 표시용 bundleVersion으로 대체하지 않음 |
| `contentVersion` | 배포/GameDB 콘텐츠 버전. 게임 서버 실행 설정과 일치; 적용하지 않는 이벤트는 `null` |
| `eventId` | 저장 이벤트의 고정 ID 등 도메인에서 확정한 ID; `event` 이름과 다름. 해당하지 않으면 `null` |
| `reasonCode` | 확정된 거절/운영 사유 코드; 없으면 `null` |

`buildId`부터 `reasonCode`까지는 기술 설계서 10장의 추적 키를 보존한다. 추가 필드는 구조화된 `data` 객체에 넣는다. ASP.NET Core의 숫자 `EventId`는 로깅 분류 번호이므로 도메인 `eventId`로 바로 바꾸지 않는다. 내장 `Timestamp`→`ts`, `LogLevel`→위 `level` 변환과 `service`/`instanceId` 보강은 수집기에서 명시적으로 구현할 수 있다. `Message` 문자열에서 경제 ID를 추측하여 뽑지 않는다.

```json
{"ts":"2026-10-01T00:00:00.000Z","level":"info","service":"dungeon","instanceId":"dungeon-01","sessionId":null,"participantId":null,"operationId":null,"event":"server.started","buildId":"REPLACE_WITH_BUILD_ID","contentVersion":"REPLACE_WITH_CONTENT_VERSION","eventId":null,"reasonCode":null,"data":{"gamePort":7780}}
```

이벤트 이름은 고정하고 사용자 입력·예외 메시지·URL·토큰을 이름에 넣지 않는다. 토큰, 비밀번호, DB 연결 문자열, 서비스 자격, Authorization 헤더, 초대 코드, 요청/응답 원문과 개인 식별 정보는 기록하지 않는다. API URL은 경로 템플릿만 기록하며 query string을 제외한다. 실패는 검증된 `reasonCode`와 예외 타입처럼 비밀값이 없는 정보로 설명한다.

## 지표 이름과 단위

수집 시 `data.metrics`의 숫자 필드로 아래 이름을 사용한다. 게임 서버는 `METRICS_PORT`를 주면 같은 이름을 Prometheus `/metrics`로도 내보낸다(구성은 [monitoring/README.md](monitoring/README.md)). 여기서 `server_tick_duration_ms`는 누적 히스토그램이고, 추가로 `server_info`, `server_tick_budget_ms`, `server_spawned_objects`, `process_start_time_seconds`, `network_send_packets_total`·`network_receive_packets_total`, 세부 집계를 켰을 때 `network_message_{send,receive}_{bytes,count}_total{message}`를 쓴다. 바이트는 KCP·UDP 헤더를 뺀 트랜스포트 페이로드다. `server_active_enemies`, `storage_failure_total`, 불일치 counter는 게임 서버가 아직 내보내지 않는다. API는 `GET /metrics`(`Metrics:AllowedNetworks`로 접근 제한)에서 위 `api_*`·`settlement_outbox_pending` 별칭과 함께 `shinshi_api_*` 지표를 내보낸다. 이름·의미는 Backend README의 "API Prometheus 지표"가 기준이며, 경보 규칙과 `shinshi-api.json` 대시보드는 그 이름을 따른다([monitoring/README.md](monitoring/README.md)). 시간은 단조 시계로 측정하고 바이트는 실측값을 쓴다.

| 이름 | 종류·단위 | 의미 |
|---|---|---|
| `server_tick_duration_ms` | histogram, ms | 서버 판정 틱 한 회의 실제 수행 시간. 대기/sleep 제외 |
| `server_tick_overrun_total` | counter, 회 | 수행 시간이 설정된 틱 예산을 넘은 횟수 |
| `server_frame_interval_ms` | histogram, ms | 서버 Update 호출 사이의 실제 간격. 틱 지표가 못 보는 렌더, 물리, GC 정지를 포함 |
| `server_frame_overrun_total` | counter, 회 | 프레임 간격이 틱 예산의 1.5배를 넘은 횟수 |
| `server_frame_interval_max_ms` | gauge, ms | 직전과 현재 60초 창 중 큰 프레임 간격 |
| `server_frame_stall_max_1s_ms` | gauge, ms | 마지막으로 끝난 1초 창의 최대 프레임 간격; 첫 창 전에는 없음 |
| `kcp_connections` | gauge, 개 | 마지막 큐 표본의 KCP 연결 수 |
| `kcp_send_queue_total` / `kcp_send_buffer_total` | gauge, 개 | 전체 연결의 KCP 송신 큐 / 송신 버퍼 길이 합 |
| `kcp_send_queue_max` / `kcp_send_buffer_max` | gauge, 개 | 연결 하나의 최대 송신 큐 / 송신 버퍼 길이 |
| `kcp_connection_backlog_max` | gauge, 개 | 연결 하나의 송신 큐와 송신 버퍼 합 중 최댓값 |
| `kcp_send_queue_warn_threshold` | gauge, 개 | 연결별 적체 경고 임계값(기본 2000) |
| `kcp_send_queue_warn_total` | counter, 회 | 연결 적체가 경고 임계값을 넘은 횟수 |
| `backend_gateway_in_flight` | gauge, 개 | 보냈으나 완료되지 않은 백엔드 요청 수 |
| `backend_gateway_completed_queue` | gauge, 개 | 메인 스레드 전달을 기다리는 백엔드 결과 수 |
| `log_queue_depth` | gauge, 줄 | 쓰기 스레드를 기다리는 로그 줄 수 |
| `log_dropped_total` | counter, 줄 | 큐가 가득 차 버려진 로그 줄 누적 수 |
| `process_resident_memory_bytes` | gauge, byte | 프로세스 RSS. GC heap이나 호스트 총 RAM으로 대체하지 않음 |
| `process_managed_heap_bytes` | gauge, byte | 관리 힙 사용량. RSS와 별도 집계 |
| `api_request_duration_ms` | histogram, ms | API 요청 한 회의 실제 응답 시간, 성공/거절/실패 포함 |
| `api_request_duration_p50_ms` | gauge, ms | 직전 60초 완료 요청의 p50 |
| `api_request_duration_p95_ms` | gauge, ms | 직전 60초 완료 요청의 p95 |
| `api_request_duration_p99_ms` | gauge, ms | 직전 60초 완료 요청의 p99 |
| `api_request_sample_count` | gauge, 건 | 위 60초 분포의 표본 수. 0이면 분위수는 `null` |
| `storage_queue_depth` | gauge, 건 | 아직 API 확정 응답을 받지 못한 저장 작업 수, 재시도 대기 포함 |
| `storage_queue_oldest_age_seconds` | gauge, 초 | 가장 오래된 미확정 저장 작업의 나이; 큐가 비면 0 |
| `storage_retry_total` | counter, 회 | 동일 `operationId`로 실제 재시도한 횟수 |
| `storage_failure_total` | counter, 회 | 저장 시도가 실패한 횟수; 업무 거절과 구분 |
| `settlement_outbox_pending` | gauge, 건 | 아직 전달/확정되지 않은 정산 outbox 행 수 |
| `server_active_connections` | gauge, 개 | 인증 완료 활성 연결 수 |
| `server_active_enemies` | gauge, 개 | 활성 적 수 |
| `network_receive_bytes_total` / `network_send_bytes_total` | counter, byte | 프로세스 누적 수신/송신량 |
| `reservation_mismatch_total` / `settlement_mismatch_total` | counter, 건 | 실제 대사에서 확인한 예약/정산 불일치 |

분위수는 histogram 원표본/버킷에서 산출한다. 인스턴스별 p95를 평균하여 전체 p95라고 보고하지 않는다. 요청 범위는 낮은 카디널리티의 경로 템플릿·HTTP 메서드·상태 분류로만 나눈다. `sessionId`, `participantId`, `operationId`, `eventId`는 로그 상관 키이며 시계열 label로 사용하지 않는다. 프로세스 재시작 시 counter 초기화를 허용하고 수집기는 reset을 처리한다.

30Hz이면 초기 틱 예산은 약 33.33ms지만 실제 `Constants.Network.SERVER_TICK_RATE`를 기준으로 계산한다. 샘플 빈도·알림 임계값·보존 기간은 Linux 부하/장애 시험 후 정한다. 메모리·CPU 실측 없이 풀 크기나 호스팅 사양을 확정하지 않는다.

## 보존과 정산 추적 (OP-6b)

체험판은 전용 로그 검색 스택 없이 호스트의 journald에 모은다. 게임 서버 유닛은 이미 `StandardOutput=journal`, `StandardError=journal`이고 `SyslogIdentifier`가 `shinshi-town`, `shinshi-dungeon-<슬롯>`이다. API 컨테이너는 Docker `journald` 로그 드라이버를 쓰면 같은 저장소에 같은 보존 설정으로 들어가고(`tag=shinshi-api`, 컨테이너를 다시 만들어도 남음), journald를 쓸 수 없는 환경은 `json-file` 회전으로 대신한다. 설정 파일은 아래와 같다.

| 대상 | 파일 | 내용 |
|---|---|---|
| 호스트 journald | [gameserver/journald-shinshi.conf](gameserver/journald-shinshi.conf) | `Storage=persistent`(재부팅 뒤에도 보존), 90일·최대 4G·여유 2G 유지, 전송 속도 상한 완화 |
| API 컨테이너 | [api/docker-compose.example.yml](api/docker-compose.example.yml) | `journald` 드라이버(권장) 또는 `json-file` 50MB x 10개 회전 |

보존 원칙은 기술 로그 30일, 행동 로그 90일이다([운영 설계 4장](../docs/design/operations-admin.md)). journald는 로그 종류별 보존을 나누지 못하므로 둘을 같은 줄에서 가려낼 수 없는 지금은 상한을 90일로 두고, 실제로 먼저 걸리는 한계는 디스크 상한이다. 따라서 기술 로그도 최대 90일 남을 수 있고, 상한에 닿으면 가장 오래된 파일부터 지워져 보존 기간이 줄어든다. `journalctl --disk-usage`를 가끔 확인하고, 30일/90일 분리는 로그 저장소(OP-12) 단계에서 한다. 경제 원장·감사·세션 이벤트는 DB가 보존하며(운영 설계 4장) 이 로그 보존과 무관하다.

### 정산 하나를 추적하는 조회

정산·세션 하나는 `sessionId`(출정 단위)나 `operationId`(멱등 작업 단위)로 마을/던전 서버와 API 로그를 함께 훑는다. 공통 키는 위 표대로 JSON에 있으므로 `jq`로 고른다. 두 값 중 하나라도 맞는 줄을 시각 순서로 보여 준다. Unity가 JSON 설치 전에 찍는 평문 줄은 `fromjson?`가 건너뛴다. 아래는 Linux 호스트용 예시이며 이 작업에서 journald로는 실행하지 못했다. 같은 `jq` 필터를 샘플 JSON 줄에 적용한 것만 확인했다.

```bash
ID=00000000-0000-0000-0000-000000000000   # sessionId 또는 operationId
SINCE="2 days ago"

{
  sudo journalctl -u 'shinshi-town.service' -u 'shinshi-dungeon@*.service' --since "$SINCE" --no-pager -o cat
  sudo journalctl CONTAINER_NAME=shinshi-api --since "$SINCE" --no-pager -o cat
} | grep -F "$ID"   | jq -R -c --arg id "$ID" 'fromjson? | select(.sessionId == $id or .operationId == $id)'   | jq -s -r 'sort_by(.ts)[] | [.ts, .service, .instanceId, .level, .event, (.operationId // "-"), (.reasonCode // "-")] | @tsv'
```

- 먼저 `sessionId`로 조회해 줄에 나온 `operationId`를 다시 `ID`에 넣어 그 작업만 좁힌다. API 요청 줄(`api.request.completed`)은 경로에 세션 GUID가 있을 때만 `sessionId`가 채워지고, 나머지 상관 키는 해당 이벤트를 남기는 코드가 채울 때만 있다.
- API를 `json-file` 드라이버로 돌리면 둘째 명령을 `docker logs --since 48h shinshi-api 2>&1`로 바꾼다.
- 한 줄 전체를 보려면 마지막 `jq`를 `jq -s 'sort_by(.ts)[]'`로 바꾼다. 로그에는 토큰·연결 문자열·요청 본문이 없다(위 규약).
- DB에 남은 기록은 어드민 API로 본다. 같은 `operationId`는 `GET /admin/v1/operations/{actorId}/{operationId}`, 세션 이벤트는 `GET /admin/v1/sessions/{id}/events`(`reason` 필수, 조회도 감사됨)다.

## 드레인과 장애 이벤트

권장 이벤트는 `server.drain.requested`, `server.drain.started`, `server.drain.completed`, `server.shutdown.forced`, `storage.retry`, `storage.failed`, `reservation.mismatch`, `settlement.mismatch`다. 실제 상태 전이를 확인한 뒤에만 기록한다. `server.drain.completed`에는 활성 세션·저장 큐·정산 outbox가 모두 마감되었다는 근거가 필요하다. SIGTERM 전송, heartbeat 부재 또는 제한 시간 경과만으로 정상 마감이라고 기록하지 않는다.

공통 키의 기준은 [기술 설계서 10장](../docs/03-technical-design.md#10-보안배포관측), 게임 서버 실행 계약은 [ServerLaunchConfig.cs](../Assets/01.Scripts/98.Servers/01.Network/Bootstrap/ServerLaunchConfig.cs)와 [Constants.network.cs](../Assets/01.Scripts/00.Common/Constants/Constants.network.cs)다.
