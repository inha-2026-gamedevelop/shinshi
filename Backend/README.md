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
-- 0017: 경험치 원장에는 수정·삭제 권한을 주지 않는다
GRANT SELECT, INSERT ON TABLE public.character_experience_events TO shinshi_runtime;
-- 0018: 옵션 재설정 누계. 행을 지우지 않는다. equipment_instances의 옵션 열과 growth_receipts.roll_values는 기존 테이블 권한을 따른다
GRANT SELECT, INSERT, UPDATE ON TABLE public.option_reroll_pity TO shinshi_runtime;
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
-- 0011: 성장, 장비, 초기 지급, 계정 진행, 일일 대사. 지급 기록, 해금 기록, 성장 영수증은 추가 전용이다
GRANT SELECT, INSERT, UPDATE ON TABLE
    public.talisman_progress, public.character_training, public.account_progress, public.economy_reconciliation_runs
    TO shinshi_runtime;
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE
    public.talisman_loadout, public.equipment_instances, public.enhancement_pity TO shinshi_runtime;
GRANT SELECT, INSERT ON TABLE public.initial_grants, public.account_unlocks, public.growth_receipts TO shinshi_runtime;
-- 0012: 30일 보상 회수함 줄. 수령은 남은 수량만 줄이고 행을 지우지 않는다. 새 열(캐릭터 삭제, 출정 계약, 이탈)은 기존 테이블 권한을 따른다
GRANT SELECT, INSERT, UPDATE ON TABLE public.reward_inbox_lines TO shinshi_runtime;
-- 0015: 유저 경매장 등록, 1:1 거래 실행. 성사 기록은 추가 전용이다. equipment_instances의 새 열(escrow_kind, escrow_ref)은 기존 테이블 권한을 따른다
GRANT SELECT, INSERT, UPDATE ON TABLE public.market_listings, public.player_trade_executions TO shinshi_runtime;
GRANT SELECT, INSERT ON TABLE public.market_trades TO shinshi_runtime;
-- 0019: 소셜. 해체한 길드 행은 남긴다. account_leases의 새 열(character_id)은 기존 테이블 권한을 따른다
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE
    public.friendships, public.friend_requests, public.guild_members, public.guild_join_requests, public.social_deliveries
    TO shinshi_runtime;
GRANT SELECT, INSERT, UPDATE ON TABLE public.guilds TO shinshi_runtime;
-- 0023: 성장 메뉴 안내 완료와 프롤로그 완료 표식. 둘 다 고치거나 지우지 않는다. characters의 새 열(prologue_*)은 기존 테이블 권한을 따른다
GRANT SELECT, INSERT ON TABLE public.account_menu_guides, public.prologue_completions TO shinshi_runtime;
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

접근·갱신·게임 하위 세션·서버 임대·참가자 접속 토큰은 서로 다른 HMAC 용도를 가진 opaque bearer다. 갱신은 계정→토큰 계열 순서로 잠근 뒤 이전 토큰을 폐기하고 새 토큰을 발급한다. 갱신 요청은 선택 필드 `refreshRequestId`(소문자 GUID N, 빈 GUID 금지)를 받는다. 클라이언트가 요청 전에 토큰과 ID를 영속화하면 응답 유실·재시작 후 같은 쌍으로 재시도할 수 있다. 서버는 `0022_refresh_rotation_retry`의 `rotation_request_hash`와 HMAC으로 최초 발급한 토큰을 재구성해 원래 만료 시각으로 반환한다. 원문 토큰이나 요청 ID는 DB에 저장하지 않는다. 재시도는 만료를 연장하거나 게임 세션을 다시 폐기하지 않는다. 이미 다음 세대로 회전한 요청의 재시도는 거절하되 새 세대를 폐기하지 않는다. 다른 ID 또는 ID 없는 소비 토큰 재사용은 계열 전체(접근/갱신/하위 세션/접속 토큰)를 폐기한다. 구형 클라이언트는 계속 동작하지만 응답 유실 복구는 지원하지 않는다. 이 ID는 기기 인증이 아니며 토큰과 ID가 함께 유출된 경우 정상 재시도와 구별할 수 없다. 서버 HMAC 키는 재시작 후 유지해야 한다. 일반 회전 시 접근 토큰은 기존 15분 만료까지 유지되지만 이전 갱신 토큰의 하위 게임 세션은 폐기된다. 기기 유실 복구는 신뢰된 운영 경로에서 `IssueInviteAsync`의 기본 `revokeExistingSessions: true`로 새 초대를 만들고 기존 계열을 폐기한다.

`ServerCredentialIssuer`는 `POST /v1/server-instances/lease`(아래 0005 절)에서만 쓰며 공개 플레이어 발급 엔드포인트를 제공하지 않는다. `ServerCredentials__Registrations__*__ServiceId/InstanceId/Role/CredentialHashBase64`는 환경에서 고정한다. 서비스 자격은 별도 256비트 무작위 원문이고 설정에는 그 UTF-8 원문의 SHA256 Base64만 둔다. 일치 비교는 고정 시간이며 ID/역할은 입력 본문 대신 등록 설정에서 정한다. `ServerCredentials__LeaseLifetimeSeconds` 기본 900초, 최대 24시간이다. 발급은 역할과 bootId를 검사하고, 임대 검증은 만료, 폐기, Offline으로 확정된 같은 boot만 거절한다(0005 절). 같은 인스턴스의 새 임대는 이전 임대를 폐기하므로 갱신한 서버는 새 토큰으로 바로 바꾼다. 접속 토큰 발급과 교환은 해당 인스턴스의 현재 살아 있는 임대를 검사하므로 출정의 과거 `server_lease` 값으로 정상 갱신을 막지 않는다.

`IssueJoinTokenAsync`는 플레이어 자격을 사용하는 내부 발급 경로다. 예약의 `JoinTokenIssuer`는 서버 자격으로 계정 ID의 활성 게임 인증 family를 찾는 `IssueDungeonJoinTokenAsync`에 연결한다. 마을 서버 임대, 예약한 마을, 참가자 roster, 계정/캐릭터 소유, 현재 출정의 인스턴스와 역할, 살아 있는 던전 임대를 DB에서 재검증한다. 활성 family가 없으면 `TOKEN_REVOKED`다. 토큰은 최대 60초이며 예약 만료와 인증 계열 만료를 넘지 않는다.

서버의 접속 토큰 교환은 `POST /v1/game-servers/join-tokens/exchange` 한 경로에서 공유 `JoinTokenExchangeRequest/Response`를 사용한다. 요청은 `{joinToken, instanceId, serverRole}`다. 응답에는 신원·출정·서버 시각·계정 임대·직업 외에도 닉네임/외형, 경험치/순번, 전투 구성과 남은 물약 등이 있으며 정확한 필드는 공유 DTO를 따른다. `classId`는 토큰이 발급된 캐릭터의 직업(`ContractCodes.PlayerClass`, 0 무사·1 도술사·2 신령사·3 검사·4 마검사·5 궁수, 생성 허용은 `Characters:EnabledClassIds` 기본 `3,4,5`로 별도 제한)으로, 마을과 던전 입장 모두 같은 교환이 같은 트랜잭션에서 `characters.class_id`를 읽어 채운다. 게임 서버는 클라이언트가 아니라 이 값으로 전투 개체의 직업을 정하며, 키가 없거나 모르는 값이면 입장을 거절한다. `Authorization: Bearer <서비스 자격으로 발급된 서버 임대 토큰>`과 `X-Shinshi-Instance-Id: <등록 인스턴스 UUID>`를 함께 보낸다. 헤더와 본문 인스턴스/역할을 검증된 자격에 대조하고 참가자/출정/캐릭터는 DB 토큰에서만 읽는다. 토큰은 한 번 소비하며 응답을 잃은 서버는 같은 토큰 교환을 재시도하지 않는다. 이전 `/v1/auth/join/consume` 경로는 제공하지 않는다. Unity의 `ServerBootstrap`은 `ApiJoinTokenVerifier`와 `BackendGatewayPump`를 연결해 결과를 메인 스레드에서 전달한다.

원장 쓰기는 `IdempotentOperationExecutor` 효과 콜백에서 받은 `OperationExecutionContext`로만 호출한다. 활성 PostgreSQL 연결과 같은 트랜잭션인지 검사하고 source operation이 Pending인지 확인한다. `PostgresEconomyLedgerWriter.AppendAsync`, `PostgresItemHistoryWriter.AppendAsync`, `PostgresOutboxWriter.EnqueueAsync`를 실제 잔액/재고 변경과 같은 callback에 넣는다. 업무 거절은 effect savepoint, 예외는 전체 트랜잭션을 롤백하므로 원장/장비 이력/outbox도 함께 롤백된다. 원장 중복 키 `(actor_id, operation_id, entry_key)`, 장비 이력 `(item_id, seq)`, outbox `event_id`가 같은 내용의 재기록을 허용하고 다른 내용은 거절한다. 원장은 직접 잔액을 바꾸지 않으므로 모든 경제 변경 담당자가 이 공용 경로를 연결해야 한다. 장비의 삭제 이력은 item_id 외래 키 없이 남겨 삭제 뒤에도 보존한다.

`EEconomyReason`과 `ContractCodes.EconomyReason`은 동일 숫자의 공유 사유 계약이다. 세션의 다섯 사유는 `SessionStartTicket=1`, `SessionPotionUse=2`, `SessionSettlement=3`, `ServiceCancelRefund=4`, `ServiceCancelEssence=5`다. 그 뒤에는 운영 도구 설계의 경제 사유를 추가했다. `LedgerWriter`는 세션 원장 문자열을 이 숫자로 바꾸고 원래 entry key를 보존하며 각 행에 새 ledger UUID를 부여한다. 원장에 기존 별도 사유 enum은 없었다. 테스트의 `TEST_REASON_CODE`는 테스트 전용 값이다.

`PostgresOutboxDispatcher.ProcessBatchAsync`는 같은 DB에 있는 소비자 전용 전달 작업이다. Committed 원본 operation의 미전달 행을 `FOR UPDATE SKIP LOCKED`로 최대 100개 잠근다. 소비 inbox `(consumer_key, event_id)` 유일 키, 짧은 DB 효과, `delivered_at`를 한 트랜잭션에서 확정한다. 소비 실패와 복원 가능한 PostgreSQL statement/lock timeout은 항목 savepoint로 효과/inbox를 되돌리고 1초 뒤 재시도한다. 직렬화 실패·데드락·드라이버 연결 타임아웃·호출자 취소/프로세스 소실은 배치 전체를 롤백한다. 해당 콜백에 네트워크/RPC/외부 I/O를 넣지 않는다. 외부 전송을 연결할 부모 전달자는 영속 claim 임대와 수신 측 동일 event_id 중복 소비 계약을 추가해야 한다. 지금 API는 background dispatcher를 자동 시작하지 않으므로 부모가 소비 종류별 callback과 주기를 등록한다. `EconomyLedgerReconciler`는 currency/tickets/essence/shards/account_items의 현재 잔액과 원장 numeric 합계를 읽기 전용 keyset 페이지로 비교한다. 초기 잔액도 원장에 기록해야 하며 일일 호출과 불일치 ERROR 보고는 `EconomyReconciliationService`가 맡고 `GrowthServiceCollectionExtensions`에서 호스티드 서비스로 등록한다. 운영 실행은 미검증이다.

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

`POST /v1/game-sessions/{sessionId}/rejoin-ticket`은 게임 하위 세션 토큰으로 부르며(접근 토큰은 403 `SESSION_MISMATCH`, 아래 "게임 하위 세션" 절) 응답은 `JoinTicket`이다. 출정 Running, 요청자 계정의 참가자 Active, 로그인 묶음이 살아 있을 때만 60초 일회 토큰을 발급하고 같은 참가자의 미교환 토큰은 폐기한다. 시작 전은 409 `WRONG_PHASE`, 다른 계정은 403 `SESSION_MISMATCH`, 정산된 참가자는 409 `ALREADY_SETTLED`다. 마을 입장 토큰은 `PostgresAuthRepository.IssueTownJoinTokenAsync`(플레이어 주체, 캐릭터, 마을 인스턴스)로 발급하며 HTTP 발급 경로는 `POST /v1/town/join-ticket`(`TownJoinTicketController`)이다. 교환 응답의 `participantId`, `sortieId`는 null이다. 다른 살아 있는 서버에 계정 임대가 있으면 `ALREADY_CONNECTED`다.

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

체험판 어드민은 API·명령줄 도구와 2026-10-07 추가한 운영 웹(`Shinshi.Admin.Web`)을 제공한다. 웹도 어드민 API만 호출한다. 경로는 `/admin/v1` 아래이고 상수는 `Shinshi.Contracts.Admin.AdminApiRoutes`, DTO와 숫자 코드는 같은 이름 공간(`AdminContractCodes`)에 있다. Unity 공유 계약에는 넣지 않는다. 컨트롤러는 `Shinshi.Api/Admin/`(이름 공간 `Shinshi.Backend.Admin`), 데이터 계층은 `Shinshi.Data/Admin/`이다.

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

