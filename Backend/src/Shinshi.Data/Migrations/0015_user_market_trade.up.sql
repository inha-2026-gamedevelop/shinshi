-- 유저 경매장(등록, 거래 기록)과 1:1 거래 실행, 장비 보관(escrow) 표시, 회수함 출처 MarketReturn.
-- 모든 쓰기는 멱등 작업이 등록/실행 행 -> 계정(uuid 오름차순) -> 지갑 -> 캐릭터 -> account_items(정의 ID) -> 장비(item_id) -> 원장
-- 순서로 잠근 뒤 한다. 수량형 보관분은 판매자 account_items에서 빼서 등록 행(remaining_qty)으로 옮기므로 원장 합계는 잔액과 같다

-- 유저 경매장 등록. remaining_qty는 종료 뒤에도 남겨 "판매 수량 = listed_qty - remaining_qty"를 항상 성립시킨다
-- (취소, 만료 때는 remaining_qty가 반환된 수량이다). 활성 등록의 remaining_qty 합이 보관 중인 수량이다
CREATE TABLE market_listings
(
    listing_id uuid PRIMARY KEY,
    seller_account_id uuid NOT NULL REFERENCES accounts (account_id),
    seller_character_id uuid NOT NULL,
    asset_kind text NOT NULL CHECK (asset_kind IN ('AccountItem', 'Equipment')),
    definition_id text NOT NULL CHECK (length(definition_id) BETWEEN 1 AND 64),
    item_id uuid NULL,
    grade smallint NOT NULL CHECK (grade BETWEEN 0 AND 3),
    class_id smallint NULL CHECK (class_id IN (0, 1, 2)),
    category smallint NOT NULL CHECK (category BETWEEN 1 AND 3),
    unit_price bigint NOT NULL CHECK (unit_price BETWEEN 1 AND 100000000),
    listed_qty integer NOT NULL CHECK (listed_qty BETWEEN 1 AND 9999),
    remaining_qty integer NOT NULL CHECK (remaining_qty BETWEEN 0 AND listed_qty),
    state text NOT NULL CHECK (state IN ('Active', 'SoldOut', 'Cancelled', 'Expired')),
    gross_total bigint NOT NULL DEFAULT 0 CHECK (gross_total >= 0),
    fee_total bigint NOT NULL DEFAULT 0 CHECK (fee_total >= 0),
    net_total bigint NOT NULL DEFAULT 0 CHECK (net_total >= 0),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    expires_at timestamptz NOT NULL,
    closed_at timestamptz NULL,
    seen_at timestamptz NULL,
    revision bigint NOT NULL DEFAULT 0 CHECK (revision >= 0),
    FOREIGN KEY (seller_account_id, seller_character_id) REFERENCES characters (account_id, character_id),
    CONSTRAINT ck_market_listings_equipment CHECK ((asset_kind = 'Equipment') = (item_id IS NOT NULL)),
    CONSTRAINT ck_market_listings_equipment_qty CHECK (asset_kind <> 'Equipment' OR listed_qty = 1),
    CONSTRAINT ck_market_listings_expiry CHECK (expires_at > created_at),
    CONSTRAINT ck_market_listings_closed CHECK ((state = 'Active') = (closed_at IS NULL)),
    CONSTRAINT ck_market_listings_active_qty CHECK (state <> 'Active' OR remaining_qty >= 1),
    CONSTRAINT ck_market_listings_soldout_qty CHECK (state <> 'SoldOut' OR remaining_qty = 0),
    CONSTRAINT ck_market_listings_seen CHECK (seen_at IS NULL OR state <> 'Active'),
    CONSTRAINT ck_market_listings_totals CHECK (net_total = gross_total - fee_total
        AND gross_total = unit_price * (listed_qty - remaining_qty))
);
-- 같은 장비는 활성 등록 하나뿐이다. 동시 등록 경합의 마지막 방어선이다
CREATE UNIQUE INDEX ux_market_listings_item_active ON market_listings (item_id)
    WHERE state = 'Active' AND item_id IS NOT NULL;
-- 검색 묶음, 한 정의 ID의 등록 목록(개당 가격, 등록 순)
CREATE INDEX ix_market_listings_search ON market_listings (definition_id, unit_price, created_at, listing_id)
    WHERE state = 'Active';
-- 분류 검색
CREATE INDEX ix_market_listings_category ON market_listings (category, definition_id) WHERE state = 'Active';
-- 내 등록, 계정 동시 등록 수 세기, 미확인 수
CREATE INDEX ix_market_listings_seller ON market_listings (seller_account_id, state, created_at DESC);
-- 만료 유지 작업
CREATE INDEX ix_market_listings_expiry ON market_listings (expires_at, listing_id) WHERE state = 'Active';
-- 활성 장비 등록이 있는 캐릭터의 삭제 거절
CREATE INDEX ix_market_listings_equipment_character ON market_listings (seller_character_id)
    WHERE state = 'Active' AND asset_kind = 'Equipment';

