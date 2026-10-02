-- 되돌리면 마을 입장 토큰 행(최대 60초 수명)을 지운다. 출정 토큰, 서버 임대 행, 세션 이벤트는 보존한다.
-- 임대의 boot 결합, 정산 정체 시각, 복구 실패 횟수는 복원할 수 없으므로 열린 출정이 없을 때 되돌린다
DROP TRIGGER tr_session_events_no_truncate ON session_events;

DROP INDEX ix_game_sessions_recovery;
ALTER TABLE game_sessions
    DROP CONSTRAINT ck_game_sessions_settling_at,
    DROP CONSTRAINT ck_game_sessions_recovery_failures,
    DROP COLUMN recovery_failures,
    DROP COLUMN settling_at;

DROP INDEX ix_auth_join_tokens_town_account;
DELETE FROM auth_join_tokens WHERE server_role = 0;
ALTER TABLE auth_join_tokens
    DROP CONSTRAINT fk_auth_join_tokens_lease_role,
    DROP CONSTRAINT ck_auth_join_tokens_role_target,
    DROP CONSTRAINT ck_auth_join_tokens_server_role,
    ALTER COLUMN participant_id SET NOT NULL,
    ALTER COLUMN session_id SET NOT NULL,
    DROP COLUMN server_role;

ALTER TABLE auth_server_leases DROP COLUMN boot_id;