**0008 테이블.** `admin_users`(소문자 login_name, PBKDF2 해시와 솔트, AES-GCM TOTP 암호문, role 0~3, 실패 횟수, 잠금 시각, 마지막 TOTP 스텝), `admin_sessions`(어드민 토큰 HMAC), `admin_audit_log`(추가 전용, 0003의 `reject_append_only_mutation` 트리거가 UPDATE/DELETE/TRUNCATE를 거절, operation_id가 있으면 같은 운영자의 operations 행을 FK로 가리킴), `admin_grants`, `reward_inbox`(`(grant_id, account_id)` 유일, 수령과 취소 상호 배타, 수령 operation과 캐릭터 소유 FK), `service_state`(id=1 단일 행, 0008이 점검 중과 빈 목록으로 넣음), `service_notices`, `admin_operation_claims`(운영자 작업 키 `(admin_id, operation_id)`를 작업 종류, 대상, 요청 해시에 묶는 추가 전용 결속, 일반 업무 변경과 수동 복구가 효과 전에 커밋). `admin_grants`의 부분 유일 인덱스 `ux_admin_grants_recovery_ref`는 취소되지 않은 `RecoveryCompensation` 지급의 `source_ref`(복구 대상 세션 ID)를 유일하게 한다. 0008은 어드민 조회용으로 기존 테이블에도 인덱스 넷을 더한다: `session_participants(account_id, session_id)`, `economy_ledger(account_id, occurred_at, ledger_id)`, `game_sessions(state, created_at, session_id)`, `admin_audit_log(target_kind, occurred_at, audit_id)`. 0008의 지급·조정 코드는 0009 경매 마이그레이션의 `account_wallets.currency_reserved` 열을 읽으므로 두 마이그레이션은 함께 배포한다. 0008 작성 당시 작업 키 결속과 빈 대상 키(생성 작업)를 반영해 수정했다. 현재 0008·0009는 저장소에 추적되며 대상 DB의 적용 여부는 마이그레이션 이력으로 확인한다. 이전 0008을 이미 적용한 DB의 DDL은 자동 갱신되지 않으며, 마이그레이션 실행기는 체크섬 불일치로 중단한다. 기존 DB는 별도 전환 절차가 필요하고 이 작업에서는 재생성하거나 변경하지 않았다. 검증은 최신 내장 SQL을 매번 적용하는 GUID 임시 테스트 스키마에서 수행했다. 0008 down은 검토용이며 이 테이블을 모두 지우므로 운영 DB에서 실행하지 않는다. 테스트는 `tests/Shinshi.Backend.Tests/Admin/`와 `tests/Shinshi.Backend.Tests/Inbox/`에 있다.

**캐릭터 프로필.** `GET /admin/v1/characters/{characterId}?reason=`(Viewer 이상)은 `admin.character.view` 감사를 단독 커밋한 뒤 한 REPEATABLE READ 읽기 전용 트랜잭션의 고정 12개 SELECT 묶음으로 조회한다. 없는 대상도 감사 후 404다. 이름 검색 응답에는 기존 필드에 `matchedCharacterId`, `matchedCharacterName`, `matchedCharacterClassId`를 추가한다. 생성 시각은 characters에 열이 없어 `createdAtUnixMs: null`, 직업 표시명과 완전한 전투 계산 콘텐츠가 없어 `classDisplayName`·`combatStats`는 null이다. `appearanceJson`은 저장된 jsonb의 문자열이고, 최고 강화는 현재 보유 장비의 최댓값이다. `wallet.actionValue`는 저장값, `life.actionValue`는 DB 스냅샷 시각에 기존 생활 규칙으로 회복한 값이다. 수련 행이 없으면 기존 성장 조회와 같은 0단계·상한 40/100을 제공하고 지갑 행이 없으면 wallet은 null이다. 계정 진행 행이 없으면 진행 필드는 null이고 명시 해금 배열은 유지하며, 없는 생활 재고를 임의 생성하지 않는다.

**엔드포인트.** 경로 상수는 `AdminApiRoutes`, 플레이어 우편함은 공유 `ApiRoutes.INBOX`·`INBOX_CLAIM`이다. 최소 역할은 컨트롤러와 액션의 `RequireAdminRole` 중 높은 값이다.

| 경로 | 메서드 | 최소 역할 | 비고 |
|---|---|---|---|
| `/admin/v1/auth/login` | POST | 없음(허용 네트워크 안) | 실패는 원인 구분 없이 401 `INVALID_CREDENTIALS(9001)` |
| `/admin/v1/auth/logout` | POST | Viewer | 현재 어드민 세션만 폐기 |
| `/admin/v1/operators` | GET, POST | Owner | 생성 응답에만 초기 비밀번호·TOTP 비밀 |
| `/admin/v1/operators/{adminId}/disable` | POST | Owner | 대상의 열린 세션 폐기. 마지막 활성 Owner는 거절 |
| `/admin/v1/accounts?query=` | GET | Viewer | UUID(계정·캐릭터 ID)만. 그 밖은 400 |
| `/admin/v1/accounts/search` | POST | Viewer | 본문 `{query}`(UUID/초대 코드) 또는 `{characterName, includeDeletedCharacters?}`(이름 정확 검색, 기본 삭제 제외). 이름과 초대 원문은 이 POST 본문으로만 받는다 |
| `/admin/v1/accounts/{accountId}?reason=` | GET | Viewer | 민감 조회. `reason` 필수, 감사 행을 먼저 커밋한 뒤 읽음 |
| `/admin/v1/characters/{characterId}?reason=` | GET | Viewer | 캐릭터 프로필. 필수 사유의 단독 감사를 먼저 커밋하고 수련·장비·부적·계정 진행·생활·지갑·임대·최근 출정 5개를 읽기 스냅샷으로 조회 |
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
| `/admin/v1/auctions?state=&cursor=&limit=` | GET | Viewer | 경매 단계 출정. state 없으면 멈춘 경매(기한 + 60초 유예 경과 또는 서버 소실), `Open`/`Closing`이면 그 품목 상태. 진행 중 품목, 예약 엽전, 품목 마감, `livenessDeferred` |
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

**기능 차단.** 키는 `ServiceFeatureKeys`의 `auction`, `crafting`, `enhance`, `inherit`, `gathering`, `boss_entry`, `inbox_claim`, `dungeon_entry`, `market`, `trade`, `talisman`, `training`이며 목록 밖 키는 저장에서 400이다. 진입점은 싱글턴 `ServiceFeatureGate`를 받아 `CheckFeatureAsync(ServiceFeatureKeys.X)`가 `FEATURE_DISABLED(1100)`이면 효과와 operations 저장 전에 409로 거절한다. 게이트 거절 자체는 저장하지 않는다. 막는 동안 같은 operationId 재전송은 `IdempotentOperationExecutor.FindStoredAsync`로 저장된 처음 결과를 돌려주고, 저장된 결과가 없으면 차단 거절이다. 그래서 차단 중에 처음 보낸 operationId는 차단이 풀린 뒤 다시 보내면 새로 실행된다. 철자가 틀린 키는 예외다. 이미 시작한 진행(정산, 경매 마감 등)은 막지 않는다. 현재 연결된 곳은 새 출정 예약(점검이면 `MAINTENANCE`, `dungeon_entry` 또는 보스전의 `boss_entry`가 꺼졌으면 `FEATURE_DISABLED`, `CheckNewSortieAsync`), `POST /v1/inbox/claim`(`inbox_claim`), 생활 채집(`gathering`)과 물약 제작(`crafting`), 경매 입찰(`auction`, 개시, 마감, 단계 종료는 막지 않음)이다. 강화(`enhance`)·계승(`inherit`)·장비 제작(`crafting`)·옵션 재설정(`reroll_options`)은 `TownEquipmentController`에 연결돼 있다. 유저 경매장(`market`)·개인 거래(`trade`)도 각 컨트롤러에서 검사한다.

**운영자 지급과 우편함.** 지급은 `admin_grants` 한 건이다. 작성(`POST /admin/v1/grants {targetAccountIds 1~1000, rewards 1~50줄, claimDays 1~365(기본 30), sourceKind, sourceRef}`)은 Draft만 만들고 대상 계정이 하나라도 없으면 `NOT_FOUND`로 전체를 거절한다. 실행은 Draft만 받아 대상마다 우편 한 행을 한 트랜잭션에 넣는다(`(grant_id, account_id)` 유일이라 다시 넣지 않음, 기한은 DB 시각 + claimDays). 대상이 2명 이상인 일괄 지급과 `RecoveryCompensation` 지급의 실행은 Admin 이상만 할 수 있다(`AdminGrantPolicy`). Operator가 부르면 작업 행 없이 403 `FORBIDDEN_ROLE`이고, 실행 효과도 잠근 지급 행으로 같은 판정을 다시 한다. `RecoveryCompensation`은 같은 `sourceRef`에 취소되지 않은 지급이 이미 있으면 작성이 409 `INVALID_STATE`다(유일 인덱스가 동시 작성도 막음, 앞 지급을 취소하면 다시 작성 가능). P0에는 2인 승인이 없어 실행 감사 행을 `self_approved=true`로 남긴다. 취소는 Draft를 그대로 Cancelled로, Executed는 기한 안의 미수령 우편만 취소하고 수령·만료 수를 감사에 남긴다. 이미 수령한 우편은 되돌리지 않으며 회수는 개인 조정으로 한다. 자산은 `Currency`, `Tickets`(계정), `Essence`, `Shards`(캐릭터, 수령 때 고름), `AccountItem`(정산과 같은 허용 ID, 귀속 `Account`)이고 행동력은 지급 대상이 아니다. 한 줄 수량 상한 1,000,000은 오타 방지용 임시값이다(TODO, 기획 미정).

개인 조정(`POST /admin/v1/accounts/{accountId}/adjustments {assetKind, assetId, bindingKind, characterId, delta}`)은 증가를 `AdminGrant(26)`, 감소를 `AdminRevoke(27)` 원장으로 남긴다(actor_kind `Admin`). 감소는 잔액에서 예약분(`tickets_reserved`, 0009의 `currency_reserved`, `account_items.reserved`)을 뺀 가용량까지만 빼고 모자란 양을 `shortfall`로 응답과 감사에 남긴다. 잔액을 음수로 만들지 않으며 가용량이 0이어도 감사와 함께 확정한다. 같은 operationId 재전송은 감사 행에서 처음 결과를 다시 읽는다.

플레이어 `GET /v1/inbox`는 미수령·미취소·미만료 우편을 곧 만료되는 순서(`expires_at` 오름차순)로 최대 100개 주고 캐릭터 자산이 있으면 `requiresCharacter`다. 저장된 보상 JSON은 읽을 때 구조만 검증한다(허용 ID·수량 상한 같은 작성 정책은 작성 때만 적용). 구조가 손상된 행은 목록에서 빼고 우편 ID만 경고 로그로 남긴다. `POST /v1/inbox/claim {operationId, inboxId, characterId}`는 actor가 계정인 플레이어 작업이며 잔액 증가, 원장(`InboxClaim(24)`, actor_kind `Player`, source_type `inbox`, source_id 우편 ID), 수령 표시를 한 트랜잭션에 쓴다. 요청 해시는 원문이 아니라 파싱한 값이라 GUID 표기만 다른 재전송도 같은 요청이다. 거절은 409와 `INBOX_EXPIRED(1101)`, `INBOX_ALREADY_CLAIMED(1102)`, `INBOX_CANCELLED(1103)`, `INBOX_CHARACTER_REQUIRED(1104, 남의 캐릭터 포함)`, `INBOX_NOT_FOUND(1105)`다. 없는 우편과 남의 우편은 operations 행을 남기지 않고 `INBOX_NOT_FOUND`다. 한도는 계정별 1분에 수령 30회, 목록 60회(`InboxRequestLimiter`, 프로세스 메모리)이고 넘으면 429 `RATE_LIMITED`와 `Retry-After`다. 한도와 수령 본문 상한(4 KiB, `Content-Length`가 넘으면 413, 길이 없는 본문은 `RequestSizeLimit`이 Kestrel에서 끊음)은 리소스 필터 `InboxRequestGateFilter`가 모델 바인딩 전에 적용하므로 거절된 요청의 본문은 읽지 않는다. `inbox_claim`이 꺼져 있으면 위 기능 차단 규칙을 따른다(저장된 결과가 있으면 그 결과, 없으면 409 `FEATURE_DISABLED`). 점검은 우편 수령을 막지 않는다. 장애 보정 지급(`sourceKind=RecoveryCompensation`)의 수령도 같은 `InboxClaim(24)`로 남으며 `RecoveryCompensation(30)` 원장 사유는 아직 쓰는 경로가 없다. 출처는 우편 행의 `source_kind`로 구분한다.

**계정과 초대.** 검색은 `GET /admin/v1/accounts?query=<UUID>`(계정·캐릭터 ID만, 그 밖은 400)와 `POST /admin/v1/accounts/search`다. POST는 기존 `{query}`(UUID 또는 초대 코드)와 `{characterName, includeDeletedCharacters?}`(정규화된 이름 정확 검색)를 지원한다. 프레임워크나 앞단 요청 로그에 조회 문자열이 남을 수 있어 이름과 초대 원문은 본문으로만 받는다. 검색 결과는 계정 요약과 매칭된 캐릭터의 삭제 상태를 제공한다. 상세는 지갑, 캐릭터, 아이템, 접속 임대, 최근 세션, 살아 있는 로그인 묶음 수, 초대 목록을 한 읽기 스냅샷에서 모은다. 초대 발급(`POST /admin/v1/invites {accountId(생략하면 새 계정), maxUses, expiresInHours, revokeExistingSessions}`)은 응답에 원문을 한 번만 싣고(`Cache-Control: no-store`) 같은 operationId 재전송은 같은 inviteId에 원문 null이다. 응답을 잃었으면 그 초대를 폐기하고 새로 발급한다. 기기 변경 재발급은 `revokeExistingSessions`(생략하면 `true`)로 기존 로그인 묶음과 하위 토큰, 이전 초대를 같은 트랜잭션에서 폐기한다. 강제 종료(`POST /admin/v1/accounts/{accountId}/kick {operationId, reason, revokeInvites}`)는 로그인 묶음, 접근·갱신 토큰, 게임 하위 세션, 미소비 입장 토큰을 폐기하고 `revokeInvites`(생략하면 `true`)면 미폐기 초대도 폐기한다. 응답은 `AdminAccountKickResponse{operation, accountId, invitesRevoked, currentSessionKept=true}`다. 이미 발급된 토큰과 초대만 폐기한다. 게임 서버가 쥔 계정 접속 임대는 지우지 않으므로 지금 접속 중인 마을·던전 연결은 이어지고, 폐기된 토큰의 갱신, 재접속, 새 입장 토큰 발급부터 거절된다. 0010의 아이디·비밀번호 로그인은 막지 않으므로 같은 아이디와 비밀번호로 새 토큰을 받을 수 있다. 로그인 차단은 제재(P1)다.

