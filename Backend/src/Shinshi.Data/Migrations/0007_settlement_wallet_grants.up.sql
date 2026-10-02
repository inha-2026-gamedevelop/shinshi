-- 완료 웨이브 기본 정기 적립(BASE_ESSENCE_ACCRUED = 5). 서비스 장애 취소는 이 합계를 전액, essence_accrued(추가 정기)는
-- 50% 내림으로 지급한다(사용자 결정 2026-10-01). 정산 전 출정 원장일 뿐 소비 가능한 잔액이 아니다
ALTER TABLE session_participants
    ADD COLUMN base_essence_accrued bigint NOT NULL DEFAULT 0,
    ADD CONSTRAINT ck_session_participants_base_essence CHECK (base_essence_accrued >= 0);

-- 새 이벤트 종류를 받는다. 0004의 열 CHECK는 이름 없이 만들어 PostgreSQL 기본 이름을 쓴다
ALTER TABLE session_events
    DROP CONSTRAINT session_events_event_kind_check,
    ADD CONSTRAINT ck_session_events_event_kind CHECK (event_kind BETWEEN 1 AND 5);
