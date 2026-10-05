-- 되돌림 유일 인덱스를 지운다. 이후 같은 원 이벤트의 중복 되돌림은 API 검사로만 막힌다
DROP INDEX ux_session_events_potion_revert;

-- session_events는 추가 전용이라 이미 저장된 POTION_USE_REVERTED(7) 행을 지울 수 없다. 직전 범위 CHECK는 NOT VALID로 걸어
-- 기존 행은 검사하지 않고 새 행만 막는다. 직전 범위 1..6은 0012(PARTICIPANT_DEPARTED = 6)가 연 범위다
ALTER TABLE session_events
    DROP CONSTRAINT ck_session_events_event_kind,
    ADD CONSTRAINT ck_session_events_event_kind CHECK (event_kind BETWEEN 1 AND 6) NOT VALID;

-- 새 지갑 행의 행동력 기본값을 0001의 0으로 돌린다. 0013 뒤에 만든 지갑 행의 값은 그대로 남는다
ALTER TABLE account_wallets ALTER COLUMN action_value SET DEFAULT 0;