-- 성사 기록. 추가 전용이고 구매 작업(구매자 계정 actor)마다 한 줄이다
CREATE TABLE market_trades
(
    trade_id uuid PRIMARY KEY,
    listing_id uuid NOT NULL REFERENCES market_listings (listing_id),
    buyer_account_id uuid NOT NULL REFERENCES accounts (account_id),
    buyer_character_id uuid NOT NULL,
    seller_account_id uuid NOT NULL REFERENCES accounts (account_id),
    definition_id text NOT NULL CHECK (length(definition_id) BETWEEN 1 AND 64),
    quantity integer NOT NULL CHECK (quantity > 0),
    unit_price bigint NOT NULL CHECK (unit_price > 0),
    gross bigint NOT NULL CHECK (gross > 0),
    fee bigint NOT NULL CHECK (fee >= 0),
    net bigint NOT NULL CHECK (net >= 0),
    actor_id uuid NOT NULL,
    operation_id uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (actor_id, operation_id),
    FOREIGN KEY (actor_id, operation_id) REFERENCES operations (actor_id, operation_id),
    FOREIGN KEY (buyer_account_id, buyer_character_id) REFERENCES characters (account_id, character_id),
    CONSTRAINT ck_market_trades_amounts CHECK (gross = unit_price * quantity AND net = gross - fee),
    CONSTRAINT ck_market_trades_parties CHECK (buyer_account_id <> seller_account_id)
);
CREATE INDEX ix_market_trades_buyer ON market_trades (buyer_account_id, created_at DESC, trade_id DESC);
CREATE INDEX ix_market_trades_seller ON market_trades (seller_account_id, created_at DESC, trade_id DESC);
CREATE INDEX ix_market_trades_definition ON market_trades (definition_id, created_at DESC);
CREATE INDEX ix_market_trades_listing ON market_trades (listing_id);
CREATE TRIGGER market_trades_append_only BEFORE UPDATE OR DELETE OR TRUNCATE ON market_trades
    FOR EACH STATEMENT EXECUTE FUNCTION reject_append_only_mutation();

-- 장비 보관 표시. 보관 중인 장비는 판매자 캐릭터 행에 그대로 두고(보관 칸을 계속 차지) 성장 API가 이 표시로 거절한다.
-- 새 CHECK는 NOT VALID로 건 뒤 VALIDATE로 기존 행을 검사한다(0012 주석 참고)
ALTER TABLE equipment_instances
    ADD COLUMN escrow_kind text NULL,
    ADD COLUMN escrow_ref uuid NULL REFERENCES market_listings (listing_id),
    ADD CONSTRAINT ck_equipment_escrow_kind CHECK (escrow_kind IS NULL OR escrow_kind IN ('Market')) NOT VALID,
    ADD CONSTRAINT ck_equipment_escrow_pair CHECK ((escrow_kind IS NULL) = (escrow_ref IS NULL)) NOT VALID,
    ADD CONSTRAINT ck_equipment_escrow_unequipped CHECK (escrow_kind IS NULL OR NOT equipped) NOT VALID;
ALTER TABLE equipment_instances VALIDATE CONSTRAINT ck_equipment_escrow_kind;
ALTER TABLE equipment_instances VALIDATE CONSTRAINT ck_equipment_escrow_pair;
ALTER TABLE equipment_instances VALIDATE CONSTRAINT ck_equipment_escrow_unequipped;

