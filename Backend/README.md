# shinshi 백엔드 기반

.NET 10 SDK, Docker Compose가 필요하다. API는 ASP.NET Core, DB는 PostgreSQL 18, 접근은 Npgsql + Dapper다. Unity DTO 원본은 `src/Shinshi.Contracts/Shinshi.Contracts.csproj`의 `Compile Include/Link`로 공유한다.

저장소 루트에서 실행한다.

```powershell
dotnet build Backend --no-restore
dotnet test Backend --no-restore
```

DB 없이도 단위/계약/HTTP 테스트가 실행된다. PostgreSQL 통합 테스트는 `SHINSHI_TEST_CONNECTION_STRING`이 없으면 이유를 표시하고 건너뛴다. 설정하면 전용 임시 스키마를 만들고 정리하므로 테스트 DB의 스키마 생성 권한이 필요하다. 연결 실패는 테스트 실패로 처리한다.

로컬 DB 비밀번호는 환경으로만 제공한다. `.env.example`은 자리표시자이며 실제 연결 문자열이나 비밀번호를 파일에 저장하지 않는다. .NET 설정은 `ConnectionStrings__Postgres`(API 실행)와 `ConnectionStrings__PostgresMigrator`(마이그레이션 실행)를 환경으로 주입한다. Compose의 `.env` 파일을 API 설정으로 자동 읽지 않는다.

```powershell
$taskPassword = Read-Host 'Local PostgreSQL bootstrap password' -AsSecureString
$env:POSTGRES_PASSWORD = [System.Net.NetworkCredential]::new('', $taskPassword).Password
docker compose -f Backend/docker-compose.yml up -d --wait
$env:ConnectionStrings__PostgresMigrator = '<local migration-owner connection string from environment or secret>'
dotnet run --project Backend/src/Shinshi.Api -- --migrate
Remove-Item Env:ConnectionStrings__PostgresMigrator
$env:ConnectionStrings__Postgres = '<local DML-only runtime connection string from environment or secret>'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5080'
dotnet run --project Backend/src/Shinshi.Api
```

`--migrate`는 `PostgresMigrator`만 사용한다. 값이 없거나 비어 있으면 `Set ConnectionStrings__PostgresMigrator in the environment.` 오류와 종료 코드 1을 반환하며 `Postgres`로 대체하지 않는다. API 실행은 `Postgres`만 사용하고 마이그레이션 연결을 대체 사용하지 않는다. 런타임 서비스에는 마이그레이션 실행기를 등록하지 않는다. DB를 조회하지 않는 공개 서비스 상태는 연결 문자열 없이도 실행된다.

다른 터미널에서 `Invoke-RestMethod http://127.0.0.1:5080/v1/service-state`로 확인한다. 마이그레이션은 내장된 번호순 SQL을 이력, 체크섬과 같은 트랜잭션에 적용한다. 재실행은 건너뛰고 적용한 SQL 변경은 거절한다. `.down.sql`은 검토용이며 실행기의 자동 되돌리기 기능은 없다. 실행 중인 API에 자동 적용하지 않는다.

`0002_dungeon_session_constraints`는 UNIQUE와 열 순서가 같은 `ix_characters_account`, `ix_auth_refresh_tokens_account`만 제거한다. 조회와 복합 FK는 기존 UNIQUE 인덱스를 사용한다. 전장 세션의 `role`은 Dungeon 코드 1로 고정하고 `(instance_id, role)` FK로 서버 역할도 일치시킨다. 기존 세션이 Town 서버를 참조하면 이력과 스키마를 포함해 전체 적용이 롤백되므로 잘못된 배정을 먼저 조사한다. down은 추가 제약과 열을 제거하고 두 일반 인덱스 및 단일 FK를 복원한다.

빈 로컬 DB의 권한 분리 예시는 아래와 같다. 이 SQL은 문서 예시이며 API나 Compose가 자동 실행하지 않는다. 로컬 관리자가 먼저 역할과 스키마 권한을 준비하고, 마이그레이션 연결로 테이블을 만든 뒤 런타임 DML 권한을 부여한다. 비밀번호는 역할 생성 SQL이나 저장소에 넣지 않고 환경/시크릿 또는 관리 도구의 비밀번호 입력 기능으로 설정한다. 기존 DB를 옮길 때는 테이블 소유자와 역할 멤버십을 별도로 확인해야 한다.

```sql
-- 빈 로컬 shinshi DB에서 관리자 계정으로, 마이그레이션 적용 전에 검토 후 실행
CREATE ROLE shinshi_migrator LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
CREATE ROLE shinshi_runtime LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
GRANT CONNECT ON DATABASE shinshi TO shinshi_migrator, shinshi_runtime;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE, CREATE ON SCHEMA public TO shinshi_migrator;
GRANT USAGE ON SCHEMA public TO shinshi_runtime;

-- shinshi_migrator 연결로 배포 대상 마이그레이션(인증/원장/예약 포함 0004)을 적용한 뒤 검토 후 실행
REVOKE ALL ON ALL TABLES IN SCHEMA public FROM shinshi_runtime;
GRANT SELECT, INSERT, UPDATE ON TABLE
    public.accounts, public.server_instances, public.account_leases,
    public.auth_invites, public.auth_refresh_tokens, public.auth_game_sessions,
    public.auth_token_families, public.auth_access_tokens, public.auth_server_leases, public.auth_join_tokens,
    public.characters, public.account_wallets, public.account_items,
    public.game_sessions, public.operations, public.session_participants, public.reservations
    TO shinshi_runtime;
GRANT DELETE ON TABLE public.account_leases TO shinshi_runtime;
-- 0006: 유지 작업이 보존 기간이 지난 만료 입장 토큰을 지운다
GRANT DELETE ON TABLE public.auth_join_tokens TO shinshi_runtime;
GRANT SELECT, INSERT ON TABLE public.session_events, public.session_checkpoints TO shinshi_runtime;
-- 원장과 감사/소비 이력은 추가 전용이다. 소유자나 상위 역할의 권한도 확인한다.
REVOKE UPDATE, DELETE, TRUNCATE ON TABLE
    public.economy_ledger, public.item_history, public.outbox_inbox FROM shinshi_runtime;
GRANT SELECT, INSERT ON TABLE
    public.economy_ledger, public.item_history, public.outbox_inbox TO shinshi_runtime;
GRANT SELECT, INSERT ON TABLE public.outbox TO shinshi_runtime;
GRANT UPDATE (delivered_at, retries, next_attempt_at) ON public.outbox TO shinshi_runtime;
-- 0009: 파티 경매. 입찰 기록은 추가 전용이다
GRANT SELECT, INSERT, UPDATE ON TABLE public.auctions TO shinshi_runtime;
GRANT SELECT, INSERT ON TABLE public.auction_bids TO shinshi_runtime;
-- 0010: 아이디, 비밀번호 자격. characters의 새 열(name, name_key, appearance)은 기존 테이블 권한을 따른다
GRANT SELECT, INSERT, UPDATE ON TABLE public.account_credentials TO shinshi_runtime;
-- 0008: 운영자 계정, 어드민 세션, 지급, 우편함, 공지. service_state는 0008이 넣은 단일 행만 갱신한다(INSERT 없음)
GRANT SELECT, INSERT, UPDATE ON TABLE
    public.admin_users, public.admin_sessions, public.admin_grants, public.reward_inbox, public.service_notices
    TO shinshi_runtime;
GRANT SELECT, UPDATE ON TABLE public.service_state TO shinshi_runtime;
-- 운영자 감사 로그와 모든 운영자 업무 변경의 작업 키 결속은 추가 전용이다
REVOKE UPDATE, DELETE, TRUNCATE ON TABLE public.admin_audit_log, public.admin_operation_claims FROM shinshi_runtime;
GRANT SELECT, INSERT ON TABLE public.admin_audit_log, public.admin_operation_claims TO shinshi_runtime;
```

런타임 역할은 테이블/스키마 소유자나 migrator 역할의 멤버로 두지 않는다. `schema_migrations`의 조회/수정 권한과 스키마 CREATE 권한을 런타임에 부여하지 않는다. 추가 테이블의 런타임 권한도 자동 확대하지 않고 작업별로 검토한다. 이 예시는 현재 테이블 기준이며, 후속 추가 전용 원장/감사 테이블에는 SELECT/INSERT만 허용한다. PostgreSQL 임시 테이블은 별도 권한이므로 임시 테이블 생성까지 차단하려는 개발 DB에서는 PUBLIC과 런타임 역할의 TEMPORARY 권한도 검토한다.

