-- 프롤로그(캐릭터 생성 직후 1인 튜토리얼 출정)와 성장 메뉴 안내 완료.
-- 단계 번호는 Shinshi.Contracts.PrologueCodes.Stage(0 INTRO, 1 FOREST, 2 WOLBAEK, 3 ENDING, 4 DONE)이고, 물약 상한은 MAX_POTIONS(9)다.
-- 잠금 순서는 다른 경제 쓰기와 같은 계정 -> 캐릭터 -> 아이템이다(PostgresPrologueRepository)

-- 이 파일 전에 만든 캐릭터는 프롤로그가 없던 시기의 캐릭터라 완료(4)로 채운다. ADD COLUMN의 DEFAULT 4가 기존 행을 채운 뒤
-- 기본값을 0으로 바꾼다. 새 캐릭터 INSERT(PostgresCharacterRepository)는 0을 명시하고, 기본값 0은 그 밖의 INSERT(시험 등)용이다
ALTER TABLE characters
    ADD COLUMN prologue_stage smallint NOT NULL DEFAULT 4,
    ADD COLUMN prologue_wolbaek_potions smallint NOT NULL DEFAULT 0,
    ADD COLUMN prologue_talisman_checked boolean NOT NULL DEFAULT false,
    ADD CONSTRAINT ck_characters_prologue_stage CHECK (prologue_stage BETWEEN 0 AND 4),
    ADD CONSTRAINT ck_characters_prologue_wolbaek_potions CHECK (prologue_wolbaek_potions BETWEEN 0 AND 9);
ALTER TABLE characters ALTER COLUMN prologue_stage SET DEFAULT 0;

-- 계정이 마친 성장 메뉴 안내. 경제 효과가 없어 operations를 쓰지 않고 (계정, 메뉴) 유일 키로 재전송을 받는다.
-- 계정당 상한(PrologueCodes.MAX_MENU_GUIDES = 64)은 계정 행을 잠근 뒤 코드에서 센다. 목록과 개수는 기본 키 인덱스로 읽는다
CREATE TABLE account_menu_guides
(
    account_id uuid NOT NULL REFERENCES accounts (account_id),
    menu_id text NOT NULL CHECK (menu_id ~ '^[a-z0-9_]{1,40}$'),
    completed_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (account_id, menu_id)
);

-- 프롤로그 완료 기본 지급품을 캐릭터마다 한 번만 주기 위한 멱등 표식. 먼저 넣은 쪽(직접 완료 또는 건너뛰기)만 지급한다.
-- 직접 완료(Played)는 완료를 기록한 프롤로그 출정을, 건너뛰기(Skipped)는 출정 없음을 남긴다. 행을 고치거나 지우지 않는다
CREATE TABLE prologue_completions
(
    character_id uuid PRIMARY KEY REFERENCES characters (character_id),
    account_id uuid NOT NULL REFERENCES accounts (account_id),
    session_id uuid NULL REFERENCES game_sessions (session_id),
    completed_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    source text NOT NULL CHECK (source IN ('Played', 'Skipped')),
    CONSTRAINT fk_prologue_completions_owner FOREIGN KEY (account_id, character_id)
        REFERENCES characters (account_id, character_id),
    CONSTRAINT ck_prologue_completions_session CHECK ((source = 'Played') = (session_id IS NOT NULL))
);

-- 프롤로그 출정. 0001의 mode 열 CHECK는 이름 없이 만들어 PostgreSQL 기본 이름(game_sessions_mode_check)을 쓴다(0012의 reward_inbox와 같은 방식).
-- 프롤로그는 보스 월백 교전이 있어 boss_id를 쓰고, 1인 전용이라 starting_count가 1이다
ALTER TABLE game_sessions
    DROP CONSTRAINT game_sessions_mode_check,
    DROP CONSTRAINT ck_game_sessions_boss_mode,
    ADD CONSTRAINT ck_game_sessions_mode CHECK (mode IN ('Normal', 'Boss', 'Prologue')),
    ADD CONSTRAINT ck_game_sessions_boss_mode CHECK (boss_id IS NULL OR mode IN ('Boss', 'Prologue')),
    ADD CONSTRAINT ck_game_sessions_prologue_solo CHECK (mode <> 'Prologue' OR starting_count = 1);

-- 배포 권한(README의 런타임 역할 예시에 추가): account_menu_guides, prologue_completions는 SELECT, INSERT만 준다.
-- characters의 새 열은 기존 테이블 권한을 따른다
