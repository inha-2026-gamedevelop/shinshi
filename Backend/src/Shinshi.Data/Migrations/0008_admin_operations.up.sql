-- 운영자 계정. 게임 계정과 분리하며 지우지 않고 disabled_at으로 끈다(감사 로그와 지급 기록이 이 행을 참조한다).
-- login_name은 소문자로 정규화해 저장한다. 비밀번호는 PBKDF2-SHA256 해시와 솔트만, TOTP 비밀은 AES-GCM 암호문만 둔다.
-- totp_last_step: 마지막으로 받은 TOTP 시간 스텝. 같은 스텝 이하의 코드는 재사용으로 거절한다
CREATE TABLE admin_users
(
    admin_id uuid PRIMARY KEY,
    login_name text NOT NULL UNIQUE CHECK (login_name ~ '^[a-z0-9][a-z0-9._-]{2,63}$'),
    password_hash bytea NOT NULL CHECK (octet_length(password_hash) = 32),
    password_salt bytea NOT NULL CHECK (octet_length(password_salt) = 16),
    password_iterations integer NOT NULL CHECK (password_iterations >= 1000),
    totp_secret_encrypted bytea NOT NULL CHECK (octet_length(totp_secret_encrypted) BETWEEN 45 AND 128),
    role integer NOT NULL CHECK (role BETWEEN 0 AND 3),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    created_by uuid NULL REFERENCES admin_users (admin_id),
    disabled_at timestamptz NULL,
    failed_attempts integer NOT NULL DEFAULT 0 CHECK (failed_attempts >= 0),
    locked_until timestamptz NULL,
    totp_last_step bigint NOT NULL DEFAULT 0 CHECK (totp_last_step >= 0),
    CHECK ((disabled_at IS NULL) OR (disabled_at >= created_at))
);

-- 어드민 접근 토큰. 원문은 응답으로만 주고 HMAC 해시만 저장한다. 게임 토큰 테이블과 섞지 않는다
CREATE TABLE admin_sessions
(
    session_id uuid PRIMARY KEY,
    admin_id uuid NOT NULL REFERENCES admin_users (admin_id),
    token_hash bytea NOT NULL UNIQUE CHECK (octet_length(token_hash) = 32),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    expires_at timestamptz NOT NULL,
    revoked_at timestamptz NULL,
    CHECK (expires_at > created_at),
    CHECK ((revoked_at IS NULL) OR (revoked_at >= created_at))
);
-- 운영자 비활성화가 그 운영자의 열린 세션을 한 번에 폐기한다
CREATE INDEX ix_admin_sessions_admin_open ON admin_sessions (admin_id, session_id) WHERE revoked_at IS NULL;
CREATE INDEX ix_admin_sessions_expiry ON admin_sessions (expires_at, session_id);