초기 상태는 점검 중, 허용 빌드 목록은 비어 있다. 런타임 연결(`ConnectionStrings__Postgres`)이 있으면 점검·기능 차단·허용 빌드·공지를 0008의 `service_state`·`service_notices`에서 읽고 어드민 API로 바꾼다(아래 "서비스 상태와 공지"). 연결 문자열이 없을 때만 외부 환경 설정 `ServiceState__Maintenance`, `ServiceState__Notice`, `ServiceState__AllowedBuildIds__0` 등을 쓴다. 예외로 입장 토큰 교환은 DB 모드에서도 `ServiceState__Maintenance`를 직접 읽는다. PostgreSQL 18 볼륨은 공식 이미지의 경로에 맞춰 `/var/lib/postgresql`에 둔다. [공식 이미지](https://hub.docker.com/_/postgres)

DB 통합 테스트를 실행하려면 API를 종료한 뒤 테스트 전용 로컬 연결을 설정한다. 런타임 역할은 스키마를 만들지 못하므로 테스트 연결로 사용하지 않는다.

```powershell
$env:SHINSHI_TEST_CONNECTION_STRING = '<dedicated local test database connection with schema-create permission>'
dotnet test Backend --no-restore
```

`IdempotentOperationExecutor`는 인증한 actor와 operation UUID, 작업 종류, JSON 본문을 받는다. 본문 객체 키를 정규화하고 작업 종류와 함께 해시한다. 같은 키와 본문은 최초 결과를 반환하고 다른 본문은 `IdempotencyConflict`로 거절한다. 효과 콜백은 제공한 DB 연결/트랜잭션만 사용한다. 외부 I/O나 확률 재추첨을 넣지 않는다. 실패는 전체 롤백, 업무 거절은 효과만 롤백하고 거절 결과를 저장한다. 서버 경로의 예약·세션·정산·경매 작업, 플레이어 우편 수령, 어드민 쓰기가 이 실행기를 거친다. 플레이어용 작업 결과 조회 엔드포인트(`GET /v1/operations/{operationId}`)는 아직 없다.

계정 임대는 하나의 활성 연결만 허용하고 충돌 시 `AlreadyConnected`, "이미 접속 중인 계정"을 반환한다(마을 입장 교환의 획득, 연장, 해제는 0006 절). 인증 헤더는 `Authorization: Bearer <token>`이다. 초대 교환, 토큰 회전, 서버 임대 검증과 경제 원장/outbox는 아래 모듈로 제공한다. 출정 예약, 세션 이벤트, 정산과 인증/원장 어댑터는 아래 모듈로 연결된다. `ALREADY_CONNECTED`는 마을 입장 교환이 반환한다(0006 절).

응답은 공유 `ServiceStateResponse`, `OperationResult`, `ApiRejection`을 Newtonsoft.Json으로 읽고 쓴다. `DataMember` 이름과 private 필드, private 생성자, 숫자 코드, UTC Unix 밀리초를 보존하며 타입 이름 메타데이터는 쓰지 않는다. HTTP 상태 변환은 `ApiResponseFactory`에 있다. SP4는 .NET 왕복 검증이며 Unity Editor/IL2CPP stripping 검증은 후속이다.

## 인증과 경제 원장 모듈 (0003)

`Program.cs`가 인증, 원장, 세션 저장소와 어댑터를 등록한다. 인증 미들웨어는 라우팅 뒤에 실행된다. 공개 엔드포인트는 `[AllowAnonymous]`, 서버 전용 엔드포인트는 `[RequireServerLease]`로 표시한다. 표시 없는 엔드포인트는 플레이어 접근 토큰을 요구하며 서버 임대 토큰으로 플레이어 권한을 얻을 수 없다. 세션 경로는 서버 전용 표시를 상속하고 검증된 인스턴스와 역할로 추가 검사한다.

```csharp
using Shinshi.Data.Ledger;

builder.Services.AddShinshiAuth(builder.Configuration);
builder.Services.AddShinshiLedger();

// builder.Build() 다음, MapControllers() 이전
app.UseRouting();
app.UseShinshiAuthentication();
app.MapControllers();
```

외부 설정은 `Auth__HashKeyBase64`(최소 32 무작위 바이트의 Base64), `AuthBuild__ProtocolVersion`, `AuthBuild__ContentHash`, 기존 `ServiceState__AllowedBuildIds__*`와 `ServiceState__Maintenance`다. 비밀키의 기본값은 없다. 해시 키를 바꾸면 기존 토큰/초대는 검증되지 않으므로 폐기와 새 초대 발급을 배포 절차에 포함한다. 토큰/초대 원문이나 설정 객체를 로그에 직렬화하지 않는다. 공개 서비스 상태는 인증 DB 연결 없이도 응답한다.

`POST /v1/auth/exchange`는 기존 `AuthExchangeRequest/Response`를 사용한다. 초대 또는 갱신 토큰 하나만 보낸다. 버전·contentHash·허용 build·점검을 코드 소비 전에 검사한다. 초대는 계정에 결합된 256비트 무작위 코드이며, HMAC-SHA256으로 용도를 분리한 해시만 저장한다. 원문 후보 공간이 충분히 커서 비밀번호용 느린 해시 대신 HMAC을 사용한다. 초대 만료 24시간, 기본 최대 사용 1회, 접근 15분, 갱신 24시간이다. `used_count < max_uses` 조건부 갱신과 계정 잠금 안에서 소비/토큰 발급이 함께 커밋된다. JSON 파싱·모델 바인딩 실패도 인증 전용 필터에서 기존 `ApiRejection` 숫자 코드로 변환하며 입력 토큰을 오류 상세에 싣지 않는다. 인증 응답에는 `Cache-Control: no-store`를 붙인다.

접근·갱신·게임 하위 세션·서버 임대·참가자 접속 토큰은 서로 다른 HMAC 용도를 가진 opaque bearer다. 갱신은 계정→토큰 계열 순서로 잠근 뒤 이전 토큰을 폐기하고 새 토큰을 발급한다. 소비된 갱신 토큰을 다시 제시하면 계열 전체(접근/갱신/하위 세션/접속 토큰)가 폐기된다. 회전 응답을 잃어도 이전 토큰 재시도를 정상 자동 로그인으로 취급하지 않으며 재로그인이 필요하다. 일반 회전 시 접근 토큰은 기존 15분 만료까지 유지되지만 이전 갱신 토큰의 하위 게임 세션은 폐기된다. 기기 유실 복구는 신뢰된 운영 경로에서 `IssueInviteAsync`의 기본 `revokeExistingSessions: true`로 새 초대를 만들고 기존 계열을 폐기한다.

`ServerCredentialIssuer`는 `POST /v1/server-instances/lease`(아래 0005 절)에서만 쓰며 공개 플레이어 발급 엔드포인트를 제공하지 않는다. `ServerCredentials__Registrations__*__ServiceId/InstanceId/Role/CredentialHashBase64`는 환경에서 고정한다. 서비스 자격은 별도 256비트 무작위 원문이고 설정에는 그 UTF-8 원문의 SHA256 Base64만 둔다. 일치 비교는 고정 시간이며 ID/역할은 입력 본문 대신 등록 설정에서 정한다. `ServerCredentials__LeaseLifetimeSeconds` 기본 900초, 최대 24시간이다. 발급은 역할과 bootId를 검사하고, 임대 검증은 만료, 폐기, Offline으로 확정된 같은 boot만 거절한다(0005 절). 같은 인스턴스의 새 임대는 이전 임대를 폐기하므로 갱신한 서버는 새 토큰으로 바로 바꾼다. 접속 토큰 발급과 교환은 해당 인스턴스의 현재 살아 있는 임대를 검사하므로 출정의 과거 `server_lease` 값으로 정상 갱신을 막지 않는다.

`IssueJoinTokenAsync`는 플레이어 자격을 사용하는 내부 발급 경로다. 예약의 `JoinTokenIssuer`는 서버 자격으로 계정 ID의 활성 게임 인증 family를 찾는 `IssueDungeonJoinTokenAsync`에 연결한다. 마을 서버 임대, 예약한 마을, 참가자 roster, 계정/캐릭터 소유, 현재 출정의 인스턴스와 역할, 살아 있는 던전 임대를 DB에서 재검증한다. 활성 family가 없으면 `TOKEN_REVOKED`다. 토큰은 최대 60초이며 예약 만료와 인증 계열 만료를 넘지 않는다.

서버의 접속 토큰 교환은 `POST /v1/game-servers/join-tokens/exchange` 한 경로에서 공유 `JoinTokenExchangeRequest/Response`를 사용한다. 요청은 `{joinToken, instanceId, serverRole}`, 응답은 `{characterId, instanceId, serverRole, participantId, sortieId, serverTimeUnixMs}`다. `Authorization: Bearer <서비스 자격으로 발급된 서버 임대 토큰>`과 `X-Shinshi-Instance-Id: <등록 인스턴스 UUID>`를 함께 보낸다. 헤더와 본문 인스턴스/역할을 검증된 자격에 대조하고 참가자/출정/캐릭터는 DB 토큰에서만 읽는다. 토큰은 한 번 소비하며 응답을 잃은 서버는 같은 토큰 교환을 재시도하지 않는다. 이전 `/v1/auth/join/consume` 경로는 제공하지 않는다. Unity verifier와 메인 스레드 전달은 Unity 작업자가 연결한다.

원장 쓰기는 `IdempotentOperationExecutor` 효과 콜백에서 받은 `OperationExecutionContext`로만 호출한다. 활성 PostgreSQL 연결과 같은 트랜잭션인지 검사하고 source operation이 Pending인지 확인한다. `PostgresEconomyLedgerWriter.AppendAsync`, `PostgresItemHistoryWriter.AppendAsync`, `PostgresOutboxWriter.EnqueueAsync`를 실제 잔액/재고 변경과 같은 callback에 넣는다. 업무 거절은 effect savepoint, 예외는 전체 트랜잭션을 롤백하므로 원장/장비 이력/outbox도 함께 롤백된다. 원장 중복 키 `(actor_id, operation_id, entry_key)`, 장비 이력 `(item_id, seq)`, outbox `event_id`가 같은 내용의 재기록을 허용하고 다른 내용은 거절한다. 원장은 직접 잔액을 바꾸지 않으므로 모든 경제 변경 담당자가 이 공용 경로를 연결해야 한다. 장비의 삭제 이력은 item_id 외래 키 없이 남겨 삭제 뒤에도 보존한다.

`EEconomyReason`과 `ContractCodes.EconomyReason`은 동일 숫자의 공유 사유 계약이다. 세션의 다섯 사유는 `SessionStartTicket=1`, `SessionPotionUse=2`, `SessionSettlement=3`, `ServiceCancelRefund=4`, `ServiceCancelEssence=5`다. 그 뒤에는 운영 도구 설계의 경제 사유를 추가했다. `LedgerWriter`는 세션 원장 문자열을 이 숫자로 바꾸고 원래 entry key를 보존하며 각 행에 새 ledger UUID를 부여한다. 원장에 기존 별도 사유 enum은 없었다. 테스트의 `TEST_REASON_CODE`는 테스트 전용 값이다.

`PostgresOutboxDispatcher.ProcessBatchAsync`는 같은 DB에 있는 소비자 전용 전달 작업이다. Committed 원본 operation의 미전달 행을 `FOR UPDATE SKIP LOCKED`로 최대 100개 잠근다. 소비 inbox `(consumer_key, event_id)` 유일 키, 짧은 DB 효과, `delivered_at`를 한 트랜잭션에서 확정한다. 소비 실패와 복원 가능한 PostgreSQL statement/lock timeout은 항목 savepoint로 효과/inbox를 되돌리고 1초 뒤 재시도한다. 직렬화 실패·데드락·드라이버 연결 타임아웃·호출자 취소/프로세스 소실은 배치 전체를 롤백한다. 해당 콜백에 네트워크/RPC/외부 I/O를 넣지 않는다. 외부 전송을 연결할 부모 전달자는 영속 claim 임대와 수신 측 동일 event_id 중복 소비 계약을 추가해야 한다. 지금 API는 background dispatcher를 자동 시작하지 않으므로 부모가 소비 종류별 callback과 주기를 등록한다. `EconomyLedgerReconciler`는 currency/tickets/essence/shards/account_items의 현재 잔액과 원장 numeric 합계를 읽기 전용 keyset 페이지로 비교한다. 초기 잔액도 원장에 기록해야 하며 일일 호출과 불일치 ERROR 보고는 부모 스케줄러가 연결한다.

추가 전용 `economy_ledger`, `item_history`, `outbox_inbox`에는 런타임 UPDATE/DELETE/TRUNCATE 권한을 주지 않는다. 0003의 트리거도 이 변경을 거절한다. 0008의 `admin_audit_log`도 동일하게 SELECT/INSERT만 부여하며(위 권한 SQL) 테이블/스키마 소유권 또는 역할 멤버십으로 권한을 상속하지 않게 한다. 위 권한 SQL의 outbox UPDATE는 전달 상태의 세 열로 제한한다. 기존 DB에서 이미 전체 outbox UPDATE를 부여했다면 먼저 그 전체 권한을 회수한 뒤 열 권한만 부여한다.

0003 down은 검토용이며 인증/원장/전달 이력을 삭제하므로 운영 DB에서 실행하지 않는다. 신규 테스트는 `Auth/`, `Ledger/`에 있다. join의 계정 행 잠금 대기 사슬과 outbox 작업자의 콜백 장벽으로 실제 잠금 겹침을 강제하는 DB 사례를 포함한다. 공유 `MigrationTestResources`가 현재 마이그레이션 개수와 역순 down 체인을 읽는다. DB 환경이 없는 결과를 실제 PostgreSQL 동시성 통과로 해석하지 않는다.


## 세션 연결과 검증 (0004)

세션 API의 `ServerCallerAuthenticator`는 Auth가 검증한 임대를 `ServerCaller(instanceId, role)`로 연결한다. 원장 어댑터는 같은 `OperationExecutionContext`의 트랜잭션으로 Tickets, Essence, AccountItem을 기록한다. `Program.cs`는 런타임 `ConnectionStrings:Postgres`가 설정된 경우에만 `AddShinshiGameSessionMaintenance`를 등록하여 예약 만료 해제와 소실 세션 정산을 5초마다 처리한다. 설정이 없는 공개 상태 조회는 인증 키와 DB 연결 없이 실행할 수 있다.

설정 키의 자리표시자는 `.env.example`에 있다. 패키지는 `Backend/.local/packages`의 기존 캐시를 사용하며 새 패키지 없이 `--no-restore`로 빌드/테스트한다. `ProgramCompositionTests`는 실제 composition root의 DI와 미들웨어 순서, DB 설정에 따른 유지보수 등록을 검사한다. 어댑터 단위 테스트와 실제 PostgreSQL 접속 토큰 HTTP 테스트가 포함되어 있으며 DB 테스트는 `SHINSHI_TEST_CONNECTION_STRING`이 없으면 Skip한다. Skip을 DB 경합/SQL 검증 통과로 해석하지 않는다.
교환은 출정 예약과 복귀의 던전 토큰(`serverRole=1`)과 마을 입장 토큰(`serverRole=0`)을 처리한다. 기존 스키마에는 계정 제재 판정 데이터가 없으므로 `SANCTIONED(808)`는 인증 결과의 HTTP 전파 계약만 검사하고 실제 제재 조회는 후속 운영 모듈에 남긴다. `MAINTENANCE(807)`는 점검 설정일 때 토큰 소비 전에 반환한다. 기존 `Contracts/Auth`의 Consume DTO와 경로 상수는 이번 Assets 쓰기 허용 범위 밖이어서 원본을 보존하고 Backend의 공유 소스 컴파일 대상에서 제외했다.
## 임대 생존 분리, 복귀, 복구 격리 (0005)

서버 프로세스는 `POST /v1/server-instances/lease`에 `Authorization: Bearer <서비스 자격 원문>`, `X-Shinshi-Instance-Id`, 본문 `ServerLeaseRequest{instanceId, serverRole, bootId}`로 임대를 받는다. 서버 경로 중 임대 없이 부르는 유일한 경로다. 서비스 자격은 설정 해시와 메모리에서만 비교하므로 틀린 자격은 DB를 열지 않는다. 인스턴스 행이 없으면 등록 전 자리(`Offline`, boot 없음, 자리 주소)로 만들고 임대를 그 bootId에 묶는다. 이어서 같은 임대로 `register`를 부르면 실제 주소와 콘텐츠 버전으로 `Idle`이 된다. 만료 전 같은 경로로 갱신한다. 응답 401은 자격 오류, 403은 역할 불일치, 409 `SESSION_TERMINAL`은 장애 취소로 확정된 boot이며 새 bootId로 프로세스를 다시 시작해야 한다.

임대 인증과 생존 판정은 분리한다. 임대 검증은 heartbeat 시각을 보지 않는다. heartbeat, 드레인, 같은 boot 등록 재전송은 시간 조건 없이 Offline이 아니면 같은 bootId를 되살린다. 생존 판정(15초)은 효과 지점인 배정, `/start`, `/events`, 입장 토큰 발급과 교환에서만 한다. 되돌릴 수 없는 사망은 장애 취소 트랜잭션이 그 boot를 `Offline`으로 쓰는 순간뿐이다. 임대는 발급 boot의 등록, heartbeat, 드레인에만 쓸 수 있고, 배정은 등록된 현재 boot에 묶인 임대가 있는 던전만 고른다. 새 bootId가 등록하면 이전 boot의 열린 출정은 복구 대상이다. 유지보수 작업은 API 시작 뒤 heartbeat 만료 시간의 두 배(30초) 동안 소실 복구를 하지 않는다. API가 내려가 있던 동안의 오래된 heartbeat를 서버 소실로 보지 않기 위해서다. 만료 예약 해제는 계속한다.

접근, 갱신, 서버 임대, 입장 토큰 원문은 길이와 문자 집합이 이전과 같지만 마지막 8바이트가 무작위 부분의 HMAC 태그다. 태그가 맞지 않는 bearer는 DB 조회 없이 거절한다. 0005 이전에 발급한 토큰과 초대는 태그가 없어 무효가 되므로 재로그인과 새 초대가 필요하다. 인증 실패(bearer 거절, 초대/갱신 교환 401, 서버 임대 자격 401)는 IP별로 1분에 20회까지 세고, 넘으면 창이 끝날 때까지 토큰 검증 없이 429(`Retry-After`)로 거절한다. 자격 교환 경로(`/v1/auth/exchange`, 마을 입장권, 복귀 입장권)는 IP별 1분 60회 ASP.NET Core 요청 한도도 받는다. 0006에서 서버 임대 경로의 실패 처리와 한도, 프록시 IP 복원이 바뀌었다(0006 절).

`POST /v1/game-sessions/{sessionId}/rejoin-ticket`은 게임 하위 세션 토큰으로 부르며(접근 토큰은 403 `SESSION_MISMATCH`, 아래 "게임 하위 세션" 절) 응답은 `JoinTicket`이다. 출정 Running, 요청자 계정의 참가자 Active, 로그인 묶음이 살아 있을 때만 60초 일회 토큰을 발급하고 같은 참가자의 미교환 토큰은 폐기한다. 시작 전은 409 `WRONG_PHASE`, 다른 계정은 403 `SESSION_MISMATCH`, 정산된 참가자는 409 `ALREADY_SETTLED`다. 마을 입장 토큰은 `PostgresAuthRepository.IssueTownJoinTokenAsync`(플레이어 주체, 캐릭터, 마을 인스턴스)로 발급하며 HTTP 발급 경로는 아직 없다. 교환 응답의 `participantId`, `sortieId`는 null이다. 다른 살아 있는 서버에 계정 임대가 있으면 `ALREADY_CONNECTED`다.

복구는 Running/Settling 중 배정 boot가 소실된 출정과, 종료 이벤트 저장 뒤 60초가 지나도 `/settlement`가 없는 Settling 출정을 같은 operationId로 종결한다. 세션별 예외는 그 세션의 `recovery_failures`만 올리고 다음 세션을 계속 처리한다. 실패가 적은 세션부터 처리하고 5회에 닿으면 자동 복구에서 빼고 ERROR로 남긴다. 격리된 세션은 원인을 고친 뒤 운영자가 `recovery_failures`를 0으로 되돌려야 다시 복구된다. `/events`는 참가자별 출정 누적 추가 정기, 정산 아이템 한 줄과 합산 수량, 허용 definitionId(`item_` 접두, 소문자, 숫자, 밑줄)를 상한으로 검사하며 값은 `SettlementSanityLimits`의 임시값이다. 같은 newLeaseId 임대 이관 재시도도 만료된 계정 임대를 성공으로 돌려주지 않는다. `session_events`는 TRUNCATE도 트리거로 거절한다.

## 생존 판정, 계정 접속 임대, 인증 한도 보강 (0006)

3차 공격자 관점 검토의 결함을 고쳤다. 0006은 `game_sessions.next_recovery_at`(복구 백오프), `game_sessions.ended_essence`(종료 정산안 정기 합계)와 인덱스 `ix_game_sessions_instance_settling`, `ix_server_instances_idle_registered`, `ix_auth_join_tokens_expiry`를 더한다. down은 두 열과 세 인덱스를 지우며 열린 출정이 없을 때 실행한다. 런타임 역할에 `auth_join_tokens` DELETE 권한이 필요하다(위 권한 SQL).

**장애 취소 보류.** 유지 작업은 API 시작 뒤뿐 아니라 DB에 닿지 못한 주기(연결 거부, 소켓 단절, 08xxx, 57P01~57P03) 뒤에도 30초 동안 소실 복구를 하지 않는다. 그동안 서버들의 heartbeat도 저장되지 못했기 때문이다. 진행 중(Running) 출정의 장애 취소(환급)는 배정 boot의 heartbeat가 60초(`SERVICE_CANCEL_HEARTBEAT_SECONDS`) 끊기거나 Offline 확정, 새 boot 등록일 때만 하고, 살아 있는 서버(Offline 아님, 15초 안)가 하나도 없으면 API 쪽 단절로 보고 하지 않는다. 정산안이 있는 Settling 복구는 15초 기준 그대로다. 복구 중 잠금/문장 제한 초과와 생존 판정 보류는 격리 실패 횟수에 넣지 않고, 업무 원인의 실패는 5초 * 2^(이전 실패 횟수)(지수 상한 6) 뒤에 다시 시도한다. DB 연결 실패는 배치를 멈추고 복구 보류를 연장한다.

**생존 판정 일시 실패.** `/start`, `/events`의 생존 판정이 실패했지만 배정 boot가 Offline도 교체도 아니면(heartbeat만 늦음) 거절을 저장하지 않고 효과와 operations 결과를 모두 되돌린 뒤 503, `BACKEND_UNAVAILABLE(810)`, `Retry-After: 5`로 응답한다. 같은 operationId로 다시 보내면 된다. 서버 효과 경로(`/start`, `/events`, `/settlement`, 예약, 입장권 발급, 임대 이관)는 효과 트랜잭션 전에 호출 boot의 heartbeat 시각을 짧은 문장으로 갱신한다(임대 boot 일치, Offline 제외). DB 경합, 제한 초과, 연결 실패 예외도 같은 503(`Retry-After: 1`)으로 바뀐다.

**인스턴스 수명.** 정산, 예약 해제(취소, 만료) 뒤 인스턴스는 Idle이 아니라 Draining이다. 출정을 맡은 boot는 다시 배정하지 않으며 Idle은 새 boot의 register만 만든다. 배정은 `registered_at, instance_id` 순서로 고른다. 같은 인스턴스에 다른 boot가 살아 있으면(Offline 아님, heartbeat 15초 안) 서버 임대 발급과 register는 503, 810, `Retry-After: 5`다. 출정을 마치고 Draining이 된 던전 boot에 열린 출정이 없으면 새 boot가 바로 넘겨받는다(마을 Draining은 예외 없음). 이 인스턴스의 임대를 받은 적이 있는 boot가 다른 boot로 대체된 뒤 임대를 다시 청하면 409 `SESSION_TERMINAL`이다. heartbeat, register, drain에서 회복할 수 없는 boot는 409 `SESSION_TERMINAL`(Offline 확정) 또는 409 `SESSION_MISMATCH`(다른 boot로 대체, 임대 boot 불일치)뿐이고, 일시 상태는 503, 만료된 서버 임대는 401이다. drain은 멱등이며 heartbeat와 같은 생존 신호(heartbeat 시각, 계정 임대 연장)다.

**계정 접속 임대.** 마을 입장 교환이 토큰 소비와 같은 트랜잭션(계정 잠금 아래)에서 `account_leases`를 얻는다. 살아 있는 다른 임대가 있으면 같은 마을이어도 409 `ALREADY_CONNECTED`다. 응답 `JoinTokenExchangeResponse.accountLeaseId`(마을만, 던전은 null)를 마을 서버가 보관한다. 임대 수명은 15초이고 인스턴스 heartbeat(drain 포함)가 그 인스턴스의 살아 있는 임대 전부를 함께 연장한다. 던전으로 이관된 임대도 같다. 만료된 임대는 되살리지 않는다. 해제는 `POST /v1/server-instances/{instanceId}/account-leases/{leaseId}/release`(서버 임대 토큰, 본문 없음)이며 이미 풀렸거나 이관된 임대도 204, 다른 인스턴스의 임대는 403이다. 정산, 장애 취소, 예약 해제는 같은 트랜잭션에서 그 던전에 남은 참가자 임대를 지운다. 잠금 순서는 계정 -> 임대 행(계정 순) -> 인스턴스이며 heartbeat의 임대 연장은 인스턴스 갱신이 커밋된 뒤 따로 실행한다.

**인증 한도.** 서버 임대 경로(`[RequireServerLease]`)는 IP 차단 대상에서 빠지고 실패도 세지 않는다. 같은 IP의 플레이어 실패가 heartbeat와 정산을 막으면 출정이 장애 취소되기 때문이다. 플레이어 경로와 자격 교환은 태그가 맞는(우리 키로 만든) 토큰의 만료, 폐기를 실패로 세지 않는다. 서비스 자격(서버 임대 발급) 실패는 별도 `AuthFailureLimiter` 인스턴스(키 `AuthFailureLimiter.SERVICE_CREDENTIAL_KEY`)에서 IP별 1분 20회로 센다. 서버 임대 경로의 요청 한도는 IP+`X-Shinshi-Instance-Id` 키로 1분 120회다. 복귀 입장권은 IP 자격 한도에 더해 계정별 1분 6회이며 계정 행은 `FOR NO KEY UPDATE`로 잠근다. 유지 작업은 만료 뒤 60분이 지난 입장 토큰을 주기마다 최대 500개 지운다.

**정산 상한.** 정산 아이템 귀속은 허용 목록(`Account`, Unity `SETTLEMENT_ITEM_BINDING_KIND`와 같음)만 받는다. 같은 던전 인스턴스의 24시간 종료 정산안 정기 합계가 `MAX_INSTANCE_DAILY_ESSENCE`(임시값: 출정 상한 * 최대 파티 4 * 하루 288출정)를 넘는 종료 배치는 효과 없이 904로 거절한다. 두 값은 기획 확정 전 임시값(TODO)이다.

**프록시 뒤 배포 경고.** 로드밸런서 뒤에 둘 때만 `ForwardedHeaders__KnownProxies__0` 등에 로드밸런서의 IP를 정확히 넣는다. 값이 있을 때만 `X-Forwarded-For`(한 단)를 적용하고 기본 루프백 신뢰도 지운다. 값 없이 로드밸런서 뒤에 두면 모든 요청이 로드밸런서 IP 하나로 보여 IP별 한도가 전체 사용자를 함께 막는다. 반대로 아무 프록시나 믿게 설정하면 클라이언트가 헤더로 IP를 바꿔 한도를 피한다. 로드밸런서 사용 여부는 아직 미정이다(docs/03 15장 API 도메인, TLS).

**배포 전 재발급(운영).** 0005의 토큰 태그 도입으로 그 이전에 발급한 접근/갱신/서버 임대/입장 토큰과 초대 코드는 모두 무효다. 아직 배포 전이라 코드 변경은 없지만, 첫 배포와 `Auth__HashKeyBase64` 교체 때는 기존 토큰 계열을 폐기하고 모든 체험 계정에 새 초대를 발급하며, 게임 서버는 서비스 자격으로 서버 임대를 다시 받는 절차를 배포 절차에 넣는다.

## 운영자 어드민 기반 (0008)

체험판 어드민은 API와 명령줄 도구만 제공한다. 경로는 `/admin/v1` 아래이고 상수는 `Shinshi.Contracts.Admin.AdminApiRoutes`, DTO와 숫자 코드는 같은 이름 공간(`AdminContractCodes`)에 있다. Unity 공유 계약에는 넣지 않는다. 컨트롤러는 `Shinshi.Api/Admin/`(이름 공간 `Shinshi.Backend.Admin`), 데이터 계층은 `Shinshi.Data/Admin/`이다.

**설정.** 모두 환경이나 비밀 저장소로 주입하며 값을 저장소에 넣지 않는다.

| 키 | 기본값 | 뜻 |
|---|---|---|
| `Admin__TotpKeyBase64` | 없음 | TOTP 비밀을 AES-256-GCM으로 암호화하는 32바이트 무작위 키의 Base64. 바꾸면 기존 운영자의 TOTP를 풀 수 없으므로 운영자를 모두 다시 만든다 |
| `Admin__AllowedNetworks__0` 등 | 비어 있음 | 어드민 경로를 열 CIDR(`10.0.0.0/8`)이나 단일 IP. 비어 있으면 루프백만 연다. 호스트 비트가 남은 CIDR, 형식 오류는 시작을 실패시킨다 |
| `Admin__SessionLifetimeMinutes` | 30 | 어드민 접근 토큰 수명(1~480분) |
| `Admin__PasswordIterations` | 600000 | 새 비밀번호의 PBKDF2-HMAC-SHA256 회수(1000~10000000). 테스트만 낮춘다. 저장된 해시는 행의 회수로 검증한다 |
| `Auth__HashKeyBase64` | 없음 | 게임 토큰과 같은 키로 어드민 토큰과 요청 IP를 HMAC한다. 용도 문자열이 달라 게임 토큰을 어드민 세션으로 찾을 수 없다 |

허용 네트워크는 forwarded headers 처리 뒤의 `RemoteIpAddress`로 판정한다. 로드밸런서 뒤에서는 `ForwardedHeaders__KnownProxies__*`를 먼저 설정해야 하며, 그렇지 않으면 모든 요청이 로드밸런서 IP로 보인다. 두 가지 잘못된 배치는 닫힌 쪽으로 처리한다. 신뢰 프록시 주소가 허용 네트워크 안에 있으면(허용 목록이 비어 있으면 루프백 프록시) 시작을 실패시킨다. 프록시가 `X-Forwarded-For`를 빠뜨린 요청이 프록시 주소로 남아 허용 판정을 통과하기 때문이다. `KnownProxies` 없이 `X-Forwarded-For`나 `Forwarded` 헤더가 붙은 어드민 요청은 허용 판정 없이 404이고 프로세스당 한 번 오류 로그를 남긴다. 어드민 설정이 없어도 게임 API는 뜨고, 비밀키가 필요한 어드민 서비스는 첫 어드민 요청에서 만들어진다(키가 없으면 그 요청이 500).

**최초 Owner.** `admin_users`가 비어 있을 때만 성공한다. 런타임 연결을 쓰며 무작위 비밀번호, TOTP 비밀(Base32), otpauth URI를 표준 출력에 한 번만 쓴다. 감사 로그에는 actor `System`으로 남는다. 출력은 인증 앱 등록과 비밀번호 보관 뒤 터미널 기록에서 지운다. 두 번째 실행은 아무것도 쓰지 않고 종료 코드 1이다.

```powershell
$env:ConnectionStrings__Postgres = '<runtime connection string from environment or secret>'
$env:Admin__TotpKeyBase64 = '<base64 of 32 random bytes from secret store>'
$env:SHINSHI_BOOTSTRAP_OWNER = 'owner.name'
dotnet run --project Backend/src/Shinshi.Api -- --bootstrap-owner
Remove-Item Env:SHINSHI_BOOTSTRAP_OWNER
```

**인증.** `POST /admin/v1/auth/login {loginName, password, totpCode}`는 비밀번호와 TOTP(RFC 6238, SHA1, 30초, 6자리, 앞뒤 한 스텝)를 함께 검사하고 30분 불투명 토큰을 준다. 이미 받은 스텝 이하의 코드는 재사용으로 거절한다. 실패는 원인과 무관하게 401 `INVALID_CREDENTIALS(9001)` 하나이고, 운영자별 연속 5회 실패는 15분 잠금이다. 로그인은 자격 교환 요청 한도(IP별 1분 60회)와 어드민 전용 실패 한도(IP별 1분 20회, 키 `shinshi-admin`)를 함께 받는다. `POST /admin/v1/auth/logout`은 현재 세션만 폐기하며 업무 변경이 아니어서 operationId/reason을 받지 않는다. 어드민 응답은 모두 `Cache-Control: no-store`다.

어드민 엔드포인트는 `AdminAuthenticationMiddleware`가 처리하고 기존 `BearerTokenMiddleware`는 건너뛴다. 판정 순서는 허용 네트워크 밖 404(존재 숨김), 토큰 없음이나 어드민 세션이 아닌 토큰(게임 접근 토큰, 서버 임대 토큰 포함) 401 `TOKEN_REVOKED`, 역할 부족 403 `FORBIDDEN_ROLE(9002)`이다. 역할은 요청마다 `admin_users`에서 다시 읽고, 비활성화된 운영자의 세션은 곧바로 무효다. `/admin/v1` 아래인데 어드민 표시가 없는 엔드포인트는 플레이어 토큰으로 열리지 않도록 404로 숨긴다. 어드민 토큰으로 플레이어 경로를 부르면 401이다.

**새 어드민 컨트롤러 작성 규칙.** `AdminControllerBase`를 상속하면 Viewer 이상 토큰, 바인딩과 operationId/reason 오류의 400 `INVALID_REQUEST(9003)`가 적용된다. 더 높은 역할은 `[RequireAdminRole(EAdminRole.Operator)]`처럼 컨트롤러나 액션에 붙이며, 둘 중 높은 역할을 요구한다(액션 표시로 낮출 수 없다). 현재 운영자는 `AdminActors.Get(HttpContext)`(또는 `CurrentAdmin`)로 읽는다. 쓰기는 모두 `AdminOperationExecutor.ExecuteAsync(HttpContext, request, operationKind, targetKey, effect)`를 거친다. 요청 DTO는 `IAdminWriteRequest`(operationId Guid, reason 공백 아닌 최대 500자)를 구현하고, 본문 밖 대상(경로의 ID)은 `targetKey`로 넘겨 본문과 함께 해시한다. actor는 운영자 ID이고 같은 operationId 재전송은 처음 결과, 다른 본문이나 대상은 `IdempotencyConflict`다. 효과 콜백은 `AdminOperationContext.Execution`의 트랜잭션만 쓰며 `context.AuditAsync(...)`로 같은 트랜잭션에 감사 행을 남긴다. Committed인데 감사 행이 없으면 실행기가 예외로 전체를 롤백한다. 실행기는 operations 행을 만들기 전에 모든 업무 변경의 작업 키를 종류, 대상, 본문 해시에 영속 결속한다. 다른 해시는 operations 행과 효과 없이 409 `IDEMPOTENCY_CONFLICT`로 거절하므로, 진행 중인 수동 복구의 결과를 다른 쓰기가 점유하지 않는다. 일시 실패 뒤에도 같은 operationId는 최초 요청 본문으로 재시도한다. 실행기는 효과보다 먼저 같은 트랜잭션에서 행위자 `admin_users` 행을 `FOR SHARE`로 잠가 비활성화나 역할 하향(인증 때 역할보다 낮음)을 다시 보고, 운영자 작업 키 결속(`admin_operation_claims`)도 다시 확인한다. 비활성화나 역할 하향은 저장되는 409 `FORBIDDEN_ROLE(9002)`다. 잠금은 커밋까지 남으므로 그 사이의 비활성화는 이 작업 뒤로 밀린다. 행위자와 대상을 ID 순서로 직접 잠그는 운영자 비활성화만 `lockActor: false`로 부르고 효과(`DisableAsync`) 안에서 다시 확인한다. 이 경로의 이미 비활성인 행위자는 기존대로 `INVALID_STATE`, 역할 하향은 `FORBIDDEN_ROLE`다. 업무 거절(`OperationDecision.Rejected`)은 효과 savepoint와 함께 감사 행도 롤백하므로 **감사 로그는 커밋된 변경만 기록한다**. 업무 거절은 `operations`의 Rejected 결과로만 남는다. 다른 요청의 키 결속과 충돌한 요청은 해당 결과를 덮어쓰지 않도록 저장하지 않는다. 계정 상세 같은 민감 조회는 `IAdminAuditWriter.AppendStandaloneAsync`로 operation 없이 기록한다. 원장 쓰기는 기존 `IEconomyLedgerEntryWriter`에 `context.Execution`을 넘긴다(actor_kind `Admin`, actor_id 운영자 ID). 서비스 등록은 `AddShinshiAdmin`이 부르는 `AdminGrantsServiceCollectionExtensions`, `AdminServiceStateServiceCollectionExtensions`, `AdminLogsServiceCollectionExtensions`에 넣고 `Program.cs`는 고치지 않는다.

**운영자 관리(Owner).** `GET /admin/v1/operators`, `POST /admin/v1/operators {operationId, reason, loginName, role}`, `POST /admin/v1/operators/{adminId}/disable {operationId, reason}`. 생성은 무작위 비밀번호와 TOTP 비밀을 그 생성을 확정한 응답에만 싣는다. 같은 operationId 재전송은 같은 adminId를 주지만 비밀값은 null이다(서버는 해시와 암호문만 저장). 응답을 잃었으면 그 운영자를 비활성화하고 다시 만든다. 같은 로그인 이름은 409 `LOGIN_NAME_TAKEN(9006)`, 자기 자신 비활성화는 409 `SELF_TARGET(9007)`이다. 행위자와 대상을 ID 순서로 함께 잠근 뒤, 행위자가 그사이 비활성화됐거나 대상이 마지막 활성 Owner면 409 `INVALID_STATE(9005)`로 거절한다. Owner 둘이 서로를 동시에 비활성화해도 활성 Owner가 0명이 되지 않는다. 비활성화는 대상의 열린 세션을 같은 트랜잭션에서 폐기한다. 운영자 행은 지우지 않는다.

**0008 테이블.** `admin_users`(소문자 login_name, PBKDF2 해시와 솔트, AES-GCM TOTP 암호문, role 0~3, 실패 횟수, 잠금 시각, 마지막 TOTP 스텝), `admin_sessions`(어드민 토큰 HMAC), `admin_audit_log`(추가 전용, 0003의 `reject_append_only_mutation` 트리거가 UPDATE/DELETE/TRUNCATE를 거절, operation_id가 있으면 같은 운영자의 operations 행을 FK로 가리킴), `admin_grants`, `reward_inbox`(`(grant_id, account_id)` 유일, 수령과 취소 상호 배타, 수령 operation과 캐릭터 소유 FK), `service_state`(id=1 단일 행, 0008이 점검 중과 빈 목록으로 넣음), `service_notices`, `admin_operation_claims`(운영자 작업 키 `(admin_id, operation_id)`를 작업 종류, 대상, 요청 해시에 묶는 추가 전용 결속, 일반 업무 변경과 수동 복구가 효과 전에 커밋). `admin_grants`의 부분 유일 인덱스 `ux_admin_grants_recovery_ref`는 취소되지 않은 `RecoveryCompensation` 지급의 `source_ref`(복구 대상 세션 ID)를 유일하게 한다. 0008은 어드민 조회용으로 기존 테이블에도 인덱스 넷을 더한다: `session_participants(account_id, session_id)`, `economy_ledger(account_id, occurred_at, ledger_id)`, `game_sessions(state, created_at, session_id)`, `admin_audit_log(target_kind, occurred_at, audit_id)`. 0008의 지급·조정 코드는 0009 경매 마이그레이션의 `account_wallets.currency_reserved` 열을 읽으므로 두 마이그레이션은 함께 배포한다. 이번 미커밋 0008은 작업 키 결속과 빈 대상 키(생성 작업)를 반영해 수정했다. 이전 0008을 이미 적용한 DB의 DDL은 자동 갱신되지 않으며, 마이그레이션 실행기는 체크섬 불일치로 중단한다. 기존 DB는 별도 전환 절차가 필요하고 이 작업에서는 재생성하거나 변경하지 않았다. 검증은 최신 내장 SQL을 매번 적용하는 GUID 임시 테스트 스키마에서 수행했다. 0008 down은 검토용이며 이 테이블을 모두 지우므로 운영 DB에서 실행하지 않는다. 테스트는 `tests/Shinshi.Backend.Tests/Admin/`와 `tests/Shinshi.Backend.Tests/Inbox/`에 있다.

**엔드포인트.** 경로 상수는 `AdminApiRoutes`, 플레이어 우편함은 공유 `ApiRoutes.INBOX`·`INBOX_CLAIM`이다. 최소 역할은 컨트롤러와 액션의 `RequireAdminRole` 중 높은 값이다.

| 경로 | 메서드 | 최소 역할 | 비고 |
|---|---|---|---|
| `/admin/v1/auth/login` | POST | 없음(허용 네트워크 안) | 실패는 원인 구분 없이 401 `INVALID_CREDENTIALS(9001)` |
| `/admin/v1/auth/logout` | POST | Viewer | 현재 어드민 세션만 폐기 |
| `/admin/v1/operators` | GET, POST | Owner | 생성 응답에만 초기 비밀번호·TOTP 비밀 |
| `/admin/v1/operators/{adminId}/disable` | POST | Owner | 대상의 열린 세션 폐기. 마지막 활성 Owner는 거절 |
| `/admin/v1/accounts?query=` | GET | Viewer | UUID(계정·캐릭터 ID)만. 그 밖은 400 |
| `/admin/v1/accounts/search` | POST | Viewer | 본문 `{query}`. UUID 또는 초대 코드 원문(HMAC으로 바꿔 검색). 원문은 이 경로로만 받는다 |
| `/admin/v1/accounts/{accountId}?reason=` | GET | Viewer | 민감 조회. `reason` 필수, 감사 행을 먼저 커밋한 뒤 읽음 |
| `/admin/v1/accounts/{accountId}/kick` | POST | Operator | 로그인 묶음과 하위 토큰, `revokeInvites`면 초대도 폐기. 현재 접속은 유지, 비밀번호 로그인은 막지 않음 |
| `/admin/v1/accounts/{accountId}/adjustments` | POST | Operator | 개인 재화·아이템 ± 조정 |
| `/admin/v1/invites` | POST | Operator | 초대 원문은 처음 확정 응답에만 |
| `/admin/v1/invites/{inviteId}/revoke` | POST | Operator | |
| `/admin/v1/service-state` | GET / PUT | Viewer / Admin | 점검·기능 차단·허용 빌드 |
| `/admin/v1/notices` | GET / POST | Viewer / Operator | 공지 목록(끝난 지 7일까지)·작성 |
| `/admin/v1/notices/{noticeId}/end` | POST | Operator | |
| `/admin/v1/grants` | POST | Operator | 지급 초안(Draft) 작성 |
| `/admin/v1/grants/{grantId}` | GET | Operator | 지급 상세와 우편 상태 집계 |
| `/admin/v1/grants/{grantId}/execute` | POST | Operator, 대상 2명 이상이나 `RecoveryCompensation`이면 Admin | 우편함에 넣기. 역할 부족은 작업 행 없이 403 |
| `/admin/v1/grants/{grantId}/cancel` | POST | Operator | 미수령분 취소 |
| `/admin/v1/ledger?reason=` | GET | Viewer | 민감 조회(감사 먼저). 원장과 첫 페이지의 사유·자산별 합계 |
| `/admin/v1/items/{itemId}/history?reason=` | GET | Viewer | 민감 조회. 아이템 이력 전체 |
| `/admin/v1/sessions/{sessionId}/events?reason=` | GET | Viewer | 민감 조회. 세션 요약, 참가자, 이벤트, 관련 원장과 작업 |
| `/admin/v1/operations/{actorId}/{operationId}?reason=` | GET | Viewer | 민감 조회. 작업 행과 같은 작업 키의 원장, 아이템 이력, outbox, 감사 |
| `/admin/v1/audit` | GET | Viewer | 감사 로그. 조회 자체는 감사하지 않음 |
| `/admin/v1/sessions?state=` | GET | Viewer | 복구 대상 세션 목록과 생존 판정 보류 여부 |
| `/admin/v1/sessions/{sessionId}/recovery` | POST | Admin | 정산 재시도, 서비스 장애 취소, 격리 세션 강제 종료 |
| `/v1/inbox` | GET | 플레이어 접근 토큰 | 어드민·서버 토큰은 401. 계정별 1분 60회 |
| `/v1/inbox/claim` | POST | 플레이어 접근 토큰 | `inbox_claim` 기능 차단 대상. 계정별 1분 30회, 본문 4 KiB(넘으면 413). 둘 다 본문을 읽기 전에 거절 |

업무 변경 쓰기(위 표에서 인증 경로 `auth/login`, `auth/logout`을 뺀 POST·PUT)는 모두 본문에 `operationId`(Guid)와 `reason`(공백 아닌 최대 500자)을 싣는다. 로그인은 자격 증명만, 로그아웃은 본문 없이 받고 operation 없이 감사한다. 업무 거절은 409와 `OperationResult`(`NOT_FOUND(9004)`, `INVALID_STATE(9005)` 등), 형식 오류는 400 `INVALID_REQUEST(9003)`다. 로그인, 로그아웃, 잠금, 계정 상세 조회, 네 로그 조회(원장, 아이템 이력, 세션 이벤트, 작업), 세션 복구 의도(`admin.session.recovery.intent`)는 operation 없이 단독 감사 행을 남긴다. 로그인 실패 자체는 감사하지 않고 잠금(5회 연속 실패)만 남긴다. 어드민 응답은 모두 `Cache-Control: no-store`(`CacheControlValues.NO_STORE`)다.

**배포 절차.** 0008을 처음 적용한 DB에서는 아래 순서를 지킨다. `service_state`의 초기 행이 점검 중·허용 빌드 없음이라 5단계 전에는 모든 게임 로그인이 `MAINTENANCE`로 거절된다.

1. `ConnectionStrings__PostgresMigrator`로 `--migrate`를 실행하고 위 권한 SQL의 0008 줄을 적용한다.
2. `Auth__HashKeyBase64`, `Admin__TotpKeyBase64`, `Admin__AllowedNetworks__*`, `ServiceState__Maintenance=false`를 비밀 저장소나 환경으로 넣는다.
3. 위 "최초 Owner" 절차로 `--bootstrap-owner`를 한 번 실행하고 출력한 비밀을 인증 앱과 비밀번호 보관소에 옮긴다.
4. API를 시작하고 허용 네트워크 안에서 `POST /admin/v1/auth/login`으로 로그인한다. 일상 작업용 운영자는 Owner가 `POST /admin/v1/operators`로 만든다.
5. Admin 이상이 `GET /admin/v1/service-state`로 `revision`을 읽고 `PUT /admin/v1/service-state {operationId, reason, maintenance:false, disabledFeatures:[], allowedBuildIds:[배포 빌드], expectedRevision}`으로 서비스를 연다. 모든 필드가 필수이며 빠지면 400이다(빠진 값을 기본값으로 저장해 점검이 의도 없이 풀리지 않게).
6. `GET /v1/service-state`로 공개 응답을 확인한다.

**서비스 상태와 공지.** 싱글턴 `ServiceStateStore`가 캐시한다. `ConnectionStrings:Postgres`가 있으면 DB 모드이고 `ServiceState__RefreshSeconds`(기본 5, 1~60)마다 다시 읽는다. 주기가 지난 요청은 이전 값을 받고 읽기는 백그라운드로 돈다. 이 인스턴스가 상태나 공지를 쓰면 응답 전에 바로 다시 읽으므로 즉시 반영되고, 다른 API 인스턴스는 최대 한 주기 늦게 본다. 읽기가 실패하면 마지막 성공 값을 유지하고 경고 로그(예외 형식 이름만)를 남긴다. DB 모드에서는 호스트 시작 때 `ServiceStateStartupService`가 백그라운드로 한 번 읽어 둔다(시작을 붙잡지 않음). 첫 읽기 전에 온 로그인(`AuthBuildPolicy.ValidateAsync`)과 공개 상태 조회, 새 출정 게이트는 그 읽기를 비동기로 기다린다. 기다리지 않는 동기 경로(`ServiceStateStore.Current`)는 첫 읽기 전이면 점검 중으로 답한다. 읽기가 계속 실패하면 점검 중으로 보고(`notice`는 설정 문구) 주기마다 한 번만 다시 시도한다. 연결 문자열이 없으면 기존 `ServiceStateOptions` 설정을 그대로 쓴다. DB 모드인데 `ServiceState:Maintenance`를 명시하지 않았으면 시작 때 경고 로그를 남긴다(아래 입장 토큰 교환 때문).

DB 점검은 로그인과 토큰 갱신 교환(`AuthBuildPolicy`)과 새 출정 예약을 막는다. 이미 예약하거나 시작한 출정의 입장, 정산, 복구는 막지 않는다. 입장 토큰 교환(`JoinTokenController`)은 DB 점검이 진행 중 출정의 재접속을 끊지 않도록 의도적으로 `ServiceState__Maintenance` 설정을 직접 읽는다. 이 설정의 기본값이 `true`이므로 운영에서는 `false`로 유지한다. 저장은 revision 낙관적 검사이며 다른 운영자가 먼저 저장했으면 409 `INVALID_STATE`이고 결과의 `committedRevision`이 현재 revision이다. 허용 빌드는 최대 64개, 각 128자다.

공개 `ServiceStateResponse`는 기존 필드 의미를 유지하고 `notices` 배열(`ServiceNotice`: noticeId, title, body, target `Launcher`/`Game`/`Both`, severity 0~2, 시작·끝 시각)을 더했다. `notices`는 지금 노출 중인 공지만 중요도, 시작 시각 내림차순으로 최대 20개다. 기존 `notice` 문자열은 노출 중인 런처 대상 첫 공지의 제목이고, DB 모드에서 그런 공지가 없으면 빈 문자열이다. 공지 작성의 형식 검사(제목·본문 길이, 대상, 중요도, 시작 < 끝)는 작업 행 없이 400이다. 현재 시각과 비교하는 검사(끝 시각이 지금 이하)는 멱등 실행 안에서 DB 시각으로 하고 저장되는 409 `INVALID_REQUEST`다. 그래서 확정된 공지를 만료 뒤 같은 operationId로 재전송해도 처음 결과를 받는다.

**기능 차단.** 키는 `ServiceFeatureKeys`의 `auction`, `crafting`, `enhance`, `inherit`, `gathering`, `boss_entry`, `inbox_claim`, `dungeon_entry`이며 목록 밖 키는 저장에서 400이다. 진입점은 싱글턴 `ServiceFeatureGate`를 받아 `CheckFeatureAsync(ServiceFeatureKeys.X)`가 `FEATURE_DISABLED(1100)`이면 효과와 operations 저장 전에 409로 거절한다. 게이트 거절 자체는 저장하지 않는다. 막는 동안 같은 operationId 재전송은 `IdempotentOperationExecutor.FindStoredAsync`로 저장된 처음 결과를 돌려주고, 저장된 결과가 없으면 차단 거절이다. 그래서 차단 중에 처음 보낸 operationId는 차단이 풀린 뒤 다시 보내면 새로 실행된다. 철자가 틀린 키는 예외다. 이미 시작한 진행(정산, 경매 마감 등)은 막지 않는다. 현재 연결된 곳은 새 출정 예약(점검이면 `MAINTENANCE`, `dungeon_entry` 또는 보스전의 `boss_entry`가 꺼졌으면 `FEATURE_DISABLED`, `CheckNewSortieAsync`)과 `POST /v1/inbox/claim`(`inbox_claim`)뿐이다. `auction`, `crafting`, `enhance`, `inherit`, `gathering`은 키만 있고 각 기능의 API 진입점이 생길 때 연결한다.

**운영자 지급과 우편함.** 지급은 `admin_grants` 한 건이다. 작성(`POST /admin/v1/grants {targetAccountIds 1~1000, rewards 1~50줄, claimDays 1~365(기본 30), sourceKind, sourceRef}`)은 Draft만 만들고 대상 계정이 하나라도 없으면 `NOT_FOUND`로 전체를 거절한다. 실행은 Draft만 받아 대상마다 우편 한 행을 한 트랜잭션에 넣는다(`(grant_id, account_id)` 유일이라 다시 넣지 않음, 기한은 DB 시각 + claimDays). 대상이 2명 이상인 일괄 지급과 `RecoveryCompensation` 지급의 실행은 Admin 이상만 할 수 있다(`AdminGrantPolicy`). Operator가 부르면 작업 행 없이 403 `FORBIDDEN_ROLE`이고, 실행 효과도 잠근 지급 행으로 같은 판정을 다시 한다. `RecoveryCompensation`은 같은 `sourceRef`에 취소되지 않은 지급이 이미 있으면 작성이 409 `INVALID_STATE`다(유일 인덱스가 동시 작성도 막음, 앞 지급을 취소하면 다시 작성 가능). P0에는 2인 승인이 없어 실행 감사 행을 `self_approved=true`로 남긴다. 취소는 Draft를 그대로 Cancelled로, Executed는 기한 안의 미수령 우편만 취소하고 수령·만료 수를 감사에 남긴다. 이미 수령한 우편은 되돌리지 않으며 회수는 개인 조정으로 한다. 자산은 `Currency`, `Tickets`(계정), `Essence`, `Shards`(캐릭터, 수령 때 고름), `AccountItem`(정산과 같은 허용 ID, 귀속 `Account`)이고 행동력은 지급 대상이 아니다. 한 줄 수량 상한 1,000,000은 오타 방지용 임시값이다(TODO, 기획 미정).

개인 조정(`POST /admin/v1/accounts/{accountId}/adjustments {assetKind, assetId, bindingKind, characterId, delta}`)은 증가를 `AdminGrant(26)`, 감소를 `AdminRevoke(27)` 원장으로 남긴다(actor_kind `Admin`). 감소는 잔액에서 예약분(`tickets_reserved`, 0009의 `currency_reserved`, `account_items.reserved`)을 뺀 가용량까지만 빼고 모자란 양을 `shortfall`로 응답과 감사에 남긴다. 잔액을 음수로 만들지 않으며 가용량이 0이어도 감사와 함께 확정한다. 같은 operationId 재전송은 감사 행에서 처음 결과를 다시 읽는다.

플레이어 `GET /v1/inbox`는 미수령·미취소·미만료 우편을 곧 만료되는 순서(`expires_at` 오름차순)로 최대 100개 주고 캐릭터 자산이 있으면 `requiresCharacter`다. 저장된 보상 JSON은 읽을 때 구조만 검증한다(허용 ID·수량 상한 같은 작성 정책은 작성 때만 적용). 구조가 손상된 행은 목록에서 빼고 우편 ID만 경고 로그로 남긴다. `POST /v1/inbox/claim {operationId, inboxId, characterId}`는 actor가 계정인 플레이어 작업이며 잔액 증가, 원장(`InboxClaim(24)`, actor_kind `Player`, source_type `inbox`, source_id 우편 ID), 수령 표시를 한 트랜잭션에 쓴다. 요청 해시는 원문이 아니라 파싱한 값이라 GUID 표기만 다른 재전송도 같은 요청이다. 거절은 409와 `INBOX_EXPIRED(1101)`, `INBOX_ALREADY_CLAIMED(1102)`, `INBOX_CANCELLED(1103)`, `INBOX_CHARACTER_REQUIRED(1104, 남의 캐릭터 포함)`, `INBOX_NOT_FOUND(1105)`다. 없는 우편과 남의 우편은 operations 행을 남기지 않고 `INBOX_NOT_FOUND`다. 한도는 계정별 1분에 수령 30회, 목록 60회(`InboxRequestLimiter`, 프로세스 메모리)이고 넘으면 429 `RATE_LIMITED`와 `Retry-After`다. 한도와 수령 본문 상한(4 KiB, `Content-Length`가 넘으면 413, 길이 없는 본문은 `RequestSizeLimit`이 Kestrel에서 끊음)은 리소스 필터 `InboxRequestGateFilter`가 모델 바인딩 전에 적용하므로 거절된 요청의 본문은 읽지 않는다. `inbox_claim`이 꺼져 있으면 위 기능 차단 규칙을 따른다(저장된 결과가 있으면 그 결과, 없으면 409 `FEATURE_DISABLED`). 점검은 우편 수령을 막지 않는다. 장애 보정 지급(`sourceKind=RecoveryCompensation`)의 수령도 같은 `InboxClaim(24)`로 남으며 `RecoveryCompensation(30)` 원장 사유는 아직 쓰는 경로가 없다. 출처는 우편 행의 `source_kind`로 구분한다.

**계정과 초대.** 검색은 `GET /admin/v1/accounts?query=<UUID>`(계정·캐릭터 ID만, 그 밖은 400)와 `POST /admin/v1/accounts/search {query}`(UUID 또는 초대 코드 원문) 둘이다. 요청 시작 로그에 조회 문자열이 남으므로 초대 원문은 본문으로만 받는다. 검색 결과는 계정 요약만 준다. 캐릭터 이름 검색은 없다. 상세는 지갑, 캐릭터, 아이템, 접속 임대, 최근 세션, 살아 있는 로그인 묶음 수, 초대 목록을 한 읽기 스냅샷에서 모은다. 초대 발급(`POST /admin/v1/invites {accountId(생략하면 새 계정), maxUses, expiresInHours, revokeExistingSessions}`)은 응답에 원문을 한 번만 싣고(`Cache-Control: no-store`) 같은 operationId 재전송은 같은 inviteId에 원문 null이다. 응답을 잃었으면 그 초대를 폐기하고 새로 발급한다. 기기 변경 재발급은 `revokeExistingSessions`(생략하면 `true`)로 기존 로그인 묶음과 하위 토큰, 이전 초대를 같은 트랜잭션에서 폐기한다. 강제 종료(`POST /admin/v1/accounts/{accountId}/kick {operationId, reason, revokeInvites}`)는 로그인 묶음, 접근·갱신 토큰, 게임 하위 세션, 미소비 입장 토큰을 폐기하고 `revokeInvites`(생략하면 `true`)면 미폐기 초대도 폐기한다. 응답은 `AdminAccountKickResponse{operation, accountId, invitesRevoked, currentSessionKept=true}`다. 이미 발급된 토큰과 초대만 폐기한다. 게임 서버가 쥔 계정 접속 임대는 지우지 않으므로 지금 접속 중인 마을·던전 연결은 이어지고, 폐기된 토큰의 갱신, 재접속, 새 입장 토큰 발급부터 거절된다. 0010의 아이디·비밀번호 로그인은 막지 않으므로 같은 아이디와 비밀번호로 새 토큰을 받을 수 있다. 로그인 차단은 제재(P1)다.

**로그 조회.** 로그 조회는 읽기 전용 스냅샷이다. 원장, 아이템 이력, 세션 이벤트, 작업 조회는 계정 데이터가 실리는 민감 조회라 `reason` 조회 문자열(최대 500자)이 필수이고, 데이터를 읽기 전에 감사 행(`admin.ledger.view`, `admin.item.history.view`, `admin.session.events.view`, `admin.operation.view`)을 자체 트랜잭션으로 커밋한다. 대상이 없어 404가 되는 조회도 시도로 감사하고, 원장은 페이지마다 한 행이다. 감사 로그 조회는 감사하지 않는다. 시각 조건은 UTC 유닉스 ms이며 `fromUnixMs` 포함, `toUnixMs` 제외다. 목록은 keyset `cursor`(응답의 `nextCursor`)와 `limit`(기본 100, 최대 500)로 넘긴다. 원장은 `accountId` 또는 `sourceType`+`sourceId`가 없으면 400(전체 스캔 방지)이고 `characterId`, `assetKind`, `assetId`, `reasonCode`(원장 사유 숫자, `reason`은 감사 사유)로 좁힌다. 필터 전체의 사유·자산별 합계(`totals`)는 cursor 없이 부른 첫 페이지에만 있고 다음 페이지는 null이다. 감사 로그는 `adminId`, `targetKind`(+`targetId`), 기간으로 거른다. 요청 IP는 HMAC 해시의 앞부분만 보인다.

**장애 세션 복구.** `GET /admin/v1/sessions`는 `state`가 없으면 복구 관심 대상(RecoveryPending, 자동 복구가 실패했거나 백오프 중인 Running/Settling, 정산 대기 시간이 지난 Settling, 배정 서버 생존 증거가 없는 Running)을, 있으면 그 상태의 세션을 준다. `POST /admin/v1/sessions/{sessionId}/recovery {operationId, reason, action}`의 action은 셋이다.

- `RetrySettlement`: 저장된 종료 이벤트가 있는 Settling을 자동 복구와 같은 배정 서버 actor, 같은 정산 operationId로 정산한다.
- `ServiceCancel`: 배정 서버 소실이 확인된 Running을 장애 취소한다. 서버가 살아 있으면 `INVALID_STATE`다.
- `ForceClose`: 정산이 반복 실패해 격리된 세션(`state=Settling` 이고 `recovery_failures >= 5`)을 장애 취소와 같은 보상 규칙(티켓·사용 물약 환급, 기본 정기 전액, 추가 정기 50% 내림)으로 닫는다. 운영자 작업 **한 트랜잭션**에서 종결, 원장(actor_kind `Admin`, actor_id 운영자 ID), 감사를 함께 쓴다. 결과는 `Closed`/`ServiceCancelled`이고 배정 서버는 Draining이 된다. 같은 세션 행 조건부 갱신이라 늦게 온 `/settlement`와 한쪽만 이기며, 강제 종료가 이기면 그 정산은 `ALREADY_SETTLED`다. 원장은 `GET /admin/v1/operations/{운영자 ID}/{operationId}`로 본다. 격리되지 않은 Settling이나 다른 상태는 `INVALID_STATE`다.

RecoveryPending 세션은 처리 경로가 없어 세 action 모두 `INVALID_STATE`다. 격리 카운터 `recovery_failures`를 되돌리는 API는 없다.

**생존 판정 보류.** `RecoveryLivenessGate`(싱글턴, 유지 작업과 공유)는 API 시작 뒤와 DB 단절을 확인한 뒤 30초 동안 서버 소실 판정을 보류한다. 그동안의 오래된 heartbeat로 출정을 장애 취소하지 않기 위해서다. 보류 중 새 `ServiceCancel`·`ForceClose`는 저장하지 않고 503 `BACKEND_UNAVAILABLE(810)`과 `Retry-After`(남은 초)로 거절한다. 이미 저장된 어드민 operationId의 재전송은 보류와 무관하게 처음 결과다. 복구 목록은 응답에 `livenessDeferred`를 싣고 보류 중에는 각 세션의 `instanceLost`를 false로 둔다. 보류는 프로세스 메모리 값이라 API 인스턴스마다 따로 판정한다.

`RetrySettlement`·`ServiceCancel`은 **두 트랜잭션**이다. 정산과 장애 취소는 늦게 온 `/settlement`나 다음 자동 복구와 겹쳐도 한 번만 적용되도록 actor(서버), operationId, 본문이 자동 복구와 같아야 하고, 그 작업이 자기 operations 행과 트랜잭션을 연다. 그래서 순서는 "의도 감사 행(`admin.session.recovery.intent`: 운영자, 사유, 어드민 operationId, 복구 작업 actor·operationId) 선커밋 → 기존 `PostgresSettlementRepository` 경로로 복구 확정 → 어드민 작업과 감사 행 확정"이다. 마지막 단계가 실패해도 의도 행이 남아 감사 없는 종결이 생기지 않는다. 감사 행의 after_state에 실제 복구 작업의 actor, operationId, 영수증과 `alreadyApplied`를 남긴다. `alreadyApplied`는 세션 상태가 아니라 복구 작업(서버 actor, 고정 operationId)이 이번 호출 전에 이미 저장돼 있었는지다. 이미 저장된 어드민 operationId는 복구를 다시 부르지 않는다. 복구는 확정됐는데 어드민 단계가 실패(503 등)해 같은 operationId로 다시 오면, 세션이 이 action이 만드는 종결 상태일 때만 기존 경로를 다시 불러 저장된 결과를 받고 감사한다. 다른 경로로 종결된 세션은 `INVALID_STATE`다. 저장 여부 검사와 서버 복구가 어드민 operations 행보다 먼저라, 세 action 모두 시작할 때 운영자 작업 키를 action, 대상 세션, 요청 해시에 묶는 `admin_operation_claims` 행을 먼저 커밋한다(`INSERT ... ON CONFLICT DO NOTHING` 뒤 읽어 비교). 일반 쓰기도 같은 결속을 먼저 커밋하므로 일반 쓰기와 복구가 같은 키로 경합해도 최초 요청만 효과를 실행한다. 결속 도입 전에 저장된 operations의 요청 해시도 확인한다. 묶인 값과 다르면 서버 복구 없이 저장하지 않는 409 `IDEMPOTENCY_CONFLICT`다. 같으면 트랜잭션 advisory 잠금(운영자, operationId)과 행위자 행 `FOR SHARE`를 끝까지 쥐어 같은 키의 실행을 직렬화하고, 행위자가 비활성이거나 역할이 낮아졌으면 서버 복구를 부르지 않는다(어드민 작업이 `FORBIDDEN_ROLE`을 저장). 같은 키의 앞 요청이 잠금 제한(2초)보다 오래 걸리면 뒤 요청은 503이며 같은 operationId로 다시 보내면 된다. 장애 보정은 `POST /admin/v1/grants`에 `sourceKind=RecoveryCompensation`, `sourceRef=세션 ID`로 따로 지급한다(실행은 Admin 이상, 세션당 하나).

**명령줄 도구.** `Backend/src/Shinshi.AdminCli`는 어드민 API만 호출하고 DB에 직접 붙지 않는다(ADR02). 사용법은 [Shinshi.AdminCli README](src/Shinshi.AdminCli/README.md)를 따른다.

**남은 위험과 결정.**

- 0008의 지급·조정·수령 코드가 0009의 `currency_reserved` 열에 의존한다. 0008과 0009는 같은 커밋·배포에 함께 넣는다.
- 계정 강제 종료(kick)는 현재 접속을 유지하고 비밀번호 로그인을 막지 않는다. 계정 접속 임대는 서버 heartbeat가 계속 연장하고, 같은 아이디와 비밀번호로 새 토큰을 받을 수 있다. 로그인 차단과 즉시 차단은 제재(P1)나 서버 쪽 연결 종료 경로가 필요하다.
- 어드민 쓰기는 커밋까지 행위자 행을 `FOR SHARE`로 잡고, 수동 복구는 서버 복구 동안 잡는다. 그 운영자의 비활성화나 로그인은 그동안 기다리고, 잠금 제한(2초, 실행기 재시도 3회)을 넘으면 503이다.
- 운영자 작업 키 결속은 모든 업무 변경이 효과 전에 공유한다. 일반 쓰기가 진행 중인 경우와 복구가 먼저 결속한 경우 모두 다른 본문의 같은 operationId는 효과 없이 거절한다. 실제 PostgreSQL 회귀 테스트에서 복구의 결과 키도 보존되는 것을 확인했다. 일시 실패한 최초 요청도 키 결속은 남으므로 같은 본문으로 재시도하고, 다른 업무는 새 operationId를 쓴다.
- 세션 강제 종료(`ForceClose`)는 원래 정산안 대신 장애 취소 보상을 적용한다. 원래 보상과 차이가 있으면 운영자가 `RecoveryCompensation` 보정 지급으로 메운다.
- 기능 차단 중에 처음 보낸 operationId는 저장되지 않으므로 차단이 풀린 뒤 같은 ID로 다시 오면 실행된다. 클라이언트가 차단 중 요청을 계속 재시도하면 해제 직후 실행될 수 있다.
- 생존 판정 보류는 프로세스별이다. 한 인스턴스만 DB 단절을 겪었으면 다른 인스턴스의 수동 취소는 막히지 않는다. 풀 전체 heartbeat 공백은 장애 취소 SQL이 단절로 보고 거절한다.
- 구조가 손상된 보상 JSON의 우편은 목록에서 빠지지만 그 우편의 수령과 어드민 조회는 500이다.
- 로그 조회 감사가 요청(페이지)마다 한 행이라 큰 원장을 넘겨 보면 감사 행이 페이지 수만큼 쌓인다.
- 어드민 로그인은 실패 응답을 하나로 통일했지만 없는 로그인 이름과 있는 이름의 처리 시간 차이로 이름 존재를 추정할 수 있다. 없는 이름도 더미 해시를 계산하지만, 있는 이름만 실패 횟수 갱신과 잠금 판정 트랜잭션을 한 번 더 거친다.
- 점검 중 우편 수령을 허용할지 정하지 않았다(현재 허용, `inbox_claim`으로만 막음).
- 입장 토큰 교환은 DB 점검이 아니라 `ServiceState__Maintenance` 설정을 읽는다. 운영 설정을 `false`로 두지 않으면 DB에서 점검을 풀어도 마을·던전 입장이 503 `MAINTENANCE`다(`ServiceStateOptions`의 TODO, 시작 경고 있음).
- 지급·조정 수량 상한 1,000,000은 임시값이다.
- 실제 PostgreSQL 테스트를 병렬로 돌리면 `SqlMigrationRunner`의 전역 advisory 잠금(2초 대기)에서 경합해 간헐적으로 실패할 수 있다. 실패하면 단독으로 다시 돌려 확인한다.
- 운영 실행(실제 배포 환경의 로그인, 허용 네트워크, 프록시 뒤 IP 판정)과 명령줄 도구 조작은 검증하지 않았다.


## 게임 하위 세션 (런처 -> 게임)

런처가 게임을 실행하기 직전에 받는 게임 전용 토큰이다(`GameSessionController`). 모든 응답은 `Cache-Control: no-store`이고 자격 교환 요청 한도(IP별 1분 60회)를 받는다. 플레이어 경로의 bearer 검증은 접근 토큰 표에 없는 우리 토큰을 게임 하위 세션으로 다시 본다(`EPlayerTokenKind`).

| 경로 | 인증 | 결과 |
|---|---|---|
| `POST /v1/auth/game-session` `{build}` | 접근 토큰만 | 200 `GameSessionIssueResponse`(15분, 부모 갱신 토큰 만료를 넘지 않음). 게임 하위 세션 bearer는 403 `SESSION_MISMATCH`, 빌드·점검 거절은 403, 부모 폐기는 401 |
| `POST /v1/auth/game-session/refresh` | 게임 하위 세션만 | 200 새 하위 세션, 이전 토큰 즉시 폐기. 같은 토큰의 동시 회전은 하나만 성공. 접근 토큰은 403 |
| `POST /v1/auth/game-session/revoke` | 공개(본문 없음, bearer로 하위 세션 전달) | 204. 이미 폐기·만료된 하위 세션도 204. 다른 용도의 우리 토큰은 401, 위조 bearer는 IP 실패 한도에 센다 |

마을 입장권(`POST /v1/town/join-ticket`)과 복귀 입장권(`POST /v1/game-sessions/{sessionId}/rejoin-ticket`)은 게임 하위 세션 토큰만 받고 접근 토큰은 403 `SESSION_MISMATCH`다(형식 검사 뒤, 저장소 호출 전). 홈페이지 로그인은 빌드 없이 접근 토큰을 받을 수 있으므로 빌드 검사를 거친 게임 세션만 게임 서버 입장 토큰을 받게 한다.

## 계정 가입, 아이디·비밀번호 로그인, 캐릭터 이름 (0010)

설계는 `docs/design/account-and-website.md`다. 코드는 `Shinshi.Api/Accounts/`, `Shinshi.Api/Characters/`, `Shinshi.Data/Accounts/`, `Shinshi.Data/Characters/`, `PostgresAuthRepository.Accounts.cs`이고 등록은 `AddShinshiAuth`가 부르는 `AddShinshiAccounts`다. 형식 규칙은 Unity와 공유하는 `AccountCredentialRules`, `CharacterNameRules`(Contracts)에 있다.

| 경로 | 인증 | 결과 |
|---|---|---|
| `POST /v1/accounts` `{loginId, password, email}` | 공개 | 201 `AccountSignupResponse`(토큰 없음). 400 `INVALID_LOGIN_ID(1202)`·`WEAK_PASSWORD(1204)`·`INVALID_EMAIL(1203)`, 409 `LOGIN_ID_TAKEN(1200)`, 429 `RATE_LIMITED`, 503 `MAINTENANCE` |
| `GET /v1/accounts/login-id-availability?loginId=` | 공개 | 200 `AvailabilityResponse`(형식 오류 `INVALID_LOGIN_ID`, 사용 중 `LOGIN_ID_TAKEN`). IP별 1분 30회 |
| `POST /v1/auth/exchange` `{loginId, password, build?}` | 공개 | 200 `AuthExchangeResponse`. 초대·갱신·아이디 비밀번호 중 정확히 하나(아니면 400). `build`가 없으면 점검만, 있으면 기존 빌드 검사(403). 실패 401 `INVALID_CREDENTIALS(1205)`, 잠금 429 `ACCOUNT_LOCKED(1206)`, 늦춤·해시 대기 429 `RATE_LIMITED` |
| `GET /v1/accounts/me` | 접근 토큰 또는 게임 하위 세션 | 200 `AccountProfileResponse`. 초대 코드 계정은 `loginId`, `email`이 null |
| `POST /v1/accounts/credentials` | 접근 토큰만(게임 하위 세션은 403 `SESSION_MISMATCH`) | 204. 일반 변경은 `{currentPassword, newPassword}`이고 현재 비밀번호 재확인이 필수다(틀리면 403 `INVALID_CREDENTIALS`, 로그인과 같은 실패 제한). `currentPassword` 없는 변경은 초대(복구 코드) 교환으로 시작한 로그인 묶음에서 시작 30분 안에만 되며(아니면 403 `INVALID_CREDENTIALS`), 아이디가 없는 계정은 `{loginId, email, newPassword}`, 있는 계정은 `{newPassword, email?}`다. 성공하면 같은 트랜잭션에서 호출한 묶음을 포함한 계정의 모든 로그인 묶음, 게임 하위 세션, 입장 토큰, 남은 초대를 폐기하므로 새 자격으로 다시 로그인한다. 409 `LOGIN_ID_TAKEN`·`INVALID_CREDENTIALS`(동시 변경) |
| `GET /v1/characters/name-availability?name=` | 플레이어 | 200 `AvailabilityResponse`(`INVALID_CHARACTER_NAME(1002)`, `CHARACTER_NAME_TAKEN(1001)`). IP별 1분 30회 |
| `POST /v1/characters` `CharacterCreateRequest` | 플레이어 | 200 확정, 409 거절(`CHARACTER_NAME_TAKEN`, `NO_CAPACITY`, `IDEMPOTENCY_CONFLICT`), 본문 `CharacterCreateResponse`. 400은 형식 오류이며 작업으로 저장하지 않는다 |

**비밀번호.** PBKDF2-HMAC-SHA256 600,000회, 16바이트 솔트, 32바이트 결과를 `pbkdf2-sha256$회수$솔트$해시`(base64)로 저장하고 행의 회수로 검증한다. `Accounts__PasswordIterations`는 테스트 호스트만 낮춘다(최소 1000). 비교는 고정 시간이고, 없는 아이디도 현재 기본 회수의 더미 해시를 계산한다(테스트는 PBKDF2 계산 수와 회수를 센다). 로그인에 성공했는데 저장 해시의 회수가 현재보다 낮으면 같은 비밀번호를 새 회수로 해시해 발급 트랜잭션에서 바꾼다. 형식이 틀린 아이디는 존재할 수 없어 해시 없이 401이다. 해시는 DB 트랜잭션 밖에서 계산하고, 동시 계산은 `PasswordHashGate`가 CPU 코어 수로 제한한다. 대기는 코어 수의 2배까지만 받고 넘으면 바로, 대기 2초가 지나면 429 `RATE_LIMITED`(`Retry-After: 2`)이며 요청이 취소되면 대기에서 빠진다. 로그인 발급과 비밀번호 변경은 계정 행 잠금 아래에서 검증한 해시가 그대로인지 다시 본다. 로그인 묶음에는 시작 수단(`auth_token_families.origin`: `invite`, `password`)을 적고 갱신 회전은 같은 묶음을 이어 쓴다.

**실패 제한.** `PasswordLoginLimiter`(프로세스 메모리)는 bearer 위조를 세는 `AuthFailureLimiter`와 따로 센다. (아이디, IP) 쌍 5회 연속 실패면 잠금 만료를 지금+15분으로 두고 그 쌍만 429 `ACCOUNT_LOCKED`다. 잠금 중에는 해시를 계산하지 않고 만료도 늘리지 않으며, 만료가 지나면 실패 수를 0으로 한다. 같은 쌍의 동시 시도는 쌍 차례(최대 2초 대기, 넘으면 `RATE_LIMITED`)로 판정, 해시, 기록을 하나씩 한다. 같은 아이디의 최근 1시간(미끄럼 창) 실패 합계가 50회를 넘으면 그 아이디의 시도를 IP마다 1분 1회로 늦춘다. 성공하면 그 쌍의 실패 수를 0으로 한다. 키는 마지막 사용 뒤 1시간이 지나면 지우고, 쌍과 아이디 키 합계가 10만 개를 넘으면 오래 쓰지 않은 것부터 9만 개까지 줄인다. 비밀번호 변경의 현재 비밀번호 확인도 같은 제한을 받는다. 가입은 형식 검사를 통과한 시도만 IP당 1시간 5회다. API를 여러 대로 늘리면 카운터를 공유 저장소로 옮겨야 한다. IP는 forwarded headers 처리 뒤 값이므로 NAS 역방향 프록시를 `ForwardedHeaders__KnownProxies__*`에 넣어야 방문자별로 센다.

**캐릭터.** 이름은 앞뒤 공백 제거 -> NFKC -> 2~12자의 완성형 한글·영문·숫자 검사 -> 표시 이름 `name` -> `ToLowerInvariant` 비교 키 `name_key`(유일 인덱스) 순서다. 생성은 계정 actor의 `character.create` 멱등 작업이며 영수증 ID가 캐릭터 ID다. 계정 행을 잠근 뒤 개수 상한(`MAX_CHARACTERS_PER_ACCOUNT = 3`, 기획 미정 임시값, 전용 코드가 없어 `NO_CAPACITY`)을 보고 `INSERT ... ON CONFLICT (name_key) DO NOTHING`으로 넣는다. 동시에 같은 키를 넣으면 먼저 커밋한 쪽만 남고 나머지는 `CHARACTER_NAME_TAKEN`으로 저장된다. 외형은 `appearance` jsonb로 저장하며 직업별 헤어·팔레트 카탈로그 검사와 초기 지급은 아직 없다.

**0010 스키마.** `account_credentials`(account_id PK/FK, 소문자 login_id 유일 + 형식 CHECK, password_hash 형식 CHECK, email, email_key, email_verified, created_at, updated_at, `email_verified`인 행만의 email_key 부분 유일 인덱스)와 `characters.name`, `name_key`, `appearance`, `auth_token_families.origin`(`invite`/`password`, 기존 행은 모두 초대 교환으로 만들었으므로 `invite`로 채운 뒤 기본값을 지워 새 묶음은 시작 수단을 반드시 적는다)을 더한다. 기존 캐릭터 행에는 채울 이름이 없어 세 열을 NULL 허용으로 두었다. NULL끼리는 유일 인덱스에서 충돌하지 않고, 이름이 있으면 형식 CHECK와 `name_key = lower(name)`을 지킨다. 임시 이름을 채워 NOT NULL로 만드는 방식은 임시 이름끼리, 또는 나중 실제 이름과 겹칠 수 있고 이름 없이 행을 넣는 기존 코드와 테스트를 깨므로 택하지 않았다. 0010 down은 모든 아이디, 비밀번호 해시, 이메일, 캐릭터 이름을 지우므로 운영 DB에서 실행하지 않는다. 런타임 권한은 위 권한 SQL의 0010 줄이다.

`GET /v1/characters`는 접근 토큰 또는 게임 하위 세션으로 인증된 계정의 `CharacterListResponse`를 반환한다. 계정 ID는 요청에서 받지 않으며 응답은 캐시하지 않는다. Bootstrap 런처 접속 화면에서 캐릭터 선택에 사용한다.

**남은 일.** 인증 메일(그 전까지 `emailVerified=false`, `EMAIL_TAKEN`은 번호만 예약), 비밀번호 찾기, 캐릭터 이름 금칙어, 계정당 캐릭터 수는 기획 미정이다. 테스트는 `tests/Shinshi.Backend.Tests/Accounts/`에 있다.
