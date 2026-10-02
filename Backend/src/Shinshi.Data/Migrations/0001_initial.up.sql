CREATE TABLE accounts
(
    account_id uuid PRIMARY KEY,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    revision bigint NOT NULL DEFAULT 0 CHECK (revision >= 0)
);

CREATE TABLE server_instances
(
    instance_id uuid PRIMARY KEY,
    role integer NOT NULL CHECK (role IN (0, 1)),
    address text NOT NULL CHECK (length(address) > 0),
    port integer NOT NULL CHECK (port BETWEEN 1 AND 65535),
    build_id text NOT NULL CHECK (length(build_id) > 0),
    content_version text NOT NULL CHECK (length(content_version) > 0),
    state text NOT NULL CHECK (state IN ('Idle', 'Reserved', 'Active', 'Draining', 'Offline')),
    heartbeat_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (instance_id, role)
);
CREATE INDEX ix_server_instances_allocation ON server_instances (role, state, heartbeat_at, instance_id);

CREATE TABLE account_leases
(
    account_id uuid PRIMARY KEY REFERENCES accounts (account_id),
    lease_id uuid NOT NULL UNIQUE,
    instance_id uuid NOT NULL,
    role integer NOT NULL CHECK (role IN (0, 1)),
    expires_at timestamptz NOT NULL,
    FOREIGN KEY (instance_id, role) REFERENCES server_instances (instance_id, role)
);
CREATE INDEX ix_account_leases_expiry ON account_leases (expires_at, account_id);
CREATE INDEX ix_account_leases_instance ON account_leases (instance_id, account_id);

CREATE TABLE auth_invites
(
    invite_id uuid PRIMARY KEY,
    account_id uuid NOT NULL REFERENCES accounts (account_id),
    code_hash bytea NOT NULL UNIQUE CHECK (octet_length(code_hash) = 32),
    expires_at timestamptz NOT NULL,
    used_at timestamptz NULL
);
CREATE INDEX ix_auth_invites_account ON auth_invites (account_id, invite_id);

CREATE TABLE auth_refresh_tokens
(
    token_id uuid PRIMARY KEY,
    account_id uuid NOT NULL REFERENCES accounts (account_id),
    token_hash bytea NOT NULL UNIQUE CHECK (octet_length(token_hash) = 32),
    expires_at timestamptz NOT NULL,
    revoked_at timestamptz NULL,
    UNIQUE (account_id, token_id)
);
CREATE INDEX ix_auth_refresh_tokens_account ON auth_refresh_tokens (account_id, token_id);

CREATE TABLE auth_game_sessions
(
    game_session_id uuid PRIMARY KEY,
    parent_token_id uuid NOT NULL,
    account_id uuid NOT NULL REFERENCES accounts (account_id),
    expires_at timestamptz NOT NULL,
    revoked_at timestamptz NULL,
    FOREIGN KEY (account_id, parent_token_id) REFERENCES auth_refresh_tokens (account_id, token_id)
);
CREATE INDEX ix_auth_game_sessions_parent ON auth_game_sessions (parent_token_id, game_session_id);
CREATE INDEX ix_auth_game_sessions_account ON auth_game_sessions (account_id, game_session_id);

CREATE TABLE characters
(
    character_id uuid PRIMARY KEY,
    account_id uuid NOT NULL REFERENCES accounts (account_id),
    class_id integer NOT NULL CHECK (class_id IN (0, 1, 2)),
    essence bigint NOT NULL DEFAULT 0 CHECK (essence >= 0),
    shards bigint NOT NULL DEFAULT 0 CHECK (shards >= 0),
    revision bigint NOT NULL DEFAULT 0 CHECK (revision >= 0),
    UNIQUE (account_id, character_id)
);
CREATE INDEX ix_characters_account ON characters (account_id, character_id);

