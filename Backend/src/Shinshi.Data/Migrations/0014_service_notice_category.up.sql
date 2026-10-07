-- 공지 분류. 런처 소식 탭의 분류이며 Update만 패치노트 탭에 나온다.
-- 기존 행은 기본값 'Notice'로 채워진다. 기본값이 상수라 테이블을 다시 쓰지 않는다.
ALTER TABLE service_notices
    ADD COLUMN category text NOT NULL DEFAULT 'Notice'
        CONSTRAINT ck_service_notices_category CHECK (category IN ('Notice', 'Update', 'Event', 'Maintenance'));
