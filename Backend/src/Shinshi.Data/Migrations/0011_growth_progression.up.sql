-- 성장, 장비, 부적, 초기 지급, 계정 진행과 일일 대사 선점.
-- 모든 쓰기는 마을 서버 임대로 들어온 멱등 작업이 계정 -> 캐릭터 -> 장비(item_id) -> account_items(정의 ID) 순서로 잠근 뒤 한다

-- 전투 준비에 영향을 주는 성장 변경만 올린다. 정산의 정기, 정련편 지급은 이 값을 바꾸지 않으므로
-- 마을이 확인한 준비가 보상 지급만으로 깨지지 않는다
ALTER TABLE characters
    ADD COLUMN loadout_revision bigint NOT NULL DEFAULT 0,
    ADD CONSTRAINT ck_characters_loadout_revision CHECK (loadout_revision >= 0);

-- 예약 시점에 고정한 전투 장비, 수련, 부적. 던전 입장, 재입장 교환이 이 값을 돌려준다. 0011 전 참가자는 NULL이다
ALTER TABLE session_participants
    ADD COLUMN loadout_snapshot jsonb NULL,
    ADD CONSTRAINT ck_session_participants_loadout_snapshot
        CHECK ((loadout_snapshot IS NULL) OR (jsonb_typeof(loadout_snapshot) = 'object'));

-- 효과별 등록 진도. level 0은 등록 실패 보정만 쌓인 상태이고 성공하면 보정을 0으로 끝낸다
CREATE TABLE talisman_progress
(
    character_id uuid NOT NULL REFERENCES characters (character_id),
    effect_id text NOT NULL CHECK (effect_id ~ '^T[0-9]{2}-[1-3][A-Z]$'),
    level integer NOT NULL DEFAULT 0 CHECK (level BETWEEN 0 AND 5),
    register_failures integer NOT NULL DEFAULT 0 CHECK (register_failures >= 0),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (character_id, effect_id),
    CHECK ((level = 0) OR (register_failures = 0))
);

-- 기술마다 단계(0 보조1, 1 보조2, 2 핵심)당 효과 하나. 배운 효과만 끼우도록 진도 행을 FK로 묶는다
CREATE TABLE talisman_loadout
(
    character_id uuid NOT NULL,
    skill_id text NOT NULL CHECK (length(skill_id) BETWEEN 1 AND 64),
    tier integer NOT NULL CHECK (tier BETWEEN 0 AND 2),
    effect_id text NOT NULL,
    PRIMARY KEY (character_id, skill_id, tier),
    UNIQUE (character_id, effect_id),
    FOREIGN KEY (character_id, effect_id) REFERENCES talisman_progress (character_id, effect_id)
);

-- 장비 개체. 소멸(분해, 계승 원본)은 행을 지우고 item_history에 마지막 상태를 남긴다
CREATE TABLE equipment_instances
(
    item_id uuid PRIMARY KEY,
    account_id uuid NOT NULL,
    character_id uuid NOT NULL,
    definition_id text NOT NULL CHECK (definition_id ~ '^equip_[a-z0-9_]{1,58}$'),
    slot integer NOT NULL CHECK (slot BETWEEN 0 AND 5),
    grade integer NOT NULL CHECK (grade BETWEEN 0 AND 3),
    is_training boolean NOT NULL,
    origin text NOT NULL CHECK (origin IN ('Craft', 'InitialGrant', 'TrialGrant')),
    enhancement integer NOT NULL DEFAULT 0 CHECK (enhancement BETWEEN 0 AND 25),
    equipped boolean NOT NULL DEFAULT false,
    locked boolean NOT NULL DEFAULT false,
    bound boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (account_id, character_id) REFERENCES characters (account_id, character_id),
    CHECK ((NOT is_training) OR (grade = 0))
);
-- 한 부위에는 하나만 착용한다. 동시 착용 경합의 마지막 방어선이다
CREATE UNIQUE INDEX ux_equipment_instances_equipped_slot ON equipment_instances (character_id, slot) WHERE equipped;
-- 성장 조회, 보관 칸 수 세기(미착용), 예약 스냅샷(착용)
CREATE INDEX ix_equipment_instances_character ON equipment_instances (character_id, equipped, item_id);

-- 강화 실패 보정. 키는 캐릭터, 부위, 현재 단계이며 장비 ID나 등급을 넣지 않는다
CREATE TABLE enhancement_pity
(
    character_id uuid NOT NULL REFERENCES characters (character_id),
    slot integer NOT NULL CHECK (slot BETWEEN 0 AND 5),
    stage integer NOT NULL CHECK (stage BETWEEN 0 AND 24),
    failures integer NOT NULL DEFAULT 0 CHECK (failures >= 0),
    PRIMARY KEY (character_id, slot, stage)
);

-- 정기 수련 투자 단계. 항목 40, 합계 100 상한을 DB도 지킨다
CREATE TABLE character_training
(
    character_id uuid PRIMARY KEY REFERENCES characters (character_id),
    attack integer NOT NULL DEFAULT 0 CHECK (attack BETWEEN 0 AND 40),
    health integer NOT NULL DEFAULT 0 CHECK (health BETWEEN 0 AND 40),
    defense integer NOT NULL DEFAULT 0 CHECK (defense BETWEEN 0 AND 40),
    crit integer NOT NULL DEFAULT 0 CHECK (crit BETWEEN 0 AND 40),
    energy integer NOT NULL DEFAULT 0 CHECK (energy BETWEEN 0 AND 40),
    CHECK ((attack + health + defense + crit + energy) <= 100)
);