-- 추가 전용 운영자 감사 로그. 런타임 역할에는 SELECT, INSERT만 주고 트리거로도 막는다.
-- 어드민 쓰기는 효과와 같은 트랜잭션에서 기록하므로 업무 거절로 효과가 롤백되면 감사 행도 남지 않는다(커밋된 변경만 기록).
-- operation_id가 있으면 같은 운영자의 operations 행을 가리켜야 한다. admin_id가 없는 System 행은 FK 검사 대상이 아니다
CREATE TABLE admin_audit_log
(
    audit_id uuid PRIMARY KEY,
    occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    admin_id uuid NULL REFERENCES admin_users (admin_id),
    actor_kind text NOT NULL CHECK (actor_kind IN ('Admin', 'System')),
    admin_role integer NULL CHECK (admin_role BETWEEN 0 AND 3),
    action text NOT NULL CHECK (length(action) BETWEEN 1 AND 100),
    target_kind text NOT NULL CHECK (length(target_kind) BETWEEN 1 AND 64),
    target_id text NOT NULL CHECK (length(target_id) BETWEEN 1 AND 200),
    before_state jsonb NULL CHECK ((before_state IS NULL) OR (jsonb_typeof(before_state) = 'object')),
    after_state jsonb NULL CHECK ((after_state IS NULL) OR (jsonb_typeof(after_state) = 'object')),
    reason text NOT NULL CHECK ((length(btrim(reason)) > 0) AND (length(reason) <= 500)),
    operation_id uuid NULL,
    approval_id uuid NULL,
    self_approved boolean NOT NULL DEFAULT false,
    request_ip_hash bytea NULL CHECK ((request_ip_hash IS NULL) OR (octet_length(request_ip_hash) = 32)),
    CHECK (((actor_kind = 'Admin') AND (admin_id IS NOT NULL) AND (admin_role IS NOT NULL))
        OR ((actor_kind = 'System') AND (admin_id IS NULL) AND (admin_role IS NULL))),
    FOREIGN KEY (admin_id, operation_id) REFERENCES operations (actor_id, operation_id)
);
CREATE INDEX ix_admin_audit_log_time ON admin_audit_log (occurred_at, audit_id);
CREATE INDEX ix_admin_audit_log_admin ON admin_audit_log (admin_id, occurred_at, audit_id);
CREATE INDEX ix_admin_audit_log_target ON admin_audit_log (target_kind, target_id, occurred_at, audit_id);
CREATE INDEX ix_admin_audit_log_operation ON admin_audit_log (admin_id, operation_id) WHERE operation_id IS NOT NULL;
-- 대상 종류만으로 거르는 감사 조회(targetId 없이 targetKind와 기간)가 시간순으로 읽는다
CREATE INDEX ix_admin_audit_log_target_kind ON admin_audit_log (target_kind, occurred_at, audit_id);
CREATE TRIGGER admin_audit_log_append_only BEFORE UPDATE OR DELETE OR TRUNCATE ON admin_audit_log
    FOR EACH STATEMENT EXECUTE FUNCTION reject_append_only_mutation();

-- 운영자 작업 키(admin_id, operation_id)를 대상과 요청 해시에 묶는다. 수동 복구(정산 재시도, 장애 취소, 강제 종료)는
-- 서버 복구를 부르기 전에 이 행을 커밋한다. 정산 재시도와 장애 취소의 operations 행은 서버 복구가 끝난 뒤에야 생기므로,
-- 이 행이 없으면 같은 operationId로 다른 세션을 동시에 복구하거나 어드민 단계가 실패한 뒤 다른 세션을 복구할 수 있다.
-- 모든 어드민 업무 변경도 효과 전에 이 행을 커밋한다. 일반 쓰기와 수동 복구가 같은 키로 경합해도 최초 요청만 실행된다.
-- 다른 해시의 요청은 operations 행을 만들지 않아 진행 중 복구의 최종 결과를 덮어쓰지 않는다. 추가 전용이다
CREATE TABLE admin_operation_claims
(
    admin_id uuid NOT NULL REFERENCES admin_users (admin_id),
    operation_id uuid NOT NULL,
    operation_kind text NOT NULL CHECK (length(operation_kind) BETWEEN 1 AND 100),
    target_key text NOT NULL CHECK (length(target_key) BETWEEN 0 AND 200),
    request_hash text NOT NULL CHECK (request_hash ~ '^[0-9A-F]{64}$'),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (admin_id, operation_id)
);
CREATE TRIGGER admin_operation_claims_append_only BEFORE UPDATE OR DELETE OR TRUNCATE ON admin_operation_claims
    FOR EACH STATEMENT EXECUTE FUNCTION reject_append_only_mutation();

