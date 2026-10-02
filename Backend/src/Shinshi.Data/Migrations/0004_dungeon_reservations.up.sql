-- 서버 풀: 프로세스 재시작을 boot_id로 구분한다. 같은 instance_id라도 boot_id가 바뀌면 이전 프로세스의 세션은 복구 대상이다
ALTER TABLE server_instances
    ADD COLUMN boot_id uuid NULL,
    ADD COLUMN registered_at timestamptz NOT NULL DEFAULT clock_timestamp();

-- 사용 가능 = 총량 - 예약량. 예약은 총량을 바꾸지 않으므로 원장 대상이 아니다
ALTER TABLE account_wallets
    ADD COLUMN tickets_reserved bigint NOT NULL DEFAULT 0,
    ADD CONSTRAINT ck_account_wallets_tickets_reserved CHECK (tickets_reserved >= 0 AND tickets_reserved <= tickets);
ALTER TABLE account_items
    ADD COLUMN reserved bigint NOT NULL DEFAULT 0,
    ADD CONSTRAINT ck_account_items_reserved CHECK (reserved >= 0 AND reserved <= quantity);

-- 출정 상태: Reserved -> Running(/start) -> Settling(SessionEnded 저장) -> Closed(정산)
-- Reserved -> Closed(취소, 만료), Running -> Closed(서비스 장애 취소)
ALTER TABLE game_sessions
    ADD COLUMN reserved_by_instance_id uuid NULL REFERENCES server_instances (instance_id),
    ADD COLUMN instance_boot_id uuid NULL,
    ADD COLUMN expires_at timestamptz NULL,
    ADD COLUMN created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    ADD COLUMN last_event_seq bigint NOT NULL DEFAULT 0 CHECK (last_event_seq >= 0),
    ADD COLUMN ended_event_seq bigint NULL CHECK (ended_event_seq > 0),
    ADD COLUMN end_reason integer NULL CHECK (end_reason BETWEEN 0 AND 5),
    ADD COLUMN settlement_operation_id uuid NULL,
    ADD COLUMN closed_at timestamptz NULL,
    ADD CONSTRAINT ck_game_sessions_reserved_expiry CHECK (state <> 'Reserved' OR expires_at IS NOT NULL),
    ADD CONSTRAINT ck_game_sessions_ended_event CHECK ((ended_event_seq IS NULL) = (settlement_operation_id IS NULL)
        AND (ended_event_seq IS NULL OR ended_event_seq <= last_event_seq)),
    ADD CONSTRAINT ck_game_sessions_closed CHECK ((state = 'Closed') = (closed_at IS NOT NULL)),
    ADD CONSTRAINT ck_game_sessions_result CHECK (result IS NULL
        OR result IN ('Released', 'Expired', 'Settled', 'ServiceCancelled'));
CREATE INDEX ix_game_sessions_reserved_expiry ON game_sessions (expires_at, session_id) WHERE state = 'Reserved';
CREATE INDEX ix_game_sessions_open ON game_sessions (state, session_id) WHERE state IN ('Reserved', 'Running', 'Settling');

CREATE TABLE session_participants
(
    session_id uuid NOT NULL REFERENCES game_sessions (session_id),
    participant_id uuid NOT NULL UNIQUE,
    account_id uuid NOT NULL,
    character_id uuid NOT NULL,
    state text NOT NULL CHECK (state IN ('Reserved', 'Active', 'Settled', 'Released')),
    essence_accrued bigint NOT NULL DEFAULT 0 CHECK (essence_accrued >= 0),
    settled_at timestamptz NULL,
    PRIMARY KEY (session_id, participant_id),
    UNIQUE (session_id, account_id),
    FOREIGN KEY (account_id, character_id) REFERENCES characters (account_id, character_id),
    CHECK ((state IN ('Settled', 'Released')) = (settled_at IS NOT NULL))
);
-- 한 계정은 열린 출정 하나만 가진다. 예약 경로의 사전 검사가 놓친 경합의 마지막 방어선이다
CREATE UNIQUE INDEX ux_session_participants_open_account ON session_participants (account_id)
    WHERE state IN ('Reserved', 'Active');
CREATE INDEX ix_session_participants_character ON session_participants (character_id, session_id);

CREATE TABLE reservations
(
    reservation_id uuid PRIMARY KEY,
    session_id uuid NOT NULL,
    participant_id uuid NOT NULL,
    account_id uuid NOT NULL REFERENCES accounts (account_id),
    asset_kind text NOT NULL CHECK (asset_kind IN ('Ticket', 'Item')),
    definition_id text NOT NULL,
    binding_kind text NOT NULL,
    quantity bigint NOT NULL CHECK (quantity > 0),
    consumed bigint NOT NULL DEFAULT 0 CHECK (consumed >= 0 AND consumed <= quantity),
    state text NOT NULL CHECK (state IN ('Reserved', 'Consumed', 'Released', 'Settled', 'Refunded')),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    closed_at timestamptz NULL,
    FOREIGN KEY (session_id, participant_id) REFERENCES session_participants (session_id, participant_id),
    UNIQUE (session_id, account_id, asset_kind, definition_id, binding_kind),
    CHECK ((asset_kind = 'Ticket') = (definition_id = '' AND binding_kind = '')),
    CHECK (asset_kind <> 'Ticket' OR quantity = 1),
    CHECK (asset_kind = 'Item' OR consumed = 0)
);
CREATE INDEX ix_reservations_account_open ON reservations (account_id, reservation_id) WHERE state = 'Reserved';

-- 추가 전용. 런타임 역할에는 SELECT, INSERT만 부여하고 트리거로도 막는다
CREATE TABLE session_events
(
    session_id uuid NOT NULL REFERENCES game_sessions (session_id),
    event_seq bigint NOT NULL CHECK (event_seq > 0),
    event_id uuid NOT NULL UNIQUE,
    event_kind integer NOT NULL CHECK (event_kind BETWEEN 1 AND 4),
    participant_id uuid NULL,
    payload jsonb NOT NULL CHECK (jsonb_typeof(payload) = 'object'),
    operation_id uuid NOT NULL,
    committed_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (session_id, event_seq)
);

CREATE FUNCTION session_events_append_only() RETURNS trigger
    LANGUAGE plpgsql AS
$$
BEGIN
    RAISE EXCEPTION 'session_events is append-only' USING ERRCODE = 'restrict_violation';
END;
$$;
CREATE TRIGGER tr_session_events_append_only BEFORE UPDATE OR DELETE ON session_events
    FOR EACH ROW EXECUTE FUNCTION session_events_append_only();

-- 진단용 30초 체크포인트. 경제 복구에 쓰지 않는다
CREATE TABLE session_checkpoints
(
    session_id uuid NOT NULL REFERENCES game_sessions (session_id),
    checkpoint_seq bigint NOT NULL CHECK (checkpoint_seq > 0),
    last_event_seq bigint NOT NULL CHECK (last_event_seq >= 0),
    schema_version integer NOT NULL CHECK (schema_version > 0),
    state_blob bytea NOT NULL CHECK (octet_length(state_blob) <= 262144),
    saved_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (session_id, checkpoint_seq)
);
