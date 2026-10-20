-- 되돌리면 진행 중인 삭제 예약을 잃는다. 이미 삭제한 캐릭터(deleted_at)는 영향이 없다
ALTER TABLE characters
    DROP COLUMN delete_requested_at;