-- 초기 지급 기록. 계정 최초분과 캐릭터분은 부분 유일 인덱스로 한 번만 넣는다(동시 생성도 한쪽만 지급)
CREATE TABLE initial_grants
(
    grant_row_id uuid PRIMARY KEY,
    family text NOT NULL CHECK (family IN ('AccountFirst', 'CharacterFirst')),
    account_id uuid NOT NULL REFERENCES accounts (account_id),
    character_id uuid NULL,
    grant_id text NOT NULL CHECK (length(grant_id) BETWEEN 1 AND 64),
    profile text NOT NULL CHECK (profile IN ('Trial', 'Standard')),
    content_version text NOT NULL CHECK (length(content_version) > 0),
    actor_id uuid NOT NULL,
    operation_id uuid NOT NULL,
    granted_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (actor_id, operation_id) REFERENCES operations (actor_id, operation_id),
    FOREIGN KEY (account_id, character_id) REFERENCES characters (account_id, character_id),
    CHECK ((family = 'AccountFirst') = (character_id IS NULL))
);
CREATE UNIQUE INDEX ux_initial_grants_account_first ON initial_grants (account_id) WHERE family = 'AccountFirst';
CREATE UNIQUE INDEX ux_initial_grants_character_first ON initial_grants (character_id) WHERE family = 'CharacterFirst';

-- 계정 영구 진행. 해금 여부는 이 값과 콘텐츠 요구 조건으로 읽을 때 계산한다
CREATE TABLE account_progress
(
    account_id uuid PRIMARY KEY REFERENCES accounts (account_id),
    intro_completed_at timestamptz NULL,
    fox_traces integer NOT NULL DEFAULT 0 CHECK (fox_traces >= 0),
    revision bigint NOT NULL DEFAULT 0 CHECK (revision >= 0),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp()
);

-- 지역 일반전 성공, 보스 첫 처치, 명시 해금 기록. 같은 기록은 한 번만 남는다
CREATE TABLE account_unlocks
(
    account_id uuid NOT NULL REFERENCES accounts (account_id),
    unlock_kind text NOT NULL CHECK (unlock_kind IN ('RegionCleared', 'BattleCleared', 'RegionUnlocked', 'BattleUnlocked')),
    target_id text NOT NULL CHECK (target_id ~ '^(region|battle)_[a-z0-9_]{1,57}$'),
    operation_id uuid NOT NULL,
    unlocked_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (account_id, unlock_kind, target_id)
);

-- 일일 경제 대사의 하루 한 번 선점. 여러 API 인스턴스 중 먼저 넣은 쪽만 그날 대사를 돈다.
-- 끝나지 않은 채 오래된 선점은 다른 인스턴스가 이어받는다
CREATE TABLE economy_reconciliation_runs
(
    run_date date PRIMARY KEY,
    claimed_by uuid NOT NULL,
    started_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    finished_at timestamptz NULL,
    mismatch_count bigint NULL CHECK (mismatch_count >= 0),
    CHECK ((finished_at IS NULL) = (mismatch_count IS NULL))
);

-- 성장 작업 영수증. 등록, 강화의 성공 여부와 제작한 장비 ID를 재전송에도 처음 결과로 돌려준다. 추가 전용이다.
-- actor_id는 멱등 actor인 계정(= account_id)이고, 중계한 마을 인스턴스는 town_instance_id에 따로 남긴다.
-- roll_basis_points는 확률 판정 작업(부적 등록, 장비 강화)에서 마을 서버가 뽑은 입력(0~9999)이다
CREATE TABLE growth_receipts
(
    receipt_id uuid PRIMARY KEY,
    actor_id uuid NOT NULL,
    operation_id uuid NOT NULL,
    account_id uuid NOT NULL,
    character_id uuid NOT NULL,
    operation_kind text NOT NULL CHECK (length(operation_kind) BETWEEN 1 AND 64),
    outcome integer NOT NULL CHECK (outcome BETWEEN 0 AND 2),
    item_id uuid NULL,
    loadout_revision bigint NOT NULL CHECK (loadout_revision >= 0),
    town_instance_id uuid NOT NULL,
    roll_basis_points integer NULL CHECK (roll_basis_points BETWEEN 0 AND 9999),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CHECK (actor_id = account_id),
    UNIQUE (actor_id, operation_id),
    FOREIGN KEY (actor_id, operation_id) REFERENCES operations (actor_id, operation_id),
    FOREIGN KEY (account_id, character_id) REFERENCES characters (account_id, character_id)
);
CREATE INDEX ix_growth_receipts_character ON growth_receipts (character_id, created_at, receipt_id);
CREATE TRIGGER growth_receipts_append_only BEFORE UPDATE OR DELETE OR TRUNCATE ON growth_receipts
    FOR EACH STATEMENT EXECUTE FUNCTION reject_append_only_mutation();

-- 배포 권한(README의 런타임 역할 예시에 추가): talisman_progress, character_training, account_progress,
-- economy_reconciliation_runs는 SELECT, INSERT, UPDATE. talisman_loadout, equipment_instances, enhancement_pity는
-- SELECT, INSERT, UPDATE, DELETE. initial_grants, account_unlocks, growth_receipts는 SELECT, INSERT만 준다
