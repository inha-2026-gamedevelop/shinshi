-- 분류 열을 지운다. 이미 입력한 분류 값은 사라지고 이후 모든 공지는 분류 없이 보인다(복구 불가).
ALTER TABLE service_notices DROP CONSTRAINT ck_service_notices_category;
ALTER TABLE service_notices DROP COLUMN category;
