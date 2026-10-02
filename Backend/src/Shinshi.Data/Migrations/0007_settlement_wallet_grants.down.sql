-- 되돌리면 참가자별 기본 정기 적립 합계를 잃는다. 열린 출정이 없을 때 되돌린다.
-- session_events는 추가 전용이라 이미 저장된 BASE_ESSENCE_ACCRUED(5) 행을 지울 수 없다. 이전 범위 CHECK는 NOT VALID로
-- 다시 걸어 기존 행은 남기고 새 행만 막는다. 정산안 JSON의 지갑, 부적 칸은 0006 이하 코드가 읽지 않으므로 스키마 변경이 없다
ALTER TABLE session_events
    DROP CONSTRAINT ck_session_events_event_kind,
    ADD CONSTRAINT session_events_event_kind_check CHECK (event_kind BETWEEN 1 AND 4) NOT VALID;

ALTER TABLE session_participants
    DROP CONSTRAINT ck_session_participants_base_essence,
    DROP COLUMN base_essence_accrued;
