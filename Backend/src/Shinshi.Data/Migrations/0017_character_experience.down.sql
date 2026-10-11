-- 검토용 파괴적 되돌림: 누적 경험치와 원장 기록을 잃는다. 운영에서는 실행하지 않는다.
DROP TABLE character_experience_events;
ALTER TABLE session_events DROP CONSTRAINT uq_session_event_experience_identity;
ALTER TABLE session_participants DROP CONSTRAINT uq_participant_experience_character;
ALTER TABLE game_sessions DROP COLUMN experience_settings;
ALTER TABLE characters DROP CONSTRAINT ck_character_experience_cursor,
    DROP COLUMN initial_experience, DROP COLUMN total_experience,
    DROP COLUMN last_experience_session_id, DROP COLUMN last_experience_event_seq;
ALTER TABLE session_events DROP CONSTRAINT ck_session_events_event_kind,
    ADD CONSTRAINT ck_session_events_event_kind CHECK (event_kind BETWEEN 1 AND 7) NOT VALID;