-- 1:1 거래 실행. prepare가 동결한 제안을 남기고 commit이 Committed로, 방치 정리와 거절 종결이 Rejected, Abandoned로 끝낸다.
-- 한 계정은 a, b 어느 쪽이든 Prepared 하나만 갖는다(prepare가 두 계정을 잠근 뒤 두 열을 모두 검사하고, 아래 부분 유일 인덱스는 보조다).
-- commit의 멱등 작업 키는 (town_instance_id, commit_operation_id)이고 resolve가 그 operations 결과로 지연 보정한다
CREATE TABLE player_trade_executions
(
    execution_id uuid PRIMARY KEY,
    trade_id uuid NOT NULL,
    commit_operation_id uuid NOT NULL UNIQUE,
    town_instance_id uuid NOT NULL REFERENCES server_instances (instance_id),
    account_a uuid NOT NULL REFERENCES accounts (account_id),
    character_a uuid NOT NULL,
    account_b uuid NOT NULL REFERENCES accounts (account_id),
    character_b uuid NOT NULL,
    offer_a jsonb NOT NULL CHECK (jsonb_typeof(offer_a) = 'object'),
    offer_b jsonb NOT NULL CHECK (jsonb_typeof(offer_b) = 'object'),
    state text NOT NULL CHECK (state IN ('Prepared', 'Committed', 'Rejected', 'Abandoned')),
    reason_code integer NOT NULL DEFAULT 0 CHECK (reason_code >= 0),
    prepared_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    finished_at timestamptz NULL,
    revision bigint NOT NULL DEFAULT 0 CHECK (revision >= 0),
    FOREIGN KEY (account_a, character_a) REFERENCES characters (account_id, character_id),
    FOREIGN KEY (account_b, character_b) REFERENCES characters (account_id, character_id),
    CONSTRAINT ck_trade_executions_accounts CHECK (account_a <> account_b),
    CONSTRAINT ck_trade_executions_finished CHECK ((state = 'Prepared') = (finished_at IS NULL)),
    CONSTRAINT ck_trade_executions_reason CHECK ((state IN ('Rejected', 'Abandoned')) = (reason_code > 0)),
    CONSTRAINT ck_trade_executions_operation CHECK (commit_operation_id <> execution_id)
);
CREATE UNIQUE INDEX ux_trade_executions_prepared_a ON player_trade_executions (account_a) WHERE state = 'Prepared';
CREATE UNIQUE INDEX ux_trade_executions_prepared_b ON player_trade_executions (account_b) WHERE state = 'Prepared';
-- 방치 정리
CREATE INDEX ix_trade_executions_prepared_expiry ON player_trade_executions (prepared_at, execution_id)
    WHERE state = 'Prepared';

-- 회수함 출처 MarketReturn(경매장 반환이 보관 상한을 넘은 몫, source_ref = 등록 ID). 0012와 같이 NOT VALID 뒤 VALIDATE로 나눈다
-- (한 트랜잭션 적용이라 잠금은 커밋까지 남는다. 운영 DB 규모에서는 VALIDATE를 별도 마이그레이션으로 나눈다)
ALTER TABLE reward_inbox
    DROP CONSTRAINT ck_reward_inbox_source_kind,
    DROP CONSTRAINT ck_reward_inbox_rewards,
    DROP CONSTRAINT ck_reward_inbox_recovery,
    DROP CONSTRAINT ck_reward_inbox_recovery_grant,
    ADD CONSTRAINT ck_reward_inbox_source_kind CHECK (source_kind IN ('AdminGrant', 'RecoveryCompensation',
        'SessionReward', 'AuctionReward', 'MarketReturn')) NOT VALID,
    ADD CONSTRAINT ck_reward_inbox_rewards CHECK ((jsonb_typeof(rewards) = 'array') AND (jsonb_array_length(rewards) >= 1)
        AND (jsonb_array_length(rewards) <= CASE WHEN source_kind IN ('SessionReward', 'AuctionReward', 'MarketReturn')
            THEN 200 ELSE 50 END)) NOT VALID,
    ADD CONSTRAINT ck_reward_inbox_recovery CHECK ((source_kind IN ('SessionReward', 'AuctionReward', 'MarketReturn'))
        = (acquired_at IS NOT NULL)) NOT VALID,
    ADD CONSTRAINT ck_reward_inbox_recovery_grant CHECK ((source_kind NOT IN ('SessionReward', 'AuctionReward',
        'MarketReturn')) OR (grant_id IS NULL)) NOT VALID;
ALTER TABLE reward_inbox VALIDATE CONSTRAINT ck_reward_inbox_source_kind;
ALTER TABLE reward_inbox VALIDATE CONSTRAINT ck_reward_inbox_rewards;
ALTER TABLE reward_inbox VALIDATE CONSTRAINT ck_reward_inbox_recovery;
ALTER TABLE reward_inbox VALIDATE CONSTRAINT ck_reward_inbox_recovery_grant;

-- 같은 등록(source_ref = 등록 ID)의 반환 회수함은 계정마다 한 행이다. 취소와 만료가 겹쳐도 DB가 두 번째 행을 막는다
DROP INDEX ux_reward_inbox_recovery_source;
CREATE UNIQUE INDEX ux_reward_inbox_recovery_source ON reward_inbox (source_kind, source_ref, account_id)
    WHERE source_kind IN ('SessionReward', 'AuctionReward', 'MarketReturn');

-- 배포 권한(README의 런타임 역할 예시에 추가): market_listings, player_trade_executions는 SELECT, INSERT, UPDATE,
-- market_trades는 SELECT, INSERT만 준다
