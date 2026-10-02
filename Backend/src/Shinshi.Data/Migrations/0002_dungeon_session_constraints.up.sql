-- UNIQUE가 동일 열의 조회를 지원하므로 중복 일반 인덱스만 제거한다
DROP INDEX ix_characters_account;
DROP INDEX ix_auth_refresh_tokens_account;

-- 전장 세션은 Dungeon 서버에만 배정한다. 기존 잘못된 배정은 FK 검증에서 중단한다
ALTER TABLE game_sessions
    ADD COLUMN role integer NOT NULL DEFAULT 1,
    ADD CONSTRAINT ck_game_sessions_dungeon_role CHECK (role = 1),
    DROP CONSTRAINT game_sessions_instance_id_fkey,
    ADD CONSTRAINT fk_game_sessions_instance_role
        FOREIGN KEY (instance_id, role) REFERENCES server_instances (instance_id, role);
