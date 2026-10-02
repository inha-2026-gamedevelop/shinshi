# shinshi-admin (Shinshi.AdminCli)

체험판 운영 도구는 어드민 API와 이 명령줄 도구만 제공한다. 이 도구는 `/admin/v1` API만 호출하고 DB에 직접 붙지 않는다. 경로, DTO, 코드는 `Shinshi.Contracts.Admin`을 그대로 쓴다.

## 실행

저장소 루트에서:

```powershell
dotnet run --project Backend/src/Shinshi.AdminCli -- --help
dotnet run --project Backend/src/Shinshi.AdminCli -- grants --help
```

빌드 결과물 이름은 `shinshi-admin`(`shinshi-admin.exe`)이다. 아래 예시는 `shinshi-admin`으로 줄여 쓴다.

### 전역 옵션

| 옵션 | 뜻 |
|---|---|
| `--api <주소>` | API 주소. 없으면 환경 변수 `SHINSHI_ADMIN_API`, 그것도 없으면 `http://127.0.0.1:5080`. 경로 접두사가 있는 주소도 받는다 |
| `--json` | 응답 JSON 원본을 표준 출력에 쓴다. 안내, 경고, 확인 질문은 표준 오류로 간다 |
| `--operation-id <guid>` | 쓰기 재시도용. 아래 "쓰기와 재시도" |
| `--yes` | 쓰기 확인 질문을 건너뛴다(스크립트용) |
| `--help`, `-h` | 도움말 |

## 로그인과 세션 파일

```powershell
shinshi-admin login --user ops.kim
```

- 비밀번호와 TOTP 코드는 인자로 받지 않는다(셸 기록과 프로세스 목록에 남는다). 콘솔에서 에코 없이 묻는다. 입력이 리디렉션돼 있으면 한 줄씩 읽는다.
- 비대화 환경(배포 스크립트 등)은 환경 변수 `SHINSHI_ADMIN_PASSWORD`, `SHINSHI_ADMIN_TOTP`를 쓸 수 있다. TOTP는 30초마다 바뀌고 같은 코드는 한 번만 받으므로 실행 직전에 넣는다.
- 받은 어드민 토큰은 아래 파일에만 저장한다. 토큰은 화면, 로그, 예외 메시지, `--json` 출력에 싣지 않는다(`login --json`도 토큰을 뺀 값만 쓴다).

| OS | 위치 | 권한 |
|---|---|---|
| Windows | `%LOCALAPPDATA%\shinshi-admin\session.json` | 폴더와 파일 모두 상속을 끊고 현재 사용자 한 명에게만 FullControl ACL |
| 그 밖 | `$XDG_STATE_HOME/shinshi-admin/session.json`, 없으면 `~/.local/state/shinshi-admin/session.json` | 폴더 0700, 파일 0600 |

- 파일은 같은 폴더의 임시 파일에 권한을 먼저 맞춰 쓴 뒤 바꿔 끼운다. 반쯤 쓴 파일이나 권한이 넓은 순간이 남지 않는다.
- 세션 파일에는 토큰을 받은 API 주소와 만료 시각이 함께 있다. `--api`가 다르면 토큰을 보내지 않고 다시 로그인하라고 안내한다(다른 호스트로 토큰이 새지 않게). 만료 시각이 지났거나 서버가 401 `TOKEN_REVOKED`를 주면 파일을 지우고 종료 코드 3이다. 어드민 토큰 수명은 기본 30분이다.
- `logout`은 서버 세션을 폐기(`POST /admin/v1/auth/logout`)하고 파일을 지운다. 서버에 닿지 못하거나 서버가 401이 아닌 오류(500, 503 등)를 주면 파일은 지우되 서버 토큰 폐기는 확인하지 못했고 토큰이 만료 시각까지 서버에서 유효할 수 있다고 알린다. 종료 코드는 오류 종류를 따른다(연결 실패, 503, 429는 3, 500 같은 그 밖 HTTP 오류는 2).
- 평문 `http://`로 루프백이 아닌 주소에 로그인하면 경고한다. 원격 API는 HTTPS 뒤에 둔다.

## 비밀값 취급