-- 운영자 지급 한 건. Draft -> Executed(대상 우편함에 넣음) -> Cancelled(미수령분 취소), Draft -> Cancelled.
-- rewards는 자산 목록 JSON 배열이다. 작성 작업의 operations 행을 FK로 묶어 지급과 작업 이력을 함께 추적한다.
-- RecoveryCompensation은 복구 대상(세션 ID 등)을 source_ref로 남긴다
CREATE TABLE admin_grants
(
    grant_id uuid PRIMARY KEY,
    created_by uuid NOT NULL REFERENCES admin_users (admin_id),
    created_operation_id uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    reason text NOT NULL CHECK ((length(btrim(reason)) > 0) AND (length(reason) <= 500)),
    rewards jsonb NOT NULL CHECK ((jsonb_typeof(rewards) = 'array') AND (jsonb_array_length(rewards) BETWEEN 1 AND 50)),
    target_account_ids uuid[] NOT NULL CHECK ((cardinality(target_account_ids) BETWEEN 1 AND 1000)
        AND (array_ndims(target_account_ids) = 1) AND (array_position(target_account_ids, NULL) IS NULL)),
    claim_days integer NOT NULL DEFAULT 30 CHECK (claim_days BETWEEN 1 AND 365),
    state text NOT NULL DEFAULT 'Draft' CHECK (state IN ('Draft', 'Executed', 'Cancelled')),
    executed_at timestamptz NULL,
    executed_by uuid NULL REFERENCES admin_users (admin_id),
    cancelled_at timestamptz NULL,
    cancelled_by uuid NULL REFERENCES admin_users (admin_id),
    source_kind text NOT NULL CHECK (source_kind IN ('AdminGrant', 'RecoveryCompensation')),
    source_ref text NULL CHECK ((source_ref IS NULL) OR (length(source_ref) BETWEEN 1 AND 200)),
    FOREIGN KEY (created_by, created_operation_id) REFERENCES operations (actor_id, operation_id),
    CHECK ((executed_at IS NULL) = (executed_by IS NULL)),
    CHECK ((cancelled_at IS NULL) = (cancelled_by IS NULL)),
    CHECK (((state = 'Draft') AND (executed_at IS NULL) AND (cancelled_at IS NULL))
        OR ((state = 'Executed') AND (executed_at IS NOT NULL) AND (cancelled_at IS NULL))
        OR ((state = 'Cancelled') AND (cancelled_at IS NOT NULL))),
    CHECK ((source_kind <> 'RecoveryCompensation') OR (source_ref IS NOT NULL))
);
CREATE INDEX ix_admin_grants_state ON admin_grants (state, created_at, grant_id);
CREATE INDEX ix_admin_grants_source ON admin_grants (source_kind, source_ref, grant_id) WHERE source_ref IS NOT NULL;
-- 같은 복구 대상의 보정 지급은 취소되지 않은 것 하나만 둔다. 운영자 두 명이 동시에 작성해도 DB가 중복 발행을 막는다
CREATE UNIQUE INDEX ux_admin_grants_recovery_ref ON admin_grants (source_ref)
    WHERE source_kind = 'RecoveryCompensation' AND state <> 'Cancelled';

-- 계정 우편함. 지급 실행이 (grant_id, account_id)당 한 행만 넣는다. 수령과 취소는 상호 배타다.
-- 수령은 플레이어 작업(actor = account_id)이므로 그 operations 행과 수령 캐릭터의 소유 계정을 FK로 검사한다
CREATE TABLE reward_inbox
(
    inbox_id uuid PRIMARY KEY,
    account_id uuid NOT NULL REFERENCES accounts (account_id),
    source_kind text NOT NULL CHECK (source_kind IN ('AdminGrant', 'RecoveryCompensation')),
    grant_id uuid NULL REFERENCES admin_grants (grant_id),
    source_ref text NOT NULL CHECK (length(source_ref) BETWEEN 1 AND 200),
    rewards jsonb NOT NULL CHECK ((jsonb_typeof(rewards) = 'array') AND (jsonb_array_length(rewards) BETWEEN 1 AND 50)),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    expires_at timestamptz NOT NULL,
    claimed_at timestamptz NULL,
    claimed_character_id uuid NULL,
    claim_operation_id uuid NULL,
    cancelled_at timestamptz NULL,
    UNIQUE (grant_id, account_id),
    FOREIGN KEY (account_id, claimed_character_id) REFERENCES characters (account_id, character_id),
    FOREIGN KEY (account_id, claim_operation_id) REFERENCES operations (actor_id, operation_id),
    CHECK (expires_at > created_at),
    CHECK ((source_kind NOT IN ('AdminGrant', 'RecoveryCompensation')) OR (grant_id IS NOT NULL)),
    CHECK ((claimed_at IS NULL) OR (cancelled_at IS NULL)),
    CHECK ((claimed_at IS NULL) = (claim_operation_id IS NULL)),
    CHECK ((claimed_character_id IS NULL) OR (claimed_at IS NOT NULL)),
    CHECK ((claimed_at IS NULL) OR (claimed_at >= created_at)),
    CHECK ((cancelled_at IS NULL) OR (cancelled_at >= created_at))
);
CREATE INDEX ix_reward_inbox_account ON reward_inbox (account_id, created_at, inbox_id);
CREATE INDEX ix_reward_inbox_account_open ON reward_inbox (account_id, expires_at, inbox_id)
    WHERE (claimed_at IS NULL) AND (cancelled_at IS NULL);