**캐릭터 이름으로 계정 검색.** `POST /admin/v1/accounts/search`에 `{characterName:"홍길동", includeDeletedCharacters:false}`를 보낸다. 기존 `{query}`와 `characterName` 중 정확히 하나만 지정한다. 이름은 생성 때와 같은 `CharacterNameRules`를 사용해 앞뒤 공백 제거, NFKC 정규화, 2~12자 완성형 한글/영문/숫자 검사, 소문자 `name_key` 정확 비교를 수행한다. 부분/접두 검색은 지원하지 않는다. 삭제 캐릭터는 기본 제외하며 `includeDeletedCharacters:true`로 포함한다. 응답 `includeDeletedCharacters`는 이름 검색에 적용한 정책(UUID/초대 검색은 false), 항목 `matchedBy=CharacterName`과 `matchedId`는 매칭된 캐릭터 ID, `matchedCharacterDeleted`는 `false`=활성, `true`=삭제, `null`=캐릭터 이외 매칭이다. GET 검색은 기존 UUID 계약을 유지한다. 형식 오류는 원문을 포함하지 않는 400 `INVALID_REQUEST`다. API 요청 로그는 기존 경로 템플릿만 기록하며 검색 원문을 로그/감사/시계열 label에 넣지 않는다. CLI는 `accounts search --character-name <이름> [--include-deleted]`이며 기존 `accounts search <UUID|초대 코드>`도 유지한다.

**로그 조회.** 로그 조회는 읽기 전용 스냅샷이다. 원장, 아이템 이력, 세션 이벤트, 작업 조회는 계정 데이터가 실리는 민감 조회라 `reason` 조회 문자열(최대 500자)이 필수이고, 데이터를 읽기 전에 감사 행(`admin.ledger.view`, `admin.item.history.view`, `admin.session.events.view`, `admin.operation.view`)을 자체 트랜잭션으로 커밋한다. 대상이 없어 404가 되는 조회도 시도로 감사하고, 원장은 페이지마다 한 행이다. 감사 로그 조회는 감사하지 않는다. 시각 조건은 UTC 유닉스 ms이며 `fromUnixMs` 포함, `toUnixMs` 제외다. 목록은 keyset `cursor`(응답의 `nextCursor`)와 `limit`(기본 100, 최대 500)로 넘긴다. 원장은 `accountId` 또는 `sourceType`+`sourceId`가 없으면 400(전체 스캔 방지)이고 `characterId`, `assetKind`, `assetId`, `reasonCode`(원장 사유 숫자, `reason`은 감사 사유)로 좁힌다. 필터 전체의 사유·자산별 합계(`totals`)는 cursor 없이 부른 첫 페이지에만 있고 다음 페이지는 null이다. 감사 로그는 `adminId`, `targetKind`(+`targetId`), 기간으로 거른다. 요청 IP는 HMAC 해시의 앞부분만 보인다.

**장애 세션 복구.** `GET /admin/v1/sessions`는 `state`가 없으면 복구 관심 대상(RecoveryPending, 자동 복구가 실패했거나 백오프 중인 Running/Settling, 정산 대기 시간이 지난 Settling, 배정 서버 생존 증거가 없는 Running, 멈춘 Auction: 항목의 `auctionOverdue`)을, 있으면 그 상태의 세션을 준다. `POST /admin/v1/sessions/{sessionId}/recovery {operationId, reason, action}`의 action은 셋이다.

- `RetrySettlement`: 저장된 종료 이벤트가 있는 Settling을 자동 복구와 같은 배정 서버 actor, 같은 정산 operationId로 정산한다.
- `ServiceCancel`: 배정 서버 소실이 확인된 Running을 장애 취소한다. 티켓·사용 물약을 환급하고 기본 정기와 서버 원장으로 확인된 미정산 추가 정기를 모두 100% 지급한다(2026-10-08 결정, `SERVICE_CANCEL_ESSENCE_PERCENT = 100`). 서버가 살아 있으면 `INVALID_STATE`다.
- `ForceClose`: 정산이 반복 실패해 격리된 세션(`state=Settling` 이고 `recovery_failures >= 5`)을 닫는다. 티켓·사용 물약 환급과 기본 정기 전액은 장애 취소와 같지만, 추가 정기는 현행 유지로 50% 내림만 지급한다(`FORCE_CLOSE_ESSENCE_PERCENT = 50`). 장애 취소의 100% 결정을 자동 확대하지 않았고 ForceClose의 추가 정기 지급률은 기획 미정이다. 운영자 작업 **한 트랜잭션**에서 종결, 원장(actor_kind `Admin`, actor_id 운영자 ID), 감사를 함께 쓴다. 결과는 `Closed`/`ServiceCancelled`이고 배정 서버는 Draining이 된다. 같은 세션 행 조건부 갱신이라 늦게 온 `/settlement`와 한쪽만 이기며, 강제 종료가 이기면 그 정산은 `ALREADY_SETTLED`다. 원장은 `GET /admin/v1/operations/{운영자 ID}/{operationId}`로 본다. 격리되지 않은 Settling이나 다른 상태는 `INVALID_STATE`다.

RecoveryPending 세션은 처리 경로가 없어 세 action 모두 `INVALID_STATE`다. 격리 카운터 `recovery_failures`를 되돌리는 API는 없다.

**생존 판정 보류.** `RecoveryLivenessGate`(싱글턴, 유지 작업과 공유)는 API 시작 뒤와 DB 단절을 확인한 뒤 30초 동안 서버 소실 판정을 보류한다. 그동안의 오래된 heartbeat로 출정을 장애 취소하지 않기 위해서다. 보류 중 새 `ServiceCancel`·`ForceClose`는 저장하지 않고 503 `BACKEND_UNAVAILABLE(810)`과 `Retry-After`(남은 초)로 거절한다. 이미 저장된 어드민 operationId의 재전송은 보류와 무관하게 처음 결과다. 복구 목록은 응답에 `livenessDeferred`를 싣고 보류 중에는 각 세션의 `instanceLost`를 false로 둔다. 보류는 프로세스 메모리 값이라 API 인스턴스마다 따로 판정한다.

`RetrySettlement`·`ServiceCancel`은 **두 트랜잭션**이다. 정산과 장애 취소는 늦게 온 `/settlement`나 다음 자동 복구와 겹쳐도 한 번만 적용되도록 actor(서버), operationId, 본문이 자동 복구와 같아야 하고, 그 작업이 자기 operations 행과 트랜잭션을 연다. 그래서 순서는 "의도 감사 행(`admin.session.recovery.intent`: 운영자, 사유, 어드민 operationId, 복구 작업 actor·operationId) 선커밋 → 기존 `PostgresSettlementRepository` 경로로 복구 확정 → 어드민 작업과 감사 행 확정"이다. 마지막 단계가 실패해도 의도 행이 남아 감사 없는 종결이 생기지 않는다. 감사 행의 after_state에 실제 복구 작업의 actor, operationId, 영수증과 `alreadyApplied`를 남긴다. `alreadyApplied`는 세션 상태가 아니라 복구 작업(서버 actor, 고정 operationId)이 이번 호출 전에 이미 저장돼 있었는지다. 이미 저장된 어드민 operationId는 복구를 다시 부르지 않는다. 복구는 확정됐는데 어드민 단계가 실패(503 등)해 같은 operationId로 다시 오면, 세션이 이 action이 만드는 종결 상태일 때만 기존 경로를 다시 불러 저장된 결과를 받고 감사한다. 다른 경로로 종결된 세션은 `INVALID_STATE`다. 저장 여부 검사와 서버 복구가 어드민 operations 행보다 먼저라, 세 action 모두 시작할 때 운영자 작업 키를 action, 대상 세션, 요청 해시에 묶는 `admin_operation_claims` 행을 먼저 커밋한다(`INSERT ... ON CONFLICT DO NOTHING` 뒤 읽어 비교). 일반 쓰기도 같은 결속을 먼저 커밋하므로 일반 쓰기와 복구가 같은 키로 경합해도 최초 요청만 효과를 실행한다. 결속 도입 전에 저장된 operations의 요청 해시도 확인한다. 묶인 값과 다르면 서버 복구 없이 저장하지 않는 409 `IDEMPOTENCY_CONFLICT`다. 같으면 트랜잭션 advisory 잠금(운영자, operationId)과 행위자 행 `FOR SHARE`를 끝까지 쥐어 같은 키의 실행을 직렬화하고, 행위자가 비활성이거나 역할이 낮아졌으면 서버 복구를 부르지 않는다(어드민 작업이 `FORBIDDEN_ROLE`을 저장). 같은 키의 앞 요청이 잠금 제한(2초)보다 오래 걸리면 뒤 요청은 503이며 같은 operationId로 다시 보내면 된다. 장애 보정은 `POST /admin/v1/grants`에 `sourceKind=RecoveryCompensation`, `sourceRef=세션 ID`로 따로 지급한다(실행은 Admin 이상, 세션당 하나).

**명령줄 도구.** `Backend/src/Shinshi.AdminCli`는 어드민 API만 호출하고 DB에 직접 붙지 않는다(ADR02). 사용법은 [Shinshi.AdminCli README](src/Shinshi.AdminCli/README.md)를 따른다.

**남은 위험과 결정.**

- 0008의 지급·조정·수령 코드가 0009의 `currency_reserved` 열에 의존한다. 0008과 0009는 같은 커밋·배포에 함께 넣는다.
- 계정 강제 종료(kick)는 현재 접속을 유지하고 비밀번호 로그인을 막지 않는다. 계정 접속 임대는 서버 heartbeat가 계속 연장하고, 같은 아이디와 비밀번호로 새 토큰을 받을 수 있다. 로그인 차단과 즉시 차단은 제재(P1)나 서버 쪽 연결 종료 경로가 필요하다.
- 어드민 쓰기는 커밋까지 행위자 행을 `FOR SHARE`로 잡고, 수동 복구는 서버 복구 동안 잡는다. 그 운영자의 비활성화나 로그인은 그동안 기다리고, 잠금 제한(2초, 실행기 재시도 3회)을 넘으면 503이다.
- 운영자 작업 키 결속은 모든 업무 변경이 효과 전에 공유한다. 일반 쓰기가 진행 중인 경우와 복구가 먼저 결속한 경우 모두 다른 본문의 같은 operationId는 효과 없이 거절한다. 실제 PostgreSQL 회귀 테스트에서 복구의 결과 키도 보존되는 것을 확인했다. 일시 실패한 최초 요청도 키 결속은 남으므로 같은 본문으로 재시도하고, 다른 업무는 새 operationId를 쓴다.
- 세션 강제 종료(`ForceClose`)는 원래 정산안 대신 장애 취소 보상 틀(추가 정기만 50%)을 적용한다. 원래 보상과 차이가 있으면 운영자가 `RecoveryCompensation` 보정 지급으로 메운다.
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

직업 ID 범위는 0~5다(0016 `player_class_range`가 `characters`, `market_listings`의 CHECK를 넓힌다). 신규 생성은 직업 3(검사), 4(마검사), 5(궁수)만 허용하며 설정 `Characters__EnabledClassIds`의 기본값은 `3,4,5`다. 구직업 0~2는 설정에 넣어도 생성되지 않는다. 닫힌 직업의 생성은 `409 FEATURE_DISABLED`(작업으로 저장하지 않음)다. 기존 구직업 캐릭터의 선택·마을 입장은 호환성을 유지하고, 새 직업의 마을 입장은 생성 허용 설정을 따른다. 범위 밖(6 이상, 음수)은 400 `INVALID_APPEARANCE`다. 부적 효과는 `growth-content.json`에 직업 6개 x 15개 = 90개이며 효과 ID 형식(`^T[0-9]{2}-[1-3][A-Z]$`)은 그대로다. 0016 down은 직업 3~5 행이 있으면 데이터를 지우지 않고 중단한다.

| 경로 | 인증 | 결과 |
|---|---|---|
| `POST /v1/accounts` `{loginId, password, email}` | 공개 | 201 `AccountSignupResponse`(토큰 없음). 400 `INVALID_LOGIN_ID(1202)`·`WEAK_PASSWORD(1204)`·`INVALID_EMAIL(1203)`, 409 `LOGIN_ID_TAKEN(1200)`, 429 `RATE_LIMITED`, 503 `MAINTENANCE` |
| `GET /v1/accounts/login-id-availability?loginId=` | 공개 | 200 `AvailabilityResponse`(형식 오류 `INVALID_LOGIN_ID`, 사용 중 `LOGIN_ID_TAKEN`). IP별 1분 30회 |
| `POST /v1/auth/exchange` `{loginId, password, build?}` | 공개 | 200 `AuthExchangeResponse`. 초대·갱신·아이디 비밀번호 중 정확히 하나(아니면 400). `build`가 없으면 점검만, 있으면 기존 빌드 검사(403). 갱신 토큰 회전도 같다(`build` 선택, 런처는 보내지 않는다). 초대 교환은 `build` 필수. 게임 서버 입장은 빌드를 검사하는 게임 세션 토큰만 받는다. 실패 401 `INVALID_CREDENTIALS(1205)`, 잠금 429 `ACCOUNT_LOCKED(1206)`, 늦춤·해시 대기 429 `RATE_LIMITED` |
| `POST /v1/auth/logout` `{refreshToken}` | 공개(Bearer 없음) | 204(멱등). 진짜 갱신 토큰이면 만료·폐기 여부와 무관하게 묶음과 하위 게임 세션을 폐기하고, 못 찾아도 204라 존재 여부를 알리지 않는다. 빈 토큰 400, 태그가 틀린 토큰은 인증 실패 한도에 세고 204. 이미 붙은 접속 임대는 끊지 않는다(런처는 게임 실행 중 로그아웃을 막음) |
| `GET /v1/accounts/me` | 접근 토큰 또는 게임 하위 세션 | 200 `AccountProfileResponse`. 초대 코드 계정은 `loginId`, `email`이 null |
| `POST /v1/accounts/credentials` | 접근 토큰만(게임 하위 세션은 403 `SESSION_MISMATCH`) | 204. 일반 변경은 `{currentPassword, newPassword}`이고 현재 비밀번호 재확인이 필수다(틀리면 403 `INVALID_CREDENTIALS`, 로그인과 같은 실패 제한). `currentPassword` 없는 변경은 초대(복구 코드) 교환으로 시작한 로그인 묶음에서 시작 30분 안에만 되며(아니면 403 `INVALID_CREDENTIALS`), 아이디가 없는 계정은 `{loginId, email, newPassword}`, 있는 계정은 `{newPassword, email?}`다. 성공하면 같은 트랜잭션에서 호출한 묶음을 포함한 계정의 모든 로그인 묶음, 게임 하위 세션, 입장 토큰, 남은 초대를 폐기하므로 새 자격으로 다시 로그인한다. 409 `LOGIN_ID_TAKEN`·`INVALID_CREDENTIALS`(동시 변경) |
| `GET /v1/characters/name-availability?name=` | 플레이어 | 200 `AvailabilityResponse`(`INVALID_CHARACTER_NAME(1002)`, `CHARACTER_NAME_TAKEN(1001)`). IP별 1분 30회 |
| `POST /v1/characters` `CharacterCreateRequest` | 플레이어 | 200 확정, 409 거절(`CHARACTER_NAME_TAKEN`, `NO_CAPACITY`, `IDEMPOTENCY_CONFLICT`), 본문 `CharacterCreateResponse`. 400은 형식 오류이며 작업으로 저장하지 않는다. 계정별 1분 10회(생성, 삭제 합산, `AccountRequestLimiter`)를 넘으면 429 `RATE_LIMITED` |
| `POST /v1/characters/{characterId}/delete` `CharacterDeleteRequest` | 플레이어 | 200 확정, 409 거절(`CHARACTER_NOT_FOUND`, `ALREADY_CONNECTED`, `RESERVATION_CONFLICT`, `STALE_REVISION`). 계정별 1분 한도와, 계정 행 잠금 뒤 최근 24시간 삭제 수가 `MAX_DELETES_PER_WINDOW`(기획 미정 임시값 3, 0012 `ix_characters_account_deleted`)에 닿으면 429 `RATE_LIMITED`(본문 `OperationResult`, 저장된 거절) |

