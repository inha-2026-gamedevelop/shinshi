-- 캐릭터 삭제 예약 시각. NULL이면 예약이 없다. 최종 삭제는 예약 뒤 유예 시간이 DB 시각으로 지나야 한다
ALTER TABLE characters
    ADD COLUMN delete_requested_at timestamptz NULL;
