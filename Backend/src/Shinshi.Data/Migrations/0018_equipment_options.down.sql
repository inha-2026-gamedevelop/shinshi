-- 되돌리면 모든 장비의 옵션 두 줄, 옵션 재설정 누계, 영수증의 뽑기 입력 기록을 잃는다. 옵션은 복구할 수 없으므로 운영 DB에서 실행하지 않는다.
-- 원장(economy_ledger)의 재설정 사유 행(38)과 장비 이력(item_history)의 OptionsRerolled 행은 추가 전용이라 지우지 않으며
-- 차감한 정기, 정련편도 되돌리지 않는다. 열 삭제는 행을 고치지 않아 growth_receipts의 추가 전용 트리거에 걸리지 않는다
ALTER TABLE growth_receipts
    DROP CONSTRAINT ck_growth_receipts_roll_values,
    DROP COLUMN roll_values;

DROP TABLE option_reroll_pity;

ALTER TABLE equipment_instances
    DROP CONSTRAINT ck_equipment_option_none,
    DROP CONSTRAINT ck_equipment_option_value,
    DROP CONSTRAINT ck_equipment_option_kind,
    DROP COLUMN option2_value,
    DROP COLUMN option2_kind,
    DROP COLUMN option1_value,
    DROP COLUMN option1_kind;