**비밀번호.** PBKDF2-HMAC-SHA256 600,000회, 16바이트 솔트, 32바이트 결과를 `pbkdf2-sha256$회수$솔트$해시`(base64)로 저장하고 행의 회수로 검증한다. `Accounts__PasswordIterations`는 테스트 호스트만 낮춘다(최소 1000). 비교는 고정 시간이고, 없는 아이디도 현재 기본 회수의 더미 해시를 계산한다(테스트는 PBKDF2 계산 수와 회수를 센다). 로그인에 성공했는데 저장 해시의 회수가 현재보다 낮으면 같은 비밀번호를 새 회수로 해시해 발급 트랜잭션에서 바꾼다. 형식이 틀린 아이디는 존재할 수 없어 해시 없이 401이다. 해시는 DB 트랜잭션 밖에서 계산하고, 동시 계산은 `PasswordHashGate`가 CPU 코어 수로 제한한다. 대기는 코어 수의 2배까지만 받고 넘으면 바로, 대기 2초가 지나면 429 `RATE_LIMITED`(`Retry-After: 2`)이며 요청이 취소되면 대기에서 빠진다. 로그인 발급과 비밀번호 변경은 계정 행 잠금 아래에서 검증한 해시가 그대로인지 다시 본다. 로그인 묶음에는 시작 수단(`auth_token_families.origin`: `invite`, `password`)을 적고 갱신 회전은 같은 묶음을 이어 쓴다.

**실패 제한.** `PasswordLoginLimiter`(프로세스 메모리)는 bearer 위조를 세는 `AuthFailureLimiter`와 따로 센다. (아이디, IP) 쌍 5회 연속 실패면 잠금 만료를 지금+15분으로 두고 그 쌍만 429 `ACCOUNT_LOCKED`다. 잠금 중에는 해시를 계산하지 않고 만료도 늘리지 않으며, 만료가 지나면 실패 수를 0으로 한다. 같은 쌍의 동시 시도는 쌍 차례(최대 2초 대기, 넘으면 `RATE_LIMITED`)로 판정, 해시, 기록을 하나씩 한다. 같은 아이디의 최근 1시간(미끄럼 창) 실패 합계가 50회를 넘으면 그 아이디의 시도를 IP마다 1분 1회로 늦춘다. 성공하면 그 쌍의 실패 수를 0으로 한다. 키는 마지막 사용 뒤 1시간이 지나면 지우고, 쌍과 아이디 키 합계가 10만 개를 넘으면 오래 쓰지 않은 것부터 9만 개까지 줄인다. 비밀번호 변경의 현재 비밀번호 확인도 같은 제한을 받는다. 가입은 형식 검사를 통과한 시도만 IP당 1시간 5회다. API를 여러 대로 늘리면 카운터를 공유 저장소로 옮겨야 한다. IP는 forwarded headers 처리 뒤 값이므로 NAS 역방향 프록시를 `ForwardedHeaders__KnownProxies__*`에 넣어야 방문자별로 센다.

**캐릭터.** 이름은 앞뒤 공백 제거 -> NFKC -> 2~12자의 완성형 한글·영문·숫자 검사 -> 표시 이름 `name` -> `ToLowerInvariant` 비교 키 `name_key`(유일 인덱스) 순서다. 생성은 계정 actor의 `character.create` 멱등 작업이며 영수증 ID가 캐릭터 ID다. 계정 행을 잠근 뒤 개수 상한(`MAX_CHARACTERS_PER_ACCOUNT = 3`, 기획 미정 임시값, 전용 코드가 없어 `NO_CAPACITY`)을 보고 `INSERT ... ON CONFLICT (name_key) DO NOTHING`으로 넣는다. 동시에 같은 키를 넣으면 먼저 커밋한 쪽만 남고 나머지는 `CHARACTER_NAME_TAKEN`으로 저장된다. 외형은 `appearance` jsonb로 저장한다. 요청 외형이 `version=1`(10필드, 과거 계약)이면 구조 검증만 보고, `version=2`이면 `CharacterAppearanceCatalog.IsAllowed`가 등록된 얼굴(`FACE_CUTE`)·헤어·의상 ID(각 1~3, 직업과 독립 선택)와 나머지 슬라이더·색이 전부 중립인지까지 본다. v1 재직렬화는 `faceId`/`outfitId` 키를 항상 생략해 과거 `operationId`의 `request_hash`를 보존한다. 초기 지급은 0011 절의 `InitialGrantService`가 같은 트랜잭션에서 한다.

**외형 계약 검증 범위.** `Accounts/CharactersPostgresTests.cs`가 실제 PostgreSQL로 확인한다: v1 10키 골든 요청(`기존캐릭터`, classId 0, 고정 operationId)의 재직렬화 해시가 과거 값과 같고, 그 해시로 미리 심은 Committed/Rejected 영수증을 같은 본문으로 다시 요청하면 효과를 재실행하지 않고 저장된 결과만(캐릭터 수·작업 수 증가 없이) 돌려준다. v2는 hair(1~3) x outfit(1~3) 9조합이 직업과 무관하게 모두 통과해 12키로 저장되고, 이름 정규화도 그대로 적용된다. `Accounts/AccountsHttpTests.cs`는 HTTP 계층에서 v2의 등록 범위 밖 얼굴/헤어/의상 ID, 비중립 슬라이더·피부·색, 미지의 직업이 저장소 호출 전 400으로 거절됨(부작용 0)을 확인한다. `Auth/Api/PostgresJoinTokenHttpTests.cs`는 마을·던전 입장 교환 응답이 DB 소유 캐릭터의 닉네임·외형 원문(레거시 null, v1, v2 세 저장 형태 모두)을 그대로 전달하며 다른 계정 값과 섞이지 않음을 확인한다. 이 세 파일의 실제 PostgreSQL 실행 결과는 격리된 테스트 DB를 연결한 환경에서만 얻을 수 있으며, DB 없이 돌린 `dotnet build`/순수 테스트 통과를 DB 검증 완료로 보지 않는다.

**0010 스키마.** `account_credentials`(account_id PK/FK, 소문자 login_id 유일 + 형식 CHECK, password_hash 형식 CHECK, email, email_key, email_verified, created_at, updated_at, `email_verified`인 행만의 email_key 부분 유일 인덱스)와 `characters.name`, `name_key`, `appearance`, `auth_token_families.origin`(`invite`/`password`, 기존 행은 모두 초대 교환으로 만들었으므로 `invite`로 채운 뒤 기본값을 지워 새 묶음은 시작 수단을 반드시 적는다)을 더한다. 기존 캐릭터 행에는 채울 이름이 없어 세 열을 NULL 허용으로 두었다. NULL끼리는 유일 인덱스에서 충돌하지 않고, 이름이 있으면 형식 CHECK와 `name_key = lower(name)`을 지킨다. 임시 이름을 채워 NOT NULL로 만드는 방식은 임시 이름끼리, 또는 나중 실제 이름과 겹칠 수 있고 이름 없이 행을 넣는 기존 코드와 테스트를 깨므로 택하지 않았다. 0010 down은 모든 아이디, 비밀번호 해시, 이메일, 캐릭터 이름을 지우므로 운영 DB에서 실행하지 않는다. 런타임 권한은 위 권한 SQL의 0010 줄이다.

`GET /v1/characters`는 접근 토큰 또는 게임 하위 세션으로 인증된 계정의 `CharacterListResponse`를 반환한다. 계정 ID는 요청에서 받지 않으며 응답은 캐시하지 않는다. Bootstrap 런처 접속 화면에서 캐릭터 선택에 사용한다.

**남은 일.** 인증 메일(그 전까지 `emailVerified=false`, `EMAIL_TAKEN`은 번호만 예약), 비밀번호 찾기, 캐릭터 이름 금칙어, 계정당 캐릭터 수는 기획 미정이다. 테스트는 `tests/Shinshi.Backend.Tests/Accounts/`에 있다.

## 성장, 장비, 초기 지급, 계정 진행 (0011)

규칙 수치의 원본은 Unity SO(`EquipmentCatalogSO`, `GrowthConfigSO`, `TalismanCatalogSO`, `ProgressionCatalogSO`, `CharacterLevelConfigSO`)다. 백엔드는 그 에셋 값을 옮긴 `src/Shinshi.Data/Content/growth-content.json`(내장 리소스 `Shinshi.Data.Content.growth-content.json`, `schemaVersion` 4, `contentVersion`은 GameDB 값)만 읽고 시작 시 개수, 범위, 참조를 검증한다. 코드는 `Shinshi.Data/Growth/`, `Shinshi.Api/Growth/`, `Shinshi.Api/Ledger/EconomyReconciliationService.cs`이고 등록은 `AddShinshiGrowth`(항상), `AddShinshiEconomyReconciliation`(연결 문자열이 있을 때만)이다.

| 경로 | 인증 | 결과 |
|---|---|---|
| `GET /v1/town/characters/{characterId}/growth` | 마을 서버 임대 | 200 `CharacterGrowthSnapshot`, 캐릭터가 없으면 404, 계정 접속 임대가 그 마을에 없으면 409 `WRONG_LOCATION` |
| `POST .../talismans/{register,upgrade,loadout,disassemble}` | 마을 서버 임대, 기능 키 `talisman` | `GrowthOperationResponse` |
| `POST .../training`, `.../training/respec` | 마을 서버 임대, 기능 키 `training` | `GrowthOperationResponse` |
| `POST .../equipment/craft`, `.../equipment/{itemId}/enhance`, `.../equipment/inherit` | 마을 서버 임대, 기능 키 `crafting`, `enhance`, `inherit` | `GrowthOperationResponse` |
| `POST .../equipment/{itemId}/reroll-options` `EquipmentRerollOptionsRequest{operationId, expectedRevision, rolls[4]}` | 마을 서버 임대, 기능 키 `reroll_options` | `GrowthOperationResponse`(아래 "장비 옵션 재설정 (0018)") |
| `POST .../equipment/{itemId}/{equip,unequip,lock,dismantle}` | 마을 서버 임대 | `GrowthOperationResponse` |
| `GET /v1/accounts/me/progress` | 플레이어 | 200 `AccountProgressResponse` |
| `POST /v1/town/accounts/{accountId}/progress/intro` `IntroCompleteRequest{operationId}` | 마을 서버 임대 | 200 `OperationResult`(영수증 ID = 계정 ID, 이미 완료도 확정), 409 `SESSION_MISMATCH`(그 계정의 접속 임대가 호출 마을에 없음), `INVALID_TARGET`(없는 계정). 계정 actor 멱등 |

모든 쓰기는 캐릭터 계정 actor의 멱등 작업이다(`growth_receipts.actor_id = account_id`, 중계한 마을은 `town_instance_id`). 실행 전에 캐릭터에서 계정을 읽으므로 재접속이나 다른 마을에서 같은 operationId를 다시 보내도 처음 결과를 받고, 요청 해시에는 `expectedRevision`과 roll을 넣지 않는다(판정 입력일 뿐이다). 없는 캐릭터는 actor를 정할 수 없어 저장 없이 `INVALID_TARGET`이다. 원장 `actor_kind`는 `Player`이고 entry_key 앞에 작업 내 일련번호가 붙어 같은 자산 두 줄도 쓴다. 등록, 강화의 roll은 `growth_receipts.roll_basis_points`에 남는다. 잠금 순서는 계정 -> 캐릭터 -> 장비(item_id) -> account_items(정의 ID)이고, 그 캐릭터 계정의 살아 있는 접속 임대가 호출한 마을에 없으면 `WRONG_LOCATION`, 열린 출정(Reserved/Active)이 있으면 `LOCKED`, 요청의 `expectedRevision`이 `characters.loadout_revision`과 다르면 `STALE_REVISION`이다. 성공하면 `loadout_revision`이 1 오르고(배분이 같은 재분배, 이미 같은 상태의 착용, 잠금은 그대로) `growth_receipts`에 성공, 실패와 장비 ID를 남겨 재전송에도 처음 결과를 돌려준다. 등록, 강화의 확률 입력 `rollBasisPoints`(0~9999)는 마을 서버가 뽑고 요청 해시에서 빠지므로 같은 operationId를 다른 roll로 다시 보내도 처음 결과다. 비용은 `EconomyDebits.TryDebitExactAsync`로 가용량(총량 - 예약량)에서 정확히 빼며 모자라면 효과 전체를 되돌리고 `INSUFFICIENT_ITEM`이다. 원장 사유는 TRAINING 12, CRAFT 13, ENHANCE 14, INHERIT 15, TALISMAN_REGISTER 16, TALISMAN_UPGRADE 17, DISMANTLE 18, INITIAL_GRANT 25이고 장비 변경은 `item_history`에 남긴다. 삭제한 캐릭터는 성장 조회가 404, 성장 쓰기가 `INVALID_TARGET`이다. 장비 분해 반환이 보스 재료 보관 상한(9999)을 넘으면 회수함으로 보내지 않고 장비를 남긴 채 `INVENTORY_FULL`이다(플레이어 행위는 공간 부족이면 거절, 회수함은 확정된 획득만). 기능 차단 중에는 저장된 결과만 돌려주고 처음 보는 operationId는 저장 없이 409 `FEATURE_DISABLED`다.

