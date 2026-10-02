-- Existing invitation hashes are retained. Provision new invitations with the configured HMAC key.
ALTER TABLE auth_invites
    ADD COLUMN max_uses bigint NOT NULL DEFAULT 1 CHECK (max_uses > 0),
    ADD COLUMN used_count bigint NOT NULL DEFAULT 0 CHECK ((used_count >= 0) AND (used_count <= max_uses)),
    ADD COLUMN revoked_at timestamptz NULL;
UPDATE auth_invites SET used_count = 1 WHERE used_at IS NOT NULL;

CREATE TABLE auth_token_families
(
    family_id uuid PRIMARY KEY,
    account_id uuid NOT NULL REFERENCES accounts (account_id),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    revoked_at timestamptz NULL,
    UNIQUE (account_id, family_id)
);
CREATE INDEX ix_auth_token_families_account ON auth_token_families (account_id, family_id);

ALTER TABLE auth_refresh_tokens
    ADD COLUMN family_id uuid NULL,
    ADD COLUMN consumed_at timestamptz NULL,
    ADD COLUMN replaced_by_token_id uuid NULL REFERENCES auth_refresh_tokens (token_id);
INSERT INTO auth_token_families (family_id, account_id, revoked_at)
    SELECT token_id, account_id, revoked_at FROM auth_refresh_tokens;
UPDATE auth_refresh_tokens SET family_id = token_id;
ALTER TABLE auth_refresh_tokens
    ALTER COLUMN family_id SET NOT NULL,
    ADD CONSTRAINT fk_auth_refresh_family FOREIGN KEY (account_id, family_id)
        REFERENCES auth_token_families (account_id, family_id),
    ADD CONSTRAINT uq_auth_refresh_family_token UNIQUE (account_id, family_id, token_id);
CREATE INDEX ix_auth_refresh_tokens_family ON auth_refresh_tokens (family_id, token_id);

CREATE TABLE auth_access_tokens
(
    token_id uuid PRIMARY KEY,
    account_id uuid NOT NULL,
    family_id uuid NOT NULL,
    refresh_token_id uuid NOT NULL,
    token_hash bytea NOT NULL UNIQUE CHECK (octet_length(token_hash) = 32),
    expires_at timestamptz NOT NULL,
    revoked_at timestamptz NULL,
    FOREIGN KEY (account_id, family_id, refresh_token_id)
        REFERENCES auth_refresh_tokens (account_id, family_id, token_id)
);
CREATE INDEX ix_auth_access_tokens_family ON auth_access_tokens (family_id, token_id);
ALTER TABLE auth_game_sessions ADD COLUMN token_hash bytea NULL UNIQUE
    CHECK ((token_hash IS NULL) OR (octet_length(token_hash) = 32));

CREATE TABLE auth_server_leases
(
    lease_id uuid PRIMARY KEY,
    service_id uuid NOT NULL,
    instance_id uuid NOT NULL,
    role integer NOT NULL CHECK (role IN (0, 1)),
    token_hash bytea NOT NULL UNIQUE CHECK (octet_length(token_hash) = 32),
    expires_at timestamptz NOT NULL,
    revoked_at timestamptz NULL,
    FOREIGN KEY (instance_id, role) REFERENCES server_instances (instance_id, role),
    UNIQUE (lease_id, instance_id),
    UNIQUE (lease_id, instance_id, role)
);
CREATE INDEX ix_auth_server_leases_instance ON auth_server_leases (instance_id, expires_at);

CREATE TABLE auth_join_tokens
(
    token_id uuid PRIMARY KEY,
    account_id uuid NOT NULL,
    family_id uuid NOT NULL,
    character_id uuid NOT NULL,
    participant_id uuid NOT NULL,
    session_id uuid NOT NULL REFERENCES game_sessions (session_id),
    instance_id uuid NOT NULL,
    server_lease_id uuid NOT NULL,
    token_hash bytea NOT NULL UNIQUE CHECK (octet_length(token_hash) = 32),
    expires_at timestamptz NOT NULL,
    consumed_at timestamptz NULL,
    revoked_at timestamptz NULL,
    FOREIGN KEY (account_id, family_id) REFERENCES auth_token_families (account_id, family_id),
    FOREIGN KEY (account_id, character_id) REFERENCES characters (account_id, character_id),
    FOREIGN KEY (server_lease_id, instance_id) REFERENCES auth_server_leases (lease_id, instance_id)
);
CREATE INDEX ix_auth_join_tokens_family ON auth_join_tokens (family_id, token_id);
CREATE INDEX ix_auth_join_tokens_participant ON auth_join_tokens (session_id, participant_id, token_id);
CREATE INDEX ix_auth_join_tokens_instance ON auth_join_tokens (instance_id, token_id);

