-- Review-only rollback: auth and economic history are permanently lost. Never run on a live service.
DROP TABLE outbox_inbox;
DROP TABLE outbox;
DROP TABLE item_history;
DROP TABLE economy_ledger;
DROP FUNCTION reject_append_only_mutation();
DROP TABLE auth_join_tokens;
DROP TABLE auth_server_leases;
ALTER TABLE auth_game_sessions DROP COLUMN token_hash;
DROP TABLE auth_access_tokens;
DROP INDEX ix_auth_refresh_tokens_family;
ALTER TABLE auth_refresh_tokens
    DROP CONSTRAINT uq_auth_refresh_family_token,
    DROP CONSTRAINT fk_auth_refresh_family,
    DROP COLUMN replaced_by_token_id,
    DROP COLUMN consumed_at,
    DROP COLUMN family_id;
DROP TABLE auth_token_families;
ALTER TABLE auth_invites DROP COLUMN revoked_at, DROP COLUMN used_count, DROP COLUMN max_uses;
