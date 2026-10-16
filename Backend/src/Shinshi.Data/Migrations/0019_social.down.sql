-- 되돌리면 친구, 친구 요청, 길드와 길드원, 가입 신청과 초대, 미전달 귓속말과 알림을 모두 잃는다. 복구할 수 없으므로 운영 DB에서 실행하지 않는다.
-- 계정 임대의 character_id 열을 떼면 접속 표시 정보만 사라지고 임대 자체(접속 판정, 이관)는 그대로 동작한다
DROP TABLE social_deliveries;
DROP TABLE guild_join_requests;
DROP TABLE guild_members;
DROP TABLE guilds;
DROP TABLE friend_requests;
DROP TABLE friendships;

DROP INDEX ix_account_leases_instance_character;
DROP INDEX ux_account_leases_character;
ALTER TABLE account_leases DROP COLUMN character_id;
