-- 캐릭터 소프트 삭제(잠정 결정 9). 행을 지우지 않아 원장, 아이템 이력, 출정 기록의 FK가 그대로 남는다.
-- 이름(name_key 유일 인덱스)은 반환하지 않고 슬롯 수와 목록은 살아 있는 캐릭터만 센다. 복구 경로는 없다
ALTER TABLE characters
    ADD COLUMN deleted_at timestamptz NULL,
    ADD COLUMN delete_operation_id uuid NULL,
    ADD CONSTRAINT ck_characters_deleted_pair CHECK ((deleted_at IS NULL) = (delete_operation_id IS NULL));
-- 캐릭터 목록과 생성 슬롯 수가 살아 있는 캐릭터만 읽는다
CREATE INDEX ix_characters_account_alive ON characters (account_id, character_id) WHERE deleted_at IS NULL;
-- 계정별 최근 24시간 삭제 수 상한(PostgresCharacterRepository.MAX_DELETES_PER_WINDOW)이 읽는다
CREATE INDEX ix_characters_account_deleted ON characters (account_id, deleted_at) WHERE deleted_at IS NOT NULL;

-- 출정 계약. battle_id는 일반전 전투 정의, boss_id와 difficulty(EBossDifficulty 0 쉬움, 1 어려움)는 보스전만 쓴다.
-- 0012 전 출정은 셋 다 NULL이다
ALTER TABLE game_sessions
    ADD COLUMN battle_id text NULL,
    ADD COLUMN boss_id text NULL,
    ADD COLUMN difficulty smallint NULL,
    ADD CONSTRAINT ck_game_sessions_battle_id CHECK (battle_id IS NULL OR length(battle_id) BETWEEN 1 AND 64),
    ADD CONSTRAINT ck_game_sessions_boss_id CHECK (boss_id IS NULL OR (length(boss_id) BETWEEN 1 AND 64)),
    ADD CONSTRAINT ck_game_sessions_boss_mode CHECK (boss_id IS NULL OR mode = 'Boss'),
    ADD CONSTRAINT ck_game_sessions_difficulty CHECK (difficulty IS NULL OR difficulty IN (0, 1));

-- 포기, 접속 만료로 출정을 떠난 참가자. 참가자 상태는 Active로 남아 경제 정산은 SessionEnded 정산안에서 한 번만 한다.
-- depart_reason은 GameSessionCodes.EndReason의 ABANDON(3), DISCONNECT_EXPIRED(4)다
ALTER TABLE session_participants
    ADD COLUMN departed_at timestamptz NULL,
    ADD COLUMN depart_reason smallint NULL,
    ADD CONSTRAINT ck_session_participants_departed CHECK ((departed_at IS NULL) = (depart_reason IS NULL)),
    ADD CONSTRAINT ck_session_participants_depart_reason CHECK (depart_reason IS NULL OR depart_reason IN (3, 4)),
    ADD CONSTRAINT ck_session_participants_departed_active CHECK (departed_at IS NULL OR state IN ('Active', 'Settled'));

-- PARTICIPANT_DEPARTED(6)를 받는다. 새 CHECK는 NOT VALID로 건 뒤 VALIDATE CONSTRAINT로 기존 행을 검사한다.
-- 검사는 쓰기를 막지 않는 SHARE UPDATE EXCLUSIVE 잠금으로 도는 형태지만, 마이그레이션 실행기가 파일 하나를 한 트랜잭션으로
-- 적용하므로 DROP이 잡은 ACCESS EXCLUSIVE 잠금이 커밋까지 남는다. 배포 전 소규모 DB를 전제로 한 파일에 두고,
-- 운영 DB 규모에서 바꿀 때는 VALIDATE를 별도 마이그레이션으로 나눈다
ALTER TABLE session_events
    DROP CONSTRAINT ck_session_events_event_kind,
    ADD CONSTRAINT ck_session_events_event_kind CHECK (event_kind BETWEEN 1 AND 6) NOT VALID;
ALTER TABLE session_events VALIDATE CONSTRAINT ck_session_events_event_kind;

