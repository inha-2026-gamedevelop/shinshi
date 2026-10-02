-- 보스 처치 성공 뒤 파티 경매. 보스 성공 정산은 세션을 바로 닫지 않고 'Auction'(0001 CHECK에 있는 값)으로 두며,
-- 던전이 경매를 마친 뒤 finish가 Closed/Settled로 닫는다. 그 사이 서버는 Active, 참가자 접속 임대는 유지된다

-- 최고 입찰금 예약. 예약은 잔액을 바꾸지 않으므로 원장 줄이 없고, 가용 엽전은 currency - currency_reserved다
ALTER TABLE account_wallets
    ADD COLUMN currency_reserved bigint NOT NULL DEFAULT 0,
    ADD CONSTRAINT ck_account_wallets_currency_reserved CHECK (currency_reserved >= 0 AND currency_reserved <= currency);

-- 경매 단계 기한. 지나면 유지 작업이 열린 품목을 취소하고 출정을 닫는다
ALTER TABLE game_sessions
    ADD COLUMN auction_ends_at timestamptz NULL,
    ADD CONSTRAINT ck_game_sessions_auction_ends_at CHECK (state <> 'Auction' OR auction_ends_at IS NOT NULL);
CREATE INDEX ix_game_sessions_auction ON game_sessions (auction_ends_at, session_id) WHERE state = 'Auction';

-- 경매 품목. 개시 때 자격 집합을 동결하고, 마감 때 던전이 확정한 종료 시각과 접속 집합을 먼저 저장(Closing)한 뒤
-- 정산이 그 값만 써서 Sold/Unsold로 끝낸다. 종료 기록 없이 버려진 Open 품목은 복구가 Cancelled로 끝낸다
CREATE TABLE auctions
(
    auction_id uuid PRIMARY KEY,
    session_id uuid NOT NULL REFERENCES game_sessions (session_id),
    item_seq integer NOT NULL CHECK (item_seq >= 1),
    definition_id text NOT NULL CHECK (definition_id ~ '^item_[a-z0-9_]{1,59}$'),
    binding_kind text NOT NULL CHECK (binding_kind = 'Account'),
    mode text NOT NULL CHECK (mode IN ('Bidding', 'Solo')),
    state text NOT NULL CHECK (state IN ('Open', 'Closing', 'Sold', 'Unsold', 'Cancelled')),
    eligible_participant_ids uuid[] NOT NULL CHECK (cardinality(eligible_participant_ids) BETWEEN 1 AND 4),
    open_operation_id uuid NOT NULL,
    opened_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    hard_ends_at timestamptz NOT NULL,
    bid_seq bigint NOT NULL DEFAULT 0 CHECK (bid_seq >= 0),
    high_bid bigint NULL CHECK (high_bid > 0),
    high_participant_id uuid NULL,
    high_account_id uuid NULL REFERENCES accounts (account_id),
    connected_participant_ids uuid[] NULL,
    ended_at timestamptz NULL,
    close_operation_id uuid NULL,
    closed_at timestamptz NULL,
    payout_each bigint NULL CHECK (payout_each >= 0),
    payout_count integer NULL CHECK (payout_count BETWEEN 0 AND 3),
    burned bigint NULL CHECK (burned >= 0),
    revision bigint NOT NULL DEFAULT 0 CHECK (revision >= 0),
    UNIQUE (session_id, item_seq),
    CHECK (hard_ends_at > opened_at),
    CHECK ((mode = 'Solo') = (cardinality(eligible_participant_ids) = 1)),
    CHECK ((bid_seq = 0) = (high_bid IS NULL)),
    CHECK ((high_bid IS NULL) = (high_participant_id IS NULL) AND (high_bid IS NULL) = (high_account_id IS NULL)),
    CHECK ((state = 'Open') = (close_operation_id IS NULL)),
    CHECK ((state = 'Open') = (connected_participant_ids IS NULL AND ended_at IS NULL)),
    CHECK ((state IN ('Sold', 'Unsold', 'Cancelled')) = (closed_at IS NOT NULL)),
    CHECK ((state = 'Sold') = (payout_each IS NOT NULL AND payout_count IS NOT NULL AND burned IS NOT NULL)),
    CHECK (state <> 'Sold' OR high_bid IS NOT NULL),
    CHECK (state <> 'Unsold' OR high_bid IS NULL)
);
-- 한 출정은 품목을 하나씩 진행한다
CREATE UNIQUE INDEX ux_auctions_session_active ON auctions (session_id) WHERE state IN ('Open', 'Closing');

-- 승인된 입찰의 추가 전용 기록. 예약 이동의 감사 근거이며 accepted_at은 던전이 접수한 시각이다
CREATE TABLE auction_bids
(
    auction_id uuid NOT NULL REFERENCES auctions (auction_id),
    bid_seq bigint NOT NULL CHECK (bid_seq >= 1),
    participant_id uuid NOT NULL,
    account_id uuid NOT NULL REFERENCES accounts (account_id),
    amount bigint NOT NULL CHECK (amount > 0),
    operation_id uuid NOT NULL,
    accepted_at timestamptz NOT NULL,
    placed_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (auction_id, bid_seq),
    FOREIGN KEY (account_id, operation_id) REFERENCES operations (actor_id, operation_id)
);
CREATE TRIGGER auction_bids_append_only BEFORE UPDATE OR DELETE OR TRUNCATE ON auction_bids
    FOR EACH STATEMENT EXECUTE FUNCTION reject_append_only_mutation();

-- 배포 권한(README의 런타임 역할 예시에 추가): auctions는 SELECT, INSERT, UPDATE, auction_bids는 SELECT, INSERT만 준다
