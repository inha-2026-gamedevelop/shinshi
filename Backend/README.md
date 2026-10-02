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
```

런타임 역할은 테이블/스키마 소유자나 migrator 역할의 멤버로 두지 않는다. `schema_migrations`의 조회/수정 권한과 스키마 CREATE 권한을 런타임에 부여하지 않는다. 추가 테이블의 런타임 권한도 자동 확대하지 않고 작업별로 검토한다. 이 예시는 현재 테이블 기준이며, 후속 추가 전용 원장/감사 테이블에는 SELECT/INSERT만 허용한다. PostgreSQL 임시 테이블은 별도 권한이므로 임시 테이블 생성까지 차단하려는 개발 DB에서는 PUBLIC과 런타임 역할의 TEMPORARY 권한도 검토한다.

초기 상태는 점검 중, 허용 빌드 목록은 비어 있다. 외부 환경 설정 `ServiceState__Maintenance`, `ServiceState__Notice`, `ServiceState__AllowedBuildIds__0` 등으로 설정한다. 운영 상태 DB 저장/관리 API는 후속 작업이다. PostgreSQL 18 볼륨은 공식 이미지의 경로에 맞춰 `/var/lib/postgresql`에 둔다. [공식 이미지](https://hub.docker.com/_/postgres)

DB 통합 테스트를 실행하려면 API를 종료한 뒤 테스트 전용 로컬 연결을 설정한다. 런타임 역할은 스키마를 만들지 못하므로 테스트 연결로 사용하지 않는다.

```powershell
$env:SHINSHI_TEST_CONNECTION_STRING = '<dedicated local test database connection with schema-create permission>'
dotnet test Backend --no-restore
```

`IdempotentOperationExecutor`는 인증한 actor와 operation UUID, 작업 종류, JSON 본문을 받는다. 본문 객체 키를 정규화하고 작업 종류와 함께 해시한다. 같은 키와 본문은 최초 결과를 반환하고 다른 본문은 `IdempotencyConflict`로 거절한다. 효과 콜백은 제공한 DB 연결/트랜잭션만 사용한다. 외부 I/O나 확률 재추첨을 넣지 않는다. 실패는 전체 롤백, 업무 거절은 효과만 롤백하고 거절 결과를 저장한다. 공개 변경/조회 엔드포인트는 아직 제공하지 않는다.

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

추가 전용 `economy_ledger`, `item_history`, `outbox_inbox`에는 런타임 UPDATE/DELETE/TRUNCATE 권한을 주지 않는다. 0003의 트리거도 이 변경을 거절한다. 앞으로 만드는 `admin_audit_log`도 동일하게 SELECT/INSERT만 부여하며 테이블/스키마 소유권 또는 역할 멤버십으로 권한을 상속하지 않게 한다. 위 권한 SQL의 outbox UPDATE는 전달 상태의 세 열로 제한한다. 기존 DB에서 이미 전체 outbox UPDATE를 부여했다면 먼저 그 전체 권한을 회수한 뒤 열 권한만 부여한다.

0003 down은 검토용이며 인증/원장/전달 이력을 삭제하므로 운영 DB에서 실행하지 않는다. 신규 테스트는 `Auth/`, `Ledger/`에 있다. join의 계정 행 잠금 대기 사슬과 outbox 작업자의 콜백 장벽으로 실제 잠금 겹침을 강제하는 DB 사례를 포함한다. 공유 `MigrationTestResources`가 현재 마이그레이션 개수와 역순 down 체인을 읽는다. DB 환경이 없는 결과를 실제 PostgreSQL 동시성 통과로 해석하지 않는다.


## 세션 연결과 검증 (0004)

세션 API의 `ServerCallerAuthenticator`는 Auth가 검증한 임대를 `ServerCaller(instanceId, role)`로 연결한다. 원장 어댑터는 같은 `OperationExecutionContext`의 트랜잭션으로 Tickets, Essence, AccountItem을 기록한다. `Program.cs`는 런타임 `ConnectionStrings:Postgres`가 설정된 경우에만 `AddShinshiGameSessionMaintenance`를 등록하여 예약 만료 해제와 소실 세션 정산을 5초마다 처리한다. 설정이 없는 공개 상태 조회는 인증 키와 DB 연결 없이 실행할 수 있다.

설정 키의 자리표시자는 `.env.example`에 있다. 패키지는 `Backend/.local/packages`의 기존 캐시를 사용하며 새 패키지 없이 `--no-restore`로 빌드/테스트한다. `ProgramCompositionTests`는 실제 composition root의 DI와 미들웨어 순서, DB 설정에 따른 유지보수 등록을 검사한다. 어댑터 단위 테스트와 실제 PostgreSQL 접속 토큰 HTTP 테스트가 포함되어 있으며 DB 테스트는 `SHINSHI_TEST_CONNECTION_STRING`이 없으면 Skip한다. Skip을 DB 경합/SQL 검증 통과로 해석하지 않는다.
교환은 출정 예약과 복귀의 던전 토큰(`serverRole=1`)과 마을 입장 토큰(`serverRole=0`)을 처리한다. 기존 스키마에는 계정 제재 판정 데이터가 없으므로 `SANCTIONED(808)`는 인증 결과의 HTTP 전파 계약만 검사하고 실제 제재 조회는 후속 운영 모듈에 남긴다. `MAINTENANCE(807)`는 점검 설정일 때 토큰 소비 전에 반환한다. 기존 `Contracts/Auth`의 Consume DTO와 경로 상수는 이번 Assets 쓰기 허용 범위 밖이어서 원본을 보존하고 Backend의 공유 소스 컴파일 대상에서 제외했다.
## 임대 생존 분리, 복귀, 복구 격리 (0005)

서버 프로세스는 `POST /v1/server-instances/lease`에 `Authorization: Bearer <서비스 자격 원문>`, `X-Shinshi-Instance-Id`, 본문 `ServerLeaseRequest{instanceId, serverRole, bootId}`로 임대를 받는다. 서버 경로 중 임대 없이 부르는 유일한 경로다. 서비스 자격은 설정 해시와 메모리에서만 비교하므로 틀린 자격은 DB를 열지 않는다. 인스턴스 행이 없으면 등록 전 자리(`Offline`, boot 없음, 자리 주소)로 만들고 임대를 그 bootId에 묶는다. 이어서 같은 임대로 `register`를 부르면 실제 주소와 콘텐츠 버전으로 `Idle`이 된다. 만료 전 같은 경로로 갱신한다. 응답 401은 자격 오류, 403은 역할 불일치, 409 `SESSION_TERMINAL`은 장애 취소로 확정된 boot이며 새 bootId로 프로세스를 다시 시작해야 한다.

임대 인증과 생존 판정은 분리한다. 임대 검증은 heartbeat 시각을 보지 않는다. heartbeat, 드레인, 같은 boot 등록 재전송은 시간 조건 없이 Offline이 아니면 같은 bootId를 되살린다. 생존 판정(15초)은 효과 지점인 배정, `/start`, `/events`, 입장 토큰 발급과 교환에서만 한다. 되돌릴 수 없는 사망은 장애 취소 트랜잭션이 그 boot를 `Offline`으로 쓰는 순간뿐이다. 임대는 발급 boot의 등록, heartbeat, 드레인에만 쓸 수 있고, 배정은 등록된 현재 boot에 묶인 임대가 있는 던전만 고른다. 새 bootId가 등록하면 이전 boot의 열린 출정은 복구 대상이다. 유지보수 작업은 API 시작 뒤 heartbeat 만료 시간의 두 배(30초) 동안 소실 복구를 하지 않는다. API가 내려가 있던 동안의 오래된 heartbeat를 서버 소실로 보지 않기 위해서다. 만료 예약 해제는 계속한다.

접근, 갱신, 서버 임대, 입장 토큰 원문은 길이와 문자 집합이 이전과 같지만 마지막 8바이트가 무작위 부분의 HMAC 태그다. 태그가 맞지 않는 bearer는 DB 조회 없이 거절한다. 0005 이전에 발급한 토큰과 초대는 태그가 없어 무효가 되므로 재로그인과 새 초대가 필요하다. 인증 실패(bearer 거절, 초대/갱신 교환 401, 서버 임대 자격 401)는 IP별로 1분에 20회까지 세고, 넘으면 창이 끝날 때까지 토큰 검증 없이 429(`Retry-After`)로 거절한다. 자격 교환 경로(`/v1/auth/exchange`, 마을 입장권, 복귀 입장권)는 IP별 1분 60회 ASP.NET Core 요청 한도도 받는다. 0006에서 서버 임대 경로의 실패 처리와 한도, 프록시 IP 복원이 바뀌었다(0006 절).

`POST /v1/game-sessions/{sessionId}/rejoin-ticket`은 플레이어 접근 토큰으로 부르며 응답은 `JoinTicket`이다. 출정 Running, 요청자 계정의 참가자 Active, 로그인 묶음이 살아 있을 때만 60초 일회 토큰을 발급하고 같은 참가자의 미교환 토큰은 폐기한다. 시작 전은 409 `WRONG_PHASE`, 다른 계정은 403 `SESSION_MISMATCH`, 정산된 참가자는 409 `ALREADY_SETTLED`다. 마을 입장 토큰은 `PostgresAuthRepository.IssueTownJoinTokenAsync`(플레이어 주체, 캐릭터, 마을 인스턴스)로 발급하며 HTTP 발급 경로는 아직 없다. 교환 응답의 `participantId`, `sortieId`는 null이다. 다른 살아 있는 서버에 계정 임대가 있으면 `ALREADY_CONNECTED`다.

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

