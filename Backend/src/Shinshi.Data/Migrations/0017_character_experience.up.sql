ALTER TABLE characters
    ADD COLUMN initial_experience bigint NOT NULL DEFAULT 0 CHECK (initial_experience >= 0),
    ADD COLUMN total_experience bigint NOT NULL DEFAULT 0 CHECK (total_experience >= initial_experience),
    ADD COLUMN last_experience_session_id uuid NULL REFERENCES game_sessions(session_id),
    ADD COLUMN last_experience_event_seq bigint NOT NULL DEFAULT 0 CHECK (last_experience_event_seq >= 0),
    ADD CONSTRAINT ck_character_experience_cursor CHECK
        ((last_experience_session_id IS NULL) = (last_experience_event_seq = 0));

UPDATE characters SET initial_experience = 2850, total_experience = 2850;

ALTER TABLE game_sessions ADD COLUMN experience_settings jsonb NULL CHECK (experience_settings IS NULL OR jsonb_typeof(experience_settings) = 'object');

ALTER TABLE session_events DROP CONSTRAINT ck_session_events_event_kind,
    ADD CONSTRAINT ck_session_events_event_kind CHECK (event_kind BETWEEN 1 AND 8);

ALTER TABLE session_events ADD CONSTRAINT uq_session_event_experience_identity
    UNIQUE (session_id, event_seq, event_id, participant_id);
ALTER TABLE session_participants ADD CONSTRAINT uq_participant_experience_character
    UNIQUE (session_id, participant_id, character_id);

CREATE TABLE character_experience_events
(
    event_id uuid PRIMARY KEY REFERENCES session_events(event_id) DEFERRABLE INITIALLY DEFERRED,
    session_id uuid NOT NULL,
    event_seq bigint NOT NULL,
    participant_id uuid NOT NULL,
    character_id uuid NOT NULL REFERENCES characters(character_id),
    source_kind integer NOT NULL CHECK (source_kind BETWEEN 1 AND 3),
    source_id text NOT NULL CHECK (length(source_id) BETWEEN 1 AND 160),
    requested_amount bigint NOT NULL CHECK (requested_amount > 0),
    amount bigint NOT NULL CHECK (amount >= 0 AND amount <= requested_amount),
    balance_before bigint NOT NULL CHECK (balance_before >= 0),
    balance_after bigint NOT NULL CHECK (balance_after = balance_before + amount),
    committed_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    FOREIGN KEY (session_id, event_seq) REFERENCES session_events(session_id, event_seq) DEFERRABLE INITIALLY DEFERRED,
    FOREIGN KEY (session_id, participant_id, character_id)
        REFERENCES session_participants(session_id, participant_id, character_id),
    FOREIGN KEY (session_id, event_seq, event_id, participant_id)
        REFERENCES session_events(session_id, event_seq, event_id, participant_id) DEFERRABLE INITIALLY DEFERRED,
    UNIQUE (session_id, participant_id, source_kind, source_id)
);
CREATE UNIQUE INDEX ux_character_experience_source ON character_experience_events
    (session_id, participant_id, (CASE WHEN source_kind IN (1, 2) THEN 1 ELSE 3 END), source_id);
CREATE INDEX ix_character_experience_character ON character_experience_events(character_id, committed_at);
CREATE TRIGGER tr_character_experience_append_only BEFORE UPDATE OR DELETE ON character_experience_events
    FOR EACH ROW EXECUTE FUNCTION session_events_append_only();