캐릭터 생성 작업은 같은 트랜잭션에서 `InitialGrantService`를 부른다. 계정 최초분(`initial_grants`의 `AccountFirst` 부분 유일 인덱스)은 계정에 한 번만 물약을 주고 `action_value = 100`, `action_anchor = now`로 지갑을 맞추며, 캐릭터분은 캐릭터마다 정기500·약초10·엽전100과 6부위 장비(착용, 귀속)를 준다. 약초·엽전은 계정 자산이며 지급 여부는 캐릭터별 영수증으로 중복을 막는다. 묶음은 설정 `Growth__ContentProfile`(`Trial` 기본, `Standard`)로 고른다. 기존 계정, 캐릭터에는 소급하지 않는다.

출정 준비 확인(readiness)이 돌려주는 `characterRevision`도 `loadout_revision`이고, 출정 예약은 참가자의 `characterRevision`을 `loadout_revision`과 대조하며 삭제한 캐릭터(0012 `deleted_at`)는 `SESSION_MISMATCH`로 거절하고 `session_participants.loadout_snapshot`에 `CharacterCombatLoadout`을 고정한다. 던전 입장, 재입장 교환 응답 `JoinTokenExchangeResponse.combatLoadout`이 이 값이다. 같은 응답의 `potions`는 그 참가자의 남은 물약 예약분(예약 수량 - 확정 사용, 지참하지 않았으면 빈 배열)이며, 던전 서버는 이 수량과 보스 사용 상한 안에서만 `POTION_USED`를 보낸다(넘는 사용은 `/events`가 `POTION_LIMIT`로 거절하고 저장 큐가 멈춘다). 일일 대사는 `economy_reconciliation_runs`(UTC 날짜 PK)를 먼저 넣은 API 인스턴스만 돌고, 불일치마다 금액 없이 ID만 ERROR 로그로 남긴다. 대사는 계정 ID 순서의 계정 창(기본 200개)마다 그 계정들의 잔액과 원장만 집계하고, 창마다 선점 `started_at`을 갱신(heartbeat)해 오래 걸려도 다른 인스턴스가 stale로 보고 이어받지 않는다. 선점을 잃으면 결과를 기록하지 않고 멈춘다.

**계정 진행 기록.** 성공 정산(`end_reason = SUCCESS`) 트랜잭션이 계정 행을 잠근 뒤, 떠나지 않은(`departed_at IS NULL`) 참가자에게 일반전은 `battle_id`(지역 ID)의 `RegionCleared`와 그 지역의 `foxTracesPerClear`(상한 `foxTraceCap`), 보스전은 `boss_id`와 `difficulty`로 찾은 보스전의 `BattleCleared`를 기록한다. 실패, 장애 취소, 콘텐츠에 없는 지도는 기록하지 않는다. 정산 효과는 `settlement_operation_id` 조건부 갱신으로 한 번만 돌므로 진행도 한 번만 오른다. 설정 `Growth__RejectUnknownBattles=true`(출시 프로필)면 예약이 성장 콘텐츠에 없는 battleId(테스트용 지도, 빈 값)를 `INVALID_TARGET`으로 거절한다(기본 false, 개발, 테스트는 판정만 생략). 0011 down은 장비, 성장 상태를 모두 지우므로 운영 DB에서 실행하지 않는다.

## 장비 옵션 재설정 (0018)

장비 하나는 옵션 두 줄(`option1_kind`, `option1_value`, `option2_kind`, `option2_value`)을 갖는다. 종류는 `EquipmentOptionRules.KIND_*`(0 없음, 1 공격%, 2 생명%, 3 방어%, 4 치명 확률), 값은 만분율 정수(100 = 1%)이며 `(0, 0)`이 옵션 없는 정상 상태다. 제작, 지급 직후에는 옵션이 없다. DB는 형식만 지킨다(종류 0~4, 값 0~10000, 종류가 0이면 값도 0). 부위별 후보와 등급별 범위는 `growth-content.json`의 `equipment.option*` 키가 정하고 `GrowthContent`가 시작 시 검증한다: 부위 6개, 빈 후보 없음, 종류 1~4가 한 번씩, 등급 4칸, `EquipmentOptionRules.IsValidRange`, 비용 > 0, 천장 >= 1, 상급 기준 0~10000, 등급이 오를 때 범위가 내려가지 않고 min이 값 간격 위에 있을 것(상위 등급 계승이 옮긴 값이 대상 범위를 벗어나지 않게 하는 조건).

`POST /v1/town/characters/{characterId}/equipment/{itemId}/reroll-options`는 마을 서버만 부른다. `rolls`는 마을 서버가 뽑은 0~9999 정수 네 개(줄1 종류, 줄1 값, 줄2 종류, 줄2 값)다. `itemId`가 UUID가 아니거나 `rolls`가 네 개가 아니거나 범위 밖이면 400이고 작업으로 저장하지 않는다. 뽑기와 상급 판정은 공용 `EquipmentOptionRules`(`PickKind`, `PickValue`, `IsHighTier`)를 그대로 쓰며 백엔드는 난수를 만들지 않는다. 판정은 `OptionRerollRules`, 효과는 `PostgresEquipmentRepository.RerollOptionsAsync`다.

효과는 기존 성장 실행기의 한 트랜잭션이다. 계정 -> 캐릭터 잠금과 공통 검사(`WRONG_LOCATION`, `LOCKED`, `STALE_REVISION`) 뒤에 장비 행 `FOR UPDATE` -> 없는 장비, 다른 캐릭터의 장비, 수련용이면 `INVALID_TARGET` -> 경매장 보관 중이면 `MARKET_ESCROWED` -> 정기 300, 정련편 3 차감(모자라면 `INSUFFICIENT_ITEM`, 먼저 뺀 쪽도 함께 되돌림) -> `option_reroll_pity` 행 `FOR UPDATE` -> 판정 -> 누계 UPSERT -> 옵션 네 값과 `bound = true` 저장, `item_history`에 `OptionsRerolled` -> `loadout_revision` + 1과 영수증이다. 착용, 잠금 여부는 보지 않는다. 결과 코드는 `SUCCEEDED`뿐이고 원장 사유는 `REROLL_OPTIONS`(38)다. 영수증의 `growth_receipts.roll_values`(integer[4])에 뽑기 네 개를 남기며 기존 `roll_basis_points`는 비워 둔다.

누계(`option_reroll_pity`, 키는 캐릭터와 부위)는 상급 줄(값이 `min + 0.75 x (max - min)` 이상)이 하나도 없던 재설정의 연속 횟수다. 누계가 9 이상인 상태에서 하는 재설정은 줄1의 값을 그 종류, 등급의 max로 확정한다(종류는 난수대로). 결과에 상급 줄이 있으면 0, 없으면 +1이다. 장비 ID와 등급은 키에 없어 같은 부위의 다른 장비가 누계를 공유한다. 성장 조회의 `optionRerollPity`는 부위 순서 6칸이다.

`rolls`와 `expectedRevision`은 요청 해시에서 빠진다. 같은 operationId 재전송은 다시 뽑지 않고 비용도 한 번만 들며, 응답의 성장 스냅숏은 현재 상태다(그때 확정된 옵션은 `item_history`와 `roll_values`로 추적한다). 기능 키 `reroll_options`가 꺼져 있으면 저장된 결과만 돌려주고 처음 보는 operationId는 저장 없이 409 `FEATURE_DISABLED`다.

다른 작업과의 관계: 강화, 착용, 해제, 잠금은 옵션을 그대로 둔다(`GrowthEffectContext`의 장비 UPDATE가 옵션 열을 함께 쓴다). 계승은 원본의 두 줄을 대상으로 옮기고 대상의 옵션은 버리며, 상위 등급 계승이면 종류가 0이 아닌 줄의 값이 대상 등급 min보다 작을 때 min으로 올린다(`InheritanceRules.CarryOption`). 분해는 옵션과 함께 장비를 지운다. 재설정과 계승은 장비를 귀속시키므로 옵션이 있는 장비는 유저 경매장, 1:1 거래 대상이 아니고 소유 이전 경로는 옵션 열을 건드리지 않는다. 출정 예약은 `CharacterCombatLoadout`의 착용 장비마다 옵션 네 값을 함께 고정한다. 장비 이력 JSON은 옵션이 있을 때만 끝에 `option1Kind`~`option2Value`를 붙여 기존 키와 순서를 유지한다. 운영 어드민의 장비 표시는 옵션을 보여 주지 않는다.

0018 down은 옵션 열, 누계 테이블, `roll_values`를 지워 옵션을 복구할 수 없으므로 운영 DB에서 실행하지 않는다. 원장의 38 행과 `OptionsRerolled` 이력은 남는다. 콘텐츠 스키마(4)와 계약(`EquipmentView`, `CombatEquipmentEntry`, `CharacterGrowthSnapshot`)이 함께 바뀌므로 클라이언트, 전용 서버, API를 같이 교체한다. 테스트는 `tests/Shinshi.Backend.Tests/Growth/`의 `OptionRerollRulesTests`, `EquipmentOptionPostgresTests`, `GrowthHttpTests`, `ReservationLoadoutPostgresTests`에 있다.

## 생활(행동력, 채집, 물약 제작)과 물약 되돌림 (0013)

마을 서버 전용 경로다. 서버 임대 bearer와 인스턴스 헤더 확인(`GameServerControllerBase`, 마을 역할)을 거치고, 본문의 계정 접속 임대가 이 마을 인스턴스의 만료 전 임대가 아니면 `SESSION_MISMATCH(901)`다. 작업 actor는 본문의 계정이며 요청 해시는 계정과 업무 값만 넣는다(임대 ID, 마을 인스턴스 제외). 재접속으로 다른 마을, 다른 임대에서 같은 operationId를 다시 보내도 처음 결과다.

| 경로 | 본문 | 결과 |
|---|---|---|
| `POST /v1/town/life/snapshot` | `{accountId, accountLeaseId}` | 200 `LifeSnapshotResponse{actionValue, actionMax, nextRecoverAtUnixMs(상한이면 0), herb{definitionId, quantity, cap}, potions[{definitionId, quantity, reserved}], serverTimeUnixMs}`. DB 시각으로 회복을 반영하고 저장 값은 바꾸지 않는다. 409 `ApiRejection(SESSION_MISMATCH)` |
| `POST /v1/town/life/gathers` | `{operationId, accountId, accountLeaseId, zoneId, nodeId}` | `OperationResult`(committedRevision은 지갑 revision). 409 `INSUFFICIENT_ACTION(602)`, `INVENTORY_FULL(501)`, `SESSION_MISMATCH`, `IDEMPOTENCY_CONFLICT`, `FEATURE_DISABLED`(`gathering`) |
| `POST /v1/town/life/potion-crafts` | `{operationId, accountId, accountLeaseId, potionDefinitionId, count}` | `OperationResult`. 409 `INSUFFICIENT_ITEM(502)`, `INVENTORY_FULL`, `SESSION_MISMATCH`, `IDEMPOTENCY_CONFLICT`, `FEATURE_DISABLED`(`crafting`). 모르는 물약, count 0 이하는 400 |

**수치.** `Shinshi.Data/Life/LifeContent`가 `Assets/09.Data/Life/LifeConfig.asset` 값을 옮긴 상수다: 행동력 상한 100, 채집 비용 2, 300초마다 1 회복, 약초 `item_herb` 채집당 2개, 보관 상한 9999, 물약 `item_potion_small`(약초 5), `item_potion_large`(약초 12), 각 1개 산출, 상한 999, 귀속 `Account`.

**행동력.** `ActionEnergyRules`가 `min(100, value + floor((now - anchor) / 300초))`로 회복을 계산한다(now는 지갑 잠금 뒤 읽은 DB 시각). 상한에서 쓰면 `action_anchor = now`, 아니면 회복에 쓴 구간만큼만 앞으로 옮겨 남은 진행분을 보존한다. 0013은 `account_wallets.action_value` 기본값만 100으로 바꾸고 기존 행은 바꾸지 않는다.

**원자성.** 잠금 순서는 계정(`FOR UPDATE`) -> 계정 임대(`FOR SHARE`) -> 지갑(`INSERT ... ON CONFLICT DO NOTHING` 뒤 `FOR UPDATE`) -> 아이템(정의 ID 순 `FOR UPDATE`)이다. 차감, 지급, 원장(`Gathering(19)` source_type `gathering` source_id `zoneId:nodeId`, `Craft(13)` source_type `potion_craft`), 작업 결과가 한 트랜잭션이다. 거절은 효과 전체를 되돌리고 거절 결과만 저장한다. 기능 차단 거절은 저장하지 않는다.

**물약 되돌림.** `GameSessionCodes.EventKind.POTION_USE_REVERTED = 7`, `SessionEventEntry.revertsEventId`. 원 `POTION_USED`가 같은 세션, 같은 참가자에 저장돼 있거나 같은 배치 앞쪽에 있고 아직 되돌리지 않았으면 `reservations.consumed - 1`, `account_items.quantity + 1, reserved + 1`, 원장 `PotionReturn(21)`이다. 아니면 배치 전체가 `SESSION_MISMATCH`다. 0013의 `ux_session_events_potion_revert`(`payload->>'revertsEventId'`, `event_kind = 7`)가 중복 저장의 마지막 방어선이다. 보스전은 참가자별 `POTION_USED - POTION_USE_REVERTED`가 9(단일 원본 `GameSessionCodes.Potion.BOSS_USE_LIMIT`, 던전 `PotionServer`도 같은 상수, `SessionConfig.asset _bossPotionUseLimit`가 다르면 SO 검증 오류)를 넘으면 배치 전체를 `POTION_LIMIT(202)`로 거절한다. 0013 down은 7 행을 지울 수 없어 직전 범위 CHECK를 `NOT VALID`로 건다. 0013은 새 테이블이 없어 런타임 권한 추가가 없다.

## 캐릭터 경험치 (0017)

