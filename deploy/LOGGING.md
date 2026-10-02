# 배포 로그와 지표 규약

적용 서비스는 `api`, `town`, `dungeon`이다. UTF-8 JSON 객체 하나를 한 줄로 표준 출력에 기록하고 오류도 같은 형식을 사용한다. systemd의 journald 또는 컨테이너 로그 수집기는 이 줄을 보존한다. 수집 시각으로 발생 시각을 덮어쓰지 않는다.

이 문서는 구현할 공통 규약이다. Dockerfile은 ASP.NET Core 내장 JSON 콘솔 포맷을 켜지만 그 출력은 `Timestamp`, `LogLevel`, `Message`, `EventId`, `Scopes` 등의 원래 키를 사용한다. 공통 키를 채우는 API 로거/수집기 연결은 후속 작업이다. 현재 Unity의 `Debug.Log` 문자열과 API의 `--migrate` 실행기가 직접 stdout/stderr에 쓰는 문장도 공통 JSON으로 자동 변환되지 않는다. 내장 포맷이나 journald를 켰다는 이유만으로 이 규약과 지표가 구현되었다고 판정하지 않는다.

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

수집 시 `data.metrics`의 숫자 필드로 아래 이름을 사용한다. 지표 수집/내보내기는 이번 배포 파일에 포함되지 않는다. 시간은 단조 시계로 측정하고 바이트는 실측값을 쓴다.

| 이름 | 종류·단위 | 의미 |
|---|---|---|
| `server_tick_duration_ms` | histogram, ms | 서버 판정 틱 한 회의 실제 수행 시간. 대기/sleep 제외 |
| `server_tick_overrun_total` | counter, 회 | 수행 시간이 설정된 틱 예산을 넘은 횟수 |
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

## 드레인과 장애 이벤트

권장 이벤트는 `server.drain.requested`, `server.drain.started`, `server.drain.completed`, `server.shutdown.forced`, `storage.retry`, `storage.failed`, `reservation.mismatch`, `settlement.mismatch`다. 실제 상태 전이를 확인한 뒤에만 기록한다. `server.drain.completed`에는 활성 세션·저장 큐·정산 outbox가 모두 마감되었다는 근거가 필요하다. SIGTERM 전송, heartbeat 부재 또는 제한 시간 경과만으로 정상 마감이라고 기록하지 않는다.

공통 키의 기준은 [기술 설계서 10장](../docs/03-technical-design.md#10-보안배포관측), 게임 서버 실행 계약은 [ServerLaunchConfig.cs](../Assets/01.Scripts/98.Servers/01.Network/ServerLaunchConfig.cs)와 [Constants.network.cs](../Assets/01.Scripts/00.Common/Constants/Constants.network.cs)다.
