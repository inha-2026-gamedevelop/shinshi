-- role은 Dungeon 상수이므로 열 제거로 사용자 데이터가 사라지지 않는다
ALTER TABLE game_sessions
    DROP CONSTRAINT fk_game_sessions_instance_role,
    DROP CONSTRAINT ck_game_sessions_dungeon_role,
    DROP COLUMN role,
    ADD CONSTRAINT game_sessions_instance_id_fkey
        FOREIGN KEY (instance_id) REFERENCES server_instances (instance_id);

CREATE INDEX ix_auth_refresh_tokens_account ON auth_refresh_tokens (account_id, token_id);
CREATE INDEX ix_characters_account ON characters (account_id, character_id);