- 초대 코드 원문(`invites issue`)과 운영자 초기 비밀번호, TOTP 비밀(`operators create`)은 서버가 그 생성을 확정한 응답에서 한 번만 준다. 도구는 화면에 한 번 출력하고 저장하지 않으며 "다시 볼 수 없다"고 안내한다. 같은 operationId 재전송 응답에는 원문이 없다. 잃었으면 초대는 폐기 후 재발급, 운영자는 비활성화 후 다시 만든다.
- `--json` 출력에는 위 1회 비밀값이 원본 그대로 들어간다. 파이프나 파일로 넘길 때 주의한다.
- `accounts search`는 초대 코드 원문을 조회 문자열이 아니라 POST 본문으로 보낸다(요청 로그에 남지 않게).
- `--reason`은 감사 로그에 그대로 남는다. 비밀값을 적지 않는다.

## 쓰기와 재시도

모든 쓰기는 실행 전에 요약을 보여 주고 `계속할까요? (y/N)`로 확인을 받는다. `y`/`yes`가 아니면 아무것도 보내지 않고 종료 코드 1이다. `--yes`로 생략한다. 실행, 취소처럼 대상이 있는 쓰기(`grants execute/cancel`)는 현재 지급 내용을 먼저 읽어 요약에 보여 준다.

쓰기는 모두 `operationId`(Guid)와 `reason`(공백 아닌 500자 이하)을 보낸다. `--operation-id`가 없으면 새 ID를 만들어 출력한다.

```text
operationId: 6f1c...  응답을 받지 못하면 같은 명령에 --operation-id 6f1c... 를 붙여 다시 실행하세요.
```

- 응답을 잃었을 때(연결 끊김, 시간 초과, 503, 429, 202) 같은 명령에 그 ID를 붙여 다시 실행하면 서버가 처음 확정한 결과를 돌려준다. 새 ID로 다시 보내면 같은 작업이 두 번 실행될 수 있다.
- 같은 ID로 다른 내용을 보내면 `IDEMPOTENCY_CONFLICT`다. 새 작업에는 `--operation-id`를 쓰지 않는다.
- `service-state set`은 현재 값을 먼저 GET해 지정하지 않은 항목을 그대로 보내고 `revision`을 `expectedRevision`으로 보낸다. 보내기 전에 실제로 보낼 네 값(`--maintenance`, `--disable`, `--allow-builds`, `--expected-revision`)과 `--operation-id`, `--reason`을 모두 담은 재시도 명령을 출력한다. 네 값을 모두 준 명령은 GET 없이 그 값 그대로 보내므로, 응답을 잃었으면 같은 명령에 `--operation-id`만 붙이지 말고 출력된 재시도 명령을 그대로 실행한다. 생략한 항목을 다시 GET으로 채우면 그 사이 다른 운영자가 바꾼 값이 섞여 본문이 달라지고 `IDEMPOTENCY_CONFLICT`가 된다. 사유도 처음과 똑같아야 한다. 출력되는 명령은 PowerShell 7.3 이상에서 `$PSNativeCommandArgumentPassing`이 기본 `Windows` 또는 `Standard`일 때 사용한다. Windows PowerShell 5.1이나 `Legacy` 모드에서는 큰따옴표가 유실될 수 있다. 값은 작은따옴표 리터럴로 쓰고 내부 ASCII·스마트 작은따옴표를 두 번 쓰므로 변수·명령 구분자도 원문 그대로 전달된다. 쉼표가 든 목록 값, 단독 `none`, 앞뒤 공백·빈 값·중복 목록, 제어문자가 든 목록이나 사유는 CLI 명령으로 같은 본문을 복원할 수 없어 재시도 명령 대신 경고를 출력한다. 이때는 `service-state show`로 결과를 확인한다.

## 출력과 종료 코드

기본 출력은 사람이 읽는 한국어 표와 요약이다. 시각은 모두 UTC(`2026-10-02 09:00:00Z`)로 보여 준다. 거절은 계약 상수 이름(`ContractCodes.RejectReason`, `AdminContractCodes.RejectReason`을 반사로 역조회)과 HTTP 상태를 함께 보여 준다.

```text
거절: INVALID_STATE (9005), HTTP 409
operationId: 2b0e...
```

| 종료 코드 | 뜻 |
|---|---|
| 0 | 성공 |
| 1 | 사용법 오류(서버에 보내기 전), 쓰기 확인 거절 |
| 2 | API 거절(400, 403, 404, 409 등). 같은 요청을 다시 보내도 결과가 같다 |
| 3 | 연결 실패, 시간 초과, 로그인 없음, 세션 만료나 폐기(401), 일시 장애 503, 요청 한도 429, 처리 대기 202. 다시 로그인하거나 같은 operationId로 재시도 |

허용 네트워크 밖이면 API가 본문 없는 404로 존재를 숨긴다. 도구는 이때 `--api` 주소와 접속 위치를 확인하라고 안내한다.

