-- 되돌리면 모든 아이디, 비밀번호 해시, 이메일과 캐릭터 이름, 외형을 잃는다. 비밀번호를 복구할 수 없으므로 운영 DB에서 실행하지 않는다.
-- 가입으로 만든 계정은 accounts 행이 남지만 로그인 수단이 없어지므로 운영자가 초대 코드를 새로 발급해야 한다
DROP INDEX ux_characters_name_key;
ALTER TABLE characters
    DROP CONSTRAINT ck_characters_appearance,
    DROP CONSTRAINT ck_characters_name_key,
    DROP CONSTRAINT ck_characters_name_format,
    DROP CONSTRAINT ck_characters_name_pair,
    DROP COLUMN appearance,
    DROP COLUMN name_key,
    DROP COLUMN name;

ALTER TABLE auth_token_families
    DROP CONSTRAINT ck_auth_token_families_origin,
    DROP COLUMN origin;

DROP TABLE account_credentials;
