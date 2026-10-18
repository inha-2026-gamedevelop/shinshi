-- 되돌리면 보고된 정원과 접속 수를 잃는다. 다음 heartbeat 전까지 입장권 발급은 정원을 제한하지 않는다
ALTER TABLE server_instances
    DROP COLUMN reported_connections,
    DROP COLUMN capacity;
