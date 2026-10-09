-- 직업 ID 범위를 0..5로 넓힌다. 0..2는 Warrior, Sorcerer, Summoner(불변)이고 3..5는 Swordsman, Mageknight, Archer다.
-- 신규 직업의 캐릭터 생성은 API 설정 플래그(Characters:EnabledClassIds, 기본 0,1,2)로 열 때까지 막혀 있으므로 이 마이그레이션만으로는
-- 어떤 신규 행도 생기지 않는다. 기존 행은 모두 0..2라 새 제약을 바로 검증해도 안전하다(ADD CONSTRAINT가 테이블을 한 번 훑는다).
-- 제약 이름은 0001, 0015의 이름 없는 컬럼 CHECK가 PostgreSQL에서 자동으로 받은 이름이다.
ALTER TABLE characters DROP CONSTRAINT characters_class_id_check;
ALTER TABLE characters
    ADD CONSTRAINT ck_characters_class_id CHECK (class_id BETWEEN 0 AND 5);

ALTER TABLE market_listings DROP CONSTRAINT market_listings_class_id_check;
ALTER TABLE market_listings
    ADD CONSTRAINT ck_market_listings_class_id CHECK (class_id IS NULL OR class_id BETWEEN 0 AND 5);