-- 30일 보상 회수함(docs/design/growth-economy.md 9장). 정산과 경매 낙찰 지급이 보관 상한을 넘긴 몫만 넣는다.
-- acquired_at은 원래 획득 시각이고 expires_at은 그 DB 시각 + 30일이다. 재접속으로 기간이 다시 시작하지 않는다.
-- 0008의 열 CHECK는 이름 없이 만들어 PostgreSQL 기본 이름을 쓴다
ALTER TABLE reward_inbox
    DROP CONSTRAINT reward_inbox_source_kind_check,
    DROP CONSTRAINT reward_inbox_rewards_check,
    ADD COLUMN acquired_at timestamptz NULL,
    ADD CONSTRAINT ck_reward_inbox_source_kind CHECK (source_kind IN ('AdminGrant', 'RecoveryCompensation',
        'SessionReward', 'AuctionReward')),
    -- 회수함 행의 rewards는 원래 수량 요약이다. 참가자 한 명의 정산안이 아이템 64줄, 부적 64줄, 지갑 칸을 넘칠 수 있어 200줄까지 둔다
    ADD CONSTRAINT ck_reward_inbox_rewards CHECK ((jsonb_typeof(rewards) = 'array') AND (jsonb_array_length(rewards) >= 1)
        AND (jsonb_array_length(rewards) <= CASE WHEN source_kind IN ('SessionReward', 'AuctionReward') THEN 200 ELSE 50 END)),
    ADD CONSTRAINT ck_reward_inbox_recovery CHECK ((source_kind IN ('SessionReward', 'AuctionReward')) = (acquired_at IS NOT NULL)),
    ADD CONSTRAINT ck_reward_inbox_recovery_grant CHECK ((source_kind NOT IN ('SessionReward', 'AuctionReward'))
        OR (grant_id IS NULL)),
    ADD CONSTRAINT ck_reward_inbox_acquired CHECK ((acquired_at IS NULL) OR (expires_at > acquired_at)),
    ADD CONSTRAINT uq_reward_inbox_account UNIQUE (inbox_id, account_id);
-- 같은 출정 정산(source_ref = 세션 ID)과 같은 경매 품목(source_ref = 경매 ID)은 계정마다 회수함 한 행이다.
-- 정산 재시도가 멱등 키를 빠져나가도 DB가 두 번째 행을 막는다. 실행기가 파일마다 트랜잭션을 열어 CONCURRENTLY를 쓸 수 없으므로
-- 배포 전 소규모 DB를 전제로 일반 생성을 쓴다(위 characters 부분 인덱스도 같다)
CREATE UNIQUE INDEX ux_reward_inbox_recovery_source ON reward_inbox (source_kind, source_ref, account_id)
    WHERE source_kind IN ('SessionReward', 'AuctionReward');

-- 회수함 줄. 수령은 보관 상한까지 남은 수량만 옮기고 remaining_qty를 줄인다(부분 수령). 모든 줄이 0이 되면 우편 행이 수령 완료다.
-- 캐릭터 자산(정련편, 정기)은 획득한 캐릭터에 묶인다
CREATE TABLE reward_inbox_lines
(
    inbox_id uuid NOT NULL,
    line_no integer NOT NULL CHECK (line_no >= 1),
    account_id uuid NOT NULL,
    asset_kind text NOT NULL CHECK (asset_kind IN ('Currency', 'Tickets', 'Essence', 'Shards', 'AccountItem')),
    definition_id text NOT NULL DEFAULT '',
    binding_kind text NOT NULL DEFAULT '',
    character_id uuid NULL,
    original_qty bigint NOT NULL CHECK (original_qty > 0),
    remaining_qty bigint NOT NULL CHECK ((remaining_qty >= 0) AND (remaining_qty <= original_qty)),
    PRIMARY KEY (inbox_id, line_no),
    UNIQUE NULLS NOT DISTINCT (inbox_id, asset_kind, definition_id, binding_kind, character_id),
    FOREIGN KEY (inbox_id, account_id) REFERENCES reward_inbox (inbox_id, account_id),
    FOREIGN KEY (account_id, character_id) REFERENCES characters (account_id, character_id),
    CHECK ((asset_kind IN ('Currency', 'Tickets') AND character_id IS NULL AND definition_id = '' AND binding_kind = '')
        OR (asset_kind IN ('Essence', 'Shards') AND character_id IS NOT NULL AND definition_id = '' AND binding_kind = '')
        OR (asset_kind = 'AccountItem' AND character_id IS NULL AND length(definition_id) > 0 AND length(binding_kind) > 0))
);

-- 배포 권한(README의 런타임 역할 예시에 추가): reward_inbox_lines는 SELECT, INSERT, UPDATE만 준다
