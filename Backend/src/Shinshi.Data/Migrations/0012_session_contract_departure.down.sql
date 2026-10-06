-- 되돌리면 회수함의 미수령분(줄과 회수함 우편 행), 출정 계약 필드, 이탈 기록, 캐릭터 삭제 표시를 잃는다.
-- 삭제한 캐릭터가 다시 목록에 나타나므로 운영 DB에서 실행하지 않는다. 회수함 수령으로 이미 지급한 잔액과 원장은 남는다.
-- session_events는 추가 전용이라 이미 저장된 PARTICIPANT_DEPARTED(6) 행을 지울 수 없다. 이전 범위 CHECK는 NOT VALID로
-- 다시 걸어 기존 행은 남기고 새 행만 막는다
DROP TABLE reward_inbox_lines;

DROP INDEX ux_reward_inbox_recovery_source;
DELETE FROM reward_inbox WHERE source_kind IN ('SessionReward', 'AuctionReward');
ALTER TABLE reward_inbox
    DROP CONSTRAINT uq_reward_inbox_account,
    DROP CONSTRAINT ck_reward_inbox_acquired,
    DROP CONSTRAINT ck_reward_inbox_recovery_grant,
    DROP CONSTRAINT ck_reward_inbox_recovery,
    DROP CONSTRAINT ck_reward_inbox_rewards,
    DROP CONSTRAINT ck_reward_inbox_source_kind,
    DROP COLUMN acquired_at,
    ADD CONSTRAINT reward_inbox_source_kind_check CHECK (source_kind IN ('AdminGrant', 'RecoveryCompensation')),
    ADD CONSTRAINT reward_inbox_rewards_check CHECK ((jsonb_typeof(rewards) = 'array')
        AND (jsonb_array_length(rewards) BETWEEN 1 AND 50));

ALTER TABLE session_events
    DROP CONSTRAINT ck_session_events_event_kind,
    ADD CONSTRAINT ck_session_events_event_kind CHECK (event_kind BETWEEN 1 AND 5) NOT VALID;

ALTER TABLE session_participants
    DROP CONSTRAINT ck_session_participants_departed_active,
    DROP CONSTRAINT ck_session_participants_depart_reason,
    DROP CONSTRAINT ck_session_participants_departed,
    DROP COLUMN depart_reason,
    DROP COLUMN departed_at;

ALTER TABLE game_sessions
    DROP CONSTRAINT ck_game_sessions_difficulty,
    DROP CONSTRAINT ck_game_sessions_boss_mode,
    DROP CONSTRAINT ck_game_sessions_boss_id,
    DROP CONSTRAINT ck_game_sessions_battle_id,
    DROP COLUMN difficulty,
    DROP COLUMN boss_id,
    DROP COLUMN battle_id;

DROP INDEX ix_characters_account_deleted;
DROP INDEX ix_characters_account_alive;
ALTER TABLE characters
    DROP CONSTRAINT ck_characters_deleted_pair,
    DROP COLUMN delete_operation_id,
    DROP COLUMN deleted_at;
