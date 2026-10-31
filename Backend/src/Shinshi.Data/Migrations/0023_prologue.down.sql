-- 되돌리면 캐릭터의 프롤로그 단계, 메뉴 안내 완료 기록, 프롤로그 완료 표식을 잃는다. 이미 지급한 완료 지급품과 원장은 남는다.
-- 프롤로그 출정 행(game_sessions mode 'Prologue')은 session_events 등 추가 전용 기록이 참조해 지울 수 없으므로,
-- 이전 CHECK를 NOT VALID로 다시 걸어 기존 행은 남기고 새 행만 막는다(0012 down과 같은 방식). 운영 DB에서 실행하지 않는다
DROP TABLE prologue_completions;
DROP TABLE account_menu_guides;

ALTER TABLE game_sessions
    DROP CONSTRAINT ck_game_sessions_prologue_solo,
    DROP CONSTRAINT ck_game_sessions_boss_mode,
    DROP CONSTRAINT ck_game_sessions_mode,
    ADD CONSTRAINT game_sessions_mode_check CHECK (mode IN ('Normal', 'Boss')) NOT VALID,
    ADD CONSTRAINT ck_game_sessions_boss_mode CHECK (boss_id IS NULL OR mode = 'Boss') NOT VALID;

ALTER TABLE characters
    DROP CONSTRAINT ck_characters_prologue_wolbaek_potions,
    DROP CONSTRAINT ck_characters_prologue_stage,
    DROP COLUMN prologue_talisman_checked,
    DROP COLUMN prologue_wolbaek_potions,
    DROP COLUMN prologue_stage;
