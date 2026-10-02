-- 되돌리면 출정 예약, 세션 이벤트, 체크포인트 이력이 사라진다. 잔액 총량(tickets, quantity, essence)은 보존되지만
-- 열린 예약의 예약량과 이벤트 원본은 복구할 수 없으므로 열린 출정이 없는 상태에서만 되돌린다
DROP TABLE session_checkpoints;
DROP TRIGGER tr_session_events_append_only ON session_events;
DROP TABLE session_events;
DROP FUNCTION session_events_append_only();
DROP TABLE reservations;
DROP TABLE session_participants;

DROP INDEX ix_game_sessions_open;
DROP INDEX ix_game_sessions_reserved_expiry;
ALTER TABLE game_sessions
    DROP CONSTRAINT ck_game_sessions_result,
    DROP CONSTRAINT ck_game_sessions_closed,
    DROP CONSTRAINT ck_game_sessions_ended_event,
    DROP CONSTRAINT ck_game_sessions_reserved_expiry,
    DROP COLUMN closed_at,
    DROP COLUMN settlement_operation_id,
    DROP COLUMN end_reason,
    DROP COLUMN ended_event_seq,
    DROP COLUMN last_event_seq,
    DROP COLUMN created_at,
    DROP COLUMN expires_at,
    DROP COLUMN instance_boot_id,
    DROP COLUMN reserved_by_instance_id;

ALTER TABLE account_items
    DROP CONSTRAINT ck_account_items_reserved,
    DROP COLUMN reserved;
ALTER TABLE account_wallets
    DROP CONSTRAINT ck_account_wallets_tickets_reserved,
    DROP COLUMN tickets_reserved;

ALTER TABLE server_instances
    DROP COLUMN registered_at,
    DROP COLUMN boot_id;
