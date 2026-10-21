-- 요청 ID 원문과 발급 토큰 원문은 저장하지 않는다. 기존 클라이언트는 NULL로 이전 재사용 방어를 유지한다.
ALTER TABLE auth_refresh_tokens
    ADD COLUMN rotation_request_hash bytea NULL
    CHECK (rotation_request_hash IS NULL OR octet_length(rotation_request_hash) = 32);
