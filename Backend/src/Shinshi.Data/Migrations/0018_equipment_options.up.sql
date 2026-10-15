-- 장비 옵션 두 줄과 옵션 재설정. 재설정은 기존 성장 작업과 같은 잠금 순서(계정 -> 캐릭터 -> 장비)로 들어와
-- 비용 차감, 옵션 갱신, 누계, 이력, 영수증을 한 트랜잭션에 확정한다

-- 옵션 종류는 0 없음, 1 공격%, 2 생명%, 3 방어%, 4 치명 확률이고 값은 만분율 정수다. (종류 0, 값 0)이 옵션 없는 정상 상태다.
-- 부위별 후보와 등급별 범위는 콘텐츠가 정하므로 DB는 형식만 지킨다.
-- 새 CHECK는 NOT VALID로 건 뒤 VALIDATE로 기존 행을 검사한다(0012 주석 참고)
ALTER TABLE equipment_instances
    ADD COLUMN option1_kind smallint NOT NULL DEFAULT 0,
    ADD COLUMN option1_value integer NOT NULL DEFAULT 0,
    ADD COLUMN option2_kind smallint NOT NULL DEFAULT 0,
    ADD COLUMN option2_value integer NOT NULL DEFAULT 0,
    ADD CONSTRAINT ck_equipment_option_kind
        CHECK ((option1_kind BETWEEN 0 AND 4) AND (option2_kind BETWEEN 0 AND 4)) NOT VALID,
    ADD CONSTRAINT ck_equipment_option_value
        CHECK ((option1_value BETWEEN 0 AND 10000) AND (option2_value BETWEEN 0 AND 10000)) NOT VALID,
    ADD CONSTRAINT ck_equipment_option_none
        CHECK (((option1_kind <> 0) OR (option1_value = 0)) AND ((option2_kind <> 0) OR (option2_value = 0))) NOT VALID;
ALTER TABLE equipment_instances VALIDATE CONSTRAINT ck_equipment_option_kind;
ALTER TABLE equipment_instances VALIDATE CONSTRAINT ck_equipment_option_value;
ALTER TABLE equipment_instances VALIDATE CONSTRAINT ck_equipment_option_none;

-- 옵션 재설정 실패 보정. 상급 줄이 하나도 없던 재설정의 연속 횟수이며 키는 캐릭터, 부위다.
-- 강화 보정처럼 장비 ID나 등급을 넣지 않아 같은 부위의 다른 장비도 누계를 공유한다
CREATE TABLE option_reroll_pity
(
    character_id uuid NOT NULL REFERENCES characters (character_id),
    slot integer NOT NULL CHECK (slot BETWEEN 0 AND 5),
    misses integer NOT NULL DEFAULT 0 CHECK (misses >= 0),
    PRIMARY KEY (character_id, slot)
);

-- 옵션 재설정에서 마을 서버가 뽑은 입력 네 개(줄1 종류, 줄1 값, 줄2 종류, 줄2 값, 각 0~9999).
-- 열 추가와 제약 검사는 행을 고치지 않으므로 추가 전용 트리거(UPDATE, DELETE, TRUNCATE)에 걸리지 않는다.
-- 기존 roll_basis_points(부적 등록, 장비 강화)는 그대로 둔다
ALTER TABLE growth_receipts
    ADD COLUMN roll_values integer[] NULL,
    ADD CONSTRAINT ck_growth_receipts_roll_values CHECK ((roll_values IS NULL)
        OR ((array_ndims(roll_values) = 1) AND (array_length(roll_values, 1) = 4)
            AND (array_position(roll_values, NULL) IS NULL)
            AND (0 <= ALL (roll_values)) AND (9999 >= ALL (roll_values)))) NOT VALID;
ALTER TABLE growth_receipts VALIDATE CONSTRAINT ck_growth_receipts_roll_values;

-- 원장 사유 RerollOptions(38), 작업 종류 growth.equipment.reroll_options, 이력 종류 OptionsRerolled는
-- 값 목록 CHECK가 없어(사유는 양수, 종류는 길이만 검사) 제약을 넓힐 것이 없다

-- 배포 권한(README의 런타임 역할 예시에 추가): option_reroll_pity는 SELECT, INSERT, UPDATE만 준다.
-- equipment_instances, growth_receipts의 새 열은 기존 테이블 권한을 따른다
