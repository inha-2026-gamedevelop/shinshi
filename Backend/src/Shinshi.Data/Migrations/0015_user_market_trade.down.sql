-- 되돌리면 경매장 등록과 성사 기록, 1:1 거래 실행 행, 장비 보관 표시, 회수함의 MarketReturn 행(미수령 포함)을 잃는다.
-- 보관 중이던 수량형 자산은 등록 행에만 있으므로 되돌리기 전에 모든 활성 등록을 취소해 반환해야 한다. 이미 쓴 원장과 장비 이력은 남는다.
-- 운영 DB에서 실행하지 않는다
DELETE FROM reward_inbox_lines
    WHERE inbox_id IN (SELECT inbox_id FROM reward_inbox WHERE source_kind = 'MarketReturn');
DELETE FROM reward_inbox WHERE source_kind = 'MarketReturn';

DROP INDEX ux_reward_inbox_recovery_source;
CREATE UNIQUE INDEX ux_reward_inbox_recovery_source ON reward_inbox (source_kind, source_ref, account_id)
    WHERE source_kind IN ('SessionReward', 'AuctionReward');

ALTER TABLE reward_inbox
    DROP CONSTRAINT ck_reward_inbox_recovery_grant,
    DROP CONSTRAINT ck_reward_inbox_recovery,
    DROP CONSTRAINT ck_reward_inbox_rewards,
    DROP CONSTRAINT ck_reward_inbox_source_kind,
    ADD CONSTRAINT ck_reward_inbox_source_kind CHECK (source_kind IN ('AdminGrant', 'RecoveryCompensation',
        'SessionReward', 'AuctionReward')) NOT VALID,
    ADD CONSTRAINT ck_reward_inbox_rewards CHECK ((jsonb_typeof(rewards) = 'array') AND (jsonb_array_length(rewards) >= 1)
        AND (jsonb_array_length(rewards) <= CASE WHEN source_kind IN ('SessionReward', 'AuctionReward')
            THEN 200 ELSE 50 END)) NOT VALID,
    ADD CONSTRAINT ck_reward_inbox_recovery CHECK ((source_kind IN ('SessionReward', 'AuctionReward'))
        = (acquired_at IS NOT NULL)) NOT VALID,
    ADD CONSTRAINT ck_reward_inbox_recovery_grant CHECK ((source_kind NOT IN ('SessionReward', 'AuctionReward'))
        OR (grant_id IS NULL)) NOT VALID;
ALTER TABLE reward_inbox VALIDATE CONSTRAINT ck_reward_inbox_source_kind;
ALTER TABLE reward_inbox VALIDATE CONSTRAINT ck_reward_inbox_rewards;
ALTER TABLE reward_inbox VALIDATE CONSTRAINT ck_reward_inbox_recovery;
ALTER TABLE reward_inbox VALIDATE CONSTRAINT ck_reward_inbox_recovery_grant;

DROP TABLE player_trade_executions;

ALTER TABLE equipment_instances
    DROP CONSTRAINT ck_equipment_escrow_unequipped,
    DROP CONSTRAINT ck_equipment_escrow_pair,
    DROP CONSTRAINT ck_equipment_escrow_kind,
    DROP COLUMN escrow_ref,
    DROP COLUMN escrow_kind;

DROP TABLE market_trades;
DROP TABLE market_listings;
