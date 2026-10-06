-- 생활(행동력, 채집, 물약 제작)과 지참 물약 사용 되돌림.
-- 근거: docs/design/life-training.md(행동력 계정 공용, 상한 100, 5분마다 1 회복), Assets/09.Data/Life/LifeConfig.asset

-- 처음 만드는 지갑 행은 행동력 상한(100)으로 시작한다. 기존 행은 바꾸지 않는다(소급 지급 없음, 잠정 결정 8).
-- 회복 기준 시각(action_anchor)의 기본값은 0001과 같은 clock_timestamp()다
ALTER TABLE account_wallets ALTER COLUMN action_value SET DEFAULT 100;

-- POTION_USE_REVERTED(7)를 받는다. 0012가 PARTICIPANT_DEPARTED(6)까지 열고 0013이 7까지 연다.
-- 0012와 같이 NOT VALID 뒤 VALIDATE CONSTRAINT로 나눈다(한 트랜잭션 적용이라 잠금은 커밋까지 남는다. 0012 주석 참고)
ALTER TABLE session_events
    DROP CONSTRAINT ck_session_events_event_kind,
    ADD CONSTRAINT ck_session_events_event_kind CHECK (event_kind BETWEEN 1 AND 7) NOT VALID;
ALTER TABLE session_events VALIDATE CONSTRAINT ck_session_events_event_kind;

-- 원 POTION_USED 한 건은 한 번만 되돌린다. payload.revertsEventId는 API가 GUID "D" 형식(소문자)으로 저장한다.
-- 원 이벤트 존재와 같은 세션, 같은 참가자 여부는 API가 세션 행 잠금 안에서 확인하고, 이 인덱스는 중복 저장의 마지막 방어선이다.
-- CREATE INDEX CONCURRENTLY는 트랜잭션 안에서 실행할 수 없는데 마이그레이션 실행기(SqlMigrationRunner)는 파일마다 트랜잭션을 연다.
-- 배포 전 소규모 DB를 전제로 일반 생성을 쓴다. 운영 DB에서 다시 만들 때는 실행기 밖에서 CONCURRENTLY로 만든다
CREATE UNIQUE INDEX ux_session_events_potion_revert ON session_events ((payload ->> 'revertsEventId'))
    WHERE event_kind = 7;
