-- 되돌리면 복구 백오프 시각과 종료 정산안 정기 합계를 잃는다. 열린 출정이 없을 때 되돌린다.
-- 정리 작업이 지운 만료 입장 토큰 행은 복원할 수 없다(최대 60초 수명의 일회 토큰이라 업무 영향은 없다)
DROP INDEX ix_auth_join_tokens_expiry;
DROP INDEX ix_server_instances_idle_registered;
DROP INDEX ix_game_sessions_instance_settling;

ALTER TABLE game_sessions
    DROP CONSTRAINT ck_game_sessions_ended_essence,
    DROP COLUMN ended_essence,
    DROP COLUMN next_recovery_at;