`0017_character_experience`는 기존 캐릭터의 `initial_experience`와 `total_experience`를 모두 2,850으로 이관한다(2026-10-11 사용자 결정). 장비·정기·부적·진행도는 보존한다. 새 캐릭터 생성은 Trial=2,850, Standard=0을 명시하며 두 열의 DB 기본값은 0이다. 레벨은 저장한 누계와 `CharacterLevelRules`에서 파생한다. 현재 상한은 Lv30·120,350이며 설정 원본은 Unity `CharacterLevelConfigSO`, 현행 내보낸 콘텐츠는 0.1.3/schema 4다(2026-10-22 `growth-content.json` 대조). 초기 경험치 도입 당시 0.1.2/schema 2와 구분한다.

인증된 던전 서버는 `/events`에 `EXPERIENCE_GRANTED`(8)를 보낸다. `experienceSourceKind`는 일반 적 1·정예 2·웨이브 3, `experienceSourceId`는 해당 출정의 고유 source, `experienceSettingsHash`는 예약 설정 hash, `amount`는 상한 적용 전 기본량이다. API는 예약 시 고정한 `game_sessions.experience_settings`, mode/map, 참가자와 이탈 순서, 지급량/hash를 검증하고 기존 세션→계정→캐릭터 잠금 순서에서 `character_experience_events`와 캐릭터 누계/마지막 경험치 세션·순번, `session_events`를 함께 커밋한다. 일반/정예는 같은 enemy source로 중복을 막으며 상한에서 실제 증가 0인 사건도 소비한다. 같은 operationId는 기존 결과를 재생한다. 부정 source를 다른 operationId로 재제출하면 거절하며 같은 배치를 건너뛰지 않는다.

경험치 원장은 UPDATE/DELETE를 막는다. 실행 계정에는 위 권한 SQL의 SELECT/INSERT만 추가한다. 설정이 없는 이전 예약은 새 경험치 지급을 받지 않으며 새 빌드의 시작·입장 검증에서 거절한다. 시작·입장에는 현재 콘텐츠와 예약 설정의 일치가 필요하다. 이미 실행 중인 출정의 이벤트 검증은 고정 설정을 사용한다. 콘텐츠 교체로 이전 출정의 재접속이 막히지 않도록 배포 전 출정을 종료하거나 같은 콘텐츠를 유지해야 한다. down은 경험치 진행을 보존하는 롤백이 아니므로 운영에 실행하지 않는다.

입장 응답의 `experience`는 예약 장비 구성과 별개인 최신 누계/세션/순번/hash다. 총량과 cursor를 한 캐릭터 행에서 읽으므로 저장 ACK보다 재접속 응답이 먼저 도착해도 서버가 중복 합산을 피할 수 있다. 확정된 경험치는 실패·포기·장애 취소 정산에서 차감하거나 다시 지급하지 않는다. 적의 실존과 분류·전투 보상 자격은 신뢰하는 던전 서버의 권위다. API는 실제 웨이브 계획의 처치 수를 재연산하지 않으므로 침해된 서버의 서로 다른 가짜 source 발급까지 증명하는 방어는 제공하지 않는다.

## 공지 분류 (0014)

`service_notices.category`(`Notice`/`Update`/`Event`/`Maintenance`, 기본 `Notice`, CHECK 제약)가 추가됐다. 공개 `GET /v1/service-state`의 `notices[]`와 어드민 공지 작성(API·CLI·웹)이 `category`를 싣고, 값이 없으면 `Notice`다. 런처 소식 탭이 분류를 쓰고 `Update`만 패치노트 탭에 나온다. down은 열만 지운다.

## 유저 경매장과 1:1 거래 (0015)

마을 서버 전용 경로다(서버 임대 bearer와 `X-Shinshi-Instance-Id`, 마을 역할). 일반 로그인 토큰, 던전 서버, 어드민 토큰은 효과 전에 401, 403이다. 경로 상수는 `MarketRoutes`, `TradeRoutes`(공유 계약)이고 ASP.NET 템플릿은 `MarketRouteTemplates`, `TradeRouteTemplates`이며 `MarketHttpTests`가 둘의 일치를 묶는다.

| 경로 | 본문 -> 응답 | 비고 |
|---|---|---|
| `POST /v1/town/market/search` | `MarketSearchRequest` -> `MarketSearchResponse` | 읽기 전용. 정의 ID 묶음(최저가, 총 수량, 등록 수, 최근 체결가), 페이지 20 |
| `POST /v1/town/market/listings/query` | `MarketListingsQueryRequest` -> `MarketListingsResponse` | 읽기 전용. 개당 가격, 등록 순. 보는 사람이 없어 `mine`은 항상 false |
| `POST /v1/town/characters/{id}/market/listings` | `MarketListRequest` -> `MarketOperationResponse` | 기능 키 `market`. 등록 ID = 영수증 ID |
| `POST .../market/purchases` | `MarketBuyRequest` -> `MarketOperationResponse` | 기능 키 `market`. 성사 ID = 영수증 ID, gross, fee, net |
| `POST .../market/listings/{listingId}/cancel` | `MarketCancelRequest` -> `MarketOperationResponse` | 기능 키를 보지 않는다(차단 중에도 회수) |
| `GET .../market/mine`, `GET .../market/history?page=` | -> `MarketMineResponse`, `MarketHistoryResponse` | 읽기 전용. 캐릭터가 없으면 404, 접속 임대가 이 마을에 없으면 409 `WRONG_LOCATION` |
| `POST .../market/ack` | `MarketAckRequest` -> `MarketOperationResponse` | 경제 효과 없음. `seen_at`만 갱신하는 단순 UPDATE라 operations 행을 만들지 않고, 같은 요청을 다시 보내도 이미 표시된 행은 건너뛰어 결과가 같다 |
| `POST /v1/town/trades/prepare` | `TradePrepareRequest` -> `TradeExecutionResponse` | 기능 키 `trade`. 경제 효과 없이 동결된 제안만 `Prepared`로 남긴다 |
| `POST /v1/town/trades/{executionId}/commit` | `TradeCommitRequest` -> `TradeExecutionResponse` | 기능 키 `trade`. `operationId`는 prepare가 저장한 `commitOperationId`와 같아야 하고, 다르면 409 `INVALID_TARGET`(저장 안 함) |
| `POST /v1/town/trades/{executionId}/resolve` | 본문 없음 -> `TradeExecutionResponse` | 막지 않는다. 상태 `Prepared`는 작업 상태 `Pending`(성공 아님)으로 돌려준다 |

**경매장 쓰기.** 모든 쓰기는 캐릭터 계정 actor(`Player`)의 멱등 작업이고 요청 해시에는 경로의 `characterId`와 업무 값만 들어간다. 잠금 순서는 등록 행 -> 계정(uuid 오름차순, 판매자와 구매자를 한 번에) -> 캐릭터 -> 지갑 -> `account_items` -> 장비(`item_id`) -> 원장이다. 만료 판정은 등록 행을 잠근 뒤 별도 문장의 `clock_timestamp()`로 한다(잠금 대기 중에 지난 만료를 `now()`로는 놓친다). 등록은 약초, 미등록 부적 카드(수량형)와 `grade 0, +0, 미착용, 비귀속, 비잠금, 비수련용, 제작품` 장비만 받고(물약은 거절 `MarketNotTradable`) 계정 잠금 뒤 활성 10건을 센다. 수량형은 판매자 `account_items`에서 빼 등록 행으로 옮기고(원장 `MarketEscrow`, 가용량 `quantity - reserved`), 장비는 `escrow_kind = 'Market'` 표시만 한다(판매자 캐릭터의 보관 칸 100을 계속 차지, 착용 중이거나 이미 보관 중이면 거절). 구매는 한 등록의 수량 지정이며 구매자 엽전은 가용량(`currency - currency_reserved`)에서 정확히 빼고(`MarketPurchase`), 수수료 `MarketFeeRules.Fee(gross)`를 소각한 순수령을 판매자 지갑에 즉시 넣고(`MarketProceeds`, 0이면 줄 없음), 구매자 지급(`MarketReceive` 또는 장비 소유 이전 + 이력 `MarketSold`), 등록 수량 감소와 `market_trades`(추가 전용) 한 줄이 같은 트랜잭션이다. 보관 상한(수량형) 또는 100칸(장비)을 넘으면 `INVENTORY_FULL`로 전체를 거절한다. 취소와 만료는 미판매분을 반환한다(수량형은 상한까지 `MarketReturn`, 넘친 몫은 회수함 `MarketReturn` 출처 한 행(30일, 부분 수령), 장비는 보관 표시만 풀고 이력 `MarketReturned`). 만료 유지 작업(`MarketMaintenanceService`, 1분)은 만료된 활성 등록을 50건씩 `ORDER BY expires_at`로 읽고 등록마다 별도 트랜잭션으로 `FOR UPDATE SKIP LOCKED` 뒤 처리한다. actor는 `System`, operationId는 등록 ID에서 만든 이름 기반 Guid라 여러 API 인스턴스가 겹쳐도 반환은 한 번이다(다른 작업이 잡고 있는 행은 저장 없이 되돌려 다음 주기로 미룬다). 활성 장비 등록이 있는 캐릭터는 삭제를 `CharacterHasMarketListing`으로 거절하고, 성장 쓰기(강화, 계승, 착용, 해제, 잠금, 분해)는 보관 중인 장비를 `MarketEscrowed`로 거절하며 성장 스냅샷의 `EquipmentView.inMarket`이 보관 표시를 알린다.

**1:1 거래.** 멱등 actor는 호출한 마을 서버 인스턴스다(`Server`). prepare(operationId = executionId)는 두 계정과 두 캐릭터를 잠근 뒤(실행 행은 잠그지 않아 commit의 실행 행 -> 계정 순서와 거꾸로 맞물리지 않는다) 두 계정의 접속 임대가 호출 마을에 있는지, 어느 쪽이든 `Prepared` 실행이 없는지(`account_a`, `account_b` 모두) 보고 동결한 제안(`offer_a`, `offer_b` JSON)과 `commit_operation_id`를 저장한다. 그 전에 두 계정의 `Prepared` 행을 지연 보정해 이미 끝난 실행을 Busy로 세지 않는다. commit(operationId = `commitOperationId`)은 실행 행 `FOR UPDATE` -> 만료(`clock_timestamp() > prepared_at + 120초` -> `TradeExpired`) -> 계정 -> 캐릭터 -> 지갑 -> `account_items` -> 장비 순서로 잠그고 보유량, 엽전 가용량, 장비 소유와 거래 가능 조건, 받는 쪽 공간(수량형은 같은 자산을 주고받는 순증감 기준 상한, 장비는 캐릭터 100칸 순증감)을 확인한 뒤 차감을 모두 끝내고 지급한다. 원장은 `TradeGive`(-) / `TradeReceive`(+)이고 `entry_key`에 방향 `a2b` / `b2a`가 들어가며 0인 줄은 만들지 않는다. 장비는 소유 계정과 캐릭터를 바꾸고 이력 `Traded`를 남긴다. 거절은 실행기가 효과를 모두 되돌린 뒤 거절만 저장하고, 실행 행의 종결(`Rejected`, 만료는 `Abandoned`)은 그 뒤 별도 짧은 트랜잭션이 `WHERE state = 'Prepared'` 조건부로 적는다. 이 갱신이 빠져도 resolve, prepare, 방치 정리가 `operations`의 commit 결과(`actor = town_instance_id`, `operation = commit_operation_id`)를 읽어 같은 종결을 만든다(지연 보정). 방치 정리(30초)는 만료된 `Prepared` 행에 resolve와 같은 처리를 한다(`SKIP LOCKED`).

**마이그레이션 0015.** `market_listings`(활성 장비 유일 `ux_market_listings_item_active`, 검색 `ix_market_listings_search(definition_id, unit_price, created_at)`, 분류, 판매자, 만료, 장비 캐릭터 인덱스), `market_trades`(추가 전용 트리거, 구매자, 판매자, 정의 ID 시각 인덱스), `player_trade_executions`(계정별 `Prepared` 부분 유일 인덱스 2개, 방치 정리 인덱스), `equipment_instances.escrow_kind/escrow_ref`, `reward_inbox` 출처 `MarketReturn`(CHECK는 `NOT VALID` 뒤 `VALIDATE`, 회수함 출처 유일 인덱스 재생성). down은 경매장, 거래 테이블과 `MarketReturn` 회수함 행을 지우므로 운영 DB에서 실행하지 않는다. 런타임 권한은 위 GRANT 예시를 따른다.

**남은 일.** 장비의 직업(`class_id`)은 장비 정의에 직업 필드가 없어 NULL로 저장한다. 부적 카드는 효과 정의의 `PlayerClass`를 저장하므로 `classId` 검색 대상이며 약초 등 직업 없는 품목은 NULL이다. 검색, 조회, 기록의 `page`는 계약이 페이지 번호라 `OFFSET`을 쓰지만 묶음 수(정의 ID 수), 한 정의의 활성 등록 수, 30일 기록으로 닫혀 있고 페이지는 1000으로 자른다. outbox는 소비자가 없어 쓰지 않는다(판매 결과는 `mine`, `history`로 읽는다). 테스트는 `tests/Shinshi.Backend.Tests/Market/`에 있다. 구현 실행이 보고한 결과는 백엔드 1388건 통과(실제 PostgreSQL 18, 2026-10-08, 병렬 2)이며 이 문서 작성 중 다시 실행하지 않았다. 마을 서버·클라이언트와 붙인 M2 실행은 미검증이다.

## API 관측 (U19)

`Shinshi.Api/Observability`. 배포는 `Logging__Console__FormatterName=shinshi-json`으로 공통 JSON 한 줄 로그(`deploy/LOGGING.md`)를 켜고 `Observability__InstanceId`, `Observability__BuildId`, `Observability__ContentVersion`을 넣는다(없으면 null). `RequestLoggingMiddleware`가 전달 헤더 처리 바로 뒤, 라우팅과 인증보다 앞에서 요청마다 `api.request.completed`(경로 템플릿, 메서드, 상태, 소요 ms, 경로의 GUID sessionId)를 남기고 `ApiMetrics`(Meter `Shinshi.Api`, 히스토그램 `api_request_duration_ms`)에 넣는다. `MetricsReporterService`가 60초마다 `api.metrics`(`data.metrics`: 직전 60초 p50/p95/p99, 표본 수, DB 연결이 있으면 미전달 outbox 수 `settlement_outbox_pending`)를 남긴다. 프레임워크 로그는 값 대신 메시지 틀만, 예외는 형식 이름만 남긴다.

