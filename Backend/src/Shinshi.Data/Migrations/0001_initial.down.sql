-- 초기 로컬 개발 스키마 전체를 제거한다. 적용 전 데이터 백업이 필요하며 운영 DB에서 실행하지 않는다.
DROP TABLE operations;
DROP TABLE game_sessions;
DROP TABLE account_items;
DROP TABLE account_wallets;
DROP TABLE characters;
DROP TABLE auth_game_sessions;
DROP TABLE auth_refresh_tokens;
DROP TABLE auth_invites;
DROP TABLE account_leases;
DROP TABLE server_instances;
DROP TABLE accounts;