## 명령과 HTTP 경로

| 명령 | HTTP | 최소 역할 |
|---|---|---|
| `login --user <이름>` | `POST /admin/v1/auth/login` | 없음 |
| `logout` | `POST /admin/v1/auth/logout` | Viewer |
| `operators list` | `GET /admin/v1/operators` | Owner |
| `operators create --user --role --reason` | `POST /admin/v1/operators` | Owner |
| `operators disable <adminId> --reason` | `POST /admin/v1/operators/{adminId}/disable` | Owner |
| `accounts search <질의>` | `POST /admin/v1/accounts/search` | Viewer |
| `accounts show <accountId> --reason` | `GET /admin/v1/accounts/{accountId}?reason=` | Viewer(감사 기록) |
| `accounts kick <accountId> [--keep-invites] --reason` | `POST /admin/v1/accounts/{accountId}/kick` (응답 `AdminAccountKickResponse`) | Operator |
| `invites issue [--account] --max-uses --hours [--keep-sessions] --reason` | `POST /admin/v1/invites` | Operator |
| `invites revoke <inviteId> --reason` | `POST /admin/v1/invites/{inviteId}/revoke` | Operator |
| `grants create --accounts --reward... [--claim-days] [--source-kind] [--source-ref] --reason` | `POST /admin/v1/grants` | Operator |
| `grants show <grantId>` | `GET /admin/v1/grants/{grantId}` | Operator |
| `grants execute <grantId> --reason` | `GET` 후 `POST /admin/v1/grants/{grantId}/execute` | Operator(대상 2명 이상이거나 RecoveryCompensation이면 Admin) |
| `grants cancel <grantId> --reason` | `GET` 후 `POST /admin/v1/grants/{grantId}/cancel` | Operator |
| `adjust <accountId> --asset --delta [--character] --reason` | `POST /admin/v1/accounts/{accountId}/adjustments` | Operator |
| `service-state show` | `GET /admin/v1/service-state` | Viewer |
| `service-state set [...] --reason` | `GET` 후 `PUT /admin/v1/service-state`(네 값을 모두 주면 `GET` 없음) | Admin |
| `notices list` | `GET /admin/v1/notices` | Viewer |
| `notices create ... --reason` | `POST /admin/v1/notices` | Operator |
| `notices end <noticeId> --reason` | `POST /admin/v1/notices/{noticeId}/end` | Operator |
| `ledger ... --reason [--all]` | `GET /admin/v1/ledger?reason=...` (원장 사유 코드 필터는 `reasonCode`) | Viewer(감사 기록) |
| `item-history <itemId> --reason` | `GET /admin/v1/items/{itemId}/history?reason=` | Viewer(감사 기록) |
| `session-events <sessionId> --reason` | `GET /admin/v1/sessions/{sessionId}/events?reason=` | Viewer(감사 기록) |
| `operation <actorId> <operationId> --reason` | `GET /admin/v1/operations/{actorId}/{operationId}?reason=` | Viewer(감사 기록) |
| `audit [...] [--all]` | `GET /admin/v1/audit` | Viewer |
| `sessions [--state] [--all]` | `GET /admin/v1/sessions` | Viewer |
| `recover <sessionId> --action retry-settlement\|service-cancel\|force-close --reason` | `POST /admin/v1/sessions/{sessionId}/recovery` | Admin |

- `accounts kick`은 계정의 기존 토큰(로그인 묶음, 접근, 갱신, 게임 하위 세션, 미소비 입장 토큰)을 폐기하고 현재 접속은 유지한다. 기본으로 미사용 초대도 폐기하며 `--keep-invites`면 남긴다. 비밀번호 로그인은 막지 않으므로 같은 아이디와 비밀번호로 새 토큰을 받을 수 있다(로그인 차단은 제재, P1). 출력에 초대 폐기 여부와 현재 접속 유지 여부를 보여 준다.
- `grants create`에서 같은 `--source-ref`의 RecoveryCompensation 지급을 다시 만들면 409 `INVALID_STATE`다.
- `sessions` 응답의 `livenessDeferred`가 true면(API 시작 직후나 DB 단절 직후) 서버 소실 판정을 보류 중이라고 경고한다. 이때 `recover --action service-cancel|force-close`는 503 `BACKEND_UNAVAILABLE`(Retry-After)로 거절되고 종료 코드 3이다. 잠시 뒤 같은 `--operation-id`로 다시 실행한다.
- 플레이어 우편 경로의 요청 한도 초과는 429 `RATE_LIMITED`이며 이 도구에서는 종료 코드 3으로 다룬다.

