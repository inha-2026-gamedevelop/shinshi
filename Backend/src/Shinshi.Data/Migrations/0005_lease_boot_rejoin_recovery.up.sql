-- 서버 임대를 발급받은 프로세스(bootId)에 묶는다. 인증은 장애 취소가 Offline으로 확정한 boot의 임대만 거절하고,
-- heartbeat 부재는 효과 지점(/start, /events, 배정)에서만 판정한다. 0005 이전 임대는 boot_id가 없다
ALTER TABLE auth_server_leases ADD COLUMN boot_id uuid NULL;

-- 마을 입장 토큰은 출정과 참가자가 없다. 토큰 역할과 교환 서버 임대의 역할을 FK로 맞춘다
ALTER TABLE auth_join_tokens
    ADD COLUMN server_role integer NOT NULL DEFAULT 1,
    ALTER COLUMN session_id DROP NOT NULL,
    ALTER COLUMN participant_id DROP NOT NULL,
    ADD CONSTRAINT ck_auth_join_tokens_server_role CHECK (server_role IN (0, 1)),
    ADD CONSTRAINT ck_auth_join_tokens_role_target CHECK (
        (server_role = 1 AND session_id IS NOT NULL AND participant_id IS NOT NULL)
        OR (server_role = 0 AND session_id IS NULL AND participant_id IS NULL)),
    ADD CONSTRAINT fk_auth_join_tokens_lease_role FOREIGN KEY (server_lease_id, instance_id, server_role)
        REFERENCES auth_server_leases (lease_id, instance_id, role);
-- 마을 토큰 재발급이 같은 계정의 미교환 토큰을 폐기할 때 쓴다
CREATE INDEX ix_auth_join_tokens_town_account ON auth_join_tokens (account_id, token_id)
    WHERE server_role = 0 AND consumed_at IS NULL AND revoked_at IS NULL;

-- settling_at: 정산 이벤트 저장 뒤 /settlement가 오지 않으면 복구 작업이 같은 operationId로 정산한다.
-- recovery_failures: 복구가 예외로 실패한 횟수. 한 세션의 반복 실패가 다른 세션 복구를 막지 않게 정렬과 격리에 쓴다
ALTER TABLE game_sessions
    ADD COLUMN settling_at timestamptz NULL,
    ADD COLUMN recovery_failures integer NOT NULL DEFAULT 0,
    ADD CONSTRAINT ck_game_sessions_recovery_failures CHECK (recovery_failures >= 0);
UPDATE game_sessions SET settling_at = clock_timestamp() WHERE state = 'Settling';
ALTER TABLE game_sessions
    ADD CONSTRAINT ck_game_sessions_settling_at CHECK (state <> 'Settling' OR settling_at IS NOT NULL);
CREATE INDEX ix_game_sessions_recovery ON game_sessions (recovery_failures, session_id)
    WHERE state IN ('Running', 'Settling');

-- 행 트리거는 TRUNCATE를 막지 못한다. 런타임 권한에서 빠져 있어도 잘못 설정된 역할을 한 번 더 막는다
CREATE TRIGGER tr_session_events_no_truncate BEFORE TRUNCATE ON session_events
    FOR EACH STATEMENT EXECUTE FUNCTION session_events_append_only();
