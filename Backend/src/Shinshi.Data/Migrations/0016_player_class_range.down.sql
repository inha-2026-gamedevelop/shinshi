-- 직업 ID 범위를 0..2로 되돌린다. 신규 직업(3..5) 캐릭터나 그 직업 부적 등록 행이 있으면 데이터를 지우지 않고 중단한다
-- (복구하려면 해당 캐릭터를 먼저 삭제하거나 이관해야 한다).
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM characters WHERE class_id > 2)
        OR EXISTS (SELECT 1 FROM market_listings WHERE class_id > 2) THEN
        RAISE EXCEPTION 'Cannot roll back 0016: rows with class_id 3..5 exist.';
    END IF;
END
$$;

ALTER TABLE market_listings DROP CONSTRAINT ck_market_listings_class_id;
ALTER TABLE market_listings
    ADD CONSTRAINT market_listings_class_id_check CHECK (class_id IN (0, 1, 2));

ALTER TABLE characters DROP CONSTRAINT ck_characters_class_id;
ALTER TABLE characters
    ADD CONSTRAINT characters_class_id_check CHECK (class_id IN (0, 1, 2));