**CLI.** `shinshi-admin auctions list [--state Open|Closing]`, `shinshi-admin service-state allow-build add|remove <buildId> --reason <사유>`(현재 목록과 revision을 읽어 하나만 바꾼 목록을 그 revision으로 저장, 이미 있거나 없으면 저장 안 함, 64개 상한, 그 사이 다른 저장이면 409 `INVALID_STATE`로 거절되고 자동 재시도하지 않음).
## API 생존과 준비 상태 확인 (OP-2)

| 엔드포인트 | 의미 | 응답 |
|---|---|---|
| `GET /health/live` | 프로세스 생존. DB 연결이나 쿼리를 수행하지 않음 | 항상 200 `{"status":"ok"}` |
| `GET /health/ready` | DB 연결과 `SELECT 1`을 요청 전체 2초 제한으로 확인 | 성공 200 `{"status":"ok"}`, 실패/시간초과/DB 설정 누락 503 `{"status":"unavailable"}` |

두 경로는 인증이 필요 없고 `Cache-Control: no-store`를 사용한다. 프로브 내부 오류는 본문이나 로그로 직렬화하지 않으며 기존 요청 완료 로그의 HTTP 503 상태로 실패를 관측한다. 점검 모드는 DB 준비 상태와 별개다. 연결 문자열 없는 공개 상태 전용 호스트도 live는 성공하고 ready는 503이다.

API Dockerfile의 `HEALTHCHECK`는 `/health/live`를 쓴다([deploy/api/Dockerfile](../deploy/api/Dockerfile)). 컨테이너 상태를 DB에 묶으면 unhealthy 재시작 도구가 DB 장애 때 멀쩡한 API를 계속 재시작해 진행 중 요청·정산만 끊기 때문이다. DB 준비 상태는 Prometheus blackbox가 바깥에서 `/health/ready`를 확인해 `ShinshiApiNotReady` 경보로 잡는다([모니터링](../deploy/monitoring/README.md)). 오케스트레이터를 쓰면 liveness는 `/health/live`, readiness는 `/health/ready`로 나눈다.

## API Prometheus 지표 (OP-16)

`GET /metrics`는 Prometheus text exposition `text/plain; version=0.0.4; charset=utf-8`을 제공한다. 외부 패키지를 추가하지 않았다. `Metrics:Enabled` 기본값은 `true`이며 `false`이면 404다. `Metrics:AllowedNetworks`는 CIDR/단일 IP 목록으로 비어 있으면 IPv4/IPv6 루프백만 허용하고 범위 밖은 404다. 환경에서는 `Metrics__Enabled`, `Metrics__AllowedNetworks__0` 등을 사용한다.

허용 여부는 기존 `ForwardedHeaders:KnownProxies` 처리 뒤 `RemoteIpAddress`로 판정한다. 어드민과 같은 원칙으로 신뢰 프록시가 허용 네트워크 안에 있으면 시작을 실패시킨다. 신뢰 프록시가 없는 호스트에 `X-Forwarded-For` 또는 `Forwarded`가 붙은 요청은 404다. 프록시 누락/헤더 위조로 허용 프록시 주소가 원격 사용자 주소처럼 판정되지 않게 운영자/수집기 네트워크를 별도로 명시한다.

| 최종 지표 | 종류/단위 | 의미 |
|---|---|---|
| `shinshi_api_http_requests_total{route,status_class}` | counter, 요청 수 | 완료 요청 누적. 4xx/5xx로 거절/오류를 구분 |
| `shinshi_api_http_request_duration_seconds{route,status_class}` | histogram, 초 | 완료 요청 누적 지연 분포. `_bucket{le}`, `_sum`, `_count` 제공 |
| `shinshi_api_settlement_outbox_pending` | gauge, 행 수 | 미전달 outbox 수. 기존 로그와 같은 `delivered_at IS NULL` 전체 행 집계 |
| `shinshi_api_reconciliation_last_run_timestamp_seconds` | gauge, Unix 초 | 공동 DB 마지막 완료 대사의 `finished_at` |
| `shinshi_api_reconciliation_last_mismatch_count` | gauge, 건 수 | 위 완료 대사의 불일치 수 |
| `api_request_duration_ms` | histogram, ms | 기존 `deploy/LOGGING.md` 규약 별칭. 같은 표본을 밀리초로 제공 |
| `api_request_duration_p50_ms`, `api_request_duration_p95_ms`, `api_request_duration_p99_ms` | gauge, ms | 기존 규약의 직전 60초 분위수. 표본 없으면 `NaN` |
| `api_request_sample_count` | gauge, 건 수 | 직전 60초 분위수 표본 수 |
| `settlement_outbox_pending` | gauge, 행 수 | 기존 규약 별칭. `shinshi_api_settlement_outbox_pending`과 같은 값 |

요청 지표는 경로 템플릿과 상태 분류만 label로 쓰며, 원문 URL/이름/UUID를 넣지 않는다. counter와 histogram은 프로세스 재시작 때 초기화되므로 Prometheus `rate`/`increase`의 reset 처리를 사용한다. 전체 지연 분위수는 histogram 버킷을 합친 뒤 계산하고 인스턴스별 p95를 평균하지 않는다. 기존 60초 `api.metrics` 로그도 유지한다.

DB 지표는 scrape 때 연결 대기를 포함한 2초 제한으로 조회한다. DB 미설정/연결 실패/시간초과는 측정 실패를 0으로 오인하지 않도록 `NaN`을 제공한다. 완료 대사가 한 번도 없으면 대사 timestamp는 `0`, 불일치 수는 `NaN`이다. 대사를 선점하지 않은 API 인스턴스도 공동 DB의 마지막 완료 결과를 제공한다. outbox와 대사 게이지는 복제본 사이에 같은 DB 값을 관측하므로 합산하지 않고 max 등으로 집계한다. 진행 중/실패한 대사는 마지막 완료 값을 덮어쓰지 않아 timestamp의 노후화로 확인할 수 있다. 불일치 경보는 `shinshi_api_reconciliation_last_mismatch_count > 0`, 대사 미실행/노후화는 `time() - shinshi_api_reconciliation_last_run_timestamp_seconds`로 판단한다. 조회 실패로 `NaN`인 경우도 운영 경보에서 별도로 다룬다.

## 운영 웹 화면 (AdminWeb)

`Backend/src/Shinshi.Admin.Web/`는 .NET 10 Blazor Interactive Server 운영 도구다. 기존 `/admin/v1` API·DTO·권한·감사를 HTTP로 사용하며 DB에 직접 연결하지 않는다. 앞의 0008 절에 적힌 API·CLI만 제공하던 범위에 운영 웹을 추가한 현재 구현이다. 기존 API와 CLI 실행 방법도 유지된다.

Interactive Server 시작에 필요한 `blazor.web.js`는 .NET 10 SDK가 암시적으로 추가하는 공식 패키지 `Microsoft.AspNetCore.App.Internal.Assets`에서 온다(공유 런타임에는 없다). 처음 한 번 `dotnet restore Backend/Shinshi.Backend.slnx`로 `Backend/.local/packages`에 받아 두면 이후 `--no-restore`로 빌드할 수 있고, `MapStaticAssets`가 `_framework/blazor.web.js`를 제공한다. Production 환경의 소스 실행도 `UseStaticWebAssets`로 SDK 자산 매니페스트를 로드한다.

API를 먼저 위 로컬 실행 절대로 `http://127.0.0.1:5080`에 시작한 뒤 별도 터미널에서 실행한다. 웹은 DB 연결 문자열이나 API의 비밀키가 필요하지 않으며 최초 Owner 생성·마이그레이션은 기존 API 절차를 따른다.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5081'
$env:AdminWeb__ApiBaseUrl = 'http://127.0.0.1:5080'
dotnet run --project Backend/src/Shinshi.Admin.Web --no-restore --no-launch-profile
```

브라우저에서 `http://127.0.0.1:5081/login`을 열어 운영자 이름·비밀번호·6자리 TOTP로 로그인한다. 로컬 HTTP 실행은 같은 PC 루프백에서만 사용한다. 운영에서는 사설 HTTPS 주소로 접속하며 HTTPS 응답의 세션 쿠키는 `__Host-shinshi.admin.session` 이름과 `Secure`를 사용한다. 실행 예시와 launchSettings의 기본 환경은 Production이며, 예외 페이지는 환경과 관계없이 상세 오류를 노출하지 않는다.

| 화면 | 경로 | 업무 |
|---|---|---|
| 대시보드 | `/`, `/dashboard` | 서비스 상태·노출 중 공지·복구 대상 수·최근 운영 기록·빠른 작업 |
| 캐릭터 | `/characters`(`/accounts`도 같은 화면) | 이름·계정 ID·초대 코드로 대상 확인, 계정 상세·로그인 토큰 폐기·개인 자산 조정 |
| 캐릭터 프로필 | `/characters/{characterId}` | 직업·수련·장비·부적·재화·최근 출정을 한 화면에(`GET /admin/v1/characters/{id}`) |
| 지급 | `/grants` | 여러 대상 확인·작성·상세·실행·취소 |
| 서비스 상태 | `/service-state` | 점검·기능 차단·허용 빌드 저장 |
| 공지 | `/notices` | 목록·작성·즉시 종료 |
| 로그 | `/logs` | 원장·아이템 이력·세션 이벤트·작업·감사 조회 |
| 장애 복구 | `/recovery` | 대상 세션·경매 조회, 정산 재시도·서비스 장애 취소·격리 세션 강제 종료 |
| 초대 | `/invites` | 코드 발급·폐기, 최초 응답 원문 표시 |
| 운영자 | `/operators` | Owner의 계정 목록·생성·비활성화 |

화면 사용 규칙: 상단 검색과 모든 대상 입력은 "캐릭터 이름 또는 계정 ID"를 받는다. UUID는 계정 ID로 그대로 쓰고 이름은 `POST /admin/v1/accounts/search`의 `characterName`으로 풀어 칩으로 확정하며, 못 찾으면 그 자리에 오류를 보인다. 날짜·시각 입력은 브라우저 달력(`datetime-local`)이고 한국 시간(KST) 기준이다. API에는 UTC 밀리초로 보내고 화면 표시는 KST, 마우스를 올리면 UTC가 보인다. 입력 칸의 placeholder에 보이는 값이 실제 기본값이다. 비워 두면 그 값을 보내고 확인 대화상자에 "기본값이 적용된 항목"과 "기본 사유 사용"을 표시한다. 기본값: 조회 사유 "운영 조회", 쓰기 사유는 동작별 문구, 조회 개수 50, 로그 기간 최근 24시간, 공지 시작 지금·종료 시작+24시간·대상 런처/게임·중요도 안내, 지급 수령 기한 30일, 초대 사용 1회·유효 24시간(API·CLI는 두 값을 필수로 받으므로 웹 기본값이다). 디자인 토큰과 컴포넌트는 `wwwroot/admin.css`·`wwwroot/admin.js`와 `Components/Shared`에 있으며 외부 글꼴·CDN·인라인 스크립트를 쓰지 않는다.

| 설정 | 기본값 | 의미 |
|---|---|---|
| `AdminWeb__ApiBaseUrl` | `http://127.0.0.1:5080` | 웹 서버가 호출할 API 주소. 브라우저가 API에 직접 접근하지 않는다 |
| `AdminWeb__AllowedNetworks__0` 등 | 빈 목록 | 운영 웹을 열 운영자 CIDR/단일 IP. 비어 있으면 IPv4/IPv6 루프백만 허용하고, 명시하면 기본 루프백도 대체한다 |
| `ForwardedHeaders__KnownProxies__0` 등 | 빈 목록 | 웹 앞 신뢰 프록시 IP만. 허용 운영자 범위 안의 프록시 설정은 시작 실패다 |
| `ASPNETCORE_URLS` | 호스트 기본값(명시 권장) | appsettings에는 `Urls`를 두지 않는다. 로컬 5081은 launchSettings 또는 위 실행 예시로 지정한다. 환경 변수 사용 시 `--no-launch-profile`로 프로필의 URL 덮어쓰기를 피한다. 컨테이너는 `http://+:8080` |

허용 네트워크는 신뢰 프록시의 forwarded headers 처리 뒤 `RemoteIpAddress`로 판정하고 허용 밖 요청은 404다. 신뢰 프록시가 없는데 `X-Forwarded-For` 또는 `Forwarded`를 붙인 요청도 404다. 프록시는 `X-Forwarded-For`·`X-Forwarded-Proto`를 보내고, 프록시 자신의 IP는 운영자 허용 범위에 넣지 않는다. 웹 접근 허용과 API의 `Admin__AllowedNetworks__*`는 별도 설정이다. API에는 웹 서버 출처를 허용하며 브라우저의 IP 헤더를 전달하지 않는다. 따라서 API 감사의 요청 IP는 웹 서버 출처이고 기존 API 정책을 변경하지 않는다.

로그인 결과의 API bearer는 웹 서버 메모리에만 둔다. 브라우저에는 무작위 세션 ID 쿠키만 보내고 토큰을 HTML·JavaScript·localStorage·sessionStorage에 넣지 않는다. 쿠키는 `HttpOnly`·`SameSite=Strict`, HTTPS에서는 `__Host-` 이름·`Secure`·`Path=/`를 사용하고 Domain을 설정하지 않는다. HTTPS 요청은 평문 HTTP용 세션 쿠키를 인증에 사용하지 않는다. 세션은 API가 응답한 만료시각과 웹 로그인 후 30분 중 이른 시각을 따르며 API가 401을 반환하면 웹 세션을 폐기하고 재로그인을 요구한다. 로그인·로그아웃 POST는 antiforgery 토큰을 검증한다. 로그아웃과 재로그인은 이전 API 세션 폐기를 시도하고 로컬 세션을 닫는다. API 폐기 실패는 토큰 없는 경고 로그와 화면 안내로 알리며, 이전 API 토큰은 만료 시각까지 유효할 수 있다. 세션 저장소는 운영자당 5개·전체 1,000개로 제한하고 초과 시 가장 오래된 세션을 폐기한다. 만료 정리는 주기적으로 및 조회 시 수행한다. 업무 화면은 만료 시각과 최대 15초 간격 확인으로 유휴 상태에서도 기존 조회·민감 데이터를 지우고 로그인 화면으로 이동한다.

