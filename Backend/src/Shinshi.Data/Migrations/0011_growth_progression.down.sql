-- 되돌리면 모든 장비 개체, 강화 보정, 수련, 부적 진도와 조합, 초기 지급 기록, 계정 진행, 성장 영수증과 대사 실행 기록을 잃는다.
-- 장비 소멸 이력은 item_history에 남지만 현재 소유 장비는 복구할 수 없으므로 운영 DB에서 실행하지 않는다.
-- 원장(economy_ledger)의 성장 사유 행(12~18, 25)은 추가 전용이라 지우지 않으며 잔액도 되돌리지 않는다
DROP TABLE growth_receipts;
DROP TABLE economy_reconciliation_runs;
DROP TABLE account_unlocks;
DROP TABLE account_progress;
DROP TABLE initial_grants;
DROP TABLE character_training;
DROP TABLE enhancement_pity;
DROP TABLE equipment_instances;
DROP TABLE talisman_loadout;
DROP TABLE talisman_progress;

ALTER TABLE session_participants
    DROP CONSTRAINT ck_session_participants_loadout_snapshot,
    DROP COLUMN loadout_snapshot;

ALTER TABLE characters
    DROP CONSTRAINT ck_characters_loadout_revision,
    DROP COLUMN loadout_revision;