CREATE TABLE economy_ledger
(
    ledger_id uuid PRIMARY KEY,
    entry_key text NOT NULL CHECK (length(entry_key) > 0),
    occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    account_id uuid NOT NULL REFERENCES accounts (account_id),
    character_id uuid NULL,
    asset_kind text NOT NULL CHECK (asset_kind IN ('Currency', 'Tickets', 'Essence', 'Shards', 'AccountItem')),
    asset_id text NOT NULL DEFAULT '',
    binding_kind text NOT NULL DEFAULT '',
    delta bigint NOT NULL CHECK (delta <> 0),
    balance_after bigint NOT NULL CHECK (balance_after >= 0),
    reason integer NOT NULL CHECK (reason > 0),
    source_type text NOT NULL CHECK (length(source_type) > 0),
    source_id text NOT NULL CHECK (length(source_id) > 0),
    operation_id uuid NOT NULL,
    actor_kind text NOT NULL CHECK (actor_kind IN ('Player', 'Server', 'Admin', 'System')),
    actor_id uuid NOT NULL,
    UNIQUE (actor_id, operation_id, entry_key),
    FOREIGN KEY (actor_id, operation_id) REFERENCES operations (actor_id, operation_id),
    FOREIGN KEY (account_id, character_id) REFERENCES characters (account_id, character_id),
    CHECK ((asset_kind IN ('Currency', 'Tickets') AND character_id IS NULL AND asset_id = '' AND binding_kind = '')
        OR (asset_kind IN ('Essence', 'Shards') AND character_id IS NOT NULL AND asset_id = '' AND binding_kind = '')
        OR (asset_kind = 'AccountItem' AND character_id IS NULL AND length(asset_id) > 0 AND length(binding_kind) > 0))
);
CREATE INDEX ix_economy_ledger_asset ON economy_ledger
    (account_id, character_id, asset_kind, asset_id, binding_kind, occurred_at, ledger_id);
CREATE INDEX ix_economy_ledger_source ON economy_ledger (source_type, source_id, ledger_id);

CREATE TABLE item_history
(
    item_id uuid NOT NULL,
    seq bigint NOT NULL CHECK (seq >= 0),
    event_kind text NOT NULL CHECK (length(event_kind) > 0),
    before_state jsonb NULL CHECK ((before_state IS NULL) OR (jsonb_typeof(before_state) = 'object')),
    after_state jsonb NULL CHECK ((after_state IS NULL) OR (jsonb_typeof(after_state) = 'object')),
    owner_id uuid NOT NULL REFERENCES accounts (account_id),
    actor_id uuid NOT NULL,
    operation_id uuid NOT NULL,
    occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (item_id, seq),
    FOREIGN KEY (actor_id, operation_id) REFERENCES operations (actor_id, operation_id),
    CHECK ((before_state IS NOT NULL) OR (after_state IS NOT NULL))
);
CREATE INDEX ix_item_history_owner ON item_history (owner_id, occurred_at, item_id, seq);
CREATE INDEX ix_item_history_operation ON item_history (actor_id, operation_id, item_id, seq);

CREATE TABLE outbox
(
    event_id uuid PRIMARY KEY,
    aggregate_id uuid NOT NULL,
    consumer_key text NOT NULL CHECK (length(consumer_key) > 0),
    event_kind text NOT NULL CHECK (length(event_kind) > 0),
    payload jsonb NOT NULL CHECK (jsonb_typeof(payload) = 'object'),
    actor_id uuid NOT NULL,
    operation_id uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    delivered_at timestamptz NULL,
    retries integer NOT NULL DEFAULT 0 CHECK (retries >= 0),
    next_attempt_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (actor_id, operation_id) REFERENCES operations (actor_id, operation_id),
    UNIQUE (consumer_key, event_id)
);
CREATE INDEX ix_outbox_dispatch ON outbox (consumer_key, next_attempt_at, created_at, event_id)
    WHERE delivered_at IS NULL;
CREATE INDEX ix_outbox_operation ON outbox (actor_id, operation_id, event_id);
CREATE TABLE outbox_inbox
(
    consumer_key text NOT NULL,
    event_id uuid NOT NULL,
    consumed_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (consumer_key, event_id),
    FOREIGN KEY (consumer_key, event_id) REFERENCES outbox (consumer_key, event_id)
);

-- Grants remain a deployment concern; these triggers also reject mutations by a misconfigured runtime role.
CREATE FUNCTION reject_append_only_mutation() RETURNS trigger LANGUAGE plpgsql AS $append_only$
BEGIN
    RAISE EXCEPTION 'append-only table cannot be mutated' USING ERRCODE = '42501';
END;
$append_only$;
CREATE TRIGGER economy_ledger_append_only BEFORE UPDATE OR DELETE OR TRUNCATE ON economy_ledger
    FOR EACH STATEMENT EXECUTE FUNCTION reject_append_only_mutation();
CREATE TRIGGER item_history_append_only BEFORE UPDATE OR DELETE OR TRUNCATE ON item_history
    FOR EACH STATEMENT EXECUTE FUNCTION reject_append_only_mutation();
CREATE TRIGGER outbox_inbox_append_only BEFORE UPDATE OR DELETE OR TRUNCATE ON outbox_inbox
    FOR EACH STATEMENT EXECUTE FUNCTION reject_append_only_mutation();
