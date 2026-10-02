-- next_recovery_at: 복구가 업무 원인으로 실패한 세션을 지수 백오프로 미룬다. 잠금/문장 제한 초과 같은 일시 실패는 세지 않는다.
-- ended_essence: SessionEnded 정산안이 지급할 정기 합계. 인스턴스별 24시간 누적 정기 상한을 저장된 값으로 검사한다
ALTER TABLE game_sessions
    ADD COLUMN next_recovery_at timestamptz NULL,
    ADD COLUMN ended_essence bigint NOT NULL DEFAULT 0,
    ADD CONSTRAINT ck_game_sessions_ended_essence CHECK (ended_essence >= 0);

-- 인스턴스별 최근 24시간 종료 정산안 합계 조회
CREATE INDEX ix_game_sessions_instance_settling ON game_sessions (instance_id, settling_at)
    WHERE settling_at IS NOT NULL;

-- 유휴 던전 배정은 먼저 등록한 프로세스부터 고른다(SKIP LOCKED)
CREATE INDEX ix_server_instances_idle_registered ON server_instances (role, registered_at, instance_id)
    WHERE state = 'Idle';

-- 만료된 입장 토큰 정리 작업이 오래된 행부터 지운다
CREATE INDEX ix_auth_join_tokens_expiry ON auth_join_tokens (expires_at, token_id);