`--all`은 `nextCursor`를 따라 끝까지 읽는다. `--json`과 함께 쓰면 첫 페이지 객체에 뒤 페이지의 배열을 이어 붙이고 `nextCursor`를 null로 둔 JSON 하나를 쓴다. 원장 `totals`는 같은 조건 전체 합계이며 cursor 없이 부른 첫 페이지에만 있고 다음 페이지에서는 null이다. `--all`은 첫 페이지 합계를 쓰고, `--cursor`로 시작한 조회는 합계를 보여 주지 않는다.

## 입력 문법

- 보상: `--reward kind[:assetId[:binding]]=수량`을 반복한다. kind는 `Currency`, `Tickets`, `Essence`, `Shards`, `AccountItem`(대소문자 무시). `AccountItem`만 assetId가 필요하고 binding을 생략하면 `Account`다. 수량은 1~1000000. 같은 자산을 두 줄로 쓰면 거절한다.
  - 예: `--reward Currency=500 --reward AccountItem:item_herb=3`
- 자산(조정): `--asset kind[:assetId[:binding]]`, `--delta`는 0이 아니고 절댓값이 1000000 이하인 정수. `Essence`, `Shards`는 `--character`가 필요하다. 차감은 가용량까지만 하고 모자란 양은 부족분으로 보여 준다.
- 지급 대상: `--accounts id,id,...` 또는 `--accounts @파일`. 파일은 쉼표, 공백, 줄바꿈으로 나누고 `#`으로 시작하는 줄은 주석이다. 중복이나 GUID가 아닌 값, 1000개 초과는 보내기 전에 거절한다.
- 시각: `2026-10-02T09:00:00Z`나 `2026-10-02T18:00:00+09:00`처럼 Z나 오프셋이 붙은 ISO 8601만 받는다. 오프셋 없는 시각은 지역 시각과 헷갈리므로 거절한다.
- 서비스 상태 목록: `--disable auction,crafting`, `--allow-builds b1,b2`는 목록을 통째로 바꾼다. 비우려면 `none`. 허용 빌드를 비우면 어떤 빌드도 로그인할 수 없다는 경고를 요약에 넣는다.
- 원장 사유 필터: `--economy-reason 26` 또는 `--economy-reason ADMIN_GRANT`(`ContractCodes.EconomyReason` 이름). 조회 문자열 `reasonCode`로 보낸다. `--reason`은 감사 사유이며 조회 문자열 `reason`으로 보낸다.

## 예시

```powershell
$env:SHINSHI_ADMIN_API = 'https://admin.example.internal'
shinshi-admin login --user ops.kim

# 장애 보상: 대상 파일로 지급 초안 작성 -> 확인 -> 실행
shinshi-admin grants create --accounts @targets.txt --reward Currency=500 --reward AccountItem:item_herb=3 `
    --source-kind RecoveryCompensation --source-ref 0b7f...-session --reason "10/02 던전 서버 장애 보상"
shinshi-admin grants show <grantId>
shinshi-admin grants execute <grantId> --reason "보상 실행"

# 개인 조정과 원장 확인
shinshi-admin adjust <accountId> --asset Currency --delta -200 --reason "중복 지급 회수"
shinshi-admin ledger --account <accountId> --from 2026-10-01T00:00:00Z --all --reason "회수 확인"

# 점검 켜기(다른 항목은 현재 값 유지)
shinshi-admin service-state set --maintenance on --reason "정기 점검"

# 스크립트: 확인 생략, JSON 출력
shinshi-admin sessions --state RecoveryPending --all --json

shinshi-admin logout
```

## 테스트

`Backend/tests/Shinshi.Backend.Tests/AdminCli/`에 있다. 인자 파서, 보상과 대상 문법, 거절 코드 이름 역조회, 세션 파일 권한, 기록용 HTTP 핸들러로 본 명령 분기와 종료 코드는 DB 없이 돈다. `AdminCliHttpTests`는 `SHINSHI_TEST_CONNECTION_STRING`이 있을 때 실제 Program 파이프라인(`AdminApiTestHost`)의 TestServer 핸들러를 `CliContext`에 넣어 로그인부터 지급 실행, 조정, 원장 조회, 서비스 상태 저장, 로그아웃까지 왕복한다.

```powershell
dotnet test Backend --no-restore --filter "FullyQualifiedName~Shinshi.Backend.Tests.AdminCli"
```
