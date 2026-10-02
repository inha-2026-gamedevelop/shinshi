-- 검토용 되돌리기. 운영자 계정, 감사 로그, 지급과 우편함, 서비스 상태와 공지가 영구히 사라지므로 운영 DB에서 실행하지 않는다.
-- 원장에 남은 AdminGrant, InboxClaim 행은 0003 테이블이라 보존된다. reject_append_only_mutation 함수는 0003 소유라 남긴다
DROP INDEX ix_game_sessions_state_created;
DROP INDEX ix_economy_ledger_account_time;
DROP INDEX ix_session_participants_account;
DROP TABLE service_notices;
DROP TABLE service_state;
DROP TABLE reward_inbox;
DROP TABLE admin_grants;
DROP TABLE admin_operation_claims;
DROP TABLE admin_audit_log;
DROP TABLE admin_sessions;
DROP TABLE admin_users;