CREATE TABLE account_wallets
(
    account_id uuid PRIMARY KEY REFERENCES accounts (account_id),
    currency bigint NOT NULL DEFAULT 0 CHECK (currency >= 0),
    tickets bigint NOT NULL DEFAULT 0 CHECK (tickets >= 0),
    action_value bigint NOT NULL DEFAULT 0 CHECK (action_value BETWEEN 0 AND 100),
    action_anchor timestamptz NOT NULL DEFAULT clock_timestamp(),
    revision bigint NOT NULL DEFAULT 0 CHECK (revision >= 0)
);

CREATE TABLE account_items
(
    account_id uuid NOT NULL REFERENCES accounts (account_id),
    definition_id text NOT NULL CHECK (length(definition_id) > 0),
    binding_kind text NOT NULL CHECK (length(binding_kind) > 0),
    quantity bigint NOT NULL DEFAULT 0 CHECK (quantity >= 0),
    PRIMARY KEY (account_id, definition_id, binding_kind)
);

CREATE TABLE game_sessions
(
    session_id uuid PRIMARY KEY,
    mode text NOT NULL CHECK (mode IN ('Normal', 'Boss')),
    map_id text NOT NULL CHECK (length(map_id) > 0),
    state text NOT NULL CHECK (state IN ('Reserved', 'Loading', 'Starting', 'Running', 'Settling',
        'Auction', 'Returning', 'RecoveryPending', 'Closed')),
    starting_count integer NOT NULL CHECK (starting_count BETWEEN 1 AND 4),
    content_version text NOT NULL CHECK (length(content_version) > 0),
    instance_id uuid NOT NULL REFERENCES server_instances (instance_id),
    server_lease uuid NOT NULL,
    started_at timestamptz NULL,
    result text NULL,
    revision bigint NOT NULL DEFAULT 0 CHECK (revision >= 0)
);
CREATE INDEX ix_game_sessions_instance_state ON game_sessions (instance_id, state, session_id);

CREATE TABLE operations
(
    actor_id uuid NOT NULL,
    operation_id uuid NOT NULL,
    operation_kind text NOT NULL CHECK (length(operation_kind) > 0),
    request_hash text NOT NULL CHECK (request_hash ~ '^[0-9A-F]{64}$'),
    status text NOT NULL CHECK (status IN ('Pending', 'Committed', 'Rejected')),
    result_json jsonb NULL,
    committed_revision bigint NULL CHECK (committed_revision >= 0),
    receipt_id text NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    committed_at timestamptz NULL,
    PRIMARY KEY (actor_id, operation_id),
    CHECK ((status = 'Pending' AND result_json IS NULL AND committed_at IS NULL
                AND committed_revision IS NULL AND receipt_id IS NULL)
        OR (status IN ('Committed', 'Rejected') AND result_json IS NOT NULL
                AND committed_at IS NOT NULL AND committed_revision IS NOT NULL)),
    CHECK (status <> 'Committed' OR (receipt_id IS NOT NULL AND length(receipt_id) > 0)),
    CHECK (result_json IS NULL OR jsonb_typeof(result_json) = 'object'),
    CHECK (result_json IS NULL OR (result_json ? 'reasonCode' AND result_json ? 'status'
        AND result_json ? 'operationId' AND result_json ? 'committedRevision'
        AND result_json ? 'serverTimeUnixMs' AND result_json ? 'receiptId')),
    CHECK (status <> 'Rejected' OR ((result_json ->> 'reasonCode')::integer > 0)),
    CHECK (status <> 'Committed' OR ((result_json ->> 'reasonCode')::integer = 0)),
    CHECK (status = 'Pending' OR (result_json ->> 'status')::integer =
        CASE status WHEN 'Committed' THEN 1 WHEN 'Rejected' THEN 2 END),
    CHECK (result_json IS NULL OR (result_json ->> 'operationId')::uuid = operation_id),
    CHECK (result_json IS NULL OR (result_json ->> 'committedRevision')::bigint = committed_revision)
);