웹을 통해 로그인하는 운영자는 API에서 같은 웹 서버 출처 IP로 보인다. 기존 API 인증 한도(IP당 로그인 실패 20회/분, 인증 요청 60회/분)를 공유하므로 API 정책은 변경하지 않고 웹에서 실패를 먼저 제한한다. IP와 대소문자를 정규화한 로그인 이름별로 실패 1~4회 후 1·2·4·8초 지연, 5회 실패 후 60초 차단하며 한 IP나 이름에서 진행 중인 인증은 1개만 허용한다. 메모리 기록은 총 4,096개 키까지만 보관하고, 비활성 기록은 15분 후 정리한다. 상한에 도달하면 새 키의 인증을 거절한다. API의 429는 공유 인증 한도 안내로 표시한다. 여러 출처에 걸친 요청과 정상 로그인 폭주에서도 API의 공유 한도가 최종 적용된다.

서버는 유효 세션 없이 업무 경로를 요청하면 `/login`으로 이동시키고 `/_blazor` 연결·협상은 401로 거절한다. `/login`, `/healthz`, 실제 MapStaticAssets 정적 자산만 비로그인 접근을 허용한다. CSP는 기본·스크립트 출처를 `'self'`로, WebSocket 출처를 현재 호스트의 ws/wss로 제한한다. Blazor 재접속 UI가 삽입하는 스타일을 위해 style-src에만 `'unsafe-inline'`을 허용하며 inline 스크립트와 eval은 허용하지 않는다. HTTPS 응답에는 HSTS(`max-age=31536000`)를 설정하며 신뢰 프록시의 HTTPS scheme 복원 뒤 판정한다.

화면의 역할별 메뉴·버튼은 Viewer/Operator/Admin/Owner로 구분하고 실제 권한은 API가 최종 판정한다. 민감 조회는 사유를 입력하고 업무 변경은 사유와 확인 단계를 거친다. 확인한 쓰기 요청에서 `operationId`를 한 번 생성하며 202, 429, 5xx, 통신 실패처럼 결과가 불명확한 경우 같은 ID와 확인 때 고정한 본문으로 수동 재시도한다. 재시도할 때 입력을 고치지 말고 먼저 결과를 확인한다. 새로운 의도는 별도 확인과 새 작업으로 시작한다.

초대 코드와 운영자 초기 비밀번호·TOTP 비밀은 API 최초 성공 응답 때만 볼 수 있다. 화면을 닫거나 다른 화면으로 이동하기 전에 안전하게 전달·보관하고 공유 화면/브라우저 캡처에서 지운다. 웹은 이 비밀을 세션이나 별도 저장소에 보존하지 않는다. 응답을 잃었거나 같은 작업 재전송에 비밀이 없으면 기존 초대/운영자를 폐기·비활성화한 뒤 새 작업으로 발급한다.

Data Protection 공급자와 키 관리 저장소도 프로세스 메모리에 둔다. Windows 사용자 키 저장소나 디스크에 키를 기록하지 않는다. 메모리 XML 저장소에는 별도 XML 암호화를 두지 않아 시작 시 프레임워크가 암호화 저장 경고를 남길 수 있으나 키를 파일에 보존하지 않는다.

운영 배치는 사설 HTTPS와 단일 웹 인스턴스를 전제로 한다. 프로세스 재시작 시 메모리 세션을 잃어 모든 운영자가 다시 로그인한다. 여러 인스턴스는 공유 세션 저장소와 sticky session 검토가 필요하며 현재 구현만으로 확장 배치를 검증했다고 보지 않는다. Dockerfile과 배치 설정은 [운영 웹 컨테이너](../deploy/admin-web/README.md)에 있다. 실제 API를 향한 운영자 조작·브라우저 시각 확인·컨테이너 배포·프록시 뒤 HTTPS 접속은 자동 HTTP 대역 테스트와 별도로 검증해야 한다.

## 소셜: 친구, 귓속말, 길드, 길드 채팅 (0019)

설계는 `docs/design/social-system.md`. 경제 효과가 없어 operations, 원장, outbox를 쓰지 않고 **상태 기준 멱등**이다(같은 요청 재전송은 이미 목표 상태면 성공, 탈퇴와 추방은 `membershipId`, 위임은 `expectedLeaderVersion`, 길드 생성은 `createRequestId`로 늦은 재전송을 막는다). 경로 상수는 `Shinshi.Contracts.SocialRoutes`, 템플릿은 `Shinshi.Api/Social/SocialRouteTemplates.cs`, 거절 코드는 `ContractCodes.RejectReason`의 1600대다. 모든 경로는 서버 임대 Bearer와 `X-Shinshi-Instance-Id`가 필요하다. 관리 경로와 스냅샷은 마을만(던전 403), 귓속말, 길드 채팅, 전달함 꺼내기는 마을과 던전이 부른다. 경로의 행위 캐릭터는 호출 인스턴스에 만료 전 계정 임대(`account_leases.character_id`)가 있어야 하며 없으면 409 `SOCIAL_NOT_CONNECTED`다. 판정 순서는 서버 자격(401/403) -> 형식(400) -> 기능 차단(`social`, 409 `FEATURE_DISABLED`, 스냅샷, 탈퇴, 추방, 꺼내기는 막지 않음) -> 캐릭터별 인메모리 한도(관리 쓰기 1분 30회, 친구 요청과 길드 초대 1시간 30회, 채팅 1분 60회, 429와 `Retry-After`) -> 저장소다.

쓰기는 `SessionDb.BeginAsync` 한 트랜잭션이며 `SessionDb.RetryAsync`로 경합, 제한 초과만 다시 시도한다. 잠금 순서는 캐릭터 행(`FOR NO KEY UPDATE`, uuid 오름차순, 한 문장) -> 길드 행(`FOR UPDATE`)이고, 캐릭터 삭제는 계정 -> 삭제 캐릭터(이번에 `FOR UPDATE`에서 `FOR NO KEY UPDATE`로 낮춤) -> 길드다. 정원은 조건부 갱신(`member_count < 30`)의 영향 행 수와 CHECK(30)로, 같은 이름 동시 생성은 `ON CONFLICT (name_key) WHERE disbanded_at IS NULL DO NOTHING`으로 판정한다. 업무 거절은 롤백하므로 부분 적용이 없다. 길드원이 있는 길드장의 캐릭터 삭제는 409 `CHARACTER_IS_GUILD_LEADER`, 혼자인 길드장은 해체, 길드원은 탈퇴하고 친구, 요청, 본인 앞 전달함 행을 지운다. 관계 행은 키 순서로 잠근 뒤 지워 서로 친구인 두 캐릭터의 동시 삭제가 교착되지 않는다.

전달함(`social_deliveries`)은 귓속말(kind 0), 길드 채팅(1), 소셜 알림(2) 모두 행을 먼저 남기고 클라이언트 수신 확인을 받은 행만 지운다(60초 만료). 발신 응답의 `localDeliveries`는 호출 인스턴스에 있는 수신자의 남은 행(새 행 이하)을 수신자별 오름차순으로 주고(수신자당 20개 넘으면 그 수신자는 꺼내기에 맡김), 돌려준 행은 `next_attempt_at`을 5초 뒤로 미룬다. 꺼내기는 확인(`delivery_id`와 수신 캐릭터가 모두 맞고 그 캐릭터 임대가 호출 인스턴스에 있는 행만)을 먼저 지우고 `next_attempt_at`이 지난 행을 수신자당 20개, 전체 200개 `delivery_id` 순으로 골라 같은 문장에서 5초 뒤로 미룬다. 알림 행은 접속 중인 상대에게만 넣고 그 대상이 응답의 `changedCharacterIds`다. `SocialMaintenanceService`(DB 연결이 있을 때만 등록)가 60초마다 만료된 요청과 전달함 행을 지운다. 계정 임대의 `character_id`는 마을 입장 교환이 채우고(재입장 `ON CONFLICT DO UPDATE` 포함), 던전 이관은 그대로 두며, `TryAcquireAsync`는 비운다.

0019 down은 소셜 상태를 모두 지우므로 운영 DB에서 실행하지 않는다. 시험은 `tests/Shinshi.Backend.Tests/Social/`에 있다.

## 프롤로그와 성장 메뉴 안내 (0023)

신입 캐릭터가 마을을 거치지 않고 1인 프롤로그 출정(숲 튜토리얼, 보스 월백, 마무리)을 한 뒤 마을로 간다. 경로 상수는 공유 `PrologueRoutes`, 단계와 한도는 `PrologueCodes`다. 0023은 `characters.prologue_stage`(0 INTRO ~ 4 DONE), `prologue_wolbaek_potions`(0~9), `prologue_talisman_checked`를 더하고 이 파일 전에 있던 캐릭터는 완료(4)로 채운다. 새 캐릭터 생성은 0을 명시한다. `account_menu_guides`(계정, 메뉴 ID 유일), `prologue_completions`(캐릭터당 완료 지급 한 번의 표식)를 만들고 `game_sessions.mode`에 `Prologue`를 허용한다(보스 ID 허용, 1인 CHECK).

| 경로 | 주체 | 결과 |
|---|---|---|
| `GET /v1/characters/{characterId}/prologue` | 플레이어 토큰 | `PrologueStateResponse`. 다른 계정 캐릭터나 삭제한 캐릭터는 404 `CHARACTER_NOT_FOUND`. `accountCompleted`는 `account_progress.intro_completed_at`(신시 안내 완료와 같은 기록) |
| `POST /v1/prologue/entry` | 게임 하위 세션 토큰 | `JoinTicket`(던전 role). 점검 503 `MAINTENANCE`, `dungeon_entry` 차단 409 `FEATURE_DISABLED` |
| `POST /v1/prologue/skip` | 플레이어 토큰 | `OperationResult`. 계정이 이미 완료했을 때만(`PROLOGUE_REQUIRED`) |
| `POST /v1/game-sessions/{sessionId}/prologue/stage` | 던전 서버 임대 | `OperationResult`, committedRevision = 저장 단계 |
| `POST /v1/game-sessions/{sessionId}/prologue/complete` | 던전 서버 임대 | `OperationResult`, 정산 전에 부른다 |
| `GET /v1/accounts/me/menu-guides` | 플레이어 토큰 | `MenuGuideListResponse`(바이트 순 정렬) |
| `POST /v1/accounts/me/menu-guides/{menuId}/complete` | 플레이어 토큰 | 같은 응답. 형식 불량 400 `MENU_GUIDE_INVALID`, 64개 초과 새 ID 409 `MENU_GUIDE_LIMIT`, 같은 메뉴 재전송은 200 |

입장은 한 작업 트랜잭션(actor = 계정, operationId 멱등)에서 계정 -> 캐릭터 -> 계정 접속 임대 -> 유휴 던전(SKIP LOCKED) -> 로그인 묶음 -> 서버 임대 순서로 잠근다. 캐릭터 소유, 삭제 예약 없음, 단계 DONE 아님을 확인하고, 살아 있는 접속 임대가 있으면 `ALREADY_CONNECTED`, 다른 열린 출정이 있으면 `RESERVATION_CONFLICT`다. 유휴 던전을 배정해 `game_sessions`(mode `Prologue`, 1인, mapId `prologue_forest`, battleId `prologue`, bossId `boss_wolbaek`, 난이도 0, 티켓·물약 예약 없음)와 참가자(일반 예약과 같은 전투 구성 스냅샷)를 넣는다. `reserved_by_instance_id`는 배정 던전 자신이고, 계정 접속 임대도 그 던전에 직접 만든다(만료 = 예약 만료 150초). 그래서 기존 `lease-transfer`가 수정 없이 던전 -> 같은 던전으로 임대를 옮기고, 접속하지 않으면 기존 예약 만료 해제가 출정과 임대를 함께 정리한다. 입장 토큰은 마을이 받는 던전 입장권과 같은 형식·클레임·60초 일회 토큰이지만 원문을 (계정, operationId)에서 HMAC으로 파생해 저장하지 않는다. 같은 operationId 재전송은 처음 세션의 같은 입장권을 돌려주고, 이미 교환했거나 새 입장이 대체했으면 401 `TOKEN_REVOKED`, 만료면 401 `TOKEN_EXPIRED`다(새 operationId로 다시 요청). 같은 캐릭터의 시작 전(Reserved) 프롤로그 출정이 살아 있으면 새 operationId 입장은 그 출정을 이어 쓰며 새 입장권을 발급하고 이전 미교환 입장권을 폐기한다. 시작한 프롤로그의 재접속은 기존 `rejoin-ticket`이다.

프롤로그 출정은 기존 흐름(교환 -> `lease-transfer` -> `/start` -> `/events` -> `/settlement`)을 그대로 지난다. 교환 응답의 `mode`는 2, `prologueStage`·`prologueWolbaekPotions`·`prologueTalismanChecked`에 캐릭터의 저장 상태를 싣는다. `/events`는 정기 적립, 경험치 이벤트, 0이 아닌 종료 정산안을 `SETTLEMENT_LIMIT_EXCEEDED`로 거절하고(물약 사용은 예약이 없어 `POTION_LIMIT`), 정산은 성공이어도 클리어 기록과 경매 없이 닫고 접속 임대를 푼다. 비정상 종료와 장애 취소는 캐릭터 단계를 바꾸지 않는다. 단계 저장은 앞으로만(같은 단계 재전송은 확정, 뒤로·DONE·물약 0~9 밖은 `PROLOGUE_STAGE_INVALID`) 가고 부적 조사 기록은 되돌리지 않는다. 완료는 한 트랜잭션에서 계정 진행의 신시 안내 완료(이미 있으면 그대로), 캐릭터 단계 DONE, `prologue_completions`(Played) 삽입을 하고 처음 넣은 쪽만 완료 지급품을 준다. 건너뛰기도 같은 표식(Skipped)과 지급을 쓴다.

완료 지급품은 `growth-content.json`의 `progression.prologueCompletionGrant`(키가 없으면 빈 묶음, 행동력 0만 허용)이며 품목은 아직 정하지 않았다. 묶음이 있으면 `InitialGrantService`가 최초 지원과 같은 방식(원장 사유 `INITIAL_GRANT`, 보관 상한 미적용)으로 지급한다. 실제 PostgreSQL에서의 프롤로그 시험(`Prologue/ProloguePostgresTests`)은 아직 실행하지 않았다.
