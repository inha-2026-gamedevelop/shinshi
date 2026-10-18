-- 마을 정원. 게임 서버가 heartbeat로 보고한다. NULL은 아직 보고 전이거나 구버전 서버라 정원을 제한하지 않는다
ALTER TABLE server_instances
    ADD COLUMN capacity integer NULL CHECK (capacity IS NULL OR capacity > 0),
    ADD COLUMN reported_connections integer NULL CHECK (reported_connections IS NULL OR reported_connections >= 0);