-- 지급 취소가 미수령분만 고른다
CREATE INDEX ix_reward_inbox_grant_open ON reward_inbox (grant_id, inbox_id)
    WHERE (claimed_at IS NULL) AND (cancelled_at IS NULL);

-- 서비스 상태 단일 행(id = 1). 갱신마다 revision을 올린다. 초기값은 점검 중, 빈 목록이다.
-- API는 DB 연결이 있으면 이 행을, 연결 문자열이 없으면 ServiceState 설정을 읽는다
CREATE TABLE service_state
(
    id integer PRIMARY KEY CHECK (id = 1),
    maintenance boolean NOT NULL,
    disabled_features text[] NOT NULL DEFAULT '{}' CHECK ((cardinality(disabled_features) <= 64)
        AND ((cardinality(disabled_features) = 0) OR (array_ndims(disabled_features) = 1))
        AND (array_position(disabled_features, NULL) IS NULL)),
    allowed_build_ids text[] NOT NULL DEFAULT '{}' CHECK ((cardinality(allowed_build_ids) <= 64)
        AND ((cardinality(allowed_build_ids) = 0) OR (array_ndims(allowed_build_ids) = 1))
        AND (array_position(allowed_build_ids, NULL) IS NULL)),
    revision bigint NOT NULL DEFAULT 0 CHECK (revision >= 0),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_by uuid NULL REFERENCES admin_users (admin_id)
);
INSERT INTO service_state (id, maintenance, disabled_features, allowed_build_ids, revision)
VALUES (1, true, '{}', '{}', 0);

-- 런처와 게임 마을에 보이는 공지. 노출 기간이 끝나기 전에 운영자가 끝내면 ended_at을 남긴다(지우지 않는다).
-- severity: 0 Info, 1 Warning, 2 Critical
CREATE TABLE service_notices
(
    notice_id uuid PRIMARY KEY,
    title text NOT NULL CHECK (length(btrim(title)) BETWEEN 1 AND 100),
    body text NOT NULL CHECK (length(body) <= 4000),
    target text NOT NULL CHECK (target IN ('Launcher', 'Game', 'Both')),
    severity integer NOT NULL CHECK (severity BETWEEN 0 AND 2),
    starts_at timestamptz NOT NULL,
    ends_at timestamptz NOT NULL,
    created_by uuid NOT NULL REFERENCES admin_users (admin_id),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    ended_at timestamptz NULL,
    ended_by uuid NULL REFERENCES admin_users (admin_id),
    CHECK (ends_at > starts_at),
    CHECK ((ended_at IS NULL) = (ended_by IS NULL)),
    CHECK ((ended_at IS NULL) OR (ended_at >= created_at))
);
CREATE INDEX ix_service_notices_active ON service_notices (ends_at, starts_at, notice_id) WHERE ended_at IS NULL;
CREATE INDEX ix_service_notices_created ON service_notices (created_at, notice_id);

-- 어드민 조회가 쓰는 기존 테이블 인덱스. 계정 상세의 출정 이력, 계정 원장의 시간순 페이지, 상태별 세션 목록이 전체를 훑지 않게 한다
CREATE INDEX ix_session_participants_account ON session_participants (account_id, session_id);
CREATE INDEX ix_economy_ledger_account_time ON economy_ledger (account_id, occurred_at, ledger_id);
CREATE INDEX ix_game_sessions_state_created ON game_sessions (state, created_at, session_id);
